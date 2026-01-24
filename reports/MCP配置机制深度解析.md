# BIMCanvas Agent SDK MCP 配置机制深度解析

> **讨论日期**: 2026-01-24
> **参与者**: 用户 + Claude Code Assistant
> **上下文**: 基于 Claude Code 与 Agent SDK 的 MCP 实现对比分析

---

## 📋 目录

1. [背景与核心问题](#背景与核心问题)
2. [问题1：MCP 工具名称映射机制](#问题1mcp-工具名称映射机制)
3. [问题2：参数 Schema 生成机制](#问题2参数-schema-生成机制)
4. [实践建议](#实践建议)
5. [完整代码示例](#完整代码示例)
6. [参考资料](#参考资料)

---

## 背景与核心问题

### 讨论背景

在分析了 Claude Code 和 Agent SDK 的请求日志后，发现两者的 MCP 工具定义方式存在显著差异。用户提出了两个关键疑问：

### 核心问题

**问题1**: 当前是如何实现从 `ai_job_create` 到 `mcp__canvas__create_job` 的名称映射的？是通过 tools 数组顺序映射吗？

**问题2**: 当前 MCP 工具的参数 Schema 为什么缺少 `$schema`、`additionalProperties`、参数级别的 `description` 等字段？

---

## 问题1：MCP 工具名称映射机制

### ❌ 常见误解

> **误解**：通过 `tools` 数组顺序和 `CANVAS_ALLOWED_TOOLS` 顺序映射

```python
# ❌ 错误理解
canvas_mcp = create_sdk_mcp_server(
    name="canvas",
    tools=[ai_job_create, ai_job_complete],  # tools[0] → CANVAS_ALLOWED_TOOLS[0]？
)

CANVAS_ALLOWED_TOOLS = [
    "mcp__canvas__create_job",    # 对应 tools[0]？
    "mcp__canvas__complete_job",  # 对应 tools[1]？
]
```

### ✅ 正确机制

**自动拼接规则**（由 Agent SDK 内部完成）：

```
mcp__{mcp_servers字典key}__{@tool工具名}
```

**关键要素**：

| 要素 | 来源 | 示例 |
|------|------|------|
| `mcp__` 前缀 | SDK 自动添加（固定） | `mcp__` |
| Server 标识 | `mcp_servers={"canvas": ...}` 的字典 **key** | `canvas` |
| 工具名称 | `@tool("create_job", ...)` 的第一个参数 | `create_job` |
| **最终名称** | **自动拼接** | `mcp__canvas__create_job` |

### 🔍 证据链

#### 证据1：官方文档

**来源**: `docs/agent_sdk/docs/Guides/Custom Tools.md:90`

```markdown
Pattern: mcp__{server_name}__{tool_name}

Example: A tool named get_weather in server my-custom-tools
         becomes mcp__my-custom-tools__get_weather
```

#### 证据2：调研报告

**来源**: `docs/Res_Agent_SDK_MCP_Implementation.md:115-137`

```python
# 创建 Server
calculator = create_sdk_mcp_server(
    name="calculator",  # ← name="calculator" 不影响调用名！
    tools=[add_tool]
)

# 配置 Agent
mcp_servers={"calc": calculator}  # ← "calc" 影响调用名
@tool("add", ...)                 # ← "add" 影响调用名

# 最终调用名
✅ "mcp__calc__add"        # 正确（使用字典 key "calc"）
❌ "mcp__calculator__add"  # 错误（不使用 Server name "calculator"）
```

#### 证据3：测试用例

**来源**: `docs/agent_sdk/examples/.../e2e-tests/test_sdk_mcp_tools.py:31-39`

```python
# 创建 Server
server = create_sdk_mcp_server(
    name="test",  # Server name（仅用于日志）
    tools=[echo_tool],
)

# 配置 Agent
options = ClaudeAgentOptions(
    mcp_servers={"test": server},  # ← 字典 key = "test"（影响调用名）
    allowed_tools=["mcp__test__echo"],  # ✅ 使用 "test"，而非 Server name
)
```

### 🎯 关键发现

1. **`create_sdk_mcp_server(name="...")` 的 name 参数不影响最终调用名**
   - 仅用于日志输出、元数据等
   - 真正影响调用名的是 `mcp_servers` 字典的 **key**

2. **`CANVAS_ALLOWED_TOOLS` 是"预判"而非"定义"**
   - SDK 根据配置自动生成工具名称
   - 我们必须准确预判 SDK 会生成什么名称，才能正确配置白名单
   - 如果预判错误，工具调用时会报错：`Tool mcp__canvas__create_job requires approval`

3. **映射逻辑在 CLI 层面完成**
   - Python SDK 仅传递配置
   - 实际拼接由 TypeScript CLI 完成（`_internal/transport/subprocess_cli.py` 启动的 Claude CLI 进程）

### 📊 对比表

| 要素 | 影响调用名？ | 用途 |
|------|------------|------|
| `create_sdk_mcp_server(name="...")` | ❌ 否 | 日志、元数据 |
| `mcp_servers={"key": ...}` 的 **key** | ✅ 是 | 拼接调用名 |
| `@tool("name", ...)` 的 **name** | ✅ 是 | 拼接调用名 |
| `CANVAS_ALLOWED_TOOLS` | ❌ 否 | 预判调用名（白名单） |

---

## 问题2：参数 Schema 生成机制

### 🔍 现象观察

**SDK 内置工具** (如 `Read`) 的 Schema：

```json
{
  "name": "Read",
  "description": "Reads a file from the local filesystem...",
  "input_schema": {
    "$schema": "http://json-schema.org/draft-07/schema#",  // ✅ 有
    "type": "object",
    "properties": {
      "file_path": {
        "type": "string",
        "description": "The absolute path to the file to read"  // ✅ 参数描述
      },
      "offset": {
        "type": "number",
        "description": "The line number to start reading from..."  // ✅ 参数描述
      }
    },
    "required": ["file_path"],
    "additionalProperties": false  // ✅ 有
  }
}
```

**当前 MCP 工具** (如 `mcp__canvas__create_job`) 的 Schema：

```json
{
  "name": "mcp__canvas__create_job",
  "description": "批量创建隔离工作环境(Git Worktree)。参数 count: 创建个数(默认1,最大10)",
  "input_schema": {
    // ❌ 缺少 "$schema"
    "type": "object",
    "properties": {
      "count": {
        "type": "integer"  // ❌ 缺少参数描述
      }
    },
    "required": ["count"]
    // ❌ 缺少 "additionalProperties"
  }
}
```

### 🔍 根因分析

#### 源码证据

**来源**: `docs/agent_sdk/claude_agent_sdk/__init__.py:227-253`

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
                # ❌ 未添加 $schema、additionalProperties 等字段
```

### 📊 缺失字段原因

| 字段 | 状态 | 原因 |
|------|------|------|
| `$schema` | ❌ 缺失 | SDK 未生成（MCP 协议不强制要求） |
| `additionalProperties` | ❌ 缺失 | SDK 默认行为未设置 |
| `description`（参数级别） | ❌ 缺失 | 简单字典格式不支持参数描述 |
| `required` | ✅ 生成 | 默认所有参数必填 |
| `type`、`properties` | ✅ 生成 | 核心字段，自动转换 |

### 📊 SDK 内置工具 vs MCP 工具对比

| 特性 | SDK 内置工具 | MCP 工具 |
|------|-------------|----------|
| **Schema 生成方式** | CLI 内置定义（TypeScript） | Python SDK 动态生成 |
| **Schema 完整度** | ✅ 完整（含所有字段） | ⚠️ 基础（仅核心字段） |
| **参数描述** | ✅ 支持（`description` 字段） | ❌ 简单字典不支持 |
| **高级约束** | ✅ 支持（`minimum`、`maximum` 等） | ⚠️ 需手动编写完整 Schema |
| **控制粒度** | 固定（CLI 硬编码） | 灵活（可自定义） |

### ✅ 解决方案：提供完整 JSON Schema

**当前实现**（简写形式）：

```python
@tool(
    "create_job",
    "批量创建隔离工作环境",
    {"count": int}  # ← 简写：仅指定参数类型
)
async def ai_job_create(args: dict[str, Any]) -> dict[str, Any]:
    ...
```

**重构实现**（完整 Schema）：

```python
@tool(
    "create_job",
    "批量创建隔离工作环境（Git Worktree）",
    {
        "$schema": "http://json-schema.org/draft-07/schema#",
        "type": "object",
        "properties": {
            "count": {
                "type": "integer",
                "description": "创建个数（默认1，最大10）",
                "minimum": 1,
                "maximum": 10,
                "default": 1
            }
        },
        "required": ["count"],
        "additionalProperties": False
    }
)
async def ai_job_create(args: dict[str, Any]) -> dict[str, Any]:
    ...
```

**生成的 Schema 对比**：

| 字段 | 简写版 | 完整版 |
|------|--------|--------|
| `$schema` | ❌ 无 | ✅ `http://json-schema.org/draft-07/schema#` |
| `properties.count.description` | ❌ 无 | ✅ `"创建个数（默认1，最大10）"` |
| `properties.count.minimum` | ❌ 无 | ✅ `1` |
| `properties.count.maximum` | ❌ 无 | ✅ `10` |
| `properties.count.default` | ❌ 无 | ✅ `1` |
| `additionalProperties` | ❌ 无 | ✅ `false` |

---

## 实践建议

### 1. 命名规范检查清单

在配置 MCP 工具时，使用以下清单验证命名正确性：

```python
# ✅ 检查清单
mcp_servers = {"canvas": canvas_mcp}  # ← 记录字典 key
@tool("create_job", ...)              # ← 记录工具名

# 计算预期调用名
expected_name = f"mcp__canvas__create_job"  # mcp__{字典key}__{工具名}

# 验证白名单
assert expected_name in CANVAS_ALLOWED_TOOLS
```

### 2. Schema 选择策略

| 工具类型 | 推荐方案 | 理由 |
|----------|---------|------|
| **简单工具**<br>（1-2个参数，无复杂约束） | 简单字典<br>`{"param": int}` | 代码简洁，快速开发 |
| **复杂工具**<br>（多参数、嵌套对象、需要验证） | 完整 JSON Schema | 类型安全，提供更好的 AI 提示 |
| **面向用户的工具**<br>（需要详细文档） | 完整 JSON Schema | 参数描述帮助 AI 理解用法 |

### 3. 完整 Schema 模板

```python
@tool(
    "tool_name",
    "工具描述（会出现在工具列表中）",
    {
        "$schema": "http://json-schema.org/draft-07/schema#",
        "type": "object",
        "properties": {
            "required_param": {
                "type": "string",
                "description": "必填参数的详细说明"
            },
            "optional_param": {
                "type": "integer",
                "description": "可选参数的详细说明",
                "minimum": 1,
                "maximum": 100,
                "default": 10
            },
            "nested_param": {
                "type": "object",
                "description": "嵌套对象参数",
                "properties": {
                    "x": {"type": "number"},
                    "y": {"type": "number"}
                },
                "required": ["x", "y"]
            }
        },
        "required": ["required_param"],
        "additionalProperties": False
    }
)
async def tool_func(args: dict[str, Any]) -> dict[str, Any]:
    ...
```

### 4. 验证工具名称的方法

**方法1：查看日志**（推荐）

启动 Agent 时，日志会输出注册的工具列表：

```bash
python -m bimcanvas_agent.main
# 输出：
# [MCP] Canvas MCP 已注册，工具: ['mcp__canvas__create_job', 'mcp__canvas__complete_job']
```

**方法2：测试调用**

配置错误的白名单，观察错误信息：

```python
# 故意配置错误
CANVAS_ALLOWED_TOOLS = ["mcp__wrong__create_job"]

# 错误信息会提示实际工具名
# Error: Tool mcp__canvas__create_job requires approval
#        ^^^^^^^^^^^^^^^^^^^^^^^^^^^ 实际名称
```

---

## 完整代码示例

### 当前实现（简写版）

**位置**: `BIMCanvas.Agent/src/mcp/canvas.py`

```python
from claude_agent_sdk import tool, create_sdk_mcp_server
from typing import Any

@tool(
    "create_job",
    "批量创建隔离工作环境(Git Worktree)。参数 count: 创建个数(默认1,最大10)",
    {"count": int}
)
async def ai_job_create(args: dict[str, Any]) -> dict[str, Any]:
    """创建独立的 Git Worktree，让 SubAgent 在隔离环境中执行修改。"""
    count = args.get("count", 1)
    # ... 业务逻辑 ...
    return {"content": [{"type": "text", "text": f"已创建 {count} 个工作环境"}]}

@tool(
    "complete_job",
    "通知 Web 端任务完成。参数 names: worktree 名称列表, summary: 执行摘要",
    {"names": str, "summary": str}
)
async def ai_job_complete(args: dict[str, Any]) -> dict[str, Any]:
    """通知 Server 标记任务完成，删除 worktree，通知 Web 端刷新。"""
    # ... 业务逻辑 ...
    return {"content": [{"type": "text", "text": "任务已完成"}]}

# 创建 MCP Server
canvas_mcp = create_sdk_mcp_server(
    name="canvas",  # 仅用于日志，不影响调用名
    version="1.0.0",
    tools=[ai_job_create, ai_job_complete],
)

# 预批准工具白名单（预判 SDK 生成的名称）
CANVAS_ALLOWED_TOOLS = [
    "mcp__canvas__create_job",    # mcp__ + canvas + create_job
    "mcp__canvas__complete_job",  # mcp__ + canvas + complete_job
]
```

### 重构实现（完整 Schema 版）

```python
from claude_agent_sdk import tool, create_sdk_mcp_server
from typing import Any

@tool(
    "create_job",
    "批量创建隔离工作环境（Git Worktree），为 SubAgent 提供独立的开发空间",
    {
        "$schema": "http://json-schema.org/draft-07/schema#",
        "type": "object",
        "properties": {
            "count": {
                "type": "integer",
                "description": "创建的工作环境个数，用于并行执行多个 SubAgent 任务",
                "minimum": 1,
                "maximum": 10,
                "default": 1
            }
        },
        "required": ["count"],
        "additionalProperties": False
    }
)
async def ai_job_create(args: dict[str, Any]) -> dict[str, Any]:
    """创建独立的 Git Worktree，让 SubAgent 在隔离环境中执行修改。"""
    count = args.get("count", 1)

    # 参数验证（Schema 已包含约束，但双重保险）
    if not isinstance(count, int) or count < 1 or count > 10:
        return {
            "content": [{"type": "text", "text": "错误: count 必须在 1-10 之间"}],
            "is_error": True
        }

    # ... 业务逻辑 ...
    return {"content": [{"type": "text", "text": f"已创建 {count} 个工作环境"}]}

@tool(
    "complete_job",
    "通知 Web 端任务完成，触发 worktree 清理和界面刷新",
    {
        "$schema": "http://json-schema.org/draft-07/schema#",
        "type": "object",
        "properties": {
            "names": {
                "type": "string",
                "description": "已完成的 worktree 名称列表，逗号分隔（如 'job_001,job_002'）"
            },
            "summary": {
                "type": "string",
                "description": "任务执行摘要，简要描述完成的工作内容"
            }
        },
        "required": ["names", "summary"],
        "additionalProperties": False
    }
)
async def ai_job_complete(args: dict[str, Any]) -> dict[str, Any]:
    """通知 Server 标记任务完成，删除 worktree，通知 Web 端刷新。"""
    names = args.get("names", "")
    summary = args.get("summary", "")

    # ... 业务逻辑 ...
    return {"content": [{"type": "text", "text": f"任务已完成: {summary}"}]}

# 创建 MCP Server
canvas_mcp = create_sdk_mcp_server(
    name="canvas",
    version="1.0.0",
    tools=[ai_job_create, ai_job_complete],
)

# 预批准工具白名单
CANVAS_ALLOWED_TOOLS = [
    "mcp__canvas__create_job",
    "mcp__canvas__complete_job",
]
```

### 对比表

| 维度 | 简写版 | 完整版 | 改进 |
|------|--------|--------|------|
| **参数描述** | ❌ 无 | ✅ 详细说明 | AI 更好理解参数用途 |
| **参数约束** | ❌ 仅代码验证 | ✅ Schema 级别约束 | 类型安全，自动验证 |
| **默认值** | ❌ 代码硬编码 | ✅ Schema 声明 | 更清晰的默认行为 |
| **额外属性** | ⚠️ 允许 | ✅ 禁止 | 防止参数错误 |
| **Schema 版本** | ❌ 无 | ✅ draft-07 | 明确 Schema 规范 |
| **代码行数** | 5 行 | 20 行 | 更详细但更规范 |

---

## 参考资料

### Agent SDK 源码

| 文件 | 路径 | 内容 |
|------|------|------|
| **MCP SDK 实现** | `docs/agent_sdk/claude_agent_sdk/__init__.py` | `create_sdk_mcp_server()` 函数、Schema 转换逻辑（227-253 行） |
| **测试用例** | `docs/agent_sdk/examples/.../test_sdk_mcp_tools.py` | SDK MCP 工具的端到端测试 |
| **示例代码** | `docs/agent_sdk/examples/mcp_calculator.py` | Calculator MCP 参考实现 |

### 官方文档

| 文档 | 路径 | 内容 |
|------|------|------|
| **Custom Tools 指南** | `docs/agent_sdk/docs/Guides/Custom Tools.md` | MCP 工具命名规则（第 90 行） |
| **MCP in the SDK** | `docs/agent_sdk/docs/Guides/MCP in the SDK.md` | Anthropic 官方 MCP 集成指南 |

### 项目文档

| 文档 | 路径 | 内容 |
|------|------|------|
| **MCP 调研报告** | `docs/Res_Agent_SDK_MCP_Implementation.md` | 详细调研、常见错误、调试技巧（115-137 行） |
| **MCP 框架设计** | `docs/Arch_MCP_Tools.md` | 技术选型、架构设计、快速上手 |
| **Agent Git 工作流** | `docs/Arch_Agent_Git_Workflow.md` | MCP 工具业务场景、完整工作流 |

### 项目代码

| 文件 | 路径 | 内容 |
|------|------|------|
| **MCP 工具定义** | `BIMCanvas.Agent/src/mcp/canvas.py` | `ai_job_create`、`ai_job_complete` 实现 |
| **MainAgent 集成** | `BIMCanvas.Agent/src/agent/main_agent.py` | MCP Server 注册逻辑（192-210 行） |

---

## 总结

### 关键结论

1. **名称映射由 SDK 自动完成**
   - 格式：`mcp__{mcp_servers字典key}__{@tool工具名}`
   - `create_sdk_mcp_server(name=...)` 的 name 参数不影响调用名
   - `CANVAS_ALLOWED_TOOLS` 是预判，不是定义

2. **Schema 缺失字段是简写导致**
   - 简单字典 `{"count": int}` 仅生成基础 Schema
   - 可提供完整 JSON Schema，SDK 会原样使用
   - 完整 Schema 提供更好的类型安全和 AI 提示

3. **最佳实践**
   - 简单工具使用简单字典（快速开发）
   - 复杂工具使用完整 Schema（规范、安全）
   - 通过日志或错误信息验证工具名称

---

**文档版本**: v1.0
**最后更新**: 2026-01-24
**维护者**: BIMCanvas 开发团队
