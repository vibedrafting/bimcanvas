"""简化版 MCP 工具装饰器"""
from claude_agent_sdk import tool as sdk_tool
from typing import Any, Callable, get_type_hints

# 全局工具注册表
_registered_tools: list[Callable] = []


def mcp_tool(name: str = None, description: str = None, schema: dict = None):
    """
    MCP 工具装饰器

    特性：
    - 支持显式指定参数 schema（推荐）
    - 或从函数签名推断参数 schema
    - 自动从 docstring 推断描述
    - 自动注册到全局注册表

    用法：
    ```python
    # 方式1：显式指定 schema（推荐，用于 args: dict 签名的工具）
    @mcp_tool(schema={"name": str, "count": int})
    async def my_tool(args: dict[str, Any]) -> dict:
        '''工具描述'''
        return {"content": [{"type": "text", "text": "结果"}]}

    # 方式2：从函数签名推断（用于具名参数的工具）
    @mcp_tool()
    async def my_tool(param1: str, param2: int) -> dict:
        '''工具描述'''
        return {"content": [{"type": "text", "text": "结果"}]}
    ```
    """
    def decorator(func: Callable):
        # 确定最终 schema
        if schema is not None:
            final_schema = schema
        else:
            # 从函数签名推断 schema
            hints = get_type_hints(func)
            hints.pop('return', None)
            # 排除 args 参数（用于 args: dict 签名的工具）
            hints.pop('args', None)

            # Python 类型 → JSON Schema 类型映射
            type_map = {
                str: str,
                int: int,
                float: float,
                bool: bool,
                list: list,
                dict: dict,
            }

            final_schema = {}
            for param_name, param_type in hints.items():
                final_schema[param_name] = type_map.get(param_type, str)

        # 推断名称和描述
        tool_name = name or func.__name__
        tool_desc = description or (func.__doc__ or "").strip().split('\n')[0]

        # 应用 SDK 装饰器
        decorated = sdk_tool(tool_name, tool_desc, final_schema)(func)

        # 注册到全局表
        _registered_tools.append(decorated)

        return decorated

    return decorator


def get_registered_tools() -> list[Callable]:
    """获取所有已注册的工具"""
    return _registered_tools.copy()


def clear_registry():
    """清空注册表（测试用）"""
    _registered_tools.clear()
