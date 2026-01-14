"""Git Worktree MCP 工具 - 管理 Git Worktree 实现并行开发隔离"""
from typing import Any
import aiohttp

from ..decorators import mcp_tool

# Server REST API URL
SERVER_URL = "http://localhost:5000"


@mcp_tool()
async def worktree_list(args: dict[str, Any]) -> dict[str, Any]:
    """
    获取所有 Worktree 列表

    返回当前项目的所有 Git Worktree（工作树）。

    Args:
        args: 空字典（不需要参数）

    Returns:
        MCP 响应，包含 Worktree 列表
    """
    try:
        async with aiohttp.ClientSession() as session:
            async with session.get(f"{SERVER_URL}/api/git/worktrees") as resp:
                if resp.status != 200:
                    error_data = await resp.json()
                    return {
                        "content": [{"type": "text", "text": f"获取 Worktree 列表失败: {error_data.get('message', '未知错误')}"}],
                        "is_error": True
                    }

                worktrees = await resp.json()

                if not worktrees:
                    return {
                        "content": [{"type": "text", "text": "没有 Worktree 或项目未加载"}]
                    }

                # 格式化 Worktree 列表
                lines = ["Git Worktree 列表:"]
                for wt in worktrees:
                    prefix = "[主] " if wt.get("isMain") else "     "
                    branch = wt.get("branch", "(detached)")
                    commit = wt.get("commitHash", "?")[:7] if wt.get("commitHash") else "?"
                    lines.append(f"{prefix}{wt.get('name', '?')} -> {branch} ({commit})")
                    lines.append(f"       路径: {wt.get('path', '?')}")

                return {
                    "content": [{"type": "text", "text": "\n".join(lines)}]
                }

    except aiohttp.ClientError as e:
        return {
            "content": [{"type": "text", "text": f"无法连接 Server: {str(e)}"}],
            "is_error": True
        }


@mcp_tool()
async def worktree_create(args: dict[str, Any]) -> dict[str, Any]:
    """
    创建新的 Worktree

    为并行任务创建独立的 Git Worktree。

    Args:
        args: 包含以下字段的字典：
            - name: Worktree 名称（如 "ai-storage"）（必需）
            - branch_name: 关联的分支名（如 "feat/ai-storage"）（必需）
                          如果分支不存在会自动创建

    Returns:
        MCP 响应，包含创建的 Worktree 信息
    """
    name = args.get("name")
    branch_name = args.get("branch_name")

    if not name:
        return {
            "content": [{"type": "text", "text": "错误: 必须指定 name"}],
            "is_error": True
        }

    if not branch_name:
        return {
            "content": [{"type": "text", "text": "错误: 必须指定 branch_name"}],
            "is_error": True
        }

    try:
        async with aiohttp.ClientSession() as session:
            async with session.post(
                f"{SERVER_URL}/api/git/worktrees",
                json={
                    "name": name,
                    "branchName": branch_name
                }
            ) as resp:
                if resp.status != 200:
                    error_data = await resp.json()
                    return {
                        "content": [{"type": "text", "text": f"创建 Worktree 失败: {error_data.get('message', '未知错误')}"}],
                        "is_error": True
                    }

                result = await resp.json()

                text = f"""Worktree 创建成功:
- 名称: {result.get('name', name)}
- 分支: {result.get('branch', branch_name)}
- 路径: {result.get('path', '?')}

现在可以在该 Worktree 中独立工作，不影响主工作区。"""

                return {
                    "content": [{"type": "text", "text": text}]
                }

    except aiohttp.ClientError as e:
        return {
            "content": [{"type": "text", "text": f"无法连接 Server: {str(e)}"}],
            "is_error": True
        }


@mcp_tool()
async def worktree_remove(args: dict[str, Any]) -> dict[str, Any]:
    """
    删除 Worktree

    删除指定的 Git Worktree。

    Args:
        args: 包含以下字段的字典：
            - name: Worktree 名称（必需）

    Returns:
        MCP 响应，包含删除结果
    """
    name = args.get("name")

    if not name:
        return {
            "content": [{"type": "text", "text": "错误: 必须指定 name"}],
            "is_error": True
        }

    try:
        async with aiohttp.ClientSession() as session:
            async with session.delete(f"{SERVER_URL}/api/git/worktrees/{name}") as resp:
                if resp.status != 200:
                    error_data = await resp.json()
                    return {
                        "content": [{"type": "text", "text": f"删除 Worktree 失败: {error_data.get('message', '未知错误')}"}],
                        "is_error": True
                    }

                result = await resp.json()
                return {
                    "content": [{"type": "text", "text": result.get("message", f"Worktree '{name}' 已删除")}]
                }

    except aiohttp.ClientError as e:
        return {
            "content": [{"type": "text", "text": f"无法连接 Server: {str(e)}"}],
            "is_error": True
        }


@mcp_tool()
async def worktree_create_for_ai_job(args: dict[str, Any]) -> dict[str, Any]:
    """
    为 AI 并行任务创建 Worktree（便捷方法）

    自动按照 AI 任务命名规范创建 Worktree：
    - Worktree 名称: ai-{job_id}
    - 分支名称: feat/ai-{job_id}

    Args:
        args: 包含以下字段的字典：
            - job_id: AI 任务 ID（如 "storage", "flow", "zone1"）（必需）

    Returns:
        MCP 响应，包含创建的 Worktree 路径
    """
    job_id = args.get("job_id")

    if not job_id:
        return {
            "content": [{"type": "text", "text": "错误: 必须指定 job_id"}],
            "is_error": True
        }

    # 按 AI 任务命名规范构造名称
    worktree_name = f"ai-{job_id}"
    branch_name = f"feat/ai-{job_id}"

    try:
        async with aiohttp.ClientSession() as session:
            async with session.post(
                f"{SERVER_URL}/api/git/worktrees",
                json={
                    "name": worktree_name,
                    "branchName": branch_name
                }
            ) as resp:
                if resp.status != 200:
                    error_data = await resp.json()
                    return {
                        "content": [{"type": "text", "text": f"创建 AI 任务 Worktree 失败: {error_data.get('message', '未知错误')}"}],
                        "is_error": True
                    }

                result = await resp.json()

                text = f"""AI 任务 Worktree 创建成功:
- 任务 ID: {job_id}
- Worktree 名称: {worktree_name}
- 分支: {branch_name}
- 工作路径: {result.get('path', '?')}

SubAgent 可以在此隔离环境中独立布置，完成后合并回主分支。"""

                return {
                    "content": [{"type": "text", "text": text}]
                }

    except aiohttp.ClientError as e:
        return {
            "content": [{"type": "text", "text": f"无法连接 Server: {str(e)}"}],
            "is_error": True
        }
