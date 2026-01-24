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

**⚠️ 注意**：`create_sdk_mcp_server(name="...")` 的 `name` 参数 **不影响** 工具调用名！

**真正影响调用名的是 `mcp_servers` 字典的 key**：

```python
# 示例对照
create_sdk_mcp_server(name="calculator", ...)  # ← name="calculator" 不影响调用名
mcp_servers={"calc": calculator}               # ← "calc" 影响调用名
@tool("add", ...)                              # ← "add" 影响调用名

# 最终调用名
✅ "mcp__calc__add"        # 正确（使用字典 key "calc"）
❌ "mcp__calculator__add"  # 错误（不使用 Server name "calculator"）
```

**完整示例**：

```python
# 创建 Server
calculator = create_sdk_mcp_server(
    name="calculator",  # ← 仅用于日志、元数据
    tools=[add_numbers, divide_numbers],
)

# 配置 Agent
options = ClaudeAgentOptions(
    mcp_servers={
        "calc": calculator  # ⭐ "calc" 决定工具调用名
    },
    allowed_tools=[         # ⭐ 使用 "calc"，而非 "calculator"
        "mcp__calc__add",
        "mcp__calc__divide",
    ],
)
```

**验证方法**：

查看启动日志或故意配置错误的白名单，观察错误信息中的实际工具名：

```python
# 故意配置错误
allowed_tools=["mcp__wrong__add"]

# 错误信息会提示实际工具名
# Error: Tool mcp__calc__add requires approval
#        ^^^^^^^^^^^^^^^^ 实际名称
```

### 2.4 支持的参数类型

#### 简单字典格式

**支持的类型映射**：

| Python 类型 | JSON Schema 类型 | 示例 |
|-------------|------------------|------|
| `str` | `"string"` | `{"name": str}` |
| `int` | `"integer"` | `{"count": int}` |
| `float` | `"number"` | `{"price": float}` |
| `bool` | `"boolean"` | `{"enabled": bool}` |
| `dict` | `"object"` | `{"config": dict}` |

**示例**：

```python
@tool(
    "add",
    "Add two numbers",
    {"a": float, "b": float}  # ← 简单字典格式
)
async def add_numbers(args: dict[str, Any]) -> dict[str, Any]:
    result = args["a"] + args["b"]
    return {"content": [{"type": "text", "text": f"Result: {result}"}]}
```

#### Schema 生成机制

**SDK 内部处理逻辑**（来源：`claude_agent_sdk/__init__.py:227-253`）：

```python
@server.list_tools()
async def list_tools() -> list[Tool]:
    """Return the list of available tools."""
    for tool_def in tools:
        # 检查是否已是完整 JSON Schema
        if isinstance(tool_def.input_schema, dict):
            if "type" in tool_def.input_schema and "properties" in tool_def.input_schema:
                # ✅ 已是完整 Schema，直接使用
                schema = tool_def.input_schema
            else:
                # ⭐ 简单字典 → JSON Schema 转换逻辑
                properties = {}
                for param_name, param_type in tool_def.input_schema.items():
                    if param_type is str:
                        properties[param_name] = {"type": "string"}
                    elif param_type is int:
                        properties[param_name] = {"type": "integer"}
                    elif param_type is float:
                        properties[param_name] = {"type": "number"}
                    elif param_type is bool:
                        properties[param_name] = {"type": "boolean"}
                    else:
                        properties[param_name] = {"type": "string"}  # 默认

                # ⭐ 生成基础 Schema（仅包含核心字段）
                schema = {
                    "type": "object",
                    "properties": properties,
                    "required": list(properties.keys()),  # 所有参数默认必填
                }
                # ❌ 未添加 $schema、additionalProperties、参数描述等字段
```

**生成的 Schema 缺失字段**：

| 字段 | 状态 | 原因 |
|------|------|------|
| `$schema` | ❌ 缺失 | SDK 未生成（MCP 协议不强制要求） |
| `additionalProperties` | ❌ 缺失 | SDK 默认行为未设置 |
| `description`（参数级别） | ❌ 缺失 | 简单字典格式不支持参数描述 |
| `minimum`/`maximum` | ❌ 缺失 | 简单字典格式不支持高级约束 |
| `default` | ❌ 缺失 | 简单字典格式不支持默认值 |
| `required` | ✅ 生成 | 默认所有参数必填 |
| `type`、`properties` | ✅ 生成 | 核心字段，自动转换 |

#### SDK 内置工具 vs MCP 工具对比

| 特性 | SDK 内置工具 | MCP 工具（简单字典） | MCP 工具（完整 Schema） |
|------|-------------|-------------------|---------------------|
| **Schema 生成方式** | CLI 内置定义（TypeScript） | Python SDK 动态生成 | 用户提供完整 Schema |
| **Schema 完整度** | ✅ 完整（含所有字段） | ⚠️ 基础（仅核心字段） | ✅ 完整（用户自定义） |
| `$schema` | ✅ 有 | ❌ 无 | ✅ 可指定 |
| `properties.*.description` | ✅ 支持 | ❌ 不支持 | ✅ 支持 |
| `additionalProperties` | ✅ 有 | ❌ 无 | ✅ 可指定 |
| `minimum`/`maximum` | ✅ 支持 | ❌ 不支持 | ✅ 支持 |
| `default` | ✅ 支持 | ❌ 不支持 | ✅ 支持 |
| **控制粒度** | 固定（CLI 硬编码） | 有限（类型映射） | 灵活（可自定义） |
| **AI 理解度** | 高 | 中 | 高 |

#### 完整 JSON Schema 格式（复杂场景）

**适用场景**：复杂参数、需要详细文档、有嵌套对象

```python
@tool(
    "place_module",
    "Place furniture module",
    {
        "$schema": "http://json-schema.org/draft-07/schema#",
        "type": "object",
        "properties": {
            "moduleId": {
                "type": "string",
                "description": "家具模块的唯一标识符"
            },
            "position": {
                "type": "object",
                "description": "定位点坐标（单位：毫米）",
                "properties": {
                    "x": {"type": "number", "description": "X坐标"},
                    "y": {"type": "number", "description": "Y坐标"}
                },
                "required": ["x", "y"]
            },
            "facing": {
                "type": "string",
                "description": "朝向（8方向）",
                "enum": ["north", "south", "east", "west", "northeast", "southeast", "southwest", "northwest"]
            }
        },
        "required": ["moduleId", "position", "facing"],
        "additionalProperties": False
    }
)
async def place_module(args: dict) -> dict:
    # ...
```

**优势对比**：

| 维度 | 简单字典 | 完整 Schema | 改进 |
|------|---------|------------|------|
| **参数描述** | ❌ 无 | ✅ 详细说明 | AI 更好理解参数用途 |
| **参数约束** | ❌ 仅代码验证 | ✅ Schema 级别约束 | 类型安全，自动验证 |
| **默认值** | ❌ 代码硬编码 | ✅ Schema 声明 | 更清晰的默认行为 |
| **额外属性** | ⚠️ 允许 | ✅ 禁止 | 防止参数错误 |
| **Schema 版本** | ❌ 无 | ✅ draft-07 | 明确 Schema 规范 |
| **代码行数** | 5 行 | 20 行 | 更详细但更规范 |

**选择策略**：

| 工具类型 | 推荐方案 | 理由 |
|----------|---------|------|
| **简单工具**<br>（1-2个参数，无复杂约束） | 简单字典<br>`{"param": int}` | 代码简洁，快速开发 |
| **复杂工具**<br>（多参数、嵌套对象、需要验证） | 完整 JSON Schema | 类型安全，提供更好的 AI 提示 |
| **面向用户的工具**<br>（需要详细文档） | 完整 JSON Schema | 参数描述帮助 AI 理解用法 |

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

## 五、常见错误与排查

### 5.1 工具函数必须是 async

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

### 5.2 工具调用名不匹配

```python
# ❌ 错误：使用 Server name
mcp_servers={"calc": calculator}
allowed_tools=["mcp__calculator__add"]  # 应该用别名 calc

# ✅ 正确：使用 mcp_servers 的 key
allowed_tools=["mcp__calc__add"]
```

### 5.3 返回值格式错误

```python
# ❌ 错误：返回字符串
return "Result: 42"

# ❌ 错误：缺少 content 数组
return {"text": "Result: 42"}

# ✅ 正确格式
return {"content": [{"type": "text", "text": "Result: 42"}]}
```

### 5.4 未预批准工具

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

### 5.5 AI 输出 XML 文本模拟工具调用 ⭐⭐⭐

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

### 5.6 工具命名常见错误

**错误 1**：误用 Server name 而非字典 key

```python
# ❌ 错误
canvas_mcp = create_sdk_mcp_server(name="canvas-mcp", ...)
mcp_servers={"canvas": canvas_mcp}
allowed_tools=["mcp__canvas-mcp__create_job"]  # 使用了 Server name

# ✅ 正确
allowed_tools=["mcp__canvas__create_job"]  # 使用字典 key
```

**错误 2**：字典 key 与白名单不一致

```python
# ❌ 错误
mcp_servers={"canvas_server": canvas_mcp}  # 字典 key 是 "canvas_server"
allowed_tools=["mcp__canvas__create_job"]  # 白名单用了 "canvas"

# ✅ 正确
allowed_tools=["mcp__canvas_server__create_job"]  # 与字典 key 一致
```

**错误 3**：工具名拼写错误

```python
# ❌ 错误
@tool("create_job", ...)  # 工具名是 "create_job"
allowed_tools=["mcp__canvas__createJob"]  # 拼写成了驼峰

# ✅ 正确
allowed_tools=["mcp__canvas__create_job"]  # 与 @tool() 参数一致
```

**验证清单**：

- [ ] 检查 `mcp_servers` 字典的 key
- [ ] 检查 `@tool()` 的第一个参数
- [ ] 拼接格式：`mcp__{字典key}__{工具名}`
- [ ] 在 `allowed_tools` 中配置拼接后的完整名称
- [ ] 通过日志或错误信息验证实际工具名

---

## 六、调试技巧

### 6.1 记录工具执行

```python
@tool("add", "Add numbers", {"a": float, "b": float})
async def add_numbers(args: dict[str, Any]) -> dict[str, Any]:
    print(f"[DEBUG] add_numbers called with {args}")  # 调试日志
    result = args["a"] + args["b"]
    print(f"[DEBUG] add_numbers result: {result}")
    return {"content": [{"type": "text", "text": str(result)}]}
```

### 6.2 使用执行跟踪

```python
executions = []  # 全局列表

@tool("echo", "Echo text", {"text": str})
async def echo_tool(args: dict[str, Any]) -> dict[str, Any]:
    executions.append({"tool": "echo", "args": args})  # 记录执行
    return {"content": [{"type": "text", "text": args["text"]}]}

# 验证工具是否被调用
assert "echo" in [e["tool"] for e in executions]
```

### 6.3 检查 MCP 连接状态

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

## 七、架构要点总结

### 7.1 SDK MCP 内部机制

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

### 7.2 执行链路

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

### 7.3 关键特性

| 特性 | 说明 |
|------|------|
| **In-Process** | Server 在 Python 进程内运行，无 IPC 开销 |
| **直接调用** | Query 直接调用 Server 方法，无序列化 |
| **类型安全** | 支持简单字典和完整 JSON Schema |
| **错误标记** | `is_error: True` 让 AI 知道需要调整策略 |

---

## 八、参考资源

### 8.1 官方文档路径

| 文档 | 路径 |
|------|------|
| MCP 集成说明 | `docs/agent_sdk/docs/MCP in the SDK.md` |
| Python SDK 概览 | `docs/agent_sdk/docs/Python SDK.md` |
| 计算器示例 | `docs/agent_sdk/examples/mcp_calculator.py` |
| 测试用例 | `docs/agent_sdk/examples/e2e-tests/test_sdk_mcp_tools.py` |

### 8.2 核心 API 速查

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

## 九、Checklist

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
