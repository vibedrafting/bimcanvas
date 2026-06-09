"""McpServerBuilder - plugin 作者用来注册 MCP 工具和 Web Actions。

设计要点:
- 装饰器风格:`@builder.tool(name, description, schema)` 内部包 claude_agent_sdk.tool
- 装饰器风格:`@builder.web_action(name)` 注册 HTTP 可调用动作,供 Web UI 直接触发
- builder.context 暴露 PluginContext 供 register 函数体闭包捕获
- builder.build() 调 claude_agent_sdk.create_sdk_mcp_server,得到 McpServer 实例
- tool_names 暴露 `mcp__{namespace}__{tool}` 形态,供平台聚合 allowed_tools
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Awaitable, Callable

from claude_agent_sdk import ToolAnnotations, create_sdk_mcp_server, tool as _sdk_tool

from .context import PluginContext


@dataclass
class WebAction:
    """插件注册的 HTTP 可调用动作。

    handler 签名: async def handler(data: dict) -> dict
    data 是请求 JSON body；返回 dict 序列化为 JSON 响应。
    """
    name: str
    method: str
    handler: Callable[[dict], Awaitable[dict]]


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
        self._web_actions: list[WebAction] = []

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

    def web_action(
        self,
        name: str,
        method: str = "POST",
    ) -> Callable[[Callable[[dict], Awaitable[dict]]], Callable[[dict], Awaitable[dict]]]:
        """装饰器:把 async fn(data) -> dict 注册为 HTTP 可调用的插件动作。

        注册后可通过 POST /api/plugin-actions/{namespace}/{name} 从 Web UI 触发。
        handler 接收请求 JSON body 作为 data dict，返回 dict 序列化为响应。

        用法:
            @builder.web_action("generate")
            async def generate(data: dict) -> dict:
                return {"imageData": "..."}
        """
        def decorator(fn: Callable[[dict], Awaitable[dict]]) -> Callable[[dict], Awaitable[dict]]:
            self._web_actions.append(WebAction(name=name, method=method, handler=fn))
            return fn
        return decorator

    @property
    def web_actions(self) -> tuple[WebAction, ...]:
        """已注册的 WebAction 列表。"""
        return tuple(self._web_actions)
