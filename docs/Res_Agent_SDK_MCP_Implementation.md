# Agent SDK MCP 工具集成指南

> **调查日期**：2025-01-22
> **文档版本**：v1.0
> **调查范围**：Anthropic Agent SDK 官方文档、示例代码、源码
> **目的**：为 BIMCanvas 项目的 MainAgent 开发提供 MCP 工具集成参考

---

## 一、概述

### 1.1 什么是 MCP 工具

MCP（Model Context Protocol）是 Anthropic 提供的标准化工具协议，允许 Claude 调用外部工具执行操作。Agent SDK 提供了两种集成方式：

| 方式 | 运行位置 | 适用场景 | 性能 |
|------|----------|----------|------|
| **SDK MCP（进程内）** | Python 进程内 | 自定义业务工具 | ⭐⭐⭐ 最优 |
| **stdio MCP（外部进程）** | 独立子进程 | 第三方成品工具 | ⭐⭐ 良好 |
| **SSE/HTTP MCP（远程）** | 远程服务器 | 远程 API 集成 | ⭐ 有网络开销 |

### 1.2 官方推荐

- **自定义工具**：优先使用 SDK MCP（`@tool` + `create_sdk_mcp_server()`）
- **第三方工具**：使用 stdio MCP 配置
- **远程 API**：使用 SSE/HTTP MCP

---

## 二、SDK MCP（自定义工具）

### 2.1 核心 API

```python
from claude_agent_sdk import tool, create_sdk_mcp_server, ClaudeAgentOptions, ClaudeSDKClient
```

| API | 作用 |
|-----|------|
| `@tool(name, description, input_schema)` | 装饰器，定义工具 |
| `create_sdk_mcp_server(name, version, tools)` | 创建 MCP Server |
| `ClaudeAgentOptions(mcp_servers, allowed_tools)` | 配置 Agent |
| `ClaudeSDKClient(options)` | 创建 Client |

### 2.2 完整代码示例

```python
from claude_agent_sdk import tool, create_sdk_mcp_server, ClaudeAgentOptions, ClaudeSDKClient
from typing import Any

# ═══════════════════════════════════════════════════════════
# 步骤 1：使用 @tool 装饰器定义工具
# ═══════════════════════════════════════════════════════════

@tool(
    "add",                              # 工具名称（影响调用名）
    "Add two numbers together",         # 描述（AI 可见，用于选择工具）
    {"a": float, "b": float}            # 参数 Schema（简单字典格式）
)
async def add_numbers(args: dict[str, Any]) -> dict[str, Any]:
    """加法工具"""
    result = args["a"] + args["b"]
    return {
        "content": [{"type": "text", "text": f"{args['a']} + {args['b']} = {result}"}]
    }


@tool("divide", "Divide a by b", {"a": float, "b": float})
async def divide_numbers(args: dict[str, Any]) -> dict[str, Any]:
    """除法工具（含错误处理）"""
    if args["b"] == 0:
        return {
            "content": [{"type": "text", "text": "Error: Division by zero"}],
            "is_error": True  # ⭐ 标记为错误，AI 会调整策略
        }
    result = args["a"] / args["b"]
    return {"content": [{"type": "text", "text": f"Result: {result}"}]}


# ═══════════════════════════════════════════════════════════
# 步骤 2：创建 SDK MCP Server
# ═══════════════════════════════════════════════════════════

calculator = create_sdk_mcp_server(
    name="calculator",      # Server 名称（不影响工具调用名）
    version="1.0.0",        # 版本号
    tools=[                 # 工具列表（传入装饰后的函数）
        add_numbers,
        divide_numbers,
    ],
)


# ═══════════════════════════════════════════════════════════
# 步骤 3：配置 Agent 并使用
# ═══════════════════════════════════════════════════════════

async def main():
    options = ClaudeAgentOptions(
        mcp_servers={
            "calc": calculator  # ⭐ 别名（key）决定工具调用名
        },
        allowed_tools=[         # ⭐ 预批准工具列表（必须配置）
            "mcp__calc__add",
            "mcp__calc__divide",
        ],
    )

    async with ClaudeSDKClient(options=options) as client:
        await client.query("Calculate 15 + 27")
        async for message in client.receive_response():
            print(message)
```

### 2.3 工具命名规范 ⭐⭐⭐

**格式**：`mcp__{server_alias}__{tool_name}`

| 组成部分 | 来源 | 示例 |
|---------|------|------|
| `mcp__` | 固定前缀 | `mcp__` |
| `{server_alias}` | `mcp_servers` 字典的 **key** | `calc` |
| `{tool_name}` | `@tool()` 的第一个参数 | `add` |
| **完整名称** | - | `mcp__calc__add` |

**注意**：`create_sdk_mcp_server(name="calculator")` 的 `name` 参数 **不影响** 工具调用名！

```python
# 示例对照
create_sdk_mcp_server(name="calculator", ...)  # name="calculator" 不影响调用名
mcp_servers={"calc": calculator}               # "calc" 影响调用名
@tool("add", ...)                              # "add" 影响调用名

# 最终调用名
"mcp__calc__add"  # ✅ 正确
"mcp__calculator__add"  # ❌ 错误！
```

### 2.4 支持的参数类型

**简单字典格式**（推荐）：

| Python 类型 | JSON Schema 类型 | 示例 |
|-------------|------------------|------|
| `str` | `"string"` | `{"name": str}` |
| `int` | `"integer"` | `{"count": int}` |
| `float` | `"number"` | `{"price": float}` |
| `bool` | `"boolean"` | `{"enabled": bool}` |
| `dict` | `"object"` | `{"config": dict}` |

**完整 JSON Schema 格式**（复杂场景）：

```python
@tool(
    "place_module",
    "Place furniture module",
    {
        "type": "object",
        "properties": {
            "moduleId": {"type": "string"},
            "position": {
                "type": "object",
                "properties": {
                    "x": {"type": "number"},
                    "y": {"type": "number"}
                },
                "required": ["x", "y"]
            },
            "facing": {"type": "string", "enum": ["north", "south", "east", "west"]}
        },
        "required": ["moduleId", "position", "facing"]
    }
)
async def place_module(args: dict) -> dict:
    # ...
```

### 2.5 返回值格式

**标准成功响应**：

```python
return {
    "content": [
        {"type": "text", "text": "结果文本"},
        {"type": "image", "data": "base64编码", "mimeType": "image/png"}
    ]
}
```

**错误响应**：

```python
return {
    "content": [{"type": "text", "text": "Error: 错误信息"}],
    "is_error": True  # ⭐ 标记为错误
}
```

**支持的 Content 类型**：

| 类型 | 格式 |
|------|------|
| TextContent | `{"type": "text", "text": str}` |
| ImageContent | `{"type": "image", "data": str, "mimeType": str}` |

---

## 三、stdio MCP（第三方工具）

### 3.1 代码配置方式

```python
options = ClaudeAgentOptions(
    mcp_servers={
        # 文件系统工具
        "filesystem": {
            "command": "python",
            "args": ["-m", "mcp_server_filesystem"],
            "env": {
                "ALLOWED_PATHS": "/Users/me/projects"
            }
        },
        # 数据库工具
        "database": {
            "command": "python",
            "args": ["-m", "mcp_server_database"],
            "env": {
                "DB_CONNECTION": "postgresql://..."
            }
        }
    },
    allowed_tools=[
        "mcp__filesystem__list_files",
        "mcp__filesystem__read_file",
        "mcp__database__query",
    ]
)
```

### 3.2 .mcp.json 配置文件

在项目根目录创建 `.mcp.json`：

```json
{
  "mcpServers": {
    "filesystem": {
      "command": "python",
      "args": ["-m", "mcp_server_filesystem"],
      "env": {
        "ALLOWED_PATHS": "/path/to/project"
      }
    },
    "database": {
      "command": "python",
      "args": ["-m", "mcp_server_database"]
    }
  }
}
```

代码中加载：

```python
options = ClaudeAgentOptions(
    mcp_servers="/path/to/.mcp.json"  # 直接传文件路径
)
```

### 3.3 SSE/HTTP 远程服务

```python
options = ClaudeAgentOptions(
    mcp_servers={
        "remote-api": {
            "type": "sse",
            "url": "https://api.example.com/mcp/sse",
            "headers": {
                "Authorization": "Bearer ${API_TOKEN}"  # 支持环境变量
            }
        }
    }
)
```

**环境变量语法**：
- `${VAR}` - 必需变量
- `${VAR:-default}` - 带默认值

---

## 四、权限控制

### 4.1 三种权限模式

| 配置 | 效果 |
|------|------|
| `allowed_tools=["mcp__calc__add"]` | ✅ 白名单，预批准自动执行 |
| `disallowed_tools=["mcp__calc__delete"]` | ❌ 黑名单，禁止执行 |
| 无配置 | ⚠️ 每次调用需用户交互确认 |

### 4.2 推荐配置

```python
# 开发环境：全部预批准
options = ClaudeAgentOptions(
    mcp_servers={"canvas": canvas_mcp},
    allowed_tools=[
        "mcp__canvas__get_room_zones",
        "mcp__canvas__place_module",
        "mcp__canvas__validate_placement",
    ],
)

# 生产环境：只读操作预批准，写操作需确认
options = ClaudeAgentOptions(
    mcp_servers={"canvas": canvas_mcp},
    allowed_tools=["mcp__canvas__get_room_zones"],  # 只读
    # place_module 需交互确认
)
```

---

## 五、BIMCanvas 集成方案

### 5.1 工具定义

```python
# BIMCanvas.Agent/mcp_tools.py

from claude_agent_sdk import tool, create_sdk_mcp_server
from typing import Any
import httpx
import json


@tool(
    "get_room_zones",
    "获取房间可布置区域，返回 innerBoundary 和 exclusionAreas",
    {"roomId": str}
)
async def get_room_zones(args: dict[str, Any]) -> dict[str, Any]:
    """获取房间区域数据"""
    async with httpx.AsyncClient() as client:
        response = await client.get(
            f"http://localhost:5000/api/canvas/rooms/{args['roomId']}/zones"
        )
    return {"content": [{"type": "text", "text": response.text}]}


@tool(
    "place_module",
    "放置家具模块到指定位置",
    {
        "moduleId": str,
        "bounds": dict,   # {"x": float, "y": float, "width": float, "height": float}
        "facing": str,    # "north" | "south" | "east" | "west" 或 Vec2D
    }
)
async def place_module(args: dict[str, Any]) -> dict[str, Any]:
    """放置家具模块"""
    async with httpx.AsyncClient() as client:
        response = await client.post(
            "http://localhost:5000/api/canvas/modules",
            json=args
        )
    if response.status_code != 200:
        return {
            "content": [{"type": "text", "text": f"Error: {response.text}"}],
            "is_error": True
        }
    return {"content": [{"type": "text", "text": response.text}]}


@tool(
    "validate_placement",
    "验证模块放置是否合法（不与禁区/其他模块重叠）",
    {"moduleId": str, "bounds": dict}
)
async def validate_placement(args: dict[str, Any]) -> dict[str, Any]:
    """验证放置合法性"""
    async with httpx.AsyncClient() as client:
        response = await client.post(
            "http://localhost:5000/api/canvas/validate",
            json=args
        )
    result = response.json()
    return {"content": [{"type": "text", "text": json.dumps(result)}]}


@tool(
    "get_project",
    "获取当前项目完整数据",
    {}
)
async def get_project(args: dict[str, Any]) -> dict[str, Any]:
    """获取项目数据"""
    async with httpx.AsyncClient() as client:
        response = await client.get("http://localhost:5000/api/canvas/project")
    return {"content": [{"type": "text", "text": response.text}]}


# 创建 Canvas-MCP Server
canvas_mcp = create_sdk_mcp_server(
    name="bimcanvas",
    version="1.0.0",
    tools=[
        get_room_zones,
        place_module,
        validate_placement,
        get_project,
    ],
)
```

### 5.2 MainAgent 配置

```python
# BIMCanvas.Agent/main_agent.py

from claude_agent_sdk import ClaudeSDKClient, ClaudeAgentOptions
from .mcp_tools import canvas_mcp


async def start_main_agent():
    options = ClaudeAgentOptions(
        mcp_servers={
            "canvas": canvas_mcp,  # SDK MCP（自定义业务工具）
        },
        allowed_tools=[
            "mcp__canvas__get_room_zones",
            "mcp__canvas__place_module",
            "mcp__canvas__validate_placement",
            "mcp__canvas__get_project",
        ],
        system_prompt="""你是 BIMCanvas 布置助手，负责在建筑平面内布置家具。

## 工作流程
1. 调用 get_project 获取项目数据
2. 调用 get_room_zones 获取目标房间的可布置区域
3. 规划家具布置方案
4. 调用 validate_placement 验证方案
5. 调用 place_module 执行布置

## 约束
- 模块 bounds 必须完全在 innerBoundary 内
- 模块 bounds 不能与 exclusionAreas 重叠
- 模块 bounds 不能与其他已放置模块重叠
""",
    )

    async with ClaudeSDKClient(options=options) as client:
        await client.query("在客厅布置沙发和茶几")
        async for msg in client.receive_response():
            await process_message(msg)
```

### 5.3 工具调用名速查表

| 工具函数 | 调用名 |
|----------|--------|
| `get_room_zones` | `mcp__canvas__get_room_zones` |
| `place_module` | `mcp__canvas__place_module` |
| `validate_placement` | `mcp__canvas__validate_placement` |
| `get_project` | `mcp__canvas__get_project` |

---

## 六、常见错误与排查

### 6.1 工具函数必须是 async

```python
# ❌ 错误：同步函数
@tool("add", "Add", {"a": float})
def add(args):
    return {"content": [...]}

# ✅ 正确：异步函数
@tool("add", "Add", {"a": float})
async def add(args: dict[str, Any]) -> dict[str, Any]:
    return {"content": [...]}
```

### 6.2 工具调用名不匹配

```python
# ❌ 错误：使用 Server name
mcp_servers={"calc": calculator}
allowed_tools=["mcp__calculator__add"]  # 应该用别名 calc

# ✅ 正确：使用 mcp_servers 的 key
allowed_tools=["mcp__calc__add"]
```

### 6.3 返回值格式错误

```python
# ❌ 错误：返回字符串
return "Result: 42"

# ❌ 错误：缺少 content 数组
return {"text": "Result: 42"}

# ✅ 正确格式
return {"content": [{"type": "text", "text": "Result: 42"}]}
```

### 6.4 未预批准工具

```python
# ❌ 错误：工具未在 allowed_tools 中
options = ClaudeAgentOptions(
    mcp_servers={"calc": calculator},
    # 未配置 allowed_tools，工具无法自动执行
)

# ✅ 正确：明确预批准
options = ClaudeAgentOptions(
    mcp_servers={"calc": calculator},
    allowed_tools=["mcp__calc__add"],
)
```

### 6.5 AI 输出 XML 文本模拟工具调用 ⭐⭐⭐

**问题现象**：

AI 不是真正调用工具，而是输出类似以下的**纯文本**：

```
<mcp__calc__create_job>
  <name>test-job</name>
  <base_branch>main</base_branch>
</mcp__calc__create_job>

我已经为您创建了... (编造的结果)
```

**诊断方法**：

| 特征 | 真正调用 | 文本模拟 |
|------|----------|----------|
| 日志格式 | `[TOOL] mcp__calc__xxx` | 无 `[TOOL]` 标记 |
| Web 端显示 | 工具调用控件 | 纯文本消息 |
| 结果来源 | Server API 返回 | AI 编造 |
| 参数值 | 可能包含上下文信息 | 通常是默认值或猜测 |

**根本原因**：

AI 模型认为"模拟调用 + 给出答案"在某些情况下是可接受的行为。系统提示词未明确禁止此行为。

**解决方案**：

在 `system_prompt` 中添加 MCP 工具使用规范：

```python
system_prompt = """
你是 BIMCanvas 布置助手...

## MCP 工具使用规范

### 强制要求
当需要使用 MCP 工具（以 `mcp__` 开头的工具）时，你**必须**：
1. **真正调用工具** - 使用正确的工具调用格式
2. **等待工具返回** - 不要预测或编造结果

### 禁止行为
你**绝对不能**：
1. 输出 `<mcp__xxx>...</mcp__xxx>` 格式的**文本**来模拟工具调用
2. 自己计算或编造工具应该返回的结果
3. 在工具调用前就给出"结果"

### 判断标准
- ✅ 正确：调用工具 → 收到结果 → 向用户展示
- ❌ 错误：输出 XML 文本 → 自己编造结果 → 向用户展示
"""
```

**验证结果**：

添加此规范后，工具调用成功率从 ~20% 提升到 **100%**。

**经验总结**：

| 教训 | 说明 |
|------|------|
| MCP 注册 ≠ 调用成功 | 工具能注册不代表会被正确调用 |
| 模型行为需要约束 | 系统提示词要明确禁止不良行为 |
| 用户措辞影响模型 | "测试 X"触发真正调用，其他措辞可能不行 |
| 代码问题 vs 模型问题 | 要区分是代码 bug 还是模型行为选择 |

> 📋 详细排查过程见 `reports/MCP_Tool_Call_Issue_Report_20250122.md`

---

## 七、调试技巧

### 7.1 记录工具执行

```python
@tool("add", "Add numbers", {"a": float, "b": float})
async def add_numbers(args: dict[str, Any]) -> dict[str, Any]:
    print(f"[DEBUG] add_numbers called with {args}")  # 调试日志
    result = args["a"] + args["b"]
    print(f"[DEBUG] add_numbers result: {result}")
    return {"content": [{"type": "text", "text": str(result)}]}
```

### 7.2 使用执行跟踪

```python
executions = []  # 全局列表

@tool("echo", "Echo text", {"text": str})
async def echo_tool(args: dict[str, Any]) -> dict[str, Any]:
    executions.append({"tool": "echo", "args": args})  # 记录执行
    return {"content": [{"type": "text", "text": args["text"]}]}

# 验证工具是否被调用
assert "echo" in [e["tool"] for e in executions]
```

### 7.3 检查 MCP 连接状态

```python
async for message in client.receive_response():
    # 检查初始化消息
    if message.get("type") == "system" and message.get("subtype") == "init":
        mcp_servers = message.get("mcp_servers", [])
        for server in mcp_servers:
            if server.get("status") != "connected":
                print(f"⚠️ MCP Server 连接失败: {server['name']}")
```

---

## 八、架构要点总结

### 8.1 SDK MCP 内部机制

```
┌──────────────────────────────────────────┐
│ ClaudeAgentOptions                       │
│   mcp_servers: {"calc": calculator}      │
└───────────────────────────────────────┬──┘
                                        │
                                        ▼
                ┌───────────────────────────┐
                │ Query 对象                 │
                │ sdk_mcp_servers: {...}    │ ← 持有 Server 实例
                └───────────────────────────┘
                                        │
                                        ▼
                ┌───────────────────────────┐
                │ MCP Server (in-process)   │
                │  - list_tools()           │ ← 暴露工具列表
                │  - call_tool()            │ ← 执行工具调用
                └───────────────────────────┘
                                        │
                                        ▼
                ┌───────────────────────────┐
                │ Tool Handler              │
                │ async def handler(args)   │
                └───────────────────────────┘
```

### 8.2 执行链路

```
Claude 请求调用工具
    ↓
Query 对象接收请求
    ↓
从 sdk_mcp_servers 获取 Server 实例
    ↓
调用 Server.call_tool(name, arguments)
    ↓
执行用户定义的 handler 函数
    ↓
转换结果为 MCP 格式
    ↓
返回给 Claude
```

### 8.3 关键特性

| 特性 | 说明 |
|------|------|
| **In-Process** | Server 在 Python 进程内运行，无 IPC 开销 |
| **直接调用** | Query 直接调用 Server 方法，无序列化 |
| **类型安全** | 支持简单字典和完整 JSON Schema |
| **错误标记** | `is_error: True` 让 AI 知道需要调整策略 |

---

## 九、参考资源

### 9.1 官方文档路径

| 文档 | 路径 |
|------|------|
| MCP 集成说明 | `docs/agent_sdk/docs/MCP in the SDK.md` |
| Python SDK 概览 | `docs/agent_sdk/docs/Python SDK.md` |
| 计算器示例 | `docs/agent_sdk/examples/mcp_calculator.py` |
| 测试用例 | `docs/agent_sdk/examples/e2e-tests/test_sdk_mcp_tools.py` |

### 9.2 核心 API 速查

```python
# 工具定义
@tool(name: str, description: str, input_schema: dict | type)

# Server 创建
create_sdk_mcp_server(name: str, version: str, tools: list[SdkMcpTool])

# Agent 配置
ClaudeAgentOptions(
    mcp_servers: dict[str, McpServerConfig],
    allowed_tools: list[str],
    disallowed_tools: list[str],
    system_prompt: str | dict,
)

# Client 使用
async with ClaudeSDKClient(options=options) as client:
    await client.query(prompt)
    async for message in client.receive_response():
        ...
```

---

## 十、Checklist

开发 MCP 工具时的检查清单：

- [ ] 工具函数使用 `async def`
- [ ] 工具函数返回 `{"content": [...]}`
- [ ] 错误响应包含 `"is_error": True`
- [ ] 工具调用名格式正确：`mcp__{别名}__{工具名}`
- [ ] `allowed_tools` 已配置预批准工具
- [ ] 参数类型在支持范围内（str/int/float/bool/dict）
- [ ] 工具描述清晰，帮助 AI 正确选择

---

**文档完成**。后续开发 BIMCanvas.Agent 时，可直接参考本文档配置 MCP 工具。
