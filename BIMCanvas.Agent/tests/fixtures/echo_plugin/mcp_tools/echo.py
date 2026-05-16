"""Echo plugin entry: 演示 register(builder) 范式与 PluginContext 闭包捕获。"""

from bimcanvas_plugin_sdk import McpServerBuilder


def register(builder: McpServerBuilder) -> None:
    """组3 任务模板 §4.1 入口约定: register(builder) -> None。

    plugin 作者通过 builder.context 闭包捕获 PluginContext,在工具实现中使用
    ctx.server_url / ctx.session / ctx.logger 等。
    """
    ctx = builder.context

    @builder.tool(
        "echo",
        "回显 text 参数,附带当前 active_plugin_id (验证 PluginContext 注入)",
        {
            "type": "object",
            "properties": {
                "text": {"type": "string", "description": "要原样回显的内容"},
            },
            "required": ["text"],
            "additionalProperties": False,
        },
    )
    async def echo(args: dict) -> dict:
        text = args.get("text", "")
        return {
            "content": [
                {
                    "type": "text",
                    "text": (
                        f"[echo-demo] {text}\n"
                        f"(plugin_id={ctx.active_plugin_id}, "
                        f"scene_id={ctx.active_scene_id}, "
                        f"server_url={ctx.server_url})"
                    ),
                }
            ]
        }
