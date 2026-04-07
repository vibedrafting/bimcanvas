---
name: generate-derived-placement
description: |
  Generate 推导布置 Skill。负责把推导规划产出的语义方案图纸（planType=derived）
  转成 modules.json，并执行验证、品质优化和最终汇报。
---

# Generate 推导布置

> 你在本 Skill 中是施工方兼品质把关人。图纸是你自己（或同系统）设计的，你有权在施工中优化自己的作品。

---

## 1. 执行模式

进入本 Skill 后，先确认执行模式：

- **交互模式**：当前可用工具包含 `AskUserQuestion`（主控 Agent 直接执行）
- **自主模式**：当前可用工具不包含 `AskUserQuestion`（layout-agent 执行）

后续所有需要用户确认的节点，统一按当前模式处理：

- 交互模式 → 提问确认
- 自主模式 → 选择当前推荐方案继续，标记"自动代决"

---

## 2. 入场动作

**【必须】**进入本 Skill 后第一步调用：

```text
load_semantic_plan({ zoneId })
```

只接受：

- `status = ok` 且 `planType = derived` → 继续
- `status = missing` → 停止，说明未找到图纸
- `status = ambiguous_legacy` → 停止，说明旧图纸不可自动判定
- `planType ≠ derived` → 停止，说明图纸类型不匹配

读取后必须显式复述：

- `planType` 与 `effectiveVersion`
- 关键家具墙面归属
- 若有"自动代决"标记，也必须复述

如果当前设计区含 `subZones`：

- 图纸仍从父设计区 `zoneId` 读取
- 实际写入目标是子分区的 `modules.json`
- `validate_layout` 使用子分区 `zoneIds`

---

## 3. 施工前读取

- `references/design_principles.md`
- `references/design_evaluation.md`
- `modules/module_library.json`
- `schemes/zones.json`
- 当前 `modules.json`（若已存在）
- 对应房间策略文件：`references/bedroom.md` / `references/bathroom.md` / `references/livingroom.md`

---

## 4. 按图施工

以 `semantic_plan` 为蓝图，结合房间策略和设计原则进行坐标化施工。

**【必须】**一次性写入完整结果，再调用 `mcp__canvas__validate_layout`。

---

## 5. 修正循环

验证失败时按优先级修正：

`平移 → 旋转 → 缩小 → 拆除附属件 → 替换 → 移除`

遇到战略性取舍（会显著影响日常体验的多方案选择），按执行模式处理。

---

## 6. Layer 1 验证

`validate_layout` 通过后，继续做：

- 可达性验证
- 功能完整性验证

任何一项失败，继续修正并重新验证。

---

## 7. 优化尾段

1. 调用截图工具审查结果
2. 参照 `design_evaluation.md` 做品质复核
3. 每个维度最多尝试一次改善
4. 改善后再次 `validate_layout`

---

## 8. 汇报

最终汇报必须包含：

- 施工依据：`planType=derived` + `effectiveVersion`
- 放置结果：家具、墙面、朝向
- 验证结果：布局验证 + 可达性 + 功能完整性
- 优化结果：哪些维度做了改善，哪些跳过
- 若发生自动兜底：`自动代决` 项

---

## 约束总览

**【硬约束】**

- 入场必须 `load_semantic_plan`，仅接受 `planType=derived`
- 一次性写入后必须 `validate_layout`
- 不编造家具尺寸

**【软指导】**

- 修正优先级：平移 → 旋转 → 缩小 → 拆除附属件 → 替换 → 移除
- 优化尾段每维度最多一次改善
- 战略选择交互模式下应询问用户

**【自由区域】**

- 家具间精确间距
- 附属件精确位置
- 模块参数化尺寸的精确值（在 limits 范围内）
- 坐标计算方式
- 优化策略选择
