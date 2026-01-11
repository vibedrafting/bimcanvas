# SVG 渲染问题报告

> 生成时间：2026-01-11
> **问题状态：未解决** ❌
> 最后更新：2026-01-11

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

## 2. 修复历程

### 2.1 修复尝试汇总

| # | 问题假设 | 修复方案 | 结果 |
|---|---------|---------|------|
| 1 | SVG 原点偏移 | 添加 Box3 居中逻辑 | ❌ 无效 |
| 2 | 黑色描边不可见 | 将 #000000 替换为 #ffffff | ❌ 无效 |
| 3 | XY→XZ 坐标系转换 | rotation.x = +Math.PI/2 | ❌ 无效 |
| 4 | CSS class 无法解析 | 添加默认描边颜色和样式 | ❌ 无效 |
| 5 | linewidth 被忽略 | 使用 SVGLoader.pointsToStroke() | ❌ 无效 |
| 6 | **Z 轴符号错误** | rotation.x 从 +PI/2 改为 -PI/2，position.z = -y | ❌ 无效 |
| 7 | **缩放轴错误** | scale 从 (x,1,y) 改为 (x,y,1) | ❌ 无效 |
| 8 | **透明材质渲染顺序** | 移除 transparent/depthWrite/opacity 设置 | ⏳ 待验证 |

### 2.2 业务专家的关键分析

业务专家指出了两个核心问题：

#### 问题 A：Z 轴坐标映射不一致
- **场景约定**：`2D (x, y) → 3D (x, 0, -y)`，`rotation.x = -Math.PI/2`
- **SVG 代码**（修复前）：`rotation.x = +Math.PI/2`，`position.z = y`
- **结果**：SVG 被放到镜像半平面（相机视野外）

**已修复**：
```typescript
// 修改前
moduleGroup.rotation.x = Math.PI / 2;
moduleGroup.position.set(x, SVG_HEIGHT, y);

// 修改后
moduleGroup.rotation.x = -Math.PI / 2;
moduleGroup.position.set(x, SVG_HEIGHT, -y);
```

#### 问题 B：缩放轴错误
- **问题**：`scale.set(scaleX, 1, scaleY)` 对 XY 平面几何体无效
- **原因**：Three.js 变换顺序是 Scale → Rotate → Position，SVG 几何在 XY 平面，scale.z 对 z=0 的几何不起作用

**已修复**：
```typescript
// 修改前
moduleGroup.scale.set(scaleX, 1, scaleY);

// 修改后
moduleGroup.scale.set(scaleX, scaleY, 1);
```

#### 问题 C：updateModuleTransform 方法不一致
- **问题**：`updateModuleTransform()` 方法未同步更新坐标映射
- **已修复**：同步应用 Z 轴和缩放轴修复

---

## 3. 当前代码状态

### 3.1 SVGModuleRenderer.ts 变换代码
```typescript
// 第 68-77 行
// 5. 应用变换（转换到 Y-Up 坐标系，与家具模块一致）
// 场景约定：2D (x, y) → 3D (x, 0, -y)
// 模块统一使用 rotation.x = -Math.PI / 2（参考 SceneBuilder.ts:754）
moduleGroup.rotation.x = -Math.PI / 2;
// 位置：与场景约定一致，z = -y
moduleGroup.position.set(transform.position.x, this.SVG_HEIGHT, -transform.position.y);
// 朝向旋转（在 XZ 平面上是绕 Y 轴）
moduleGroup.rotation.y = transform.rotation;
// SVG 几何在 XY 平面，缩放作用在 X 和 Y 轴（变换顺序：Scale → Rotate → Position）
moduleGroup.scale.set(transform.scale.x, transform.scale.y, 1);
```

### 3.2 日志输出（精简版 - 2026-01-11）

```
[SVG] Path0: pts=53, verts=312
[SVG] Path1: pts=53, verts=312
[SVG] Path2: pts=53, verts=312
[SVG] Path3: pts=53, verts=312
[SVG] Path4: pts=2, verts=0
[SVG] Path5: pts=2, verts=0
[SVG] children=6, center=(900, 1000)
[SVG] m_1: pos=(10100, 760, -2420), scale=(1.11, 0.90)
```

### 3.3 日志分析

| 指标 | 值 | 分析 |
|------|-----|------|
| **几何体顶点数** | 312 (Path0-3) | ✅ 正常，有实际几何数据 |
| **Path4-5 顶点数** | 0 | ⚠️ 直线段(pts=2)生成失败，但不影响主要形状 |
| **子对象数量** | 6 | ✅ 正常 |
| **居中计算** | (900, 1000) | ✅ 与 viewBox 1800x2000 匹配 |
| **X 位置** | 10100 | ✅ 在场景范围内 |
| **Y 位置** | 760 | ✅ 高于家具(750)，低于相机(10000) |
| **Z 位置** | -2420 | ✅ 符合 z=-y 约定 |
| **缩放** | (1.11, 0.90) | ✅ 接近 1:1，合理 |

### 3.4 相机与 SVG 空间关系

```
相机位置: Y = 10000 (俯视)
         ↓ 看向 Y = 0 平面
─────────────────────────────
SVG 位置: Y = 760
家具顶面: Y = 750
地面:     Y = 0
```

**结论**：几何体数据正确，变换值合理，相机应该能看到 SVG。问题可能在渲染层面（材质/深度/图层）。

---

## 4. 待排查的问题

### 4.1 pointsToStroke 几何体是否有效
- `Created group with 6 children` 说明确实创建了子对象
- 但不确定这些对象的几何体是否有效（顶点数是否 > 0）

**建议验证**：
```typescript
if (strokeGeometry) {
  console.log('vertex count:', strokeGeometry.attributes.position?.count);
}
```

### 4.2 SVG 高度 (SVG_HEIGHT = 760) 是否合适
- 当前设置为 760，略高于家具模块高度 (750)
- 可能需要验证相机是否能看到这个高度

### 4.3 材质渲染问题 ⬅️ 第8次修复目标
- `depthWrite: false` 可能导致渲染顺序问题
- `transparent: true` + `opacity: 0.9` 可能与其他对象冲突
- **修复方案**：已移除所有透明度设置，改用纯不透明材质

### 4.4 图层设置
- SVG 设置了 `layers.enable(LAYER_MODEL)`
- 需要验证相机是否启用了该图层

---

## 5. 建议的下一步

### 5.1 添加详细调试日志
在 loadSVG 中添加：
```typescript
for (const subPath of path.subPaths) {
  const points = subPath.getPoints();
  console.log('[SVGModuleRenderer] points count:', points.length);

  const geo = SVGLoader.pointsToStroke(points, strokeStyle);
  if (geo) {
    console.log('[SVGModuleRenderer] geometry vertices:',
      geo.attributes.position?.count || 0);
  } else {
    console.warn('[SVGModuleRenderer] pointsToStroke returned null!');
  }
}
```

### 5.2 创建简化测试 SVG
使用内联样式的最简 SVG 验证基础流程：
```svg
<svg viewBox="0 0 100 100">
  <rect x="10" y="10" width="80" height="80"
        fill="none" stroke="#ffffff" stroke-width="5"/>
</svg>
```

### 5.3 参考 Three.js 官方 SVGLoader 示例
- https://threejs.org/examples/#webgl_loader_svg
- 对比官方实现的差异

### 5.4 考虑修改 SVG 文件
将 CSS class 样式改为内联样式，排除样式解析问题。

---

## 6. 相关文件

| 文件 | 职责 |
|------|------|
| `BIMCanvas.Web/src/services/builders/SVGModuleRenderer.ts` | SVG 加载与渲染 |
| `BIMCanvas.Web/src/services/builders/SceneBuilder.ts` | 场景构建 |
| `modules/assets/mod_bed_001.svg` | 床 SVG 模板 |
| `modules/assets/mod_cabinet_005.svg` | 床头柜 SVG 模板 |

---

## 7. 结论

已完成 7 次修复尝试，包括业务专家指出的 Z 轴映射和缩放轴问题，但 SVG 仍然不可见。

**最可能的剩余原因**：
1. `SVGLoader.pointsToStroke()` 返回空几何体（样式参数不完整）
2. SVG 几何体顶点数为 0
3. 渲染/深度/图层配置问题

**建议**：添加更详细的调试日志，验证几何体是否真的被创建。

---

*报告更新完毕*
