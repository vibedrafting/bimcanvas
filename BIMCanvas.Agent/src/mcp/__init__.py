"""MCP 模块 - 为 Agent SDK 提供轻量级 MCP 服务"""

from .decorators import mcp_tool, get_registered_tools
from .server import create_canvas_mcp, get_allowed_tools

__all__ = [
    "mcp_tool",
    "get_registered_tools",
    "create_canvas_mcp",
    "get_allowed_tools",
]
