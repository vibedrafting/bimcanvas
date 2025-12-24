# PlacementAgent 架构决策 专家评审记录

本文档用于讨论 BIMCanvas 项目中 **AI 布置方案的触发机制** 和 **PlacementAgent 的架构设计** 问题。

> [!IMPORTANT]
> **协作规则**：
>
> 1. 所有新意见请以 `### [时间戳] [专家名]: [观点]` 的格式追加在文档末尾的 **讨论 (Discussion)** 章节。
> 2. **严禁修改**文档中已有的内容，尤其是其他专家的评审意见。
> 3. **区分优先级**：明确指出问题是"阻碍性 (Blocker)"还是"建议性 (Suggestion)"。

> [!TIP]
> **讨论原则**：
>
> - **建设性批评**：提出反对意见时，请尽量提供替代方案或改进建议。
> - **关注核心**：优先讨论架构风险、数据一致性、安全隐患等核心问题。
> - **积极回应**：当其他专家在讨论中 @你 或针对你的观点提出疑问时，请务必做出回应，保持对话的连贯性。
> - **拥抱共识**：当出现分歧时，寻找双方都能接受的折中方案或最优解。

---

## 背景 (Context)

### 讨论范围

在 BIMCanvas 项目架构讨论过程中，确定了以下职责划分：
- **BIMCanvas.Revit**：原始数据提取（输出精简版 CanvasDocument）
- **BIMCanvas.Server**：数据处理 + 状态管理（计算 Zone、处理 Module 放置）
- **BIMCanvas.Core**：算法库（被各项目引用）
- **BIMCanvas.Web**：前端渲染

但遗留了一个核心问题：**哪个项目负责触发 AI 生成模块布置方案？如何触发？**

### 相关文档

| 文档 | 路径 | 说明 |
|------|------|------|
| 系统架构 | `docs/Architecture.md` | 整体架构设计 |
| Revit 实现计划 | `plans/Revit_Implementation_Plan.md` | Revit 层实现计划 |
| 几何类型评审 | `reviews/Geometry_Type_Architecture_Review.md` | AI = OBB 规划师 共识 |

### 架构层级

```
┌─────────────────────────────────────────────────────────────────────┐
│  Claude Code (AI CLI)                                               │
│  • 用户与 AI 的对话交互入口                                           │
│  • 通过 MCP Protocol 调用工具                                        │
└─────────────────────────────────────────────────────────────────────┘
                                  ↕ MCP Protocol
┌─────────────────────────────────────────────────────────────────────┐
│  BIMCanvas.Server (.NET 6+)                                         │
│  • MCP 工具实现                                                      │
│  • 画布状态管理                                                      │
│  • Zone 计算 / Module 放置验证                                       │
│  • REST API / SignalR Hub                                           │
└─────────────────────────────────────────────────────────────────────┘
                                  ↕ HTTP / WebSocket
┌─────────────────────────────────────────────────────────────────────┐
│  BIMCanvas.Web (Vue 3 + TypeScript)                                 │
│  • JSON → 画布渲染                                                   │
│  • 用户交互（拖拽、按钮点击）                                         │
└─────────────────────────────────────────────────────────────────────┘
```

### 核心问题

| 问题 | 说明 |
|------|------|
| **触发源** | AI 布置方案由谁触发？Claude Code 对话？Web 按钮？自动检测？ |
| **执行主体** | 布置逻辑在哪里执行？MCP 工具？Server 服务？独立 Agent？ |
| **决策分层** | 简单场景 vs 复杂场景的处理策略是否需要区分？ |

---

## 专家：Claude (AI 架构视角)

### 核心观点

**建议采用"Server 内部服务 + MCP 工具封装"的混合架构**：PlacementAgent 作为 Server 内部的核心服务，同时通过 MCP 工具暴露给 Claude Code 调用。

### 1. PlacementAgent 的架构位置

- **优先级**：Blocker

- **候选方案对比**：

| 方案 | 位置 | 优点 | 缺点 |
|------|------|------|------|
| **A** | MCP 工具 | Claude Code 和 Server 都能调用 | MCP 协议限制，状态管理复杂 |
| **B** | 独立 AI 服务 | 灵活、可独立部署、可扩展 | 架构复杂、通信成本高 |
| **C** | Server 内部服务 | 简单、状态共享、无 IPC | 耦合度较高 |

- **推荐**：**方案 C（Server 内部服务）+ MCP 工具封装**

- **理由**：
  1. **状态共享**：PlacementService 可直接访问 CanvasStateManager，无需跨进程通信
  2. **多入口支持**：MCP 工具、REST API、SignalR 都可以调用同一个服务
  3. **架构简洁**：避免引入新的进程或服务

### 2. 触发方式设计

- **优先级**：Blocker

- **三种触发方式**：

| 触发方式 | 触发源 | 调用路径 | 场景 |
|----------|--------|----------|------|
| **AI 对话触发** | Claude Code | MCP: `generate_layout` → PlacementService | 用户通过对话指示 AI 布置 |
| **Web 按钮触发** | Web 前端 | REST: `POST /api/canvas/{id}/generate` → PlacementService | 用户点击"一键布置"按钮 |
| **自动修正触发** | Server 内部 | CanvasHub.OnLayoutError() → PlacementService.AutoFix() | 检测到布置错误时自动修正 |

- **数据流图**：

```
【触发方式 1：AI 对话触发】
用户: "帮我布置客厅"
    ↓
Claude Code (主 Agent)
    ↓ [MCP: generate_layout]
BIMCanvas.Server
    ↓ [PlacementService.GenerateLayout()]
生成布置方案
    ↓ [WebSocket 推送]
Web 显示结果

【触发方式 2：Web 按钮触发】
用户点击"一键布置"
    ↓
Web 前端
    ↓ [POST /api/canvas/{id}/generate]
BIMCanvas.Server
    ↓ [PlacementService.GenerateLayout()]
生成布置方案
    ↓ [WebSocket 推送]
Web 显示结果

【触发方式 3：自动修正触发】
用户手动拖拽模块，造成重叠
    ↓
Web 前端 → Server（提交修改）
    ↓ [PlacementService.ValidateLayout()]
检测到重叠错误
    ↓ [PlacementService.AutoFix()]
自动修正布置
    ↓ [WebSocket 推送]
Web 显示修正后的结果
```

### 3. PlacementService 内部设计

- **优先级**：Suggestion

- **建议结构**：

```
BIMCanvas.Server/
├── Services/
│   ├── PlacementService.cs          ← 布置逻辑核心
│   │   ├── GenerateLayout()         ← 整体生成
│   │   ├── AdjustModule()           ← 局部调整
│   │   ├── ValidateLayout()         ← 验证布置
│   │   └── AutoFix()                ← 自动修正
│   │
│   └── PlacementAgentBridge.cs      ← AI Agent 桥接（可选）
│       └── InvokeAI()               ← 调用 Claude API（复杂决策时）
│
├── McpTools/
│   └── PlacementTools.cs            ← MCP 工具封装
│       ├── generate_layout          ← 暴露给 Claude Code
│       └── adjust_module
│
├── Controllers/
│   └── PlacementController.cs       ← REST API
│       └── POST /api/canvas/{id}/generate
│
└── Hubs/
    └── CanvasHub.cs
        └── OnLayoutError()          ← 检测到错误时触发修正
```

### 4. 决策分层策略

- **优先级**：Suggestion

- **双轨决策**：

```csharp
public class PlacementService
{
    private readonly RuleEngine _rules;           // 规则引擎（快速、确定性）
    private readonly PlacementAgentBridge _aiBridge;  // AI 桥接（复杂决策）

    public async Task<LayoutResult> GenerateLayoutAsync(
        string canvasId,
        GenerateOptions options)
    {
        // 1. 获取画布状态
        var canvas = await _stateManager.GetAsync(canvasId);

        // 2. 简单场景：使用规则引擎
        if (IsSimpleCase(canvas, options))
        {
            return _rules.Generate(canvas, options);
        }

        // 3. 复杂场景：调用 AI
        return await _aiBridge.InvokeAI(canvas, options);
    }

    private bool IsSimpleCase(CanvasDocument canvas, GenerateOptions options)
    {
        // 判断条件：Zone 数量少、无复杂约束、用户未指定特殊需求等
        return canvas.Zones.Count <= 2 && !options.HasCustomConstraints;
    }
}
```

### 5. 潜在风险

- **PlacementAgentBridge 的实现复杂度**：调用 Claude API 需要处理 Token 限制、超时、重试等问题
  - **缓解**：MVP 阶段可先只实现规则引擎，AI 桥接作为后续增强

- **自动修正的触发条件**：过于敏感可能导致用户操作被频繁覆盖
  - **缓解**：设置阈值（如重叠面积 > 10%）或用户确认机制

- **多入口一致性**：MCP、REST、SignalR 调用同一服务，需确保状态同步
  - **缓解**：PlacementService 内部加锁或使用乐观并发控制

---

## 讨论 (Discussion)

<!-- 请在此处追加新的讨论内容，格式：### [YYYY-MM-DD HH:mm] [专家名]: 观点 -->

### [2025-12-05 10:00] Claude: 初始观点

提出"Server 内部服务 + MCP 工具封装"的混合架构方案：

- **PlacementService**：Server 内部的布置逻辑核心
- **MCP 工具封装**：暴露给 Claude Code 调用
- **三种触发方式**：AI 对话、Web 按钮、自动修正
- **双轨决策**：简单场景用规则引擎，复杂场景调用 AI

等待 @用户 对以下问题的意见：

1. 是否认同 PlacementAgent 作为 Server 内部服务的架构位置？
2. 三种触发方式是否覆盖了所有场景？
3. 规则引擎 vs AI 的边界如何划分？

### [2025-12-05 10:05] 用户: 核心观点

针对 @Claude 的问题，我提出以下观点：

**触发 AI 生成布置方案的应该是一个子 Agent（PlacementAgent）**，而非直接操作。

触发这个 Agent 会有几种方式：
1. **AI 通过对话/上下文自己调用 Agent**：用户指示 AI 进行整体、局部的生成动作
2. **Web 端绘制按钮触发**：通过设置绘制类型快捷的指导 AI 调用 Agent
3. **程序判断触发**：绘制的结果不理想/明显错误（如模块重叠、模块超出设计区）时自动触发

### [2025-12-05 10:10] Claude: 回应用户观点

@用户 的观点与我的方案高度一致。补充几点：

**1. 关于"子 Agent"的概念**

用户提出的"子 Agent"概念很精准。在我的方案中：
- **PlacementService** 就是这个"子 Agent"的实现
- 它不是独立进程，而是 Server 内部的服务
- 通过不同入口（MCP/REST/SignalR）被调用

**2. 关于三种触发方式的对应关系**

| 用户表述 | 我的方案对应 |
|----------|--------------|
| AI 通过对话调用 | MCP: `generate_layout` → PlacementService |
| Web 端绘制按钮 | REST: `POST /api/canvas/{id}/generate` |
| 程序判断触发 | CanvasHub.OnLayoutError() → PlacementService.AutoFix() |

**3. 待确认事项**

- PlacementAgentBridge 如何调用 Claude API？（需要 API Key 管理）
- 规则引擎 vs AI 的边界如何划分？
- 自动修正的触发条件和修正策略

### [2025-12-05 10:15] 用户: PlacementAgent 应作为 Claude Code SubAgent

@Claude 我重新考虑了架构位置问题。

**核心观点**：PlacementAgent 不应该是 Server 内部服务，而应该**充分发挥 Claude Code 的原生功能**。

Claude Code 本身就有 SubAgents 的功能，PlacementAgent 很自然的就是应该作为 Claude Code 的一个 SubAgent。

**理由**：
1. **AI 能力复用**：Claude Code 本身就是 AI Agent，为什么要在 Server 里再实现一个 AI 调用（PlacementAgentBridge）？
2. **上下文共享**：作为 SubAgent 可以共享 Claude Code 的对话上下文
3. **工具调用**：可以直接使用 Claude Code 已有的 MCP 工具
4. **触发机制**：通过 SubAgent 的描述自然触发，符合 Claude Code 设计哲学

**数据流调整**：
```
Claude Code 主 AI 获取到模块布置数据后
    ↓ 通过 MCP 工具
把数据返回给 Web 端的画布上
```

### [2025-12-05 10:20] Claude: 三种触发方式可行性分析

@用户 如果 PlacementAgent 作为 Claude Code 的 SubAgent，我们需要分析三种触发方式的可行性：

**架构对比**：

```
【原方案 - Server 端 AI 服务】
Claude Code → MCP: generate_layout → Server.PlacementService
                                         ↓
                                    PlacementAgentBridge → Claude API (再调一次 AI?!)
                                         ↓
                                    返回结果 → WebSocket → Web

【用户方案 - Claude Code SubAgent】
Claude Code (主 Agent)
    ↓ 识别布置意图，启动 SubAgent
PlacementAgent (Claude Code SubAgent)
    ↓ MCP: canvas_get_state (读取画布)
    ↓ MCP: module_search (查询模块库)
    ↓ AI 推理生成方案
    ↓ MCP: module_add / module_move (写入画布)
Server (只做状态管理)
    ↓ WebSocket
Web (显示结果)
```

**可行性分析**：

| 触发方式 | Claude Code 实现方式 | 可行性 |
|----------|---------------------|--------|
| **AI 对话触发** | Claude Code 识别意图 → 启动 SubAgent | ✅ 天然支持 |
| **Web 按钮触发** | Web → ??? → Claude Code | ❓ 需要外部触发机制 |
| **自动修正触发** | Server 检测错误 → ??? → Claude Code | ❓ 需要外部触发机制 |

**核心问题**：Claude Code 是否支持从外部程序注入消息/任务？

### [2025-12-05 10:25] 用户: Web 按钮和自动修正应通过程序推送消息

@Claude 我认为原"Web 按钮触发"和"自动修正触发"的触发原因应该保留，但是后续触发方式可以改成：

**程序判断符合这两种情况之一时，自动在当前 Claude Code 终端通过 MCP/终端打印对话的形式，把当前情况返回给 Claude Code 主 AI。**

类似模拟人类用户的行为：
```
（两下 Esc 终止 Claude Code 的当前操作）
+ "hi，Claude，我发现当前项目中模块 X 布置超出了设计区，请你重新设计"
```

请调研这种外部消息注入机制是否可行。

### [2025-12-05 10:30] Claude: 技术可行性分析

@用户 这是一个非常精准的问题抽象！本质上是：**外部程序（Server）→ 向正在运行的 Claude Code 注入一条"用户消息"**

**MCP 协议层面分析**：

MCP 协议支持双向通信：
- **Request/Response**：Client → Server（Claude Code 调用工具）
- **Notifications**：Server → Client（服务端主动通知）

```typescript
// MCP 规范中的 notification 机制
{
  "jsonrpc": "2.0",
  "method": "notifications/message",
  "params": {
    "level": "info",
    "message": "用户在 Web 端点击了「一键布置客厅」"
  }
}
```

**可能的实现方案对比**：

| 优先级 | 方案 | 实现难度 | 实时性 |
|--------|------|----------|--------|
| 1 | MCP Notifications | 需验证 Claude Code 支持度 | ✅ 实时 |
| 2 | 工具返回值携带 pendingEvents | 简单 | ⚠️ 依赖下次调用 |
| 3 | 轮询（定期 check_events） | 简单 | ❌ 延迟 |

**方案 2 示例**：
```json
// 任何 Canvas-MCP 工具的返回值
{
  "result": { /* 正常返回 */ },
  "pendingEvents": [
    {
      "type": "layout_request",
      "source": "web_button",
      "message": "用户在 Web 端点击了「一键布置客厅」",
      "zoneId": "zone_living",
      "timestamp": "2025-12-05T10:30:00Z"
    }
  ]
}
```

建议先实现方案 2，同时研究 Claude Code 对 MCP notifications 的支持。

### [2025-12-05 10:35] 用户: 核心问题抽象

@Claude 总结一下，问题的根本是：

**如何实现在 Claude Code 运行时，我们的 Server 检测到事件后，能返回指定信息给到当前 Claude Code 程序？**

实现类似模拟人类用户的行为：
```
（两下 Esc 终止 Claude Code 的当前操作）
+ "hi，Claude，我发现当前项目中模块 X 布置超出了设计区，请你重新设计"
```

### [2025-12-05 10:40] Claude: 调研结果 - Claude Code 不支持外部消息注入

@用户 经过调研 Claude Code 官方文档和 Agent SDK，结论是：

**Claude Code 不支持外部消息注入**

| 机制 | 现状 | 原因 |
|------|------|------|
| **外部消息注入** | ❌ 不支持 | 安全考虑（防提示注入）+ 单线程设计 |
| **MCP Notifications** | ⚠️ 有限支持 | 仅用于内部状态管理，不会转为用户提示 |
| **Hooks** | ⚠️ 被动触发 | 只在预定义检查点执行，不能被外部调用 |

**影响评估**：

| 触发方式 | Claude Code 方案 |
|----------|------------------|
| AI 对话触发 | ✅ 可行 |
| Web 按钮触发 | ❌ 无法直接实现 |
| 自动修正触发 | ❌ 无法直接实现 |

**备选方案**：Anthropic 提供了 **Agent SDK**，可以构建独立的 AI Agent 应用，支持更灵活的事件驱动机制。

### [2025-12-05 10:45] 用户: 询问 Agent SDK

@Claude Agent SDK 是什么？展开介绍一下。

需要了解：
1. 它是什么？
2. 与 Claude Code 的关系？
3. 能否解决我们的问题？

### [2025-12-05 10:50] Claude: Agent SDK 详细介绍

@用户 Agent SDK 是 Anthropic 官方提供的**生产级 Agent 框架**。

**1. Agent SDK 是什么**

Claude Agent SDK 将驱动 Claude Code 的核心引擎抽象为可编程的 SDK，让开发者能够构建**任意领域**的 AI Agent。

```python
# 安装
pip install claude-agent-sdk

# 基本使用
from claude_agent_sdk import ClaudeSDKClient, ClaudeAgentOptions

options = ClaudeAgentOptions(
    model="claude-sonnet-4-5",
    system_prompt="你是 BIMCanvas 的布置助手...",
    mcp_servers={"bimcanvas": bimcanvas_mcp_server}
)

client = ClaudeSDKClient(options)
session = client.create_session(user_id="user_123")

# 执行查询
response = await session.query(prompt="帮我布置客厅")
```

**官方资源**：
- Python SDK: https://github.com/anthropics/claude-agent-sdk-python
- TypeScript SDK: https://github.com/anthropics/claude-agent-sdk-typescript
- 文档: https://docs.claude.com/en/api/agent-sdk/overview

**2. 与 Claude Code 的关系**

| 维度 | Claude Code | Agent SDK |
|------|-------------|-----------|
| **本质** | 产品（CLI/IDE） | 底层框架 |
| **使用者** | 终端用户 | 开发者 |
| **交互方式** | 交互式对话 | 可编程 API |
| **扩展方式** | Hooks、Slash Commands | 完整应用开发 |
| **运行模式** | 按需启动 | **可长期运行** ✨ |
| **外部事件** | ❌ 不支持 | ✅ **支持** |

**关键洞察**：Claude Code 是 Agent SDK 的一个"实例"。Agent SDK 开放了构建类似 Claude Code 的能力。

**3. 能否解决我们的问题**

✅ **可以！** 通过 Agent SDK，我们可以：
- 构建一个**长期运行**的 PlacementAgent 服务
- 通过 **SSE/WebSocket** 接收外部事件
- 调用 MCP 工具操作画布

**三种触发方式对比**：

| 触发方式 | Claude Code 方案 | Agent SDK 方案 |
|----------|------------------|----------------|
| AI 对话触发 | ✅ 支持 | ✅ 支持 |
| Web 按钮触发 | ❌ 无法实现 | ✅ **实时事件** |
| 自动修正触发 | ❌ 无法实现 | ✅ **实时事件** |

### [2025-12-05 10:55] 用户: 采用 Agent SDK 方向

@Claude 如果这个 Agent SDK 项目真实存在，我希望进一步考虑如何把 Agent SDK 集成到当前项目的流程中。

### [2025-12-05 11:00] Claude: BIMCanvas + Agent SDK 集成架构设计

@用户 以下是完整的集成架构设计：

**1. 目标架构图**

```
┌─────────────────────────────────────────────────────────────────────┐
│  BIMCanvas.Agent (Python 3.10+, 长期运行)                            │
│  ├── PlacementAgent 模块 (基于 Agent SDK)                            │
│  ├── EventListener (SSE 客户端)                                      │
│  └── MCP 工具集成                                                    │
└─────────────────────────────────────────────────────────────────────┘
         ↑ 事件推送 (SSE)          ↓ MCP / HTTP 调用
         │                         │
┌────────┴─────────────────────────┴──────────────────────────────────┐
│  BIMCanvas.Server (.NET 6+)                                          │
│  ├── CanvasStateManager                                              │
│  ├── EventBus ← 新增                                                 │
│  └── Controllers/EventsController ← 新增 SSE 端点                    │
└─────────────────────────────────────────────────────────────────────┘
         ↓ WebSocket
┌─────────────────────────────────────────────────────────────────────┐
│  BIMCanvas.Web (Vue 3)                                               │
│  └── 「一键布置」按钮 → EventBus                                     │
└─────────────────────────────────────────────────────────────────────┘
```

**2. 三种触发方式实现**

| 触发方式 | 数据流 |
|----------|--------|
| **AI 对话** | 用户 → Agent Chat UI → `session.query()` |
| **Web 按钮** | Web → Server EventBus → SSE → Agent EventListener → `session.query()` |
| **自动修正** | Server 检测错误 → EventBus → SSE → Agent EventListener → `session.query()` |

**3. Agent 核心代码示例**

```python
# bimcanvas_agent/main.py
import anyio
from claude_agent_sdk import ClaudeSDKClient, ClaudeAgentOptions
from .event_listener import EventListener
from .mcp_tools import create_bimcanvas_mcp_server

async def main():
    # 创建 MCP 工具服务器
    mcp_server = create_bimcanvas_mcp_server()

    # 配置 Agent
    options = ClaudeAgentOptions(
        model="claude-sonnet-4-5",
        system_prompt="""你是 BIMCanvas 的布置助手。
        你的职责是在建筑平面内布置符合设计逻辑的家具组合。
        遵循以下原则：
        - 模块 bounds 必须在 zone.innerBoundary 内
        - 模块不能与 exclusionAreas 或其他模块重叠
        - 使用语义化朝向 (north/south/east/west)
        """,
        mcp_servers={"bimcanvas": mcp_server}
    )

    client = ClaudeSDKClient(options)

    # 启动事件监听器
    event_listener = EventListener(
        server_url="http://localhost:5000",
        on_event=lambda event: handle_event(client, event)
    )

    # 并行运行
    async with anyio.create_task_group() as tg:
        tg.start_soon(event_listener.start)
        tg.start_soon(run_chat_interface, client)

async def handle_event(client: ClaudeSDKClient, event: dict):
    """处理来自 Server 的事件"""
    if event["type"] == "layout_request":
        session = client.create_session(user_id=event["user_id"])
        await session.query(
            prompt=f"请为 {event['zone_name']} 区域生成布置方案"
        )
    elif event["type"] == "placement_error":
        session = client.create_session(user_id=event["user_id"])
        await session.query(
            prompt=f"检测到布置错误：{event['error_message']}，请修正"
        )
```

**4. Server 端 EventBus 设计**

```csharp
// BIMCanvas.Server/Services/EventBus.cs
public class EventBus
{
    private readonly Channel<AgentEvent> _channel;

    public EventBus()
    {
        _channel = Channel.CreateUnbounded<AgentEvent>();
    }

    public async Task PublishAsync(AgentEvent evt)
    {
        await _channel.Writer.WriteAsync(evt);
    }

    public IAsyncEnumerable<AgentEvent> SubscribeAsync(CancellationToken ct)
    {
        return _channel.Reader.ReadAllAsync(ct);
    }
}

// BIMCanvas.Server/Controllers/EventsController.cs
[ApiController]
[Route("api/events")]
public class EventsController : ControllerBase
{
    [HttpGet("agent")]
    public async Task GetAgentEvents(CancellationToken ct)
    {
        Response.ContentType = "text/event-stream";

        await foreach (var evt in _eventBus.SubscribeAsync(ct))
        {
            var json = JsonSerializer.Serialize(evt);
            await Response.WriteAsync($"data: {json}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
    }
}
```

**5. 事件格式定义**

```json
{
  "id": "evt_001",
  "type": "placement_request",
  "timestamp": "2025-12-05T10:30:00Z",
  "payload": {
    "canvasId": "canvas_001",
    "trigger": "web_button",
    "scope": "full",
    "zoneId": null,
    "constraints": { "preferredStyle": "modern" }
  }
}
```

**6. 项目结构新增**

```
BIMCanvas/
├── ... (现有项目)
│
└── BIMCanvas.Agent/                 ← 新增 Python 项目
    ├── pyproject.toml
    ├── src/
    │   ├── __init__.py
    │   ├── main.py                  入口
    │   ├── agent/
    │   │   ├── placement_agent.py   PlacementAgent 实现
    │   │   └── prompts.py           系统提示词
    │   ├── events/
    │   │   ├── listener.py          SSE 事件监听器
    │   │   └── handlers.py          事件处理器
    │   └── mcp/
    │       └── canvas_client.py     MCP 工具客户端
    └── tests/
```

**7. 实施路径**

| Phase | 内容 | 依赖 |
|-------|------|------|
| 1 | Server 添加 EventBus + SSE 端点 | BIMCanvas.Server 已存在 |
| 2 | 创建 BIMCanvas.Agent Python 项目骨架 | Agent SDK 安装 |
| 3 | 实现 Agent SDK 集成 + MCP 工具调用 | Server REST API |
| 4 | 实现三种触发方式 | Phase 1-3 |
| 5 | 测试与优化 | Phase 4 |

---

## 结论与行动 (Conclusion & Actions)

### 达成共识

**1. 架构方向**：采用 Agent SDK 实现 PlacementAgent

经过讨论，确认以下关键决策：
- PlacementAgent 从 **Server 内部服务** → **独立 Python Agent**
- 基于 **Anthropic Agent SDK** 构建
- 支持**长期运行**，监听外部事件触发

**2. 新增项目**：BIMCanvas.Agent (Python)

| 属性 | 值 |
|------|-----|
| 运行时 | Python 3.10+ |
| 框架 | Anthropic Agent SDK |
| 职责 | 接收事件、执行布置规划、调用 MCP 工具 |

**3. 事件驱动机制**

| 组件 | 位置 | 职责 |
|------|------|------|
| EventBus | BIMCanvas.Server | 事件发布/订阅 |
| EventsController | BIMCanvas.Server | SSE 端点 (`/api/events/agent`) |
| EventListener | BIMCanvas.Agent | SSE 客户端 |

**4. 三种触发方式**

| 触发方式 | 实现路径 | 实时性 |
|----------|----------|--------|
| AI 对话 | 用户 → Agent Chat → `session.query()` | ✅ 实时 |
| Web 按钮 | Web → Server EventBus → SSE → Agent | ✅ 实时 |
| 自动修正 | Server 检测 → EventBus → SSE → Agent | ✅ 实时 |

### 架构变更总结

| 维度 | 原方案 | 新方案 |
|------|--------|--------|
| PlacementAgent 位置 | Server 内部服务 | 独立 Python Agent |
| AI 调用方式 | PlacementAgentBridge → Claude API | Agent SDK 原生支持 |
| 外部事件支持 | 不支持 | SSE 事件驱动 |
| 技术栈 | .NET 6+ | Python 3.10+ + Agent SDK |

### 后续行动

| 任务 | 负责方 | 优先级 | 状态 |
|------|--------|--------|------|
| 更新 Architecture.md | Claude | P0 | ⬜ 待执行 |
| 更新 CLAUDE.md | Claude | P1 | ⬜ 待执行 |
| 更新 README.md | Claude | P1 | ⬜ 待执行 |
| 创建 BIMCanvas.Agent 项目 | 后续 | P2 | ⬜ 待规划 |
| 实现 Server EventBus | 后续 | P2 | ⬜ 待规划 |

### 实施路径

| Phase | 内容 | 预计产出 |
|-------|------|----------|
| **Phase 1** | Server 添加 EventBus + SSE 端点 | EventBus.cs, EventsController.cs |
| **Phase 2** | 创建 BIMCanvas.Agent Python 项目骨架 | pyproject.toml, 目录结构 |
| **Phase 3** | 实现 Agent SDK 集成 + MCP 工具调用 | placement_agent.py, canvas_client.py |
| **Phase 4** | 实现三种触发方式 | event_listener.py, handlers.py |
| **Phase 5** | 测试与优化 | 测试用例, 性能优化 |
