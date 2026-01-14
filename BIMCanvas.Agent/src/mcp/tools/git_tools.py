"""Git 操作 MCP 工具 - 通过 Server REST API 执行 Git 操作"""
from typing import Any
import aiohttp

from ..decorators import mcp_tool

# Server REST API URL
SERVER_URL = "http://localhost:5000"


@mcp_tool()
async def git_status(args: dict[str, Any]) -> dict[str, Any]:
    """
    获取 Git 工作区状态

    返回当前项目的 Git 状态，包括：
    - 是否有未提交的更改
    - 当前分支名

    Args:
        args: 空字典（不需要参数，使用当前加载的项目）

    Returns:
        MCP 响应，包含工作区状态信息
    """
    try:
        async with aiohttp.ClientSession() as session:
            async with session.get(f"{SERVER_URL}/api/git/status") as resp:
                if resp.status != 200:
                    error_data = await resp.json()
                    return {
                        "content": [{"type": "text", "text": f"获取状态失败: {error_data.get('message', '未知错误')}"}],
                        "is_error": True
                    }

                result = await resp.json()

                # 格式化输出
                status_text = f"""Git 工作区状态:
- 项目已加载: {result.get('isLoaded', False)}
- Git 仓库: {result.get('isGitRepo', False)}
- 当前分支: {result.get('currentBranch', '(无)')}
- 有未提交更改: {result.get('hasUncommittedChanges', False)}"""

                return {
                    "content": [{"type": "text", "text": status_text}]
                }

    except aiohttp.ClientError as e:
        return {
            "content": [{"type": "text", "text": f"无法连接 Server: {str(e)}"}],
            "is_error": True
        }


@mcp_tool()
async def git_commit(args: dict[str, Any]) -> dict[str, Any]:
    """
    提交当前更改（存档）

    将当前的更改提交到 Git 仓库。

    Args:
        args: 包含以下字段的字典：
            - message: 提交信息（可选，不传则自动生成）

    Returns:
        MCP 响应，包含提交结果
    """
    message = args.get("message")

    try:
        async with aiohttp.ClientSession() as session:
            async with session.post(
                f"{SERVER_URL}/api/git/commit",
                json={"message": message}
            ) as resp:
                if resp.status != 200:
                    error_data = await resp.json()
                    return {
                        "content": [{"type": "text", "text": f"提交失败: {error_data.get('message', '未知错误')}"}],
                        "is_error": True
                    }

                result = await resp.json()

                if result.get("committed"):
                    commit_info = result.get("commit", {})
                    text = f"""提交成功:
- Hash: {commit_info.get('hash', '?')}
- 信息: {commit_info.get('message', '?')}
- 作者: {commit_info.get('author', '?')}"""
                else:
                    text = "没有需要提交的更改"

                return {
                    "content": [{"type": "text", "text": text}]
                }

    except aiohttp.ClientError as e:
        return {
            "content": [{"type": "text", "text": f"无法连接 Server: {str(e)}"}],
            "is_error": True
        }


@mcp_tool()
async def git_branches(args: dict[str, Any]) -> dict[str, Any]:
    """
    获取分支列表

    返回当前项目的所有 Git 分支。

    Args:
        args: 空字典（不需要参数）

    Returns:
        MCP 响应，包含分支列表
    """
    try:
        async with aiohttp.ClientSession() as session:
            async with session.get(f"{SERVER_URL}/api/git/branches") as resp:
                if resp.status != 200:
                    error_data = await resp.json()
                    return {
                        "content": [{"type": "text", "text": f"获取分支列表失败: {error_data.get('message', '未知错误')}"}],
                        "is_error": True
                    }

                branches = await resp.json()

                if not branches:
                    return {
                        "content": [{"type": "text", "text": "没有分支或项目未加载"}]
                    }

                # 格式化分支列表
                lines = ["Git 分支列表:"]
                for branch in branches:
                    prefix = "* " if branch.get("isCurrent") else "  "
                    commit = branch.get("commit", {})
                    commit_info = f" ({commit.get('hash', '?')}: {commit.get('message', '?')})" if commit else ""
                    lines.append(f"{prefix}{branch.get('name', '?')}{commit_info}")

                return {
                    "content": [{"type": "text", "text": "\n".join(lines)}]
                }

    except aiohttp.ClientError as e:
        return {
            "content": [{"type": "text", "text": f"无法连接 Server: {str(e)}"}],
            "is_error": True
        }


@mcp_tool()
async def git_checkout(args: dict[str, Any]) -> dict[str, Any]:
    """
    切换分支

    切换到指定的 Git 分支，可选自动创建新分支。

    Args:
        args: 包含以下字段的字典：
            - branch_name: 目标分支名（必需）
            - create_if_not_exist: 分支不存在时是否自动创建（默认 False）
            - commit_before_checkout: 切换前是否自动提交更改（默认 False）
            - discard_before_checkout: 切换前是否放弃更改（默认 False）

    Returns:
        MCP 响应，包含切换结果
    """
    branch_name = args.get("branch_name")
    if not branch_name:
        return {
            "content": [{"type": "text", "text": "错误: 必须指定 branch_name"}],
            "is_error": True
        }

    try:
        async with aiohttp.ClientSession() as session:
            async with session.post(
                f"{SERVER_URL}/api/git/checkout",
                json={
                    "branchName": branch_name,
                    "createIfNotExist": args.get("create_if_not_exist", False),
                    "commitBeforeCheckout": args.get("commit_before_checkout", False),
                    "discardBeforeCheckout": args.get("discard_before_checkout", False)
                }
            ) as resp:
                result = await resp.json()

                if resp.status == 409:  # Conflict - 有未提交更改
                    return {
                        "content": [{"type": "text", "text": f"切换失败: {result.get('message', '存在未提交的更改')}\n请先提交或放弃更改"}],
                        "is_error": True
                    }

                if resp.status != 200:
                    return {
                        "content": [{"type": "text", "text": f"切换失败: {result.get('message', '未知错误')}"}],
                        "is_error": True
                    }

                created = "（新建）" if result.get("created") else ""
                return {
                    "content": [{"type": "text", "text": f"已切换到分支: {result.get('currentBranch', branch_name)}{created}"}]
                }

    except aiohttp.ClientError as e:
        return {
            "content": [{"type": "text", "text": f"无法连接 Server: {str(e)}"}],
            "is_error": True
        }


@mcp_tool()
async def git_merge(args: dict[str, Any]) -> dict[str, Any]:
    """
    合并分支到当前分支

    将指定的源分支合并到当前分支。

    Args:
        args: 包含以下字段的字典：
            - source_branch: 要合并的源分支名（必需）
            - commit_message: 合并提交信息（可选）

    Returns:
        MCP 响应，包含合并结果
    """
    source_branch = args.get("source_branch")
    if not source_branch:
        return {
            "content": [{"type": "text", "text": "错误: 必须指定 source_branch"}],
            "is_error": True
        }

    try:
        async with aiohttp.ClientSession() as session:
            async with session.post(
                f"{SERVER_URL}/api/git/merge",
                json={
                    "sourceBranch": source_branch,
                    "commitMessage": args.get("commit_message")
                }
            ) as resp:
                if resp.status != 200:
                    error_data = await resp.json()
                    return {
                        "content": [{"type": "text", "text": f"合并失败: {error_data.get('message', '未知错误')}"}],
                        "is_error": True
                    }

                result = await resp.json()

                if result.get("success"):
                    return {
                        "content": [{"type": "text", "text": f"分支 '{source_branch}' 已成功合并到当前分支"}]
                    }
                elif result.get("hasConflicts"):
                    return {
                        "content": [{"type": "text", "text": f"合并存在冲突，需要手动解决: {result.get('message', '')}"}],
                        "is_error": True
                    }
                else:
                    return {
                        "content": [{"type": "text", "text": f"合并失败: {result.get('message', '未知错误')}"}],
                        "is_error": True
                    }

    except aiohttp.ClientError as e:
        return {
            "content": [{"type": "text", "text": f"无法连接 Server: {str(e)}"}],
            "is_error": True
        }


@mcp_tool()
async def git_discard(args: dict[str, Any]) -> dict[str, Any]:
    """
    放弃所有未提交的更改

    危险操作：将放弃工作区所有未提交的修改。

    Args:
        args: 空字典（不需要参数）

    Returns:
        MCP 响应，包含操作结果
    """
    try:
        async with aiohttp.ClientSession() as session:
            async with session.post(f"{SERVER_URL}/api/git/discard") as resp:
                if resp.status != 200:
                    error_data = await resp.json()
                    return {
                        "content": [{"type": "text", "text": f"放弃更改失败: {error_data.get('message', '未知错误')}"}],
                        "is_error": True
                    }

                result = await resp.json()
                return {
                    "content": [{"type": "text", "text": result.get("message", "已放弃所有更改")}]
                }

    except aiohttp.ClientError as e:
        return {
            "content": [{"type": "text", "text": f"无法连接 Server: {str(e)}"}],
            "is_error": True
        }
