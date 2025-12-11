# BIMCanvas.Web 实施计划 (Cyberpunk Edition)

> **版本**：v2.0
> **更新日期**：2025-12-11
> **状态**：Phase 1 开发中
> **架构变更**：从 SVG/Konva 转向 Three.js (WebGL) 以实现赛博朋克全息风格

---

## 一、项目概述

### 1.1 核心定位

**BIMCanvas.Web 是基于 Three.js 的 3D 可视化前端**

不再使用 SVG 渲染，而是构建一个高性能的 WebGL 场景，提供“赛博朋克/科幻全息”风格的视觉体验，同时保留 CAD 软件的精准操作手感。

**核心职责**：
- **3D 渲染**：将 CanvasDocument 数据转换为 3D 场景 (Three.js)
- **视觉特效**：实现霓虹发光 (Bloom)、全息材质、深空背景
- **交互编辑**：射线拾取 (Raycaster)、拖拽移动、旋转、属性编辑
- **状态同步**：与 Server 进行 SignalR 实时通信

### 1.2 系统架构

```
BIMCanvas.Web (Vue 3 + TS)
├── ThreeSceneService (渲染核心)
│   ├── Scene (Deep Space Black)
│   ├── OrthographicCamera (Top-down View)
│   ├── WebGLRenderer
│   └── EffectComposer (UnrealBloomPass)
│
├── SceneBuilder (构建器)
│   ├── WallBuilder (Neon Lines)
│   ├── ZoneBuilder (Holographic Planes)
│   └── ModuleBuilder (3D Models/Placeholders)
│
├── InteractionService (交互)
│   ├── Raycaster (拾取)
│   ├── DragControls (拖拽)
│   └── Selection (高亮)
│
└── GridSystem (辅助)
    └── Dynamic Grid (Snap-to-Grid, Auto-hide)
```

---

## 二、技术选型

### 2.1 核心框架

| 组件 | 选型 | 说明 |
|------|------|------|
| **前端框架** | Vue 3 | Composition API, Script Setup |
| **构建工具** | Vite | 极速构建 |
| **语言** | TypeScript | 类型安全 |
| **3D 引擎** | Three.js | WebGL 渲染库 |
| **后期处理** | UnrealBloomPass | 霓虹发光特效 |
| **状态管理** | Pinia | 响应式状态 |
| **通信** | SignalR | 实时同步 |

### 2.2 依赖清单

```json
{
  "dependencies": {
    "three": "^0.160.0",
    "@types/three": "^0.160.0",
    "vue": "^3.4.0",
    "pinia": "^2.1.0",
    "@microsoft/signalr": "^8.0.0",
    "axios": "^1.6.0",
    "sass": "^1.69.0"
  }
}
```

---

## 三、功能规格

### 3.1 视觉风格 (Cyberpunk Holographic)

- **环境 (Atmosphere)**：
    - 背景：深邃太空黑 (Deep Space Black, `0x050510`)。
    - 粒子：悬浮的环境粒子 (Ambient Particles)，随相机微动。
- **光效 (Neon & Bloom)**：
    - 全局泛光：使用 `UnrealBloomPass` (Strength: 1.5, Radius: 0.4, Threshold: 0.85)。
    - 墙体：青色/蓝色霓虹线条 (`LineSegments`)。
    - 选中：高亮发光，甚至带有呼吸效果。
- **材质 (Material)**：
    - 区域 (Zone)：半透明全息材质，边缘发光。
    - 家具 (Module)：线框或半透明实体。

### 3.2 交互系统

- **导航 (Navigation)**：
    - **平移**：中键/右键拖拽 (1:1 无惯性，CAD 手感)。
    - **缩放**：滚轮缩放 (以光标为中心)。
    - **适配**：Zoom Extents (自动适配视图范围)。
- **编辑 (Editing)**：
    - **拾取**：Raycaster 射线检测 (支持 Wall, Column, Zone, Module, Opening)。
    - **拖拽**：左键拖拽 Module，支持吸附。
    - **旋转**：(待开发) 选中后按 R 键或使用 Gizmo 旋转。
- **辅助 (Aids)**：
    - **网格系统**：
        - 仅在**拖拽物体**时显示。
        - 支持 **Snap-to-Grid** (吸附)，步长可配 (如 100mm)。
        - 样式：深灰色细线，位于物体下方。

### 3.3 UI 组件

- **Toolbar (顶部工具栏)**：
    - 全宽固定 (Fixed Top)。
    - 功能：加载演示、同步、撤销/重做 (预留)。
- **PropertyPanel (属性面板)**：
    - 右侧固定，位于 Toolbar 下方。
    - 动态显示选中对象的属性 (ID, Type, Dimensions, Custom Data)。
- **StatusBar (状态栏)**：
    - 底部固定。
    - 显示：连接状态、版本号、当前选中项 ID。

---

## 四、数据结构 (v2.8)

严格遵循 `BIMCanvas.Core` 定义的数据模型。

```typescript
export interface CanvasDocument {
  id: string;
  version: number;
  coordinateSystem: 'cartesian_mm_yUp'; // Y-up 坐标系
  walls: Wall[];
  columns: Column[];
  zones: Zone[];
  modules: Module[];
  openings: Opening[];
  // ...
}
```

> **注意**：Three.js 是 Y-up 坐标系，与 Core 的定义一致。但在 Top-down 视图中，相机看向 -Z，屏幕平面为 X-Y 平面。

---

## 五、实施计划

### 5.1 Phase 1: 核心重构 (已完成)

- [x] **基础设施**：Vue 3 + Vite + TS + Pinia。
- [x] **渲染引擎**：ThreeSceneService, UnrealBloomPass。
- [x] **场景构建**：WallBuilder, ZoneBuilder (Holographic)。
- [x] **交互基础**：Raycaster, Selection, Drag & Drop。
- [x] **导航优化**：CAD-style Pan/Zoom, Zoom Extents。
- [x] **UI 重构**：Fixed Toolbar/StatusBar/PropertyPanel。

### 5.2 Phase 2: 功能完善 (进行中)

- [ ] **网格系统优化**：实现“仅拖拽时显示”逻辑。
- [ ] **旋转功能**：实现 Module 的旋转操作。
- [ ] **粒子效果**：添加 Ambient Particles。
- [ ] **墙面完成面**：(暂缓) 暂时忽略 WallFinish 显示。
- [ ] **实时同步**：联调 SignalRService。

### 5.3 Phase 3: 高级特性 (待定)

- [ ] **多选支持**：框选、Shift 加选。
- [ ] **性能优化**：InstancedMesh 渲染大量重复构件。
- [ ] **Gizmo 工具**：可视化移动/旋转轴。

---

## 六、变更记录

| 日期 | 版本 | 变更内容 |
|------|------|----------|
| 2025-12-11 | v2.0 | **架构重构**：SVG -> Three.js；**风格变更**：Cyberpunk Holographic；**交互升级**：CAD 导航、Raycaster 拾取 |
| 2025-12-11 | v1.1 | (已废弃) SVG/Konva 混合方案 |
