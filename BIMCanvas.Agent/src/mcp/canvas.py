"""Canvas MCP Server - BIMCanvas 画布操作工具

按 Calculator MCP 模式重构，直接使用 @tool 装饰器，避免复杂的动态发现机制。
"""

from datetime import datetime
from pathlib import Path
from typing import Any
import base64
import json
import re
import aiohttp
from claude_agent_sdk import tool, create_sdk_mcp_server

SERVER_URL = "http://localhost:5000"
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
    mode = viewport.get("mode", "full")
    if mode == "room":
        return f"room_{viewport.get('roomId', index)}"
    if mode == "zone":
        return f"zone_{viewport.get('zoneId', index)}"
    if mode == "bounds":
        return f"bounds_{index}"
    return "full"


def _decode_image_data(image_data: str) -> bytes:
    if not image_data:
        raise ValueError("imageData 为空")
    if "," in image_data:
        image_data = image_data.split(",", 1)[1]
    return base64.b64decode(image_data)


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
    "后台截图：调用 Server 截图 API，保存到项目 screenshots 目录并返回完整路径（仅供 layout-agent 使用）",
    {
        "$schema": "http://json-schema.org/draft-07/schema#",
        "type": "object",
        "properties": {
            "projectPath": {
                "type": "string",
                "description": "项目根目录（必须是解压后的项目目录，通常使用当前工作目录 cwd）"
            },
            "viewport": {
                "type": "object",
                "description": "单张截图范围",
                "properties": {
                    "mode": {
                        "type": "string",
                        "enum": ["full", "room", "zone", "bounds"],
                        "description": "full/room/zone/bounds"
                    },
                    "roomId": {"type": "string"},
                    "zoneId": {"type": "string"},
                    "bounds": {
                        "type": "object",
                        "properties": {
                            "minX": {"type": "number"},
                            "minY": {"type": "number"},
                            "maxX": {"type": "number"},
                            "maxY": {"type": "number"}
                        },
                        "required": ["minX", "minY", "maxX", "maxY"]
                    }
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
                            "properties": {
                                "mode": {
                                    "type": "string",
                                    "enum": ["full", "room", "zone", "bounds"]
                                },
                                "roomId": {"type": "string"},
                                "zoneId": {"type": "string"},
                                "bounds": {
                                    "type": "object",
                                    "properties": {
                                        "minX": {"type": "number"},
                                        "minY": {"type": "number"},
                                        "maxX": {"type": "number"},
                                        "maxY": {"type": "number"}
                                    },
                                    "required": ["minX", "minY", "maxX", "maxY"]
                                }
                            }
                        }
                    },
                    "required": ["viewport"],
                    "additionalProperties": False
                }
            }
        },
        "required": ["projectPath"],
        "additionalProperties": False,
        "oneOf": [
            {"required": ["viewport"]},
            {"required": ["shots"]}
        ]
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
                return {"content": [{"type": "text", "text": saved_path}]}

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

            saved_paths: list[str] = []
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
                saved_paths.append(_save_screenshot(image_data, project_dir, filename))

            if errors:
                return {
                    "content": [{"type": "text", "text": "后台批量截图部分失败:\n" + "\n".join(errors)}],
                    "is_error": True
                }

            return {"content": [{"type": "text", "text": "\n".join(saved_paths)}]}

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


@tool(
    "get_workflow_guide",
    "【唯一官方来源】获取布置任务工作流指导。本工具是执行流程的唯一权威定义，必须在执行 Generate 任务前调用以确保遵守最新规范。返回内容覆盖所有任务类型的完整决策树和实现步骤。",
    {
        "$schema": "http://json-schema.org/draft-07/schema#",
        "type": "object",
        "properties": {
            "task_type": {
                "type": "string",
                "enum": ["query", "edit", "generate"],
                "description": "任务类型：query（查询统计）、edit（单一修改）、generate（完整布置）"
            }
        },
        "required": ["task_type"],
        "additionalProperties": False
    }
)
async def get_workflow_guide(args: dict[str, Any]) -> dict[str, Any]:
    """获取布置任务工作流指导"""
    task_type = args.get("task_type", "generate")

    guides = {
        "query": """# Query 流程（只读）

⚠️ **本流程是 query 任务的唯一官方定义，必须严格按步骤执行**

**触发条件**：关键词"统计/查看/列出/有多少"

**允许工具**：Read, Glob, Grep
**禁止工具**：Write, Edit

**步骤**：
1. 如需空间/布局判断，先调用 `mcp__canvas__request_background_screenshot` 获取必要截图
2. Read 目标数据文件（如 modules.json）
3. 空数据检查 → 空则报告"数量为 0"
4. 分析/统计（仅基于实际读取的数据）
5. 验证：报告内容必须与文件实际内容一致
6. 返回结果

**禁止行为**：
- 根据房间信息推断/编造不存在的模块
- 空数据时自动创建示例数据

**示例**：
- "统计当前卧室有多少家具" → Read modules.json，统计 zoneId 为卧室的模块数量
- "查看客厅布置状态" → Read modules.json，筛选客厅区域的模块并展示
""",
        "edit": """# Edit 流程（单一修改）

⚠️ **本流程是 edit 任务的唯一官方定义，必须严格按步骤执行**

**触发条件**：关键词"移动/删除/旋转/调整"

**步骤**：
1. 视需要在修改前调用 `mcp__canvas__request_background_screenshot`（局部或全局）
2. Read modules.json
3. 定位目标模块
4. 执行修改操作
5. 验证约束（间距≥800mm、不超边界、不重叠）
6. Write 保存结果
7. 视需要在修改后再次调用截图工具验证结果

**示例**：
- "移动沙发到靠窗位置" → Read → 修改 bounds 坐标 → 验证 → Write
- "删除茶几" → Read → 移除对应项 → Write
- "旋转床 90 度" → Read → 修改 facing 和 bounds → Write
""",
        "generate": """# Generate 流程（完整布置）

⚠️ **本流程是 generate 任务的唯一官方定义，必须严格按步骤执行**

## 执行前强制检查清单

在执行任何 Write 操作前，必须确认以下步骤已完成：

□ 已调用 mcp__canvas__request_background_screenshot（前置）
  → 如果未调用，立即停止并先执行截图

□ 已读取 knowledge/placement_guide.md
  → 如果未读取，立即停止并先读取
  → 重点阅读：§四 尺寸标准、§五 房间布置要点

□ 已读取 modules/module_library.json
  → 如果未读取，立即停止并先读取
  → 家具尺寸必须从此文件选择，禁止编造

□ 已读取 computed/room_zones.json

□ 已读取 computed/exclusions.json

□ 已读取 baseline/openings.json

**警告**：如果以上任何步骤缺失，禁止执行 Write 操作。

## 执行步骤

1. **前置截图**（必须）
   mcp__canvas__request_background_screenshot(
     projectPath="{当前工作目录}",
     viewport={"mode": "full"}
   )
   → 理解空间形态、门窗位置、房间朝向

2. **读取设计规范**（必须）
   Read knowledge/placement_guide.md

3. **读取家具库**（必须）
   Read modules/module_library.json
   → 家具尺寸必须从此文件选择

4. **读取空间数据**
   - Read computed/room_zones.json
   - Read computed/exclusions.json
   - Read baseline/openings.json

5. **设计布置方案**
   基于：
   - 截图理解的空间形态
   - placement_guide 的设计规范
   - module_library 的家具选择

   按优先级布置：
   - 锚点家具（电视柜/床/餐桌）
   - 主要家具（沙发/衣柜）
   - 辅助家具（茶几/边几）

6. **写入结果**（注意路径）
   Write schemes/modules.json

   **路径规范**：
   - ✅ 正确：schemes/modules.json（统一文件）
   - ❌ 错误：schemes/rz_1/modules.json（不存在）

   **数据格式**：
   ```json
   [
     {
       "id": "m_1",
       "zoneId": "rz_1",  // ← 区分所属区域
       "moduleId": "mod_bed_double_001",  // ← 来自 module_library
       "bounds": [[x1,y1], [x2,y2], [x3,y3], [x4,y4]],
       "facing": "north",
       "items": []
     }
   ]
   ```

7. **后置截图验证**（必须）
   mcp__canvas__request_background_screenshot(
     projectPath="{当前工作目录}",
     viewport={"mode": "full"}
   )

## 数据真实性约束

- 家具尺寸必须来自 module_library.json，禁止编造
- 空数组 → 报告"数量为 0"，禁止推断
- 所有 moduleId 必须在 module_library 中实际存在

## 常见错误

| 错误 | 正确做法 |
|------|----------|
| 写入 schemes/rz_1/modules.json | 写入 schemes/modules.json |
| 凭空编造家具尺寸 | 从 module_library.json 选择 |
| 跳过截图步骤 | 前后各调用一次截图工具 |
| 跳过 placement_guide | 必须读取并遵守规范 |
"""
    }

    guide_text = guides.get(task_type, guides["generate"])

    return {
        "content": [{"type": "text", "text": guide_text}]
    }


# 创建 Canvas MCP Server
canvas_mcp = create_sdk_mcp_server(
    name="canvas",
    version="1.0.0",
    tools=[ai_job_create, ai_job_complete, request_background_screenshot, get_workflow_guide],
)

# 预批准工具列表
CANVAS_ALLOWED_TOOLS = [
    "mcp__canvas__create_job",
    "mcp__canvas__complete_job",
    "mcp__canvas__request_background_screenshot",
    "mcp__canvas__get_workflow_guide",
]
