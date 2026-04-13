"""Canvas MCP Server - BIMCanvas 画布操作工具

按 Calculator MCP 模式重构，直接使用 @tool 装饰器，避免复杂的动态发现机制。
"""

from datetime import datetime
import os
from pathlib import Path
from typing import Any
import base64
import json
import re
import aiohttp
from claude_agent_sdk import tool, create_sdk_mcp_server

SERVER_URL = os.getenv("BIMCANVAS_SERVER_URL", "http://localhost:5000").rstrip("/")
SCREENSHOT_LAYER_PRESET = "Agent"
SCREENSHOT_SCALE = 2
SCREENSHOT_AUTO_FIT = True
SCREENSHOT_DIR_NAME = "screenshots"


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
            required_keys = {"minX", "minY", "maxX", "maxY"}
            if not required_keys.issubset(bounds.keys()):
                return None, "viewport.bounds 需要 minX/minY/maxX/maxY"
            normalized["bounds"] = bounds
        return normalized, None

    # 旧格式：mode + roomId/zoneId/bounds（向后兼容）
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
        if not isinstance(bounds, dict):
            return None, "viewport.mode=bounds 时必须提供 bounds"
        required_keys = {"minX", "minY", "maxX", "maxY"}
        if not required_keys.issubset(bounds.keys()):
            return None, "viewport.bounds 需要 minX/minY/maxX/maxY"
        normalized["bounds"] = bounds

    return normalized, None


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
    "后台截图：调用 Server 截图 API，直接返回截图图片（同时保存到 screenshots 目录备查）",
    {
        "$schema": "http://json-schema.org/draft-07/schema#",
        "type": "object",
        "properties": {
            "projectPath": {
                "type": "string",
                "description": "BIMCanvas 项目目录（包含 project.json 的目录）。必须使用系统提示词中的「项目路径」或「工作目录」，不要使用 skill/plugin 目录。"
            },
            "viewport": {
                "type": "object",
                "description": "单张截图范围。推荐用 id 字段（如 rz_1/r_1/dz_1），前端自动查找；留空则全屏。也兼容旧格式 mode+roomId/zoneId。",
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
                "description": "批量截图列表（每项仅包含 viewport）",
                "items": {
                    "type": "object",
                    "properties": {
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
                    "required": ["viewport"],
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

    viewport_arg = args.get("viewport")
    shots_arg = args.get("shots")
    if viewport_arg and shots_arg:
        return {
            "content": [{"type": "text", "text": "错误: viewport 与 shots 不能同时提供"}],
            "is_error": True
        }

    if shots_arg is None:
        if viewport_arg is None:
            return {
                "content": [{"type": "text", "text": "错误: 必须提供 viewport 或 shots"}],
                "is_error": True
            }
        shots_arg = [{"viewport": viewport_arg}]

    if not isinstance(shots_arg, list) or not shots_arg:
        return {
            "content": [{"type": "text", "text": "错误: shots 必须是非空数组"}],
            "is_error": True
        }

    viewports: list[dict[str, Any]] = []
    for idx, shot in enumerate(shots_arg):
        if not isinstance(shot, dict):
            return {
                "content": [{"type": "text", "text": f"错误: shots[{idx}] 必须是对象"}],
                "is_error": True
            }
        viewport_data = shot.get("viewport")
        if viewport_data is None:
            return {
                "content": [{"type": "text", "text": f"错误: shots[{idx}].viewport 必须提供"}],
                "is_error": True
            }
        normalized, err = _normalize_viewport(viewport_data)
        if err:
            return {
                "content": [{"type": "text", "text": f"错误: shots[{idx}] {err}"}],
                "is_error": True
            }
        viewports.append(normalized)

    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    timeout = aiohttp.ClientTimeout(total=90)

    try:
        async with aiohttp.ClientSession(timeout=timeout) as session:
            if len(viewports) == 1:
                payload = {
                    "projectPath": str(project_dir),
                    "layerPreset": SCREENSHOT_LAYER_PRESET,
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


@tool(
    "validate_layout",
    "验证当前方案的布局合法性（布局编译器）。检查三类错误：(1)超出设计区域 (2)与墙体/柱子/禁区重叠 (3)模块间重叠。调用时还会将目标 modules.json 中有效的 facing.semantic 收敛为 facing.value，并把 semantic 清空为 null。可选 zoneIds 参数仅验证指定分区内的模块。",
    {
        "$schema": "http://json-schema.org/draft-07/schema#",
        "type": "object",
        "properties": {
            "zoneIds": {
                "type": "array",
                "items": {"type": "string"},
                "description": "可选。仅验证这些 Zone 内的模块（如 [\"rz_1\", \"dz_2\"]）。不传则验证全部模块。"
            }
        },
        "additionalProperties": False
    }
)
async def validate_layout(args: dict[str, Any]) -> dict[str, Any]:
    """验证当前方案的布局合法性（布局编译器）"""
    zone_ids = args.get("zoneIds")
    body = {"zoneIds": zone_ids} if zone_ids else None

    try:
        async with aiohttp.ClientSession() as session:
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
            },
            "debug": {
                "type": "boolean",
                "description": "可选。为 true 时同步推送数据到 Web 端进行可视化调试。"
            }
        },
        "additionalProperties": False
    }
)
async def get_zone_boundaries(args: dict[str, Any]) -> dict[str, Any]:
    """获取 Zone 边界段语义数据"""
    zone_ids = args.get("zoneIds")
    debug = args.get("debug", False)
    body = {}
    if zone_ids:
        body["zoneIds"] = zone_ids
    if debug:
        body["debug"] = True
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
    "save_semantic_plan",
    "保存语义方案版本。在规划阶段的每个子阶段（2.1/2.2/2.3）完成后调用，提交当前版本的语义方案。",
    {
        "$schema": "http://json-schema.org/draft-07/schema#",
        "type": "object",
        "properties": {
            "zoneId": {
                "type": "string",
                "description": "目标 Zone ID，如 'rz_3'"
            },
            "version": {
                "type": "string",
                "enum": ["v0.1", "v0.2", "v0.3"],
                "description": "语义方案版本：v0.1=空间骨架, v0.2=主体框架, v0.3=完整方案"
            },
            "planType": {
                "type": "string",
                "enum": ["derived", "reference"],
                "description": "图纸类型：derived=主动推导设计，reference=参考图翻译"
            },
            "content": {
                "type": "string",
                "description": "语义方案文本内容（markdown 格式）"
            }
        },
        "required": ["zoneId", "version", "planType", "content"],
        "additionalProperties": False
    }
)
async def save_semantic_plan(args: dict[str, Any]) -> dict[str, Any]:
    """保存语义方案版本"""
    zone_id = args["zoneId"]
    version = args["version"]
    plan_type = args["planType"]
    content = args["content"]

    body = {
        "zoneId": zone_id,
        "version": version,
        "planType": plan_type,
        "content": content
    }

    try:
        async with aiohttp.ClientSession() as session:
            async with session.post(
                f"{SERVER_URL}/api/semantic-plan/save",
                json=body
            ) as resp:
                if resp.status == 200:
                    return {
                        "content": [{
                            "type": "text",
                            "text": f"语义方案 {plan_type} {version} 已保存。继续下一阶段。"
                        }]
                    }
                else:
                    error_text = await resp.text()
                    return {
                        "content": [{"type": "text", "text": f"保存失败: {error_text}"}],
                        "is_error": True
                    }
    except aiohttp.ClientError as e:
        return {
            "content": [{"type": "text", "text": f"无法连接 Server: {str(e)}"}],
            "is_error": True
        }


@tool(
    "load_semantic_plan",
    "加载当前设计区的生效语义方案。返回当前可施工图纸，而不是完整历史。",
    {
        "$schema": "http://json-schema.org/draft-07/schema#",
        "type": "object",
        "properties": {
            "zoneId": {
                "type": "string",
                "description": "目标 Zone ID，如 'rz_3'"
            }
        },
        "required": ["zoneId"],
        "additionalProperties": False
    }
)
async def load_semantic_plan(args: dict[str, Any]) -> dict[str, Any]:
    """加载语义方案当前生效版本"""
    zone_id = args["zoneId"]

    try:
        async with aiohttp.ClientSession() as session:
            async with session.get(
                f"{SERVER_URL}/api/semantic-plan/{zone_id}"
            ) as resp:
                if resp.status == 200:
                    data = await resp.json()

                    # 构建基础文本
                    text_parts = [
                        f"status: {data['status']}",
                        f"zoneId: {data['zoneId']}",
                        f"planType: {data['planType']}",
                        f"effectiveVersion: {data['effectiveVersion']}",
                        f"timestamp: {data['timestamp']}"
                    ]

                    # 如果有 referenceAnalysis，添加到文本中
                    if data.get('referenceAnalysis'):
                        ref_analysis = data['referenceAnalysis']
                        text_parts.append(f"\nreferenceAnalysis:")
                        text_parts.append(f"  relevance: {ref_analysis.get('relevance', 'N/A')}")
                        text_parts.append(f"  sourceImageId: {ref_analysis.get('sourceImageId', 'N/A')}")
                        text_parts.append(f"  confirmedConstraints: {len(ref_analysis.get('confirmedConstraints', []))} 条")
                        text_parts.append(f"  referenceHints: {len(ref_analysis.get('referenceHints', []))} 条")

                    text_parts.append(f"\n{data['content']}")
                    text = "\n".join(text_parts)

                    return {
                        "content": [{
                            "type": "text",
                            "text": text
                        }],
                        "structuredContent": data
                    }

                if resp.status in (400, 404, 409):
                    data = await resp.json()
                    message = data.get("message", "加载语义方案失败")
                    return {
                        "content": [{"type": "text", "text": message}],
                        "structuredContent": data,
                        "is_error": True
                    }

                error_text = await resp.text()
                return {
                    "content": [{"type": "text", "text": f"加载失败: {error_text}"}],
                    "is_error": True
                }
    except aiohttp.ClientError as e:
        return {
            "content": [{"type": "text", "text": f"无法连接 Server: {str(e)}"}],
            "is_error": True
        }


@tool(
    "save_reference_analysis",
    "保存参考分析结果到语义方案。在参考图分析完成后调用，提交约束包（confirmedConstraints + referenceHints）。",
    {
        "$schema": "http://json-schema.org/draft-07/schema#",
        "type": "object",
        "properties": {
            "zoneId": {
                "type": "string",
                "description": "目标 Zone ID，如 'rz_3'"
            },
            "sourceImageId": {
                "type": "string",
                "description": "参考图附件 ID"
            },
            "relevance": {
                "type": "string",
                "enum": ["partially_related", "structurally_related"],
                "description": "关联性等级：partially_related=部分相关, structurally_related=结构相关"
            },
            "confirmedConstraints": {
                "type": "array",
                "description": "确认的约束（硬约束）",
                "items": {
                    "type": "object",
                    "properties": {
                        "type": {
                            "type": "string",
                            "enum": ["negativeSpace", "furnitureSelection", "anchorPoint"],
                            "description": "约束类型"
                        },
                        "description": {
                            "type": "string",
                            "description": "约束描述"
                        },
                        "source": {
                            "type": "string",
                            "description": "约束来源：用户确认、几何验证"
                        }
                    },
                    "required": ["type", "description", "source"]
                }
            },
            "referenceHints": {
                "type": "array",
                "description": "参考提示（软提示）",
                "items": {
                    "type": "object",
                    "properties": {
                        "type": {
                            "type": "string",
                            "enum": ["zoningIntent", "designPrinciple", "furnitureRelation"],
                            "description": "提示类型"
                        },
                        "description": {
                            "type": "string",
                            "description": "提示描述"
                        }
                    },
                    "required": ["type", "description"]
                }
            },
            "knownDifferences": {
                "type": "array",
                "description": "已知差异列表",
                "items": {"type": "string"}
            },
            "userConfirmations": {
                "type": "array",
                "description": "用户确认记录",
                "items": {"type": "string"}
            }
        },
        "required": ["zoneId", "relevance", "confirmedConstraints", "referenceHints"],
        "additionalProperties": False
    }
)
async def save_reference_analysis(args: dict[str, Any]) -> dict[str, Any]:
    """保存参考分析结果"""
    zone_id = args["zoneId"]
    relevance = args["relevance"]

    body = {
        "zoneId": zone_id,
        "sourceImageId": args.get("sourceImageId", ""),
        "relevance": relevance,
        "confirmedConstraints": args.get("confirmedConstraints", []),
        "referenceHints": args.get("referenceHints", []),
        "knownDifferences": args.get("knownDifferences", []),
        "userConfirmations": args.get("userConfirmations", [])
    }

    try:
        async with aiohttp.ClientSession() as session:
            async with session.post(
                f"{SERVER_URL}/api/semantic-plan/save-reference-analysis",
                json=body
            ) as resp:
                if resp.status == 200:
                    return {
                        "content": [{
                            "type": "text",
                            "text": f"参考分析结果已保存（关联性：{relevance}）。继续规划阶段。"
                        }]
                    }
                else:
                    error_text = await resp.text()
                    return {
                        "content": [{"type": "text", "text": f"保存失败: {error_text}"}],
                        "is_error": True
                    }
    except aiohttp.ClientError as e:
        return {
            "content": [{"type": "text", "text": f"无法连接 Server: {str(e)}"}],
            "is_error": True
        }


# 创建 Canvas MCP Server
canvas_mcp = create_sdk_mcp_server(
    name="canvas",
    version="1.0.0",
    tools=[
        # ai_job_create,          # 临时禁用：Worktree 隔离环境（暂不需要）
        # ai_job_complete,        # 临时禁用：Job 完成通知（暂不需要）
        request_background_screenshot,
        validate_layout,
        get_zone_boundaries,
        save_semantic_plan,  # 语义方案提交（turn 边界）
        load_semantic_plan,  # 加载当前生效图纸
        save_reference_analysis,  # 新增：保存参考分析结果
    ],
)

# 预批准工具列表
CANVAS_ALLOWED_TOOLS = [
    # "mcp__canvas__create_job",                      # 临时禁用
    # "mcp__canvas__complete_job",                     # 临时禁用
    "mcp__canvas__request_background_screenshot",
    "mcp__canvas__validate_layout",
    "mcp__canvas__get_zone_boundaries",
    "mcp__canvas__save_semantic_plan",
    "mcp__canvas__load_semantic_plan",
]
