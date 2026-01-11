# SVG 渲染问题报告

> 生成时间：2026-01-11
> 问题状态：未解决
> 优先级：中

---

## 1. 问题概述

### 1.1 目标功能
在 BIMCanvas Web 端为家具模块渲染 SVG 轮廓预览，显示家具的内部细节（如床的枕头、被子轮廓，柜子的门板线条等）。

### 1.2 当前状态
- SVG 文件加载成功（API 返回 200）
- SVGLoader 解析成功（Paths count: 6）
- Three.js 对象创建成功（Created group with 6 children）
- 对象添加到场景成功
- **但界面上完全看不到 SVG 轮廓**

---

## 2. 技术架构

### 2.1 数据流
```
模块库 SVG 文件 (modules/assets/*.svg)
    ↓
Server API (/api/modules/svg/{moduleId})
    ↓
Web 端 fetch 请求
    ↓
Three.js SVGLoader 解析
    ↓
SVGModuleRenderer.ts 处理
    ↓
添加到 Three.js 场景
```

### 2.2 关键文件
| 文件 | 职责 |
|------|------|
| `BIMCanvas.Web/src/services/builders/SVGModuleRenderer.ts` | SVG 加载与渲染 |
| `BIMCanvas.Web/src/services/builders/SceneBuilder.ts` | 场景构建（调用 SVGModuleRenderer） |
| `BIMCanvas.Server/Controllers/ModulesController.cs` | SVG 文件 API |
| `modules/assets/*.svg` | SVG 源文件 |

### 2.3 SVG 文件特点
```svg
<!-- mod_bed_001.svg 示例 -->
<svg viewBox="0 0 1800 2000">
  <defs>
    <style>
      .main-lines { fill: none; stroke: #000000; stroke-width: 25; }
      .detail-lines { fill: none; stroke: #000000; stroke-width: 20; }
    </style>
  </defs>
  <rect x="12.5" y="12.5" width="1775" height="1975" rx="15" class="main-lines" />
  <!-- ... 其他元素 ... -->
</svg>
```

**注意**：SVG 使用 CSS class 定义样式，而非内联样式。

---

## 3. 已尝试的修复方案

### 3.1 修复历程

| # | 问题 | 修复方案 | 结果 |
|---|------|---------|------|
| 1 | SVG 原点偏移 | 添加 Box3 居中逻辑 | ✅ 已应用 |
| 2 | 黑色描边不可见 | 将 #000000 替换为 #ffffff | ✅ 已应用 |
| 3 | XY→XZ 坐标系转换 | rotation.x = Math.PI/2 | ✅ 已应用 |
| 4 | CSS class 无法解析 | 添加默认描边颜色 | ✅ 已应用 |
| 5 | linewidth 被忽略 | 使用 SVGLoader.pointsToStroke() | ✅ 已应用 |

### 3.2 当前代码实现

```typescript
// SVGModuleRenderer.ts - loadSVG 方法核心逻辑

// 填充处理
const fillColor = path.userData?.style?.fill;
if (fillColor && fillColor !== 'none') {
  const shapes = SVGLoader.createShapes(path);
  // 创建 ShapeGeometry + MeshBasicMaterial
}

// 描边处理（当前实现）
const strokeColor = path.userData?.style?.stroke;
const shouldRenderStroke = strokeColor !== 'none' && (!fillColor || fillColor === 'none' || strokeColor);

if (shouldRenderStroke) {
  const strokeStyle = {
    ...path.userData?.style,
    strokeWidth: path.userData?.style?.strokeWidth || 20
  };

  const material = new THREE.MeshBasicMaterial({
    color: new THREE.Color('#ffffff'),
    side: THREE.DoubleSide,
    depthWrite: false,
    transparent: true,
    opacity: 0.9
  });

  for (const subPath of path.subPaths) {
    const strokeGeometry = SVGLoader.pointsToStroke(subPath.getPoints(), strokeStyle);
    if (strokeGeometry) {
      const strokeMesh = new THREE.Mesh(strokeGeometry, material);
      group.add(strokeMesh);
    }
  }
}
```

---

## 4. 问题分析

### 4.1 已排除的原因
- ❌ SVG 文件加载失败 → 日志显示加载成功
- ❌ SVGLoader 解析失败 → Paths count: 6
- ❌ 对象未添加到场景 → Added moduleGroup to scene
- ❌ 线宽太细 → 已改用 pointsToStroke

### 4.2 可能的根本原因

#### 假设 A：pointsToStroke 返回空几何体
- SVG 使用 CSS class 定义样式
- `path.userData?.style` 可能为空对象 `{}`
- `strokeStyle` 缺少必要属性（如 strokeLineCap, strokeLineJoin）
- `pointsToStroke` 可能因为样式不完整而返回空几何体

**验证方法**：添加日志检查 `strokeGeometry.attributes.position.count`

#### 假设 B：居中逻辑破坏了几何体
- 居中时使用 `child.position.x -= center.x`
- 如果 center 计算错误（如 NaN），可能导致几何体位置异常

**验证方法**：添加日志检查 center 的具体数值

#### 假设 C：深度/渲染顺序问题
- SVG_HEIGHT = 760，但可能与其他对象深度冲突
- depthWrite: false 可能导致渲染问题

**验证方法**：尝试不同的 SVG_HEIGHT 值和 depthTest/depthWrite 组合

#### 假设 D：subPaths 为空
- 对于某些 SVG 元素，`path.subPaths` 可能是空数组
- SVGLoader 可能将路径信息存储在其他位置

**验证方法**：添加日志检查 `path.subPaths.length`

### 4.3 最可能的原因
**假设 A** - `pointsToStroke` 需要完整的样式对象，包括：
- strokeWidth
- strokeLineCap
- strokeLineJoin
- strokeMiterLimit

当 CSS class 样式未被解析时，这些属性都是 undefined，可能导致 pointsToStroke 行为异常。

---

## 5. 建议的下一步调试

### 5.1 添加详细日志
```typescript
// 在 loadSVG 方法中添加
console.log('[SVGModuleRenderer] path.userData:', JSON.stringify(path.userData));
console.log('[SVGModuleRenderer] path.subPaths.length:', path.subPaths.length);

for (const subPath of path.subPaths) {
  const points = subPath.getPoints();
  console.log('[SVGModuleRenderer] subPath points count:', points.length);

  const strokeGeometry = SVGLoader.pointsToStroke(points, strokeStyle);
  if (strokeGeometry) {
    console.log('[SVGModuleRenderer] strokeGeometry vertex count:',
      strokeGeometry.attributes.position?.count || 0);
  } else {
    console.warn('[SVGModuleRenderer] pointsToStroke returned null!');
  }
}
```

### 5.2 测试完整样式对象
```typescript
const strokeStyle = {
  strokeWidth: 20,
  strokeLineCap: 'round',
  strokeLineJoin: 'round',
  strokeMiterLimit: 4
};
```

### 5.3 简化测试
创建一个最简单的测试 SVG（使用内联样式），验证基础流程是否正常：
```svg
<svg viewBox="0 0 100 100">
  <rect x="10" y="10" width="80" height="80"
        fill="none" stroke="#ffffff" stroke-width="5"/>
</svg>
```

### 5.4 参考 Three.js 官方示例
- https://threejs.org/examples/#webgl_loader_svg
- 对比官方示例的实现方式

---

## 6. 相关资源

### 6.1 Three.js 文档
- [SVGLoader](https://threejs.org/docs/#examples/en/loaders/SVGLoader)
- [SVGLoader.pointsToStroke](https://threejs.org/docs/#examples/en/loaders/SVGLoader.pointsToStroke)

### 6.2 相关代码文件
- `BIMCanvas.Web/src/services/builders/SVGModuleRenderer.ts`
- `BIMCanvas.Web/src/services/builders/SceneBuilder.ts`
- `modules/assets/mod_bed_001.svg`
- `modules/assets/mod_cabinet_005.svg`

### 6.3 日志文件参考
最后一次测试的 F12 日志显示：
```
SVGModuleRenderer.ts:120 [SVGModuleRenderer] SVG loaded successfully. Paths count: 6
SVGModuleRenderer.ts:201 [SVGModuleRenderer] Created group with 6 children
SVGModuleRenderer.ts:216 [SVGModuleRenderer] Loaded SVG: mod_bed_002
SVGModuleRenderer.ts:96 [SVGModuleRenderer] Added moduleGroup to scene. Children count: 6
SVGModuleRenderer.ts:101 [SVGModuleRenderer] Rendered SVG for module m_1 (大双人床)
```

---

## 7. 结论

SVG 渲染的数据流程完整，但最终输出不可见。最可能的原因是 `SVGLoader.pointsToStroke()` 在缺少完整样式参数时行为异常。

建议后续研究方向：
1. 添加更详细的调试日志，验证 `pointsToStroke` 的输入输出
2. 测试使用内联样式的简化 SVG
3. 参考 Three.js 官方 SVGLoader 示例的完整实现
4. 考虑直接修改 SVG 文件，将 CSS class 改为内联样式

---

*报告完毕*
