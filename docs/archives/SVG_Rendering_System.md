# SVG 模块渲染系统技术文档

> 版本：v1.0 | 更新日期：2026-01-11
>
> 本文档旨在帮助开发者（包括 AI）快速理解 BIMCanvas 的 SVG 模块渲染系统，以便进行后续升级和维护。

---

## 目录

1. [系统概述](#1-系统概述)
2. [架构设计](#2-架构设计)
3. [核心流程](#3-核心流程)
4. [关键代码解析](#4-关键代码解析)
5. [坐标系统与变换](#5-坐标系统与变换)
6. [SVG 文件规范](#6-svg-文件规范)
7. [已知问题与解决方案](#7-已知问题与解决方案)
8. [扩展指南](#8-扩展指南)

---

## 1. 系统概述

### 1.1 功能描述

SVG 模块渲染系统负责在 Three.js 3D 场景中为家具模块渲染 2D 轮廓预览图。这些 SVG 轮廓显示家具的内部细节（如床的枕头、被子轮廓，柜子的门板线条等），悬浮在家具 3D 模型的正上方。

### 1.2 设计目标

| 目标 | 说明 |
|------|------|
| **视觉预览** | 在 3D 场景中显示家具的 2D 细节轮廓 |
| **位置同步** | SVG 轮廓与家具模块位置、朝向保持一致 |
| **性能优化** | SVG 按 moduleId 缓存，避免重复加载 |
| **拖拽跟随** | 模块移动时 SVG 实时跟随更新 |

### 1.3 技术栈

- **Three.js**: 3D 渲染引擎
- **SVGLoader**: Three.js 官方 SVG 加载器
- **TypeScript**: 类型安全的代码实现

---

## 2. 架构设计

### 2.1 系统组件

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

### 2.2 数据流

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

### 2.3 文件结构

```
BIMCanvas.Web/src/services/
├── builders/
│   ├── SceneBuilder.ts          # 场景构建器（调用方）
│   └── SVGModuleRenderer.ts     # SVG 渲染器（核心）
├── ModuleLibraryService.ts      # 模块库服务
└── three/
    └── LayerManager.ts          # 图层管理
```

---

## 3. 核心流程

### 3.1 渲染流程 (`renderModuleSVG`)

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

### 3.2 SVG 加载流程 (`loadSVG`)

```typescript
private async loadSVG(moduleId: string): Promise<THREE.Group | null>
```

**步骤**：

1. **检查缓存** - svgCache Map
2. **获取 URL** - `/api/modules/svg/{moduleId}`
3. **SVGLoader 加载** - 解析 SVG 文件
4. **遍历路径** - 处理每个 path 元素
5. **创建几何体**：
   - 填充 → `ShapeGeometry`
   - 描边 → `SVGLoader.pointsToStroke()`
6. **居中几何体** - 将原点移到几何体中心
7. **缓存并返回**

### 3.3 变换计算流程 (`calculateModuleTransform`)

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

---

## 4. 关键代码解析

### 4.1 父子 Group 方案（核心）

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

### 4.2 描边几何体创建

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

### 4.3 颜色处理

```typescript
// SVGLoader 无法解析 <style> 中的 CSS class
// 需要兜底处理黑色 → 白色（深色背景可见）

const displayColor = (!strokeColor || strokeColor === '#000000')
  ? '#ffffff'
  : strokeColor;
```

### 4.4 渲染可见性兜底

```typescript
// 确保 SVG 不被其他对象遮挡
material.depthTest = false;
mesh.renderOrder = 999;
```

---

## 5. 坐标系统与变换

### 5.1 坐标系对比

| 坐标系 | X 轴 | Y 轴 | Z 轴 | 原点 |
|--------|------|------|------|------|
| **SVG** | 右 | 下 | - | 左上角 |
| **BIMCanvas 2D** | 右 | 上 | - | 左下角 |
| **Three.js 3D** | 右 | 上 | 前 | 中心 |

### 5.2 变换链路

```
SVG 坐标 (viewBox)
    │
    ▼ 居中（移动到几何体中心）
SVG 居中坐标
    │
    ▼ 子级旋转（绕 Z 轴，在 XY 平面内）
朝向旋转后
    │
    ▼ 子级缩放（X, Y 方向）
缩放后
    │
    ▼ 父级旋转（绕 X 轴 -90°，XY → XZ）
压平到水平面
    │
    ▼ 父级平移（世界坐标）
最终 3D 位置

位置映射：2D (x, y) → 3D (x, SVG_HEIGHT, -y)
```

### 5.3 朝向角度转换

```typescript
const directionMap = {
  'north': 0,      // Y+ 方向
  'east': 90,      // X+ 方向
  'south': 180,    // Y- 方向
  'west': 270      // X- 方向
};

// 转换为弧度（取反因为 Three.js 旋转方向）
const radians = -degrees * Math.PI / 180;
```

---

## 6. SVG 文件规范

### 6.1 文件格式

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

### 6.2 尺寸约定

| 属性 | 说明 |
|------|------|
| **viewBox** | 应与 moduleDef.size 成比例 |
| **单位** | SVG 像素，与毫米 1:1 对应 |
| **原点** | 左上角 |

### 6.3 样式约定

| 样式 | 推荐值 | 说明 |
|------|--------|------|
| **fill** | `none` | 轮廓线通常不填充 |
| **stroke** | `#000000` | 会被转换为白色显示 |
| **stroke-width** | 20-25 | 线条粗细（SVG 像素） |

### 6.4 CSS Class 限制

> **重要**：SVGLoader 无法解析 `<style>` 标签中的 CSS class 样式。
>
> 当前通过兜底逻辑处理，但建议在后续升级中考虑：
> 1. 使用内联样式
> 2. 或在加载前预处理 SVG

---

## 7. 已知问题与解决方案

### 7.1 Euler 旋转陷阱

| 问题 | 原因 | 解决方案 |
|------|------|---------|
| SVG 变成垂直面 | rotation.y 绕本地 Y 轴旋转 | 使用父子 Group |

**验证方法**：
```typescript
const size = new THREE.Box3().setFromObject(root).getSize(new THREE.Vector3());
// size.y ≈ 0 表示水平面 ✅
// size.y >> 0 表示垂直/倾斜 ❌
```

### 7.2 CSS Class 样式不生效

| 问题 | 原因 | 解决方案 |
|------|------|---------|
| 描边不显示 | SVGLoader 不解析 CSS class | 提供默认描边颜色和宽度 |

### 7.3 描边宽度被忽略

| 问题 | 原因 | 解决方案 |
|------|------|---------|
| THREE.Line linewidth 无效 | WebGL 限制 | 使用 pointsToStroke() |

### 7.4 SVG 被遮挡

| 问题 | 原因 | 解决方案 |
|------|------|---------|
| SVG 不可见 | 深度测试/渲染顺序 | depthTest: false + renderOrder: 999 |

---

## 8. 扩展指南

### 8.1 添加新模块 SVG

1. 创建 SVG 文件：`modules/assets/{moduleId}.svg`
2. 确保 viewBox 与模块尺寸匹配
3. 使用 `fill: none; stroke: #000000` 样式
4. 在 ModuleLibraryService 中注册模块

### 8.2 自定义渲染样式

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

### 8.3 性能优化建议

| 优化点 | 建议 |
|--------|------|
| **缓存策略** | 当前按 moduleId 缓存，可考虑 LRU |
| **几何体合并** | 多个模块可共享几何体实例 |
| **LOD** | 远距离时简化或隐藏 SVG |

### 8.4 潜在升级方向

1. **预处理 SVG**：服务端将 CSS class 转换为内联样式
2. **动态样式**：支持选中/悬停时改变颜色
3. **动画效果**：添加渐入/渐出动画
4. **交互增强**：SVG 元素可点击

---

## 附录

### A. 相关文件

| 文件 | 职责 |
|------|------|
| `SVGModuleRenderer.ts` | SVG 渲染核心逻辑 |
| `SceneBuilder.ts` | 场景构建，调用 SVG 渲染 |
| `ModuleLibraryService.ts` | 模块定义和 SVG URL |
| `LayerManager.ts` | 图层管理 |

### B. 调试日志

```
[SVG] Path0: pts=53, verts=312    # 路径点数和顶点数
[SVG] children=6, center=(900, 1000)  # 子对象数和居中点
[SVG] m_1: pos=(x, y, z), size=(w, 0, d)  # 位置和尺寸（y≈0 表示水平）
```

### C. 参考文档

- [Three.js SVGLoader 文档](https://threejs.org/docs/#examples/en/loaders/SVGLoader)
- [SVG 渲染问题报告](../reports/SVG_Rendering_Issue_Report.md)

---

*文档结束*
