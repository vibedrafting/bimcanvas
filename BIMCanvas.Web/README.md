# BIMCanvas.Web 项目文档

**BIMCanvas.Web** 是 BIMCanvas 系统的前端可视化核心，致力于提供现代化的、基于 Web 的 3D 建筑空间展示与交互体验。项目采用 "Calm Tech" 设计理念，通过高对比度的暗色主题和极简的 UI 设计，让用户专注于空间设计本身。

## 🌟 项目概述

本项目是一个基于 **Vue 3** 和 **Three.js** 的单页应用 (SPA)，主要职责是加载、解析并渲染 BIMCanvas 的标准 JSON 数据格式 (`CanvasDocument`)。它不仅是一个查看器，更是未来 AI 辅助设计 (Copilot) 的交互界面。

## ✨ 核心功能与开发状态 (Feature Status)

> 状态图例: ✅ 已完成 | 🔶 进行中 | ⬜ 待开发

### 1. 视图与渲染 (View & Rendering)
- ✅ **3D 渲染引擎**: 基于 Three.js 实现高性能建筑模型渲染，支持门窗颜色优化与材质升级。
- ✅ **双渲染模式 (Dual Render Mode)**:
    - **Human View (默认)**: 拟真材质、柔和光影 (AO)、极简信息，面向人类设计师。
    - **Agent View (AI)**: 开启所有辅助图层（网格、标签、包围盒），并叠加 **AI Vision Layer**。
- ✅ **AI 视觉层 (AI Vision Layer)**:
    - 专为 Agent 识别优化的高对比度语义图层。
    - 采用 "Elegant Tech" 配色方案，通过明度与色相差异清晰区分构件。
- 🔶 **CAD 图层管理器 (Layer Manager)**: 
    - 正在恢复并优化类似 CAD 的图层控制系统。
    - 支持 Base (底图), Layout (布置), Intent (意图), Analysis (分析) 图层的独立开关。

### 2. 交互与编辑 (Interaction & Editing)
- ✅ **基础导航**: 平移 (Pan)、缩放 (Zoom)、旋转视图 (Orbit)。
- ✅ **对象选择**: 支持点击选择场景中的构件，显示高亮包围盒。
- ✅ **移动 (Move)**: 支持对象拖拽移动，集成幽灵显示 (Ghosting) 预览。
- ✅ **旋转 (Rotate)**: 支持对象旋转，集成角度吸附与幽灵预览。
- ✅ **幽灵系统 (Ghost System)**: 移动/旋转操作时显示半透明预览，操作结束后自动清除。
- ⬜ **语义吸附 (Semantic Snapping)**: 待实现，吸附墙中线、门窗边缘、对齐线。

### 3. 数据与协作 (Data & Sync)
- ⬜ **AI 实时同步**: 基于 SignalR 的双向状态同步，AI 操作实时可见。
- 🔶 **撤销/重做 (Undo/Redo)**: 正在恢复基于时间轴 (Timeline) 的历史记录管理。
- ⬜ **补丁审查 (Patch Review)**: 可视化审查 AI 提出的修改建议 (Diff)。

### 4. 调试与辅助 (Debug & Tools)
- ✅ **调试控制台 (Debug Console)**: 
    - 悬浮式调试面板，支持 `Ctrl + \`` 快捷键唤起。
    - 实时显示错误日志与执行状态，不遮挡主界面。

### 5. 界面与体验 (UI & UX)
- ✅ **灵动岛 (Dynamic Island)**:
    - 顶部居中悬浮工具栏，支持折叠/展开交互。
    - **状态反馈**: 实时显示 Agent 连接状态 (红/绿/黄点) 和当前操作 (Moving/Rotating/Selecting)。
    - **调试技巧**: 修改 `RibbonToolbar.vue` 中的 `DEBUG_KEEP_EXPANDED = true` 可强制展开灵动岛，方便截图或调试 UI。
- ✅ **主题系统 (Theme System)**:
    - 支持 **明亮 (Light)** / **暗色 (Dark)** 模式一键切换。
    - 基于 CSS Variables 实现，自动适配 3D 场景背景、网格及 UI 控件颜色。

## 🛠️ 技术栈 (Tech Stack)

| 领域 | 技术选型 | 说明 |
|------|----------|------|
| **核心框架** | Vue 3 + TypeScript | 使用 Composition API 和 `<script setup>` 语法 |
| **构建工具** | Vite | 极速冷启动与热更新 (HMR) |
| **3D 引擎** | Three.js | 业界标准的 WebGL 库 |
| **状态管理** | Pinia | 轻量级、类型安全的状态管理 |
| **样式方案** | Vanilla CSS | 使用 CSS Variables 定义设计系统 (Design Tokens) |
| **通信协议** | SignalR (规划中) | 用于与后端 Server/Agent 进行实时事件通讯 |

## 🚀 快速开始 (Getting Started)

### 环境要求
- Node.js 16+
- npm 或 yarn/pnpm

### 安装与运行

1.  **安装依赖**
    ```bash
    npm install
    ```

2.  **启动开发服务器**
    ```bash
    npm run dev
    ```
    启动后访问：`http://localhost:5173`

3.  **构建生产版本**
    ```bash
    npm run build
    ```

## 📂 项目结构 (Project Structure)

```
src/
├── components/         # Vue UI 组件
│   └── (UI 覆盖层、工具栏等)
├── services/           # 核心业务逻辑服务
│   ├── builders/       # 3D 场景构建器
│   │   └── SceneBuilder.ts  # 负责解析 JSON 并生成 Three.js Mesh
│   └── three/          # Three.js 集成层
│       └── ThreeSceneService.ts # 负责场景、相机、渲染器、光照的生命周期管理
├── stores/             # Pinia 状态仓库
│   └── canvasStore.ts  # 管理 CanvasDocument 数据流和加载状态
├── types/              # TypeScript 类型定义
│   └── canvas.ts       # 核心数据模型 (Wall, Column, Opening, etc.)
├── App.vue             # 应用入口组件 (负责挂载 3D 画布和 UI)
└── main.ts             # 应用初始化
```

## 🎨 设计规范 (Design Philosophy)

### 坐标系统 (Coordinate System)
- **Y-Up**: 遵循 Three.js 标准，Y 轴垂直向上。
- **单位**: 毫米 (mm)。
- **数据映射**: JSON 中的 `[x, y]` 坐标直接映射到 3D 场景的 `x, y` 平面，高度由 `z` 轴（挤压深度）控制，或通过旋转使平面躺在 XZ 面上（当前实现为 XY 平面直立模式，相机 Z 轴朝向）。

### 视觉风格与主题 (Visual Style & Themes)
项目内置了强大的主题系统 (`ThemeService`)，支持 **明亮 (Light)** / **暗色 (Dark)** 模式一键切换。

#### AI 视觉配色 (AI Vision Scheme - Elegant Tech)
专为计算机视觉设计的语义化配色方案：

| 构件 | 颜色 | Hex | 说明 |
| :--- | :--- | :--- | :--- |
| **家具模块** | 暖金 (Warm Gold) | `#FFB74D` | **视觉焦点**，柔和高亮 |
| **墙体** | 深蓝灰 (Blue Grey 800) | `#37474F` | 深沉背景结构 |
| **柱子** | 亮蓝灰 (Blue Grey 300) | `#90A4AE` | 高明度，与墙体形成对比 |
| **门** | 春绿 (Spring Green) | `#00E676` | 清新高亮，代表通行/安全 |
| **窗** | 天蓝 (Blue 300) | `#64B5F6` | 标准玻璃语义 |

### 配色系统原则 (Color System Principles)

遵循以下三大核心原则，确保视觉清晰度与一致性：

1.  **不同图层颜色不同**：
    *   **Grid (网格)**：灰色系（辅助层）。
    *   **Components (构件)**：绿色系（核心层）。
    *   **Labels (标签)**：黑/白单色（信息层，极致对比）。

2.  **统一图层颜色统一**：
    *   同一模式下，所有同类元素（如所有标签）必须使用完全一致的颜色，方便 AI 视觉识别。

3.  **明亮主题单独设计**：
    *   不搞简单的颜色反转，而是针对白色背景重新设计高对比度配色。

#### 最终配色方案

| 图层 | 亮色模式 (Light) | 暗色模式 (Dark) | 样式特征 |
| :--- | :--- | :--- | :--- |
| **Grid** | 灰色 (`#6b7280`) | 灰色 (`#6b7280`) | 低调辅助，无干扰 |
| **Components** | 绿色 (`#34c759`) | 绿色 (`#34c759`) | 鲜明轮廓，核心主体 |
| **AI Vision** | **Elegant Tech** | **Elegant Tech** | 高对比度语义填充 (Overlay) |
| **Labels** | **纯黑** (`#000000`) | **纯白** (`#ffffff`) | **极简风格**，无背景，极细反色描边 (1px) |

> **注**：标签层移除了所有发光效果和胶囊背景，采用工程制图标准的“黑白文字 + 描边”方案，以实现最佳的可读性和通透感。

## 📊 数据模型摘要

前端核心依赖 `CanvasDocument` 接口渲染：

```typescript
interface CanvasDocument {
  walls: Wall[];       // 墙体 (Polygon2D)
  columns: Column[];   // 柱子 (Polygon2D)
  openings: Opening[]; // 门窗 (Line2D + Type)
  modules: Module[];   // 家具组合 (Polygon2D + Items)
  // ... 其他字段
}
```

---
*文档最后更新时间: 2025-12-17*
