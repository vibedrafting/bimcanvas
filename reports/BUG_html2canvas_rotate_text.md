# BUG 报告：html2canvas 无法正确渲染旋转文字

**报告日期**：2026-01-14  
**严重程度**：中等  
**影响范围**：截图功能 - Labels 图层显示  
**状态**：待修复

---

## 问题描述

使用截图工具时，Labels 图层中的**竖向文字**（通过 CSS `transform: rotate()` 实现）在截图后变成**横向显示**，导致截图内容与实际显示不一致。

### 复现步骤

1. 打开 BIMCanvas 项目
2. 加载包含 Labels 图层的户型图
3. 点击截图按钮
4. 观察截图结果中的 Labels 文字方向

### 预期结果

Labels 文字保持竖向显示，与屏幕实际显示一致。

### 实际结果

Labels 文字变成横向显示。

---

## 技术分析

### 根本原因

`html2canvas` 库的工作原理**不是真正的屏幕截图**，而是：

1. 遍历 DOM 树，读取每个元素的计算样式
2. 使用 Canvas 2D API **重新绘制**页面内容

对于 CSS `transform: rotate()` 属性，html2canvas 需要：
- 解析 CSS transform matrix
- 在 Canvas context 上执行 `ctx.translate()` + `ctx.rotate()`
- 然后绘制文字内容

**这个转换过程对复杂的 transform 组合支持不完善，是 html2canvas 库的已知限制。**

### 为什么不能直接换用其他截图方案

| 方案 | 问题 |
|------|------|
| Three.js Canvas `toDataURL()` | 无法捕获 DOM 文字（Labels 是 CSS2DRenderer 创建的 DOM 元素，叠加在 Canvas 上） |
| 浏览器原生 `getDisplayMedia()` | 需要用户授权，弹出权限提示，用户体验差 |
| 服务端截图（Puppeteer） | 需要后端支持，部署复杂，延迟高 |

**html2canvas 的不可替代优势**：能同时捕获 WebGL Canvas 内容 + 叠加的 DOM 元素（包括文字标签）。

---

## 可能的解决方案

### 方案 A：截图时切换 CSS 属性

**思路**：将 `transform: rotate(-90deg)` 临时替换为 `writing-mode: vertical-rl`

**优点**：改动小，只需在截图前后切换样式  
**缺点**：需验证 html2canvas 对 `writing-mode` 的支持程度  
**改造成本**：低

### 方案 B：分层截图 + 合成

**思路**：
1. 使用 Three.js Canvas 的 `toDataURL()` 截取 Canvas 内容
2. 使用 html2canvas 单独截取 Labels 图层
3. 用 Canvas 合成两张图

**优点**：各取所长  
**缺点**：逻辑复杂，需要处理图层对齐  
**改造成本**：中

### 方案 C：手动绘制旋转文字

**思路**：在 html2canvas 调用前，遍历所有旋转的 Labels，用 Canvas 2D API 手动绘制（包括正确旋转），然后隐藏原 DOM Labels

**优点**：完全控制渲染  
**缺点**：实现复杂，需要同步字体、颜色、位置等参数  
**改造成本**：高

### 方案 D：接受限制

**思路**：暂不修复，在用户文档中说明此限制

**优点**：零成本  
**缺点**：用户体验不佳  
**改造成本**：无

---

## 推荐方案

**优先尝试方案 A**：验证 `writing-mode: vertical-rl` 在 html2canvas 中的渲染效果。

### 验证步骤

1. 查找 Labels 竖向文字的 CSS 实现代码
2. 创建测试页面，对比 `transform: rotate()` 和 `writing-mode` 的 html2canvas 渲染效果
3. 如果可行，在截图流程中添加样式切换逻辑

---

## 相关代码位置

- 截图组件：`BIMCanvas.Web/src/components/UI/AdvancedScreenshotOverlay.vue`
- Labels 渲染：待确认（可能在 Three.js Scene Builder 或 CSS2DRenderer 相关代码中）

---

## 参考资料

- [html2canvas GitHub Issues - Transform support](https://github.com/niklasvh/html2canvas/issues)
- [CSS writing-mode MDN](https://developer.mozilla.org/en-US/docs/Web/CSS/writing-mode)
