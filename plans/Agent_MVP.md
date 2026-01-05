# BIMCanvas Agent MVP 实施计划

> **版本**：v3.0 | **日期**：2025-12-30
> **目标**：验证 AI Agent 能理解房间功能，并完成符合设计常识的家具布置
> **技术栈**：Python 3.10+ / Anthropic Agent SDK / Claude Sonnet 4
> **核心原则**：使用 Agent SDK 为后期开发打下坚实基础

---

## 一、阶段概览

| 阶段 | 名称 | 核心产出 | 验收标准 |
|------|------|----------|----------|
| **P1** | 基础对话（Client SDK） | 对话功能 + Web 接入 | Web 端能与 Agent 正常对话 |
| **P1.5** | 迁移到 Agent SDK | Agent SDK 对话 | 使用 Agent SDK 实现对话，为 P2 打基础 |
| **P2** | 基础布置功能 | 布置决策 + modules.json | AI 能在房间中布置家具 |

### MVP 简化策略

| 完整版功能 | MVP 处理方式 | 执行者 |
|------------|--------------|--------|
| 创建任务 | 只创建默认策略，接入 Web 任务面板 | Server |
| 功能标签分配 | 固定预设（房间类型→标签组） | Server |
| 分区设计 | 不考虑，直接用 room_zones.json | - |
| 素材过滤 | 不考虑，使用 modules/ 全量素材 | - |
| 模块布置 | AI 按固定规则布置 | Agent |
| Git Worktree 并行 | 不考虑，单任务串行 | - |
| 策略参数化 | 不考虑，使用默认策略 | - |

### 关于分区设计的说明

> **分区设计是 AI 的一个能力**，即在面对大空间设计时（如客餐厅、主卧），为了更好应用设计策略（如动线优先），AI 需要先考虑合理的将房间分区（Room 类型的 Zone）划分为更加细致的设计区。
>
> 而面对小空间（如卫生间）通常不需要考虑分区设计。
>
> **MVP 阶段**：不考虑分区设计，直接把 `computed/room_zones.json` 中的房间分区作为最终设计区使用。

---

## 二、阶段详细计划

### 阶段 P1：基础对话（Client SDK）+ Web 接入

**目标**：实现基于 Anthropic Client SDK 的对话功能，并接入 Web 端 AI 对话面板

> **说明**：P1 阶段使用 Anthropic Client SDK（`anthropic` 包）快速实现对话功能。P1.5 阶段将迁移到 Agent SDK。

#### P1.1 基础设施搭建

**任务清单**：
- [ ] 创建 Agent 项目结构
- [ ] 配置 pyproject.toml（Agent SDK 依赖）
- [ ] 实现基础工具类（file_tools.py、svg_parser.py）
- [ ] 创建配置管理（settings.py）

**目标结构**：
```
BIMCanvas.Agent/
├── pyproject.toml
├── README.md
├── src/
│   ├── __init__.py
│   ├── main.py                 # Agent 入口（支持 HTTP 服务）
│   ├── agent/
│   │   ├── __init__.py
│   │   └── placement_agent.py  # 主 Agent（Agent SDK）
│   ├── tools/
│   │   ├── __init__.py
│   │   ├── file_tools.py       # JSON 读写
│   │   └── svg_parser.py       # SVG 解析
│   ├── server/
│   │   ├── __init__.py
│   │   └── http_server.py      # HTTP 服务（供 Web 调用）
│   └── config/
│       ├── __init__.py
│       └── settings.py         # 配置项
├── MOSS/                       # 保留现有代码（参考）
└── AgentSDK-Quickstart.md      # Agent SDK 参考文档
```

**pyproject.toml**：
```toml
[project]
name = "bimcanvas-agent"
version = "0.1.0"
requires-python = ">=3.10"
dependencies = [
    "anthropic>=0.40.0",
    "aiohttp>=3.9.0",           # HTTP 服务
    "python-dotenv>=1.0.0",     # 环境变量
]

[project.scripts]
bimcanvas-agent = "src.main:main"
```

#### P1.2 Agent SDK 对话实现

**核心代码 - placement_agent.py**：
```python
import asyncio
from anthropic import Anthropic

client = Anthropic()

# Agent 系统提示词
SYSTEM_PROMPT = """
你是 BIMCanvas 的 PlacementAgent，一个专业的室内布置助手。

你的职责：
1. 理解用户的布置需求
2. 分析房间功能和空间特点
3. 为用户提供专业的布置建议
4. 执行家具布置任务

当前阶段（MVP）你可以：
- 与用户对话，理解需求
- 解答室内设计相关问题
- 执行基础的家具布置任务

请用简洁专业的中文回答。
"""

class PlacementAgent:
    """基于 Agent SDK 的布置助手"""

    def __init__(self, project_path: str = None):
        self.project_path = project_path
        self.conversation_history = []

    async def chat(self, user_message: str) -> str:
        """处理用户消息并返回回复"""

        # 添加用户消息到历史
        self.conversation_history.append({
            "role": "user",
            "content": user_message
        })

        # 调用 Claude API
        response = client.messages.create(
            model="claude-sonnet-4-20250514",
            max_tokens=4096,
            system=SYSTEM_PROMPT,
            messages=self.conversation_history
        )

        # 提取回复内容
        assistant_message = response.content[0].text

        # 添加到历史
        self.conversation_history.append({
            "role": "assistant",
            "content": assistant_message
        })

        return assistant_message

    def clear_history(self):
        """清空对话历史"""
        self.conversation_history = []
```

#### P1.3 HTTP 服务（供 Web 调用）

**http_server.py**：
```python
from aiohttp import web
from src.agent.placement_agent import PlacementAgent

# 全局 Agent 实例（按项目路径缓存）
agents: dict[str, PlacementAgent] = {}

def get_agent(project_path: str) -> PlacementAgent:
    """获取或创建 Agent 实例"""
    if project_path not in agents:
        agents[project_path] = PlacementAgent(project_path)
    return agents[project_path]

async def chat_handler(request: web.Request) -> web.Response:
    """处理对话请求"""
    data = await request.json()

    project_path = data.get("projectPath", "")
    message = data.get("message", "")

    if not message:
        return web.json_response({"error": "消息不能为空"}, status=400)

    agent = get_agent(project_path)
    reply = await agent.chat(message)

    return web.json_response({
        "reply": reply,
        "projectPath": project_path
    })

async def clear_history_handler(request: web.Request) -> web.Response:
    """清空对话历史"""
    data = await request.json()
    project_path = data.get("projectPath", "")

    if project_path in agents:
        agents[project_path].clear_history()

    return web.json_response({"success": True})

def create_app() -> web.Application:
    """创建 HTTP 应用"""
    app = web.Application()
    app.router.add_post("/api/chat", chat_handler)
    app.router.add_post("/api/clear-history", clear_history_handler)
    return app

def run_server(host: str = "127.0.0.1", port: int = 8765):
    """启动 HTTP 服务"""
    app = create_app()
    web.run_app(app, host=host, port=port)
```

#### P1.4 Web 端接入

**Web 端需要修改的文件**：`BIMCanvas.Web/src/components/UI/AICommandCenter.vue`

**接口调用示例**：
```typescript
// 发送消息
const sendMessage = async (message: string) => {
  const response = await fetch('http://127.0.0.1:8765/api/chat', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      projectPath: currentProjectPath,
      message: message
    })
  });
  const data = await response.json();
  return data.reply;
};

// 清空历史
const clearHistory = async () => {
  await fetch('http://127.0.0.1:8765/api/clear-history', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ projectPath: currentProjectPath })
  });
};
```

#### P1 验收标准

- [x] Agent HTTP 服务可正常启动（`python -m src.main --serve`）
- [x] Web 端 AI 对话面板可调用 Agent API
- [x] 对话历史正确维护
- [x] 多轮对话正常工作

> ✅ **P1 阶段已完成**（2025-01-05）

---

### 阶段 P1.5：迁移到 Agent SDK

**目标**：将对话功能从 Anthropic Client SDK 迁移到 Agent SDK，保持现有 API 不变，为 P2 工具调用打下基础。

**前提**：P1 阶段验收通过

#### P1.5.1 为什么要迁移？

| 方面 | Client SDK (当前) | Agent SDK (目标) |
|------|------------------|------------------|
| 包名 | `anthropic` | `claude-agent-sdk` |
| 工具执行 | 手动实现工具循环 | Claude 自主执行 |
| 内置工具 | 无 | Read, Write, Edit, Bash, Glob, Grep 等 |
| 对话管理 | 手动维护 history | 框架自动管理（会话恢复） |

**迁移价值**：
1. **文件驱动架构**：BIMCanvas 是文件驱动架构，Agent SDK 内置 Read/Write/Edit 工具正好用于操作项目文件
2. **为 P2 打基础**：P2 阶段需要工具调用，Agent SDK 原生支持
3. **简化代码**：Agent SDK 自动管理工具循环，无需手动实现

#### P1.5.2 依赖变更

**pyproject.toml 修改**：
```toml
# 移除
dependencies = [
    "anthropic>=0.40.0",
    ...
]

# 新增
dependencies = [
    "claude-agent-sdk>=0.1.0",  # Agent SDK
    "aiohttp>=3.9.0",
    "aiohttp-cors>=0.7.0",
    "python-dotenv>=1.0.0",
]
```

**前置条件**：
- 安装 Claude Code CLI：`npm install -g @anthropic-ai/claude-code`
- 设置 API Key：`ANTHROPIC_API_KEY`

#### P1.5.3 PlacementAgent 重写

**目标实现**（Agent SDK）：
```python
from claude_agent_sdk import query, ClaudeAgentOptions, AssistantMessage, TextBlock

# Agent 系统提示词
SYSTEM_PROMPT = """你是 BIMCanvas 的 PlacementAgent，一个专业的室内布置助手。
...（保持不变）
"""

class PlacementAgent:
    """基于 Agent SDK 的布置助手"""

    def __init__(self, project_path: str = None):
        self.project_path = project_path
        self.session_id = None  # Agent SDK 会话管理

    async def chat(self, user_message: str) -> str:
        """处理用户消息并返回回复"""
        options = ClaudeAgentOptions(
            system_prompt=SYSTEM_PROMPT,
            cwd=self.project_path,  # 设置工作目录
            max_turns=1,  # P1.5: 单轮对话
            # P2 阶段将启用工具：
            # allowed_tools=["Read", "Write", "Glob"],
            # permission_mode="acceptEdits"
        )

        # 如果有会话，恢复上下文
        if self.session_id:
            options.resume = self.session_id

        full_response = ""
        async for message in query(prompt=user_message, options=options):
            # 捕获会话 ID
            if hasattr(message, 'subtype') and message.subtype == 'init':
                self.session_id = message.data.get('session_id')

            # 提取文本响应
            if isinstance(message, AssistantMessage):
                for block in message.content:
                    if isinstance(block, TextBlock):
                        full_response += block.text

        return full_response

    async def chat_stream(self, user_message: str) -> AsyncIterator[str]:
        """流式处理用户消息"""
        options = ClaudeAgentOptions(
            system_prompt=SYSTEM_PROMPT,
            cwd=self.project_path,
            max_turns=1
        )

        if self.session_id:
            options.resume = self.session_id

        async for message in query(prompt=user_message, options=options):
            if hasattr(message, 'subtype') and message.subtype == 'init':
                self.session_id = message.data.get('session_id')

            if isinstance(message, AssistantMessage):
                for block in message.content:
                    if isinstance(block, TextBlock):
                        yield block.text

    def clear_history(self) -> None:
        """清空对话历史（重置会话）"""
        self.session_id = None

    def get_history(self) -> list[dict]:
        """获取对话历史（Agent SDK 通过 session 管理，返回空列表）"""
        return []  # Agent SDK 内部管理，外部无法直接获取
```

#### P1.5.4 需要修改的文件

| 文件 | 操作 | 说明 |
|------|------|------|
| `pyproject.toml` | 修改 | 依赖从 `anthropic` 改为 `claude-agent-sdk` |
| `src/agent/placement_agent.py` | 重写 | 使用 Agent SDK 的 `query()` 函数 |
| `src/server/http_server.py` | 微调 | 适配新的 Agent 接口（如有需要） |
| `README.md` | 更新 | 更新依赖说明和架构描述 |

#### P1.5 验收标准

- [ ] `pip install -e .` 成功安装 `claude-agent-sdk`
- [ ] Agent HTTP 服务可正常启动
- [ ] Web 端 AI 对话面板可正常对话
- [ ] 流式响应正常工作
- [ ] 清空历史功能正常
- [ ] 多轮对话上下文保持（通过 session_id）

---

### 阶段 P2：基础布置功能

**目标**：实现 AI 家具布置决策，输出 modules.json

**前提**：P1.5 阶段验收通过（已迁移到 Agent SDK）

#### P2.1 Server 端预处理（非 Agent 职责）

> 以下功能由 Server 端实现，Agent 直接使用结果

**功能标签分配**（Server 端固定预设）：

| 房间类型 (reason) | 功能标签 (tags) |
|-------------------|-----------------|
| room:LivingRoom | sitting, entertainment, tv_media |
| room:MasterBedroom | sleeping, rest, storage, dressing |
| room:Bedroom | sleeping, rest |
| room:Bathroom | bathing, toilet |
| room:Kitchen | cooking, storage |
| room:DiningRoom | dining |

**Server 端实现要点**：
- 读取 `computed/room_zones.json`
- 根据 `reason` 字段匹配功能标签
- 直接输出 `schemes/{s}/zones.json`（MVP 不做分区设计）

#### P2.2 Agent 布置决策

**输入数据**：
- `computed/room_zones.json` - 房间分区（直接作为设计区）
- `baseline/openings.json` - 门窗数据
- `modules/*.svg` - 全量素材库（MVP 不过滤）

**输出数据**：
- `schemes/{s}/modules.json` - 布置结果

**布置决策规则**（来自 Agent_Design_Spec.md §4.3）：

| 规则 | 说明 | 适用家具 |
|------|------|----------|
| 靠墙规则 | 大型家具尽量靠墙 | 床、衣柜、沙发 |
| 居中规则 | 某些家具居中于墙面 | 电视柜 |
| 顶角规则 | 某些家具顶墙角 | 衣柜、书柜 |
| 朝向规则 | 模块背对墙 | 沙发背墙，面向中心 |
| 对位规则 | 家具对位关系 | 沙发正对电视 |
| 避窗规则 | 除淋浴外避免靠窗 | 床头不靠窗 |
| 避门规则 | 不阻挡门开启范围 | 利用 openings 数据 |

**布置优先级**：
```
1. 【锚点家具】确定设计区的"锚点"
   • 客厅: 电视墙位置 → 电视柜
   • 卧室: 床头墙位置 → 床
   • 餐厅: 主位置 → 餐桌

2. 【主要家具】围绕锚点布置
   • 客厅: 沙发（正对电视柜）
   • 卧室: 衣柜、床头柜

3. 【辅助家具】填充剩余空间
   • 茶几、边几、装饰柜等
```

#### P2.3 启用 Agent SDK 内置工具

> **关键变更**：Agent SDK 内置 Read/Write/Glob/Edit 等工具，无需手动定义。Claude 会自动执行文件操作。

**placement_agent.py 配置更新**：
```python
from claude_agent_sdk import query, ClaudeAgentOptions, AssistantMessage, TextBlock

# P2 阶段 System Prompt（增加文件操作指导）
SYSTEM_PROMPT = """你是 BIMCanvas 的 PlacementAgent，一个专业的室内布置助手。

## 职责
1. 理解用户的布置需求
2. 分析房间功能和空间特点
3. 执行家具布置任务

## 当前项目文件结构
工作目录已设置为项目根目录，你可以直接访问以下文件：

**输入数据**（只读）：
- computed/room_zones.json - 房间分区数据
- baseline/openings.json - 门窗数据
- modules/*.svg - 家具素材库

**输出数据**（可写）：
- schemes/{schemeId}/modules.json - 布置结果

## 布置规则
- 大型家具尽量靠墙放置（床、衣柜、沙发）
- 电视柜居中于电视墙
- 沙发正对电视，保持合理观看距离
- 床头不靠窗，避免对流
- 家具不阻挡门的开启范围
- 保持主要动线畅通（至少800mm通道宽度）

请用简洁专业的中文回答，不要使用Emoji。
"""

class PlacementAgent:
    """基于 Agent SDK 的布置助手"""

    def __init__(self, project_path: str = None):
        self.project_path = project_path
        self.session_id = None

    async def chat(self, user_message: str) -> str:
        """处理用户消息（支持文件操作）"""
        options = ClaudeAgentOptions(
            system_prompt=SYSTEM_PROMPT,
            cwd=self.project_path,  # 设置工作目录
            max_turns=10,  # 允许多轮工具调用
            max_thinking_tokens=8000,
            # P2 阶段启用内置工具：
            allowed_tools=["Read", "Write", "Glob"],
            permission_mode="acceptEdits",  # 自动接受文件编辑
        )

        if self.session_id:
            options.resume = self.session_id

        full_response = ""
        async for message in query(prompt=user_message, options=options):
            if hasattr(message, 'subtype') and message.subtype == 'init':
                self.session_id = message.data.get('session_id')

            if isinstance(message, AssistantMessage):
                for block in message.content:
                    if isinstance(block, TextBlock):
                        full_response += block.text

        return full_response
```

**关键配置说明**：

| 参数 | 值 | 说明 |
|------|-----|------|
| `cwd` | project_path | 工作目录，Agent 读写文件的根路径 |
| `max_turns` | 10 | 允许多轮工具调用（读取→分析→写入） |
| `allowed_tools` | ["Read", "Write", "Glob"] | 启用文件读写和搜索 |
| `permission_mode` | "acceptEdits" | 自动接受文件编辑权限 |

**Agent SDK 内置工具**：

| 工具 | 用途 | 对应 MVP 操作 |
|------|------|---------------|
| Read | 读取文件内容 | 读取 room_zones.json, openings.json |
| Write | 写入文件 | 写入 modules.json |
| Glob | 搜索文件 | 列出 modules/*.svg |
| Edit | 编辑文件 | 修改现有配置 |

#### P2.4 布置任务接入 Web

**任务触发流程**：
```
Web 端点击"开始布置"
    ↓
调用 Agent API: POST /api/task/layout
    ↓
Agent 自动读取数据 → 执行布置决策 → 写入 modules.json
    ↓
返回布置结果摘要 → Web 端渲染
```

**新增 API 端点**（http_server.py）：
```python
async def layout_task_handler(request: web.Request) -> web.Response:
    """执行布置任务"""
    data = await request.json()

    project_path = data.get("projectPath")
    scheme_id = data.get("schemeId", "default")
    user_prompt = data.get("prompt", "请为这个户型布置家具")

    agent = get_agent(project_path)

    # 构造布置任务指令（Agent SDK 会自动调用内置工具）
    task_prompt = f"""
用户请求：{user_prompt}

请执行家具布置任务：
1. 读取 computed/room_zones.json 获取房间分区
2. 读取 baseline/openings.json 获取门窗数据
3. 使用 Glob 查找 modules/*.svg 获取可用家具
4. 根据布置规则为每个房间布置家具
5. 将布置结果写入 schemes/{scheme_id}/modules.json

注意：输出的 modules.json 必须符合 v3.0 数据模型规范。
"""

    # 使用 chat() 方法执行任务（Agent SDK 自动处理工具调用）
    result = await agent.chat(task_prompt)

    return web.json_response({
        "success": True,
        "summary": result,
        "schemeId": scheme_id
    })
```

**说明**：
- 使用 `agent.chat()` 而非 `agent.run_task()`（后者不存在）
- Agent SDK 自动执行工具调用循环，无需手动管理
- Claude 会根据 System Prompt 中的文件路径指导自动读写文件

#### P2 验收标准

- [ ] Agent SDK 内置工具正常启用（allowed_tools 配置）
- [ ] Agent 可使用 Read 工具读取 room_zones.json
- [ ] Agent 可使用 Read 工具读取 openings.json
- [ ] Agent 可使用 Glob 工具列出 modules/*.svg
- [ ] Agent 可使用 Write 工具输出 modules.json
- [ ] Agent 能为每个房间布置合理的家具
- [ ] 家具不阻挡门开启范围
- [ ] modules.json 格式符合 v3.0 规范
- [ ] Web 端能触发布置任务（POST /api/task/layout）
- [ ] Web 端能正确渲染布置结果

---

## 三、关键文件清单

### P1 阶段需要创建/修改的文件

| 文件 | 类型 | 说明 |
|------|------|------|
| `BIMCanvas.Agent/pyproject.toml` | 新建 | 项目配置 |
| `BIMCanvas.Agent/src/__init__.py` | 新建 | 包初始化 |
| `BIMCanvas.Agent/src/main.py` | 新建 | 入口（支持 HTTP 服务） |
| `BIMCanvas.Agent/src/agent/placement_agent.py` | 新建 | 主 Agent |
| `BIMCanvas.Agent/src/server/http_server.py` | 新建 | HTTP 服务 |
| `BIMCanvas.Agent/src/tools/file_tools.py` | 新建 | JSON 读写 |
| `BIMCanvas.Agent/src/tools/svg_parser.py` | 新建 | SVG 解析 |
| `BIMCanvas.Agent/src/config/settings.py` | 新建 | 配置管理 |
| `BIMCanvas.Web/src/components/UI/AICommandCenter.vue` | 修改 | 接入 Agent API |

### P1.5 阶段需要修改的文件

| 文件 | 类型 | 说明 |
|------|------|------|
| `BIMCanvas.Agent/pyproject.toml` | 修改 | 依赖从 `anthropic` 改为 `claude-agent-sdk` |
| `BIMCanvas.Agent/src/agent/placement_agent.py` | 重写 | 使用 Agent SDK 的 `query()` 函数 |
| `BIMCanvas.Agent/src/server/http_server.py` | 微调 | 适配新的 Agent 接口（如有需要） |
| `BIMCanvas.Agent/README.md` | 更新 | 更新依赖说明和架构描述 |

### P2 阶段需要创建/修改的文件

| 文件 | 类型 | 说明 |
|------|------|------|
| `BIMCanvas.Agent/src/agent/placement_agent.py` | 修改 | 启用 Agent SDK 内置工具（allowed_tools, permission_mode） |
| `BIMCanvas.Agent/src/agent/placement_agent.py` | 修改 | 更新 System Prompt（增加文件路径指导） |
| `BIMCanvas.Agent/src/server/http_server.py` | 修改 | 添加布置任务 API（/api/task/layout） |
| `BIMCanvas.Server/Services/ZoneTagService.cs` | 新建 | Server 端功能标签分配 |

### Agent 读取的文件（通过 Agent SDK 内置 Read/Glob 工具）

| 文件 | 生成者 | 用途 | 读取方式 |
|------|--------|------|----------|
| `computed/room_zones.json` | Server | 房间分区数据 | Read |
| `baseline/openings.json` | Revit | 门窗数据 | Read |
| `modules/*.svg` | 手动准备 | 家具素材库 | Glob |

### Agent 写入的文件（通过 Agent SDK 内置 Write 工具）

| 文件 | 内容 | 写入方式 |
|------|------|----------|
| `schemes/{s}/modules.json` | 家具布置结果 | Write |

---

## 四、测试数据

### demo_1 项目路径

```
C:\Users\huhaonan\Documents\BIMCanvas\Projects\demo_1
```

### demo_1 房间数据

| Zone ID | 房间名 | 类型 | 功能标签（Server 预设） |
|---------|--------|------|-------------------------|
| rz_1 | 次卧一 | Bedroom | sleeping, rest |
| rz_2 | 次卧二 | Bedroom | sleeping, rest |
| rz_3 | 主卧 | MasterBedroom | sleeping, rest, storage, dressing |
| rz_4 | 主卫 | Bathroom | bathing, toilet |
| rz_5 | 公卫 | Bathroom | bathing, toilet |
| rz_6 | 公共空间 | LivingRoom | sitting, entertainment, dining |

### 素材库（modules/）

已准备就绪。

---

## 五、后续扩展（MVP 后）

| 优先级 | 功能 | 参考文档 |
|--------|------|----------|
| P1 | 分区设计（大空间细分） | Agent_Design_Spec.md §4.2 |
| P1 | 素材过滤（按功能标签） | Agent_Design_Spec.md §4.2 |
| P2 | 策略参数化（storage_weight 等） | Agent_Design_Spec.md §5 |
| P2 | Git Worktree 并行架构 | AI_Parallel_Design_Patterns.md |
| P3 | SSE 事件触发 | Architecture.md §6.4 |
| P3 | 自动 Commit + 设计说明 | Agent_Design_Spec.md §6.3 |

---

## 六、总体验收标准

### P1 阶段验收

- [x] Agent HTTP 服务可正常启动
- [x] Web 端 AI 对话面板可调用 Agent API
- [x] 对话历史正确维护
- [x] 多轮对话正常工作

> ✅ **P1 阶段已完成**

### P1.5 阶段验收

- [ ] `pip install -e .` 成功安装 `claude-agent-sdk`
- [ ] Agent HTTP 服务可正常启动
- [ ] Web 端 AI 对话面板可正常对话
- [ ] 流式响应正常工作
- [ ] 清空历史功能正常
- [ ] 多轮对话上下文保持

### P2 阶段验收

- [ ] Agent SDK 内置工具正常启用（Read, Write, Glob）
- [ ] Agent 可自动读取项目数据（room_zones.json, openings.json）
- [ ] Agent 能为每个房间布置合理的家具
- [ ] 家具不阻挡门开启范围
- [ ] modules.json 格式符合 v3.0 规范
- [ ] Web 端能触发布置任务并渲染结果

---

## 附录：相关文档

- `docs/Agent_Design_Spec.md` - PlacementAgent 完整理论文档
- `docs/AI_Parallel_Design_Patterns.md` - 并行设计模式详细说明
- `docs/Schema-JSON-v3.md` - v3.0 数据模型定义
- `BIMCanvas.Agent/AgentSDK-Quickstart.md` - Agent SDK 快速入门指南
