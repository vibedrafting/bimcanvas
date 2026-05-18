---
name: edit-workflow
description: |
  BIMCanvas 通用机械编辑工作流（core-base 简化版）。
  当用户需要"移动"、"删除"、"旋转"等单一机械修改操作时使用此工作流。
  本简化版不带任何 domain 决策智能；如需参数化尺寸推理或房间策略，请安装对应 domain plugin。
allowed-tools: Read, Write, Edit, Glob, Grep, mcp__canvas__validate_layout, mcp__canvas__save_modules, mcp__canvas__request_background_screenshot, AskUserQuestion
---

# Edit 工作流（机械版）

> 本工作流只做"机械"动作：把现有模块移到指定位置、删除、旋转固定角度。
> **不做尺寸推理、不读 reference 设计规则、不基于 module_library 决策**——这些能力由 domain plugin 提供（如室内布置类 plugin）。

**触发条件**：关键词"移动 / 删除 / 旋转 / 移到 / 改到"。

---

## 必读集（仅 2 项）

按动作类型最小化读取：

| 动作 | 必读 |
|------|------|
| 删除 | `schemes/zones.json` + 目标 `modules.json` |
| 旋转 N 度 | `schemes/zones.json` + 目标 `modules.json` |
| 移动到精确坐标 / 精确语义 | `schemes/zones.json` + 目标 `modules.json` |

**跳过**：`references/*.md`、`module_library.json`、修改前后截图。

**WHY**：core-base 不持有 domain 决策表，只做"AABB → 写位置"的机械翻译。

---

## 流程

1. 读必读集（按上表）。
2. 按动作类型计算新 bounds / facing / 旋转角，**不做尺寸推理**：
   - 移动：保持原 size，仅平移 bounds。
   - 旋转：保持原 size，按用户指定角度旋转 bounds 顶点。
   - 删除：从 modules 数组移除目标项。
3. **碰撞规避（仅在用户明确要求时）**：检查目标位置是否与同 zone 其他模块 AABB 重叠，若重叠 → 沿用户意图方向最小平移至无冲突。
4. 调 `mcp__canvas__save_modules({designZoneId, leafZoneId, modules})`。
5. 调 `mcp__canvas__validate_layout()` 兜底碰撞 / 边界 / 禁区。
   - 通过 → 完成。
   - 失败 → 报告失败原因，**不自动重试**（避免无依据的连续猜测）；若用户继续指示再迭代。

**【必须】**通过 `mcp__canvas__save_modules` 写 modules;**禁止用 Write 工具直写 modules.json**。schemeMetadata 由 Server 派生。

---

## 用户输入要求

core-base 的 edit 工作流**依赖用户提供明确的目标位置**:
- ✓ AABB（左下 + 右上坐标）
- ✓ 精确语义（"靠 X 墙"、"X 旁边"、"X 上面 N mm"）
- ✓ 精确坐标差（"向东移动 500mm"）

若用户输入模糊（"调整一下" / "看起来不舒服" / "优化布局"）：
- 不要凭直觉决策——本简化版工作流不持有任何 domain 设计规则
- 通过 `AskUserQuestion` 请用户给出明确目标位置（ID / 坐标 / 偏移量）
- 若用户意图本身需要"设计判断"（如优化、推荐），把指令交回主控由 active domain plugin 处理；无 plugin 时主控会引导用户安装对应领域 plugin

---

## facing 字段约定

- `value`：数值方向向量（运行时真理）。
- `semantic`：可选输入槽（"north"/"south"/"east"/"west" 等）。
- 同时存在时读取只认 `value`；`validate_layout` 会用有效 `semantic` 覆盖 `value` 并清空 `semantic`。
- **推荐**：默认写 `semantic`，由 validate 归一化为 `value`。
