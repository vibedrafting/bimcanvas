"""Canvas MCP Server - BIMCanvas 画布操作工具

按 Calculator MCP 模式重构，直接使用 @tool 装饰器，避免复杂的动态发现机制。
"""

from typing import Any
import json
import aiohttp
from claude_agent_sdk import tool, create_sdk_mcp_server

SERVER_URL = "http://localhost:5000"


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
    "get_workflow_guide",
    "获取 layout-agent 工作流指导。**必须在执行任何操作前调用此工具**获取详细的操作流程。",
    {
        "$schema": "http://json-schema.org/draft-07/schema#",
        "type": "object",
        "properties": {
            "task_type": {
                "type": "string",
                "enum": ["query", "edit", "generate", "overview"],
                "description": "任务类型：query（查询统计）、edit（单一修改）、generate（完整布置）、overview（完整决策树）",
                "default": "overview"
            }
        },
        "additionalProperties": False
    }
)
async def get_workflow_guide(args: dict[str, Any]) -> dict[str, Any]:
    """获取 layout-agent 工作流指导"""
    task_type = args.get("task_type", "overview")

    guides = {
        "overview": """# Layout Agent 工作流程完整指南

## 决策树

```
收到任务
    │
    ▼
Step 1: 阅读项目 README.md
│ → 了解目录结构、文件用途
    │
    ▼
Step 2: 判断任务类型
│ → 根据【操作类型】或关键词判断
    │
    ├─ query（只读）→ 调用 get_workflow_guide(task_type="query")
    │
    ├─ edit（单一修改）→ 调用 get_workflow_guide(task_type="edit")
    │
    └─ generate（完整布置）→ 调用 get_workflow_guide(task_type="generate")
```

## 核心约束（所有流程适用）

### 布置规则
- 大型家具靠墙（床、衣柜、沙发）
- 电视柜居中于电视墙，沙发正对电视（2.5-4m）
- 床头不靠窗，家具不阻挡门
- 通道宽度 ≥ 800mm
- 不与禁区重叠
- 不阻挡门开启

### 数据真实性
- 输出必须**严格基于**实际读取的文件内容
- 统计结果必须与 modules.json 数组长度一致
- 空数组 → 报告"数量为 0"，禁止推断
- 报告的每个模块 ID 必须在 modules.json 中实际存在

## 工作目录说明
- **query 任务**：使用当前工作目录
- **execute 任务**：使用 MainAgent 传递的 worktreePath（隔离环境）

确保所有文件操作都在正确的工作目录下执行。
""",
        "query": """# Query 流程（只读）

**触发条件**：【操作类型】: query 或关键词"统计/查看/列出/有多少"

**允许工具**：Read, Glob, Grep
**禁止工具**：Write, Edit

**步骤**：
1. Read 目标数据文件（如 modules.json）
2. 空数据检查 → 空则报告"数量为 0"
3. 分析/统计（仅基于实际读取的数据）
4. 验证：报告内容必须与文件实际内容一致
5. 返回结果

**禁止行为**：
- 根据房间信息推断/编造不存在的模块
- 空数据时自动创建示例数据

**示例**：
- "统计当前卧室有多少家具" → Read modules.json，统计 zoneId 为卧室的模块数量
- "查看客厅布置状态" → Read modules.json，筛选客厅区域的模块并展示
""",
        "edit": """# Edit 流程（单一修改）

**触发条件**：【操作类型】: execute + 关键词"移动/删除/旋转/调整"

**步骤**：
1. Read modules.json
2. 定位目标模块
3. 执行修改操作
4. 验证约束（间距≥800mm、不超边界、不重叠）
5. Write 保存结果

**示例**：
- "移动沙发到靠窗位置" → Read → 修改 bounds 坐标 → 验证 → Write
- "删除茶几" → Read → 移除对应项 → Write
- "旋转床 90 度" → Read → 修改 facing 和 bounds → Write
""",
        "generate": """# Generate 流程（完整布置）

**触发条件**：【操作类型】: execute + 关键词"布置/设计/创建/生成"

**步骤**：
1. **必须**先阅读 knowledge/placement_guide.md
2. Read computed/room_zones.json
3. Read modules/module_library.json
4. Read baseline/openings.json
5. 分析空间，按优先级布置：
   - 锚点家具（电视柜/床/餐桌）
   - 主要家具（沙发/衣柜）
   - 辅助家具（茶几/边几）
6. 验证约束
7. Write schemes/modules.json

**布置优先级**：
1. **锚点家具**：客厅→电视柜，卧室→床，餐厅→餐桌
2. **主要家具**：围绕锚点布置
3. **辅助家具**：填充剩余空间

**标签驱动选择**：
根据 zone.tags 筛选 module.tags 有交集的模块。

**输出格式（modules.json）**：
```json
[
  {
    "id": "m_1",
    "moduleId": "mod_bed_001",
    "zoneId": "rz_3",
    "bounds": [[11100, 2000], [13100, 2000], [13100, 4000], [11100, 4000]],
    "facing": "north",
    "items": []
  }
]
```
"""
    }

    guide_text = guides.get(task_type, guides["overview"])

    return {
        "content": [{"type": "text", "text": guide_text}]
    }


# 创建 Canvas MCP Server
canvas_mcp = create_sdk_mcp_server(
    name="canvas",
    version="1.0.0",
    tools=[ai_job_create, ai_job_complete, get_workflow_guide],
)

# 预批准工具列表
CANVAS_ALLOWED_TOOLS = [
    "mcp__canvas__create_job",
    "mcp__canvas__complete_job",
    "mcp__canvas__get_workflow_guide",
]
