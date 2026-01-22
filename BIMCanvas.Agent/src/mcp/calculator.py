"""Calculator MCP Server - 用于测试 Agent SDK MCP 集成"""

from typing import Any
import aiohttp
from claude_agent_sdk import tool, create_sdk_mcp_server

# Canvas 工具的 Server URL
SERVER_URL = "http://localhost:5000"


@tool("add", "Add two numbers", {"a": float, "b": float})
async def add_numbers(args: dict[str, Any]) -> dict[str, Any]:
    result = args["a"] + args["b"]
    return {"content": [{"type": "text", "text": f"{args['a']} + {args['b']} = {result}"}]}


@tool("subtract", "Subtract one number from another", {"a": float, "b": float})
async def subtract_numbers(args: dict[str, Any]) -> dict[str, Any]:
    result = args["a"] - args["b"]
    return {"content": [{"type": "text", "text": f"{args['a']} - {args['b']} = {result}"}]}


@tool("multiply", "Multiply two numbers", {"a": float, "b": float})
async def multiply_numbers(args: dict[str, Any]) -> dict[str, Any]:
    result = args["a"] * args["b"]
    return {"content": [{"type": "text", "text": f"{args['a']} × {args['b']} = {result}"}]}


@tool("divide", "Divide one number by another", {"a": float, "b": float})
async def divide_numbers(args: dict[str, Any]) -> dict[str, Any]:
    if args["b"] == 0:
        return {
            "content": [{"type": "text", "text": "Error: Division by zero"}],
            "is_error": True,
        }
    result = args["a"] / args["b"]
    return {"content": [{"type": "text", "text": f"{args['a']} ÷ {args['b']} = {result}"}]}


@tool("echo", "回显输入的消息", {"message": str})
async def echo_message(args: dict[str, Any]) -> dict[str, Any]:
    """回显输入消息 - 用于测试自定义工具"""
    message = args.get("message", "")
    return {"content": [{"type": "text", "text": f"Echo: {message}"}]}


@tool("ping_server", "测试 Server 连通性", {"url": str})
async def ping_server(args: dict[str, Any]) -> dict[str, Any]:
    """测试 HTTP 调用 - 验证 aiohttp 异步请求"""
    url = args.get("url", "http://localhost:5000")
    try:
        async with aiohttp.ClientSession() as session:
            async with session.get(f"{url}/health", timeout=aiohttp.ClientTimeout(total=5)) as resp:
                return {"content": [{"type": "text", "text": f"Server responded: {resp.status}"}]}
    except aiohttp.ClientError as e:
        return {"content": [{"type": "text", "text": f"Connection error: {str(e)}"}], "is_error": True}
    except Exception as e:
        return {"content": [{"type": "text", "text": f"Error: {str(e)}"}], "is_error": True}


# ============ Step 6: 最小差异测试工具 ============

@tool("test_api", "Test API call", {"name": str})
async def test_api(args: dict[str, Any]) -> dict[str, Any]:
    """调用与 ai_job_create 相同的 API，但使用简单英文名称和描述"""
    name = args.get("name", "test")
    try:
        async with aiohttp.ClientSession() as session:
            async with session.post(
                f"{SERVER_URL}/api/git/ai-job",
                json={"name": name},
                timeout=aiohttp.ClientTimeout(total=5)
            ) as resp:
                return {"content": [{"type": "text", "text": f"API responded: {resp.status}"}]}
    except Exception as e:
        return {"content": [{"type": "text", "text": f"Error: {str(e)}"}], "is_error": True}


# ============ Canvas 工具（从 canvas.py 迁移）============

@tool("create_job", "Create isolated work environment (Git Worktree)", {"name": str, "base_branch": str})
async def create_job(args: dict[str, Any]) -> dict[str, Any]:
    """Create isolated Git Worktree for SubAgent to work in."""
    name = args.get("name", "")
    base_branch = args.get("base_branch", "")
    # Step 11: 回退到 echo 实现，验证稳定性
    return {"content": [{"type": "text", "text": f"Echo: name={name}, base_branch={base_branch}"}]}


@tool("ai_job_complete", "批量通知 Web 端：指定的 AI Job 已完成，可供用户审查", {"names": list})
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


# 创建 Calculator MCP Server
calculator_mcp = create_sdk_mcp_server(
    name="calculator",
    version="1.0.0",
    tools=[add_numbers, subtract_numbers, multiply_numbers, divide_numbers, echo_message, ping_server, test_api, create_job, ai_job_complete],
)

# 预批准工具列表（注意：别名是 "calc"，不是 "calculator"）
CALCULATOR_ALLOWED_TOOLS = [
    "mcp__calc__add",
    "mcp__calc__subtract",
    "mcp__calc__multiply",
    "mcp__calc__divide",
    "mcp__calc__echo",  # 测试自定义工具
    "mcp__calc__ping_server",  # 测试 HTTP 调用
    "mcp__calc__test_api",  # Step 6: 最小差异测试
    # Canvas 工具（迁移自 canvas.py）
    "mcp__calc__create_job",
    "mcp__calc__ai_job_complete",
]
