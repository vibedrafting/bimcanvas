# BIMCanvas.Web 改造计划 (v3.0 Refactoring)

> **目标**：将当前的 Prototype 升级为符合 "Calm Tech" 设计哲学的专业工作台。
> **优先级**：Architecture > Dual View > Interaction > Features

---

## Phase 1: 架构重构 (Architecture Refactoring)
**预计耗时**：1-2 天
**目标**：建立 Vue 组件化架构，解耦 UI 与 3D 逻辑。

### 1.1 组件拆分
- [ ] **创建 `src/layouts/MainLayout.vue`**：作为应用主框架。
- [ ] **创建 `src/components/Canvas/ThreeCanvas.vue`**：封装 `ThreeSceneService` 的生命周期（Mount/Unmount/Resize）。
- [ ] **创建 `src/components/UI/CanvasToolbar.vue`**：顶部工具栏（加载按钮、视图切换）。
- [ ] **创建 `src/components/UI/SideGallery.vue`**：左侧方案画廊（暂为空壳）。
- [ ] **重构 `App.vue`**：仅保留 `MainLayout` 和 `RouterView`（如需要）。

### 1.2 服务层重构
- [ ] **创建 `src/services/three/LayerManager.ts`**：管理 Three.js 图层（Human View vs AI View）。
- [ ] **创建 `src/services/interaction/InteractionService.ts`**：接管鼠标事件（Raycaster, Dragging）。
- [ ] **更新 `ThreeSceneService.ts`**：移除硬编码逻辑，注入 `LayerManager` 和 `InteractionService`。

---

## Phase 2: 双视图核心 (Dual Render Core)
**预计耗时**：2 天
**目标**：实现 Human View 与 AI Vision View 的切换与渲染差异。

### 2.1 图层管理
- [ ] **定义图层常量**：
    - `LAYER_DEFAULT (0)`: 通用物体
    - `LAYER_HUMAN (1)`: 装饰、AO 光影
    - `LAYER_AI (2)`: 网格、辅助线、ID 标签
- [ ] **实现 `LayerManager.toggleView(mode: 'human' | 'ai')`**：切换 Camera 的 `layers.enable/disable`。

### 2.2 渲染差异化
- [ ] **SceneBuilder 升级**：
    - 为所有 Mesh 设置 `layers.enable(LAYER_HUMAN)`。
    - 创建 AI 专用的线框/辅助线，设置 `layers.set(LAYER_AI)`。
- [ ] **实现 `GridBuilder`**：仅在 AI 图层渲染高对比度网格。
- [ ] **实现 `SemanticLineBuilder`**：仅在 AI 图层渲染墙中线、对齐线。

---

## Phase 3: 交互与手感 (Interaction & Feel)
**预计耗时**：3 天
**目标**：实现“有物理质感”的拖拽与吸附，以及基础编辑操作。

### 3.1 基础交互
- [ ] **实现 `DragControls`**：支持鼠标左键拖拽 Module。
- [ ] **实现 `SelectionManager`**：点击选中，高亮显示（蓝色边框）。
- [ ] **实现视图操作 (View Operations)**：
    - 鼠标中键滚轮缩放 (Zoom)。
    - 鼠标中键按住平移 (Pan)。
    - 鼠标双击重置视图 (Reset)。

### 3.2 编辑操作 (Edit Operations)
- [ ] **实现基础编辑指令**：
    - **移动 (Move)**：支持长按左键直接拖动，或点击命令按钮后操作。
    - **旋转 (Rotate)**：选中物体后通过快捷键 (R) 或按钮旋转。
    - **快捷键支持**：实现 `ShortcutManager`，绑定常用的 CAD 快捷键 (M: Move, R: Rotate, Space: Confirm)。

### 3.3 语义吸附 (Semantic Snapping)
- [ ] **实现 `SnappingEngine`**：
    - **正交吸附**：移动时按住 Shift 锁定 XY 轴。
    - **边缘吸附**：吸附附近物品边缘、墙体边缘。
    - **辅助线**：吸附时显示动态辅助线。

### 3.4 Ghost 系统
- [ ] **实现 `GhostManager`**：
    - 支持加载 Patch 数据。
    - 创建半透明材质 (`opacity: 0.5`, `transparent: true`)。
    - 渲染虚线边框。

---

## Phase 4: 完整闭环 (Full Loop)
**预计耗时**：2 天
**目标**：接入数据流与 UI。

- [ ] **集成 Pinia Store**：连接 `CanvasToolbar` 与 `SideGallery` 到 Store。
- [ ] **实现 Timeline**：记录每次 Commit 的 JSON 快照，支持 Undo/Redo。
- [ ] **实现属性面板 (Property Panel)**：
    - 创建 `src/components/UI/PropertyPanel.vue`。
    - 选中构件时显示详细信息（尺寸、ID、材质）。
- [ ] **Constraint Lint**：实现基础的碰撞检测，并在 AI 图层显示红色警告框。

---

## Phase 5: 高级特性 (Advanced Features)
**预计耗时**：待定
**目标**：进一步提升专业度与智能化。

- [ ] **高级网格吸附**：
    - 沿着边缘延长线移动。
    - 对称吸附。
- [ ] **命令面板 (Command Palette)**：集成自然语言输入。

---

## 执行顺序建议

1.  **立即执行 Phase 1**：这是所有后续工作的基础。
2.  **紧接执行 Phase 2**：双视图是 AI 协作的核心前提。
3.  **Phase 3 & 4**：可根据业务紧迫度并行或顺序执行。
