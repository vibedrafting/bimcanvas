# BIMCanvas Agent

基于多 Runtime 适配层的 AI 室内布置助手。当前 Host 可在 `claude-sdk` 与 `openai-agents` 之间按进程级配置切换。

## 架构定位

```
┌─────────────────────────────────────────────────────────────┐
│                      BIMCanvas 系统                          │
├─────────────────────────────────────────────────────────────┤
│  Web (Vue)  ←──HTTP──→  Server (.NET)  ←──HTTP──→  Agent   │
│   前端 UI                 状态管理                  AI 决策  │
└─────────────────────────────────────────────────────────────┘
```

**Agent 角色**：系统的"大脑"，负责理解用户意图、规划布置方案、发出操作指令。

**协议阶段**：当前处于 Runtime v0.1 收尾阶段。Host 已抽出 runtime-neutral `StreamChunk + MainStreamMapper`，Claude 与 OpenAI 都经由统一 ControlPlane 暴露 capability matrix，并固定 `SESSION_EXPIRED` / `SESSION_PAUSED` / `SESSION_ERROR` 错误语义。

## 快速开始

### 1. 前置条件

**Windows 环境**：
```bash
# Agent SDK 需要 git-bash，如果 Git 不在 PATH 中，需设置环境变量
set CLAUDE_CODE_GIT_BASH_PATH=D:\Git\bin\bash.exe
```

**可选**：安装 Claude Code CLI
```bash
npm install -g @anthropic-ai/claude-code
```

### 2. 安装依赖

```bash
cd BIMCanvas.Agent
pip install -e .
```

### 3. 配置环境变量

```bash
# 复制配置模板
cp .env.example .env

# Claude Runtime
# ANTHROPIC_API_KEY=your-api-key-here

# OpenAI Runtime
# AGENT_RUNTIME_PROVIDER=openai-agents
# OPENAI_API_KEY=your-openai-api-key
# OPENAI_BASE_URL=https://api.openai.com/v1
```

### 4. 启动服务

Agent 只能由 `BIMCanvas.Server` 托管启动，不支持脱离 Server 独立运行。

Agent 会在 BIMCanvas.Server 启动时自动启动，无需手动操作。
当 `<BIMCANVAS_HOME>/server_config.json > ccr.enabled=true` 时，Server 会注入
`AGENT_SDK_BASE_URL`、`AGENT_SDK_API_KEY` 以及 Claude Code 家族模型映射环境变量，
默认把主模型、background requests、subagent 请求都指向 CCR 网关。

## API 接口

| 端点 | 方法 | 说明 |
|------|------|------|
| `/health` | GET | 健康检查 |
| `/api/chat` | POST | 发送聊天消息（同步响应） |
| `/api/chat/stream` | POST | 发送聊天消息（SSE 流式响应） |
| `/api/clear-history` | POST | 清空对话历史 |
| `/api/history` | GET | 获取对话历史 |
| `/api/config` | GET | 获取 Agent 配置（模型、思考强度、Runtime capability matrix） |
| `/api/agent/close` | POST | 关闭指定窗口的 Agent 实例 |
| `/api/interrupt` | POST | 中断当前 Agent 执行 |
| `/api/interaction/events` | GET | 统一 InteractionChannel SSE 事件流 |
| `/api/interaction` | GET | 查询当前窗口活跃 session 的 unresolved interactions |
| `/api/interaction/{id}/submit` | POST | 提交 interaction 结果 |
| `/api/interaction/{id}/cancel` | POST | 取消 interaction |
| `/api/question/events` | GET | 兼容问题 SSE 通道（底层复用统一 interaction store） |
| `/api/question/answer` | POST | 兼容问题回答端点（底层复用统一 interaction store） |
| `/api/screenshot/events` | GET | 截图 SSE 事件流（Web→Agent 截图通道） |
| `/api/screenshot/request` | POST | 兼容截图请求端点（底层复用统一 interaction store） |
| `/api/screenshot/result` | POST | 兼容截图结果端点（底层复用统一 interaction store） |
| `/api/screenshot/save` | POST | 保存截图到项目目录 |

### 请求示例

```http
POST /api/chat
Content-Type: application/json

{
  "projectPath": "C:/Users/.../Projects/demo_1",
  "model": "sonnet",
  "message": "帮我设计客厅的布置方案"
}
```

### 响应示例

```json
{
  "reply": "好的，我来帮您设计客厅布置方案...",
  "projectPath": "C:/Users/.../Projects/demo_1"
}
```

### SSE 流式响应

```http
POST /api/chat/stream
Content-Type: application/json

{
  "projectPath": "...",
  "clientMessageId": "msg_xxx",
  "attachmentIds": ["att_xxx", "att_yyy"],
  "model": "sonnet",
  "message": "..."
}
```

说明：

- Web 端不再把整张截图 base64 直接放进 `/api/chat/stream`
- Agent 会根据 `projectPath/screenshots/_chat_attachments.json` 解析 `attachmentIds`
- 发送给模型前，Agent 会先做缩放 / 降采样 / 格式兜底，避免再次触发超限
- 兼容期内仍支持旧字段 `images: string[]`，但 Web 已切换到 `attachmentIds`

响应格式（Server-Sent Events）：
```
data: {"eventId":"...","sessionId":"...","turnId":"...","eventType":"thinking.delta","payload":{"content":"让我分析一下..."},"type":"thinking","content":"让我分析一下..."}
data: {"eventId":"...","sessionId":"...","turnId":"...","eventType":"text.delta","payload":{"content":"好的"},"type":"text","content":"好的"}
data: {"eventId":"...","sessionId":"...","turnId":"...","eventType":"turn.completed","payload":{"stopReason":"completed"}}
...
data: [DONE]
```

说明：

- Slice B/C 兼容期内，主流事件采用“双写”模式：同一条 `data:` 同时带 v0.1 envelope 字段和 legacy flat 字段
- envelope 最小字段：`eventId`、`sessionId`、`turnId`、`eventType`、`payload`
- 当前前端仍消费 legacy `type` / flat fields / `[DONE]`，因此这些字段继续保留
- `session_ready` 保持旧特例格式，不升级为 envelope
- `task_output_polling` 保持 ClaudeRuntime 私有 legacy flat 事件，不进入 envelope
- `turn.completed` / `turn.failed` 由 Host 显式合成；legacy `[DONE]` 继续保留

### ControlPlane 配置

`GET /api/config` 在保留现有 `models` / `defaultEffort` / `defaultThinking` 字段的同时，额外返回：

```json
{
  "runtime": "claude-sdk",
  "runtimeVersion": "0.1.0",
  "capabilityMatrix": [
    {
      "capabilityKey": "text_stream",
      "level": "required",
      "providerMapping": "content_block_delta.text_delta + text.completed",
      "frontendFallback": null,
      "notes": "..."
    }
  ]
}
```

当前矩阵的关键能力声明：

- `text_stream` / `tool_call_lifecycle` / `interaction_query` / `interaction_submit` / `interaction_cancel` / `question_pause_resume` / `screenshot_async`：`required`
- `thinking`：`optional`
- `subtask_causality`：Claude 为 `optional`；OpenAI phase 1 为 `unsupported`
- `usage` / `trace` / `permission_pause_resume`：`unsupported`

### Runtime 选择

Host 通过 `runtimeProvider` 决定使用哪套适配器：

```json
{
  "runtimeProvider": "claude-sdk"
}
```

或：

```json
{
  "runtimeProvider": "openai-agents"
}
```

也可由环境变量覆盖：

```bash
set AGENT_RUNTIME_PROVIDER=openai-agents
```

当前约束：

- 单个 Agent Host 进程只运行一种 Runtime
- `http_server.py` 通过工厂创建 `HostAgentProtocol`，不再直接依赖 Claude `MainAgent`
- 会话内不支持热切换 Runtime；切换 provider 需要重建该窗口的 Agent/session

### OpenAI Pause/Resume

OpenAI 首版适配走原生 `FunctionTool(needs_approval=True) + RunState` 路径：

- 普通多轮对话使用 OpenAI SDK `SQLiteSession`，并绑定到 Host `sessionId`，由应用侧持久维护历史
- `AskUserQuestion` 会被投影为 `PendingInteractionRecord(kind=question, blocking=true)`
- Host 私下保存 `PendingInteractionRuntimeBinding`，其中包含 `runStateJson`、`approvalCallId`、`projectionState`
- Web 端提交 `/api/interaction/{id}/submit` 或兼容 `/api/question/answer` 后，Host 会恢复 `Runner.run_streamed()` 继续同一 turn
- `resumeToken` 支持跨 SSE 断线 / 页面 reload；但 v0.1 不保证 Agent Host 进程重启后仍可恢复

### OpenAI 阶段一范围

OpenAI runtime 第一阶段只提供稳定基础 Runtime，不追求与 Claude 工作流等价：

- 支持文本对话
- 支持图片输入
- 支持 `AskUserQuestion -> PendingInteractionRecord -> RunState resume`
- Root Agent 注册本地 function tools：`Read / Write / Edit / Glob / Grep / Bash / AskUserQuestion`
- Root Agent 额外挂载两个 native helper sub-agents：
  - `delegate_query_task`：只读子任务，子代理工具限定为 `Read / Glob / Grep`
  - `delegate_edit_task`：单一编辑子任务，子代理工具限定为 `Read / Write / Edit / Glob / Grep`
- 支持把 `<BIMCANVAS_HOME>/agents/*.md` 中“纯 prompt + 本地工具”的配置型 agents 投影为原生 OpenAI agent tools
- Subtask 事件通过 OpenAI 原生 `Agent.as_tool()` 投影为 `subtask.started / subtask.completed`
- 不注册 Claude 风格 `Task` 兼容壳
- 不注册 `Skill / Plugin`
- 不注册任何 `mcp__canvas__*`
- 依赖 `Skill / mcp__canvas__* / AskUserQuestion / Task` 的配置型 agents 暂不注册；当前 `layout-agent` 继续后移到 Skill/MCP 阶段

### ControlPlane 错误语义

对 `POST /api/chat` / `POST /api/chat/stream`，Host 固定以下控制面错误码：

- `SESSION_EXPIRED`：请求携带的 `X-Session-Id` 与当前窗口活跃 session 不一致
- `SESSION_PAUSED`：当前 session 仍有 `blocking=true && status=pending` 的 interaction
- `SESSION_ERROR`：当前 session 已进入不可恢复错误状态，需要重建

这些错误继续返回 JSON，并在可用时附带当前 `X-Session-Id` 响应头，供客户端重新同步 session。

补充说明：`turn.failed.payload.error.code` 中若出现 `PROVIDER_*` 前缀，表示 ClaudeRuntime 私有扩展码，不属于 v0.1 最小错误枚举；前端和联调脚本不得依赖这些扩展码做渲染分支决策。

## SSE 事件协议

Agent 通过 SSE（Server-Sent Events）推送实时事件。Slice B/C 期间同时存在两层协议：

- 标准 envelope：`eventType` + `payload`
- legacy 兼容层：`type` + flat fields + `[DONE]`

### 事件类型一览

| 标准 `eventType` | legacy `type` | 说明 |
|----------|------|----------|
| `thinking.delta` | `thinking` | 思考内容（流式） |
| `thinking.completed` | `thinking_complete` | 思考完成 |
| `text.delta` | `text` | 文本响应（流式） |
| `text.completed` | `text_complete` | 文本完成 |
| `subtask.started` | `subagent_start` | SubAgent 启动 |
| `subtask.completed` | `subagent_complete` | SubAgent 完成 |
| `tool.started` | `tool_call_start` | 工具调用开始 |
| `tool.output` | `tool_call_output` | 工具输出；仅原始 `toolOutput` 非空时补发 legacy 事件 |
| `tool.completed` | `tool_call_complete` | 工具调用完成 |
| `turn.completed` | 无，对应 legacy `[DONE]` | 本轮正常完成，`payload.stopReason='completed'` |
| `turn.failed` | 无，异常路径仍附带 flat `{"error": ...}` | 本轮失败终态 |
| 无 | `session_ready` | 会话 bootstrap 特例，不进入 envelope |
| 无 | `task_output_polling` | ClaudeRuntime 私有 legacy 事件，不进入 envelope |

### SubAgent 事件

当 AI 决定派发 SubAgent（如 Explore、Plan、layout-agent）时，会产生以下事件序列：

**1. SubAgent 启动事件**
```json
{
  "type": "subagent_start",
  "subAgentId": "sa-toolu_01ABC123",
  "subAgentName": "探索项目结构",
  "subAgentType": "Explore"
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `subAgentId` | string | SubAgent 唯一标识，格式 `sa-{tool_use_id}` |
| `subAgentName` | string | 任务描述（来自 Task 工具的 description 参数） |
| `subAgentType` | string | SubAgent 类型：`Explore`、`Plan`、`general-purpose`、`layout-agent` 等 |

**2. SubAgent 完成事件**
```json
{
  "type": "subagent_complete",
  "subAgentId": "sa-toolu_01ABC123",
  "content": "分析完成，发现3个房间...",
  "success": true
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `subAgentId` | string | 对应的 SubAgent 标识 |
| `content` | string | 执行结果摘要（最多500字符） |
| `success` | boolean | 是否成功 |
| `error` | string | 失败时的错误信息 |

### 工具调用事件

SubAgent 内部执行工具时，会产生以下事件：

**1. 工具调用开始**
```json
{
  "type": "tool_call_start",
  "subAgentId": "sa-toolu_01ABC123",
  "toolCallId": "tc-1",
  "toolName": "Read",
  "toolDescription": "读取房间数据",
  "toolParams": {
    "file_path": "C:/Projects/demo/computed/room_zones.json"
  }
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `subAgentId` | string | 所属 SubAgent 的标识 |
| `toolCallId` | string | 工具调用唯一标识，格式 `tc-{递增数字}` |
| `toolName` | string | 工具名称：`Read`、`Write`、`Glob`、`Grep`、`Edit`、`Bash` 等 |
| `toolDescription` | string | 工具调用描述 |
| `toolParams` | object | 工具参数（如 file_path、pattern、command 等） |

**2. 工具输出（可选，流式）**
```json
{
  "eventType": "tool.output",
  "payload": {
    "output": "文件内容..."
  },
  "type": "tool_call_output",
  "toolCallId": "tc-1",
  "toolOutput": "文件内容..."
}
```

说明：当 `tool.output` 已经输出原始内容时，紧随其后的 legacy `tool_call_complete` 不再重复携带 `toolOutput`，避免当前前端重复追加同一份输出。

**3. 工具调用完成**
```json
{
  "eventType": "tool.completed",
  "payload": {
    "output": "文件内容...",
    "success": true
  },
  "type": "tool_call_complete",
  "toolCallId": "tc-1",
  "success": true
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `toolCallId` | string | 对应的工具调用标识 |
| `success` | boolean | 是否成功 |
| `error` | string | 失败时的错误信息 |

### 完整事件流示例

以下是用户询问"看一看当前户型有几个卧室"时的完整事件序列：

```
# 1. AI 开始思考
data: {"type": "thinking", "content": "用户想了解户型的卧室数量..."}

# 2. AI 决定派发 SubAgent
data: {"type": "subagent_start", "subAgentId": "sa-toolu_01X", "subAgentName": "查看户型卧室分布", "subAgentType": "layout-agent"}

# 3. SubAgent 开始执行工具
data: {"type": "tool_call_start", "subAgentId": "sa-toolu_01X", "toolCallId": "tc-1", "toolName": "Read", "toolParams": {"file_path": ".../room_zones.json"}}

# 4. 工具执行完成
data: {"type": "tool_call_complete", "toolCallId": "tc-1", "success": true}

# 5. SubAgent 可能执行更多工具...
data: {"type": "tool_call_start", "subAgentId": "sa-toolu_01X", "toolCallId": "tc-2", "toolName": "Read", "toolParams": {"file_path": ".../openings.json"}}
data: {"type": "tool_call_complete", "toolCallId": "tc-2", "success": true}

# 6. SubAgent 完成
data: {"type": "subagent_complete", "subAgentId": "sa-toolu_01X", "content": "分析完成", "success": true}

# 7. AI 输出最终响应
data: {"type": "text", "content": "根据分析，当前户型共有3个卧室..."}
data: {"type": "text_complete", "content": "...完整内容"}

# 8. Host 显式发送 turn 终态
data: {"eventType": "turn.completed", "payload": {"stopReason": "completed"}}

# 9. legacy 结束标记
data: [DONE]
```

### 前端处理建议

```typescript
// 1. 解析 SSE 事件
const eventSource = new EventSource('/api/chat/stream');
eventSource.onmessage = (event) => {
  if (event.data === '[DONE]') {
    eventSource.close();
    return;
  }

  const parsed = JSON.parse(event.data);

  switch (parsed.type) {
    case 'subagent_start':
      // 创建 SubAgent 卡片，记录 startTime
      createSubAgentCard(parsed.subAgentId, parsed.subAgentName, parsed.subAgentType);
      break;

    case 'tool_call_start':
      // 在对应 SubAgent 下添加工具调用项
      addToolCall(parsed.subAgentId, parsed.toolCallId, parsed.toolName, parsed.toolParams);
      break;

    case 'tool_call_complete':
      // 更新工具状态，记录 endTime
      updateToolStatus(parsed.toolCallId, parsed.success, parsed.error);
      break;

    case 'subagent_complete':
      // 更新 SubAgent 状态，记录 endTime
      updateSubAgentStatus(parsed.subAgentId, parsed.success, parsed.error);
      break;

    case 'text':
      // 追加文本内容
      appendText(parsed.content);
      break;
  }
};
```

### 流式输出配置要点

要实现真正的流式文本输出（逐字显示），必须在 `ClaudeAgentOptions` 中启用 `include_partial_messages`：

```python
ClaudeAgentOptions(
    ...
    include_partial_messages=True,  # 关键配置
)
```

**为什么需要这个配置？**

| 配置 | SDK 返回内容 | 文本事件类型 | 前端效果 |
|------|-------------|-------------|---------|
| `False`（默认） | 完整的 `AssistantMessage` | `text_complete`（一次性） | 文本突然出现 |
| `True` | 增量的 `StreamEvent` | `text`（逐字） | 文本逐字流式显示 |

**典型问题场景**：SubAgent 完成后，父 Agent 生成的总结文本不是流式输出，而是一次性显示。

**原因**：未启用 `include_partial_messages`，SDK 默认返回完整消息而非增量事件。

**解决**：在 `main_agent.py` 的 `_create_options()` 中添加 `include_partial_messages=True`。

> 参考：`docs/Agent_SDK/examples/claude-agent-sdk-python/examples/include_partial_messages.py`

> **注意**：布置任务已整合到 `/api/chat/stream`，MainAgent 自主决定何时派发 layout-agent SubAgent，无需专用端点。

## 项目结构

```
BIMCanvas.Agent/
├── pyproject.toml              # 项目配置 + 依赖声明
├── .env.example                # 环境变量模板
├── README.md                   # 本文件
│
├── src/
│   ├── __init__.py
│   ├── main.py                 # 入口文件（CLI 参数解析）
│   │
│   ├── agent/
│   │   ├── __init__.py
│   │   ├── main_agent.py       # MainAgent（主控 Agent）
│   │   ├── subagents.py        # SubAgent 定义（从配置加载）
│   │   ├── agent_logger.py     # Agent 日志系统
│   │   └── worktree_manager.py # Worktree 生命周期管理
│   │
│   ├── server/
│   │   ├── __init__.py
│   │   └── http_server.py      # HTTP 服务（aiohttp + CORS）
│   │
│   ├── runtime/
│   │   ├── __init__.py
│   │   ├── records.py          # RuntimeSessionRecord / PendingInteractionRecord
│   │   └── store.py            # Host 侧 session / interaction 真相源
│   │
│   ├── tools/
│   │   ├── __init__.py
│   │   ├── file_tools.py       # JSON 文件读写工具
│   │   ├── svg_parser.py       # SVG 解析工具
│   │   ├── placement_tools.py  # 布置工具（模块放置/移动/删除）
│   │   └── zone_tools.py       # 区域工具（Zone 查询/操作）
│   │
│   ├── mcp/
│   │   ├── __init__.py
│   │   └── canvas.py           # MCP 工具（create_job, complete_job, screenshot）
│   │
│   └── config/
│       ├── __init__.py
│       ├── settings.py         # 配置管理（从 loader 加载）
│       └── loader.py           # 统一配置加载器
│
├── MOSS/                       # 历史代码（仅供参考）
└── AgentSDK-Quickstart.md      # Agent SDK 快速入门文档
```

## 核心模块说明

### Runtime Adapters (`agent/`)

当前有两套 Host-facing adapter：

- `MainAgent`：Claude 专属实现，保留原有 `Task` / `can_use_tool` 路径
- `OpenAIAgent`：OpenAI Agents SDK 适配器，阶段一只负责本地 function tools、图片输入与 `RunState` pause/resume

它们都实现同一个 `HostAgentProtocol`，并通过 `agent/factory.py` 由 `http_server.py` 统一创建。

### MainAgent (`agent/main_agent.py`)

基于 Claude Agent SDK 的主控 Agent，采用 MainAgent + SubAgent 架构：

**架构特点**：
- 使用 ClaudeSDKClient 维持持久连接
- 通过 Task 工具自动派发 SubAgent（如 layout-agent）
- 系统提示词和 SubAgent 配置从外部文件加载

**对话接口**：
- **chat(message)** - 同步对话，返回完整响应
- **chat_stream(message)** - 流式对话，支持 SubAgent 事件追踪

**会话管理**：
- **connect()** / **disconnect()** - 连接管理
- **clear_history()** - 清空会话
- **set_project_path(path)** - 设置项目路径

### SubAgents (`agent/subagents.py`)

从 `<BIMCANVAS_HOME>/agents/*.md` 配置文件加载 SubAgent 定义：

- **layout-agent** - 单房间设计专家，负责单区 generate 链路自动执行

### HTTP Server (`server/http_server.py`)

基于 aiohttp 的 HTTP 服务：

- 支持 CORS 跨域（Web 前端调用）
- 按 `windowId` 复用 Agent 实例，并由 Host 颁发 `sessionId`
- `POST /api/chat/stream` 每次响应头都会返回 `X-Session-Id`
- 主流仍保持现有 flat SSE，新增统一 `/api/interaction*` 通道
- `get_agent()` 已通过工厂模式切换 Claude / OpenAI adapter
- OpenAI question resume 在 Host 侧完成，不新增 `/api/chat/resume` 流接口

### Runtime Store (`runtime/`)

Host 进程内的运行时真相源：

- `RuntimeSessionRecord`：记录 `sessionId`、window 绑定、项目路径、活跃 turn 等 session 元数据
- `PendingInteractionRecord`：统一承载 `question` / `screenshot` 等 interaction 状态
- `PendingInteractionRuntimeBinding`：OpenAI pause/resume 私有绑定，保存 `RunStateJson + projectionState`
- `RuntimeStateStore`：维护 `windowId -> sessionId`、`sessionId -> pending interactions` 与统一 interaction SSE 发布

### 配置系统 (`config/`)

**配置文件驱动架构**：全局配置由 Server 启动早期统一初始化到 `<BIMCANVAS_HOME>/`。

```
<BIMCANVAS_HOME>/             ← 同时作为 Agent 配置目录和 Claude Plugin 目录
├── .claude-plugin/
│   └── plugin.json        # Plugin 清单（使该目录成为合法 Plugin）
├── skills/                # Agent Skills（通过 Plugin 机制加载，避免 CLAUDE.md 污染）
│   ├── query-workflow/
│   ├── edit-workflow/
│   ├── generate-reference-analysis/
│   ├── generate-planning/
│   ├── generate-placement/
│   └── generate-zoning/
├── BIMCANVAS.md           # 主 Agent 系统提示词（可编辑）
├── config.json            # 应用配置（API、模型、工具）
└── agents/
    └── layout-agent.md    # SubAgent 配置（YAML frontmatter + 提示词）
```

默认路径规则：
- Windows：`Documents/BIMCanvas`
- 非 Windows：`~/.bimcanvas`

**配置原则**：

- Agent 只从 `<BIMCANVAS_HOME>/config.json` 读取连接参数与推理参数。
- Web 对话默认模型统一存放在 `<BIMCANVAS_HOME>/web_config.json > defaultModel`，
  并由 Web 在聊天请求中显式传给 Agent。
- 当 `runtimeProvider=openai-agents` 时，不再支持 `opus / sonnet / haiku` 这类 Claude alias。
  `config.json > modelMapping` 的 key 必须就是实际 OpenAI model id，且 entry.id 必须与 key 相同；
  `web_config.json > defaultModel` 也必须直接写实际 OpenAI model id。

#### config.json 格式

```json
{
  "runtimeProvider": "claude-sdk",
  "baseUrl": "https://your-direct-provider.example/v1",
  "apiKey": "your-direct-api-key",
  "openaiApi": "responses",
  "openaiDisableTracing": false,
  "defaultEffort": "medium",
  "defaultThinking": "adaptive",
  "maxThinkingTokens": 16000,
  "permissions": {
    "allow": ["Read", "Glob", "Grep", "Task", "AskUserQuestion"],
    "deny": []
  },
  "server": { "host": "127.0.0.1", "port": 8865 }
}
```

#### 直连模式与 CCR 托管模式

- 直连模式：Agent 使用 `config.json` 中的 `baseUrl`、`apiKey` 直连下游。
- CCR 模式：`config.json` 中的 `baseUrl` 和 `apiKey` 仅保留，不参与当前请求链路；实际连接参数由 Server 注入的 CCR 网关环境变量托管。
- 默认模型不再由 Agent 配置保存，所有 Web 对话请求都必须显式携带 `model`。

#### permissions 字段说明

`permissions` 字段通过 allow/deny 列表控制 Agent 可用工具权限：

| 字段 | 效果 |
|------|------|
| `allow` | 白名单：只允许使用列出的工具 |
| `deny` | 黑名单：禁止使用列出的工具 |

**注意**：

- `baseUrl` / `apiKey` 是“直连模式”配置；启用 CCR 托管后，它们不会被覆盖删除，但会暂时失效。
- `openaiApi` 仅在 `runtimeProvider=openai-agents` 时生效，取值只能是 `responses` 或 `chat_completions`。
- OpenAI Agents SDK 官方主路径是 `responses`；BIMCanvas 现在也默认走 `responses`。
- 使用第三方 OpenAI-compatible 网关时，建议先测试 `responses`；若网关在工具续跑或多轮状态上不兼容，再手动把 `openaiApi` 切回 `chat_completions` 作为兼容模式。
- 当前对“三方网关 + responses”增加了 Host 侧回退：若 SDK 的 `run_streamed()` 无法稳定完成工具续跑，BIMCanvas 会改用 `Runner.run()` 并把 `RunResult.new_items` 投影成事件流。这样能保住工具与暂停恢复，但文本会退化为完成态输出，不再是 token 级增量。
- `openaiDisableTracing` 仅在 `runtimeProvider=openai-agents` 时生效；第三方网关默认会关闭 tracing，避免把第三方 key 误发到 OpenAI traces 接口。
- OpenAI phase 1 会对 `permissions.allow/deny` 执行“配置权限 ∩ 本地工具支持集”裁剪。
  `Task`、`Skill`、`mcp__canvas__*` 等不在阶段一支持集内的工具会被忽略，并在启动日志中提示。

#### SubAgent 配置格式 (agents/*.md)

```markdown
---
name: layout-agent
description: 家具布置专家...
tools: Read, Glob, Write
model: inherit
---

（系统提示词内容）
```

#### 环境变量覆盖

| 环境变量 | 说明 |
|----------|------|
| `AGENT_SDK_API_KEY` | CCR 模式下的 Agent SDK API Key |
| `AGENT_SDK_BASE_URL` | CCR 模式下的 Agent SDK Base URL |
| `ANTHROPIC_DEFAULT_OPUS_MODEL` | 覆盖 Claude Code 的 `opus` 家族映射 |
| `ANTHROPIC_DEFAULT_SONNET_MODEL` | 覆盖 Claude Code 的 `sonnet` 家族映射 |
| `ANTHROPIC_DEFAULT_HAIKU_MODEL` | 覆盖 Claude Code 的 `haiku` / background 映射 |
| `SERVER_HOST` | 覆盖服务地址 |
| `SERVER_PORT` | 覆盖服务端口 |

**说明**：当 Agent 由 BIMCanvas.Server 托管启动且 `ccr.enabled=true` 时，上述 CCR 模式相关环境变量通常都由 Server 注入，无需手工写入 `.env`。

## 开发状态

### P1 阶段（Client SDK）- 已完成

- [x] Anthropic Client SDK 集成
- [x] HTTP 服务 + CORS
- [x] 基础对话功能
- [x] Web 前端集成
- [x] Server 自动启动 Agent

### P1.5 阶段（Agent SDK 迁移）- 已完成

- [x] 迁移到 Claude Agent SDK
- [x] 会话式对话管理（session_id）
- [x] 流式响应 + 思考过程展示
- [x] 多轮对话上下文验证

### P2 阶段（工具调用）- 当前

- [x] 启用 Agent SDK 内置工具（Read, Write, Glob, Edit）
- [x] 更新 System Prompt（布置任务指导）
- [x] ~~布置任务 API~~ → 已废弃，整合到 /api/chat（MainAgent 自主派发 SubAgent）
- [x] Skill 功能集成（Plugin 旁路策略，零配置污染）
- [ ] **端到端测试（完整布置流程验证）** ← 当前最紧迫
- [ ] Web 端布置任务集成

### P3 阶段（完整功能）- 待开发

- [ ] 编写生产级 Skills（layout-guide、git-workflow、furniture-catalog）
- [ ] 多轮对话上下文优化
- [ ] 布置方案评估与修正
- [ ] 与 Revit 回写集成

## 开发难点记录

本章节记录开发过程中遇到的技术难点及解决方案，供后续参考。

### 1. 连续对话失败（Thinking Signature）

| 项目 | 内容 |
|------|------|
| **现象** | 启用 thinking 后，第二条消息返回 400 错误：`Invalid signature in thinking block` |
| **根因** | Claude Agent SDK 在构建多轮对话请求时，把之前响应中的 thinking block（包含 signature）原封不动地放入历史消息中，导致 API 校验失败 |
| **方案** | **临时**：禁用 thinking（`max_thinking_tokens=None`）；**长期**：等待 SDK 修复 |
| **状态** | ✅ 临时方案已生效 |

### 2. SubAgent 结果传递（状态机追踪）

| 项目 | 内容 |
|------|------|
| **现象** | SubAgent 完成后，MainAgent 收到的结果是 `agentId: xxx` 而非实际输出 |
| **根因** | SDK 的 `ToolResultBlock.content` 返回的是恢复标识符，而非 SubAgent 的实际文本输出 |
| **方案** | 使用**状态机模式**追踪 `parent_tool_use_id`，从流式事件（`text_delta`）和 `AssistantMessage` 中收集实际文本 |
| **状态** | ✅ 已修复（v3 方案） |

**技术细节**：

- 官方示例（`research-agent`）也使用状态机追踪 SubAgent 上下文
- 流式 `text_delta` 事件没有 `parent_tool_use_id`，需要从 `AssistantMessage` 中获取并保存
- 核心变量：`_current_subagent_parent_id`、`_subagent_text_collector`

### 3. SubAgent 文本关联

| 项目 | 内容 |
|------|------|
| **现象** | 流式 `text_delta` 事件无法直接关联到 SubAgent |
| **根因** | SDK 的 `StreamEvent` 中 `parent_tool_use_id` 始终为 None，只有完整的 `AssistantMessage` 才有这个字段 |
| **方案** | 从 `AssistantMessage` 中获取 `parent_tool_use_id`，更新状态机的当前上下文，后续 `text_delta` 使用该上下文关联 |
| **状态** | ✅ 已修复（v3 方案） |

**关键代码**（`main_agent.py`）：

```python
# 状态机核心：从 AssistantMessage 更新当前 SubAgent 上下文
msg_parent_id = getattr(message, 'parent_tool_use_id', None)
if msg_parent_id and msg_parent_id in self._subagent_text_collector:
    self._current_subagent_parent_id = msg_parent_id
elif not msg_parent_id:
    self._current_subagent_parent_id = None
```

### 4. Skill 配置污染（Plugin 旁路策略）

| 项目 | 内容 |
|------|------|
| **现象** | 使用 `setting_sources=["project"]` 加载 Skills 时，CLI 同时注入了 `~/.claude/CLAUDE.md` 全局配置（Git 存档规则、MSBuild 路径等），导致 Agent 行为异常 |
| **根因** | SDK 的 `setting_sources` 是粗粒度开关，无法单独加载 Skills 而不加载其他配置。即使只设 `"project"`，CLI 仍扫描全局 CLAUDE.md |
| **方案** | **Plugin 旁路策略**：`setting_sources=None`（不加载任何配置）+ `plugins=[{"type": "local", "path": "<BIMCANVAS_HOME>"}]`（通过 Plugin 机制独立加载 Skills） |
| **状态** | ✅ 已解决（HTTP 抓包验证零污染） |

**技术细节**：

- `<BIMCANVAS_HOME>/` 同时作为 Agent 配置目录和 Claude Plugin 目录
- 需要 `.claude-plugin/plugin.json` 清单文件使目录成为合法 Plugin
- Skills 通过 `system-reminder` 注入，AI 通过 `Skill` 工具按需调用
- 详细研究报告见 `reports/Skill/Agent_SDK_Skill最终实践报告.md`

## 与 Server 集成

Agent 通过两种方式与 .NET Server 通信：
- **MCP 工具调用**：`mcp__canvas__create_job`、`mcp__canvas__request_background_screenshot` 等，通过进程内 MCP Server 调用 .NET REST API
- **文件驱动**：Agent 直接读写项目目录下的 JSON 文件，Server 的 FileWatcher 检测变化并通过 SignalR 推送给 Web 前端

## 开发指南

### 本地调试

```bash
# 1. 安装开发依赖
pip install -e ".[dev]"

# 2. 启动交互模式调试
python -m src.main

# 3. 或启动 HTTP 服务后用 curl 测试
python -m src.main --serve

curl -X POST http://127.0.0.1:8765/api/chat \
  -H "Content-Type: application/json" \
  -d '{"message": "你好"}'
```

### 独立图片生成调试

第一阶段新增了一个独立的图片处理客户端，不依赖 Agent/Server/Web 运行时，可直接用两张本地图测试 API：

```bash
cd BIMCanvas.Agent
python -m src.image_generation.cli ^
  --source "E:\工作文档\开发类\MyCode\BIMCanvas\references\凤栖湖127主卧.png" ^
  --style "E:\工作文档\开发类\MyCode\BIMCanvas\references\参考图.png" ^
  --output "E:\工作文档\开发类\MyCode\BIMCanvas\references\outputs\phase1_result.png"
```

如需临时覆盖默认 Key，可额外传入 `--api-key "..."`

当前 CLI 默认 prompt 已重构为 8 条规则、三级优先级（结构保真 > 家具保真 > 视觉风格）的精简版本：结构保真 + 家具矩形化 + 色彩键值映射 + 零幻觉优先；方向箭头规则已完全移除（旧版箭头规则导致方向幻觉）。同时 API 请求的 parts 顺序已调整为 `[source_image, style_image, text]`，与 Gemini 官方图像编辑示例一致，让模型先建立视觉锚点再阅读指令。

核心实现位于：

- `src/image_generation/nano_banana_client.py`
- `src/image_generation/cli.py`

### MCP 工具开发指南

#### 已集成 MCP 工具（canvas）

- `mcp__canvas__validate_layout`：布局编译检查
- `mcp__canvas__request_background_screenshot`：后台截图
- `mcp__canvas__get_zone_boundaries`：读取设计区边界语义
- `mcp__canvas__save_semantic_plan`：提交语义方案阶段图纸
- `mcp__canvas__load_semantic_plan`：读取当前生效图纸
- `mcp__canvas__save_reference_analysis`：保存独立 `reference_analysis.json` 完整版本快照
- `mcp__canvas__load_reference_analysis`：读取最新或指定版本的参考分析

#### 后台截图 MCP 工具（request_background_screenshot）

- **用途**：调用后台截图 API，保存到 `projectPath/screenshots`，返回保存后的完整路径。
- **参数**：仅开放 `projectPath` + `viewport`（单张）或 `shots`（批量）。
- **默认**：`layerPreset=Agent`、`autoFitViewport=true`、`scale=2`。
- **权限**：需在 `<BIMCANVAS_HOME>/agents/layout-agent.md` 的 `tools` 中显式加入该工具。

单张示例：
```json
{
  "projectPath": "C:\\Users\\...\\Projects\\demo_1",
  "viewport": { "mode": "full" }
}
```

批量示例：
```json
{
  "projectPath": "C:\\Users\\...\\Projects\\demo_1",
  "shots": [
    { "viewport": { "mode": "full" } },
    { "viewport": { "mode": "zone", "zoneId": "rz_1" } }
  ]
}
```

#### MCP 工具命名规则 ⭐

**格式**：`mcp__{server_key}__{tool_name}`

| 组成部分 | 来源 | 示例 |
|---------|------|------|
| `mcp__` | 固定前缀 | `mcp__` |
| `{server_key}` | `mcp_servers` 字典的 **key** | `canvas` |
| `{tool_name}` | `@tool()` 的第一个参数 | `create_job` |
| **完整名称** | - | `mcp__canvas__create_job` |

**⚠️ 重要**：`create_sdk_mcp_server(name="...")` 的 `name` 参数**不影响**工具调用名！

```python
# 示例对照
canvas_mcp = create_sdk_mcp_server(
    name="canvas",  # ← name="canvas" 不影响调用名！
    tools=[ai_job_create]
)

# 配置 Agent
mcp_servers={"canvas": canvas_mcp}  # ← "canvas" 影响调用名
@tool("create_job", ...)            # ← "create_job" 影响调用名

# 最终调用名
✅ "mcp__canvas__create_job"        # 正确（使用字典 key "canvas"）
❌ "mcp__different__create_job"     # 错误（不使用 Server name）
```

**常见错误**：

```python
# ❌ 错误：误用 Server name
canvas_mcp = create_sdk_mcp_server(name="canvas-mcp", ...)
mcp_servers={"canvas": canvas_mcp}
allowed_tools=["mcp__canvas-mcp__create_job"]  # 应该用 "canvas"

# ✅ 正确：使用字典 key
allowed_tools=["mcp__canvas__create_job"]
```

#### Schema 定义指南

MCP 工具的参数 Schema 决定了 AI 如何理解和调用工具，支持两种定义方式：

##### 简单字典格式（推荐简单工具）

适用于参数简单（1-3 个基本类型）的工具：

```python
@tool(
    "create_job",
    "批量创建隔离工作环境",
    {"count": int}  # ← 简单字典：仅指定参数类型
)
async def ai_job_create(args: dict[str, Any]) -> dict[str, Any]:
    count = args.get("count", 1)
    # ...
```

**支持的类型映射**：
- `str` → `{"type": "string"}`
- `int` → `{"type": "integer"}`
- `float` → `{"type": "number"}`
- `bool` → `{"type": "boolean"}`

**局限性**：
- ❌ 缺少参数级别的 `description`（AI 难以理解参数用途）
- ❌ 缺少 `minimum`、`maximum` 等高级约束
- ❌ 缺少 `$schema`、`additionalProperties` 等字段

##### 完整 JSON Schema（推荐复杂工具）

适用于复杂参数、需要详细文档的工具：

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
    count = args.get("count", 1)
    # ...
```

**优势**：
- ✅ 参数描述帮助 AI 理解用途
- ✅ 类型约束（minimum、maximum）提供自动验证
- ✅ 明确 Schema 规范（`$schema`）
- ✅ 禁止额外属性（`additionalProperties: False`）

##### Schema 选择策略

| 工具类型 | 推荐方案 | 理由 |
|----------|---------|------|
| **简单工具**<br>（1-2 个参数，无复杂约束） | 简单字典<br>`{"param": int}` | 代码简洁，快速开发 |
| **复杂工具**<br>（多参数、嵌套对象、需要验证） | 完整 JSON Schema | 类型安全，提供更好的 AI 提示 |
| **面向用户的工具**<br>（需要详细文档） | 完整 JSON Schema | 参数描述帮助 AI 理解用法 |

##### 完整 Schema 模板

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

### 添加新工具

1. 在 `src/mcp/` 下创建或修改工具模块（使用 `@tool` 装饰器）
2. 在 `create_sdk_mcp_server(...)` 中注册工具并更新 `CANVAS_ALLOWED_TOOLS`
3. 若仅限子代理使用，在 `<BIMCANVAS_HOME>/agents/*.md` 的 `tools` 中显式添加
4. 更新 README 文档

## 常见问题

| 问题 | 排查 |
|------|------|
| Agent 启动失败 | 检查 `AGENT_SDK_API_KEY` 环境变量是否设置 |
| MCP 工具调用失败 | 确认 .NET Server 已启动且端口可达 |
| 中文路径问题 | 在 git-bash 下路径需要转义或使用引号 |

## 相关文档

- [Agent 设计规范](../docs/Agent_Design_Spec.md)
- [Agent MVP 计划](../plans/Agent_MVP.md)
- [系统架构文档](../docs/Architecture.md)
