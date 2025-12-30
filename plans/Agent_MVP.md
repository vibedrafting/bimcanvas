# BIMCanvas Agent MVP 实施计划

> **版本**：v3.0 | **日期**：2025-12-30
> **目标**：验证 AI Agent 能理解房间功能，并完成符合设计常识的家具布置
> **技术栈**：Python 3.10+ / Anthropic Agent SDK / Claude Sonnet 4
> **核心原则**：使用 Agent SDK 为后期开发打下坚实基础

---

## 一、阶段概览

| 阶段 | 名称 | 核心产出 | 验收标准 |
|------|------|----------|----------|
| **P1** | Agent SDK 基础对话 | 对话功能 + Web 接入 | Web 端能与 Agent 正常对话 |
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

### 阶段 P1：Agent SDK 基础对话 + Web 接入

**目标**：实现基于 Agent SDK 的对话功能，并接入 Web 端 AI 对话面板

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

- [ ] Agent HTTP 服务可正常启动（`python -m src.main --serve`）
- [ ] Web 端 AI 对话面板可调用 Agent API
- [ ] 对话历史正确维护
- [ ] 多轮对话正常工作

---

### 阶段 P2：基础布置功能

**目标**：实现 AI 家具布置决策，输出 modules.json

**前提**：P1 阶段验收通过

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

#### P2.3 Agent 工具定义

**placement_agent.py 扩展**：
```python
import json
from anthropic import Anthropic
from src.tools.file_tools import read_json, write_json
from src.tools.svg_parser import list_modules

# Agent SDK 工具定义
TOOLS = [
    {
        "name": "read_room_zones",
        "description": "读取项目的房间分区数据（Room Zone）",
        "input_schema": {
            "type": "object",
            "properties": {
                "project_path": {"type": "string", "description": "项目路径"}
            },
            "required": ["project_path"]
        }
    },
    {
        "name": "read_openings",
        "description": "读取门窗数据",
        "input_schema": {
            "type": "object",
            "properties": {
                "project_path": {"type": "string", "description": "项目路径"}
            },
            "required": ["project_path"]
        }
    },
    {
        "name": "list_modules",
        "description": "列出可用的家具模块",
        "input_schema": {
            "type": "object",
            "properties": {
                "project_path": {"type": "string", "description": "项目路径"}
            },
            "required": ["project_path"]
        }
    },
    {
        "name": "write_modules",
        "description": "写入家具布置结果",
        "input_schema": {
            "type": "object",
            "properties": {
                "project_path": {"type": "string", "description": "项目路径"},
                "scheme_id": {"type": "string", "description": "方案ID"},
                "modules": {"type": "array", "description": "布置的模块列表"}
            },
            "required": ["project_path", "scheme_id", "modules"]
        }
    }
]

def execute_tool(tool_name: str, tool_input: dict) -> str:
    """执行工具调用"""
    if tool_name == "read_room_zones":
        data = read_json(tool_input["project_path"], "computed/room_zones.json")
        return json.dumps(data, ensure_ascii=False)

    elif tool_name == "read_openings":
        data = read_json(tool_input["project_path"], "baseline/openings.json")
        return json.dumps(data, ensure_ascii=False)

    elif tool_name == "list_modules":
        data = list_modules(tool_input["project_path"])
        return json.dumps(data, ensure_ascii=False)

    elif tool_name == "write_modules":
        write_json(
            tool_input["project_path"],
            f"schemes/{tool_input['scheme_id']}/modules.json",
            tool_input["modules"]
        )
        return "布置结果已保存"

    return f"未知工具: {tool_name}"
```

#### P2.4 布置任务接入 Web

**任务触发流程**：
```
Web 端点击"开始布置"
    ↓
调用 Agent API: POST /api/task/layout
    ↓
Agent 读取数据 → 执行布置决策 → 写入 modules.json
    ↓
返回布置结果摘要 → Web 端渲染
```

**新增 API 端点**：
```python
async def layout_task_handler(request: web.Request) -> web.Response:
    """执行布置任务"""
    data = await request.json()

    project_path = data.get("projectPath")
    scheme_id = data.get("schemeId", "default")
    user_prompt = data.get("prompt", "请为这个户型布置家具")

    agent = get_agent(project_path)

    # 构造布置任务指令
    task_prompt = f"""
    用户请求：{user_prompt}

    请执行以下步骤：
    1. 使用 read_room_zones 读取房间分区
    2. 使用 read_openings 读取门窗数据
    3. 使用 list_modules 获取可用家具
    4. 根据设计原则为每个房间布置家具
    5. 使用 write_modules 保存布置结果

    方案ID: {scheme_id}
    """

    # 执行 Agent 任务（带工具调用循环）
    result = await agent.run_task(task_prompt)

    return web.json_response({
        "success": True,
        "summary": result
    })
```

#### P2 验收标准

- [ ] Agent 可正确读取 room_zones.json
- [ ] Agent 可正确读取 openings.json
- [ ] Agent 可正确列出 modules/*.svg
- [ ] Agent 能为每个房间布置合理的家具
- [ ] 家具不阻挡门开启范围
- [ ] modules.json 格式符合规范
- [ ] Web 端能触发布置任务
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

### P2 阶段需要创建/修改的文件

| 文件 | 类型 | 说明 |
|------|------|------|
| `BIMCanvas.Agent/src/agent/placement_agent.py` | 修改 | 添加工具定义和布置逻辑 |
| `BIMCanvas.Agent/src/server/http_server.py` | 修改 | 添加布置任务 API |
| `BIMCanvas.Server/Services/ZoneTagService.cs` | 新建 | Server 端功能标签分配 |

### Agent 读取的文件

| 文件 | 生成者 | 用途 |
|------|--------|------|
| `computed/room_zones.json` | Server | 房间分区数据 |
| `baseline/openings.json` | Revit | 门窗数据 |
| `modules/*.svg` | 手动准备 | 家具素材库 |

### Agent 写入的文件

| 文件 | 内容 |
|------|------|
| `schemes/{s}/modules.json` | 家具布置结果 |

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

- [ ] Agent HTTP 服务可正常启动
- [ ] Web 端 AI 对话面板可调用 Agent API
- [ ] 对话历史正确维护
- [ ] 多轮对话正常工作

### P2 阶段验收

- [ ] Agent 可正确读取项目数据
- [ ] Agent 能为每个房间布置合理的家具
- [ ] 家具不阻挡门开启范围
- [ ] modules.json 格式符合规范
- [ ] Web 端能触发布置任务并渲染结果

---

## 附录：相关文档

- `docs/Agent_Design_Spec.md` - PlacementAgent 完整理论文档
- `docs/AI_Parallel_Design_Patterns.md` - 并行设计模式详细说明
- `docs/Schema-JSON-v3.md` - v3.0 数据模型定义
- `BIMCanvas.Agent/AgentSDK-Quickstart.md` - Agent SDK 快速入门指南
