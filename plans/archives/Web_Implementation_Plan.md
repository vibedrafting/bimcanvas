# BIMCanvas.Web 实施计划 (Calm Tech Edition)

> **版本**：v3.0
> **更新日期**：2025-12-15
> **状态**：Phase 1 重构规划
> **设计哲学**：Calm Tech (克制科技) + Professional Trust (专业可信)
> **变更摘要**：废弃 v2.0 的“赛博朋克”风格，转向“静谧生命感”的专业工作台；确立双渲染模式与 AI 协作闭环。

---

## 一、项目概述

### 1.1 核心定位

**BIMCanvas.Web 是 AI 驱动的专业空间方案探索器 (AI-Driven Space Proposal Explorer)**

它不仅仅是一个绘图工具，而是设计师与 AI 协作的指挥中心。它通过“意图导向”的交互模式，帮助设计师快速探索、验证和决策空间布局方案。

**核心价值**：
- **Calm Tech**：视觉克制，信息分层，避免过度干扰。
- **Professional Trust**：AI 操作可解释、可预览、可回滚。
- **Data Truth**：JSON 是唯一真理，视觉服务于数据。

### 1.2 核心体验 (The "North Star")

MVP 阶段打造 **"智能且克制的专业工作台"**，核心循环为：
`Select (Gallery/Chat) -> Preview (Ghost + Lint) -> Commit (Timeline)`

1.  **Canvas First**：以画布为中心，方案画廊作为侧边辅助。
2.  **Dual Render Mode (双渲染模式)**：
    -   **Human View (默认)**：精美、克制、高级感。微弱 AO 光影，隐藏复杂辅助线。
    -   **AI Vision View (后台/调试)**：高对比度，强制显示网格、语义参考线 (Semantic Guidelines)、对象 ID。
3.  **Semantic Snapping**：吸附墙中线、窗边线、对齐线，提供专业手感。
4.  **Constraint Lint**：实时显示微弱光点提示冲突（如通道过窄）。

---

## 二、技术架构

### 2.1 系统架构

```
BIMCanvas.Web (Vue 3 + TS)
├── ThreeSceneService (渲染核心)
│   ├── Scene (Calm Life Style)
│   ├── OrthographicCamera (Top-down)
│   ├── WebGLRenderer (ShadowMap Enabled)
│   ├── PostProcessing (Subtle AO, No Bloom)
│   └── LayerManager (Human/AI View Switch)
│
├── SceneBuilder (构建器)
│   ├── WallBuilder (Solid with Subtle Shadow)
│   ├── ZoneBuilder (Minimalist Plane)
│   ├── ModuleBuilder (3D Models + Ghost Material)
│   └── GuideLineBuilder (Semantic Lines for AI View)
│
├── InteractionService (交互)
│   ├── Raycaster (Smart Picking)
│   ├── DragControls (Inertia & Magnetic Snapping)
│   └── GhostManager (Patch Preview)
│
└── StateManager (Pinia)
    ├── CanvasStore (Current JSON)
    ├── TimelineStore (History/Undo)
    └── ProposalStore (Gallery Candidates)
```

### 2.2 关键技术点

-   **渲染风格**：
    -   **背景**：接近纯黑但带微蓝 (`#0a0a0f`)，非深空黑。
    -   **材质**：哑光 (Matte) 质感，拒绝高光反射。
    -   **光影**：启用 `AmbientOcclusion` (SAO/SSAO)，强度 10%，仅增强体积感。
    -   **动效**：流体插值 (Lerp)，家具移动带有物理惯性。
-   **双视图实现**：
    -   利用 Three.js 的 `Layers` 机制。
    -   Layer 0: 通用物体。
    -   Layer 1: Human View 专属 (美化装饰)。
    -   Layer 2: AI View 专属 (辅助线、ID 标签)。
    -   截图时切换 Camera 的 Layer Mask 进行分别渲染。

---

## 三、功能规格 (MVP)

### 3.1 界面与视觉

-   **布局**：
    -   **Main Canvas**：全屏，无边框。
    -   **Side Panel (Left)**：方案画廊 (Gallery)，默认收起或窄条显示。
    -   **Property Panel (Right)**：选中物体属性，支持精确数值微调。
    -   **Command Palette (Center/Top)**：自然语言输入入口 (Cmd+K)。
    -   **Timeline (Bottom)**：版本节点与回滚。
-   **色彩**：
    -   主色：沉稳蓝 (`#3b82f6`)，仅用于选中/关键操作。
    -   警告色：柔和橙/红，仅在 Lint 触发时出现微弱光点。
    -   中性色：90% 界面为深灰/浅灰。

### 3.2 交互逻辑

-   **Ghost Patch Preview**：
    -   当 AI 生成方案或用户从画廊选择时，不直接覆盖当前方案。
    -   以 **Ghost (半透明 + 虚线框)** 形式叠加在当前画布上。
    -   提供 "Apply" (√) 和 "Discard" (×) 浮动按钮。
-   **Semantic Snapping**：
    -   拖拽家具时，自动检测并吸附：
        -   Grid (基础网格)
        -   Wall Center/Face (墙体)
        -   Window/Door Edges (门窗)
        -   Alignment Lines (相邻家具对齐)
    -   吸附时提供轻微视觉反馈 (高亮参考线)。

### 3.3 AI 协作特性

-   **Semantic Guidelines (语义参考线)**：
    -   数据结构化：在 `CanvasDocument` 中不持久化，但在运行时计算并缓存。
    -   类型：`WallExtension`, `SymmetryAxis`, `DivisionLine`.
    -   渲染：仅在 AI View 或用户按住特定热键 (如 Alt) 时显示。
-   **Constraint Lint**：
    -   前端实时计算简单约束 (如重叠、出界)。
    -   视觉表现：在问题区域显示微弱的脉冲红点，鼠标悬停显示原因。

---

## 四、实施路线图

### 4.1 Phase 1: 基础重构 (Calm Foundation)
*目标：建立新的视觉基调与双视图架构*

- [ ] **样式重置**：移除 Cyberpunk 特效 (Bloom, Neon)，调整背景色与材质。
- [ ] **双视图架构**：实现 `LayerManager`，区分 Human/AI 视图渲染逻辑。
- [ ] **语义参考线**：实现基础的 `GuideLineBuilder`，在 AI 视图中绘制墙体延长线与中线。
- [ ] **光影优化**：配置 SAO/SSAO，调整至"隐约可见"的 10% 强度。

### 4.2 Phase 2: 核心交互 (Professional Feel)
*目标：实现专业级的手感与 AI 协作闭环*

- [ ] **Ghost 系统**：实现 `GhostManager`，支持加载 Patch 并以半透明材质渲染。
- [ ] **语义吸附**：重构 `DragControls`，集成语义参考线的吸附逻辑。
- [ ] **时间线**：实现 `TimelineStore`，支持 JSON 状态的快照与回滚。
- [ ] **Lint 基础**：实现简单的重叠检测与视觉提示。

### 4.3 Phase 3: 完整体验 (Full Loop)
*目标：接入 AI 与画廊*

- [ ] **Side Gallery**：开发侧边栏组件，展示方案缩略图。
- [ ] **Command Palette**：集成自然语言输入 UI (Mock 阶段)。
- [ ] **截图服务**：实现基于 AI View 的无头渲染截图功能 (供 Agent 使用)。

---

## 五、变更记录

| 日期 | 版本 | 变更内容 |
|------|------|----------|
| 2025-12-15 | v3.0 | **完全重构**：确立 Calm Tech 风格，引入双视图、Ghost Patch、语义吸附；废弃 Cyberpunk 风格。 |
| 2025-12-11 | v2.0 | (已废弃) Cyberpunk Holographic 风格 |
