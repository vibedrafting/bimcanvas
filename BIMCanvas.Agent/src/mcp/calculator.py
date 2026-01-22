"""Calculator MCP Server - 用于测试 Agent SDK MCP 集成"""

from typing import Any
import aiohttp
from claude_agent_sdk import tool, create_sdk_mcp_server


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


# 创建 Calculator MCP Server
calculator_mcp = create_sdk_mcp_server(
    name="calculator",
    version="1.0.0",
    tools=[add_numbers, subtract_numbers, multiply_numbers, divide_numbers, echo_message, ping_server],
)

# 预批准工具列表（注意：别名是 "calc"，不是 "calculator"）
CALCULATOR_ALLOWED_TOOLS = [
    "mcp__calc__add",
    "mcp__calc__subtract",
    "mcp__calc__multiply",
    "mcp__calc__divide",
    "mcp__calc__echo",  # 测试自定义工具
    "mcp__calc__ping_server",  # 测试 HTTP 调用
]
