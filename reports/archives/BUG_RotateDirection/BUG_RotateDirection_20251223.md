# Bug Report: 旋转命令方向与用户期望相反

**报告日期**: 2025-12-23
**更新日期**: 2025-12-23 (加入 Codex 深入分析)
**严重程度**: 高
**状态**: 已修复
**影响范围**: Web 项目旋转命令

---

## 问题描述

用户执行旋转命令时，实际旋转方向与预期相反：
- 用户顺时针拖动鼠标 → 预览（Ghost）显示顺时针旋转 ✓
- 用户确认操作 → 实际结果逆时针旋转 ✗

用户描述出现选择Bug的时间：
是在尝试修复Web端门的左右开启方向错误后导致的，在修复完成之前旋转命令是可以正常旋转的。

**复现步骤**：
1. 选择一个模块
2. 激活旋转命令
3. 设置旋转中心
4. 设置起始角度
5. 顺时针拖动鼠标 → Ghost 预览显示顺时针旋转
6. 点击确认
7. **结果**：模块逆时针旋转，与预览方向相反

---

## 深入根因分析 (Codex)

> **核心结论**：这不是简单的"取反逻辑被移除"问题，而是 **deltaRotation 在代码里同时被当成了两种不同语义的角度**：
> - 鼠标交互角（屏幕顺时针为正，CW+）
> - 数据模型角（2D 数学逆时针为正，CCW+）

### 三套角度语义

| 角度类型 | 定义来源 | 正方向 | 代码位置 |
|----------|----------|--------|----------|
| **数据模型角** | 2D 坐标系 Y 向上 | CCW（逆时针） | `coordinates.ts` line 4, 147 |
| **交互角** | `atan2(vector.z, vector.x)` | CW（顺时针） | `RotateTool.ts` line 311, 345 |
| **Three.js 旋转** | `rotation.y` | CCW（从上往下看） | Three.js 约定 |

### 坐标映射导致的语义分裂

**关键映射**：`Data(x, y) → World(x, 0, -y)`（coordinates.ts line 33）

- 这个 `y → -z` **本质是镜像操作**，会翻转 CCW/CW
- 屏幕"向下"对应 +Z，所以用户顺时针拖动时 `atan2(z, x)` 角度差变大（CW+）
- 但数据模型使用的 `rotatePoint2D` 期望 CCW+ 的输入

### 三条链路的角度处理

```
用户顺时针拖动（假设起始在 +X，拖到 +Z）
    ↓
startAngle = 0, endAngle ≈ +π/2
deltaRotation = +π/2 (CW+ 交互角)
    ↓
┌─────────────────────────────────────────────────────────────┐
│ Ghost 预览链路                                               │
│ rotation.y = -deltaRotation = -π/2                          │
│ Three.js 中 -π/2 表现为顺时针 → 预览顺时针 ✓                   │
└─────────────────────────────────────────────────────────────┘
┌─────────────────────────────────────────────────────────────┐
│ 数据更新链路                                                 │
│ rotatePoint2D(..., +π/2)                                    │
│ 2D 数学中 +π/2 是 CCW → 结果逆时针 ✗                          │
└─────────────────────────────────────────────────────────────┘
```

### 为什么"预览对、结果反"

| 链路 | 输入角度 | 内部转换 | 角度语义 | 实际效果 |
|------|----------|----------|----------|----------|
| Ghost 预览 | `+π/2` (CW+) | `rotation.y = -rotation` | 转成 CCW+ | 顺时针 ✓ |
| 数据更新 | `+π/2` (CW+) | 无转换 | 直接当 CCW+ 用 | 逆时针 ✗ |

---

## Claude 的初步分析（作为参考）

### 时间线追溯

| 提交 | 日期 | 改动描述 | 状态 |
|------|------|----------|------|
| `ebd45a7` | 2025-12-18 | 功能：修复旋转方向一致性 | ✓ 正常工作 |
| `88faf08` | 2025-12-21 | feat: Revit风格交互增强 | ✗ 引入此 bug |

### 引入 Bug 的提交分析

**提交**: `88faf08`
**信息**: "feat: Revit风格交互增强 - 键盘数值输入 + 临时标注"
**问题改动**: 在此提交中，`executeRotate()` 方法的 deltaRotation 计算被修改

```diff
- const deltaRotation = -(endAngle - this.startAngle); // Negate for 2D Math compatibility
+ const deltaRotation = endAngle - this.startAngle; // 与 Ghost 预览保持一致
```

### 技术分析

**数据流对比**：

| 阶段 | Ghost 预览 | 数据更新 | 一致性 |
|------|-----------|----------|--------|
| ebd45a7 之后 | `-deltaRotation` | `-deltaRotation` | ✓ 一致 |
| 88faf08 之后 | `-deltaRotation` | `deltaRotation` | ✗ 相反 |

**核心矛盾**：
- `GhostManager.setRotation()` 内部对 rotation 取反：`ghostGroup.rotation.y = -rotation`
- `executeRotate()` 被错误修改为不取反
- 导致 Ghost 预览方向正确，但实际数据更新方向相反

---

## 影响范围

| 功能 | 影响程度 |
|------|---------|
| 单个模块旋转 | 严重 - 方向相反 |
| 多选批量旋转 | 严重 - 方向相反 |
| 数字键精确输入旋转 | 严重 - 方向相反 |

---

## 相关文件

| 文件路径 | 相关代码位置 | 说明 |
|----------|-------------|------|
| `BIMCanvas.Web/src/services/interaction/tools/RotateTool.ts` | 第 465 行 | deltaRotation 计算 |
| `BIMCanvas.Web/src/services/interaction/GhostManager.ts` | 第 239-241 行 | setRotation 取反逻辑 |
| `BIMCanvas.Web/src/utils/coordinates.ts` | rotatePoint2D, rotateFacing2D | 2D 旋转函数 |

---

## 相关提交详情

### 提交 ebd45a7 (正确实现)
```
功能：修复旋转方向一致性 - Preview 和 Result 都逆时针旋转，匹配用户手势
```

### 提交 88faf08 (引入 bug)
```
feat: Revit风格交互增强 - 键盘数值输入 + 临时标注

- 新增 NumericInputManager 单例管理键盘数值输入
- 新增 FloatingInput 浮动输入框组件（跟随鼠标位置）
- MoveTool: 添加距离标注 + 数字键精确输入距离(mm)
- RotateTool: 添加角度标注 + 数字键精确输入角度(°)
- ShortcutManager: 数值输入激活时暂停快捷键处理
- 修复旋转方向与Ghost预览不一致的问题  ← 此行描述与实际效果相反
```

---

## 待讨论问题

1. **修复策略选择**：恢复 `executeRotate()` 的取反 vs 移除 `GhostManager` 的取反？
2. **数字键输入**：`applyNumericRotate()` 是否也需要同步修改？
3. **测试覆盖**：是否需要添加旋转方向的自动化测试？
4. **其他工具影响**：移动、镜像命令是否有类似问题？

---

## 修复记录

**修复日期**: 2025-12-23
**修复策略**: 恢复交互角到模型角的转换逻辑

### 修改 1: `executeRotate()` 恢复取反

**文件**: `BIMCanvas.Web/src/services/interaction/tools/RotateTool.ts`
**位置**: 第 465-467 行

```typescript
// 修复前
const deltaRotation = endAngle - this.startAngle;

// 修复后
// 交互角(CW+) 需要取反转换为 模型角(CCW+)
// GhostManager.setRotation() 内部也做了取反，所以预览和结果方向一致
const deltaRotation = -(endAngle - this.startAngle);
```

### 修改 2: `applyNumericRotate()` 适配取反逻辑

**文件**: `BIMCanvas.Web/src/services/interaction/tools/RotateTool.ts`
**位置**: 第 446-465 行

```typescript
// 修复前
const endAngle = this.startAngle + radians;

// 修复后
// 注意：startAngle 是交互角(CW+)，用户输入是模型角(CCW+)
// 取反 radians 以补偿 executeRotate 中的取反
const endAngle = this.startAngle - radians;
```

### 修复原理

1. `executeRotate()` 中的 `deltaRotation` 来自 `atan2(z, x)`，是交互角（CW+）
2. `GhostManager.setRotation()` 对输入取反：`rotation.y = -rotation`
3. `rotatePoint2D()` 期望模型角（CCW+）
4. 为保持预览和结果一致，`executeRotate()` 也需要取反
5. `applyNumericRotate()` 用户输入的是模型角（CCW+），需要补偿 `executeRotate()` 的取反

---

**报告人**: Claude Code
**调查方法**: Git 历史追溯 + 代码静态分析 + Codex 深入分析
