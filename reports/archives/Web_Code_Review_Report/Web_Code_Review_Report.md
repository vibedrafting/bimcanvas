# BIMCanvas.Web 代码审查报告

> **审查日期**：2025-12-15
> **审查对象**：`BIMCanvas.Web` 项目
> **审查基准**：
> - `reviews/Web_Design_Philosophy_Review.md` (v2.0)
> - `plans/Web_Implementation_Plan.md` (v3.0)
> - `docs/Architecture.md` (v2.8)

---

## 1. 总体评价

**当前状态**：`Prototype` (原型阶段)
**符合度评分**：30%

当前代码库实现了一个基础的 Three.js 2.5D 查看器，能够加载并渲染墙、柱、门窗和家具模块。然而，它距离“AI 驱动的专业空间方案探索器”这一目标仍有巨大差距。文档中定义的核心交互特性（双视图、语义吸附、Ghost 预览、方案画廊）均未实现。架构上也较为初级，未遵循组件化原则。

---

## 2. 详细审查发现

### 2.1 架构与工程结构 (Architecture)

| 检查项 | 现状 | 差距 | 优先级 |
|--------|------|------|--------|
| **组件化架构** | `src/components` 目录为空，UI 直接写在 `App.vue` 中。 | 严重违反 Vue 组件化最佳实践。缺失 `CanvasToolbar`, `SidePanel`, `PropertyPanel` 等组件。 | **Critical** |
| **渲染技术栈** | 使用 Three.js + WebGL。 | 符合 `Web_Implementation_Plan.md`，但与 `Architecture.md` (SVG) 描述冲突（需更新架构文档）。 | Low (Doc fix) |
| **服务层分层** | 仅有 `ThreeSceneService` 和 `SceneBuilder`。 | 缺失 `InteractionService` (交互), `LayerManager` (图层), `GhostManager` (预览)。 | **Critical** |
| **状态管理** | 使用 Pinia (`canvasStore`)。 | 符合要求，但目前仅用于存储 Document，缺乏 Timeline 和 Proposal 状态。 | High |

### 2.2 视觉与设计哲学 (Design Philosophy)

| 检查项 | 现状 | 差距 | 优先级 |
|--------|------|------|--------|
| **Calm Tech 风格** | 基础的深色背景 (`#0a0a0f`) 和 Glassmorphism UI。 | 材质和光影尚处于默认状态，未达到“微缩模型”的精致感。 | Medium |
| **双渲染模式** | **未实现**。所有物体都在同一图层渲染。 | 无法区分 Human View 和 AI Vision View，AI 无法获取语义辅助线信息。 | **Critical** |
| **光影设置** | 包含环境光、平行光和半球光。 | 尚未精细调整 AO (Ambient Occlusion) 参数以达到 10% 微弱光影的要求。 | Medium |

### 2.3 核心功能 (Functional Requirements)

| 检查项 | 现状 | 差距 | 优先级 |
|--------|------|------|--------|
| **语义吸附** | **未实现**。场景是静态的，无法拖拽。 | 缺失 `DragControls` 和吸附计算逻辑，这是“专业手感”的核心。 | **Critical** |
| **Ghost Patch** | **未实现**。 | 无法预览 AI 方案，无法进行 Intent -> Preview -> Commit 闭环。 | **Critical** |
| **方案画廊** | **未实现**。仅有加载 Demo 数据的按钮。 | 缺失侧边栏画廊 UI 和数据流。 | High |
| **语义参考线** | **未实现**。 | AI 视图缺失关键的墙中线、对齐线等辅助信息。 | High |
| **Constraint Lint** | **未实现**。 | 无冲突检测和视觉提示。 | Medium |

---

## 3. 风险与建议

### 3.1 关键风险

1.  **架构债**：如果继续在 `App.vue` 中堆砌逻辑，后续维护将极其困难。必须立即进行组件化重构。
2.  **交互缺失**：目前仅是 Viewer，缺乏 Editor 能力。交互逻辑（拖拽、吸附）是开发难点，需尽快启动。
3.  **文档冲突**：`Architecture.md` 中的 SVG 描述会误导后续开发（如 Agent 团队可能误以为能直接读取 SVG 字符串）。

### 3.2 改进建议

1.  **立即重构 UI**：将 `App.vue` 拆分为 `MainLayout`, `CanvasView`, `SideGallery`, `Toolbar` 等组件。
2.  **实现双视图底层**：优先打通 `LayerManager`，确保能分别渲染 Human 和 AI 视图，这是 AI 协作的基础。
3.  **更新架构文档**：修正 `Architecture.md` 中关于 Web 渲染层的描述，明确使用 Three.js。

---

## 4. 结论

`BIMCanvas.Web` 目前处于 **Pre-Alpha** 阶段。虽然基础渲染管线已跑通，但要达到 v3.0 计划的要求，需要进行一次**全面的架构升级和功能补全**。

建议按照 `plans/Web_Refactoring_Plan.md` (待生成) 严格执行改造。
