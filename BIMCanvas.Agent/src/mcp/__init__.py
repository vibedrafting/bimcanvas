"""MCP 模块 - 为 Agent SDK 提供轻量级 MCP 服务"""

# === 注释掉现有不工作的 Canvas MCP ===
# from .decorators import mcp_tool, get_registered_tools
# from .server import create_canvas_mcp, get_allowed_tools

# === 使用 Calculator MCP 进行测试 ===
from .calculator import calculator_mcp, CALCULATOR_ALLOWED_TOOLS

__all__ = [
    # "mcp_tool",
    # "get_registered_tools",
    # "create_canvas_mcp",
    # "get_allowed_tools",
    "calculator_mcp",
    "CALCULATOR_ALLOWED_TOOLS",
]
