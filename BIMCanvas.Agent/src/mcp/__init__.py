"""MCP 模块 - 为 Agent SDK 提供轻量级 MCP 服务"""

# Canvas MCP（重构后）
from .canvas import canvas_mcp, CANVAS_ALLOWED_TOOLS

# Calculator MCP（测试用，可选保留）
from .calculator import calculator_mcp, CALCULATOR_ALLOWED_TOOLS

__all__ = [
    "canvas_mcp",
    "CANVAS_ALLOWED_TOOLS",
    "calculator_mcp",
    "CALCULATOR_ALLOWED_TOOLS",
]
