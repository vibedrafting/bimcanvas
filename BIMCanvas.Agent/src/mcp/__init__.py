"""MCP 模块 - 为 Agent SDK 提供 MCP 服务

组3 改造 (主真理源 v1.1 §3.10 / §4.6):
- canvas_core.py: 7 个底座工具的工厂函数 (Phase 1 起 main_agent 走这条路)
- canvas.py: indoor-layout 专属工具留原地 (canvas_mcp 单例向后兼容,M2/组5 才物理迁出)

新用法 (组3+):
```python
from ..mcp import build_core_server, CORE_ALLOWED_TOOLS

core_mcp = build_core_server(launch_context, session)
options = ClaudeAgentOptions(
    mcp_servers={"canvas": core_mcp, **plugin_mcps},
    allowed_tools=list(CORE_ALLOWED_TOOLS) + [...],
)
```

旧用法 (兼容期保留,M2 删除):
```python
from ..mcp import canvas_mcp, CANVAS_ALLOWED_TOOLS
```
"""

# Core-base (组3 装配器实际使用)
from .canvas_core import build_core_server, CORE_ALLOWED_TOOLS

# Indoor-layout 兼容导出 (M2 / 组5 物理迁移后删除)
from .canvas import canvas_mcp, CANVAS_ALLOWED_TOOLS

__all__ = [
    "build_core_server",
    "CORE_ALLOWED_TOOLS",
    "canvas_mcp",
    "CANVAS_ALLOWED_TOOLS",
]
