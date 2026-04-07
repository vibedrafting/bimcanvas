---
name: generate-placement
description: |
  Generate 布置 Skill。负责把语义方案图纸转成 modules.json，并执行验证、优化尾段和最终汇报。
---

# Generate 布置

> 你在本 Skill 中是施工方。第一职责不是重新设计，而是先读取图纸，再按图施工。

## 1. 入场动作

**【必须】**进入本 Skill 后第一步调用：

```text
load_semantic_plan({ zoneId })
```

只接受以下状态：

- `status = ok` -> 继续
- `status = missing` -> 停止，说明未找到图纸
- `status = ambiguous_legacy` -> 停止，说明旧图纸不可自动判定

读取后必须显式复述：

- `planType`
- `effectiveVersion`
- 关键家具墙面归属
- 若 `planType = reference`：图纸中的完整家具清单与保留空段
- 若有“自动适配”或“自动改图纸”，也必须复述

若 `planType = reference`，复述内容还必须包含每一项的墙面/角部/朝向。若任何一项仍不足以唯一定位到具体墙段或角部，则不得直接施工：

- 当前执行者具备 `AskUserQuestion`：必须先提问
- 当前执行者不具备 `AskUserQuestion`：只能在最终结果中标记为 `自动改图纸`

---

## 2. 施工前读取

读取以下施工规范与数据：

- `references/design_principles.md`
- `modules/module_library.json`
- `schemes/zones.json`
- 当前 `modules.json`（若已存在）
- 对应房间策略文件：
  - `references/bedroom.md`
  - `references/bathroom.md`
  - `references/livingroom.md`

若 `planType = derived`，额外读取：

- `references/design_evaluation.md`

若 `planType = reference`，不读取 `references/design_evaluation.md`

如果当前设计区含 `subZones`：

- 图纸仍从父设计区 `zoneId` 读取
- 实际写入目标是子分区的 `modules.json`
- `validate_layout` 使用子分区 `zoneIds`

---

## 3. Stage 3 布置与验证

### 3.1 按图施工

- 图纸决定：哪面墙、朝哪个方向、需要哪些家具，哪些空段必须保留
- 施工规范决定：在该墙面上如何选模块、如何计算坐标、如何修正

若 `planType = reference`，规则优先级固定为：

1. `semantic_plan` 中的家具清单与保留空段
2. `validate_layout` 等几何硬约束
3. 房间施工规则
4. derived 默认偏好

**【必须】**当 `semantic_plan` 与 `bedroom.md` / `design_principles.md` 的默认建议冲突时，`reference` 模式优先服从图纸；知识库只用于施工计算与合法性修正，不得把图纸改写成 derived 默认方案。

`bedroom.md` 和 `design_principles.md` 在 `reference` 模式下只负责“怎么合法落地”，不负责“决定要不要换位置、补家具、吞留白”。

**【必须】**不得添加 `v0.2` 中没有的家具或附属件。

**【必须】**不得删除 `v0.2` 中已有家具；若确实无法落地，只能走“提问确认”或“自动改图纸”。

**【必须】**不得侵占 `v0.2` 中记录的保留空段。

**【必须】**保留空段按一级对象处理，必须先从目标墙段中扣除保留空段，再做模块选型。

**【必须】**若 `v0.2` 为家具指定了角部或邻接关系，施工时必须保留该关系，不能在同一面墙上自由换端点。

`reference` 快路径：

1. 复述图纸中的家具清单与保留空段
2. 先从目标墙段中扣除保留空段
3. 再逐项施工家具
4. `validate_layout`
5. 若无偏离，直接汇报

反例：

- 图纸里没有床头柜，却自动补双床头柜
- 图纸里有门侧留白，却把衣柜排满整墙
- 图纸里有梳妆台在衣柜旁暗角，却改到窗边
- 图纸里有梳妆台，却因为衣柜更完整而吞掉梳妆台

**【必须】**一次性写入完整结果，再调用 `mcp__canvas__validate_layout`。

### 3.2 修正循环

验证失败时按优先级修正：

`平移 -> 旋转 -> 缩小 -> 拆除附属件 -> 替换 -> 移除`

跨墙面迁移、删除图纸家具、侵占图纸保留空段、把家具从指定角部/邻接位置改到同墙另一端都等于修改图纸：

- 当前执行者具备 `AskUserQuestion`：必须先询问用户
- 当前执行者不具备 `AskUserQuestion`：选择“最可施工”的替代墙面或更小组合继续落地，并在汇报中显式标记“自动改图纸”

### 3.3 Layer 1

`validate_layout` 通过后，继续做：

- 可达性验证
- 功能完整性验证

任何一项失败，都要继续修正并重新验证。

---

## 4. 优化尾段

### derived

若 `planType = derived`：

1. 调用截图工具审查结果
2. 参照 `design_evaluation.md` 做品质复核
3. 每个维度最多尝试一次改善
4. 改善后再次 `validate_layout`

### reference

若 `planType = reference`：

- 默认跳过优化尾段，`validate_layout` 通过后直接汇报
- 只有当前执行者具备 `AskUserQuestion` 且用户明确允许偏离参考意图，才可做优化性调整
- 若当前执行者不具备 `AskUserQuestion`，一律不因“更美观”“更完整”“更整洁”“更合理”而主动偏离参考图纸
- 一律不因知识库中的 derived 默认而补双床头柜、拉满整墙衣柜、把梳妆台改到更亮位置

---

## 5. 汇报

最终汇报必须包含：

- 施工依据：`planType + effectiveVersion`
- 放置结果：家具、墙面、朝向、保留空段执行结果
- 验证结果：布局验证 + 可达性 + 功能完整性
- 若发生自动兜底：
  - `自动适配`（reference translation 阶段）
  - `自动改图纸`（placement 阶段）

reference 路径额外汇报：

- 识别摘要
- 适配说明
- 与原参考图存在的偏离点
