# Agent SDK 技术指南

> **版本**：v1.4 | **更新日期**：2026-01-05
> **目的**：记录 BIMCanvas Agent 项目的技术细节、架构决策和最佳实践
> **重要更新**：经官方文档深度研究，Agent SDK **完全支持**"Claude Code 底座"愿景

---

## 零、核心设计理念

### 0.1 架构哲学

> **BIMCanvas Agent = Claude Code + 领域 MCP 工具**

我们不从头造一个 Agent 框架，而是：

```
┌─────────────────────────────────────────────────────────────────┐
│                    BIMCanvas Agent 架构                          │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│   ┌─────────────────────────────────────────────────────────┐   │
│   │              领域 MCP 工具层                              │   │
│   │  ├── canvas__capture_zone (截图)                         │   │
│   │  ├── canvas__get_room_data (房间数据)                    │   │
│   │  ├── placement__validate (布置验证)                      │   │
│   │  └── git__branch_manager (分支管理)                      │   │
│   └─────────────────────────────────────────────────────────┘   │
│                           ↑ 注入                                │
│   ┌─────────────────────────────────────────────────────────┐   │
│   │              Claude Code 底座                             │   │
│   │  ├── 内置工具 (Read, Write, Task, Bash...)               │   │
│   │  ├── 会话管理 (session_id, resume)                       │   │
│   │  ├── Hooks (pre/post-tool-use)                          │   │
│   │  ├── 自定义命令 (/command)                               │   │
│   │  └── MCP 协议支持                                        │   │
│   └─────────────────────────────────────────────────────────┘   │
│                           ↑ 封装                                │
│   ┌─────────────────────────────────────────────────────────┐   │
│   │              Agent SDK (Python)                          │   │
│   │              query() + ClaudeAgentOptions                │   │
│   └─────────────────────────────────────────────────────────┘   │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### 0.2 设计原则

| 原则 | 说明 | 好处 |
|------|------|------|
| **以 Claude Code 为底座** | 不重新实现 CLI 已有功能 | 代码量最小化 |
| **通过 MCP 注入领域能力** | 截图、布置验证等封装为 MCP 工具 | 职责清晰、可复用 |
| **随 Claude Code 升级** | 新功能自动继承 | 持续获得能力提升 |
| **AI 自主决策** | 主 Agent 自主派发 SubAgent | 灵活应对复杂任务 |

### 0.3 与"从头造轮子"的对比

| 方面 | 从头造轮子 | Claude Code 底座（我们的选择） |
|------|-----------|-------------------------------|
| 工具实现 | 自己写 Read/Write/Bash | 直接用 Claude Code 内置工具 |
| 会话管理 | 自己实现上下文维护 | 用 session_id + resume |
| SubAgent | 自己实现任务派发 | 用 `agents` + `AgentDefinition` 定义，Task 工具派发 |
| Hooks | 自己实现事件系统 | 用 Claude Code Hooks |
| 升级成本 | 每次都要跟进 | 自动继承新功能 |

---

## 一、核心概念

### 1.1 Agent SDK 是什么？

Agent SDK (`claude-agent-sdk`) 是 Claude Code CLI 的 Python 封装，本质上是**程序化调用 CLI**。

```
┌─────────────────────────────────────────────────────────────────┐
│                        概念映射                                  │
├─────────────────────────────────────────────────────────────────┤
│  CLI 命令                        Agent SDK                       │
│  ─────────────────────────────────────────────────────────────  │
│  claude "你好"                   query(prompt="你好")            │
│  --system-prompt "..."           options.system_prompt           │
│  --cwd /path                     options.cwd                     │
│  --allowedTools Read,Write       options.allowed_tools           │
│  --max-turns 10                  options.max_turns               │
└─────────────────────────────────────────────────────────────────┘
```

### 1.2 query() 函数

`query()` 是 Agent SDK 的核心函数，等价于执行一次 `claude` 命令：

```python
from claude_agent_sdk import query, ClaudeAgentOptions

async for message in query(
    prompt="用户消息",
    options=ClaudeAgentOptions(
        system_prompt="系统提示词",
        cwd="/工作目录",
        allowed_tools=["Read", "Write", "Glob"],
        max_turns=10,
    )
):
    # 处理响应消息
    pass
```

### 1.2.1 query() vs ClaudeSDKClient 选择指南

> **关键区别**：`query()` **不支持** Hooks 和 Custom Tools，这些功能仅 `ClaudeSDKClient` 支持。

| 特性 | `query()` | `ClaudeSDKClient` |
|------|-----------|-------------------|
| **会话** | 每次创建新会话 | 复用同一会话 |
| **上下文** | 单次交互 | 多轮对话，保持上下文 |
| **Hooks** | ❌ **不支持** | ✅ 支持 |
| **Custom Tools (SDK MCP)** | ❌ **不支持** | ✅ 支持 |
| **中断 (Interrupts)** | ❌ 不支持 | ✅ 支持 |
| **流式输入** | ✅ 支持 | ✅ 支持 |

**选择原则**：
- 需要 Hooks 或 Custom Tools → **必须用 `ClaudeSDKClient`**
- 需要多轮对话 → 推荐 `ClaudeSDKClient`
- 简单一次性任务（无 Hooks/Custom Tools） → 可用 `query()`

### 1.3 关键配置项

| 配置项 | 类型 | 说明 |
|--------|------|------|
| `system_prompt` | str | 系统提示词，定义 Agent 角色和行为 |
| `cwd` | str | 工作目录，文件工具的根路径 |
| `allowed_tools` | list | 允许使用的内置工具列表 |
| `permission_mode` | str | 权限模式（见下表） |
| `max_turns` | int | 最大对话轮次（工具调用次数） |
| `resume` | str | 会话 ID，用于恢复之前的对话 |
| `fork_session` | bool | 恢复会话时是否分叉为新会话 |
| `mcp_servers` | dict | MCP Server 配置（如 `{"canvas": server}`） |
| `can_use_tool` | Callable | 工具权限回调函数，用于细粒度权限控制 |
| `enable_file_checkpointing` | bool | 启用文件检查点，支持 `rewind_files()` 回滚 |
| `sandbox` | SandboxSettings | 沙箱配置，控制命令执行隔离 |
| `output_format` | OutputFormat | 结构化输出格式（JSON Schema） |
| `setting_sources` | list | 设置源：`["user", "project", "local"]`，默认不加载文件系统配置 |
| `add_dirs` | list | 额外允许访问的目录列表 |

**权限模式 (permission_mode)**：

| 模式 | 说明 |
|------|------|
| `"default"` | 标准权限行为（默认） |
| `"acceptEdits"` | 自动接受文件编辑 |
| `"plan"` | 规划模式 - 仅规划不执行 |
| `"bypassPermissions"` | 绕过所有权限检查（谨慎使用） |

---

## 二、内置工具

### 2.1 可用工具列表

Agent SDK 通过 Claude Code CLI 提供以下内置工具：

| 工具 | 说明 | 典型用途 |
|------|------|----------|
| `Read` | 读取文件内容 | 读取 JSON、代码文件 |
| `Write` | 写入文件 | 创建或覆盖文件 |
| `Edit` | 编辑文件 | 精确修改文件内容 |
| `Glob` | 搜索文件 | 按模式查找文件 |
| `Grep` | 搜索内容 | 在文件中搜索文本 |
| `Bash` | 执行命令 | 运行 shell 命令 |
| `Task` | 启动子 Agent | **实现 SubAgent 的关键** |

### 2.2 启用工具

```python
options = ClaudeAgentOptions(
    allowed_tools=["Read", "Write", "Glob", "Task"],  # 按需启用
    permission_mode="acceptEdits",  # 自动接受文件编辑
)
```

---

## 三、Agent 架构模型

### 3.1 主 Agent + SubAgent 模式

```
┌─────────────────────────────────────────────────────────────────┐
│  主 Agent（后台常驻）                                            │
│  ├── 职责：分析需求、制定计划、派发任务、管理分支                  │
│  ├── 状态：长期存活，维护上下文                                   │
│  ├── 决策：自主判断何时启动 SubAgent                              │
│  │                                                               │
│  ├── 派发子任务 ──────────────────────────────────────┐         │
│  └── 整合结果                                          │         │
│                                                        ↓         │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │  SubAgent（临时存在）                                        ││
│  │  ├── 布置家具 Agent   → 专注家具布置规则                      ││
│  │  ├── 分区设计 Agent   → 专注空间功能划分                      ││
│  │  ├── 策略生成 Agent   → 专注设计方案生成                      ││
│  │  └── Git 管理 Agent   → 专注版本控制操作                      ││
│  │                                                               ││
│  │  特点：任务完成即销毁，不持有长期状态                          ││
│  └─────────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────┘
```

### 3.2 关键设计要点

| 要点 | 主 Agent | SubAgent |
|------|----------|----------|
| **生命周期** | 后台常驻 | 临时存在，任务完成即销毁 |
| **状态管理** | 维护会话上下文 | 无状态，每次独立执行 |
| **决策权** | 自主决定何时派发任务 | 专注执行单一任务 |
| **工具权限** | 全量工具 + Task + MCP 领域工具 | 最小必要权限（不含 Task） |
| **System Prompt** | 通用协调者 | 领域专家 |

### 3.3 核心要求

1. **AI 自主性**：主 Agent 能自主决定是否/何时启动 SubAgent，而非程序硬编码
2. **职责隔离**：每个 SubAgent 有专门的 System Prompt 和工具权限
3. **生命周期分离**：主 Agent 常驻，SubAgent 临时

---

## 四、SubAgent 实现（官方推荐）

> **官方明确**："This guide focuses on the **programmatic approach**, which is **recommended for SDK applications**."

### 4.1 SubAgent 创建方式（官方列出）

| 方式 | 实现 | 官方态度 |
|------|------|----------|
| **Programmatic** | `agents` 参数 + `AgentDefinition` | ✅ **SDK 应用推荐** |
| Filesystem-based | `.claude/agents/*.md` 文件 | 替代方案 |
| Built-in | `general-purpose` 内置 agent | 自动可用，无需定义 |

**核心机制**：
- SubAgent 通过 **Task 工具调用**（Task 是调用机制，不是实现方式）
- SubAgent 通过 **AgentDefinition 定义**（完全可自定义 prompt、tools、model）
- SubAgent **不能再派发 SubAgent**（不要在 subagent 的 tools 里包含 Task）

### 4.2 AgentDefinition 完整配置

| 字段 | 类型 | 必需 | 说明 |
|------|------|------|------|
| `description` | `str` | ✅ | 告诉 Claude 何时使用这个 agent |
| `prompt` | `str` | ✅ | agent 的 system prompt，定义角色和行为 |
| `tools` | `list[str]` | ❌ | 允许的工具，省略则继承父 agent 所有工具 |
| `model` | `str` | ❌ | 模型覆盖：`"sonnet"` / `"opus"` / `"haiku"` / `"inherit"` |

### 4.3 完整示例（官方推荐方式）

```python
from claude_agent_sdk import query, ClaudeAgentOptions, AgentDefinition

# 定义 SubAgent（通过 AgentDefinition）
subagents = {
    "layout-agent": AgentDefinition(
        # description: 告诉 Claude 何时调用这个 SubAgent
        description="家具布置专家。用于空间规划和家具摆放任务。",
        # prompt: SubAgent 的 System Prompt（完全自定义！）
        prompt="""你是布置家具专家。

## 专业能力
- 精通家具布置规则
- 熟悉空间动线设计
- 了解人体工程学

## 输出要求
- 输出符合 modules.json 格式
- 确保家具不与禁区重叠
- 保持主要通道畅通（≥800mm）""",
        # tools: 限制 SubAgent 的工具权限（最小权限原则）
        tools=["Read", "Write", "Glob"],
        # model: 可为不同 SubAgent 指定不同模型
        model="sonnet"
    ),
    "zone-agent": AgentDefinition(
        description="空间分区专家。用于大空间功能区划分。",
        prompt="你是空间分区专家，负责大空间功能划分...",
        tools=["Read", "Glob"],  # 只读权限
    ),
}

# 主 Agent 配置
async for message in query(
    prompt="帮我布置整个户型",
    options=ClaudeAgentOptions(
        system_prompt="""你是 BIMCanvas 主控 Agent。

## 职责
1. 分析用户需求，制定任务计划
2. 根据任务类型，调用合适的 SubAgent
3. 整合子任务结果，向用户汇报

## 可用 SubAgent
- layout-agent: 家具布置专家
- zone-agent: 空间分区专家

## 注意
- 复杂任务拆分后派发给 SubAgent
- SubAgent 完成后整合结果""",
        # 必须启用 Task 工具！
        allowed_tools=["Read", "Write", "Glob", "Bash", "Task"],
        # 注册 SubAgent
        agents=subagents,
        max_turns=20,
    )
):
    print(message)
```

### 4.4 工作流程

```
用户: "帮我布置整个户型"
    ↓
主 Agent 分析（AI 自主决策）:
    "这个户型有客厅、主卧、次卧，需要分别布置"
    "应该使用 layout-agent 来处理"
    ↓
主 Agent 调用 Task 工具（自动匹配 description）:
    Task(subagent_type="layout-agent", prompt="为客厅布置家具...")
    Task(subagent_type="layout-agent", prompt="为主卧布置家具...")
    ↓
SubAgent 执行（使用自己的 prompt 和 tools）
    ↓
主 Agent 整合结果:
    "已完成所有房间布置，客厅放置了沙发、茶几..."
```

### 4.5 显式调用 vs 自动匹配

| 方式 | 示例 | 说明 |
|------|------|------|
| **显式调用** | `"Use the layout-agent to..."` | 在 prompt 中指定 agent 名称 |
| **自动匹配** | `"帮我布置客厅"` | Claude 根据 description 自动选择 |

### 4.6 常见工具权限组合

| 用途 | tools 配置 | 说明 |
|------|------------|------|
| 只读分析 | `["Read", "Grep", "Glob"]` | 可查看但不可修改 |
| 测试执行 | `["Bash", "Read", "Grep"]` | 可运行命令和分析输出 |
| 代码修改 | `["Read", "Edit", "Write", "Glob"]` | 读写但不执行命令 |
| 完全访问 | 省略 `tools` | 继承父 agent 所有工具 |

### 4.7 ⚠️ 不推荐的做法

以下做法**不是官方推荐的 SubAgent 实现**：

| 做法 | 问题 |
|------|------|
| 用 MCP 工具封装 SubAgent | 自创方案，绕过官方机制，增加复杂度 |
| 程序路由（硬编码调用不同方法） | 不是 SubAgent，AI 无法自主决策 |
| 在 SubAgent 的 tools 里加 Task | 官方禁止，SubAgent 不能再派发 SubAgent |

---

## 五、MCP 工具分层设计

### 5.1 分层架构

```
┌─────────────────────────────────────────────────────────────────┐
│                      MCP 工具层                                  │
├─────────────────────────────────────────────────────────────────┤
│  通用工具（多 Agent 共享）                                        │
│  ├── canvas__capture_zone    分区截图（多模态分析）               │
│  ├── canvas__get_room_data   获取房间数据                        │
│  ├── canvas__validate_placement  布置冲突验证                    │
│  └── canvas__get_openings    获取门窗数据                        │
├─────────────────────────────────────────────────────────────────┤
│  专用工具（特定 Agent 使用）                                      │
│  ├── placement__suggest_furniture  布置 Agent 专用               │
│  ├── zone__analyze_traffic   分区 Agent 专用                     │
│  └── git__branch_manager     主 Agent 专用                       │
└─────────────────────────────────────────────────────────────────┘
```

### 5.2 按需注入原则

| Agent | 通用工具 | 专用工具 | 说明 |
|-------|----------|----------|------|
| 主 Agent | ✅ 全部 | git__* | 协调全局，管理版本 |
| 布置 Agent | capture_zone, get_room_data | placement__* | 专注家具布置 |
| 分区 Agent | capture_zone, get_room_data | zone__* | 专注空间划分 |

### 5.3 工具复用的好处

- **节省 Token**：不需要在 System Prompt 中重复描述数据格式
- **一致性**：所有 Agent 用同一工具获取数据，格式统一
- **可维护**：数据格式变化只需修改工具，不需改每个 Agent

---

## 六、Agent SDK 能力评估

> **结论**：经过官方文档深度研究，Agent SDK **完全支持**我们的"Claude Code 底座"愿景！

### 6.1 Claude Code CLI vs Agent SDK 能力对照

| 能力 | Claude Code CLI | Agent SDK | 状态 |
|------|----------------|-----------|------|
| 内置工具 (Read/Write/Task...) | ✅ | ✅ `allowed_tools` | ✅ 已确认 |
| MCP Server | ✅ settings.json | ✅ `mcp_servers` 参数 | ✅ **已确认** |
| SDK MCP Server (进程内) | - | ✅ `create_sdk_mcp_server()` | ✅ **新发现** |
| Hooks (6 种类型) | ✅ | ✅ `hooks` 参数 | ✅ **已确认** |
| 自定义命令 (/command) | ✅ .claude/commands/ | ✅ `setting_sources` | ✅ **已确认** |
| 会话恢复 | ✅ --resume | ✅ `resume` 参数 | ✅ 已确认 |
| 会话分叉 | ✅ | ✅ `fork_session` 参数 | ✅ **新发现** |
| 扩展思考 | ✅ | ✅ `max_thinking_tokens` | ✅ 已确认 |
| 权限模式 | ✅ | ✅ `permission_mode` | ✅ 已确认 |
| 读取 .claude/ 配置 | ✅ 自动读取 | ✅ `setting_sources` | ✅ **已确认** |
| SubAgent 定义 | ✅ agents.md | ✅ `agents` + `AgentDefinition` | ✅ **已确认** |
| 持久会话客户端 | - | ✅ `ClaudeSDKClient` | ✅ **新发现** |

### 6.2 SubAgent 支持

> **详见第四章**：SubAgent 的完整实现方案和代码示例。

**要点回顾**：
- 官方推荐：`agents` 参数 + `AgentDefinition`
- 调用机制：Task 工具
- 关键限制：SubAgent 不能再派发 SubAgent

### 6.3 Hooks 支持详解

> ⚠️ **重要**：Hooks **仅在 `ClaudeSDKClient` 中支持**，`query()` 函数不支持 Hooks。

#### 6.3.1 Python SDK 支持的 Hook 类型

Python SDK 支持 6 种 Hook 类型：

| Hook 类型 | 触发时机 | 典型用途 |
|-----------|----------|----------|
| `PreToolUse` | 工具执行前 | 验证/拦截危险命令 |
| `PostToolUse` | 工具执行后 | 记录/修改结果 |
| `UserPromptSubmit` | 用户提交前 | 预处理/注入上下文 |
| `Stop` | Agent 停止时 | 清理/保存状态 |
| `SubagentStop` | SubAgent 停止时 | 收集子任务结果 |
| `PreCompact` | 上下文压缩前 | 保留关键信息 |

#### 6.3.2 Python SDK 不支持的 Hook 事件

> ⚠️ **重要**：由于设置限制，Python SDK **不支持**以下 Hook 事件（仅 TypeScript SDK 可用）：

| Hook 事件 | 说明 |
|-----------|------|
| `SessionStart` | 会话开始时 |
| `SessionEnd` | 会话结束时 |
| `Notification` | 通知事件 |
| `SubagentStart` | SubAgent 启动时 |
| `PostToolUseFailure` | 工具执行失败后 |
| `PermissionRequest` | 权限请求时 |

#### 6.3.3 正确的 Hooks 使用示例

```python
from claude_agent_sdk import ClaudeSDKClient, ClaudeAgentOptions, HookMatcher

async def validate_bash(input_data, tool_use_id, context):
    """拦截危险的 Bash 命令"""
    if input_data['tool_name'] == 'Bash':
        command = input_data['tool_input'].get('command', '')
        if 'rm -rf' in command:
            return {
                'hookSpecificOutput': {
                    'hookEventName': 'PreToolUse',
                    'permissionDecision': 'deny',
                    'permissionDecisionReason': '危险命令已拦截'
                }
            }
    return {}

options = ClaudeAgentOptions(
    hooks={
        'PreToolUse': [HookMatcher(matcher='Bash', hooks=[validate_bash])]
    }
)

# ✅ 正确：使用 ClaudeSDKClient（Hooks 仅在此支持）
async with ClaudeSDKClient(options=options) as client:
    await client.query("执行一些文件操作")
    async for msg in client.receive_response():
        print(msg)

# ❌ 错误：query() 不支持 Hooks
# async for msg in query(prompt="...", options=options):  # Hooks 不会生效！
```

### 6.4 SDK MCP Server（推荐）

Agent SDK 支持**进程内** MCP Server，无需外部进程：

```python
from claude_agent_sdk import tool, create_sdk_mcp_server, ClaudeAgentOptions

# 使用 @tool 装饰器定义工具
@tool("get_room_data", "获取房间分区数据", {"room_id": str})
async def get_room_data(args):
    room_id = args['room_id']
    # 读取 room_zones.json...
    return {"content": [{"type": "text", "text": json.dumps(room_data)}]}

@tool("validate_placement", "验证布置是否有冲突", {"module_bounds": dict})
async def validate_placement(args):
    # 碰撞检测逻辑...
    return {"content": [{"type": "text", "text": "无冲突"}]}

# 创建 SDK MCP Server
server = create_sdk_mcp_server(
    name="canvas-tools",
    version="1.0.0",
    tools=[get_room_data, validate_placement]
)

# 使用
options = ClaudeAgentOptions(
    mcp_servers={"canvas": server},
    allowed_tools=[
        "Read", "Write",
        "mcp__canvas__get_room_data",
        "mcp__canvas__validate_placement"
    ]
)
```

**三种 MCP Server 类型对比**：

| 类型 | 实现方式 | 适用场景 |
|------|----------|----------|
| stdio | 外部进程 | 复用现有 MCP Server |
| HTTP/SSE | 远程服务 | 跨网络调用 |
| **SDK MCP Server** | 进程内 | **推荐**，最简单 |

### 6.5 ClaudeSDKClient（持久会话）

对于需要维持长期会话的主 Agent，`ClaudeSDKClient` 比 `query()` 更合适：

```python
from claude_agent_sdk import ClaudeSDKClient, ClaudeAgentOptions

async with ClaudeSDKClient(options=options) as client:
    # 第一次对话
    await client.query("帮我分析这个户型")
    async for msg in client.receive_response():
        print(msg)

    # 后续对话自动保持上下文！
    await client.query("客厅应该怎么布置？")
    async for msg in client.receive_response():
        print(msg)
```

**`query()` vs `ClaudeSDKClient`**：

| 方面 | `query()` | `ClaudeSDKClient` |
|------|-----------|-------------------|
| 会话生命周期 | 每次调用新会话 | 持久会话 |
| 上下文管理 | 需手动 resume | 自动维护 |
| 适用场景 | 独立任务、SubAgent | **主 Agent** |

### 6.6 推荐架构方案

基于研究结论，BIMCanvas Agent 推荐实现方案：

| 组件 | SDK 能力 | 实现方式 |
|------|----------|----------|
| 主 Agent | `ClaudeSDKClient` | 持久会话，全量工具 + Task |
| SubAgent | `AgentDefinition` | 通过 agents 参数定义，Task 工具派发 |
| MCP 工具 | SDK MCP Server | 进程内 Python 实现 |
| 安全控制 | Hooks | PreToolUse 验证危险命令 |

**代码架构建议**：

```
BIMCanvas.Agent/
├── src/
│   ├── main.py
│   ├── agent/
│   │   ├── main_agent.py      # 主 Agent（ClaudeSDKClient）
│   │   └── subagents.py       # SubAgent 定义（AgentDefinition）
│   ├── mcp/
│   │   └── canvas_tools.py    # SDK MCP Server
│   ├── hooks/
│   │   └── safety_hooks.py    # PreToolUse 安全验证
│   └── server/
│       └── http_server.py
```

---

## 七、最佳实践

### 7.1 System Prompt 设计原则

```python
SYSTEM_PROMPT = """
## 角色定义
[清晰定义 Agent 的角色和职责]

## 可用工具
[列出可用工具及其用途]

## 工作流程
[描述典型的工作流程]

## 输出格式
[定义期望的输出格式]

## 约束条件
[列出必须遵守的规则]
"""
```

### 7.2 工具权限最小化

```python
# 不好：给所有权限
allowed_tools=["Read", "Write", "Edit", "Bash", "Task", ...]

# 好：按需授权
allowed_tools=["Read", "Glob"]  # 只读任务
allowed_tools=["Read", "Write"]  # 读写任务
```

### 7.3 错误处理

```python
async def safe_query(prompt: str, options: ClaudeAgentOptions) -> str:
    try:
        result = ""
        async for msg in query(prompt=prompt, options=options):
            if isinstance(msg, AssistantMessage):
                result += extract_text(msg)
        return result
    except Exception as e:
        logger.exception(f"Agent error: {e}")
        return f"任务执行失败: {str(e)}"
```

### 7.4 会话管理

```python
class AgentSession:
    def __init__(self):
        self.session_id = None

    async def chat(self, message: str) -> str:
        options = ClaudeAgentOptions(...)

        # 恢复会话
        if self.session_id:
            options.resume = self.session_id

        async for msg in query(prompt=message, options=options):
            # ⚠️ 注意：以下 session_id 捕获方式是推测代码
            # 官方文档仅展示 resume 参数用法，未明确文档化如何从响应获取 session_id
            # 实际使用时需验证消息结构
            if hasattr(msg, 'subtype') and msg.subtype == 'init':
                self.session_id = msg.data.get('session_id')
            # ...

    def clear(self):
        self.session_id = None
```

---

## 八、常见问题

### Q1: Task 工具和 MCP 工具有什么区别？

**核心区别**：Task 是**调用机制**，MCP 是**能力扩展**。

| 方面 | Task 工具 | MCP 工具 |
|------|-----------|----------|
| **本质** | 派发 SubAgent 的调用机制 | 领域能力的封装 |
| **定义方式** | SubAgent 通过 `AgentDefinition` 定义 | 通过 `@tool` 装饰器或外部进程 |
| **用途** | AI 自主决策，派发复杂子任务 | 提供特定领域功能（如截图、验证） |
| **例子** | `Task(subagent_type="layout-agent")` | `mcp__canvas__get_room_data` |

**澄清**：MCP 工具**不是** SubAgent 实现方式。用 MCP 封装 SubAgent 是自创方案，不是官方推荐。

### Q2: Windows 环境需要注意什么？

```python
# main.py 开头设置 git-bash 路径
import os
import sys

if sys.platform == "win32":
    git_bash_paths = [
        r"D:\Git\bin\bash.exe",
        r"C:\Program Files\Git\bin\bash.exe",
    ]
    for path in git_bash_paths:
        if os.path.exists(path):
            os.environ["CLAUDE_CODE_GIT_BASH_PATH"] = path
            break
```

### Q3: 如何调试 Agent？

1. **查看完整消息**：
```python
async for msg in query(prompt, options):
    print(f"Message type: {type(msg)}")
    print(f"Content: {msg}")
```

2. **启用详细日志**：
```python
import logging
logging.basicConfig(level=logging.DEBUG)
```

3. **检查工具调用**：
```python
if isinstance(msg, ToolUseMessage):
    print(f"Tool: {msg.tool_name}")
    print(f"Input: {msg.tool_input}")
```

---

## 九、版本历史

| 版本 | 日期 | 更新内容 |
|------|------|----------|
| v1.0 | 2026-01-05 | 初始版本：核心概念、SubAgent 方案、最佳实践 |
| v1.1 | 2026-01-05 | 新增：核心设计理念、Agent 架构模型、MCP 工具分层、SDK 能力评估 |
| v1.2 | 2026-01-05 | **重大更新**：完成官方文档深度研究，确认所有能力均已支持 |
| v1.3 | 2026-01-05 | **概念修正**：基于官方文档纠正 SubAgent 实现方式的错误理解 |
| v1.4 | 2026-01-05 | **准确性验证**：基于官方文档逐项核对，修正 2 处错误 |

### v1.4 更新详情

基于官方文档逐项核对：

| 修正项 | 原错误 | 修正后 |
|--------|--------|--------|
| 配置项表 | `max_thinking_tokens` 列为配置项 | 已删除（官方无此参数） |
| 配置项表 | `mcp_servers` 类型为 `list` | 改为 `dict` |
| 会话管理代码 | session_id 捕获方式无说明 | 添加警告注释 |

### v1.3 更新详情

基于官方 SubAgent 文档，纠正以下概念性错误：

| 位置 | 原错误 | 修正后 |
|------|--------|--------|
| 第四章 | 将 Task/MCP/程序路由作为三种 SubAgent 实现方案对比 | Task 是调用机制，SubAgent 通过 `AgentDefinition` 定义 |
| 第 0.3 节 | "用 Task 工具或 MCP" 暗示 MCP 是替代方案 | 明确 `agents` + `AgentDefinition` + Task 派发 |
| 第 3.2 节 | "全量工具 + Task/MCP" 表述模糊 | "全量工具 + Task + MCP 领域工具" |
| 第八章 Q1 | 对比 Task 和 MCP 作为 SubAgent 实现 | 澄清 Task 是调用机制，MCP 是领域工具 |

**核心纠正**：MCP 工具封装 SubAgent **不是**官方推荐方式，是自创方案。

### v1.2 更新详情

经过官方文档深度研究，确认以下能力：

| 能力 | 之前状态 | 现状态 | SDK 实现方式 |
|------|----------|--------|-------------|
| SubAgent | 待验证 | ✅ 已确认 | `agents` + `AgentDefinition` |
| Hooks | 待验证 | ✅ 已确认 | `hooks` 参数，6 种类型 |
| MCP Server | 待验证 | ✅ 已确认 | `mcp_servers` + SDK MCP Server |
| .claude/ 配置 | 待验证 | ✅ 已确认 | `setting_sources` |
| 持久会话 | 未知 | ✅ 新发现 | `ClaudeSDKClient` |
| 会话分叉 | 未知 | ✅ 新发现 | `fork_session` |

---

## 十、待研究问题

- [x] ~~Task 工具的详细参数和行为~~ → 已确认：通过 `AgentDefinition` 定义
- [x] ~~MCP Server 与 Agent SDK 的集成方式~~ → 已确认：支持 SDK MCP Server
- [x] ~~Hooks 支持情况~~ → 已确认：6 种 Hook 类型
- [ ] 多 Agent **并行**执行的实现（Task 是顺序的，如何并行？）
- [ ] 长时间任务的超时处理和恢复机制
- [ ] `ClaudeSDKClient` 与现有 `query()` 代码的迁移路径
- [ ] 生产环境下的错误处理和重试策略

---

## 参考资料

### 官方文档（已研读）

- [Agent SDK Overview](https://docs.anthropic.com/en/docs/claude-code/agent-sdk/overview) - SDK 概览
- [Agent SDK SubAgents](https://docs.anthropic.com/en/docs/claude-code/agent-sdk/subagents) - SubAgent 详解
- [Agent SDK MCP](https://docs.anthropic.com/en/docs/claude-code/agent-sdk/mcp) - MCP 集成
- [Agent SDK Hooks](https://docs.anthropic.com/en/docs/claude-code/agent-sdk/hooks) - Hooks 详解
- [Agent SDK Sessions](https://docs.anthropic.com/en/docs/claude-code/agent-sdk/sessions) - 会话管理
- [Python SDK Reference](https://docs.anthropic.com/en/docs/claude-code/agent-sdk/python-sdk-reference) - Python API 参考

### 示例代码

- [Research Agent Demo](https://github.com/anthropics/claude-code/tree/main/agent-sdk-demos/research-agent) - 多 Agent 协作示例

### 项目文档

- [MCP 协议规范](https://modelcontextprotocol.io)
- [BIMCanvas Agent MVP 计划](../plans/Agent_MVP.md)
