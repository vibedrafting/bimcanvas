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

### 专家：Claude（系统架构师）

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

---

## 3. 深入讨论

<!-- 请在此分隔线下方追加新的讨论内容 -->

---

## 4. 共识总结

<!-- 讨论结束并且得到用户明确要求后填写 -->

