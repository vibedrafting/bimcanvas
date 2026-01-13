# Web 前端技术文档

> **版本**：v1.0 | **更新日期**：2026-01-13
> **本文档整合自**：SVG_Rendering_System.md + Web_Loading_Sequence.md

---

## 目录

1. [系统概述](#1-系统概述)
2. [启动流程](#2-启动流程)
3. [SVG 渲染系统](#3-svg-渲染系统)
4. [坐标系统与变换](#4-坐标系统与变换)
5. [SVG 文件规范](#5-svg-文件规范)
6. [已知问题与解决方案](#6-已知问题与解决方案)
7. [扩展指南](#7-扩展指南)

---

## 1. 系统概述

### 1.1 前端技术栈

| 技术 | 用途 |
|------|------|
| **Vue 3 + TypeScript** | UI 框架与类型安全 |
| **Three.js** | 3D 渲染引擎 |
| **SVGLoader** | Three.js 官方 SVG 加载器 |
| **HTML5 Canvas** | 启动动画 |

### 1.2 核心模块

| 模块 | 职责 |
|------|------|
| **启动流程** | 电影式加载体验，从蓝图到场景 |
| **SVG 渲染** | 家具模块 2D 轮廓预览 |
| **场景构建** | 3D 建筑生长动画 |

---

## 2. 启动流程

BIMCanvas Web 端的启动过程被设计为一个**连贯的电影式体验（Cinematic Experience）**。它不仅仅是一个加载进度条，而是通过"从无序到有序"、"从蓝图到实体"的视觉隐喻，引导用户平滑地进入沉浸式的工作环境。

### 2.1 四阶段流程

| 阶段 | 名称 | 描述 |
|------|------|------|
| Phase 1 | **蓝图构建 (Splash)** | 建立坐标系与网格 |
| Phase 2 | **UI 展开 (Chrome)** | 界面框架入场 |
| Phase 3 | **场景搭建 (Scene Build)** | 3D 建筑生长 |
| Phase 4 | **就绪 (Ready)** | 交互元素激活 |

### 2.2 核心编排器 (Orchestrator)

整个加载流程的"总导演"是根组件 `App.vue`。它负责协调数据加载、最小展示时间和各阶段状态的切换。

- **文件位置**: `src/App.vue`
- **关键状态**: `loadingStage` (Ref<number>)

| Stage 值 | 含义 |
|----------|------|
| 0 | Loader (启动画面) |
| 1 | Grid (网格就绪) |
| 2 | (保留) 预留位置，当前跳过 |
| 3 | Island (灵动岛/工具层) |
| 4 | Chrome (主UI框架) |
| 5 | Scene (场景构建) |

**核心逻辑**：
1. **强制等待**：使用 `Promise.race` 确保启动画面至少展示 **2.5秒**，避免加载过快导致视觉闪烁，同时为数据加载和视口计算预留充足时间
2. **视口计算**：数据加载完成后，立即调用 `ViewCalculator` 计算最佳视口参数（Spacing, Offset），并将这些参数传递给启动画面，实现粒子从"混乱"到"对齐网格"的平滑过渡
3. **时序控制**：通过 `setTimeout` 逐步改变 `loadingStage` 的值，触发不同层级 UI 的 CSS 过渡

### 2.3 Phase 1: 蓝图构建 (Splash Screen)

用户首先看到的画面，基于 HTML5 Canvas 的粒子网格动画。

**视觉效果**：
- **初始**：粒子处于混沌或扫描状态，屏幕中央显示 "ESTABLISHING GRID"
- **锁定**：当后端数据返回并计算出视口后，粒子开始移动
- **流星效应**：粒子拖着长长的尾巴（Meteor Trails）移动
- **网格编织**：采用**基于线段 (Segment-based)** 的生成逻辑，确保每一根网格线都有且仅有一个粒子负责绘制

**组件**: `src/components/UI/BlueprintLoader.vue`

**关键代码**：
- `initParticles()`: 逻辑重构为遍历网格线段（而非节点），对于每一段横向或纵向的网格线，生成一个专属粒子，确保每根网格线有且仅有一个粒子负责绘制
- `Particle` 类: 构造函数接收明确的 `direction` 参数，确保粒子运动方向与线段绘制需求一致
- `draw()`: 实现流星头部淡出逻辑（80%进度时消失）和尾巴绘制
- **去静态化设计**：移除原有的 `drawConnections` 静态连线逻辑，网格完全由动态轨迹生成。随着粒子移动到位，头部"燃尽"消失，留下的尾巴首尾相连，完美编织成最终的背景网格

### 2.4 Phase 2: UI 展开 (UI Expansion)

当蓝图网格建立完毕，加载层淡出，应用的主界面 UI 元素按层级依次入场。

**视觉效果**：
- **Stage 3**：顶部的灵动岛 (`DynamicIsland`) 和右下角的图层管理器 (`FloatingLayerManager`) 首先浮现
- **Stage 4**：顶部导航栏 (`AppHeader`, `RibbonToolbar`) 和左侧属性面板 (`PropertyPanel`) 显现

**组件**: `src/layouts/MainLayout.vue`

**关键代码**：
- CSS 类 `.visible` 配合 `loadingStage` prop 控制 `opacity` 和 `transform`
- 动效参数: `transition: ... 0.6s cubic-bezier(0.34, 1.56, 0.64, 1)` (带轻微回弹)

### 2.5 Phase 3: 场景搭建 (Cinematic Scene Build)

最核心的"建筑生长"过程。3D 场景中的物体不是一次性出现，而是按建筑逻辑逐步生成。

**视觉效果**：
1. **结构层**：柱子 (Columns) 批量升起
2. **围护层**：墙体 (Walls) 批量升起
3. **开口**：门窗 (Openings) 出现
4. **内容层**：家具模块 (Modules) 一个接一个地快速弹出 ("Pop" effect)

**触发机制**: `App.vue` 派发 `bimcanvas:play-build-sequence` 全局事件

**服务**：
- 监听: `src/services/three/ThreeSceneService.ts`
- 实现: `src/services/builders/SceneBuilder.ts`

**关键代码**：
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

### 2.6 Phase 4: 就绪与收尾 (Final Polish)

当场景构建完成，最后的交互元素就位，系统完全可交互。

**视觉效果**：
- 右侧 **AI 指挥中心** (`AICommandCenter`) 从屏幕边缘滑入
- 顶部 **灵动岛** 执行一次"脉冲"动画 (`hint-pulse`)，提示用户系统已就绪

**关键代码**：
- `watch(() => props.buildComplete)`: 监听构建完成状态
- CSS 动画 `@keyframes island-pulse`: 控制灵动岛的宽度和高度微调

---

## 3. SVG 渲染系统

SVG 模块渲染系统负责在 Three.js 3D 场景中为家具模块渲染 2D 轮廓预览图。这些 SVG 轮廓显示家具的内部细节（如床的枕头、被子轮廓，柜子的门板线条等），悬浮在家具 3D 模型的正上方。

### 3.1 设计目标

| 目标 | 说明 |
|------|------|
| **视觉预览** | 在 3D 场景中显示家具的 2D 细节轮廓 |
| **位置同步** | SVG 轮廓与家具模块位置、朝向保持一致 |
| **性能优化** | SVG 按 moduleId 缓存，避免重复加载 |
| **拖拽跟随** | 模块移动时 SVG 实时跟随更新 |

### 3.2 系统架构

```
┌─────────────────────────────────────────────────────────────────┐
│                        SceneBuilder                              │
│  - 场景构建入口                                                   │
│  - 创建家具模块时调用 SVGModuleRenderer                           │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                     SVGModuleRenderer                            │
│  - SVG 加载与缓存                                                 │
│  - 几何体创建（填充 + 描边）                                       │
│  - 坐标变换（2D → 3D）                                            │
│  - 生命周期管理                                                   │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                    ModuleLibraryService                          │
│  - 提供模块定义（尺寸、SVG URL）                                   │
│  - API: /api/modules/svg/{moduleId}                              │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                      SVG 文件存储                                 │
│  - 路径: modules/assets/{moduleId}.svg                           │
│  - 格式: 标准 SVG，viewBox 定义尺寸                               │
└─────────────────────────────────────────────────────────────────┘
```

### 3.3 数据流

```
Module 数据 (JSON)
    │
    ▼
SceneBuilder.createModuleMesh()
    │
    ├──► 创建 3D 家具模块 (ExtrudeGeometry)
    │
    └──► svgRenderer.renderModuleSVG(module)
              │
              ▼
         loadSVG(moduleId)
              │
              ├─ 缓存命中 → 返回缓存 Group
              │
              └─ 缓存未命中 → SVGLoader.load()
                      │
                      ▼
                 解析 SVG paths
                      │
                      ├─ 填充 → ShapeGeometry
                      │
                      └─ 描边 → pointsToStroke()
                              │
                              ▼
                         居中几何体
                              │
                              ▼
                         缓存 & 返回
```

### 3.4 核心流程

#### 渲染流程 (`renderModuleSVG`)

```typescript
async renderModuleSVG(module: Module): Promise<THREE.Group | null>
```

**步骤**：
1. **获取模块定义** - 从 ModuleLibraryService 获取尺寸信息
2. **加载 SVG** - 调用 `loadSVG()`，支持缓存
3. **计算变换** - 位置、旋转、缩放
4. **创建父子 Group** - 显式控制旋转顺序（关键！）
5. **设置渲染属性** - depthTest: false, renderOrder: 999
6. **设置图层** - LAYER_MODEL
7. **添加到场景**
8. **记录映射表** - moduleId → Group

#### 变换计算 (`calculateModuleTransform`)

```typescript
private calculateModuleTransform(module: Module, moduleDef: ModuleDefinition): {
  position: { x: number, y: number };
  rotation: number;
  scale: { x: number, y: number };
}
```

| 属性 | 计算方式 |
|------|---------|
| **position** | 模块 bounds 多边形的几何中心 |
| **rotation** | 解析 facing（字符串或向量）→ 弧度 |
| **scale** | bounds 尺寸 / moduleDef.size |

### 3.5 关键代码解析

#### 父子 Group 方案（核心）

```typescript
// ❌ 错误方式（会导致 SVG 变成垂直面）
group.rotation.x = -Math.PI / 2;  // 压平
group.rotation.y = facing;        // 绕本地 Y 轴，会把 SVG 掀起来

// ✅ 正确方式（父子 Group）
const root = new THREE.Group();          // 父级：压平 + 世界坐标
const svg2D = svgGroup.clone(true);      // 子级：2D 变换

// 子级：在 XY 平面内做 2D 变换
svg2D.rotation.set(0, 0, transform.rotation);  // 绕 Z 做朝向
svg2D.scale.set(scaleX, scaleY, 1);

root.add(svg2D);

// 父级：压平 + 定位
root.rotation.set(-Math.PI / 2, 0, 0);   // 压平（XY → XZ）
root.position.set(cx, SVG_HEIGHT, -cy);  // 世界坐标
```

**为什么需要父子 Group？**

Three.js Euler 旋转默认是 XYZ 顺序，绕**本地轴**旋转。当先设置 `rotation.x` 后，`rotation.y` 是绕已旋转后的本地 Y 轴，不是世界 Y 轴。这会导致 facing = east/west 时 SVG 变成垂直面。

#### 描边几何体创建

```typescript
// THREE.Line 的 linewidth 在大多数平台被忽略
// 必须使用 pointsToStroke 创建有宽度的描边

const strokeStyle = {
  ...path.userData?.style,
  strokeWidth: path.userData?.style?.strokeWidth || 20  // 默认宽度
};

const points = subPath.getPoints();
const strokeGeometry = SVGLoader.pointsToStroke(points, strokeStyle);

if (strokeGeometry && strokeGeometry.attributes.position?.count > 0) {
  const strokeMesh = new THREE.Mesh(strokeGeometry, material);
  group.add(strokeMesh);
}
```

#### 颜色处理

```typescript
// SVGLoader 无法解析 <style> 中的 CSS class
// 需要兜底处理黑色 → 白色（深色背景可见）

const displayColor = (!strokeColor || strokeColor === '#000000')
  ? '#ffffff'
  : strokeColor;
```

#### 渲染可见性兜底

```typescript
// 确保 SVG 不被其他对象遮挡
material.depthTest = false;
mesh.renderOrder = 999;
```

---

## 4. 坐标系统与变换

> BIMCanvas 坐标系与角度系统详见 [Architecture.md §7 坐标系统](./Architecture.md#7-坐标系统)

### 4.1 Web 端坐标映射

| 坐标系 | X 轴 | Y 轴 | 原点 | 用途 |
|--------|------|------|------|------|
| **BIMCanvas 2D** | 右 | 上 | 左下角 | 数据模型 |
| **SVG** | 右 | 下 | 左上角 | 2D 渲染 |
| **Three.js** | 右 | 上 | 中心 | 3D 预览 |

**转换公式**：`y_screen = canvasHeight - y_model`（用于 SVG 渲染）

### 4.2 Three.js 变换链路

```
位置映射：2D (x, y) → 3D (x, SVG_HEIGHT, -y)
朝向映射：facing → radians（取反，因为 Three.js 旋转方向相反）
```

---

## 5. SVG 文件规范

### 5.1 文件格式

```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {width} {height}">
  <defs>
    <style>
      .main-lines { fill: none; stroke: #000000; stroke-width: 25; }
      .detail-lines { fill: none; stroke: #000000; stroke-width: 20; }
    </style>
  </defs>

  <!-- 图形元素 -->
  <rect ... class="main-lines" />
  <path ... class="detail-lines" />
</svg>
```

### 5.2 尺寸约定

| 属性 | 说明 |
|------|------|
| **viewBox** | 应与 moduleDef.size 成比例 |
| **单位** | SVG 像素，与毫米 1:1 对应 |
| **原点** | 左上角 |

### 5.3 样式约定

| 样式 | 推荐值 | 说明 |
|------|--------|------|
| **fill** | `none` | 轮廓线通常不填充 |
| **stroke** | `#000000` | 会被转换为白色显示 |
| **stroke-width** | 20-25 | 线条粗细（SVG 像素） |

### 5.4 CSS Class 限制

> **重要**：SVGLoader 无法解析 `<style>` 标签中的 CSS class 样式。
>
> 当前通过兜底逻辑处理，但建议在后续升级中考虑：
> 1. 使用内联样式
> 2. 或在加载前预处理 SVG

---

## 6. 已知问题与解决方案

### 6.1 Euler 旋转陷阱

| 问题 | 原因 | 解决方案 |
|------|------|---------|
| SVG 变成垂直面 | rotation.y 绕本地 Y 轴旋转 | 使用父子 Group |

**验证方法**：
```typescript
const size = new THREE.Box3().setFromObject(root).getSize(new THREE.Vector3());
// size.y ≈ 0 表示水平面 ✅
// size.y >> 0 表示垂直/倾斜 ❌
```

### 6.2 CSS Class 样式不生效

| 问题 | 原因 | 解决方案 |
|------|------|---------|
| 描边不显示 | SVGLoader 不解析 CSS class | 提供默认描边颜色和宽度 |

### 6.3 描边宽度被忽略

| 问题 | 原因 | 解决方案 |
|------|------|---------|
| THREE.Line linewidth 无效 | WebGL 限制 | 使用 pointsToStroke() |

### 6.4 SVG 被遮挡

| 问题 | 原因 | 解决方案 |
|------|------|---------|
| SVG 不可见 | 深度测试/渲染顺序 | depthTest: false + renderOrder: 999 |

---

## 7. 扩展指南

### 7.1 添加新模块 SVG

1. 创建 SVG 文件：`modules/assets/{moduleId}.svg`
2. 确保 viewBox 与模块尺寸匹配
3. 使用 `fill: none; stroke: #000000` 样式
4. 在 ModuleLibraryService 中注册模块

### 7.2 自定义渲染样式

修改 `loadSVG()` 中的材质创建：

```typescript
const material = new THREE.MeshBasicMaterial({
  color: new THREE.Color(displayColor),
  side: THREE.DoubleSide,
  depthTest: false,
  // 可添加：
  transparent: true,
  opacity: 0.8
});
```

### 7.3 性能优化建议

| 优化点 | 建议 |
|--------|------|
| **缓存策略** | 当前按 moduleId 缓存，可考虑 LRU |
| **几何体合并** | 多个模块可共享几何体实例 |
| **LOD** | 远距离时简化或隐藏 SVG |

### 7.4 潜在升级方向

1. **预处理 SVG**：服务端将 CSS class 转换为内联样式
2. **动态样式**：支持选中/悬停时改变颜色
3. **动画效果**：添加渐入/渐出动画
4. **交互增强**：SVG 元素可点击

---

## 附录

### A. 文件结构

```
BIMCanvas.Web/src/
├── App.vue                           # 启动编排器
├── components/
│   └── UI/
│       └── BlueprintLoader.vue       # 蓝图加载动画
├── layouts/
│   └── MainLayout.vue                # 主布局 + UI 过渡
└── services/
    ├── builders/
    │   ├── SceneBuilder.ts           # 场景构建器
    │   └── SVGModuleRenderer.ts      # SVG 渲染器
    ├── ModuleLibraryService.ts       # 模块库服务
    └── three/
        ├── ThreeSceneService.ts      # 3D 服务桥接
        └── LayerManager.ts           # 图层管理
```

### B. 代码索引

| 模块 | 文件路径 | 职责 |
|------|----------|------|
| **Orchestrator** | `src/App.vue` | 状态机管理，串联所有阶段 |
| **Loader UI** | `src/components/UI/BlueprintLoader.vue` | Canvas 粒子网格动画 |
| **Layout UI** | `src/layouts/MainLayout.vue` | CSS3 UI 元素进场过渡 |
| **3D Service** | `src/services/three/ThreeSceneService.ts` | 桥接 Vue 事件与 Three.js |
| **3D Builder** | `src/services/builders/SceneBuilder.ts` | `buildProgressively` 异步生长逻辑 |
| **SVG Renderer** | `src/services/builders/SVGModuleRenderer.ts` | SVG 渲染核心逻辑 |
| **Module Library** | `src/services/ModuleLibraryService.ts` | 模块定义和 SVG URL |
| **Layer Manager** | `src/services/three/LayerManager.ts` | 图层管理 |

### C. 调试日志

```
[SVG] Path0: pts=53, verts=312    # 路径点数和顶点数
[SVG] children=6, center=(900, 1000)  # 子对象数和居中点
[SVG] m_1: pos=(x, y, z), size=(w, 0, d)  # 位置和尺寸（y≈0 表示水平）
```

### D. 相关文档

| 文档 | 路径 | 内容 |
|------|------|------|
| MCP 工具规范 | [Arch_MCP_Tools.md](./Arch_MCP_Tools.md) | 坐标系统定义 |
| 数据模型 | [Schema.md](./Schema.md) | JSON Schema 定义 |
| 系统架构 | [Architecture.md](./Architecture.md) | 整体架构设计 |

---

*文档整合自 archives/SVG_Rendering_System.md 与 archives/Web_Loading_Sequence.md*
