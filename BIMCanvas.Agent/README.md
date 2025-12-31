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

### 1. 安装依赖

```bash
cd BIMCanvas.Agent
pip install -e .
```

### 2. 配置环境变量

```bash
# 复制配置模板
cp .env.example .env

# 编辑 .env 文件，填入 Anthropic API Key
# ANTHROPIC_API_KEY=your-api-key-here
```

### 3. 启动服务

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
data: {"chunk": "好的"}
data: {"chunk": "，我来"}
data: {"chunk": "帮您"}
...
data: [DONE]
```

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
│   │   └── placement_agent.py  # PlacementAgent（Agent SDK 封装）
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
│       └── settings.py         # 配置管理（环境变量）
│
├── MOSS/                       # 历史代码（仅供参考）
└── AgentSDK-Quickstart.md      # Agent SDK 快速入门文档
```

## 核心模块说明

### PlacementAgent (`agent/placement_agent.py`)

基于 Anthropic Agent SDK 的智能助手：

- **chat(message)** - 同步对话，返回完整响应
- **chat_stream(message)** - 流式对话，逐 token 返回
- **clear_history()** - 清空对话历史
- **get_history()** - 获取对话历史

### HTTP Server (`server/http_server.py`)

基于 aiohttp 的 HTTP 服务：

- 支持 CORS 跨域（Web 前端调用）
- 按 projectPath 缓存 Agent 实例
- SSE 流式响应支持

### Settings (`config/settings.py`)

配置项（优先级：环境变量 > 默认值）：

| 配置项 | 环境变量 | 默认值 | 说明 |
|--------|----------|--------|------|
| API Key | `ANTHROPIC_API_KEY` | - | Anthropic API 密钥（必填） |
| 模型 | `AGENT_MODEL` | `claude-sonnet-4-20250514` | 使用的模型 |
| 服务地址 | `AGENT_HOST` | `127.0.0.1` | HTTP 服务监听地址 |
| 服务端口 | `AGENT_PORT` | `8765` | HTTP 服务监听端口 |

## 开发状态

### P1 阶段（MVP）- 已完成

- [x] Agent SDK 集成
- [x] HTTP 服务 + CORS
- [x] 基础对话功能
- [x] Web 前端集成
- [x] Server 自动启动 Agent

### P2 阶段（工具调用）- 待开发

- [ ] 定义 MCP 工具（读取项目数据）
- [ ] 实现布置决策逻辑
- [ ] 添加布置任务 API

### P3 阶段（完整功能）- 待开发

- [ ] 多轮对话上下文优化
- [ ] 布置方案评估与修正
- [ ] 与 Revit 回写集成

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
