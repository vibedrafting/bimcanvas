"""
AI Job MCP 工具

用于 Agent Git 工作流中的隔离环境管理。
Agent 只需调用这两个工具，复杂的 Git 操作由 Server 处理。

工具:
- ai_job_create: 创建隔离工作环境
- ai_job_complete: 标记完成，通知用户审查
"""

from typing import Any
import aiohttp
from ..decorators import mcp_tool

SERVER_URL = "http://localhost:5000"


@mcp_tool(
    description="为 SubAgent 创建隔离工作环境（Git Worktree）",
    schema={"name": str, "base_branch": str}
)
async def ai_job_create(args: dict[str, Any]) -> dict[str, Any]:
    """
    为 SubAgent 创建隔离工作环境

    创建独立的 Git Worktree，让 SubAgent 在隔离环境中执行修改。
    Server 会自动处理：获取当前分支、自动存档、创建临时分支。
    """
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


@mcp_tool(
    description="标记 AI Job 完成，通知 Web 端供用户审查",
    schema={"name": str, "summary": str}
)
async def ai_job_complete(args: dict[str, Any]) -> dict[str, Any]:
    """
    标记 AI Job 完成，通知 Web 端供用户审查

    Server 会自动处理：提交 Worktree 中的更改、通知 Web 端展示 diff/合并按钮。
    用户将在 Web 端决定是否接受这些修改。
    """
    name = args.get("name")
    summary = args.get("summary")

    if not name:
        return {
            "content": [{"type": "text", "text": "错误: 必须指定 name"}],
            "is_error": True
        }

    if not summary:
        return {
            "content": [{"type": "text", "text": "错误: 必须指定 summary（修改总结）"}],
            "is_error": True
        }

    try:
        async with aiohttp.ClientSession() as session:
            async with session.post(
                f"{SERVER_URL}/api/git/ai-job/{name}/complete",
                json={"summary": summary}
            ) as resp:
                if resp.status != 200:
                    error_data = await resp.json()
                    return {
                        "content": [{"type": "text", "text": f"标记完成失败: {error_data.get('message', '未知错误')}"}],
                        "is_error": True
                    }

                result = await resp.json()
                text = f"""AI Job 已标记完成:
- 名称: {name}
- 状态: 等待用户审查

修改总结: {summary}

用户将在 Web 端看到 diff 预览，并决定是否合并这些修改。"""

                return {"content": [{"type": "text", "text": text}]}

    except aiohttp.ClientError as e:
        return {
            "content": [{"type": "text", "text": f"无法连接 Server: {str(e)}"}],
            "is_error": True
        }
