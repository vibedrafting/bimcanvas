# Agent SDK 技术指南

> **版本**：v2.0 | **更新日期**：2026-01-13
> **目的**：记录 BIMCanvas Agent 项目的技术细节、架构决策和最佳实践

---

## 一、核心设计理念

### 1.1 架构哲学

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
│   │              ClaudeSDKClient + AgentDefinition           │   │
│   └─────────────────────────────────────────────────────────┘   │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### 1.2 设计原则

| 原则 | 说明 | 好处 |
|------|------|------|
| **以 Claude Code 为底座** | 不重新实现 CLI 已有功能 | 代码量最小化 |
| **通过 MCP 注入领域能力** | 截图、布置验证等封装为 MCP 工具 | 职责清晰、可复用 |
| **随 Claude Code 升级** | 新功能自动继承 | 持续获得能力提升 |
| **AI 自主决策** | 主 Agent 自主派发 SubAgent | 灵活应对复杂任务 |

### 1.3 并行设计三大支柱

> 详见 [Flow_Agent_Parallel_Workflows.md §1.2 三大支柱](./Flow_Agent_Parallel_Workflows.md#12-三大支柱)

**核心支柱**：文件驱动（Git 分支 = 完整状态）+ 异步协作（Commit/PR 交付）+ 并行生成（Git Worktree 隔离）

### 1.4 Agent SDK 与 CLI 映射

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

---

## 二、核心 API 选择

### 2.1 query() vs ClaudeSDKClient

> **架构决策**：主 Agent 使用 `ClaudeSDKClient`，SubAgent 临时任务可用 `query()`。

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
- 需要多轮对话或程序触发 → **推荐 `ClaudeSDKClient`**
- 简单一次性任务（无 Hooks/Custom Tools） → 可用 `query()`

### 2.2 ClaudeAgentOptions 配置项

| 配置项 | 类型 | 说明 |
|--------|------|------|
| `system_prompt` | str | 系统提示词，定义 Agent 角色和行为 |
| `cwd` | str | 工作目录，文件工具的根路径 |
| `allowed_tools` | list | 允许的内置工具列表 |
| `permission_mode` | str | 权限模式（见下表） |
| `max_turns` | int | 最大对话轮次（工具调用次数） |
| `resume` | str | 会话 ID，用于恢复对话 |
| `fork_session` | bool | 恢复时是否分叉为新会话 |
| `mcp_servers` | dict | MCP Server 配置（如 `{"canvas": server}`） |
| `agents` | dict | SubAgent 定义（`AgentDefinition` 字典） |
| `hooks` | dict | Hook 配置 |
| `can_use_tool` | Callable | 工具权限回调函数 |
| `sandbox` | SandboxSettings | 沙箱配置，控制命令执行隔离 |
| `enable_file_checkpointing` | bool | 启用文件检查点，支持回滚 |
| `output_format` | OutputFormat | 结构化输出格式（JSON Schema） |
| `setting_sources` | list | 设置源：`["user", "project", "local"]` |
| `add_dirs` | list | 额外允许访问的目录列表 |

**权限模式 (permission_mode)**：

| 模式 | 说明 |
|------|------|
| `"default"` | 标准权限行为 |
| `"acceptEdits"` | 自动接受文件编辑 |
| `"plan"` | 规划模式 - 仅规划不执行 |
| `"bypassPermissions"` | 绕过所有权限检查 |

---

## 三、内置工具

### 3.1 可用工具列表

| 工具 | 说明 | 典型用途 |
|------|------|----------|
| `Read` | 读取文件内容 | 读取 JSON、代码文件 |
| `Write` | 写入文件 | 创建或覆盖文件 |
| `Edit` | 编辑文件 | 精确修改文件内容 |
| `Glob` | 搜索文件 | 按模式查找文件 |
| `Grep` | 搜索内容 | 在文件中搜索文本 |
| `Bash` | 执行命令 | 运行 shell 命令 |
| `Task` | 启动子 Agent | **实现 SubAgent 的关键** |

### 3.2 启用工具

```python
options = ClaudeAgentOptions(
    allowed_tools=["Read", "Write", "Glob", "Task"],  # 按需启用
    permission_mode="acceptEdits",
)
```

---

## 四、SubAgent 实现

> **官方推荐**："This guide focuses on the **programmatic approach**, which is **recommended for SDK applications**."

### 4.1 创建方式

| 方式 | 实现 | 官方态度 |
|------|------|----------|
| **Programmatic** | `agents` 参数 + `AgentDefinition` | ✅ **SDK 应用推荐** |
| Filesystem-based | `.claude/agents/*.md` 文件 | 替代方案 |

**核心机制**：
- SubAgent 通过 **Task 工具调用**
- SubAgent 通过 **AgentDefinition 定义**
- SubAgent **不能再派发 SubAgent**（禁止嵌套）

### 4.2 AgentDefinition 配置

| 字段 | 类型 | 必需 | 说明 |
|------|------|------|------|
| `description` | str | ✅ | 告诉 Claude 何时使用这个 agent |
| `prompt` | str | ✅ | agent 的 system prompt |
| `tools` | list | ❌ | 允许的工具，省略则继承父 agent |
| `model` | str | ❌ | 模型：`"sonnet"` / `"opus"` / `"haiku"` |

### 4.3 完整示例

```python
from claude_agent_sdk import ClaudeSDKClient, ClaudeAgentOptions, AgentDefinition

# 定义 SubAgent
subagents = {
    "layout-agent": AgentDefinition(
        description="家具布置专家。用于空间规划和家具摆放任务。",
        prompt="""你是布置家具专家。

## 专业能力
- 精通家具布置规则
- 熟悉空间动线设计
- 了解人体工程学

## 输出要求
- 输出符合 modules.json 格式
- 确保家具不与禁区重叠
- 保持主要通道畅通（≥800mm）""",
        tools=["Read", "Write", "Glob"],
        model="sonnet"
    ),
}

# 主 Agent 配置
options = ClaudeAgentOptions(
    system_prompt="""你是 BIMCanvas 主控 Agent。

## 职责
1. 分析用户需求，制定任务计划
2. 根据任务类型，调用合适的 SubAgent
3. 整合子任务结果，向用户汇报

## 可用 SubAgent
- layout-agent: 家具布置专家""",
    allowed_tools=["Read", "Write", "Glob", "Bash", "Task"],
    agents=subagents,
    max_turns=20,
)

# 使用 ClaudeSDKClient
async with ClaudeSDKClient(options=options) as client:
    await client.query("帮我布置整个户型")
    async for msg in client.receive_response():
        print(msg)
```

### 4.4 MCP 工具 vs SubAgent（重要澄清）

> 详见 [Agent_Design.md §1.4 MCP 工具 vs SubAgent](./Agent_Design.md#14-mcp-工具-vs-subagent)

**关键区别**：MCP 是能力扩展（函数调用，无决策），SubAgent 是 AI Agent（有决策能力）。

**错误做法**：用 MCP 工具封装 SubAgent（自创方案，不推荐）

---

## 五、MCP 工具

### 5.1 SDK MCP Server（进程内）

> **推荐**：Agent SDK 支持进程内 MCP Server，无需外部进程。

```python
from claude_agent_sdk import tool, create_sdk_mcp_server

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
```

### 5.2 使用方式

> ⚠️ **注意**：Custom Tools 仅在 `ClaudeSDKClient` 中支持

```python
options = ClaudeAgentOptions(
    mcp_servers={"canvas": server},
    allowed_tools=[
        "Read", "Write",
        "mcp__canvas__get_room_data",
        "mcp__canvas__validate_placement"
    ]
)

# 必须使用 ClaudeSDKClient
async with ClaudeSDKClient(options=options) as client:
    await client.query("获取客厅的房间数据")
    async for msg in client.receive_response():
        print(msg)
```

### 5.3 MCP Server 类型对比

| 类型 | 实现方式 | 适用场景 |
|------|----------|----------|
| stdio | 外部进程 | 复用现有 MCP Server |
| HTTP/SSE | 远程服务 | 跨网络调用 |
| **SDK MCP Server** | 进程内 | **推荐**，最简单 |

---

## 六、Hooks

> ⚠️ **重要**：Hooks **仅在 `ClaudeSDKClient` 中支持**

### 6.1 支持的 Hook 类型

| Hook 类型 | 触发时机 | 典型用途 |
|-----------|----------|----------|
| `PreToolUse` | 工具执行前 | 验证/拦截危险命令 |
| `PostToolUse` | 工具执行后 | 记录/修改结果 |
| `UserPromptSubmit` | 用户提交前 | 预处理/注入上下文 |
| `Stop` | Agent 停止时 | 清理/保存状态 |
| `SubagentStop` | SubAgent 停止时 | 收集子任务结果 |
| `PreCompact` | 上下文压缩前 | 保留关键信息 |

### 6.2 使用示例

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

# 必须使用 ClaudeSDKClient
async with ClaudeSDKClient(options=options) as client:
    await client.query("执行一些文件操作")
```

---

## 七、ClaudeSDKClient 完整封装

> **架构说明**：当前采用"主控 Agent + SubAgent"模式，详见 [Agent_Design.md](./Agent_Design.md)。

```python
from claude_agent_sdk import ClaudeSDKClient, ClaudeAgentOptions, AssistantMessage, TextBlock

class MainAgent:
    """基于 ClaudeSDKClient 的布置助手 - 支持持续对话和程序触发"""

    def __init__(self, project_path: str = None):
        self.project_path = project_path
        self._client: ClaudeSDKClient | None = None
        self._connected = False

    async def connect(self) -> None:
        """建立持久连接"""
        if self._connected:
            return
        options = ClaudeAgentOptions(
            system_prompt=SYSTEM_PROMPT,
            cwd=self.project_path,
            max_turns=10,
            allowed_tools=["Read", "Write", "Glob", "Edit"],
            permission_mode="acceptEdits",
        )
        self._client = ClaudeSDKClient(options)
        await self._client.connect()
        self._connected = True

    async def disconnect(self) -> None:
        """断开连接"""
        if self._client and self._connected:
            await self._client.disconnect()
            self._connected = False
            self._client = None

    async def chat(self, user_message: str) -> str:
        """对话（自动保持上下文）"""
        if not self._connected:
            await self.connect()

        await self._client.query(user_message)

        full_response = ""
        async for message in self._client.receive_response():
            if isinstance(message, AssistantMessage):
                for block in message.content:
                    if isinstance(block, TextBlock):
                        full_response += block.text
        return full_response

    async def execute_task(self, task_prompt: str) -> str:
        """执行任务（程序触发入口）"""
        return await self.chat(task_prompt)

    async def interrupt(self) -> None:
        """中断当前任务"""
        if self._client and self._connected:
            await self._client.interrupt()
```

**使用方式**：

```python
agent = MainAgent(project_path="/path/to/project")

# 用户对话（多轮保持上下文）
reply1 = await agent.chat("帮我分析这个户型")
reply2 = await agent.chat("客厅怎么布置？")

# 程序触发任务（SSE 事件触发）
result = await agent.execute_task("检测到模块重叠，请重新布置区域 rz_1")

# 清理
await agent.disconnect()
```

---

## 八、推荐架构方案

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

## 九、最佳实践

### 9.1 System Prompt 设计

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

### 9.2 工具权限最小化

```python
# 不好：给所有权限
allowed_tools=["Read", "Write", "Edit", "Bash", "Task", ...]

# 好：按需授权
allowed_tools=["Read", "Glob"]  # 只读任务
allowed_tools=["Read", "Write"]  # 读写任务
```

### 9.3 错误处理

```python
async def safe_query(prompt: str, options: ClaudeAgentOptions) -> str:
    try:
        async with ClaudeSDKClient(options=options) as client:
            await client.query(prompt)
            result = ""
            async for msg in client.receive_response():
                if isinstance(msg, AssistantMessage):
                    result += extract_text(msg)
            return result
    except Exception as e:
        logger.exception(f"Agent error: {e}")
        return f"任务执行失败: {str(e)}"
```

---

## 十、常见问题

### Q1: Task 工具和 MCP 工具有什么区别？

**核心区别**：Task 是**调用机制**，MCP 是**能力扩展**。

| 方面 | Task 工具 | MCP 工具 |
|------|-----------|----------|
| **本质** | 派发 SubAgent 的调用机制 | 领域能力的封装 |
| **定义方式** | SubAgent 通过 `AgentDefinition` 定义 | 通过 `@tool` 装饰器 |
| **用途** | AI 自主决策，派发复杂子任务 | 提供特定领域功能 |

### Q2: Windows 环境需要注意什么？

```python
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
async for msg in client.receive_response():
    print(f"Message type: {type(msg)}")
    print(f"Content: {msg}")
```

2. **启用详细日志**：
```python
import logging
logging.basicConfig(level=logging.DEBUG)
```

---

## 附录 A: 技术栈

- **语言**：Python 3.10+
- **框架**：Anthropic Agent SDK
- **模型**：Claude Sonnet 4
- **依赖**：`pip install anthropic`

---

## 附录 B: 相关文档

| 文档 | 路径 | 内容 |
|------|------|------|
| Agent 架构设计 | [Agent_Design.md](./Agent_Design.md) | SubAgent 架构、提示词设计 |
| 并行设计模式 | [Agent_SDK_Parallel.md](./Agent_SDK_Parallel.md) | 三大支柱、核心场景、Git Worktree 架构 |
| 空间理解 | [Agent_Spatial.md](./Agent_Spatial.md) | AI 空间理解 |
| 业务流程 | [Flow_Workflows.md](./Flow_Workflows.md) | 端到端工作流 |

### 官方文档

- [Agent SDK Overview](https://docs.anthropic.com/en/docs/claude-code/agent-sdk/overview)
- [Agent SDK SubAgents](https://docs.anthropic.com/en/docs/claude-code/agent-sdk/subagents)
- [Agent SDK MCP](https://docs.anthropic.com/en/docs/claude-code/agent-sdk/mcp)
- [Agent SDK Hooks](https://docs.anthropic.com/en/docs/claude-code/agent-sdk/hooks)

---

## 附录 C: 版本历史

| 版本 | 日期 | 变更 |
|------|------|------|
| v1.0~v1.5 | 2026-01-05~06 | Agent_SDK_Technical_Guide 初始版本及更新 |
| v1.0 | 2025-12-30 | AI_Parallel_Design_Patterns 初始版本 |
| v2.0 | 2026-01-13 | 合并两文档，强调 ClaudeSDKClient 为主 Agent 推荐方案 |
