"""McpServerBuilder - plugin 作者用来注册 MCP 工具 (主真理源 v1.1 §3.8 / 组3 任务模板 §4.1)。

设计要点:
- 装饰器风格:`@builder.tool(name, description, schema)` 内部包 claude_agent_sdk.tool
- builder.context 暴露 PluginContext 供 register 函数体闭包捕获
- builder.build() 调 claude_agent_sdk.create_sdk_mcp_server,得到 McpServer 实例
- tool_names 暴露 `mcp__{namespace}__{tool}` 形态,供平台聚合 allowed_tools
"""

from __future__ import annotations

from typing import Any, Awaitable, Callable

from claude_agent_sdk import ToolAnnotations, create_sdk_mcp_server, tool as _sdk_tool

from .context import PluginContext


class McpServerBuilder:
    """构造 in-process MCP server 的 builder。

    使用范式 (plugin 作者):

        def register(builder: McpServerBuilder) -> None:
            ctx = builder.context

            @builder.tool("echo", "回显文本", {
                "type": "object",
                "properties": {"text": {"type": "string"}},
                "required": ["text"],
                "additionalProperties": False,
            })
            async def echo(args: dict) -> dict:
                return {"content": [{"type": "text", "text": args["text"]}]}
    """

    def __init__(
        self,
        namespace: str,
        context: PluginContext,
        version: str = "1.0.0",
    ) -> None:
        self.namespace = namespace
        self.context = context
        self.version = version
        self._tools: list[Any] = []

    def tool(
        self,
        name: str,
        description: str,
        schema: dict | type,
        annotations: ToolAnnotations | None = None,
    ) -> Callable[[Callable[[dict], Awaitable[dict]]], Any]:
        """装饰器:把 async fn(args) -> dict 注册为 MCP 工具。

        装饰后的对象是 SdkMcpTool 实例 (来自 claude_agent_sdk),具有 .name 属性。
        装饰器返回 SdkMcpTool 本身 (与 claude_agent_sdk.tool 一致),便于 plugin
        作者可选地引用工具对象做更细控制。
        """
        def decorator(fn: Callable[[dict], Awaitable[dict]]) -> Any:
            decorated = _sdk_tool(name, description, schema, annotations)(fn)
            self._tools.append(decorated)
            return decorated

        return decorator

    def build(self) -> Any:
        """构造 in-process MCP server,可直接挂入 ClaudeAgentOptions.mcp_servers dict。"""
        return create_sdk_mcp_server(
            name=self.namespace,
            version=self.version,
            tools=list(self._tools),
        )

    @property
    def tool_names(self) -> tuple[str, ...]:
        """返回 mcp__{namespace}__{tool_name} 形态,供平台聚合 allowed_tools。"""
        return tuple(f"mcp__{self.namespace}__{t.name}" for t in self._tools)

    @property
    def tools(self) -> tuple[Any, ...]:
        """已注册的工具对象列表 (SdkMcpTool 实例)。"""
        return tuple(self._tools)
