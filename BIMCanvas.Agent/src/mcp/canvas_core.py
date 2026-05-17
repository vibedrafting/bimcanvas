"""Core-base MCP server - 平台基座的 9 个工具 (主真理源 v1.1 §3.10 / 组3 任务模板 §4.6 + §4.7 + §4.8 / 组5 §5.A.8 修订)。

9 个工具组成:
- 5 个从 canvas.py 复用 (通用 BIM 能力):
  request_background_screenshot / validate_layout / get_zone_boundaries / save_modules / analyze_image
- 2 个从 canvas.py 复用 (Git Worktree + 通知机制平台基座, 组5 §5.A.8 补齐):
  ai_job_create (mcp__canvas__create_job) / ai_job_complete (mcp__canvas__complete_job)
- 2 个新增 (跨 scene 元数据 + 只读 artifact):
  list_project_scenes / load_scene_artifact

设计说明:
- `build_core_server(launch_context, session)` 工厂函数:每次 Agent 启动构造一次
- 7 个旧工具仍用模块级 SERVER_URL (Phase 1 过渡)
- 2 个新工具通过闭包绑定 launch_context + session (服从主真理源 §3.10)
- `mcp_servers` dict 仍以 "canvas" 为 key,工具调用名 `mcp__canvas__*` 与旧版兼容
"""

from __future__ import annotations

import json
from typing import TYPE_CHECKING, Any

from claude_agent_sdk import create_sdk_mcp_server, tool

from .canvas import (
    ai_job_complete,
    ai_job_create,
    analyze_image,
    get_zone_boundaries,
    request_background_screenshot,
    save_modules,
    validate_layout,
)

if TYPE_CHECKING:
    import aiohttp

    from ..runtime.launch_context import PluginLaunchContext


_LIST_SCENES_SCHEMA = {
    "type": "object",
    "properties": {},
    "additionalProperties": False,
}


_LOAD_ARTIFACT_SCHEMA = {
    "type": "object",
    "properties": {
        "sceneId": {
            "type": "string",
            "minLength": 1,
            "description": "目标 scene 的唯一 id (project.json.scenes[].sceneId);允许等于 activeSceneId",
        },
        "artifactKind": {
            "type": "string",
            "enum": ["modules", "zones", "semantic_plan", "reference_analysis", "readme"],
            "description": "要读取的产物类型;Phase 1 不支持子路径,只整文件读",
        },
    },
    "required": ["sceneId", "artifactKind"],
    "additionalProperties": False,
}


def _make_list_project_scenes(launch_context: "PluginLaunchContext | None"):
    """生成 list_project_scenes 工具 (闭包绑定 launch_context)。"""

    @tool(
        "list_project_scenes",
        (
            "列出当前 .bcp 项目内所有 scene 的元数据 (主真理源 v1.1 §3.10)。"
            "返回数组 JSON,每项含 sceneId / scene / plugin{id,versionRange} / status / "
            "createdAt / isActive。Phase 1 只返回 status='active' 的 scene。"
            "未绑定项目时返回 is_error。"
        ),
        _LIST_SCENES_SCHEMA,
    )
    async def list_project_scenes(args: dict[str, Any]) -> dict[str, Any]:
        if launch_context is None or launch_context.scenes is None:
            return {
                "content": [{"type": "text", "text": "未绑定项目,无可列举的 scenes"}],
                "is_error": True,
            }

        active_scene_id = launch_context.active_scene_id
        items: list[dict[str, Any]] = []
        for scene in launch_context.scenes.scenes:
            if scene.status.value != "active":
                continue
            items.append(
                {
                    "sceneId": scene.scene_id,
                    "scene": scene.scene,
                    "plugin": {
                        "id": scene.plugin.id,
                        "versionRange": scene.plugin.version_range,
                    },
                    "status": scene.status.value,
                    "createdAt": scene.created_at,
                    "isActive": scene.scene_id == active_scene_id,
                }
            )

        return {
            "content": [
                {"type": "text", "text": json.dumps(items, ensure_ascii=False, indent=2)}
            ]
        }

    return list_project_scenes


def _make_load_scene_artifact(
    launch_context: "PluginLaunchContext | None",
    session: "aiohttp.ClientSession | None",
):
    """生成 load_scene_artifact 工具 (闭包绑定 launch_context + session)。

    通过 Server REST 调用 `GET {server_url}/api/scheme/scenes/{sceneId}/{artifactKind}`。
    端点完整版由组5 实现 (任务模板 §4.8);组3 阶段不写自动化测试,用户手动联调。
    """

    @tool(
        "load_scene_artifact",
        (
            "读取指定 scene 的 artifact (主真理源 v1.1 §3.10)。"
            "Phase 1 整文件读;支持 modules / zones / semantic_plan / reference_analysis / readme。"
            "sceneId 等于 activeSceneId 时仍允许调用 (plugin 作者无需区分读自己 vs 读他人)。"
        ),
        _LOAD_ARTIFACT_SCHEMA,
    )
    async def load_scene_artifact(args: dict[str, Any]) -> dict[str, Any]:
        if launch_context is None or not launch_context.server_url:
            return {
                "content": [{"type": "text", "text": "Server URL 未配置"}],
                "is_error": True,
            }
        if session is None:
            return {
                "content": [{"type": "text", "text": "HTTP session 未初始化"}],
                "is_error": True,
            }

        scene_id = args["sceneId"]
        artifact_kind = args["artifactKind"]
        url = f"{launch_context.server_url}/api/scheme/scenes/{scene_id}/{artifact_kind}"

        try:
            async with session.get(url) as resp:
                if resp.status == 200:
                    body = await resp.text()
                    return {"content": [{"type": "text", "text": body}]}
                if resp.status == 404:
                    return {
                        "content": [
                            {
                                "type": "text",
                                "text": (
                                    f"未找到 artifact: sceneId={scene_id}, "
                                    f"artifactKind={artifact_kind} (HTTP 404)"
                                ),
                            }
                        ],
                        "is_error": True,
                    }
                body = await resp.text()
                return {
                    "content": [
                        {
                            "type": "text",
                            "text": f"Server 错误 HTTP {resp.status}: {body[:200]}",
                        }
                    ],
                    "is_error": True,
                }
        except Exception as exc:  # noqa: BLE001 - 网络异常归一为工具错误
            return {
                "content": [{"type": "text", "text": f"调用失败: {type(exc).__name__}: {exc}"}],
                "is_error": True,
            }

    return load_scene_artifact


def build_core_server(
    launch_context: "PluginLaunchContext | None" = None,
    session: "aiohttp.ClientSession | None" = None,
):
    """构造 core-base MCP server。

    Args:
        launch_context: PluginLaunchContext 实例 (Projectless 时 scenes/lock 为 None)
        session: long-lived aiohttp.ClientSession,供 load_scene_artifact 使用

    Returns:
        McpServer 实例 (直接放进 ClaudeAgentOptions.mcp_servers["canvas"])
    """
    return create_sdk_mcp_server(
        name="canvas",
        version="1.0.0",
        tools=[
            request_background_screenshot,
            validate_layout,
            get_zone_boundaries,
            save_modules,
            analyze_image,
            ai_job_create,
            ai_job_complete,
            _make_list_project_scenes(launch_context),
            _make_load_scene_artifact(launch_context, session),
        ],
    )


CORE_ALLOWED_TOOLS: tuple[str, ...] = (
    "mcp__canvas__request_background_screenshot",
    "mcp__canvas__validate_layout",
    "mcp__canvas__get_zone_boundaries",
    "mcp__canvas__save_modules",
    "mcp__canvas__analyze_image",
    "mcp__canvas__create_job",
    "mcp__canvas__complete_job",
    "mcp__canvas__list_project_scenes",
    "mcp__canvas__load_scene_artifact",
)
