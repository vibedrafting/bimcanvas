# BIMCanvas.Web 项目文档

**BIMCanvas.Web** 是 BIMCanvas 系统的前端可视化核心，致力于提供现代化的、基于 Web 的 3D 建筑空间展示与交互体验。项目采用 "Calm Tech" 设计理念，通过高对比度的暗色主题和极简的 UI 设计，让用户专注于空间设计本身。

## 🌟 项目概述

本项目是一个基于 **Vue 3** 和 **Three.js** 的单页应用 (SPA)，主要职责是加载、解析并渲染 BIMCanvas 的标准 JSON 数据格式 (`CanvasDocument`)。它不仅是一个查看器，更是未来 AI 辅助设计 (Copilot) 的交互界面。

### 核心能力
- **高性能 3D 渲染**：基于 Three.js 的 WebGL 渲染引擎，支持复杂多边形几何体的实时生成。
- **参数化构件**：
  - **墙体/柱子**：基于 2D 多边形轮廓 (`Polygon2D`) 自动挤压生成 3D 实体。
  - **门窗系统**：参数化生成的门窗模型，包含门框、门扇、开启弧线（2D/3D 混合显示）和玻璃材质。
- **智能相机控制**：内置 `fitToScreen` 算法，根据加载的户型数据自动计算包围盒，调整正交相机 (Orthographic Camera) 的位置和缩放，确保户型完美居中。
- **演示数据集成**：内置标准测试数据 (`basic_structure.json`, `layout_proposal.json`)，方便开发与调试。

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

### 视觉风格 (Visual Style)
采用高对比度的暗色模式，确保在复杂光照环境下依然清晰可读。

- **背景色**: `0x0a0a0f` (深邃黑)
- **墙体**: `0xD0D0D0` (亮灰) - 强调空间边界
- **柱子**: `0x808080` (中灰) - 区分结构构件
- **家具模块**: `0x3b82f6` (沉静蓝) - 突出设计方案
- **门窗**:
  - 门框/窗框: 深灰色金属质感
  - 玻璃: 半透明淡蓝色 (`0x88ccff`, Opacity 0.6)
  - 开启线: 白色半透明线条

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
*文档最后更新时间: 2025-12-15*
