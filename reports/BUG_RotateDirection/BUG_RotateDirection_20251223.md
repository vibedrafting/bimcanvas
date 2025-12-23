# Bug Report: 旋转命令方向与用户期望相反

**报告日期**: 2025-12-23
**严重程度**: 高
**状态**: 已定位根因，待讨论修复方案
**影响范围**: Web 项目旋转命令

---

## 问题描述

用户执行旋转命令时，实际旋转方向与预期相反：
- 用户顺时针拖动鼠标 → 预览（Ghost）显示顺时针旋转 ✓
- 用户确认操作 → 实际结果逆时针旋转 ✗

**复现步骤**：
1. 选择一个模块
2. 激活旋转命令
3. 设置旋转中心
4. 设置起始角度
5. 顺时针拖动鼠标 → Ghost 预览显示顺时针旋转
6. 点击确认
7. **结果**：模块逆时针旋转，与预览方向相反

---

## Claude认为的问题根因

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

**报告人**: Claude Code
**调查方法**: Git 历史追溯 + 代码静态分析
