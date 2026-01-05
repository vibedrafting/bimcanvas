# Agent SDK 技术指南

> **版本**：v1.0 | **更新日期**：2026-01-05
> **目的**：记录 BIMCanvas Agent 项目的技术细节、架构决策和最佳实践

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

### 1.3 关键配置项

| 配置项 | 类型 | 说明 |
|--------|------|------|
| `system_prompt` | str | 系统提示词，定义 Agent 角色和行为 |
| `cwd` | str | 工作目录，文件工具的根路径 |
| `allowed_tools` | list | 允许使用的内置工具列表 |
| `permission_mode` | str | 权限模式：`"default"` / `"acceptEdits"` |
| `max_turns` | int | 最大对话轮次（工具调用次数） |
| `max_thinking_tokens` | int | 思考 token 上限（启用扩展思考） |
| `resume` | str | 会话 ID，用于恢复之前的对话 |
| `mcp_servers` | list | 启用的 MCP Server 列表 |

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

## 三、SubAgent 实现方案

### 3.1 设计目标

```
┌─────────────────────────────────────────────────────────────────┐
│  主 Agent（后台常驻）                                            │
│  ├── 分析用户需求                                                │
│  ├── 制定任务计划                                                │
│  ├── 派发子任务 ──────────────────────────────────────┐         │
│  └── 整合结果                                          │         │
│                                                        ↓         │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │  SubAgent（临时存在）                                        ││
│  │  ├── 布置家具 Agent                                          ││
│  │  ├── 分区设计 Agent                                          ││
│  │  ├── 策略生成 Agent                                          ││
│  │  └── Git 管理 Agent                                          ││
│  └─────────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────┘
```

### 3.2 方案对比

| 方案 | 实现方式 | 优点 | 缺点 |
|------|----------|------|------|
| **Task 工具** | 启用内置 Task 工具 | 简单、原生支持 | SubAgent 继承父配置，无法自定义 |
| **MCP 工具** | 把 SubAgent 注册为 MCP 工具 | 完全可控、专业化 | 需要额外开发 MCP Server |
| **程序路由** | Python 代码判断后调用不同方法 | 最简单 | AI 无法自主决策 |

### 3.3 方案一：Task 工具（推荐入门）

**原理**：Claude Code 内置 Task 工具，可启动子 Agent 会话。

**配置**：

```python
options = ClaudeAgentOptions(
    system_prompt="""你是 BIMCanvas 主控 Agent。

## 职责
1. 分析用户需求，制定任务计划
2. 使用 Task 工具派发子任务
3. 整合子任务结果，向用户汇报

## 子任务派发
当任务复杂或需要专业处理时，使用 Task 工具：

- 布置任务：Task(prompt="为[房间]布置[风格]家具，读取 room_zones.json 和 openings.json，输出到 modules.json")
- 分区任务：Task(prompt="分析[房间]空间，设计功能分区")
- Git 任务：Task(prompt="创建分支 feature/xxx 并切换")

## 注意事项
- 每个子任务应该是独立、完整的
- 子任务完成后整合结果
- 复杂任务可以并行派发多个子任务
""",
    allowed_tools=["Read", "Write", "Glob", "Bash", "Task"],
    max_turns=20,
)
```

**工作流程**：

```
用户: "帮我布置整个户型"
    ↓
主 Agent 分析:
    "这个户型有客厅、主卧、次卧，需要分别布置"
    ↓
主 Agent 调用 Task 工具:
    Task(prompt="为客厅布置家具...")
    Task(prompt="为主卧布置家具...")
    Task(prompt="为次卧布置家具...")
    ↓
各子 Agent 执行，返回结果
    ↓
主 Agent 整合:
    "已完成所有房间布置，客厅放置了沙发、茶几..."
```

### 3.4 方案二：MCP 工具（推荐生产）

**原理**：把每个 SubAgent 封装为 MCP 工具，主 Agent 可调用。

**优势**：
- 每个 SubAgent 有专门的 System Prompt
- 可以限制每个 SubAgent 的工具权限
- 更好的职责隔离和错误处理

**实现步骤**：

#### 步骤 1：创建 MCP Server

```python
# mcp_agents_server.py
from mcp import Server
from claude_agent_sdk import query, ClaudeAgentOptions

server = Server("bimcanvas-agents")

# 布置家具 SubAgent
PLACEMENT_PROMPT = """你是布置家具专家。
- 精通家具布置规则
- 熟悉空间动线设计
- 输出符合 modules.json 格式
"""

@server.tool()
async def placement_agent(room_id: str, style: str, project_path: str) -> str:
    """
    布置家具 SubAgent。
    为指定房间布置符合风格的家具。

    Args:
        room_id: 房间 ID
        style: 设计风格
        project_path: 项目路径
    """
    options = ClaudeAgentOptions(
        system_prompt=PLACEMENT_PROMPT,
        cwd=project_path,
        allowed_tools=["Read", "Write", "Glob"],
        permission_mode="acceptEdits",
        max_turns=10,
    )

    prompt = f"为房间 {room_id} 布置 {style} 风格家具"

    result = ""
    async for msg in query(prompt=prompt, options=options):
        if isinstance(msg, AssistantMessage):
            for block in msg.content:
                if isinstance(block, TextBlock):
                    result += block.text
    return result


# 分区设计 SubAgent
ZONE_DESIGN_PROMPT = """你是空间分区专家。
- 精通大空间功能划分
- 熟悉人体工程学
- 输出合理的功能分区方案
"""

@server.tool()
async def zone_design_agent(room_id: str, project_path: str) -> str:
    """分区设计 SubAgent。分析大空间，设计功能分区。"""
    options = ClaudeAgentOptions(
        system_prompt=ZONE_DESIGN_PROMPT,
        cwd=project_path,
        allowed_tools=["Read", "Write"],
        max_turns=5,
    )
    # ... 实现


# Git 管理 SubAgent
@server.tool()
async def git_manager(action: str, branch_name: str = "") -> str:
    """Git 分支管理工具。创建、切换、合并分支。"""
    # 直接执行 git 命令，不需要启动 Agent
    import subprocess
    if action == "create":
        result = subprocess.run(["git", "checkout", "-b", branch_name], capture_output=True)
    elif action == "switch":
        result = subprocess.run(["git", "checkout", branch_name], capture_output=True)
    # ...
    return result.stdout.decode()
```

#### 步骤 2：主 Agent 配置

```python
# main_agent.py

MAIN_AGENT_PROMPT = """你是 BIMCanvas 主控 Agent。

## 可用工具

### 内置工具
- Read/Write/Glob: 文件操作
- Bash: 执行命令

### SubAgent 工具（通过 MCP）
- placement_agent(room_id, style, project_path): 布置家具专家
- zone_design_agent(room_id, project_path): 分区设计专家
- git_manager(action, branch_name): Git 分支管理

## 工作流程
1. 分析用户需求
2. 制定任务计划
3. 调用合适的 SubAgent 工具
4. 整合结果并汇报
"""

async def run_main_agent(user_request: str, project_path: str):
    options = ClaudeAgentOptions(
        system_prompt=MAIN_AGENT_PROMPT,
        cwd=project_path,
        allowed_tools=[
            "Read", "Write", "Glob", "Bash",
            "mcp__bimcanvas-agents__placement_agent",
            "mcp__bimcanvas-agents__zone_design_agent",
            "mcp__bimcanvas-agents__git_manager",
        ],
        mcp_servers=["bimcanvas-agents"],
        max_turns=30,
    )

    async for msg in query(prompt=user_request, options=options):
        yield msg
```

### 3.5 方案三：程序路由（最简单，但 AI 无法自主）

**原理**：Python 代码根据条件调用不同方法。

```python
class PlacementAgent:
    async def chat(self, message: str) -> str:
        """普通对话"""
        # ... 配置 A

    async def run_layout(self, prompt: str) -> str:
        """布置任务"""
        # ... 配置 B

    async def run_zone_design(self, prompt: str) -> str:
        """分区任务"""
        # ... 配置 C

# HTTP 路由
@app.post("/api/chat")
async def chat_handler():
    return await agent.chat(message)

@app.post("/api/task/layout")
async def layout_handler():
    return await agent.run_layout(prompt)
```

**限制**：AI 无法自主选择调用哪个方法，必须由程序或用户决定。

---

## 四、最佳实践

### 4.1 System Prompt 设计原则

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

### 4.2 工具权限最小化

```python
# 不好：给所有权限
allowed_tools=["Read", "Write", "Edit", "Bash", "Task", ...]

# 好：按需授权
allowed_tools=["Read", "Glob"]  # 只读任务
allowed_tools=["Read", "Write"]  # 读写任务
```

### 4.3 错误处理

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

### 4.4 会话管理

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
            # 捕获新会话 ID
            if hasattr(msg, 'subtype') and msg.subtype == 'init':
                self.session_id = msg.data.get('session_id')
            # ...

    def clear(self):
        self.session_id = None
```

---

## 五、常见问题

### Q1: Task 工具和 MCP 工具有什么区别？

| 方面 | Task 工具 | MCP 工具 |
|------|-----------|----------|
| 定义位置 | Claude Code 内置 | 开发者自定义 |
| System Prompt | 继承父 Agent | 完全自定义 |
| 工具权限 | 继承父 Agent | 可以限制 |
| 适用场景 | 通用子任务 | 专业化子任务 |

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

## 六、版本历史

| 版本 | 日期 | 更新内容 |
|------|------|----------|
| v1.0 | 2026-01-05 | 初始版本：核心概念、SubAgent 方案、最佳实践 |

---

## 七、待研究问题

- [ ] Task 工具的详细参数和行为
- [ ] MCP Server 与 Agent SDK 的集成方式
- [ ] 多 Agent 并行执行的实现
- [ ] Agent 间通信机制
- [ ] 长时间任务的状态管理

---

## 参考资料

- [Claude Agent SDK 官方文档](https://docs.anthropic.com/claude-code/agent-sdk)
- [MCP 协议规范](https://modelcontextprotocol.io)
- [BIMCanvas Agent MVP 计划](../plans/Agent_MVP.md)
