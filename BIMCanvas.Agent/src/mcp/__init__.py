"""MCP 模块 - 为 Agent SDK 提供 MCP 服务

架构：
- canvas.py: 业务工具（Canvas 操作、AI Job 管理）
- test_mcp.py: 测试工具（计算器、ping 等）

使用方式：
```python
from ..mcp import canvas_mcp, CANVAS_ALLOWED_TOOLS, test_mcp, TEST_ALLOWED_TOOLS

options = ClaudeAgentOptions(
    mcp_servers={
        "canvas": canvas_mcp,  # 业务工具
        "test": test_mcp,      # 测试工具（可选）
    },
    allowed_tools=CANVAS_ALLOWED_TOOLS + TEST_ALLOWED_TOOLS
)
```
"""

# Canvas MCP（业务工具）
from .canvas import canvas_mcp, CANVAS_ALLOWED_TOOLS

# Test MCP（测试工具）
from .test_mcp import test_mcp, TEST_ALLOWED_TOOLS

__all__ = [
    "canvas_mcp",
    "CANVAS_ALLOWED_TOOLS",
    "test_mcp",
    "TEST_ALLOWED_TOOLS",
]
