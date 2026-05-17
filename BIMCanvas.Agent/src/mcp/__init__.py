"""MCP 模块 - 为 Agent SDK 提供 MCP 服务

组3 改造 (主真理源 v1.1 §3.10 / §4.6):
- canvas_core.py: 9 个底座工具的工厂函数 (组5 §5.A.8 修订:7→9,补齐 ai_job_create + ai_job_complete)
- canvas.py: 9 个 core 工具的 source-of-truth(@tool 装饰器原型定义),由 canvas_core.py 通过 from .canvas import 复用

组5 §5.A.3 物理迁移后:
- 5 个 indoor-layout 专属工具(save/load_semantic_plan、save/load_reference_analysis、clone_scheme_to_variant)
  已迁出到 `Templates/plugins/indoor-layout/mcp_tools/canvas_indoor_layout.py`,通过 `register(builder)` 范式加载
- `canvas_mcp` 单例 + `CANVAS_ALLOWED_TOOLS` 已删除(M1 兼容期结束)
- main_agent.py 已切到 `bundle.mcp_servers_spec`,不再直接 import canvas_mcp

用法:
```python
from ..mcp import build_core_server, CORE_ALLOWED_TOOLS

core_mcp = build_core_server(launch_context, session)
options = ClaudeAgentOptions(
    mcp_servers={"canvas": core_mcp, **plugin_mcps},
    allowed_tools=list(CORE_ALLOWED_TOOLS) + [...],
)
```
"""

from .canvas_core import build_core_server, CORE_ALLOWED_TOOLS

__all__ = [
    "build_core_server",
    "CORE_ALLOWED_TOOLS",
]
