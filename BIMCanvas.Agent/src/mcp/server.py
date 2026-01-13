"""MCP 服务器工厂 - 一行代码创建服务器"""
from claude_agent_sdk import create_sdk_mcp_server
from .decorators import get_registered_tools
import importlib
import pkgutil
import logging

logger = logging.getLogger(__name__)


def create_canvas_mcp(name: str = "canvas", version: str = "1.0.0") -> dict:
    """
    创建 Canvas MCP 服务器

    自动发现并注册 tools 包下所有带 @mcp_tool 装饰器的函数。

    Args:
        name: 服务器名称
        version: 服务器版本

    Returns:
        SDK MCP 服务器对象

    用法：
    ```python
    mcp_server = create_canvas_mcp()
    options = ClaudeAgentOptions(
        mcp_servers={"canvas": mcp_server},
        allowed_tools=get_allowed_tools()
    )
    ```
    """
    # 自动导入 tools 包下所有模块
    try:
        from . import tools
        for _, module_name, _ in pkgutil.iter_modules(tools.__path__):
            try:
                importlib.import_module(f".tools.{module_name}", package=__package__)
                logger.debug(f"已加载 MCP 工具模块: {module_name}")
            except ImportError as e:
                logger.warning(f"加载 MCP 工具模块失败 ({module_name}): {e}")
    except ImportError:
        logger.warning("未找到 tools 包")

    tools_list = get_registered_tools()

    if not tools_list:
        raise ValueError("没有发现任何注册的 MCP 工具")

    logger.info(f"创建 MCP 服务器 '{name}' v{version}，共 {len(tools_list)} 个工具")

    return create_sdk_mcp_server(
        name=name,
        version=version,
        tools=tools_list
    )


def get_allowed_tools(server_name: str = "canvas") -> list[str]:
    """
    获取所有已注册工具的 allowed_tools 列表

    Returns:
        ["mcp__canvas__tool1", "mcp__canvas__tool2", ...]
    """
    tools = get_registered_tools()

    allowed = []
    for t in tools:
        # 获取原始函数名
        if hasattr(t, '__wrapped__'):
            tool_name = t.__wrapped__.__name__
        else:
            tool_name = getattr(t, '__name__', 'unknown')
        allowed.append(f"mcp__{server_name}__{tool_name}")

    return allowed
