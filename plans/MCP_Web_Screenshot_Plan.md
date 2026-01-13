# MCP 服务实现：Web 截图工具

## 需求

实现一个 MCP 工具：`web_screenshot`
- 功能：截取指定 URL 的网页截图
- 输出：保存到项目目录后返回文件路径

---

## 技术选型

| 库 | 浏览器支持 | 异步 | 特点 |
|------|----------|------|------|
| **Playwright** | Chrome/Firefox/WebKit | 原生 | 现代、功能全、推荐 |
| Pyppeteer | 仅 Chromium | 是 | 轻量、Puppeteer 移植 |
| Selenium | 全部 | 否 | 老牌、较慢 |

**推荐**：`playwright` - 现代框架、原生 async、支持全页截图

---

## 实现架构

```
BIMCanvas.Agent/
├── src/
│   ├── mcp/                      # 新增 MCP 模块
│   │   ├── __init__.py           # 导出 mcp_tool 和工厂函数
│   │   ├── decorators.py         # 简化版装饰器
│   │   ├── server.py             # MCP 服务器工厂
│   │   └── tools/
│   │       ├── __init__.py
│   │       └── web_screenshot.py # Web 截图工具
│   └── agent/
│       └── main_agent.py         # 集成 MCP 服务器
```

---

## 核心实现

### 1. 简化版装饰器 (`mcp/decorators.py`)

```python
"""简化版 MCP 工具装饰器"""
from claude_agent_sdk import tool as sdk_tool
from typing import Any, Callable, get_type_hints

# 全局工具注册表
_registered_tools: list[Callable] = []

def mcp_tool(name: str = None, description: str = None):
    """
    简化版 MCP 工具装饰器
    - 自动从函数签名推断参数 schema
    - 自动从 docstring 推断描述
    - 自动注册到全局注册表
    """
    def decorator(func: Callable):
        hints = get_type_hints(func)
        hints.pop('return', None)

        type_map = {str: str, int: int, float: float, bool: bool, list: list, dict: dict}
        schema = {k: type_map.get(v, str) for k, v in hints.items()}

        tool_name = name or func.__name__
        tool_desc = description or (func.__doc__ or "").strip().split('\n')[0]

        decorated = sdk_tool(tool_name, tool_desc, schema)(func)
        _registered_tools.append(decorated)
        return decorated

    return decorator

def get_registered_tools() -> list[Callable]:
    return _registered_tools.copy()
```

### 2. 服务器工厂 (`mcp/server.py`)

```python
"""MCP 服务器工厂"""
from claude_agent_sdk import create_sdk_mcp_server
from .decorators import get_registered_tools
import importlib
import pkgutil

def create_canvas_mcp(name: str = "canvas", version: str = "1.0.0") -> dict:
    """创建 Canvas MCP 服务器（自动发现工具）"""
    # 自动导入 tools 包下所有模块
    try:
        from . import tools
        for _, module_name, _ in pkgutil.iter_modules(tools.__path__):
            importlib.import_module(f".tools.{module_name}", package="src.mcp")
    except ImportError:
        pass

    tools_list = get_registered_tools()
    if not tools_list:
        raise ValueError("没有发现任何注册的 MCP 工具")

    return create_sdk_mcp_server(name=name, version=version, tools=tools_list)

def get_allowed_tools(server_name: str = "canvas") -> list[str]:
    """获取所有已注册工具的 allowed_tools 列表"""
    tools = get_registered_tools()
    return [f"mcp__{server_name}__{getattr(t, '__wrapped__', t).__name__}" for t in tools]
```

### 3. Web 截图工具 (`mcp/tools/web_screenshot.py`)

```python
"""Web 截图 MCP 工具"""
import os
from datetime import datetime
from urllib.parse import urlparse
import re

from ..decorators import mcp_tool

# 项目截图目录（相对于项目根目录）
SCREENSHOT_DIR = "screenshots"

@mcp_tool()
async def web_screenshot(
    project_path: str,
    url: str,
    full_page: bool = True,
    width: int = 1920,
    height: int = 1080
) -> dict:
    """
    截取网页截图并保存到项目目录

    Args:
        project_path: 项目根路径
        url: 要截图的网页 URL
        full_page: 是否截取整个页面（默认 True）
        width: 视口宽度（默认 1920）
        height: 视口高度（默认 1080）

    Returns:
        截图保存路径
    """
    try:
        from playwright.async_api import async_playwright

        # 从 URL 提取域名作为文件名前缀
        parsed = urlparse(url)
        domain = parsed.netloc.replace(":", "_").replace(".", "_")
        # 清理非法字符
        domain = re.sub(r'[<>:"/\\|?*]', '_', domain)

        # 生成文件名：domain_YYYYMMDD_HHMMSS.png
        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        filename = f"{domain}_{timestamp}.png"

        # 保存到项目目录下的 screenshots/ 子目录
        save_dir = os.path.join(project_path, SCREENSHOT_DIR)
        os.makedirs(save_dir, exist_ok=True)
        filepath = os.path.join(save_dir, filename)

        async with async_playwright() as p:
            # 启动 Chromium 浏览器（headless 模式）
            browser = await p.chromium.launch(headless=True)
            page = await browser.new_page(viewport={"width": width, "height": height})

            # 访问页面
            await page.goto(url, wait_until="networkidle")

            # 截图
            await page.screenshot(path=filepath, full_page=full_page)

            await browser.close()

        return {
            "content": [{"type": "text", "text": filepath}]
        }

    except ImportError:
        return {
            "content": [{"type": "text", "text": "错误：需要安装 playwright (pip install playwright && playwright install chromium)"}],
            "is_error": True
        }
    except Exception as e:
        return {
            "content": [{"type": "text", "text": f"截图失败: {str(e)}"}],
            "is_error": True
        }
```

### 4. MainAgent 集成 (`agent/main_agent.py`)

```python
# 在 _create_options 方法中添加：
from ..mcp import create_canvas_mcp, get_allowed_tools

def _create_options(self, thinking_level: str = None) -> ClaudeAgentOptions:
    # ... 现有代码 ...

    # 创建 MCP 服务器
    try:
        canvas_mcp = create_canvas_mcp()
        mcp_tools = get_allowed_tools()
    except ValueError:
        canvas_mcp = None
        mcp_tools = []

    # 合并工具权限
    all_allowed = (allowed_tools or []) + mcp_tools

    return ClaudeAgentOptions(
        # ... 现有配置 ...
        mcp_servers={"canvas": canvas_mcp} if canvas_mcp else {},
        allowed_tools=all_allowed,
    )
```

---

## 实现步骤

1. **安装依赖**：
   ```bash
   pip install playwright
   playwright install chromium
   ```
2. **创建 `src/mcp/` 目录结构**
3. **实现 `decorators.py`**
4. **实现 `server.py`**
5. **实现 `tools/web_screenshot.py`**
6. **修改 `main_agent.py` 集成 MCP**
7. **测试验证**

---

## 关键文件

| 文件 | 操作 | 说明 |
|------|------|------|
| `src/mcp/__init__.py` | 新建 | 导出 mcp_tool 和工厂函数 |
| `src/mcp/decorators.py` | 新建 | 简化版装饰器 |
| `src/mcp/server.py` | 新建 | 服务器工厂 |
| `src/mcp/tools/__init__.py` | 新建 | 工具包初始化 |
| `src/mcp/tools/web_screenshot.py` | 新建 | Web 截图工具 |
| `src/agent/main_agent.py` | 修改 | 集成 MCP 服务器 |

---

## 验证方式

```bash
# 1. 安装依赖
pip install playwright
playwright install chromium

# 2. 启动 Agent 交互模式
python -m src.main --project E:\工作文档\开发类\MyCode\BIMCanvas

# 3. 测试 Web 截图工具
You: 请帮我截取 https://example.com 的网页截图
Agent: [调用 mcp__canvas__web_screenshot 工具]
Agent: 截图已保存到: E:\工作文档\开发类\MyCode\BIMCanvas\screenshots\example_com_20260113_143052.png
```

---

## 参考资料

- [Playwright Python 文档 - Screenshots](https://playwright.dev/python/docs/screenshots)
- [Agent SDK Custom Tools](docs/Agent_SDK/docs/Guides/Custom Tools.md)
