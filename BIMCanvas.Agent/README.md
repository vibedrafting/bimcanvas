# BIMCanvas Agent

基于 Anthropic Agent SDK 的 AI 室内布置助手。

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

# 编辑 .env 文件，填入 Anthropic API Key
# ANTHROPIC_API_KEY=your-api-key-here
```

### 4. 启动服务

**方式一：随 Server 自动启动（推荐）**

Agent 会在 BIMCanvas.Server 启动时自动启动，无需手动操作。

**方式二：HTTP 服务模式（独立运行）**

```bash
python -m src.main --serve
```

服务地址：`http://127.0.0.1:8765`

**方式三：交互式命令行模式（调试用）**

```bash
python -m src.main
```

## API 接口

| 端点 | 方法 | 说明 |
|------|------|------|
| `/health` | GET | 健康检查 |
| `/api/chat` | POST | 发送聊天消息（同步响应） |
| `/api/chat/stream` | POST | 发送聊天消息（SSE 流式响应） |
| `/api/clear-history` | POST | 清空对话历史 |
| `/api/history` | GET | 获取对话历史 |
| `/api/task/layout` | POST | 执行布置任务（P2 功能） |
| `/api/task/layout/stream` | POST | 执行布置任务（SSE 流式响应） |

### 请求示例

```http
POST /api/chat
Content-Type: application/json

{
  "projectPath": "C:/Users/.../Projects/demo_1",
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
  "message": "..."
}
```

响应格式（Server-Sent Events）：
```
data: {"type": "thinking", "content": "让我分析一下..."}
data: {"type": "text", "content": "好的"}
data: {"type": "text", "content": "，我来帮您"}
...
data: [DONE]
```

## SSE 事件协议

Agent 通过 SSE（Server-Sent Events）推送实时事件，支持以下事件类型：

### 事件类型一览

| 事件类型 | 说明 | 关键字段 |
|----------|------|----------|
| `thinking` | 思考内容（流式） | `content` |
| `thinking_complete` | 思考完成 | `content` |
| `text` | 文本响应（流式） | `content` |
| `text_complete` | 文本完成 | `content` |
| `subagent_start` | SubAgent 启动 | `subAgentId`, `subAgentName`, `subAgentType` |
| `subagent_complete` | SubAgent 完成 | `subAgentId`, `success`, `error` |
| `tool_call_start` | 工具调用开始 | `subAgentId`, `toolCallId`, `toolName`, `toolParams` |
| `tool_call_output` | 工具输出（流式） | `toolCallId`, `toolOutput` |
| `tool_call_complete` | 工具调用完成 | `toolCallId`, `success`, `error` |

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
  "type": "tool_call_output",
  "toolCallId": "tc-1",
  "toolOutput": "文件内容..."
}
```

**3. 工具调用完成**
```json
{
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

# 8. 流结束
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

### 布置任务 API（P2 功能）

```http
POST /api/task/layout
Content-Type: application/json

{
  "projectPath": "C:/Users/.../Projects/demo_1",
  "schemeId": "default",
  "prompt": "请为客厅布置现代简约风格的家具"
}
```

响应：
```json
{
  "success": true,
  "summary": "布置任务完成。已为客厅布置沙发、茶几、电视柜...",
  "schemeId": "default"
}
```

Agent 会自动：
1. 读取 `computed/room_zones.json` 获取房间数据
2. 读取 `baseline/openings.json` 获取门窗信息
3. 查看 `modules/` 目录获取可用家具
4. 将布置结果写入 `schemes/{schemeId}/modules.json`

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
│   │   └── agent_logger.py     # Agent 日志系统
│   │
│   ├── server/
│   │   ├── __init__.py
│   │   └── http_server.py      # HTTP 服务（aiohttp + CORS）
│   │
│   ├── tools/
│   │   ├── __init__.py
│   │   ├── file_tools.py       # JSON 文件读写工具
│   │   └── svg_parser.py       # SVG 解析工具
│   │
│   └── config/
│       ├── __init__.py
│       ├── settings.py         # 配置管理（从 loader 加载）
│       ├── loader.py           # 统一配置加载器
│       └── templates/          # 配置模板（首次运行自动复制）
│           ├── BIMCANVAS.md.template
│           ├── config.json.template
│           └── agents/
│               └── layout-agent.md.template
│
├── MOSS/                       # 历史代码（仅供参考）
└── AgentSDK-Quickstart.md      # Agent SDK 快速入门文档
```

## 核心模块说明

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

从 `~/.bimcanvas/agents/*.md` 配置文件加载 SubAgent 定义：

- **layout-agent** - 家具布置专家，负责空间规划和家具摆放

### HTTP Server (`server/http_server.py`)

基于 aiohttp 的 HTTP 服务：

- 支持 CORS 跨域（Web 前端调用）
- 按 projectPath 缓存 Agent 实例
- SSE 流式响应支持

### 配置系统 (`config/`)

**配置文件驱动架构**：首次运行时自动在 `~/.bimcanvas/` 创建配置文件。

```
~/.bimcanvas/
├── BIMCANVAS.md           # 主 Agent 系统提示词（可编辑）
├── config.json            # 应用配置（API、模型、工具）
└── agents/
    └── layout-agent.md    # SubAgent 配置（YAML frontmatter + 提示词）
```

**配置优先级**：环境变量 > config.json

#### config.json 格式

```json
{
  "apiKey": "$ANTHROPIC_API_KEY",
  "model": "claude-opus-4-5-20250514",
  "maxTokens": 4096,
  "tools": ["Read", "Glob", "Grep", "Task"],
  "server": { "host": "127.0.0.1", "port": 8765 }
}
```

#### tools 字段说明

`tools` 字段控制 Agent 可用工具的基础集合：

| 配置值 | 效果 |
|--------|------|
| `null` 或不设置 | **默认全开**（使用 Claude Code 全部内置工具） |
| `[]` 空数组 | **禁用所有工具**（Agent 只能对话） |
| `["Read", "Glob", ...]` | **只启用指定工具** |

**注意**：`tools` 参数与 `allowed_tools` 参数不同：
- `tools`：控制**可用工具集合**（真正的工具限制）
- `allowed_tools`：控制**权限规则**（哪些工具无需用户确认）

**常用工具列表**：
```
Task, Bash, Glob, Grep, LS, Read, Edit, MultiEdit, Write,
NotebookEdit, WebFetch, TodoWrite, WebSearch, ExitPlanMode
```

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
| `ANTHROPIC_API_KEY` | Anthropic API 密钥（必填） |
| `MODEL_NAME` | 覆盖模型名称 |
| `SERVER_HOST` | 覆盖服务地址 |
| `SERVER_PORT` | 覆盖服务端口 |

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
- [x] 添加布置任务 API（/api/task/layout）
- [x] 添加流式布置任务 API（/api/task/layout/stream）
- [ ] 端到端测试（完整布置流程）
- [ ] Web 端布置任务集成

### P3 阶段（完整功能）- 待开发

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

### 添加新工具

1. 在 `src/tools/` 下创建工具模块
2. 在 `PlacementAgent` 中注册工具定义
3. 更新 README 文档

## 相关文档

- [Agent 设计规范](../docs/Agent_Design_Spec.md)
- [Agent MVP 计划](../plans/Agent_MVP.md)
- [系统架构文档](../docs/Architecture.md)
