# Web 端加载动画流程详解

> **文档版本**: v1.0  
> **最后更新**: 2026-01-11  
> **适用项目**: BIMCanvas.Web

## 1. 概述 (Overview)

BIMCanvas Web 端的启动过程被设计为一个**连贯的电影式体验（Cinematic Experience）**。它不仅仅是一个加载进度条，而是通过“从无序到有序”、“从蓝图到实体”的视觉隐喻，引导用户平滑地进入沉浸式的工作环境。

整个流程分为四个精心编排的阶段：
1.  **蓝图构建 (Splash)**: 建立坐标系与网格。
2.  **UI 展开 (Chrome)**: 界面框架入场。
3.  **场景搭建 (Scene Build)**: 3D 建筑生长。
4.  **就绪 (Ready)**: 交互元素激活。

---

## 2. 核心编排 (Orchestrator)

整个加载流程的“总导演”是根组件 `App.vue`。它负责协调数据加载、最小展示时间和各阶段状态的切换。

*   **文件位置**: [`src/App.vue`](../BIMCanvas.Web/src/App.vue)
*   **关键状态**: `loadingStage` (Ref<number>)
    *   `0`: Loader (启动画面)
    *   `1`: Grid (网格就绪)
    *   `3`: Island (灵动岛/工具层)
    *   `4`: Chrome (主UI框架)
    *   `5`: Scene (场景构建)

### 核心逻辑
1.  **强制等待**: 使用 `Promise.race` 确保启动画面至少展示 **2.5秒**，避免加载过快导致闪烁。
2.  **视口计算**: 数据加载完成后，立即调用 `ViewCalculator` 计算最佳视口参数（Spacing, Offset），并将这些参数传递给启动画面，实现粒子从“混乱”到“对齐网格”的平滑过渡。
3.  **时序控制**: 通过 `setTimeout` 逐步改变 `loadingStage` 的值，触发不同层级 UI 的 CSS 过渡。

---

## 3. 阶段详解 (Stage Breakdown)

### Phase 1: 蓝图构建 (Splash Screen)

用户首先看到的画面，基于 HTML5 Canvas 的粒子网格动画。

*   **视觉效果**:
    *   **初始**: 粒子处于混沌或扫描状态，屏幕中央显示 "ESTABLISHING GRID"。
    *   **锁定**: 当后端数据返回并计算出视口后，粒子平滑移动并锁定到最终的网格交叉点 (`isOrdered = true`)。
    *   **结束**: 进度条填满，网格线淡入，随后整个遮罩层淡出。
*   **组件**: [`src/components/UI/BlueprintLoader.vue`](../BIMCanvas.Web/src/components/UI/BlueprintLoader.vue)
*   **关键代码**:
    *   `Particle` 类: 管理每个网格点的运动（行/列分离运动逻辑）。
    *   `animate()`: 主渲染循环，使用 `easeInOutCubic` 缓动函数处理位置插值。
    *   `drawConnections()`: 动态绘制透明度变化的网格线。

### Phase 2: UI 展开 (UI Expansion)

当蓝图网格建立完毕，加载层淡出，应用的主界面 UI 元素按层级依次入场。

*   **视觉效果**:
    *   **Stage 3**: 顶部的灵动岛 (`DynamicIsland`) 和右下角的图层管理器 (`FloatingLayerManager`) 首先浮现并滑入位置。
    *   **Stage 4**: 顶部导航栏 (`AppHeader`, `RibbonToolbar`) 和左侧属性面板 (`PropertyPanel`) 显现。
*   **组件**: [`src/layouts/MainLayout.vue`](../BIMCanvas.Web/src/layouts/MainLayout.vue)
*   **关键代码**:
    *   CSS 类 `.visible` 配合 `loadingStage` prop 控制 `opacity` 和 `transform`。
    *   动效参数: `transition: ... 0.6s cubic-bezier(0.34, 1.56, 0.64, 1)` (带轻微回弹)。

### Phase 3: 场景搭建 (Cinematic Scene Build)

最核心的“建筑生长”过程。3D 场景中的物体不是一次性出现，而是按建筑逻辑逐步生成。

*   **视觉效果**:
    1.  **结构层**: 柱子 (Columns) 批量升起。
    2.  **围护层**: 墙体 (Walls) 批量升起。
    3.  **开口**: 门窗 (Openings) 出现。
    4.  **内容层**: 家具模块 (Modules) 一个接一个地快速弹出 ("Pop" effect)。
*   **触发机制**: `App.vue` 派发 `bimcanvas:play-build-sequence` 全局事件。
*   **服务**: 
    *   监听: [`src/services/three/ThreeSceneService.ts`](../BIMCanvas.Web/src/services/three/ThreeSceneService.ts)
    *   实现: [`src/services/builders/SceneBuilder.ts`](../BIMCanvas.Web/src/services/builders/SceneBuilder.ts)
*   **关键代码**:
    ```typescript
    // SceneBuilder.ts -> buildProgressively()
    // 家具逐个弹出动画
    if (activeScheme?.modules) {
        for (const mod of activeScheme.modules) {
            this.createModuleMesh(mod);
            await delay(30); // 30ms 间隔，形成连贯的生长感
        }
    }
    ```

### Phase 4: 就绪与收尾 (Final Polish)

当场景构建完成，最后的交互元素就位，系统完全可交互。

*   **视觉效果**:
    *   右侧 **AI 指挥中心** (`AICommandCenter`) 从屏幕边缘滑入。
    *   顶部 **灵动岛** 执行一次“脉冲”动画 (`hint-pulse`)，提示用户系统已就绪。
*   **组件**: [`src/layouts/MainLayout.vue`](../BIMCanvas.Web/src/layouts/MainLayout.vue)
*   **关键代码**:
    *   `watch(() => props.buildComplete)`: 监听构建完成状态。
    *   CSS 动画 `@keyframes island-pulse`: 控制灵动岛的宽度和高度微调。

---

## 4. 代码索引 (Code Index)

| 模块 | 文件路径 | 职责 |
| :--- | :--- | :--- |
| **Orchestrator** | `src/App.vue` | 状态机管理，串联所有阶段 |
| **Loader UI** | `src/components/UI/BlueprintLoader.vue` | Canvas 粒子网格动画 |
| **Layout UI** | `src/layouts/MainLayout.vue` | CSS3 UI 元素进场过渡 |
| **3D Service** | `src/services/three/ThreeSceneService.ts` | 桥接 Vue 事件与 Three.js |
| **3D Builder** | `src/services/builders/SceneBuilder.ts` | `buildProgressively` 异步生长逻辑 |
