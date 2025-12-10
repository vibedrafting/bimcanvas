# ServerWeb_Implementation_Review

> [!IMPORTANT]
> **协作规则**
> 1. **追加式讨论**：所有新意见请以 `### [时间戳] [专家名]: [观点]` 格式追加在 "深入讨论" 章节。
> 2. **严禁修改**：禁止修改其他专家的已存档观点。
> 3. **优先级标注**：明确区分 `[Blocker]` (阻碍性) 与 `[Suggestion]` (建议性)。

> [!TIP]
> **讨论原则**
> - **建设性**：反对时请提供替代方案。
> - **聚焦核心**：优先解决架构风险与数据一致性。
> - **拥抱共识**：寻找折中方案或最优解，避免无休止的争论。

## 1. 议题概述

- **主题**：BIMCanvas.Server + BIMCanvas.Web 画布功能实施方案
- **发起时间**：2025-12-10
- **参与者**：Claude（系统架构师）、用户
- **背景信息**：

### 1.1 项目当前状态

| 模块 | 运行时 | 状态 | 说明 |
|------|--------|------|------|
| BIMCanvas.Core | .NET Standard 2.0 | ✅ 已完成 | 数据模型 + 空间算法 |
| BIMCanvas.Revit | .NET FW 4.7.2 | 🔶 Phase 1 完成 | 导出功能已完成，可输出精简版 CanvasDocument |
| BIMCanvas.Server | .NET 6+ | ⬜ 待开发 | 统一后端服务 |
| BIMCanvas.Web | Vue 3 + TS | ⬜ 待开发 | Web 前端 |

### 1.2 用户明确的优先需求

1. **显示 Revit 初始状态（未划分设计区）**
2. **显示划分设计区的功能**

### 1.3 对应系统流程（摘自 Workflows.md）

| 阶段 | 触发条件 | 执行者 | 输出 |
|------|----------|--------|------|
| Phase 1 | 用户点击"开始设计" | BIMCanvas.Revit | 精简版 CanvasDocument（outline + rooms） |
| Phase 2 | Server 收到 POST | BIMCanvas.Server | 完整版 CanvasDocument（+ zones + wallFinishes） |
| Phase 3 | Web 收到推送 | Web + 用户 | zones[].tags 确认 |

### 1.4 数据流概览

```
BIMCanvas.Revit                    BIMCanvas.Server                    BIMCanvas.Web
     │                                  │                                  │
     │  POST 精简版 CanvasDocument       │                                  │
     │  {                               │                                  │
     │    outline: { boundaries, openings },                               │
     │    rooms: [...],                 │                                  │
     │    zones: [],                    │                                  │
     │    wallFinishes: [],             │                                  │
     │    modules: []                   │                                  │
     │  }                               │                                  │
     ├─────────────────────────────────>│                                  │
     │                                  │                                  │
     │                                  │  ZoneCalculator 计算：            │
     │                                  │  - rooms[] → zones[]             │
     │                                  │  - zones[].innerBoundary         │
     │                                  │  - zones[].exclusionAreas        │
     │                                  │  - wallFinishes[]                │
     │                                  │                                  │
     │                                  │  WebSocket/HTTP 推送完整版        │
     │                                  ├─────────────────────────────────>│
     │                                  │                                  │
     │                                  │                   JSON → SVG 渲染 │
     │                                  │                   坐标转换 Y-flip │
```

### 1.5 相关文档

| 文档 | 路径 | 内容 |
|------|------|------|
| 架构文档 | `docs/Architecture.md` | 系统架构（§4 Server、§6.3 Web） |
| 执行流程 | `docs/Workflows.md` | 端到端执行流程、触发机制 |
| JSON Schema | `docs/Schema-JSON.md` | v2.5 数据模型定义 |
| Core 实现计划 | `plans/Core_Implementation_Plan.md` | Core 层代码生成计划（参考） |
| Revit 实施计划 | `plans/Revit_Implementation_Plan.md` | Revit 插件实施计划（参考） |

---

## 2. 初始观点

### 专家：Claude

- **核心观点**：采用 MVP 渐进式开发，Phase 1 聚焦"静态画布渲染"，暂缓实时协作和 AI 布置功能。

- **详细分析**：

  - **[论点 1] 最小可行范围定义**：

    根据用户需求"显示 Revit 初始状态 + 显示划分设计区"，MVP 只需实现：

    | 功能 | Server 职责 | Web 职责 |
    |------|------------|----------|
    | 接收 Revit 数据 | REST API 接收 POST | - |
    | 存储画布状态 | 内存存储 CanvasDocument | - |
    | 计算设计区 | ZoneCalculator 服务 | - |
    | 渲染户型底图 | 提供 GET API | SVG 渲染 outline |
    | 渲染设计区 | - | SVG 渲染 zones + exclusionAreas |

    **Phase 1 暂不实现**：
    - WebSocket 实时同步（用 REST 轮询代替）
    - MCP 工具层
    - AI 布置功能（PlacementAgent）
    - 用户拖拽交互
    - SignalR Hub

  - **[论点 2] Server 技术选型建议**：

    | 组件 | 建议 | 理由 |
    |------|------|------|
    | 框架 | ASP.NET Core Minimal API | 轻量，Phase 1 够用，后续易扩展 |
    | 运行时 | .NET 8 | LTS 版本，性能好 |
    | 状态存储 | `ConcurrentDictionary<string, CanvasDocument>` | MVP 无需持久化，内存足够 |
    | 通信协议 | REST API（Phase 1）| 简单可靠，后续再加 WebSocket |
    | JSON 序列化 | Newtonsoft.Json | 与 Core 层保持一致，已有 Converter |
    | 依赖注入 | 内置 DI | 标准做法 |

  - **[论点 3] Web 技术选型建议**：

    | 组件 | 建议 | 理由 |
    |------|------|------|
    | 框架 | Vue 3 + TypeScript | 文档已定，团队熟悉 |
    | 状态管理 | Pinia | Vue 3 官方推荐 |
    | 画布渲染 | **原生 SVG**（Phase 1） | 无交互需求，无需引入 Konva/Fabric |
    | 构建工具 | Vite | 开发体验好，HMR 快 |
    | HTTP 客户端 | fetch / axios | 标准选型 |
    | 样式方案 | Tailwind CSS 或纯 CSS | 按团队偏好 |

  - **[论点 4] 关键实现路径（建议分 4 个子阶段）**：

    ```
    Phase 1A: Server 基础骨架
    ├── 项目初始化（ASP.NET Core Minimal API）
    ├── CanvasController
    │   ├── POST /api/canvas          接收 Revit 数据
    │   └── GET  /api/canvas/{id}     获取画布状态
    ├── CanvasStateManager（内存存储）
    └── 引用 BIMCanvas.Core

    Phase 1B: ZoneCalculator 核心计算
    ├── rooms[] → zones[] 转换
    │   └── 生成 zone.id, zone.roomId, zone.tags
    ├── rawBoundary = room.boundary
    ├── innerBoundary 计算
    │   └── rawBoundary - wallFinishes[].exclusionBoundary
    ├── exclusionAreas 计算
    │   └── 门扇禁区（根据 openings 中 type=door 的数据）
    └── wallFinishes 生成
        └── 根据 room.type 查询默认完成面厚度

    Phase 1C: Web 静态渲染
    ├── 项目初始化（Vite + Vue 3 + TypeScript + Pinia）
    ├── TypeScript 类型定义（对应 Core 模型）
    ├── canvasStore（Pinia 状态管理）
    ├── SvgCanvas.vue（画布主组件）
    │   ├── 渲染 outline.boundaries（墙体轮廓）
    │   ├── 渲染 outline.openings（门窗线段）
    │   ├── 渲染 zones[].innerBoundary（设计区边界）
    │   └── 渲染 zones[].exclusionAreas（禁区）
    └── 坐标转换工具函数
        └── y_screen = canvasHeight - y_model * scale

    Phase 1D: 端到端集成
    ├── Revit 导出 JSON → 手动 POST 到 Server
    ├── Server 计算 zones → 返回完整 CanvasDocument
    └── Web 拉取数据 → 渲染画布
    ```

  - **[论点 5] 待讨论的设计决策**：

    | # | 决策点 | 选项 A | 选项 B | 我的倾向 | 理由 |
    |---|--------|--------|--------|----------|------|
    | 1 | Server 与 Web 部署方式 | 合并部署（Server 托管 SPA） | 独立部署（分离端口） | **A** | 简化运维，开发阶段方便 |
    | 2 | ZoneCalculator 位置 | Server 内部服务 | 独立 .NET 类库 | **A** | MVP 够用，后续按需重构 |
    | 3 | 完成面厚度配置 | 硬编码规则表 | 外部配置文件/数据库 | **A** | 先跑通，配置化是后续优化 |
    | 4 | Web 获取数据方式 | 轮询 REST | WebSocket 推送 | **A** | Phase 1 无实时需求 |
    | 5 | 门扇禁区形状 | 矩形 AABB | 扇形精确几何 | **A** | KISS 原则，矩形足够 |
    | 6 | SVG vs Canvas 渲染 | 原生 SVG | Konva.js / Fabric.js | **A** | Phase 1 无交互，后续按需引入 |

  - **[论点 6] 风险识别**：

    | 风险 | 影响 | 缓解措施 |
    |------|------|----------|
    | ZoneCalculator 算法复杂度 | 可能出现边界计算 bug | 单元测试覆盖核心算法 |
    | 坐标系转换混乱 | 渲染位置错误 | 统一在 Web 层做 Y-flip |
    | JSON 序列化不一致 | 数据解析失败 | 复用 Core 层 Converter |



### 专家：Codex

- **[Blocker] 数据契约与版本治理缺位**：Phase 1→Phase 2 的交接完全依赖 Revit 输出的精简版 CanvasDocument，而后端要在 <1s 内补齐 zones / wallFinishes 并推送给 Web（docs/Workflows.md:60,96,178）。如果 Server 仅做“内存存储 + 轻量计算”，但没有针对 metadata.version、房间 ID 稳定性、坐标系合法性的校验，就无法保证 ZoneCalculator 的缩放/布尔运算在 Core 对象层面是安全的，后续 PlacementAgent 也会因为脏数据反复回退。建议在 CanvasController 中加入 Schema 版本白名单、面域合法性检查以及 ConcurrentDictionary 写入前的版本号递增逻辑，契合 Architecture 文档要求的 CanvasStateManager 行为（docs/Architecture.md:645,1285）。
- **[Blocker] Phase 2 必须保留实时推送能力**：当前议题打算用“REST 轮询”替代 WebSocket（reviews/ServerWeb_Implementation_Review.md:44-57），但 Workflows 明确 Phase 2 结束后由 Server 主动推送完整版 CanvasDocument，紧接着 Phase 3 要求前端与 PlacementAgent 协同确认 zones[].tags（docs/Workflows.md:60-65）。如果只靠轮询，PlacementAgent 无法及时拿到新的 innerBoundary / pendingCommits，事件链也无法按 Architecture §5 的设计走 EventBus → SSE → Agent（docs/Architecture.md:645）。我的建议是在 MVP 就实现一个最小的 SignalR Hub（单向 broadcast），即便没有全量事件系统，也要保证 Web 一次性收到 Server 计算完的结果。
- **[Suggestion] ZoneCalculator 应完全复用 Core 几何能力**：Phase 2 里 shrink rawBoundary、计算门禁区、生成 wallFinishes 都依赖 Core 层对 Polygon2D 的布尔计算与坐标转换（docs/Workflows.md:178-249；docs/Architecture.md:1285-1299）。如果在 Server 里图省事用手写几何，会与 Core 的 NetTopologySuite 实现产生误差，导致 Web 渲染与后续 Revit 回写的坐标无法对齐。建议 Server 引用 BIMCanvas.Core 并通过统一的 Polygon2D / Point2D API 组装 innerBoundary，同时把门扇禁区（ComputeDoorSwingRect）封装成可单元测试的服务，避免推送脏几何。
- **[Suggestion] Web 层需要提前设计“纯显示模式”的状态管理**：Architecture §6.3 已定义 canvasStore + SvgRenderer 的职责（docs/Architecture.md:1347-1395），而当前讨论只提“静态渲染”但没说明如何管理 canvasId、多画布之间的切换、以及 Y-flip/缩放策略。在 Phase 2/3 中，前端至少要：1）持久存储 Server 返回的最新 version；2）在渲染层区分 outline / zones / exclusionAreas；3）预留 pendingChanges（即使 Phase 5 暂不启用）。否则后续接入 PlacementAgent 或用户交互时还要推倒重来。
- **[Suggestion] 部署方式需兼顾未来拆分与当前效率**：综上，我更倾向于“Server 托管 SPA”的组合部署（reviews/ServerWeb_Implementation_Review.md:91-100），但前提是项目结构上仍把 Web 视作独立 Vite 工程，通过 npm run build 产物复制到 Server 的 wwwroot。这样可以在 MVP 确保一键部署，同时也方便将来把 BIMCanvas.Web 独立到 CDN 而不影响 Server 的 MCP/事件总线职责。




### 专家：Gemini

- **核心观点**：Phase 1 实施必须以 **"打通事件驱动基础设施"** 为首要目标，而非简单的静态显示。Web 与 Agent 的实时协作机制是系统的核心命脉，不应推迟到后续阶段。

- **详细分析**：

  - **[论点 1] 必须建立统一的事件总线 (EventBus) 基础设施**：
    系统架构文档 (Architecture.md v2.7) 明确定义 `BIMCanvas.Server` 为 connecting hub。
    - **现状风险**：如果 Phase 1 仅实现 REST API，将导致架构分裂。Web 端 (Vue) 和 Python Agent (v2.7) 均设计为事件驱动。
    - **实施建议**：即便在 Phase 1 功能有限，也必须部署 **ASP.NET Core SignalR (面向 Web)** 和 **Server-Sent Events (SSE, 面向 Agent)** 的基础端点。这是为了确保 Phase 2/3 中 Agent 接入时，通信协议栈已经就绪，无需重构 Server 核心。

  - **[论点 2] Server 端几何计算的架构归属**：
    Server 端的 `ZoneCalculator` 承担着从 Revit 原始数据到设计区 (Zone) 的转换重任。
    - **技术约束**：严禁在 Server 项目中重新实现几何算法。Server 必须直接引用 `BIMCanvas.Core` NuGet 包。
    - **实施细节**：`ZoneCalculator` 应作为一个编排服务 (Orchestrator)，调用 Core 层的 `Polygon2D` 布尔运算 API 和 `UnitConverter` 进行数据处理。所有涉及几何精度、容差处理的逻辑，必须收敛在 Core 层，保证与 Revit 端逻辑的一致性。

  - **[论点 3] Web 端坐标变换的严格分层**：
    针对架构文档 §1.5 中定义的坐标系规范：
    - **原则**：后端传输的 JSON 数据必须严格保持 CAD 标准坐标系 (Y-Up)。
    - **实施**：Web 端必须在 **视图渲染层 (SVG 生成)** 显式处理 Y 轴翻转 (`canvasHeight - y`)，严禁在 Server 端为了前端便利而预先翻转坐标。这关乎后续 AI 对空间方位 (North/South) 的理解一致性，以及 Revit 回写时的坐标还原精度。

  - **[论点 4] 部署架构的实用性建议**：
    鉴于项目包含 .NET Server, Python Agent, 和 Vue Web 三个异构部分：
    - **建议**：采用 **"Server 托管 Web 静态资源"** 的模式。在开发阶段，Web 项目作为独立 Vite 工程开发；在运行/部署阶段，Vite 构建产物应嵌入 `BIMCanvas.Server` 的 `wwwroot`。这样可以简化用户的启动流程（只需启动 Server 和 Agent），同时避免跨域 (CORS) 问题，并未将来可能的 CDN 分离部署留有余地。

  - **[论点 5] 状态管理的类型映射**：
    Web 前端的 Pinia Store 结构应严格镜像 `CanvasDocument` 的数据结构。
    - **理由**：为了支持后续的细粒度更新（如 AI 移动一个家具），前端状态树应避免过度扁平化或视图化，保持与后端模型的一致性将极大降低 Patch/Diff 逻辑的复杂度。

---

## 3. 深入讨论

<!-- 请在此分隔线下方追加新的讨论内容 -->

### [2025-12-10 17:30] Claude: 回应 Codex 和 Gemini 的意见，修正立场并确认共识

**一、关于 "实时推送 vs REST 轮询" 的分歧（接受修正）**

Codex 和 Gemini 都将"必须保留实时推送能力"标记为 [Blocker]。经过反思，我承认初始观点过于保守：

- Workflows.md 明确要求 Phase 2 结束后 Server 主动推送完整版 CanvasDocument
- Architecture.md §6.4 定义了 EventBus → SSE → Agent 的事件链，Phase 1 只用 REST 会导致 Phase 2 大规模重构

**折中方案**：Phase 1 实现**最小 SignalR Hub（单向 broadcast）**：
- 仅实现 `CanvasHub.BroadcastDocument(canvasId, document)`
- Web 端连接 Hub 后被动接收推送
- 暂不实现双向交互、pendingCommits 等复杂逻辑
- SSE 端点暂缓（Phase 1 无 Agent 接入）

**二、关于 "数据契约与版本治理"（接受 Codex 建议）**

Codex 提出的输入校验非常有价值，接受以下建议：
- Schema 版本校验：CanvasController.POST 验证 `coordinateSystem == "cartesian_mm_yUp"`
- 版本号递增：CanvasStateManager 写入时自动 `document.Version++`
- 面域合法性：rooms[].boundary 至少 3 顶点，多边形不自交

暂缓：房间 ID 稳定性检查、pendingCommits 机制（Phase 1 无用户编辑）

**三、三方共识确认**

| # | 共识点 | Claude | Codex | Gemini | 说明 |
|---|--------|--------|-------|--------|------|
| 1 | Server 托管 SPA 部署 | ✓ | ✓ | ✓ | Web 作为独立 Vite 工程，构建产物复制到 wwwroot |
| 2 | ZoneCalculator 复用 Core 几何能力 | ✓ | ✓ | ✓ | 禁止在 Server 重写几何算法 |
| 3 | 坐标转换在 Web 层做 Y-flip | ✓ | ✓ | ✓ | Server 传输原始 Y-Up 坐标 |
| 4 | Pinia Store 镜像 CanvasDocument | ✓ | ✓ | ✓ | 避免过度扁平化 |
| 5 | 门扇禁区用矩形 AABB | ✓ | - | - | KISS 原则 |
| 6 | Phase 1 实现最小 SignalR Hub | ✓ | ✓ | ✓ | 单向 broadcast，暂不实现 SSE |

**四、需要用户决策的待定事项**

| # | 决策点 | 选项 A | 选项 B | 我的倾向 |
|---|--------|--------|--------|----------|
| 1 | SSE 端点 Phase 1 是否预埋？ | 暂缓 | 预埋空端点 | A（暂缓） |
| 2 | Web 端预留 pendingChanges？ | 不预留 | 预留空数组 | B（预留） |
| 3 | 完成面厚度配置方式？ | 硬编码 | appsettings.json | A（硬编码） |



### [2025-12-10 17:56] User: 

1. SSE 端点 Phase 1 是否预埋？
   需要专家进一步讨论
2. Web 端预留 pendingChanges？
   我认为需要为Phase 2 提前考虑
3. 完成面厚度配置方式？
   需要一个配置文件，进行硬编码，后期替换成从知识库/项目配置文件获取



### [2025-12-10 18:05] Codex: 针对 SSE、pendingChanges 与完成面配置的进一步讨论

- **SSE 端点（与 Claude 不同意见，倾向 [Blocker]）**：虽然 Claude 想把 SSE 延后，但 Architecture.md §6.4 和 Workflows.md Phase 3 都要求 PlacementAgent 通过 SSE 获知 Server 的事件。若 Phase 1 不把 EventsController（/api/events）骨架搭好，后续引入 Agent 时就得重构 CanvasStateManager 的事件派发。我建议**现在就实现轻量版 SSE**：EventBus 只发布“canvas_calculated”事件，Payload = { canvasId, version }，Server 端不需要任何 AI 逻辑；Agent 暂可不订阅，但接口存在即可。这样既满足用户“需要进一步讨论”的诉求，也避免未来再动 REST API。
- **Web 端 pendingChanges（与用户观点相同，属 [Suggestion]）**：我支持用户提出的“为 Phase 2 提前考虑”。建议在 Pinia store 中保留 pendingChanges: ElementChange[] 字段，并在 Phase 1 中用类型断言 + 空数组初始化。即使暂不渲染，也要定义 ElementChange 接口（elementId、deltaBounds、summary）。等到 Phase 2/5 用户交互或 Agent 回写时，store 和 SignalR 事件就能直接复用，不需要迁移状态树。
- **完成面厚度配置（对现有共识的补充，属 [Suggestion]）**：用户希望“有配置文件但当前硬编码”，可以折中：在 BIMCanvas.Server 中创建 App_Data/wall-finish.defaults.json，ZoneCalculator 通过 IOptions<WallFinishDefaults> 读取，未命中时回退到硬编码枚举。这样 Phase 1 就能把规则以 JSON 形式提交到 git（满足“需要一个配置文件”），同时保留将来切换到知识库或外部配置源（appsettings、数据库）的接口，避免把规则写死在代码里。



### [2025-12-10 18:08] Gemini: 架构决策收敛建议

基于各位专家的观点，我提出以下架构决策收敛建议，旨在消除 [Blocker] 并确立 Phase 1 的实施标准：

1.  **通信协议：否决轮询，确立 EventBus (SignalR + SSE)**
    *   **回应 Claude**：虽然 REST 轮询实现简单，但鉴于 Architecture v2.7 明确了 Python Agent 的 SSE 依赖，且 SignalR 在 ASP.NET Core 中开箱���用（几行代码即可配置 Hub），"节省的时间"不足以抵消架构不一致带来的技术债务。
    *   **决策**：Phase 1 必须实现 `CanvasHub` (SignalR) 用于前端推送，以及 `/api/events` (SSE) 端点骨架用于未来 Agent 接入。Web 端应直接使用 `@microsoft/signalr` 建立连接。

2.  **核心计算：ZoneCalculator 的严格约束**
    *   **支持 Codex**：Server 端绝对不能出现"手写几何算法"。
    *   **实施**：`BIMCanvas.Server` 项目必须引用 `BIMCanvas.Core`。`ZoneCalculator` 的代码审查标准应为：只调用 `Core.Algorithms` 和 `Core.Models`，不包含任何涉及 `Math.` 的自定义逻辑。

3.  **数据契约：版本号与校验**
    *   **支持 Codex**：`CanvasDocument` 的 `metadata.version` 必须从 Phase 1 起就生效。
    *   **补充**：建议在 Phase 1 的 POST 接口增加简单的校验逻辑：如果接收到的 JSON 无法通过 `BIMCanvas.Core` 的反序列化（即 Schema 不匹配），应直接拒绝，防止脏数据污染内存状态。

4.  **Web 渲染：坐标系与状态**
    *   **共识**：数据层 Y-Up，视图层 Y-Flip。
    *   **补充**：建议 Web 端的 `SvgCanvas.vue` 组件内部封装一个 `WorldToScreen(point: Point2D)` 函数，统一处理 `y_screen = height - y_world`。所有的 SVG 元素（path, rect, circle）都必须通过此函数转换坐标，严禁在 CSS 层使用 `transform: scaleY(-1)`（会导致文字倒置）。

5.  **部署策略：单体发布**
    *   **共识**：Server 托管 Web 静态资源。
    *   **实施细节**：在 `BIMCanvas.Server` 的 `Program.cs` 中配置 `UseStaticFiles` 和 `MapFallbackToFile("index.html")`。开发时保持分离（Vite 代理），发布时只需构建 Web 并复制到 Server 的 `wwwroot`。



---

### [2025-12-10 18:15] Claude: 回应 Codex、Gemini 及用户意见，接受 SSE 预埋方案

**一、关于 SSE 端点（修正立场，接受 Codex/Gemini 的 [Blocker]）**

Codex 和 Gemini 的论据说服了我：

- Codex 指出：若 Phase 1 不把 EventsController 骨架搭好，后续引入 Agent 时需重构 CanvasStateManager 的事件派发
- Gemini 指出：SignalR 在 ASP.NET Core 中开箱即用，"节省的时间"不足以抵消架构不一致带来的技术债务

**我接受 Codex 的轻量版 SSE 方案**：
- Phase 1 实现 `/api/events` SSE 端点骨架
- EventBus 只发布 `canvas_calculated` 事件，Payload = `{ canvasId, version }`
- Agent 暂不订阅，但接口存在即可

**修正后的 Phase 1A 通信协议**：

| 协议 | 端点 | 用途 | Phase 1 实现程度 |
|------|------|------|------------------|
| REST | POST /api/canvas | 接收 Revit 数据 | 完整实现 |
| REST | GET /api/canvas/{id} | 获取画布状态 | 完整实现 |
| SignalR | /hubs/canvas | 推送给 Web | 单向 broadcast |
| SSE | /api/events | 推送给 Agent | **骨架实现**（空事件流） |

**二、关于 pendingChanges（与用户、Codex 达成一致）**

支持用户"为 Phase 2 提前考虑"的观点。采纳 Codex 的具体建议：

```typescript
// canvasStore.ts
interface ElementChange {
  elementId: string;
  deltaBounds?: Polygon2D;
  summary: string;
}

state: () => ({
  document: null as CanvasDocument | null,
  pendingChanges: [] as ElementChange[],  // Phase 1 预留，暂不使用
})
```

**三、关于完成面厚度配置（采纳 Codex 的折中方案）**

Codex 的方案很好地平衡了用户需求：

- 创建 `App_Data/wall-finish.defaults.json` 配置文件
- ZoneCalculator 通过 `IOptions<WallFinishDefaults>` 读取
- 未命中时回退到 Core 层 `FinishRules.cs` 的硬编码枚举

这样满足了用户"需要一个配置文件"的诉求，同时保留了后续切换到知识库的接口。

**四、回应 Gemini 的架构决策收敛建议**

Gemini 提出的 5 点实施标准，我全部同意：

| # | Gemini 建议 | Claude 回应 |
|---|-------------|-------------|
| 1 | SignalR + SSE 双轨 | ✓ 接受，已在上文修正 |
| 2 | ZoneCalculator 只调用 Core | ✓ 完全同意，禁止 `Math.` 自定义逻辑 |
| 3 | 版本号从 Phase 1 生效 | ✓ 同意，POST 入口加反序列化校验 |
| 4 | Web 封装 `WorldToScreen()` 函数 | ✓ 同意，禁止 CSS `scaleY(-1)` |
| 5 | Server 托管 SPA 静态资源 | ✓ 同意，`UseStaticFiles` + `MapFallbackToFile` |

**五、建议进入共识总结阶段**

经过三轮讨论，所有 [Blocker] 已消除，待决策事项已明确。建议用户确认后，我可以填写"共识总结"并开始生成实施计划。



### [2025-12-10 18:15] Codex: 对最新决策点的再回应

- **SSE 端点（坚持 [Blocker]）**：用户希望“进一步讨论”，我仍认为 Phase 1 至少要把 /api/events 的 SSE endpoint 接好。理由：1）Workflows Phase 3 依赖 Server → Agent 的主动推送，若现在缺口，未来上线 Agent 必须重构整条链路；2）实现一个“只广播 canvas_calculated 事件”的 SSE 端点成本极低（等同于 SignalR Hub 的单向 broadcast），但能保证事件流的契约在 Phase 1 就固定下来。建议方案：EventBus.Publish(new CanvasCalculatedEvent(canvasId, version)) → EventsController SSE 输出 text/event-stream。即使 Agent 尚未订阅，接口也要存在。
- **pendingChanges（与用户保持一致，属 [Suggestion]）**：既然用户明确“Phase 2 要提前考虑”，我建议 store 初始化时就包含 pendingChanges: []，同时定义空的 enqueueChange(change: ElementChange) action，Phase 1 里可以直接 no-op。这样保证 SignalR/SSE 接入后只需在 action 中真正 push 数据，而无需改动现有组件。
- **完成面厚度配置（与用户诉求对齐，属 [Suggestion]）**：用户想要“配置文件 + 未来可替换知识库”，我建议采用“配置文件优先、代码兜底”的双轨策略：1）在 appsettings.Development.json 中新增 WallFinishDefaults Section，ZoneCalculator 通过 IOptionsMonitor 读取；2）若未命中则回退到硬编码枚举（保持当前逻辑）；3）未来要接入知识库或项目级配置时，只需替换 IWallFinishProvider 的实现即可。这既满足用户“需要配置文件”又避免 Phase 1 无法启动。



### [2025-12-10 18:18] Gemini: 决策点确认与实施建议

我同意 Codex 和 Claude 的最新修正，并补充以下实施细节以确保落地：

1.  **SSE 实施细节 (针对 Codex 的 [Blocker])**：
    *   **确认**：Phase 1 必须实现 `/api/events` SSE 端点。
    *   **实施**：在 `BIMCanvas.Server` 中创建一个极简的 `EventsController`，并在 `ZoneCalculator` 完成计算后，通过 `EventBus` 发布事件。
    *   **事件定义**：Phase 1 仅需定义一种事件类型：`canvas_ready`（表示 Server 已完成计算，数据就绪）。Payload: `{ "eventType": "canvas_ready", "canvasId": "...", "version": 1 }`���

2.  **Web 状态管理 (针对 pendingChanges)**：
    *   **确认**：Phase 1 的 Pinia Store 必须包含 `pendingChanges` 字段。
    *   **实施**：定义 `ElementChange` 接口，但在 Phase 1 的 UI 组件中暂不绑定任何修改动作。确保 `canvasStore.ts` 的结构在 Phase 1 就完整镜像后端模型，避免 Phase 2 修改 Store 结构导致大量重构。

3.  **完成面配置 (针对用户诉求)**：
    *   **确认**：采用 "配置文件优先" 策略。
    *   **实施**：在 `appsettings.json` 中添加 `WallFinishRules` 节点。`ZoneCalculator` 优先读取配置，读取失败则回退到 `Core` 中的硬编码规则。这符合 .NET 的标准配置模式 (`IOptionsSnapshot`)，既满足了灵活性，又保证了健壮性。

**总结**：我们已经达成全面共识。Phase 1 不再是一个简单的"显示 Demo"，而是一个 **具备完整事件驱动骨架、基于标准坐标系、拥有健壮配置系统的 MVP**。这为 Phase 2 的 Agent 接入奠定了坚实基础。

建议无需再进行更多讨论，直接进入实施阶段。



### [2025-12-10 18:20] User:

我有几个疑问：
**如何在Web端实现"可交互式操作"？我们需要：**

- 交互式的修改指定墙面的完成面类型/厚度
- 交互式的划分/修改 设计区轮廓/设计区标签
- 后期需要交互式的修改AI返回的平面布置方案中的家具模块的位置/选项

**后期如何实现类似git commit的提交修改**

如何实现这一机制，当前可以暂时不实现，但是要预留好功能的架构/按钮位置

**网格划分**

怎么确定网格划分密度？我认为应该根据常见家具尺寸决定

**辅助线**

单一按照固定间距的网格线我认为不能满足复杂布置场景的需求，我们是否需要引入辅助线的概念？如：

- 门两侧端点的垂直辅助线
- 墙内凹角两个方向的延长辅助线
- 由柱子分隔引起的类似墙内凹角的各边的延长辅助线

**如何确定当前开发范围**

我们不可能一口气实现全部功能，在当前开发阶段如何确定最核心的开发功能？开发范围是哪里？如何为后期开发打下兼容性更强的基础？



### [2025-12-10 18:30] Claude: 回应用户的五个关键问题

用户提出的问题非常关键，直接影响 Phase 1 的架构设计。以下是我的分析：

**一、Web 端可交互式操作的实现方案**

用户提到的三类交互需求，按复杂度排序：

| 交互类型 | 复杂度 | 建议实现阶段 | 技术方案 |
|----------|--------|--------------|----------|
| 修改完成面类型/厚度 | 低 | Phase 1.5 | 点击墙面 → 侧边栏选择器 |
| 修改设计区标签 | 低 | Phase 1.5 | 点击 Zone → 标签多选面板 |
| 划分/修改设计区轮廓 | 中 | Phase 2 | 多边形绘制工具（需 Konva/Fabric） |
| 拖拽家具模块 | 高 | Phase 3+ | 拖拽 + 碰撞检测 + 吸附 |

**Phase 1 架构预留建议**：
- SVG 元素添加 `data-id` 属性，支持点击选中
- 预留 `selectedElementId` 状态
- 预留右侧 `PropertyPanel.vue` 组件位置（Phase 1 可为空壳）
- 交互库选型：**建议 Phase 2 引入 Konva.js**（比 Fabric 更轻量，拖拽约束 API 友好）

**二、类似 Git Commit 的提交修改机制**

这正是 Architecture.md §5.2 定义的 **Commit 同步机制**（docs/Architecture.md:746-754）：

```
用户操作 → 本地修改 pendingChanges → 点击"同步"按钮 →
Server 生成 change_set → AI 感知 pendingCommits → AI 确认
```

**Phase 1 预留建议**：
- UI 预留：顶部工具栏添加 **"同步到 AI"按钮**（disabled 状态）
- 状态预留：`pendingChanges: ElementChange[]`（已确认）
- 接口预留：`POST /api/canvas/{id}/commit`（Phase 1 返回 501 Not Implemented）

**三、网格划分密度**

用户观点正确——网格应基于家具尺寸。建议：

| 网格类型 | 间距 | 依据 |
|----------|------|------|
| 主网格 | 600mm | 标准柜体深度（衣柜、橱柜） |
| 辅助网格 | 300mm | 半柜深度、床头柜宽度 |
| 精细网格 | 100mm | 微调用（可选显示） |

**Phase 1 实现建议**：
- 默认 600mm 主网格
- `metadata.gridSize` 已在 Schema 中定义，支持配置
- 网格渲染为 SVG `<pattern>` 背景，不影响交互

**四、辅助线概念**

用户提出的辅助线非常有价值，这是专业 CAD 软件的标配。建议分两层：

**Layer 1: 静态辅助线（Phase 1.5 可实现）**
- 门两侧端点垂直辅助线
- 墙内凹角延长辅助线
- 柱子分隔延长辅助线

**Layer 2: 动态辅助线（Phase 3+ 实现）**
- 拖拽时的对齐吸附线
- 与其他家具的间距标注线

**数据模型扩展建议**：
```typescript
// 新增 CanvasDocument.guides（Phase 1 预留为空数组）
interface GuideLine {
  id: string;
  type: 'door_edge' | 'corner_extend' | 'column_divide' | 'custom';
  line: Line2D;
  sourceElementId?: string;  // 来源元素（如门 ID）
}
```

**Phase 1 预留**：
- `guides: []` 字段占位
- Server 端 `GuideCalculator` 服务接口定义（暂不实现逻辑）

**五、如何确定当前开发范围（核心建议）**

这是最关键的问题。我建议采用 **"洋葱模型"分层开发**：

```
┌─────────────────────────────────────────────────────────────┐
│  Phase 3+: 交互编辑层                                        │
│  拖拽家具、绘制 Zone、动态吸附                                │
├─────────────────────────────────────────────────────────────┤
│  Phase 2: 属性编辑层                                         │
│  点击选中、侧边栏修改属性、标签编辑                           │
├─────────────────────────────────────────────────────────────┤
│  Phase 1.5: 辅助可视化层                                     │
│  静态辅助线、网格、禁区高亮                                   │
├─────────────────────────────────────────────────────────────┤
│  Phase 1: 核心渲染层 ← 当前开发范围                          │
│  outline + zones + exclusionAreas + wallFinishes 渲染        │
│  SignalR/SSE 骨架、数据契约、坐标转换                        │
└─────────────────────────────────────────────────────────────┘
```

**Phase 1 的"最小完整性"标准**：

| 维度 | Phase 1 必须实现 | Phase 1 预留（不实现逻辑） |
|------|------------------|---------------------------|
| **通信** | REST + SignalR + SSE 骨架 | - |
| **渲染** | outline/zones/exclusionAreas | guides、modules |
| **状态** | canvasStore 完整结构 | pendingChanges（空数组） |
| **UI** | SvgCanvas + 基础工具栏 | PropertyPanel（空壳）、CommitButton（disabled） |
| **配置** | wallFinish 配置文件 | 网格密度配置 |

**兼容性基础原则**：
1. **数据模型先行**：所有后续功能的数据结构在 Phase 1 定义（即使值为空）
2. **接口契约先行**：API 端点在 Phase 1 存在（可返回 501）
3. **组件占位先行**：UI 组件位置在 Phase 1 确定（可为空壳）



### 2025-12-10 18:35] Codex: 补充对交互、提交机制、网格/辅助线以及范围划分的建议

- **可交互式操作（与 Claude 的分层方案保持一致，但补充实现细节）**：建议在 Phase 1 中就把 SelectionService 概念化：SvgCanvas 上所有图形元素统一触发 pointerdown → selectionStore.select(elementId)，即便 Phase 1 只高亮、无属性面板，也能保证 Phase 1.5 直接在 PropertyPanel 中读写选中元素。针对未来的划区/拖拽操作，我更倾向在 Phase 2 引入 Konva.js 并封装成 InteractiveCanvas.vue（替换掉 Phase 1 的纯 SVG），避免在 SVG 与 Canvas 之间来回切换。
- **类似 Git 的提交机制（强调契约预留的重要性）**：Architecture.md §5.2 的 pendingCommits 流程已经定义，所以 Phase 1 必须把 ChangeSetService 和 POST /api/canvas/{id}/commit 的接口签名固定下来（即使返回 501）。此外建议 Web 端在工具栏预留两个按钮：Sync to AI 和 Discard local changes（均 disabled），并在 store 中实现 commitChanges(summary) 空方法，这样 Phase 2 只需要填充逻辑，不会破坏 UI 布局。
- **网格划分（认同用户“基于家具尺寸”的观点并给出公式）**：建议在配置文件中允许定义多级网格，例如 gridLevels = [{ name: "coarse", interval: 600 }, { name: "medium", interval: 300 }, { name: "fine", interval: 100 }]。默认渲染 coarse，用户可在 UI 中切换。密度建议与家具 bounding box 关联：gridInterval = roundToNearest50(minDominantFurnitureDepth))，初期可用静态表（客厅 600mm、卧室 300mm）。
- **辅助线（扩展 Claude 的 guides 提案）**：我建议将 guides 数据归在 CanvasDocument.layers.guides，并把生成逻辑放在 Server 端的 GuideCalculator 中：① doors → two perpendicular lines ② concave corners → 延长线 ③ columns → offset lines。Phase 1 先返回空数组，同时在 Web 端实现 GuidesLayer.vue（接受空数组不渲染），这样 Phase 1.5 只需填数据即可。
- **当前开发范围与后续兼容（提醒务必固定数据/接口契约）**：认同 Claude 的“洋葱模型”。从工程角度，我建议将 Phase 1 的验收准则写成 checklist：1) CanvasDocument 完整字段（包括尚未使用的 guides、modules、pendingChanges、gridLevels） 2) REST/SignalR/SSE 三个端点响应格式固定 3) Web 端至少有 SvgCanvas、Toolbar、PropertyPanel(占位)、StatusBar 四个区域布局。只要这些契约在 Phase 1 固化，后续新增交互层就不会打破现有代码结构。



### [2025-12-10 18:48] Gemini: 决策点确认与实施建议

我同意 Codex 和 Claude 的最新修正，并补充以下实施细节以确保落地：

1.  **SSE 实施细节 (针对 Codex 的 [Blocker])**：
    *   **确认**：Phase 1 必须实现 `/api/events` SSE 端点。
    *   **实施**：在 `BIMCanvas.Server` 中创建一个极简的 `EventsController`，并在 `ZoneCalculator` 完成计算后，通过 `EventBus` 发布事件。
    *   **事件定义**：Phase 1 仅需定义一种事件类型：`canvas_ready`（表示 Server 已完成计算，数据就绪）。Payload: `{ "eventType": "canvas_ready", "canvasId": "...", "version": 1 }`

2.  **Web 状态管理 (针对 pendingChanges)**：
    *   **确认**：Phase 1 的 Pinia Store 必须包含 `pendingChanges` 字段。
    *   **实施**：定义 `ElementChange` 接口，但在 Phase 1 的 UI 组件中暂不绑定任何修改动作。确保 `canvasStore.ts` 的结构在 Phase 1 就完整镜像后端模型，避免 Phase 2 修改 Store 结构导致大量重构。

3.  **完成面配置 (针对用户诉求)**：
    *   **确认**：采用 "配置文件优先" 策略。
    *   **实施**：在 `appsettings.json` 中添加 `WallFinishRules` 节点。`ZoneCalculator` 优先读取配置，读取失败则回退到 `Core` 中的硬编码规则。这符合 .NET 的标准配置模式 (`IOptionsSnapshot`)，既满足了灵活性，又保证了健壮性。

4.  **交互与辅助系统 (针对用户新问题)**：
    *   **确认**：支持分层开发与契约预留。
    *   **实施**：
        *   **交互**：Phase 1 仅实现**点击选中**（`data-id` + `SelectionService`），侧边栏预留空壳。
        *   **网格**：Phase 1 渲染 600mm 默认网格，不提供配置 UI。
        *   **辅助线**：在 `CanvasDocument` 预留 `guides` 字段（空数组），前端预留 `GuideLayer` 组件。
        *   **提交**：预留 `Sync` 按钮（Disabled）和 `POST /commit` 接口（返回 501）。

**总��**：我们已经达成全面共识。Phase 1 不再是一个简单的"显示 Demo"，而是一个 **具备完整事件驱动骨架、基于标准坐标系、拥有健壮配置系统、且为交互/辅助线/提交机制预留了完整契约的 MVP**。这为 Phase 2 的 Agent 接入和 Phase 3 的高级交互奠定了坚实基础。

建议无需再进行更多讨论，直接进入实施阶段。



### [2025-12-10 18:55] Claude: 对 Codex/Gemini 补充建议的回应

我同意大部分建议，但有两个技术点需要进一步讨论：

**一、SVG vs Konva.js 切换策略 [需讨论]**

Codex 建议 Phase 2 用 InteractiveCanvas.vue (Konva.js) **替换** Phase 1 的 SvgCanvas.vue。我倾向于 **混合方案** 而非完全替换：

| 方案 | 优点 | 缺点 |
|------|------|------|
| **A: 完全替换** | 代码统一，无混合渲染 | Phase 1 SVG 代码作废；Konva 渲染静态图形性能不如 SVG |
| **B: 混合使用** | SVG 渲染静态层（outline/zones），Konva 渲染交互层（modules/draggable） | 两套渲染逻辑，需协调坐标 |

我倾向 **方案 B** 的理由：
1. Phase 1 的 SVG 渲染代码可复用，不作废
2. 静态元素（墙体、门窗、Zone 边界）用 SVG 更轻量
3. 只有 modules 和 guides 需要拖拽，用 Konva Stage 叠加即可
4. 坐标协调问题可通过共享 `WorldToScreen()` 函数解决

**建议实现**：
```
┌─────────────────────────────────────┐
│  SvgCanvas.vue (静态层, z-index: 1) │  ← Phase 1
│  - outline, zones, wallFinishes     │
├─────────────────────────────────────┤
│  KonvaCanvas.vue (交互层, z-index: 2)│  ← Phase 2+
│  - modules (draggable)              │
│  - guides (snappable)               │
└─────────────────────────────────────┘
```

**二、guides 数据结构位置 [需确认]**

Codex 建议 `CanvasDocument.layers.guides`，我之前建议顶级 `guides[]`。

分析现有 Schema 结构：
- 顶级字段：`outline`, `rooms`, `zones`, `wallFinishes`, `modules`
- 没有 `layers` 嵌套结构

**我倾向保持顶级 `guides[]`**，理由：
1. 与现有 Schema 结构一致（扁平化）
2. 避免引入新的嵌套层级
3. guides 与 zones/modules 同级，逻辑清晰

但如果 Codex 坚持 `layers` 结构有更深远的考虑（如未来支持图层显隐控制），我可以接受。

**三、完全同意的建议**

| 建议来源 | 建议内容 | 我的态度 |
|----------|----------|----------|
| Codex | SelectionService + selectionStore.select(elementId) | ✓ 完全同意 |
| Codex | Phase 1 预留 Sync + Discard 两个按钮 | ✓ 完全同意 |
| Codex | gridLevels 多级网格配置 | ✓ 完全同意 |
| Codex | Phase 1 验收 checklist | ✓ 完全同意，建议写入共识总结 |
| Gemini | canvas_ready 事件定义 | ✓ 完全同意 |
| Gemini | 点击选中 + 600mm 默认网格 | ✓ 完全同意 |

**四、Phase 1 验收 Checklist（采纳 Codex 建议，补充细节）**

```
□ 数据契约
  □ CanvasDocument 完整字段：outline, rooms, zones, wallFinishes, modules, guides, metadata
  □ ElementChange 接口定义
  □ canvas_ready 事件 Payload 定义

□ 后端接口
  □ POST /api/canvas - 完整实现 + 输入校验
  □ GET /api/canvas/{id} - 完整实现
  □ POST /api/canvas/{id}/commit - 返回 501
  □ /hubs/canvas SignalR Hub - 单向 broadcast
  □ /api/events SSE - canvas_ready 事件

□ 前端布局
  □ SvgCanvas.vue - 渲染 outline/zones/wallFinishes
  □ Toolbar.vue - Sync(disabled) + Discard(disabled) + 网格切换
  □ PropertyPanel.vue - 占位空壳
  □ StatusBar.vue - 连接状态 + 版本号

□ 状态管理
  □ canvasStore.ts - document + pendingChanges + selectedElementId
  □ SelectionService - select(elementId) 基础实现

□ 配置文件
  □ appsettings.json - WallFinishRules 节点
  □ gridLevels 配置（可选）
```



### [2025-12-10 19:05] Codex: 回应 Claude 最新问题并补充实施要点

- **SVG vs Konva（赞同“混合渲染”并补充约束）**：我接受方案 B 的叠层设计：Phase 1 的 SvgCanvas 继续负责 outline/zones/wallFinishes，Phase 2 引入 KonvaCanvas 覆盖交互层。但要注意 2 点：1）两层必须共用 WorldToScreen()/ScreenToWorld()，避免双坐标系；2）Konva 层需要禁用对底层 SVG 的 pointer 事件（pointer-events: none + Konva hit test）以免事件穿透失控。若未来模块数量很大，可考虑在 Konva 层按 zone 分组以减少重绘。
- **guides 结构（坚持顶层字段并给出扩展方式）**：赞成保持 canvas.guides: GuideLine[]，原因与 Claude 一致：Schema 当前是扁平结构，而 guides 与 zones/modules 的逻辑层级相同。如果未来确实需要图层控制，可在 metadata.layers 中记录显隐配置，而无须改动主文档结构。例如：metadata.layers = { structure: true, guides: true, modules: true }。这样既能满足显隐需求，也不会影响现有后端/前端解析。
- **Phase 1 验收清单（附补充项）**：Claude 的 checklist 很清晰，我建议再补两条：1）SignalR/SSE 的 Demo Client（简单 console app 或雷同脚本）用于验收事件链；2）Konva 层尚未实现时，也要在代码中有 InteractiveCanvas.vue 占位组件（返回 null），确保 Phase 2 直接补逻辑即可。



### [2025-12-10 19:10] Gemini: 最终架构确认与实施批准

经过四轮深入讨论，架构分歧已全部消解，实施路径清晰。我做最后的确认与补充：

1.  **混合渲染架构 (Hybrid Rendering)**：
    *   **确认**：采用 Claude 提出的方案 B，Phase 1 开发 SvgCanvas.vue（负责静态层），Phase 2 叠加 KonvaCanvas.vue（负责交互层）。
    *   **补充**：建议在 `services/CoordinateService.ts` 中集中管理 `WorldToScreen` 逻辑，供 SVG 和 Konva 两层共享，确保坐标系绝对对齐。

2.  **Guide 数据结构**：
    *   **确认**：采用顶级 `guides[]` 字段。图层显隐控制如需实现，应放在 `metadata.viewSettings` 中。

3.  **验收标准 (Checklist)**：
    *   **确认**：完全采纳 Claude 整理、Codex 补充的清单。这是 Phase 1 完工的唯一标准。

**最终共识总结**：

*   **架构**：Event-Driven Server (SignalR/SSE) + Hybrid Web Client (SVG/Konva预留)
*   **数据**：Y-Up 笛卡尔坐标系 + 扁平化 JSON Schema + 强版本控制
*   **配置**：Configuration-First 策略 (appsettings.json > Hardcode)
*   **交互**：分层实施，Phase 1 聚焦核心渲染与基础选中，预留完整交互/提交契约

至此，架构评审结束。建议用户直接依据此共识生成具体代码实施计划。

---

## 4. 共识总结

> **评审结论**：经过四轮深入讨论，三位专家就 BIMCanvas.Server + BIMCanvas.Web Phase 1 实施方案达成全面共识。以下为最终决策。

### 4.1 架构决策

| 决策项 | 最终方案 | 决策依据 |
|--------|----------|----------|
| **部署模式** | Server 托管 SPA（UseStaticFiles + MapFallbackToFile） | 简化部署，避免 CORS |
| **通信协议** | REST + SignalR + SSE 三轨并行 | REST 主数据流，SignalR 实时推送，SSE 预留 Agent 事件 |
| **渲染架构** | 混合方案：SVG 静态层 + Konva 交互层（Phase 2+） | Phase 1 代码可复用，静态元素 SVG 更轻量 |
| **坐标系统** | Y-Up 笛卡尔坐标，Web 层 CoordinateService 统一转换 | 禁止 CSS scaleY(-1)，WorldToScreen() 共享 |
| **状态管理** | Pinia Store 完整镜像 CanvasDocument 结构 | 避免 Phase 2 重构 |

### 4.2 数据契约

**CanvasDocument 完整字段（Phase 1 必须定义）**：

```typescript
interface CanvasDocument {
  id: string;
  version: number;
  coordinateSystem: 'y-up';
  metadata: {
    gridSize: number;           // 默认 600mm
    viewSettings?: {
      layers: { structure: boolean; guides: boolean; modules: boolean };
    };
  };
  outline: {
    boundaries: Polygon2D[];
    openings: Opening[];
  };
  rooms: Room[];
  zones: Zone[];
  wallFinishes: WallFinish[];
  modules: Module[];            // Phase 1 预留空数组
  guides: GuideLine[];          // Phase 1 预留空数组
}
```

**ElementChange 接口（提交机制预留）**：

```typescript
interface ElementChange {
  id: string;
  elementType: 'zone' | 'wallFinish' | 'module';
  elementId: string;
  changeType: 'create' | 'update' | 'delete';
  before?: Partial<Zone | WallFinish | Module>;
  after?: Partial<Zone | WallFinish | Module>;
  timestamp: number;
}
```

**canvas_ready 事件 Payload**：

```json
{ "eventType": "canvas_ready", "canvasId": "...", "version": 1 }
```

### 4.3 API 端点

| 端点 | 方法 | Phase 1 实现程度 | 说明 |
|------|------|------------------|------|
| `/api/canvas` | POST | 完整实现 | 输入校验（coordinateSystem 白名单、多边形合法性） |
| `/api/canvas/{id}` | GET | 完整实现 | 返回完整 CanvasDocument |
| `/api/canvas/{id}/commit` | POST | 返回 501 | 预留提交接口 |
| `/hubs/canvas` | SignalR | 单向 broadcast | BroadcastDocument(canvasId, document) |
| `/api/events` | SSE | 骨架实现 | EventBus → canvas_ready 事件 |

### 4.4 前端组件布局

```
┌─────────────────────────────────────────────────────────────┐
│  Toolbar.vue                                                │
│  [Sync to AI (disabled)] [Discard (disabled)] [Grid: 600mm] │
├───────────────────────────────────────────┬─────────────────┤
│                                           │                 │
│  CanvasContainer.vue                      │ PropertyPanel   │
│  ├─ SvgCanvas.vue (静态层, z:1)           │ .vue            │
│  │  - outline, zones, wallFinishes        │ (占位空壳)      │
│  │  - 600mm 网格背景                      │                 │
│  └─ KonvaCanvas.vue (交互层, z:2, Phase2) │                 │
│     - modules (draggable)                 │                 │
│     - guides (snappable)                  │                 │
│                                           │                 │
├───────────────────────────────────────────┴─────────────────┤
│  StatusBar.vue                                              │
│  [连接状态: Connected] [版本: v1] [选中: zone-001]          │
└─────────────────────────────────────────────────────────────┘
```

### 4.5 配置策略

**appsettings.json 结构**：

```json
{
  "WallFinishRules": {
    "RoomDefaults": {
      "bathroom": { "type": "tile", "thickness": 50 },
      "kitchen": { "type": "tile", "thickness": 50 },
      "bedroom": { "type": "latex", "thickness": 5 }
    },
    "ZoneOverrides": {
      "tv_media": { "type": "panel", "thickness": 80 }
    }
  },
  "GridLevels": [
    { "name": "coarse", "interval": 600 },
    { "name": "medium", "interval": 300 },
    { "name": "fine", "interval": 100 }
  ]
}
```

**读取优先级**：IOptionsSnapshot<WallFinishRules> → Core 硬编码兜底

### 4.6 Phase 1 验收 Checklist

```
□ 数据契约
  □ CanvasDocument 完整字段定义（含 guides、modules 空数组）
  □ ElementChange 接口定义
  □ canvas_ready 事件 Payload 定义

□ 后端接口
  □ POST /api/canvas - 完整实现 + 输入校验
  □ GET /api/canvas/{id} - 完整实现
  □ POST /api/canvas/{id}/commit - 返回 501
  □ /hubs/canvas SignalR Hub - 单向 broadcast
  □ /api/events SSE - canvas_ready 事件
  □ EventBus 服务实现

□ 前端布局
  □ SvgCanvas.vue - 渲染 outline/zones/wallFinishes + 网格
  □ KonvaCanvas.vue - 占位组件（返回 null）
  □ Toolbar.vue - Sync(disabled) + Discard(disabled) + 网格切换
  □ PropertyPanel.vue - 占位空壳
  □ StatusBar.vue - 连接状态 + 版本号 + 选中元素

□ 状态管理
  □ canvasStore.ts - document + pendingChanges + selectedElementId
  □ CoordinateService.ts - WorldToScreen()/ScreenToWorld() 共享
  □ SelectionService - select(elementId) 基础实现

□ 配置文件
  □ appsettings.json - WallFinishRules + GridLevels 节点

□ 验收测试
  □ SignalR/SSE Demo Client（验证事件链）
  □ Revit 导出 JSON → Server → Web 显示全流程
```

### 4.7 后续 Phase 路线图

| Phase | 核心目标 | 关键交付物 |
|-------|----------|------------|
| **1** | 核心渲染 + 事件骨架 | SvgCanvas、REST/SignalR/SSE、数据契约 |
| **1.5** | 辅助可视化 | GuideCalculator、GuidesLayer、网格密度切换 |
| **2** | 属性编辑 | PropertyPanel 实现、点击修改 wallFinish/zone tags |
| **2.5** | 提交机制 | ChangeSetService、Sync/Discard 功能、commit API |
| **3** | 交互编辑 | KonvaCanvas、模块拖拽、动态吸附线 |
| **3+** | Agent 集成 | PlacementAgent SSE 订阅、AI 布置结果渲染 |

---

**评审完成时间**：2025-12-10 19:15

**参与专家**：Claude、Codex、Gemini

**下一步行动**：依据本共识生成 `plans/ServerWeb_Implementation_Plan.md` 实施计划

