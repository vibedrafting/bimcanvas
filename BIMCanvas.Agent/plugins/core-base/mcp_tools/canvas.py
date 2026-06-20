"""core-base plugin MCP 工具入口。

5 个工具通过 register(builder) 范式注册:
- 1 个通用视觉能力:canvas_vision（截图 / 识图 / 截图+识图 三模式自动判断；
  识图后端走 aoment 图像识别 API，主 agent 无 vision 时一次拿到文字结论）
- 2 个 Git Worktree + 通知:create_job / complete_job
- 1 个只读 artifact:load_artifact(按物理 zone 读 schemes/;裸设计区经拓扑解析 adopted 指针)
- 1 个校验 dispatch:validate_layout(包A 迁回平台:本身是通用"触发校验"派发,
  domain 校验逻辑在当前 active plugin 的 validators 脚本里,经 Server 端点委派执行)

合并自旧 BIMCanvas.Agent/src/mcp/{canvas.py, canvas_core.py}。
改造要点:
- 全部走 ctx.session / ctx.server_url (跟 domain plugin 完全对称)
- modules.json 不再有专用写入工具——AI 通过 Write/Edit 直写
- 变体目录由 AI 用 Write 直接建、Bash mv 转正/翻指针;平台不再提供 register_variant /
  list_variants(列方案 = Glob schemes/{zoneId}/*/,生效 = 读父 DESIGN.md 的 adopted)。
- 退役(项目去插件态 + 指针模型):register_variant / list_variants / list_project_scenes
  已删除;load_scene_artifact 改名 load_artifact 并去掉 sceneId 入参。
"""

from __future__ import annotations

import sys
from pathlib import Path

# v3.4 D3:防御性 sys.path,使绝对 import 在任意入口都成立
# mcp_tools/canvas.py → mcp_tools → core-base → plugins → BIMCanvas.Agent
_AGENT_ROOT = Path(__file__).resolve().parents[3]
if str(_AGENT_ROOT) not in sys.path:
    sys.path.insert(0, str(_AGENT_ROOT))

import asyncio
import base64
import json
import mimetypes
import re
import time
import traceback
from datetime import datetime
from typing import Any

import aiohttp
from claude_agent_sdk import ToolAnnotations

from bimcanvas_plugin_sdk import McpServerBuilder

# v3.4 D2:平台共享基础设施留在 src/,改绝对 import
from src.attachments.chat_attachments import (
    AttachmentResolutionError,
    resolve_attachment_local_path,
)
# canvas_vision 识图侧:多 provider 后端配置（apiyi / aoment，不复用 reference_analysis）
from src.image_recognition import (
    RecognitionConfigError,
    load_recognition_config,
)

# ============================================================
# 模块级配置常量 (纯静态,与 ctx 无关)
# ============================================================

SCREENSHOT_LAYER_PRESET = "Agent"
SCREENSHOT_LAYER_DISABLE = ["svg"]
SCREENSHOT_SCALE = 2
SCREENSHOT_AUTO_FIT = True
SCREENSHOT_DIR_NAME = "screenshots"


# ============================================================
# 辅助函数 (从原 canvas.py 搬运,纯函数,无 ctx 依赖)
# ============================================================

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
    if viewport.get("id"):
        return f"id_{viewport['id']}"
    mode = viewport.get("mode", "full")
    if mode == "room":
        return f"room_{viewport.get('roomId', index)}"
    if mode == "zone":
        return f"zone_{viewport.get('zoneId', index)}"
    if mode == "bounds":
        return f"bounds_{index}"
    return "full"


def _strip_data_uri_prefix(image_data: str) -> str:
    """去掉 data URI 前缀(如 data:image/png;base64,),返回纯 base64 字符串"""
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


# ============================================================
# 模块级:每个工具的 schema + description 常量
# (拆出来便于测试直读,无须 mock builder)
# ============================================================

_CREATE_JOB_DESC = "批量创建隔离工作环境(Git Worktree),为 SubAgent 提供独立的开发空间"
_CREATE_JOB_SCHEMA = {
    "$schema": "http://json-schema.org/draft-07/schema#",
    "type": "object",
    "properties": {
        "count": {
            "type": "integer",
            "description": "创建的工作环境个数,用于并行执行多个 SubAgent 任务",
            "minimum": 1,
            "maximum": 10,
            "default": 1,
        }
    },
    "required": ["count"],
    "additionalProperties": False,
}

_COMPLETE_JOB_DESC = "通知 Web 端 AI Job 已完成"
_COMPLETE_JOB_SCHEMA = {
    "$schema": "http://json-schema.org/draft-07/schema#",
    "type": "object",
    "properties": {
        "names": {
            "type": "array",
            "items": {"type": "string"},
            "description": "已完成的 worktree 名称列表",
            "minItems": 1,
        }
    },
    "required": ["names"],
    "additionalProperties": False,
}

_CANVAS_VISION_DESC = (
    "通用视觉工具:截图 / 识图 / 截图+识图 三模式自动判断(无显式 mode 参数)。"
    "必须传入 projectPath(当前 BIMCanvas 项目目录,含 project.json;逐字用系统提示词中的「项目路径」,"
    "禁止 skill/plugin 目录、源码仓库目录或 BIMCANVAS_HOME)。"
    "【模式判定】"
    "① 不传 prompt → 只截图:用 targetId/targetIds/viewport/shots/variantId 截图,直接返回截图图片(同时保存到 screenshots 目录备查)。"
    "② 传 prompt + 传图源(attachmentId/path/base64 三选一)→ 只识图:把该图喂 aoment 图像识别,返回纯文字结论。"
    "③ 传 prompt + 不传图源 + 传截图范围(targetId/viewport 等)→ 截图+识图:先截单图存 bg_*.png,再喂 aoment,返回纯文字结论(若截图范围解析到设计区方案,自动附该方案 id→家具名 图例,无需手动写入)。"
    "【约束】图源与截图范围同时给会报错(二选一);识图(②③)只返回文字、不返回图片;截图+识图(③)不支持批量(批量截图仅①可用)。"
)
_CANVAS_VISION_SCHEMA = {
    "$schema": "http://json-schema.org/draft-07/schema#",
    "type": "object",
    "properties": {
        "projectPath": {
            "type": "string",
            "description": "BIMCanvas 项目目录绝对路径,必填。必须逐字使用系统提示词中的「项目路径」;如果只有「工作目录」且该目录包含 project.json,才可使用工作目录。禁止省略、传空字符串、传 null,禁止使用 BIMCANVAS_HOME、skill/plugin 目录或源码仓库目录。",
        },
        "prompt": {
            "type": "string",
            "description": "可选。识图要求文本。不传 = 只截图;传入则触发识图(aoment 后端),本文本直接作为识别要求。识图模式只返回文字、不返回图片。",
        },
        "attachmentId": {
            "type": "string",
            "description": "识图图源之一(仅识图模式)。参考图 attachmentId;attachmentId/path/base64 三选一。与截图范围(targetId/viewport 等)互斥。",
        },
        "path": {
            "type": "string",
            "description": "识图图源之一(仅识图模式)。本地图片绝对路径;attachmentId/path/base64 三选一。与截图范围互斥。",
        },
        "base64": {
            "type": "string",
            "description": "识图图源之一(仅识图模式)。图片 base64 或 data URL;attachmentId/path/base64 三选一。与截图范围互斥。",
        },
        "targetId": {
            "type": "string",
            "description": "常用单张截图目标 ID(推荐):如 rz_1/r_1/dz_1。必须与 projectPath 一起传,例如 {\"projectPath\":\"...\",\"targetId\":\"rz_1\"}。传入后会截取对应房间/计算区域/设计分区;优先级高于 viewport/targetIds/shots。",
        },
        "targetIds": {
            "type": "array",
            "description": "常用批量截图目标 ID 列表(推荐):如 [\"rz_1\", \"rz_2\"]。必须与 projectPath 一起传,例如 {\"projectPath\":\"...\",\"targetIds\":[\"rz_1\",\"rz_2\"]}。仅在未提供 targetId/viewport 时生效。",
            "items": {"type": "string"},
        },
        "viewport": {
            "type": "object",
            "description": "高级单张截图范围。必须与 projectPath 一起传。常用局部截图优先用 targetId;留空对象则全屏。也兼容旧格式 mode+roomId/zoneId。",
            "properties": {
                "id": {
                    "type": "string",
                    "description": "目标 ID(推荐):传入任意有效 ID(如 rz_1、r_1、dz_1),前端依次在物理房间、计算区域、设计分区中查找。留空则全屏截图。",
                },
                "bounds": {
                    "type": "object",
                    "description": "精确坐标范围(优先级最高,可与 id 同时提供覆盖自动计算)",
                    "properties": {
                        "minX": {"type": "number"},
                        "minY": {"type": "number"},
                        "maxX": {"type": "number"},
                        "maxY": {"type": "number"},
                    },
                    "required": ["minX", "minY", "maxX", "maxY"],
                },
                "mode": {
                    "type": "string",
                    "enum": ["full", "room", "zone", "bounds"],
                    "description": "旧格式兼容,推荐改用 id 字段",
                },
                "roomId": {"type": "string", "description": "旧格式兼容,配合 mode=room 使用"},
                "zoneId": {"type": "string", "description": "旧格式兼容,配合 mode=zone 使用"},
            },
        },
        "shots": {
            "type": "array",
            "description": "高级批量截图列表,必须与 projectPath 一起传。每项可包含 targetId 或 viewport;targetId 优先。仅在未提供 targetId/viewport/targetIds 时生效。",
            "items": {
                "type": "object",
                "properties": {
                    "targetId": {
                        "type": "string",
                        "description": "当前截图目标 ID(推荐):如 rz_1/r_1/dz_1。优先级高于本项 viewport。",
                    },
                    "viewport": {
                        "type": "object",
                        "description": "截图范围,同单张截图的 viewport",
                        "properties": {
                            "id": {"type": "string", "description": "目标 ID(推荐)"},
                            "bounds": {
                                "type": "object",
                                "properties": {
                                    "minX": {"type": "number"},
                                    "minY": {"type": "number"},
                                    "maxX": {"type": "number"},
                                    "maxY": {"type": "number"},
                                },
                                "required": ["minX", "minY", "maxX", "maxY"],
                            },
                            "mode": {"type": "string", "enum": ["full", "room", "zone", "bounds"]},
                            "roomId": {"type": "string"},
                            "zoneId": {"type": "string"},
                        },
                    },
                },
                "additionalProperties": False,
            },
        },
        "variantId": {
            "type": "string",
            "description": "可选。指针模型下截指定候选/变体方案 slug(如 \"_cand-a\"),仅多候选/变体评审场景用,常规截图留空(留空=截 adopted 当前生效方案)。非空时必须配 viewport.mode=\"zone\" + viewport.zoneId(批量则每个 shots[].viewport.zoneId)指明目标分区——Server 据此解析该候选的 modules,缺 zoneId 会报错。",
        },
    },
    "required": ["projectPath"],
    "additionalProperties": False,
}

# ============================================================
# Step1 重试:对截图/识图的瞬时类失败做指数退避重试
#   - 瞬时(retryable=True):连接失败/超时、HTTP 5xx/429、空结果
#   - 永久(retryable=False):HTTP 4xx/参数错、模型级 success=False —— 重试无意义,立即返回
# ============================================================

_VISION_RETRY_ATTEMPTS = 5
_VISION_RETRY_BASE_DELAY = 1.0  # 退避基数(秒):1s / 2s / 4s / 8s（5 次尝试≈15s 窗口，扛瞬时/并发突发）
_VISION_RETRY_MAX_DELAY = 8.0   # 单次退避上限，避免次数调高时退避失控


async def _retry_request(label: str, attempt_fn: Any) -> tuple[Any, str | None]:
    """对瞬时类失败做指数退避重试,返回 (result, error)。

    attempt_fn 是无参 async,返回三元组 (result, error, retryable):
      - error is None      → 成功,立即返回 (result, None)
      - retryable is False → 永久失败,立即返回 (None, error)
      - retryable is True  → 瞬时失败,退避后重试,耗尽后返回 (None, error)
    """
    last_error: str | None = None
    for i in range(_VISION_RETRY_ATTEMPTS):
        result, error, retryable = await attempt_fn()
        if error is None:
            return result, None
        last_error = error
        if not retryable or i == _VISION_RETRY_ATTEMPTS - 1:
            return None, error
        delay = min(_VISION_RETRY_BASE_DELAY * (2 ** i), _VISION_RETRY_MAX_DELAY)
        print(
            f"[canvas_vision] {label} 第{i + 1}/{_VISION_RETRY_ATTEMPTS}次失败"
            f"(可重试,{delay:.0f}s 后重试): {error}",
            file=sys.stderr,
            flush=True,
        )
        await asyncio.sleep(delay)
    return None, last_error


async def _render_viewport(
    session: Any,
    server_url: str,
    project_dir: Path,
    viewport: dict[str, Any],
    variant_id: str,
    request_timeout: "aiohttp.ClientTimeout",
) -> tuple[str | None, str | None]:
    """渲染单个 viewport,返回 (imageData, error)。截图+识图 与 只截图单图共用。

    瞬时失败(连接/超时/5xx/空图)经 _retry_request 指数退避重试。
    """
    payload: dict[str, Any] = {
        "projectPath": str(project_dir),
        "layerPreset": SCREENSHOT_LAYER_PRESET,
        "layerDisable": SCREENSHOT_LAYER_DISABLE,
        "viewport": viewport,
        "autoFitViewport": SCREENSHOT_AUTO_FIT,
        "scale": SCREENSHOT_SCALE,
    }
    if variant_id:
        payload["variantId"] = variant_id

    async def _attempt() -> tuple[str | None, str | None, bool]:
        try:
            async with session.post(
                f"{server_url}/api/screenshot/render",
                json=payload,
                timeout=request_timeout,
            ) as resp:
                data = await resp.json() if resp.content_type == "application/json" else await resp.text()
                if resp.status != 200:
                    message = data.get("message") if isinstance(data, dict) else str(data)
                    retryable = resp.status >= 500 or resp.status == 429
                    return None, f"后台截图失败: HTTP {resp.status} {message}", retryable
            image_data = data.get("imageData") if isinstance(data, dict) else None
            if not image_data:
                return None, "后台截图失败: imageData 为空", True
            return image_data, None, False
        except (aiohttp.ClientError, asyncio.TimeoutError) as e:
            return None, f"无法连接 Server(截图): {type(e).__name__}: {e}", True

    return await _retry_request(f"render:{_build_shot_label(viewport, 1)}", _attempt)


async def _aoment_recognize(
    session: Any,
    cfg: Any,
    image_path: Path,
    prompt: str,
) -> tuple[str | None, str | None]:
    """multipart 上传单张图到 aoment 识别端点,返回 (resultText, error)。

    瞬时失败(连接/超时/5xx/空结果)经 _retry_request 指数退避重试;
    4xx 与模型级 success=False 视为永久失败,不重试。
    """
    request_timeout = aiohttp.ClientTimeout(total=cfg.timeout_seconds)
    mime = mimetypes.guess_type(image_path.name)[0] or "image/png"
    image_bytes = image_path.read_bytes()  # 读一次,重试复用

    async def _attempt() -> tuple[str | None, str | None, bool]:
        # FormData 单次请求消耗性,每次重试重建
        form = aiohttp.FormData()
        form.add_field("prompt", prompt)
        form.add_field("model", cfg.model)
        form.add_field("images", image_bytes, filename=image_path.name, content_type=mime)
        try:
            async with session.post(
                cfg.endpoint,
                data=form,
                headers={"Authorization": f"Bearer {cfg.api_key}"},
                timeout=request_timeout,
            ) as resp:
                data = await resp.json() if resp.content_type == "application/json" else await resp.text()
                if resp.status != 200:
                    message = data.get("message") if isinstance(data, dict) else str(data)[:300]
                    retryable = resp.status >= 500 or resp.status == 429
                    return None, f"aoment 识图失败: HTTP {resp.status} {message}", retryable
                if not isinstance(data, dict):
                    return None, f"aoment 识图失败: 响应非 JSON ({str(data)[:200]})", True
                if data.get("success") is False:
                    return None, f"aoment 识图失败: {data.get('message') or data.get('resultText') or '未知错误'}", False
                result_text = str(data.get("resultText") or "").strip()
                if not result_text:
                    return None, "aoment 识图失败: resultText 为空", True
                return result_text, None, False
        except (aiohttp.ClientError, asyncio.TimeoutError) as e:
            return None, f"无法连接 aoment: {type(e).__name__}: {e}", True

    return await _retry_request("aoment", _attempt)


def _extract_openai_text(content: Any) -> str:
    """从 OpenAI choices[0].message.content 提取文本（兼容 str 或 content-block 列表）。"""
    if isinstance(content, str):
        return content.strip()
    if isinstance(content, list):
        parts: list[str] = []
        for block in content:
            if isinstance(block, dict) and isinstance(block.get("text"), str):
                parts.append(block["text"])
        return "\n".join(parts).strip()
    return ""


async def _apiyi_recognize(
    session: Any,
    cfg: Any,
    image_path: Path,
    prompt: str,
) -> tuple[str | None, str | None]:
    """OpenAI Chat Completions 格式识图（apiyi 等 OpenAI 兼容服务），返回 (text, error)。

    图片转 base64 data URL 塞 image_url；取 choices[0].message.content。
    瞬时失败(连接/超时/5xx/空结果)经 _retry_request 指数退避重试;4xx 不重试。
    """
    request_timeout = aiohttp.ClientTimeout(total=cfg.timeout_seconds)
    mime = mimetypes.guess_type(image_path.name)[0] or "image/png"
    b64 = base64.b64encode(image_path.read_bytes()).decode("ascii")
    data_url = f"data:{mime};base64,{b64}"
    payload: dict[str, Any] = {
        "model": cfg.model,
        "messages": [
            {
                "role": "user",
                "content": [
                    {"type": "text", "text": prompt},
                    {"type": "image_url", "image_url": {"url": data_url}},
                ],
            }
        ],
    }

    async def _attempt() -> tuple[str | None, str | None, bool]:
        try:
            async with session.post(
                cfg.endpoint,
                json=payload,
                headers={"Authorization": f"Bearer {cfg.api_key}"},
                timeout=request_timeout,
            ) as resp:
                data = await resp.json() if resp.content_type == "application/json" else await resp.text()
                if resp.status != 200:
                    if isinstance(data, dict):
                        err = data.get("error")
                        message = (err.get("message") if isinstance(err, dict) else err) or data.get("message") or str(data)[:300]
                    else:
                        message = str(data)[:300]
                    retryable = resp.status >= 500 or resp.status == 429
                    return None, f"apiyi 识图失败: HTTP {resp.status} {message}", retryable
                if not isinstance(data, dict):
                    return None, f"apiyi 识图失败: 响应非 JSON ({str(data)[:200]})", True
                choices = data.get("choices")
                if not isinstance(choices, list) or not choices:
                    err = data.get("error")
                    return None, f"apiyi 识图失败: {err or '响应无 choices'}", True
                message_obj = choices[0].get("message") if isinstance(choices[0], dict) else None
                content = message_obj.get("content") if isinstance(message_obj, dict) else None
                text = _extract_openai_text(content)
                if not text:
                    return None, "apiyi 识图失败: content 为空", True
                return text, None, False
        except (aiohttp.ClientError, asyncio.TimeoutError) as e:
            return None, f"无法连接 apiyi: {type(e).__name__}: {e}", True

    return await _retry_request("apiyi", _attempt)


# 失败切换:两个 provider 互为备用
_FALLBACK_PROVIDER = {"aoment": "apiyi", "apiyi": "aoment"}


async def _recognize_one(
    session: Any,
    cfg: Any,
    image_path: Path,
    prompt: str,
) -> tuple[str | None, str | None]:
    """单 provider 识图(按 cfg.provider 分发,含 provider 内指数退避重试)。"""
    if cfg.provider == "aoment":
        return await _aoment_recognize(session, cfg, image_path, prompt)
    return await _apiyi_recognize(session, cfg, image_path, prompt)


async def _recognize_image(
    session: Any,
    cfg: Any,
    image_path: Path,
    prompt: str,
) -> tuple[str | None, str | None]:
    """识图:配置 provider 先试(含重试);整体失败则切换到备用 provider 补救一次(含重试)。

    一次切换补救——主 provider 重试耗尽后,自动换另一个 provider 再跑一轮重试;
    备用 provider 未配置(无 apiKey)则放弃切换,返回主 provider 的原始错误。
    """
    result, error = await _recognize_one(session, cfg, image_path, prompt)
    if error is None:
        return result, None

    other = _FALLBACK_PROVIDER.get(cfg.provider)
    if not other:
        return None, error
    try:
        cfg_fallback = load_recognition_config(provider_override=other)
    except RecognitionConfigError as exc:
        print(
            f"[canvas_vision] {cfg.provider} 识图失败,且备用 provider {other} 未配置、无法切换: {exc.message}",
            file=sys.stderr, flush=True,
        )
        return None, error
    print(
        f"[canvas_vision] {cfg.provider} 识图失败,切换到备用 provider {other} 补救一次",
        file=sys.stderr, flush=True,
    )
    result2, error2 = await _recognize_one(session, cfg_fallback, image_path, prompt)
    if error2 is None:
        return result2, None
    return None, f"识图失败:主 {cfg.provider}({error});备用 {other}({error2})"


# ============================================================
# 截图+识图(模式③)专用:id→moduleName 图例自动注入
#   渲染同源(zone/variant)取 modules,把「图中 id = 家具名」图例 prepend 到 caller 的 prompt,
#   让无 vision 的识图后端按名字认家具(实测:有图例才不把窗帘误识成高柜、才能判最优墙)。
#   任何失败一律静默跳过、绝不阻断识图——图例是增强项,不是前置条件。
# ============================================================

def _zone_id_from_viewport(viewport: dict[str, Any]) -> str | None:
    """从单个 viewport 取设计区 zoneId(供 mode③ 图例注入);full/bounds/room 取不到则返回 None。"""
    vid = str(viewport.get("id") or "").strip()
    if vid:
        return vid
    if viewport.get("mode") == "zone":
        return str(viewport.get("zoneId") or "").strip() or None
    return None


def _build_module_legend(files: list[Any]) -> str | None:
    """从 artifacts/modules 的 files[{relativePath, content}] 抽 id=moduleName 拼图例;无模块返回 None。"""
    lines: list[str] = []
    for f in files:
        content = f.get("content") if isinstance(f, dict) else None
        if not content:
            continue
        try:
            obj = json.loads(content)
        except (ValueError, TypeError):
            continue
        for m in (obj.get("modules") or []) if isinstance(obj, dict) else []:
            if not isinstance(m, dict):
                continue
            mid = str(m.get("id") or "").strip()
            if not mid:
                continue
            name = str(m.get("moduleName") or "").strip()
            lines.append(f"- {mid} = {name}")
    if not lines:
        return None
    return (
        "【图中模块清单（id = 家具名；图中标签 #xx 为 id 前缀，按前缀对应认）】\n"
        + "\n".join(lines)
    )


async def _fetch_scheme_modules(
    session: Any,
    server_url: str,
    zone_id: str,
    variant_id: str,
    request_timeout: "aiohttp.ClientTimeout",
) -> list[Any] | None:
    """取渲染同源方案 modules(variant 空=adopted;非空=指定候选)。返回 files 列表;任何失败返回 None。"""
    params: dict[str, str] = {"path": zone_id}
    if variant_id:
        params["variantId"] = variant_id
    try:
        async with session.get(
            f"{server_url}/api/scheme/artifacts/modules",
            params=params,
            timeout=request_timeout,
        ) as resp:
            if resp.status != 200 or resp.content_type != "application/json":
                return None
            data = await resp.json()
    except (aiohttp.ClientError, asyncio.TimeoutError):
        return None
    if not isinstance(data, dict):
        return None
    files = data.get("files")
    return files if isinstance(files, list) else None


_LOAD_ARTIFACT_DESC = (
    "读取 artifact(持久数据按物理 zone 组织在 schemes/ 下)。"
    "artifactKind 是 plugin-agnostic 的字符串(字符集 ^[a-z][a-z0-9_-]*$),"
    "平台 reserved 通用 kind:modules / zones / readme(对应 baseline 派生 / AI Write 直写);"
    "其他 kind 是 plugin domain 产物,走 schemes/ 下同名文件聚合。"
    "可选 path 参数精确读单文件 schemes/{path}/{artifactKind}.json"
    "(如 path='rz_3' 裸设计区经拓扑解析 adopted 指针,或 path='rz_3/cand-c' 显式方案 slug);"
    "留空时走聚合返回(schemes/ 下所有同名文件 + relativePath)。"
)
_LOAD_ARTIFACT_SCHEMA = {
    "type": "object",
    "properties": {
        "artifactKind": {
            "type": "string",
            "pattern": "^[a-z][a-z0-9_-]*$",
            "description": (
                "产物类型,字符集 ^[a-z][a-z0-9_-]*$。Reserved 通用 kind:"
                "modules(schemes/ 下所有叶子分区 modules.json 聚合)、"
                "zones(schemes/zones.json)、"
                "readme(项目根 README.md)。"
                "其他 kind 由 plugin 自定义,Server 按 schemes/ 下同名文件聚合返回。"
            ),
        },
        "path": {
            "type": "string",
            "description": (
                "可选。schemes/ 内相对子路径,如 'rz_3'(裸设计区,经拓扑解析 adopted 指针)"
                "或 'rz_3/cand-c'(显式方案 slug)。"
                "非空时精确读单文件 schemes/{path}/{artifactKind}.json;"
                "空时走聚合(schemes/ 下所有同名 artifactKind 文件)。"
                "字符集 [a-zA-Z0-9_/-]+,禁止 .. / \\\\ / 前导斜杠。"
            ),
        },
    },
    "required": ["artifactKind"],
    "additionalProperties": False,
}


def _format_validation_report(report: dict[str, Any]) -> str:
    """将 SchemeValidationReport JSON 格式化为 AI 友好文本（通用渲染，按 code 分组）。"""
    total = report.get("totalModules", 0)
    error_count = report.get("errorCount", 0)
    warning_count = report.get("warningCount", 0)
    elapsed = report.get("elapsedMs", 0)
    diagnostics = report.get("diagnostics", [])

    if report.get("isValid", True) and error_count == 0:
        if warning_count > 0:
            header = f"=== 布局验证通过({warning_count} 个警告)==="
            summary = f"共 {total} 个模块,0 个错误,{warning_count} 个警告 ({elapsed}ms)"
        else:
            return f"=== 布局验证通过 ===\n共 {total} 个模块,0 个错误 ({elapsed}ms)"
        lines = [header, summary, ""]
    else:
        lines = [
            "=== 布局验证失败 ===",
            f"共 {total} 个模块,{error_count} 个错误,{warning_count} 个警告 ({elapsed}ms)",
            "",
        ]

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
        count_label = ",".join(count_parts) if count_parts else f"{len(diags)} 个"
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

            msg = d.get("message")
            if msg:
                base_line += f"\n    → {msg}"

            penetration = d.get("penetrationDepthMm")
            direction = d.get("penetrationDirection")
            area = d.get("overlapAreaMm2")
            if penetration is not None and direction is not None and penetration > 0:
                fix_dir = _reverse_dir.get(direction, direction)
                fix_cn = _dir_cn.get(fix_dir, fix_dir)
                action = "建议" if severity == "warning" else "修正"
                hint = f" | {action}:向{fix_cn}移动 {penetration}mm"
                if area is not None:
                    hint += f"(重叠 {area}mm²)"
                base_line += hint

            lines.append(base_line)
        lines.append("")

    return "\n".join(lines)


def _format_normalization_report(report: dict[str, Any]) -> str:
    """将 ModuleNormalizationReport JSON 格式化为 AI 友好文本（通用渲染）。"""
    total = report.get("totalModules", 0)
    normalized_count = report.get("normalizedCount", 0)
    error_count = report.get("errorCount", 0)
    warning_count = report.get("warningCount", 0)
    elapsed = report.get("elapsedMs", 0)
    diagnostics = report.get("diagnostics", [])

    if report.get("isValid", True) and error_count == 0:
        if warning_count > 0:
            lines = [
                f"=== 模块数据规范化完成({warning_count} 个警告)===",
                f"共 {total} 个模块,规范化 {normalized_count} 个,0 个错误,{warning_count} 个警告 ({elapsed}ms)",
                "",
            ]
        else:
            return f"=== 模块数据规范化完成 ===\n共 {total} 个模块,规范化 {normalized_count} 个,0 个错误 ({elapsed}ms)"
    else:
        lines = [
            "=== 模块数据规范化失败 ===",
            f"共 {total} 个模块,规范化 {normalized_count} 个,{error_count} 个错误,{warning_count} 个警告 ({elapsed}ms)",
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
        count_label = ",".join(count_parts) if count_parts else f"{len(diags)} 个"
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


_VALIDATE_LAYOUT_DESC = (
    "验证当前激活插件方案数据的布局合法性(布局编译器)。本工具是通用的「触发校验」派发:"
    "委派当前 active plugin 的校验脚本——先做数据规范化,规范化无错误后再检查结构与碰撞"
    "(如越界 / 与墙·柱·禁区重叠 / 元素间重叠等;具体规则由当前插件定义)。"
    "可选 zoneIds 仅验证指定分区;可选 variantId 验证非 canonical 变体,必须与非空 zoneIds 同时提供。"
)
_VALIDATE_LAYOUT_SCHEMA = {
    "$schema": "http://json-schema.org/draft-07/schema#",
    "type": "object",
    "properties": {
        "zoneIds": {
            "type": "array",
            "items": {"type": "string"},
            "description": (
                "可选。仅验证这些分区内的元素(如 [\"rz_1\", \"dz_2\"])。不传则验证全部。"
                "⚠️ 与 variantId 同时使用时,这里必须传**设计区路径**(如 [\"rz_3\"]),"
                "不能传候选方案内部的叶子 id(如 dz_1);否则定位不到候选数据,校验会直接报错(不会静默通过)。"
            ),
        },
        "variantId": {
            "type": "string",
            "description": (
                "可选。验证非 canonical 变体(仅变体探索场景用,常规验证留空)。"
                "取值=候选方案 slug。非空时必须与非空 zoneIds(设计区路径)同时提供。"
            ),
        },
    },
    "additionalProperties": False,
}


# ============================================================
# register(builder) — 平台调用入口,9 个工具的注册地
# ============================================================

def register(builder: McpServerBuilder) -> None:
    """core-base plugin 注册入口。

    所有工具通过 ctx.session / ctx.server_url 与 BIMCanvas Server 通信,
    跟 domain plugin 完全对称。
    """
    ctx = builder.context

    # ---------- create_job ----------
    @builder.tool("create_job", _CREATE_JOB_DESC, _CREATE_JOB_SCHEMA)
    async def ai_job_create(args: dict[str, Any]) -> dict[str, Any]:
        """创建独立的 Git Worktree,让 SubAgent 在隔离环境中执行修改。"""
        count = args.get("count", 1)

        if not isinstance(count, int) or count < 1 or count > 10:
            return {
                "content": [{"type": "text", "text": "错误: count 必须在 1-10 之间"}],
                "is_error": True,
            }

        results = []
        try:
            for _ in range(count):
                async with ctx.session.post(
                    f"{ctx.server_url}/api/git/ai-job",
                    json={},
                ) as resp:
                    if resp.status == 200:
                        data = await resp.json()
                        results.append({
                            "name": data.get("name", "?"),
                            "path": data.get("worktreePath", "?"),
                            "branch": data.get("branchName", "?"),
                        })
                    else:
                        try:
                            error_data = await resp.json()
                            error_msg = error_data.get("message", "未知错误")
                        except Exception:
                            error_msg = await resp.text()
                        results.append({"error": error_msg})

            success_count = len([r for r in results if "error" not in r])

            if success_count == 0:
                error_msgs = [r.get("error", "未知错误") for r in results]
                return {
                    "content": [{"type": "text", "text": "创建隔离环境失败:\n" + "\n".join(error_msgs)}],
                    "is_error": True,
                }

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
                "is_error": True,
            }

    # ---------- complete_job ----------
    @builder.tool("complete_job", _COMPLETE_JOB_DESC, _COMPLETE_JOB_SCHEMA)
    async def ai_job_complete(args: dict[str, Any]) -> dict[str, Any]:
        """通知 Web 端 AI Job 已完成"""
        names_list = args.get("names", [])

        if not names_list:
            return {
                "content": [{"type": "text", "text": "错误: names 参数是必需的"}],
                "is_error": True,
            }

        try:
            message = json.dumps(names_list, ensure_ascii=False)

            async with ctx.session.post(
                f"{ctx.server_url}/api/notification/agent",
                json={
                    "title": "AI Job 已完成",
                    "message": message,
                    "type": "success",
                },
            ) as resp:
                if resp.status != 200:
                    return {
                        "content": [{"type": "text", "text": f"发送通知失败: HTTP {resp.status}"}],
                        "is_error": True,
                    }

            return {
                "content": [{"type": "text", "text": f"已通知 Web 端:{', '.join(names_list)} 任务完成"}]
            }

        except aiohttp.ClientError as e:
            return {
                "content": [{"type": "text", "text": f"无法连接 Server: {str(e)}"}],
                "is_error": True,
            }

    # ---------- canvas_vision（截图 / 识图 / 截图+识图 三模式自动判断）----------
    async def _canvas_vision_body(args: dict[str, Any]) -> dict[str, Any]:
        """截图 / 识图 / 截图+识图 三模式自动判断（无显式 mode 参数）。"""
        project_path = str(args.get("projectPath") or "").strip()
        if not project_path:
            return {
                "content": [{"type": "text", "text": "错误: projectPath 必须提供"}],
                "is_error": True,
            }

        project_dir = Path(project_path).expanduser().resolve()
        if not project_dir.exists():
            return {
                "content": [{"type": "text", "text": f"错误: 项目目录不存在: {project_dir}"}],
                "is_error": True,
            }
        if project_dir.is_file():
            return {
                "content": [{"type": "text", "text": "错误: projectPath 必须是解压后的项目目录"}],
                "is_error": True,
            }

        prompt = str(args.get("prompt") or "").strip()
        attachment_id = str(args.get("attachmentId") or "").strip()
        image_path_arg = str(args.get("path") or "").strip()
        image_base64 = str(args.get("base64") or "").strip()
        has_image_source = bool(attachment_id or image_path_arg or image_base64)
        # 截图范围信号:targetId/targetIds/viewport/shots（variantId 须配 viewport,不单独计为范围）
        has_shot_range = any(
            args.get(k) is not None for k in ("targetId", "targetIds", "viewport", "shots")
        )

        variant_id = str(args.get("variantId") or "").strip()
        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        # v3.4 D9:timeout 转单请求级 (ctx.session 是长期复用 session)
        request_timeout = aiohttp.ClientTimeout(total=90)

        # ===== 模式①:只截图（无 prompt）=====
        if not prompt:
            if has_image_source:
                return {
                    "content": [{"type": "text", "text": "错误: 只截图模式(未传 prompt)不接受图源参数 attachmentId/path/base64;识图请同时传 prompt。"}],
                    "is_error": True,
                }
            resolved_viewports, err = _resolve_screenshot_viewports(args)
            if err:
                return {"content": [{"type": "text", "text": f"错误: {err}"}], "is_error": True}
            viewports = resolved_viewports or _full_screenshot_viewports()

            try:
                if len(viewports) == 1:
                    image_data, serr = await _render_viewport(
                        ctx.session, ctx.server_url, project_dir, viewports[0], variant_id, request_timeout
                    )
                    if serr:
                        return {"content": [{"type": "text", "text": serr}], "is_error": True}
                    label = _sanitize_filename(_build_shot_label(viewports[0], 1))
                    filename = f"bg_{label}_{timestamp}.png"
                    saved_path = _save_screenshot(image_data, project_dir, filename)
                    return {"content": [
                        {"type": "image", "data": _strip_data_uri_prefix(image_data), "mimeType": "image/png"},
                        {"type": "text", "text": f"截图已完成(已保存至 {saved_path})。请先仔细查看上方图片再继续后续步骤。如果看不到图片,请用 Read 工具查看 {saved_path} 。"},
                    ]}

                items = []
                for idx, viewport in enumerate(viewports, start=1):
                    label = _sanitize_filename(_build_shot_label(viewport, idx))
                    items.append({
                        "name": label,
                        "layerPreset": SCREENSHOT_LAYER_PRESET,
                        "layerDisable": SCREENSHOT_LAYER_DISABLE,
                        "viewport": viewport,
                    })
                payload = {
                    "projectPath": str(project_dir),
                    "scale": SCREENSHOT_SCALE,
                    "autoFitViewport": SCREENSHOT_AUTO_FIT,
                    "items": items,
                }
                if variant_id:
                    payload["variantId"] = variant_id
                async with ctx.session.post(
                    f"{ctx.server_url}/api/screenshot/render-batch",
                    json=payload,
                    timeout=request_timeout,
                ) as resp:
                    data = await resp.json() if resp.content_type == "application/json" else await resp.text()
                    if resp.status != 200:
                        message = data.get("message") if isinstance(data, dict) else str(data)
                        return {
                            "content": [{"type": "text", "text": f"后台批量截图失败: HTTP {resp.status} {message}"}],
                            "is_error": True,
                        }

                items_result = data.get("items") if isinstance(data, dict) else None
                if not isinstance(items_result, list):
                    return {
                        "content": [{"type": "text", "text": "后台批量截图失败: 返回 items 无效"}],
                        "is_error": True,
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
                        "is_error": True,
                    }

                content_blocks.append({"type": "text", "text": "以上是所有截图。请先仔细查看图片再继续后续步骤。如果你无法直接看到图片,请用 Read 工具逐一查看上述路径。"})
                return {"content": content_blocks}

            except aiohttp.ClientError as e:
                return {"content": [{"type": "text", "text": f"无法连接 Server: {str(e)}"}], "is_error": True}
            except Exception as e:
                return {"content": [{"type": "text", "text": f"后台截图处理失败: {str(e)}"}], "is_error": True}

        # ===== 有 prompt:识图（②/③）=====
        if has_image_source and has_shot_range:
            return {
                "content": [{"type": "text", "text": "错误: 图源(attachmentId/path/base64)与截图范围(targetId/targetIds/viewport/shots)二选一,不可同给。"}],
                "is_error": True,
            }

        try:
            cfg = load_recognition_config()
        except RecognitionConfigError as exc:
            return {"content": [{"type": "text", "text": f"识图配置缺失: {exc.message}"}], "is_error": True}

        # ----- 模式②:只识图（有图源）-----
        if has_image_source:
            source_count = sum(1 for v in (attachment_id, image_path_arg, image_base64) if v)
            if source_count != 1:
                return {
                    "content": [{"type": "text", "text": "错误: 图源 attachmentId/path/base64 三选一(只能给一个)。"}],
                    "is_error": True,
                }

            tmp_file: Path | None = None
            try:
                if attachment_id:
                    try:
                        local_path = Path(resolve_attachment_local_path(project_path, attachment_id))
                    except AttachmentResolutionError as exc:
                        return {"content": [{"type": "text", "text": exc.message}], "is_error": True}
                    if not local_path.is_file():
                        return {"content": [{"type": "text", "text": f"attachment_missing: {attachment_id}"}], "is_error": True}
                elif image_path_arg:
                    local_path = Path(image_path_arg).expanduser()
                    if not local_path.is_file():
                        return {"content": [{"type": "text", "text": f"path_missing: {image_path_arg}"}], "is_error": True}
                    mime_guess = mimetypes.guess_type(local_path.name)[0] or ""
                    if not mime_guess.startswith("image/"):
                        return {"content": [{"type": "text", "text": f"path_invalid: not an image ({image_path_arg})"}], "is_error": True}
                else:
                    try:
                        image_bytes = _decode_image_data(image_base64)
                    except Exception as exc:
                        return {"content": [{"type": "text", "text": f"base64_invalid: {exc}"}], "is_error": True}
                    tmp_dir = project_dir / SCREENSHOT_DIR_NAME
                    tmp_dir.mkdir(parents=True, exist_ok=True)
                    tmp_file = tmp_dir / f"vision_tmp_{timestamp}.png"
                    tmp_file.write_bytes(image_bytes)
                    local_path = tmp_file

                result_text, err = await _recognize_image(ctx.session, cfg, local_path, prompt)
                if err:
                    return {"content": [{"type": "text", "text": err}], "is_error": True}
                return {"content": [{"type": "text", "text": result_text}]}
            finally:
                if tmp_file is not None:
                    try:
                        tmp_file.unlink()
                    except OSError:
                        pass

        # ----- 模式③:截图+识图（有截图范围,不批量）-----
        if not has_shot_range:
            return {
                "content": [{"type": "text", "text": "错误: 传了 prompt 但既无图源也无截图范围;只识图请传图源(attachmentId/path/base64),截图+识图请传截图范围(targetId/viewport 等)。"}],
                "is_error": True,
            }

        resolved_viewports, verr = _resolve_screenshot_viewports(args)
        if verr:
            return {"content": [{"type": "text", "text": f"错误: {verr}"}], "is_error": True}
        viewports = resolved_viewports or _full_screenshot_viewports()
        if len(viewports) != 1:
            return {
                "content": [{"type": "text", "text": "错误: 截图+识图不支持批量(targetIds/shots);单图请用 targetId 或 viewport。"}],
                "is_error": True,
            }

        try:
            image_data, serr = await _render_viewport(
                ctx.session, ctx.server_url, project_dir, viewports[0], variant_id, request_timeout
            )
        except aiohttp.ClientError as e:
            return {"content": [{"type": "text", "text": f"无法连接 Server: {str(e)}"}], "is_error": True}
        if serr:
            return {"content": [{"type": "text", "text": serr}], "is_error": True}

        label = _sanitize_filename(_build_shot_label(viewports[0], 1))
        filename = f"bg_{label}_{timestamp}.png"
        saved_path = _save_screenshot(image_data, project_dir, filename)

        # id→家具名 图例自动注入(仅③):从渲染同源 zone/variant 取 modules 拼图例 prepend 到 prompt。
        # 任何失败静默跳过、绝不阻断识图(图例是增强项)。空户型/无方案/外部图天然 no-op。
        effective_prompt = prompt
        try:
            zone_id = _zone_id_from_viewport(viewports[0])
            if zone_id:
                module_files = await _fetch_scheme_modules(
                    ctx.session, ctx.server_url, zone_id, variant_id, request_timeout
                )
                if module_files:
                    legend = _build_module_legend(module_files)
                    if legend:
                        effective_prompt = legend + "\n\n" + prompt
        except Exception as exc:  # noqa: BLE001 —— 图例注入失败不得阻断识图
            print(f"[canvas_vision] 图例注入跳过: {type(exc).__name__}: {exc}", file=sys.stderr, flush=True)

        result_text, aerr = await _recognize_image(ctx.session, cfg, Path(saved_path), effective_prompt)
        if aerr:
            return {"content": [{"type": "text", "text": f"{aerr}(截图已存 {saved_path})"}], "is_error": True}
        return {"content": [{"type": "text", "text": result_text}]}

    @builder.tool("canvas_vision", _CANVAS_VISION_DESC, _CANVAS_VISION_SCHEMA)
    async def canvas_vision(args: dict[str, Any]) -> dict[str, Any]:
        """canvas_vision 入口薄包装(Step0 可观测性)。

        把 _canvas_vision_body 的任何未捕获异常 / 进程级崩溃转成结构化 is_error
        结果(含异常类型 + 尾部 traceback + 总耗时),消除上层 "Command failed with
        no output" 黑洞——该消息不是本工具的任何 return 文案,说明此前是裸异常冒泡。
        """
        started = time.monotonic()
        try:
            return await _canvas_vision_body(args)
        except Exception as e:  # noqa: BLE001 —— 兜底转结构化错误,不让裸异常冒泡成 "no output"
            elapsed = time.monotonic() - started
            tb = traceback.format_exc()
            return {
                "content": [{"type": "text", "text": (
                    f"canvas_vision 未捕获异常(elapsed={elapsed:.1f}s): "
                    f"{type(e).__name__}: {e}\n--- traceback(尾部) ---\n{tb[-1800:]}"
                )}],
                "is_error": True,
            }

    # ---------- load_artifact ----------
    @builder.tool(
        "load_artifact",
        _LOAD_ARTIFACT_DESC,
        _LOAD_ARTIFACT_SCHEMA,
        annotations=ToolAnnotations(maxResultSizeChars=500_000),
    )
    async def load_artifact(args: dict[str, Any]) -> dict[str, Any]:
        """读取 artifact(按物理 zone 组织在 schemes/ 下)。"""
        if not ctx.server_url:
            return {
                "content": [{"type": "text", "text": "Server URL 未配置"}],
                "is_error": True,
            }

        artifact_kind = args["artifactKind"]
        path = args.get("path")
        # 数据按物理 zone 组织(schemes/{path}/);裸设计区 path 由 Server 经拓扑解析 adopted 指针。
        url = f"{ctx.server_url}/api/scheme/artifacts/{artifact_kind}"
        params = {"path": path} if path else None

        try:
            async with ctx.session.get(url, params=params) as resp:
                if resp.status == 200:
                    body = await resp.text()
                    return {"content": [{"type": "text", "text": body}]}
                if resp.status == 404:
                    # 「未找到」是 read 工具的正常负结果，不标 is_error（否则新设计区开局探查必污染成 [ERROR]）。
                    path_part = f", path={path}" if path else ""
                    return {
                        "content": [
                            {
                                "type": "text",
                                "text": f"未找到 artifact: artifactKind={artifact_kind}{path_part}",
                            }
                        ],
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
        except Exception as exc:
            return {
                "content": [{"type": "text", "text": f"调用失败: {type(exc).__name__}: {exc}"}],
                "is_error": True,
            }

    # ---------- validate_layout（包A：迁回平台，委派 active plugin 校验脚本）----------
    @builder.tool("validate_layout", _VALIDATE_LAYOUT_DESC, _VALIDATE_LAYOUT_SCHEMA)
    async def validate_layout(args: dict[str, Any]) -> dict[str, Any]:
        """触发校验：先调 /api/modules/normalize 规范化，无错后调 /api/validation/layout。
        两端点内部委派当前 active plugin 的 validators 脚本。"""
        zone_ids = args.get("zoneIds")
        variant_id = args.get("variantId")
        if variant_id and not zone_ids:
            return {
                "content": [{"type": "text", "text": "validate_layout 错误: variantId 非空时必须显式指定 zoneIds（不允许全分区扫描变体）"}],
                "is_error": True,
            }
        body: dict[str, Any] = {}
        if zone_ids:
            body["zoneIds"] = zone_ids
        if variant_id:
            body["variantId"] = variant_id

        try:
            async with ctx.session.post(f"{ctx.server_url}/api/modules/normalize", json=body) as resp:
                if resp.status != 200:
                    try:
                        error_data = await resp.json()
                        error_msg = error_data.get("message", f"HTTP {resp.status}")
                    except Exception:
                        error_msg = await resp.text()
                    return {"content": [{"type": "text", "text": f"规范化请求失败: {error_msg}"}], "is_error": True}

                normalize_report = await resp.json()
                if normalize_report.get("errorCount", 0) > 0:
                    return {"content": [{"type": "text", "text": _format_normalization_report(normalize_report)}], "is_error": True}

            async with ctx.session.post(f"{ctx.server_url}/api/validation/layout", json=body) as resp:
                if resp.status != 200:
                    try:
                        error_data = await resp.json()
                        error_msg = error_data.get("message", f"HTTP {resp.status}")
                    except Exception:
                        error_msg = await resp.text()
                    return {"content": [{"type": "text", "text": f"验证请求失败: {error_msg}"}], "is_error": True}

                report = await resp.json()
                return {"content": [{"type": "text", "text": _format_validation_report(report)}]}

        except aiohttp.ClientError as e:
            return {"content": [{"type": "text", "text": f"无法连接 Server: {e}"}], "is_error": True}
