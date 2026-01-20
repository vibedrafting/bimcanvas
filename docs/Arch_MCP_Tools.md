# MCP 框架设计

> **版本**：v1.0 | **更新日期**：2026-01-14
> **目的**：记录 BIMCanvas MCP 框架的技术选型、架构设计和快速上手指南

---

## 一、概述

### 1.1 什么是 MCP

MCP（Model Context Protocol）是 Anthropic 定义的标准协议，用于扩展 AI 模型的工具调用能力。通过 MCP，可以让 Claude 调用自定义函数来完成特定领域的任务。

### 1.2 MCP 在 BIMCanvas 中的定位

```
┌─────────────────────────────────────────────────────────────────┐
│                    BIMCanvas Agent 架构                          │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│   ┌─────────────────────────────────────────────────────────┐   │
│   │              MCP 工具层（领域能力扩展）                    │   │
│   │  通过 @mcp_tool 装饰器定义，自动发现和注册                 │   │
│   └─────────────────────────────────────────────────────────┘   │
│                           ↑ 注入                                │
│   ┌─────────────────────────────────────────────────────────┐   │
│   │              Agent SDK                                    │   │
│   │  ClaudeSDKClient + SDK MCP Server（进程内）               │   │
│   └─────────────────────────────────────────────────────────┘   │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

**核心理念**：MCP 是**能力扩展**（函数调用，无决策），不是 Agent（有决策能力）。

---

## 二、技术选型

### 2.1 MCP Server 类型对比

MCP 协议支持三种 Server 实现方式：

| 类型 | 实现方式 | 优点 | 缺点 |
|------|----------|------|------|
| **stdio** | 外部进程通信 | 语言无关，可复用现有 Server | 需启动子进程，通信开销 |
| **HTTP/SSE** | 远程服务调用 | 支持跨网络、微服务架构 | 网络延迟，需部署服务 |
| **SDK MCP Server** | 进程内直接调用 | 无进程开销，与 SDK 原生集成 | 仅限 Python |

### 2.2 选型决策

**BIMCanvas 选择：SDK MCP Server（进程内）**

**理由**：

1. **简单性**：无需启动外部进程，一行代码创建 Server
2. **性能**：进程内直接函数调用，无 IPC 或网络开销
3. **原生集成**：Agent SDK 内置支持，配置简洁
4. **类型安全**：Python 装饰器提供类型推断

```python
# 对比：stdio 方式需要配置外部命令
mcp_servers={
    "external": {
        "command": "npx",
        "args": ["some-mcp-server"]
    }
}

# SDK MCP Server：进程内直接使用
canvas_mcp = create_canvas_mcp()
mcp_servers={"canvas": canvas_mcp}
```

### 2.3 工具注册机制选型

| 方案 | 说明 | 优缺点 |
|------|------|--------|
| **手动注册** | 显式列出所有工具 | 清晰但繁琐，易遗漏 |
| **装饰器 + 自动发现** | 扫描模块自动注册 | 简洁，新增工具无需修改注册代码 |

**BIMCanvas 选择：装饰器 + 自动发现**

```python
# 只需添加装饰器，工具自动被发现和注册
@mcp_tool()
async def my_tool(args):
    """工具描述"""
    pass
```

---

## 三、框架架构

### 3.1 代码结构

```
BIMCanvas.Agent/src/mcp/
├── __init__.py          # 模块导出
├── decorators.py        # @mcp_tool 装饰器
├── server.py            # MCP Server 工厂
└── tools/               # 工具实现目录
    ├── __init__.py
    ├── tool_a.py        # 各工具实现
    ├── tool_b.py
    └── ...
```

### 3.2 核心组件

#### 装饰器（decorators.py）

```python
_registered_tools: list[Callable] = []

def mcp_tool(name: str = None, description: str = None):
    """
    MCP 工具装饰器

    - 自动从函数名推断工具名（snake_case）
    - 自动从 docstring 推断描述
    """
    def decorator(func: Callable) -> Callable:
        func._mcp_tool_name = name or func.__name__
        func._mcp_tool_description = description or func.__doc__ or ""
        _registered_tools.append(func)
        return func
    return decorator

def get_registered_tools() -> list[Callable]:
    """获取所有已注册的工具"""
    return _registered_tools
```

#### Server 工厂（server.py）

```python
from claude_agent_sdk import create_sdk_mcp_server

def create_canvas_mcp(name: str = "canvas-mcp", version: str = "1.0.0"):
    """
    创建 Canvas MCP 服务器

    自动发现 tools/ 目录下所有带 @mcp_tool 装饰器的函数
    """
    # 自动发现：扫描 tools 包下所有模块
    from . import tools
    for _, module_name, _ in pkgutil.iter_modules(tools.__path__):
        importlib.import_module(f".tools.{module_name}", package=__name__)

    tools_list = get_registered_tools()

    return create_sdk_mcp_server(
        name=name,
        version=version,
        tools=tools_list
    )

def get_allowed_tools() -> list[str]:
    """获取工具名列表，用于 allowed_tools 配置"""
    return [f"mcp__canvas__{f._mcp_tool_name}" for f in get_registered_tools()]
```

### 3.3 与 Agent 集成

```python
from mcp import create_canvas_mcp, get_allowed_tools

# 创建 MCP Server
canvas_mcp = create_canvas_mcp()

# 配置 Agent
options = ClaudeAgentOptions(
    mcp_servers={"canvas": canvas_mcp},
    allowed_tools=["Read", "Write"] + get_allowed_tools()
)

# 使用 ClaudeSDKClient（必需，query() 不支持 MCP）
async with ClaudeSDKClient(options=options) as client:
    await client.query("执行某个任务")
```

---

## 四、快速上手指南

### 4.1 添加新工具

**步骤 1**：在 `mcp/tools/` 下创建 Python 文件

```python
# mcp/tools/my_tool.py
from typing import Any
from ..decorators import mcp_tool

@mcp_tool()
async def my_tool(args: dict[str, Any]) -> dict[str, Any]:
    """
    工具简短描述（会作为 MCP 工具的 description）

    详细说明可以写在这里，但只有第一行会被提取为描述。
    """
    # 从 args 获取参数
    param1 = args.get("param1")
    param2 = args.get("param2", "default_value")

    # 执行业务逻辑
    result = do_something(param1, param2)

    # 返回 MCP 标准响应格式
    return {
        "content": [
            {"type": "text", "text": f"执行结果: {result}"}
        ]
    }
```

**步骤 2**：无需其他操作

工具会被 `create_canvas_mcp()` 自动发现和注册。

### 4.2 MCP 响应格式

```python
# 成功响应
return {
    "content": [
        {"type": "text", "text": "操作成功"},
        {"type": "image", "data": base64_data, "mimeType": "image/png"}  # 可选
    ]
}

# 错误响应
return {
    "content": [{"type": "text", "text": "错误信息"}],
    "is_error": True
}
```

### 4.3 自定义工具名和描述

```python
# 默认：从函数名和 docstring 推断
@mcp_tool()
async def get_room_data(args):
    """获取房间数据"""
    pass
# 工具名: get_room_data, 描述: 获取房间数据

# 自定义
@mcp_tool(name="room_info", description="查询指定房间的详细信息")
async def get_room_data(args):
    pass
# 工具名: room_info, 描述: 查询指定房间的详细信息
```

### 4.4 工具调用命名规则

MCP 工具在 `allowed_tools` 中的命名格式：

```
mcp__{server_name}__{tool_name}
```

示例：
- Server 名称：`canvas`
- 工具名称：`get_room_data`
- 完整名称：`mcp__canvas__get_room_data`

```python
options = ClaudeAgentOptions(
    mcp_servers={"canvas": canvas_mcp},
    allowed_tools=[
        "Read", "Write",
        "mcp__canvas__get_room_data",
        "mcp__canvas__validate_placement"
    ]
)
```

### 4.5 参数 Schema 定义

MCP 工具的参数 Schema 定义决定了 AI 如何理解和调用工具。有两种方案：

#### 方案 A：简单类型映射（推荐用于简单工具）

直接在 `@mcp_tool` 装饰器中指定参数类型：

```python
from ..decorators import mcp_tool

@mcp_tool(
    name="add",
    description="Add two numbers",
    schema={"a": float, "b": float}
)
async def add_numbers(args: dict[str, Any]) -> dict[str, Any]:
    result = args["a"] + args["b"]
    return {"content": [{"type": "text", "text": f"Result: {result}"}]}
```

**支持的类型映射**：
- `str` → `{"type": "string"}`
- `int` → `{"type": "integer"}`
- `float` → `{"type": "number"}`
- `bool` → `{"type": "boolean"}`

**适用场景**：参数简单（2-5个），无需复杂描述

#### 方案 B：完整 JSON Schema（推荐用于复杂工具）

使用完整的 JSON Schema 格式，可为每个参数添加描述：

```python
@mcp_tool(
    name="create_element",
    description="创建元素",
    schema={
        "type": "object",
        "properties": {
            "typeId": {
                "type": "integer",
                "description": "族类型的 ElementId，必须是项目中已加载的有效类型"
            },
            "location": {
                "type": "object",
                "properties": {
                    "x": {"type": "number", "description": "X坐标（毫米）"},
                    "y": {"type": "number", "description": "Y坐标（毫米）"}
                },
                "description": "定位点坐标"
            }
        },
        "required": ["typeId", "location"]
    }
)
async def create_element(args: dict[str, Any]) -> dict[str, Any]:
    type_id = args["typeId"]
    location = args["location"]
    # ...
```

**适用场景**：复杂参数、需要 AI 理解每个字段含义、有嵌套对象

#### Schema 选择指南

| 场景 | 推荐方案 | 示例 |
|------|----------|------|
| 简单参数（1-3个基本类型） | 方案 A | `{"name": str, "count": int}` |
| 需要参数描述 | 方案 B | 字段含义不明显时 |
| 嵌套对象参数 | 方案 B | location 包含 x、y 坐标 |
| 可选参数 | 方案 B | 需要指定 `required` 字段 |

---

## 五、注意事项

### 5.1 仅 ClaudeSDKClient 支持 MCP

```python
# ✅ 正确：使用 ClaudeSDKClient
async with ClaudeSDKClient(options=options) as client:
    await client.query(prompt)

# ❌ 错误：query() 不支持 MCP 工具
result = await query(prompt, options=options)  # MCP 工具不可用
```

### 5.2 异步函数要求

MCP 工具必须是异步函数：

```python
# ✅ 正确
@mcp_tool()
async def my_tool(args):
    pass

# ❌ 错误
@mcp_tool()
def my_tool(args):  # 同步函数
    pass
```

### 5.3 参数通过 args 字典传递

```python
@mcp_tool()
async def my_tool(args: dict[str, Any]) -> dict[str, Any]:
    # Claude 调用时传入的参数在 args 字典中
    room_id = args.get("room_id")
    options = args.get("options", {})
```

---

## 六、相关文档

| 文档 | 说明 |
|------|------|
| [Agent_SDK.md](./Agent_SDK.md) | Agent SDK 技术指南，包含 MCP 使用示例 |
| [Architecture.md](./Architecture.md) | 系统架构总设计 |

---

## 七、版本历史

| 版本 | 日期 | 变更内容 |
|------|------|----------|
| v1.0 | 2026-01-14 | 初始版本：MCP 框架技术选型、架构设计、快速上手指南 |
