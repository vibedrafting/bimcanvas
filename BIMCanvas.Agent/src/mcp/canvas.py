"""Canvas MCP Server - BIMCanvas 画布操作工具

按 Calculator MCP 模式重构，直接使用 @tool 装饰器，避免复杂的动态发现机制。
"""

from datetime import datetime
import asyncio
import os
from pathlib import Path
from typing import Any
import base64
import json
import mimetypes
import re
import aiohttp
from claude_agent_sdk import tool

from ..attachments.chat_attachments import (
    AttachmentResolutionError,
    resolve_attachment_local_path,
    resolve_attachment_mime_type,
)
from ..reference_analysis import (
    ReferenceAnalysisClient,
    ReferenceAnalysisError,
    ReferenceSource,
    build_custom_image_analysis_prompt,
    load_chatgpt_backend_config,
)

SERVER_URL = os.getenv("BIMCANVAS_SERVER_URL", "http://localhost:5000").rstrip("/")
SCREENSHOT_LAYER_PRESET = "Agent"
SCREENSHOT_LAYER_DISABLE = ["svg"]
SCREENSHOT_SCALE = 2
SCREENSHOT_AUTO_FIT = True
SCREENSHOT_DIR_NAME = "screenshots"


def _normalize_bounds(bounds: Any) -> tuple[dict[str, Any] | None, str | None]:
    if not isinstance(bounds, dict):
        return None, "viewport.bounds 必须是对象"
    required_keys = {"minX", "minY", "maxX", "maxY"}
    if not required_keys.issubset(bounds.keys()):
        return None, "viewport.bounds 需要 minX/minY/maxX/maxY"
    return bounds, None


def _sanitize_filename(value: str) -> str:
    cleaned = re.sub(r"[^A-Za-z0-9._-]+", "_", value or "")
    cleaned = cleaned.strip("._-")
    return cleaned[:60] if cleaned else "shot"


def _normalize_viewport(viewport: dict[str, Any]) -> tuple[dict[str, Any] | None, str | None]:
    if not isinstance(viewport, dict):
        return None, "viewport 必须是对象"

    # 新格式：id 字段 — 前端依次在 baseline.rooms → computed.roomZones → activeScheme.zones 中查找
    target_id = str(viewport.get("id") or "").strip()
    if target_id:
        normalized: dict[str, Any] = {"id": target_id}
        bounds = viewport.get("bounds")
        if isinstance(bounds, dict):
            normalized_bounds, err = _normalize_bounds(bounds)
            if err:
                return None, err
            normalized["bounds"] = normalized_bounds
        return normalized, None

    # 旧格式：mode + roomId/zoneId/bounds（向后兼容）
    bounds = viewport.get("bounds")
    if isinstance(bounds, dict) and not viewport.get("mode"):
        normalized_bounds, err = _normalize_bounds(bounds)
        if err:
            return None, err
        return {"mode": "bounds", "bounds": normalized_bounds}, None

    mode = str(viewport.get("mode") or "full").strip().lower()
    if mode not in {"full", "room", "zone", "bounds"}:
        return None, "viewport.mode 必须是 full/room/zone/bounds"

    normalized: dict[str, Any] = {"mode": mode}
    if mode == "room":
        room_id = str(viewport.get("roomId") or "").strip()
        if not room_id:
            return None, "viewport.mode=room 时必须提供 roomId"
        normalized["roomId"] = room_id
    elif mode == "zone":
        zone_id = str(viewport.get("zoneId") or "").strip()
        if not zone_id:
            return None, "viewport.mode=zone 时必须提供 zoneId"
        normalized["zoneId"] = zone_id
    elif mode == "bounds":
        bounds = viewport.get("bounds")
        normalized_bounds, err = _normalize_bounds(bounds)
        if err:
            return None, "viewport.mode=bounds 时必须提供 bounds" if bounds is None else err
        normalized["bounds"] = normalized_bounds

    return normalized, None


def _clean_target_id(value: Any) -> str:
    return str(value).strip() if value is not None else ""


def _full_screenshot_viewports() -> list[dict[str, Any]]:
    return [{"mode": "full"}]


def _resolve_screenshot_viewports(args: dict[str, Any]) -> tuple[list[dict[str, Any]] | None, str | None]:
    target_id = _clean_target_id(args.get("targetId"))
    if target_id:
        return [{"id": target_id}], None

    viewport_arg = args.get("viewport")
    if viewport_arg is not None:
        normalized, err = _normalize_viewport(viewport_arg)
        if err:
            return None, err
        return [normalized], None

    target_ids_arg = args.get("targetIds")
    if target_ids_arg is not None:
        if not isinstance(target_ids_arg, list):
            return None, "targetIds 必须是数组"
        viewports = [
            {"id": target}
            for target in (_clean_target_id(value) for value in target_ids_arg)
            if target
        ]
        return viewports or _full_screenshot_viewports(), None

    shots_arg = args.get("shots")
    if shots_arg is not None:
        if not isinstance(shots_arg, list):
            return None, "shots 必须是数组"

        viewports: list[dict[str, Any]] = []
        for idx, shot in enumerate(shots_arg):
            if not isinstance(shot, dict):
                return None, f"shots[{idx}] 必须是对象"

            shot_target_id = _clean_target_id(shot.get("targetId"))
            if shot_target_id:
                viewports.append({"id": shot_target_id})
                continue

            if "viewport" not in shot:
                continue

            normalized, err = _normalize_viewport(shot.get("viewport"))
            if err:
                return None, f"shots[{idx}] {err}"
            viewports.append(normalized)

        return viewports or _full_screenshot_viewports(), None

    return _full_screenshot_viewports(), None


def _build_shot_label(viewport: dict[str, Any], index: int) -> str:
    # 新格式
    if viewport.get("id"):
        return f"id_{viewport['id']}"
    # 旧格式
    mode = viewport.get("mode", "full")
    if mode == "room":
        return f"room_{viewport.get('roomId', index)}"
    if mode == "zone":
        return f"zone_{viewport.get('zoneId', index)}"
    if mode == "bounds":
        return f"bounds_{index}"
    return "full"


def _strip_data_uri_prefix(image_data: str) -> str:
    """去掉 data URI 前缀（如 data:image/png;base64,），返回纯 base64 字符串"""
    if "," in image_data:
        return image_data.split(",", 1)[1]
    return image_data


def _decode_image_data(image_data: str) -> bytes:
    if not image_data:
        raise ValueError("imageData 为空")
    return base64.b64decode(_strip_data_uri_prefix(image_data))


def _save_screenshot(image_data: str, project_dir: Path, filename: str) -> str:
    image_bytes = _decode_image_data(image_data)
    output_dir = project_dir / SCREENSHOT_DIR_NAME
    output_dir.mkdir(parents=True, exist_ok=True)
    output_path = output_dir / filename
    output_path.write_bytes(image_bytes)
    return str(output_path)


@tool(
    "create_job",
    "批量创建隔离工作环境（Git Worktree），为 SubAgent 提供独立的开发空间",
    {
        "$schema": "http://json-schema.org/draft-07/schema#",
        "type": "object",
        "properties": {
            "count": {
                "type": "integer",
                "description": "创建的工作环境个数，用于并行执行多个 SubAgent 任务",
                "minimum": 1,
                "maximum": 10,
                "default": 1
            }
        },
        "required": ["count"],
        "additionalProperties": False
    }
)
async def ai_job_create(args: dict[str, Any]) -> dict[str, Any]:
    """创建独立的 Git Worktree，让 SubAgent 在隔离环境中执行修改。"""
    count = args.get("count", 1)

    # 参数验证
    if not isinstance(count, int) or count < 1 or count > 10:
        return {
            "content": [{"type": "text", "text": "错误: count 必须在 1-10 之间"}],
            "is_error": True
        }

    results = []
    try:
        async with aiohttp.ClientSession() as session:
            for i in range(count):
                async with session.post(
                    f"{SERVER_URL}/api/git/ai-job",
                    json={}  # 空 body，Server 自动生成 name 和 baseBranch
                ) as resp:
                    if resp.status == 200:
                        data = await resp.json()
                        results.append({
                            "name": data.get("name", "?"),
                            "path": data.get("worktreePath", "?"),
                            "branch": data.get("branchName", "?")
                        })
                    else:
                        # 部分失败处理
                        try:
                            error_data = await resp.json()
                            error_msg = error_data.get("message", "未知错误")
                        except:
                            error_msg = await resp.text()
                        results.append({"error": error_msg})

        # 格式化输出
        success_count = len([r for r in results if "error" not in r])

        if success_count == 0:
            # 全部失败
            error_msgs = [r.get("error", "未知错误") for r in results]
            return {
                "content": [{"type": "text", "text": f"创建隔离环境失败:\n" + "\n".join(error_msgs)}],
                "is_error": True
            }

        # 构建成功输出
        output_lines = [f"创建 {success_count}/{count} 个隔离环境:"]
        for r in results:
            if "error" not in r:
                output_lines.append(f"- {r['name']}: {r['path']} (分支: {r['branch']})")
            else:
                output_lines.append(f"- [失败]: {r['error']}")

        output_lines.append("")
        output_lines.append("SubAgent 应在对应目录下执行文件修改。")

        return {"content": [{"type": "text", "text": "\n".join(output_lines)}]}

    except aiohttp.ClientError as e:
        return {
            "content": [{"type": "text", "text": f"无法连接 Server: {str(e)}"}],
            "is_error": True
        }


@tool(
    "complete_job",
    "通知 Web 端 AI Job 已完成",
    {
        "$schema": "http://json-schema.org/draft-07/schema#",
        "type": "object",
        "properties": {
            "names": {
                "type": "array",
                "items": {"type": "string"},
                "description": "已完成的 worktree 名称列表",
                "minItems": 1
            }
        },
        "required": ["names"],
        "additionalProperties": False
    }
)
async def ai_job_complete(args: dict[str, Any]) -> dict[str, Any]:
    """通知 Web 端 AI Job 已完成"""
    names_list = args.get("names", [])

    # 参数验证
    if not names_list:
        return {
            "content": [{"type": "text", "text": "错误: names 参数是必需的"}],
            "is_error": True
        }

    try:
        async with aiohttp.ClientSession() as session:
            # 将 names 数组转为 JSON 字符串作为消息
            message = json.dumps(names_list, ensure_ascii=False)

            # 发送简化的通知（title + message）
            async with session.post(
                f"{SERVER_URL}/api/notification/agent",
                json={
                    "title": "AI Job 已完成",
                    "message": message,
                    "type": "success"
                }
            ) as resp:
                if resp.status != 200:
                    return {
                        "content": [{"type": "text", "text": f"发送通知失败: HTTP {resp.status}"}],
                        "is_error": True
                    }

            return {
                "content": [{"type": "text", "text": f"已通知 Web 端：{', '.join(names_list)} 任务完成"}]
            }

    except aiohttp.ClientError as e:
        return {
            "content": [{"type": "text", "text": f"无法连接 Server: {str(e)}"}],
            "is_error": True
        }


@tool(
    "request_background_screenshot",
    "后台截图。调用时必须传入 projectPath，且 projectPath 不可省略、不可为空、不可为 null。"
    "projectPath 必须是当前 BIMCanvas 项目目录（包含 project.json 的目录），"
    "必须使用系统提示词中的「项目路径」；禁止使用 skill/plugin 目录、源码仓库目录或 BIMCANVAS_HOME。"
    "最小合法调用示例：{\"projectPath\":\"<当前项目路径>\"}。"
    "需要局部截图时，在 projectPath 之外再追加 targetId、targetIds、viewport 或 shots。"
    "工具会调用 Server 截图 API，直接返回截图图片（同时保存到 screenshots 目录备查）。",
    {
        "$schema": "http://json-schema.org/draft-07/schema#",
        "type": "object",
        "properties": {
            "projectPath": {
                "type": "string",
                "description": "BIMCanvas 项目目录绝对路径，必填。必须逐字使用系统提示词中的「项目路径」；如果只有「工作目录」且该目录包含 project.json，才可使用工作目录。禁止省略、传空字符串、传 null，禁止使用 BIMCANVAS_HOME、skill/plugin 目录或源码仓库目录。"
            },
            "targetId": {
                "type": "string",
                "description": "常用单张截图目标 ID（推荐）：如 rz_1/r_1/dz_1。必须与 projectPath 一起传，例如 {\"projectPath\":\"...\",\"targetId\":\"rz_1\"}。传入后会截取对应房间/计算区域/设计分区；优先级高于 viewport/targetIds/shots。"
            },
            "targetIds": {
                "type": "array",
                "description": "常用批量截图目标 ID 列表（推荐）：如 [\"rz_1\", \"rz_2\"]。必须与 projectPath 一起传，例如 {\"projectPath\":\"...\",\"targetIds\":[\"rz_1\",\"rz_2\"]}。仅在未提供 targetId/viewport 时生效。",
                "items": {"type": "string"}
            },
            "viewport": {
                "type": "object",
                "description": "高级单张截图范围。必须与 projectPath 一起传。常用局部截图优先用 targetId；留空对象则全屏。也兼容旧格式 mode+roomId/zoneId。",
                "properties": {
                    "id": {
                        "type": "string",
                        "description": "目标 ID（推荐）：传入任意有效 ID（如 rz_1、r_1、dz_1），前端依次在物理房间、计算区域、设计分区中查找。留空则全屏截图。"
                    },
                    "bounds": {
                        "type": "object",
                        "description": "精确坐标范围（优先级最高，可与 id 同时提供覆盖自动计算）",
                        "properties": {
                            "minX": {"type": "number"},
                            "minY": {"type": "number"},
                            "maxX": {"type": "number"},
                            "maxY": {"type": "number"}
                        },
                        "required": ["minX", "minY", "maxX", "maxY"]
                    },
                    "mode": {
                        "type": "string",
                        "enum": ["full", "room", "zone", "bounds"],
                        "description": "旧格式兼容，推荐改用 id 字段"
                    },
                    "roomId": {"type": "string", "description": "旧格式兼容，配合 mode=room 使用"},
                    "zoneId": {"type": "string", "description": "旧格式兼容，配合 mode=zone 使用"}
                }
            },
            "shots": {
                "type": "array",
                "description": "高级批量截图列表，必须与 projectPath 一起传。每项可包含 targetId 或 viewport；targetId 优先。仅在未提供 targetId/viewport/targetIds 时生效。",
                "items": {
                    "type": "object",
                    "properties": {
                        "targetId": {
                            "type": "string",
                            "description": "当前截图目标 ID（推荐）：如 rz_1/r_1/dz_1。优先级高于本项 viewport。"
                        },
                        "viewport": {
                            "type": "object",
                            "description": "截图范围，同单张截图的 viewport",
                            "properties": {
                                "id": {"type": "string", "description": "目标 ID（推荐）"},
                                "bounds": {
                                    "type": "object",
                                    "properties": {
                                        "minX": {"type": "number"},
                                        "minY": {"type": "number"},
                                        "maxX": {"type": "number"},
                                        "maxY": {"type": "number"}
                                    },
                                    "required": ["minX", "minY", "maxX", "maxY"]
                                },
                                "mode": {"type": "string", "enum": ["full", "room", "zone", "bounds"]},
                                "roomId": {"type": "string"},
                                "zoneId": {"type": "string"}
                            }
                        }
                    },
                    "additionalProperties": False
                }
            }
        },
        "required": ["projectPath"],
        "additionalProperties": False
    }
)
async def request_background_screenshot(args: dict[str, Any]) -> dict[str, Any]:
    """请求后台截图并保存到项目目录"""
    project_path = str(args.get("projectPath") or "").strip()
    if not project_path:
        return {
            "content": [{"type": "text", "text": "错误: projectPath 必须提供"}],
            "is_error": True
        }

    project_dir = Path(project_path).expanduser().resolve()
    if not project_dir.exists():
        return {
            "content": [{"type": "text", "text": f"错误: 项目目录不存在: {project_dir}"}],
            "is_error": True
        }
    if project_dir.is_file():
        return {
            "content": [{"type": "text", "text": "错误: projectPath 必须是解压后的项目目录"}],
            "is_error": True
        }

    resolved_viewports, err = _resolve_screenshot_viewports(args)
    if err:
        return {
            "content": [{"type": "text", "text": f"错误: {err}"}],
            "is_error": True
        }

    viewports = resolved_viewports or _full_screenshot_viewports()

    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    timeout = aiohttp.ClientTimeout(total=90)

    try:
        async with aiohttp.ClientSession(timeout=timeout) as session:
            if len(viewports) == 1:
                payload = {
                    "projectPath": str(project_dir),
                    "layerPreset": SCREENSHOT_LAYER_PRESET,
                    "layerDisable": SCREENSHOT_LAYER_DISABLE,
                    "viewport": viewports[0],
                    "autoFitViewport": SCREENSHOT_AUTO_FIT,
                    "scale": SCREENSHOT_SCALE
                }
                async with session.post(f"{SERVER_URL}/api/screenshot/render", json=payload) as resp:
                    data = await resp.json() if resp.content_type == "application/json" else await resp.text()
                    if resp.status != 200:
                        message = data.get("message") if isinstance(data, dict) else str(data)
                        return {
                            "content": [{"type": "text", "text": f"后台截图失败: HTTP {resp.status} {message}"}],
                            "is_error": True
                        }
                image_data = data.get("imageData") if isinstance(data, dict) else None
                if not image_data:
                    return {
                        "content": [{"type": "text", "text": "后台截图失败: imageData 为空"}],
                        "is_error": True
                    }
                label = _sanitize_filename(_build_shot_label(viewports[0], 1))
                filename = f"bg_{label}_{timestamp}.png"
                saved_path = _save_screenshot(image_data, project_dir, filename)
                return {"content": [
                    {"type": "image", "data": _strip_data_uri_prefix(image_data), "mimeType": "image/png"},
                    {"type": "text", "text": f"截图已完成（已保存至 {saved_path}）。请先仔细查看上方图片再继续后续步骤。如果看不到图片，请用 Read 工具查看 {saved_path} 。"}
                ]}

            items = []
            for idx, viewport in enumerate(viewports, start=1):
                label = _sanitize_filename(_build_shot_label(viewport, idx))
                items.append({
                    "name": label,
                    "layerPreset": SCREENSHOT_LAYER_PRESET,
                    "layerDisable": SCREENSHOT_LAYER_DISABLE,
                    "viewport": viewport
                })
            payload = {
                "projectPath": str(project_dir),
                "scale": SCREENSHOT_SCALE,
                "autoFitViewport": SCREENSHOT_AUTO_FIT,
                "items": items
            }
            async with session.post(f"{SERVER_URL}/api/screenshot/render-batch", json=payload) as resp:
                data = await resp.json() if resp.content_type == "application/json" else await resp.text()
                if resp.status != 200:
                    message = data.get("message") if isinstance(data, dict) else str(data)
                    return {
                        "content": [{"type": "text", "text": f"后台批量截图失败: HTTP {resp.status} {message}"}],
                        "is_error": True
                    }

            items_result = data.get("items") if isinstance(data, dict) else None
            if not isinstance(items_result, list):
                return {
                    "content": [{"type": "text", "text": "后台批量截图失败: 返回 items 无效"}],
                    "is_error": True
                }

            content_blocks: list[dict[str, Any]] = []
            errors: list[str] = []
            for idx, result in enumerate(items_result):
                if result.get("error"):
                    errors.append(f"{items[idx]['name']}: {result.get('error')}")
                    continue
                image_data = result.get("imageData")
                if not image_data:
                    errors.append(f"{items[idx]['name']}: imageData 为空")
                    continue
                filename = f"bg_{items[idx]['name']}_{timestamp}_{idx + 1:02d}.png"
                saved_path = _save_screenshot(image_data, project_dir, filename)
                content_blocks.append({"type": "image", "data": _strip_data_uri_prefix(image_data), "mimeType": "image/png"})
                content_blocks.append({"type": "text", "text": f"[{items[idx]['name']}] 已保存至 {saved_path}"})

            if errors:
                return {
                    "content": [{"type": "text", "text": "后台批量截图部分失败:\n" + "\n".join(errors)}],
                    "is_error": True
                }

            content_blocks.append({"type": "text", "text": "以上是所有截图。请先仔细查看图片再继续后续步骤。如果你无法直接看到图片，请用 Read 工具逐一查看上述路径。"})
            return {"content": content_blocks}

    except aiohttp.ClientError as e:
        return {
            "content": [{"type": "text", "text": f"无法连接 Server: {str(e)}"}],
            "is_error": True
        }
    except Exception as e:
        return {
            "content": [{"type": "text", "text": f"后台截图处理失败: {str(e)}"}],
            "is_error": True
        }


def _format_validation_report(report: dict[str, Any]) -> str:
    """将 SchemeValidationReport JSON 格式化为 AI 友好文本"""
    total = report.get("totalModules", 0)
    error_count = report.get("errorCount", 0)
    warning_count = report.get("warningCount", 0)
    elapsed = report.get("elapsedMs", 0)
    diagnostics = report.get("diagnostics", [])

    # 通过判断：0 个 error 即通过（warning 不阻塞）
    if report.get("isValid", True) and error_count == 0:
        if warning_count > 0:
            header = f"=== 布局验证通过（{warning_count} 个警告）==="
            summary = f"共 {total} 个模块，0 个错误，{warning_count} 个警告 ({elapsed}ms)"
        else:
            return f"=== 布局验证通过 ===\n共 {total} 个模块，0 个错误 ({elapsed}ms)"
        lines = [header, summary, ""]
    else:
        lines = [
            f"=== 布局验证失败 ===",
            f"共 {total} 个模块，{error_count} 个错误，{warning_count} 个警告 ({elapsed}ms)",
            "",
        ]

    # 按错误代码分组
    by_code: dict[str, list[dict[str, Any]]] = {}
    for d in diagnostics:
        code = d.get("code", "UNKNOWN")
        by_code.setdefault(code, []).append(d)

    _reverse_dir = {"north": "south", "south": "north", "east": "west", "west": "east"}
    _dir_cn = {"north": "北", "south": "南", "east": "东", "west": "西"}

    for code, diags in by_code.items():
        errors_in_group = sum(1 for d in diags if d.get("severity") == "error")
        warnings_in_group = sum(1 for d in diags if d.get("severity") == "warning")
        count_parts = []
        if errors_in_group > 0:
            count_parts.append(f"{errors_in_group} 个错误")
        if warnings_in_group > 0:
            count_parts.append(f"{warnings_in_group} 个警告")
        count_label = "，".join(count_parts) if count_parts else f"{len(diags)} 个"
        lines.append(f"--- {code} ({count_label}) ---")

        for d in diags:
            severity = d.get("severity", "error")
            prefix = "⚠" if severity == "warning" else "✗"
            module_id = d.get("moduleId", "?")
            module_name = d.get("moduleName")
            name_part = f" ({module_name})" if module_name else ""
            conflict_id = d.get("conflictId")
            conflict_type = d.get("conflictType")
            if conflict_id and conflict_type:
                if conflict_type == "module":
                    base_line = f"  {prefix} {module_id}{name_part} ↔ {conflict_type}:{conflict_id}"
                else:
                    base_line = f"  {prefix} {module_id}{name_part} ← {conflict_type}:{conflict_id}"
            else:
                base_line = f"  {prefix} {module_id}{name_part}"

            # 追加诊断消息（E006 等结构错误的具体原因）
            msg = d.get("message")
            if msg:
                base_line += f"\n    → {msg}"

            # 追加穿透修正指引
            penetration = d.get("penetrationDepthMm")
            direction = d.get("penetrationDirection")
            area = d.get("overlapAreaMm2")
            if penetration is not None and direction is not None and penetration > 0:
                fix_dir = _reverse_dir.get(direction, direction)
                fix_cn = _dir_cn.get(fix_dir, fix_dir)
                action = "建议" if severity == "warning" else "修正"
                hint = f" | {action}：向{fix_cn}移动 {penetration}mm"
                if area is not None:
                    hint += f"（重叠 {area}mm²）"
                base_line += hint

            lines.append(base_line)
        lines.append("")

    return "\n".join(lines)


def _format_normalization_report(report: dict[str, Any]) -> str:
    """将 ModuleNormalizationReport JSON 格式化为 AI 友好文本"""
    total = report.get("totalModules", 0)
    normalized_count = report.get("normalizedCount", 0)
    error_count = report.get("errorCount", 0)
    warning_count = report.get("warningCount", 0)
    elapsed = report.get("elapsedMs", 0)
    diagnostics = report.get("diagnostics", [])

    if report.get("isValid", True) and error_count == 0:
        if warning_count > 0:
            lines = [
                f"=== 模块数据规范化完成（{warning_count} 个警告）===",
                f"共 {total} 个模块，规范化 {normalized_count} 个，0 个错误，{warning_count} 个警告 ({elapsed}ms)",
                "",
            ]
        else:
            return f"=== 模块数据规范化完成 ===\n共 {total} 个模块，规范化 {normalized_count} 个，0 个错误 ({elapsed}ms)"
    else:
        lines = [
            "=== 模块数据规范化失败 ===",
            f"共 {total} 个模块，规范化 {normalized_count} 个，{error_count} 个错误，{warning_count} 个警告 ({elapsed}ms)",
            "",
        ]

    by_code: dict[str, list[dict[str, Any]]] = {}
    for d in diagnostics:
        code = d.get("code", "UNKNOWN")
        by_code.setdefault(code, []).append(d)

    for code, diags in by_code.items():
        errors_in_group = sum(1 for d in diags if d.get("severity") == "error")
        warnings_in_group = sum(1 for d in diags if d.get("severity") == "warning")
        count_parts = []
        if errors_in_group > 0:
            count_parts.append(f"{errors_in_group} 个错误")
        if warnings_in_group > 0:
            count_parts.append(f"{warnings_in_group} 个警告")
        count_label = "，".join(count_parts) if count_parts else f"{len(diags)} 个"
        lines.append(f"--- {code} ({count_label}) ---")

        for d in diags:
            severity = d.get("severity", "error")
            prefix = "⚠" if severity == "warning" else "✗"
            module_id = d.get("moduleId", "?")
            module_name = d.get("moduleName")
            name_part = f" ({module_name})" if module_name else ""
            line = f"  {prefix} {module_id}{name_part}"
            msg = d.get("message")
            if msg:
                line += f"\n    → {msg}"
            lines.append(line)
        lines.append("")

    return "\n".join(lines)


@tool(
    "validate_layout",
    "验证当前方案的布局合法性（布局编译器）。调用时会先将目标 modules.json 中有效的 facing.semantic 收敛为 facing.value 并清空 semantic；规范化无错误后再检查三类错误：(1)超出设计区域 (2)与墙体/柱子/禁区重叠 (3)模块间重叠。可选 zoneIds 参数仅验证指定分区内的模块。可选 variantId 参数仅供 module-relocation-agent 用于验证 modules-{variantId}.json 变体文件，必须与非空 zoneIds 同时使用。",
    {
        "$schema": "http://json-schema.org/draft-07/schema#",
        "type": "object",
        "properties": {
            "zoneIds": {
                "type": "array",
                "items": {"type": "string"},
                "description": "可选。仅验证这些 Zone 内的模块（如 [\"rz_1\", \"dz_2\"]）。不传则验证全部模块。"
            },
            "variantId": {
                "type": "string",
                "description": "可选。验证非 canonical 变体文件，如 'alt-1' → 读取每个目标 Zone 下的 modules-alt-1.json。仅 module-relocation-agent 使用；layout-agent / generate-placement / 其他工作流必须留空。非空时必须与非空 zoneIds 同时提供。"
            }
        },
        "additionalProperties": False
    }
)
async def validate_layout(args: dict[str, Any]) -> dict[str, Any]:
    """验证当前方案的布局合法性（布局编译器）"""
    zone_ids = args.get("zoneIds")
    variant_id = args.get("variantId")
    if variant_id and not zone_ids:
        return {
            "content": [{
                "type": "text",
                "text": "validate_layout 错误: variantId 非空时必须显式指定 zoneIds（不允许全分区扫描变体）"
            }],
            "is_error": True
        }
    # 注意：必须传空 dict 而非 None。aiohttp 在 json=None 时不发 body 也不带 Content-Type，
    # ASP.NET Core [FromBody] 会返回 415；Server 端 ValidateLayoutRequest 已支持空 body 全局验证。
    body: dict[str, Any] = {}
    if zone_ids:
        body["zoneIds"] = zone_ids
    if variant_id:
        body["variantId"] = variant_id

    try:
        async with aiohttp.ClientSession() as session:
            async with session.post(f"{SERVER_URL}/api/modules/normalize", json=body) as resp:
                if resp.status == 400:
                    try:
                        error_data = await resp.json()
                        error_msg = error_data.get("message", "规范化请求无效")
                    except Exception:
                        error_msg = await resp.text()
                    return {
                        "content": [{"type": "text", "text": f"规范化请求失败: {error_msg}"}],
                        "is_error": True
                    }
                if resp.status != 200:
                    try:
                        error_data = await resp.json()
                        error_msg = error_data.get("message", f"HTTP {resp.status}")
                    except Exception:
                        error_msg = await resp.text()
                    return {
                        "content": [{"type": "text", "text": f"规范化请求失败: {error_msg}"}],
                        "is_error": True
                    }

                normalize_report = await resp.json()
                if normalize_report.get("errorCount", 0) > 0:
                    text = _format_normalization_report(normalize_report)
                    return {"content": [{"type": "text", "text": text}], "is_error": True}

            async with session.post(f"{SERVER_URL}/api/validation/layout", json=body) as resp:
                if resp.status == 400:
                    try:
                        error_data = await resp.json()
                        error_msg = error_data.get("message", "验证请求无效")
                    except Exception:
                        error_msg = await resp.text()
                    return {
                        "content": [{"type": "text", "text": f"验证请求失败: {error_msg}"}],
                        "is_error": True
                    }
                if resp.status != 200:
                    try:
                        error_data = await resp.json()
                        error_msg = error_data.get("message", f"HTTP {resp.status}")
                    except Exception:
                        error_msg = await resp.text()
                    return {
                        "content": [{"type": "text", "text": f"验证请求失败: {error_msg}"}],
                        "is_error": True
                    }

                report = await resp.json()
                text = _format_validation_report(report)
                return {"content": [{"type": "text", "text": text}]}

    except aiohttp.ClientError as e:
        return {
            "content": [{"type": "text", "text": f"无法连接 Server: {str(e)}"}],
            "is_error": True
        }



def _segment_direction_label(dx: float, dy: float) -> str:
    """根据方向向量返回方位标签：东/南/西/北/斜边"""
    if abs(dx) < 1e-3 and abs(dy) < 1e-3:
        return "斜边"
    if abs(dx) < 1e-3:
        # 垂直段：dy>0 → 东墙（从南到北，墙面朝东），dy<0 → 西墙
        return "东墙" if dy > 0 else "西墙"
    if abs(dy) < 1e-3:
        # 水平段：dx>0 → 北墙（从西到东，墙面朝北/外），dx<0 → 南墙
        # 注意：CAD 坐标系 Y 向上，边界逆时针时上边从右到左(dx<0)是北墙
        return "南墙" if dx > 0 else "北墙"
    return "斜边"


def _segment_length(start: list, end: list) -> int:
    """计算段长度（毫米，取整）"""
    dx = end[0] - start[0]
    dy = end[1] - start[1]
    return round((dx * dx + dy * dy) ** 0.5)


def _format_zone_boundaries(data: list[dict[str, Any]]) -> str:
    """将 ZoneBoundaryData 列表格式化为按墙面分组的 AI 友好文本。

    输出格式：每面墙一个摘要行（方位 | 总长 | 实墙描述），下面列出各段。
    方向变化处自动断开为新墙面，同方位多面墙加序号区分。
    """
    if not data:
        return "没有找到 zone 边界数据"

    all_zone_lines: list[str] = []
    for zone_data in data:
        zone_id = zone_data.get("zoneId", "?")
        segments = zone_data.get("segments", [])
        if not segments:
            all_zone_lines.append(f"=== {zone_id} 边界语义 (0 面墙) ===")
            all_zone_lines.append("")
            continue

        # 1. 为每段计算方向向量和方位标签
        seg_infos = []
        for seg in segments:
            start = seg.get("start", [0, 0])
            end = seg.get("end", [0, 0])
            dx = end[0] - start[0]
            dy = end[1] - start[1]
            label = _segment_direction_label(dx, dy)
            length = _segment_length(start, end)
            seg_infos.append({
                "seg": seg,
                "label": label,
                "length": length,
                "start": start,
                "end": end,
            })

        # 2. 方向变化处断开为墙面组
        walls: list[list[dict]] = []
        current_wall: list[dict] = [seg_infos[0]]
        for i in range(1, len(seg_infos)):
            if seg_infos[i]["label"] != seg_infos[i - 1]["label"]:
                walls.append(current_wall)
                current_wall = [seg_infos[i]]
            else:
                current_wall.append(seg_infos[i])
        walls.append(current_wall)

        # 3. 同方位计数，决定是否加序号
        label_counts: dict[str, int] = {}
        for wall in walls:
            lbl = wall[0]["label"]
            label_counts[lbl] = label_counts.get(lbl, 0) + 1

        # 4. 格式化输出
        all_zone_lines.append(f"=== {zone_id} 边界语义 ({len(walls)} 面墙) ===")
        all_zone_lines.append("")

        label_index: dict[str, int] = {}
        for wall in walls:
            lbl = wall[0]["label"]
            total_length = sum(s["length"] for s in wall)
            wall_length = sum(s["length"] for s in wall if s["seg"].get("type") == "wall")

            # 方位名称（同方位多面墙时加下标序号）
            if label_counts[lbl] > 1:
                idx = label_index.get(lbl, 0) + 1
                label_index[lbl] = idx
                wall_name = f"{lbl}{chr(0x2080 + idx)}"  # 下标数字 ₁₂₃...
            else:
                wall_name = lbl

            # 实墙描述
            all_wall = all(s["seg"].get("type") == "wall" for s in wall)
            all_passage = all(s["seg"].get("type") == "passage" for s in wall)
            if all_passage:
                # 找 adjacent 信息
                adj = wall[0]["seg"].get("adjacent", "")
                summary = f"通道（→{adj}）" if adj else "通道"
            elif all_wall:
                summary = "完整实墙"
            else:
                summary = f"实墙 {wall_length}mm"

            all_zone_lines.append(f"{wall_name} | 总长 {total_length}mm | {summary}")

            # 段明细
            for s in wall:
                seg = s["seg"]
                seg_type = seg.get("type", "?")
                seg_id = seg.get("id")
                start = s["start"]
                end = s["end"]
                length = s["length"]
                if seg_type == "wall":
                    all_zone_lines.append(f"  wall {length}mm [{start[0]},{start[1]}]→[{end[0]},{end[1]}]")
                else:
                    id_part = f"({seg_id})" if seg_id else ""
                    adj = seg.get("adjacent")
                    adj_part = f"→{adj}" if adj and seg_type == "passage" else ""
                    all_zone_lines.append(
                        f"  {seg_type}{id_part}{adj_part} {length}mm [{start[0]},{start[1]}]→[{end[0]},{end[1]}]"
                    )

            all_zone_lines.append("")

    return "\n".join(all_zone_lines)


@tool(
    "get_zone_boundaries",
    "获取 Zone 边界语义数据：将 zone 的多边形边界拆分为 wall/passage/door/window 段，"
    "帮助理解每条边的物理含义。子分区场景下区分实墙和通道。",
    {
        "$schema": "http://json-schema.org/draft-07/schema#",
        "type": "object",
        "properties": {
            "zoneIds": {
                "type": "array",
                "items": {"type": "string"},
                "description": "可选。指定要查询的 Zone ID 列表（如 [\"dz_1\", \"dz_2\"]）。"
                               "不传则返回所有叶子 zone 的边界段数据。"
            }
        },
        "additionalProperties": False
    }
)
async def get_zone_boundaries(args: dict[str, Any]) -> dict[str, Any]:
    """获取 Zone 边界段语义数据"""
    zone_ids = args.get("zoneIds")
    body = {}
    if zone_ids:
        body["zoneIds"] = zone_ids
    body = body or None

    try:
        async with aiohttp.ClientSession() as session:
            async with session.post(
                f"{SERVER_URL}/api/validation/zone-boundaries", json=body
            ) as resp:
                if resp.status == 400:
                    return {
                        "content": [{"type": "text", "text": "错误: 没有加载的项目"}],
                        "is_error": True
                    }
                if resp.status != 200:
                    try:
                        error_data = await resp.json()
                        error_msg = error_data.get("message", f"HTTP {resp.status}")
                    except Exception:
                        error_msg = await resp.text()
                    return {
                        "content": [{"type": "text", "text": f"获取边界数据失败: {error_msg}"}],
                        "is_error": True
                    }

                data = await resp.json()
                text = _format_zone_boundaries(data)
                return {"content": [{"type": "text", "text": text}]}

    except aiohttp.ClientError as e:
        return {
            "content": [{"type": "text", "text": f"无法连接 Server: {str(e)}"}],
            "is_error": True
        }


@tool(
    "analyze_image",
    "通用图像分析工具(generic)。调用方负责提供 task 文本(描述本次识图目标);"
    "domain plugin 通常通过 Read 读自己的 prompt 文件后传入。"
    "适用场景:Read 看图失败后兜底,或 domain Skill 需要按特定 prompt 分析图像。"
    "不要在 chat / query / edit / 普通看图 / 风格参考 / free mode planning 调用。",
    {
        "$schema": "http://json-schema.org/draft-07/schema#",
        "type": "object",
        "properties": {
            "projectPath": {
                "type": "string",
                "description": "项目绝对路径（attachment manifest 所在目录）",
            },
            "attachmentId": {
                "type": "string",
                "description": "Reference image attachmentId; provide exactly one of attachmentId/path/base64",
            },
            "path": {
                "type": "string",
                "description": "Local image path; provide exactly one of attachmentId/path/base64",
            },
            "base64": {
                "type": "string",
                "description": "Image base64 or data URL; provide exactly one of attachmentId/path/base64",
            },
            "task": {
                "type": "string",
                "description": "识图任务描述。可以是简短的目标(如'统计带文字注释的家具'),"
                               "也可以是 domain plugin 通过 Read 读到的完整 prompt 文本(如 indoor-layout 的 reference_analysis_prompt_v1.md)。"
                               "安全外壳由后端固定,调用方不需要写防护语句。",
            },
        },
        "required": ["projectPath", "task"],
        "additionalProperties": False,
    },
)
async def analyze_image(args: dict[str, Any]) -> dict[str, Any]:
    project_path = str(args.get("projectPath") or "").strip()
    attachment_id = str(args.get("attachmentId") or "").strip()
    image_path = str(args.get("path") or "").strip()
    image_base64 = str(args.get("base64") or "").strip()
    task = str(args.get("task") or "").strip()

    if not project_path:
        return {
            "content": [{"type": "text", "text": "error: projectPath is required"}],
            "is_error": True,
        }

    if not task:
        return {
            "content": [{"type": "text", "text": "error: task is required"}],
            "is_error": True,
        }

    source_count = sum(1 for value in (attachment_id, image_path, image_base64) if value)
    if source_count != 1:
        return {
            "content": [{"type": "text", "text": "error: provide exactly one of attachmentId/path/base64"}],
            "is_error": True,
        }

    try:
        if attachment_id:
            local_path = resolve_attachment_local_path(project_path, attachment_id)
            mime_type = resolve_attachment_mime_type(project_path, attachment_id)
            reference = ReferenceSource(
                mode="path",
                value=str(local_path),
                mime=mime_type,
            )
            source_kind = "attachmentId"
            source_id = attachment_id
        elif image_path:
            local_path = Path(image_path).expanduser()
            if not local_path.is_file():
                return {
                    "content": [{"type": "text", "text": f"path_missing: {image_path}"}],
                    "is_error": True,
                }
            mime_type = mimetypes.guess_type(local_path.name)[0] or "image/png"
            if not mime_type.startswith("image/"):
                return {
                    "content": [{"type": "text", "text": f"path_invalid: not an image ({image_path})"}],
                    "is_error": True,
                }
            reference = ReferenceSource(
                mode="path",
                value=str(local_path),
                mime=mime_type,
            )
            source_kind = "path"
            source_id = str(local_path)
        else:
            reference = ReferenceSource(
                mode="base64",
                value=image_base64,
                mime="image/png",
            )
            source_kind = "base64"
            source_id = "inline"
    except AttachmentResolutionError as exc:
        return {
            "content": [{"type": "text", "text": exc.message}],
            "is_error": True,
        }

    try:
        config = load_chatgpt_backend_config()
    except ReferenceAnalysisError as exc:
        return {
            "content": [{"type": "text", "text": f"image_analysis_config_missing: {exc.message}"}],
            "is_error": True,
        }

    try:
        prompt_text = build_custom_image_analysis_prompt(task)
    except ValueError as exc:
        return {
            "content": [{"type": "text", "text": f"error: {exc}"}],
            "is_error": True,
        }

    client = ReferenceAnalysisClient(config)
    try:
        result = await asyncio.to_thread(
            client.analyze,
            reference,
            prompt_text,
        )
    except ReferenceAnalysisError as exc:
        return {
            "content": [{"type": "text", "text": exc.message}],
            "is_error": True,
        }
    except Exception as exc:  # pragma: no cover
        return {
            "content": [{"type": "text", "text": f"image_analysis_unexpected: {exc}"}],
            "is_error": True,
        }

    raw_text = result.raw_text or ""
    if not raw_text:
        return {
            "content": [{"type": "text", "text": "image_analysis_empty: model returned no text (response_id=" + result.response_id + ")"}],
            "is_error": True,
        }

    return {
        "content": [{"type": "text", "text": raw_text}],
        "structuredContent": {
            "responseId": result.response_id,
            "model": result.model,
            "sourceKind": source_kind,
            "sourceId": source_id,
            "task": task,
            "rawText": raw_text,
        },
    }



@tool(
    "save_modules",
    "保存叶子分区的 modules.json（wrapper 形态由 Server 派生）。"
    "替代直接 Write modules.json 文件——Phase 0b 起裸数组已淘汰。"
    "**不要传 schemeMetadata 字段**：即使传也会被 Server 忽略，schemeMetadata 由 Server 从 semantic_plan / variantId 派生。",
    {
        "$schema": "http://json-schema.org/draft-07/schema#",
        "type": "object",
        "properties": {
            "designZoneId": {
                "type": "string",
                "description": "设计区 ID，如 'rz_3'。用于 schemeMetadata.summary 从对应 semantic_plan 派生。"
            },
            "leafZoneId": {
                "type": "string",
                "description": "叶子分区 ID。顶层叶子时与 designZoneId 相同；嵌套叶子时如 'dz_1'。"
            },
            "variantId": {
                "type": "string",
                "description": "可选。非空时写变体路径 schemes/{designZoneId}/variants/{variantId}/{leafZoneId}/modules.json；为空时写 canonical。"
            },
            "modules": {
                "type": "array",
                "description": "完整模块数组（含 id / moduleId / moduleName / bounds / facing / items 等字段）。"
            }
        },
        "required": ["designZoneId", "leafZoneId", "modules"],
        "additionalProperties": False
    }
)
async def save_modules(args: dict[str, Any]) -> dict[str, Any]:
    """保存叶子分区 modules.json（wrapper 形态由 Server 派生）"""
    design_zone_id = args["designZoneId"]
    leaf_zone_id = args["leafZoneId"]
    modules = args["modules"]
    variant_id = args.get("variantId")

    body: dict[str, Any] = {
        "designZoneId": design_zone_id,
        "leafZoneId": leaf_zone_id,
        "modules": modules
    }
    if variant_id:
        body["variantId"] = variant_id

    try:
        async with aiohttp.ClientSession() as session:
            async with session.post(
                f"{SERVER_URL}/api/scheme/modules",
                json=body
            ) as resp:
                if resp.status == 200:
                    data = await resp.json()
                    count = data.get("modulesCount", len(modules))
                    variant_suffix = f"（variant={variant_id}）" if variant_id else ""
                    return {
                        "content": [{
                            "type": "text",
                            "text": f"已保存 {count} 个模块到 {design_zone_id}/{leaf_zone_id}{variant_suffix}。"
                        }]
                    }
                else:
                    error_text = await resp.text()
                    return {
                        "content": [{"type": "text", "text": f"保存失败: HTTP {resp.status} {error_text}"}],
                        "is_error": True
                    }
    except aiohttp.ClientError as e:
        return {
            "content": [{"type": "text", "text": f"无法连接 Server: {str(e)}"}],
            "is_error": True
        }

