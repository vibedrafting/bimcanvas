"""Canvas MCP Server - BIMCanvas 画布操作工具

按 Calculator MCP 模式重构，直接使用 @tool 装饰器，避免复杂的动态发现机制。
"""

from typing import Any
import aiohttp
from claude_agent_sdk import tool, create_sdk_mcp_server

SERVER_URL = "http://localhost:5000"


@tool("create_job", "为 SubAgent 创建隔离工作环境（Git Worktree）", {"name": str, "base_branch": str})
async def ai_job_create(args: dict[str, Any]) -> dict[str, Any]:
    """创建独立的 Git Worktree，让 SubAgent 在隔离环境中执行修改。"""
    name = args.get("name")
    base_branch = args.get("base_branch")

    if not name:
        return {
            "content": [{"type": "text", "text": "错误: 必须指定 name"}],
            "is_error": True
        }

    try:
        async with aiohttp.ClientSession() as session:
            request_body = {"name": name}
            if base_branch:
                request_body["baseBranch"] = base_branch

            async with session.post(
                f"{SERVER_URL}/api/git/ai-job",
                json=request_body
            ) as resp:
                if resp.status != 200:
                    error_data = await resp.json()
                    return {
                        "content": [{"type": "text", "text": f"创建 AI Job 失败: {error_data.get('message', '未知错误')}"}],
                        "is_error": True
                    }

                result = await resp.json()
                text = f"""AI Job 创建成功:
- 名称: {name}
- 工作目录: {result.get('worktreePath', '?')}
- 分支: {result.get('branchName', '?')}

SubAgent 应在此目录下执行文件修改。"""

                return {"content": [{"type": "text", "text": text}]}

    except aiohttp.ClientError as e:
        return {
            "content": [{"type": "text", "text": f"无法连接 Server: {str(e)}"}],
            "is_error": True
        }


@tool("complete_job", "批量通知 Web 端：指定的 AI Job 已完成，可供用户审查", {"names": list})
async def ai_job_complete(args: dict[str, Any]) -> dict[str, Any]:
    """Web 端收到通知后，会打开 diff/merge 可视化界面。"""
    names = args.get("names", [])

    if not names or not isinstance(names, list):
        return {
            "content": [{"type": "text", "text": "错误: 必须指定 names（AI Job 名称列表）"}],
            "is_error": True
        }

    try:
        async with aiohttp.ClientSession() as session:
            async with session.post(
                f"{SERVER_URL}/api/git/ai-jobs/complete",
                json={"names": names}
            ) as resp:
                if resp.status != 200:
                    error_data = await resp.json()
                    return {
                        "content": [{"type": "text", "text": f"批量标记完成失败: {error_data.get('message', '未知错误')}"}],
                        "is_error": True
                    }

                result = await resp.json()
                jobs = result.get("jobs", [])

                job_lines = []
                for job in jobs:
                    job_lines.append(f"  - {job.get('name')}: {job.get('branchName')} ({job.get('status')})")

                text = f"""AI Jobs 已标记完成 ({len(jobs)} 个):
{chr(10).join(job_lines)}

用户将在 Web 端看到 diff 预览，并决定是否合并这些修改。"""

                return {"content": [{"type": "text", "text": text}]}

    except aiohttp.ClientError as e:
        return {
            "content": [{"type": "text", "text": f"无法连接 Server: {str(e)}"}],
            "is_error": True
        }


# 创建 Canvas MCP Server
canvas_mcp = create_sdk_mcp_server(
    name="canvas",
    version="1.0.0",
    tools=[ai_job_create, ai_job_complete],
)

# 预批准工具列表
CANVAS_ALLOWED_TOOLS = [
    "mcp__canvas__create_job",
    "mcp__canvas__complete_job",
]
