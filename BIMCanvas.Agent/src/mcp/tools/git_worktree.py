"""Git Worktree MCP 工具 - 管理 Git Worktree 实现并行开发隔离

两种使用场景：
- 场景 A：并行开发 - 用户多开窗口，检出已有分支，删除时保留分支
- 场景 B：隔离环境 - AI 任务，创建临时分支，删除时连同分支一起删除
"""
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
    创建新的 Git Worktree

    智能判断分支是否存在：
    - 分支已存在 → 检出到 Worktree（场景 A：并行开发）
    - 分支不存在 → 基于 base_branch 创建新分支（场景 B：隔离环境）

    创建前会自动存档：如有未提交更改，Server 会静默执行自动存档。

    Args:
        args: 包含以下字段的字典：
            - name: Worktree 名称（如 "ai-storage"）（必需）
            - branch: 目标分支名（如 "feat/ai-storage"）（必需）
            - base_branch: 基准分支（可选），分支不存在时作为创建起点，默认 HEAD

    Returns:
        MCP 响应，包含创建的 Worktree 信息

    示例:
        场景 A（并行开发，分支已存在）:
            worktree_create({"name": "dev-window", "branch": "scheme/极致收纳"})

        场景 B（隔离环境，创建新分支）:
            worktree_create({
                "name": "ai-job-1",
                "branch": "feat/ai-storage",
                "base_branch": "scheme/极致收纳"
            })
    """
    name = args.get("name")
    branch = args.get("branch")
    base_branch = args.get("base_branch")

    if not name:
        return {
            "content": [{"type": "text", "text": "错误: 必须指定 name"}],
            "is_error": True
        }

    if not branch:
        return {
            "content": [{"type": "text", "text": "错误: 必须指定 branch"}],
            "is_error": True
        }

    try:
        async with aiohttp.ClientSession() as session:
            # 构造请求体
            request_body = {
                "name": name,
                "branch": branch
            }
            if base_branch:
                request_body["baseBranch"] = base_branch

            async with session.post(
                f"{SERVER_URL}/api/git/worktrees",
                json=request_body
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
- 分支: {result.get('branch', branch)}
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
    删除 Git Worktree

    Args:
        args: 包含以下字段的字典：
            - name: Worktree 名称（必需）
            - delete_branch: 是否同时删除关联的分支（可选，默认 false）
                - false: 场景 A（并行开发），保留分支供后续使用
                - true: 场景 B（隔离环境），删除临时分支

    Returns:
        MCP 响应，包含删除结果

    示例:
        场景 A（并行开发，保留分支）:
            worktree_remove({"name": "dev-window"})

        场景 B（隔离环境，删除分支）:
            worktree_remove({"name": "ai-job-1", "delete_branch": true})
    """
    name = args.get("name")
    delete_branch = args.get("delete_branch", False)

    if not name:
        return {
            "content": [{"type": "text", "text": "错误: 必须指定 name"}],
            "is_error": True
        }

    try:
        async with aiohttp.ClientSession() as session:
            # 构造 URL 参数
            url = f"{SERVER_URL}/api/git/worktrees/{name}"
            if delete_branch:
                url += "?deleteBranch=true"

            async with session.delete(url) as resp:
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
