"""
AI Job MCP 工具

用于 Agent Git 工作流中的隔离环境管理。
Agent 只需调用这两个工具，复杂的 Git 操作由 Server 处理。

工具:
- ai_job_create: 为 SubAgent 创建隔离工作环境
- ai_job_complete: 批量通知 Web 端 AI Job 已完成，可供用户审查
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
    description="批量通知 Web 端：指定的 AI Job 已完成，可供用户审查",
    schema={"names": list}
)
async def ai_job_complete(args: dict[str, Any]) -> dict[str, Any]:
    """
    批量通知 Web 端指定的 AI Job 已完成

    Web 端收到通知后，会打开 diff/merge 可视化界面。
    MainAgent 在对话中总结修改内容，此工具仅负责通知。
    """
    names = args.get("names", [])

    if not names or not isinstance(names, list):
        return {
            "content": [{"type": "text", "text": "错误: 必须指定 names（AI Job 名称列表）"}],
            "is_error": True
        }

    if len(names) == 0:
        return {
            "content": [{"type": "text", "text": "错误: names 列表不能为空"}],
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

                # 构建返回信息
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
