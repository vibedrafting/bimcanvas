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

---

## 结论与行动 (Conclusion & Actions)

<!-- 讨论结束后填写，汇总达成的共识和后续行动 -->
