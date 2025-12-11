# BIMCanvas.Web

> **版本**：v2.8
> **更新日期**：2025-12-11
> **状态**：Phase 1 核心渲染与交互功能已完成

---

## 一、项目概述

### 1.1 核心定位

**BIMCanvas.Web 是 BIMCanvas 系统的可视化前端**，基于 WebGL (Three.js) 技术构建，提供高性能、赛博朋克风格的 2D/3D 建筑平面交互体验。

**核心职责**：
- **可视化渲染**：将 `CanvasDocument` 数据渲染为带有霓虹特效的 3D 场景
- **用户交互**：提供 CAD 风格的平移、缩放、选择和拖拽操作
- **状态管理**：管理前端数据状态，并与后端进行实时同步 (SignalR)
- **UI 呈现**：提供属性面板、工具栏等操作界面

### 1.2 技术栈

| 配置项 | 值 | 说明 |
|--------|-----|------|
| 框架 | Vue 3 + TypeScript | 组合式 API (Script Setup) |
| 构建工具 | Vite | 极速冷启动与 HMR |
| 渲染引擎 | Three.js | WebGL 3D 渲染 |
| 后期处理 | UnrealBloomPass | 霓虹发光特效 |
| 状态管理 | Pinia | 响应式状态存储 |
| 实时通信 | @microsoft/signalr | 双向实时同步 |
| 样式预处理 | SCSS | 赛博朋克主题变量管理 |

---

## 二、项目结构

```
BIMCanvas.Web/
├── src/
│   ├── components/               【组件层】Vue 组件
│   │   ├── Canvas/
│   │   │   └── ThreeCanvas.vue      Three.js 渲染容器
│   │   └── UI/
│   │       ├── Toolbar.vue          顶部工具栏
│   │       ├── StatusBar.vue        底部状态栏
│   │       └── PropertyPanel.vue    属性面板
│   │
│   ├── services/                 【服务层】核心逻辑
│   │   ├── ThreeSceneService.ts     场景/相机/渲染器管理
│   │   ├── SceneBuilder.ts          数据 -> 3D 对象转换器
│   │   ├── InteractionService.ts    射线检测与交互事件处理
│   │   ├── GridSystem.ts            动态网格系统
│   │   ├── SignalRService.ts        SignalR 通信服务
│   │   └── ApiService.ts            REST API 调用
│   │
│   ├── stores/                   【状态层】Pinia Store
│   │   └── canvasStore.ts           文档数据与选中状态管理
│   │
│   ├── types/                    【类型定义】
│   │   └── canvas.d.ts              CanvasDocument 数据模型定义
│   │
│   └── style/                    【样式层】
│       └── cyberpunk.scss           全局主题变量与样式
│
├── public/                       【静态资源】
│   └── TestData.json                演示数据
│
├── vite.config.ts                Vite 配置
└── tsconfig.json                 TypeScript 配置
```

---

## 三、核心架构

### 3.1 渲染管线 (ThreeSceneService)

项目摒弃了传统的 SVG/Canvas 2D 方案，采用 Three.js 构建 3D 场景以实现高级视觉效果。

1.  **场景 (Scene)**：使用深空黑 (`0x050510`) 背景。
2.  **相机 (Camera)**：使用 `OrthographicCamera` (正交相机) 模拟 CAD 的 2D 视图，视线沿 -Z 轴向下。
3.  **渲染器 (Renderer)**：开启抗锯齿 (`antialias: true`)。
4.  **后期处理 (Post-Processing)**：
    *   `RenderPass`：基础场景渲染。
    *   `UnrealBloomPass`：全局泛光特效，实现霓虹线框的发光质感。

### 3.2 数据流转

```
后端/文件 (JSON)
    ↓ (Load/SignalR)
CanvasStore (Pinia)
    ↓ (Watch document change)
ThreeCanvas.vue
    ↓ (Rebuild Scene)
SceneBuilder
    ↓ (Create Meshes/Lines)
Three.js Scene
```

### 3.3 交互机制 (InteractionService)

交互逻辑与渲染逻辑分离，通过 `Raycaster` 实现物体拾取。

1.  **指针事件**：监听 `pointerdown`, `pointermove`, `pointerup`。
2.  **射线检测**：将鼠标屏幕坐标转换为世界坐标，检测与 `userData.id` 对象的相交。
3.  **操作分发**：
    *   **左键点击**：选择对象 (Select)，触发属性面板更新。
    *   **左键拖拽**：移动对象 (Translate)，结合 `GridSystem` 实现吸附。
    *   **中键/右键拖拽**：平移视图 (Pan)，由 `MapControls` 接管。
    *   **滚轮滚动**：缩放视图 (Zoom)，自定义逻辑实现“以光标为中心缩放”。

---

## 四、关键功能

### 4.1 赛博朋克视觉风格
- **霓虹线框**：墙体和柱子使用高亮青色/蓝色线条，配合 Bloom 产生发光效果。
- **全息区域**：功能分区 (Zone) 使用半透明材质，模拟全息投影质感。
- **深空背景**：深色背景减少视觉疲劳，突显高亮元素。

### 4.2 CAD 级导航体验
- **无惯性平移**：禁用了 `MapControls` 的阻尼 (Damping)，实现 1:1 的跟手平移体验。
- **光标中心缩放**：重写了缩放逻辑，确保缩放时画面始终以鼠标指针为中心，符合 CAD 软件习惯。
- **自动范围适配**：加载数据时自动计算包围盒，执行 `Zoom Extents` 将内容完整展示在视口内。

### 4.3 动态属性面板
- 支持显示任意选中元素 (Wall, Column, Zone, Module, Opening) 的属性。
- 自动格式化数值和复杂对象，高亮显示关键信息 (如 Type)。

---

## 五、开发状态

### 5.1 已完成 (Phase 1)

| 模块 | 功能 | 状态 |
|------|------|------|
| **基础设施** | 项目搭建 (Vue3+TS+Vite) | ✅ |
| | Pinia 状态管理 | ✅ |
| | 类型定义 (v2.8) | ✅ |
| **渲染引擎** | Three.js 场景搭建 | ✅ |
| | UnrealBloomPass 特效 | ✅ |
| | 场景构建器 (SceneBuilder) | ✅ |
| **交互系统** | 射线检测与选择 | ✅ |
| | 拖拽移动 (Drag & Drop) | ✅ |
| | 网格吸附 (Snap-to-Grid) | ✅ |
| **导航系统** | 平移 (Pan) | ✅ |
| | 缩放 (Zoom to Cursor) | ✅ |
| | 范围适配 (Zoom Extents) | ✅ |
| **UI 组件** | 顶部工具栏 (Toolbar) | ✅ |
| | 属性面板 (PropertyPanel) | ✅ |
| | 状态栏 (StatusBar) | ✅ |

### 5.2 待开发 (Phase 2)

| 模块 | 功能 | 状态 |
|------|------|------|
| **高级交互** | 旋转逻辑 (Rotation) | ⬜ |
| | 多选支持 (Multi-select) | ⬜ |
| **通信** | SignalR 实时同步对接 | 🔶 (服务已建，待联调) |
| **优化** | 渲染性能优化 (Instancing) | ⬜ |

---

## 六、使用指南

### 6.1 环境要求
- Node.js 16+
- npm 8+

### 6.2 安装与运行

1.  **安装依赖**
    ```bash
    npm install
    ```

2.  **启动开发服务器**
    ```bash
    npm run dev
    ```
    访问 `http://localhost:3000`。默认会自动加载演示数据。

3.  **构建生产版本**
    ```bash
    npm run build
    ```

### 6.3 操作说明

| 动作 | 操作方式 |
|------|----------|
| **平移视图** | 按住 **中键** 或 **右键** 拖拽 |
| **缩放视图** | 滚动 **鼠标滚轮** |
| **选择元素** | **左键** 点击元素 |
| **移动元素** | 按住 **左键** 拖拽 (仅限 Module) |
| **取消选择** | 点击空白处或属性面板关闭按钮 |
| **重置视图** | 刷新页面 (F5) |

---

## 七、相关文档

| 文档 | 路径 | 内容 |
|------|------|------|
| 任务列表 | `task.md` | 开发进度追踪 |
| 实施计划 | `implementation_plan.md` | 技术方案细节 |
| 演示演练 | `walkthrough.md` | 功能验证记录 |
