"""BIMCanvas Plugin SDK - 平台稳定对外面 (主真理源 v1.1 §3.4 / 组3 任务模板 §4.1)。

Plugin 作者只 import 这一个包,不应触达 src.* 平台内部模块:

    from bimcanvas_plugin_sdk import McpServerBuilder, PluginContext

入口约定 (每个 plugin 的 mcp_tools/<entry>.py 必须暴露):

    def register(builder: McpServerBuilder) -> None:
        ctx = builder.context  # 闭包捕获

        @builder.tool("my_tool", "描述", {"type":"object", ...})
        async def my_tool(args: dict) -> dict:
            ...
            return {"content": [{"type": "text", "text": "..."}]}

Phase 1 稳定承诺 (主真理源 §7.4):
- 4 类公开能力 (McpServerBuilder / PluginContext / @tool 装饰器 / register 入口约定)
  在 Phase 1 不变
- 不暴露 LaunchMode / TrustMode / Lock / ConfigBundle 等平台内部类型
- 新字段加 Phase 2
"""

from .builder import McpServerBuilder
from .context import PluginContext
from .exceptions import (
    LaunchContextError,
    PluginManifestError,
    PluginRegisterError,
)

__all__ = [
    "McpServerBuilder",
    "PluginContext",
    "LaunchContextError",
    "PluginManifestError",
    "PluginRegisterError",
]
