# 提示词文件结构索引

> 审查前阅读此文件，快速了解四个被审查文件的当前结构和互联关系。

## 文件清单

| # | 文件 | 路径 | 角色 |
|---|------|------|------|
| 1 | BIMCANVAS.md | `BIMCanvas.Agent/templates/BIMCANVAS.md` | 系统身份 + 执行规范 |
| 2 | generate SKILL.md | `BIMCanvas.Agent/templates/skills/generate-workflow/SKILL.md` | 执行流程卡 |
| 3 | placement_guide.md | `BIMCanvas.Server/Templates/knowledge/placement_guide.md` | 设计规范手册 |
| 4 | module_library.json | `BIMCanvas.Server/Templates/modules/module_library.json` | 家具模块规格 |

## 文件间引用关系

```
BIMCANVAS.md
  ├── 定义任务类型 → 触发 generate-workflow SKILL.md
  └── 定义禁止事项（全局生效）

generate SKILL.md
  ├── 步骤 2 → 引用 placement_guide.md
  ├── 步骤 4 → 引用 module_library.json
  ├── §5.6 → 引用 placement_guide §6.4（衣柜选墙）
  ├── §6A → 引用 placement_guide §4.1（通道标准）、§5.2/§5.3（间距分配）
  └── 检查清单 S1-S9 → 多项引用 placement_guide 章节

placement_guide.md
  ├── §9.2 自检清单 → 与 SKILL.md 检查清单部分重叠
  └── 各章节规则 ← 被 module_library relation_rules 细化

module_library.json
  └── 各模块 agent_config.relation_rules ← 细化 placement_guide 的通用规则
```

## 职责边界速查

| 文件 | 应该有 | 不应该有 |
|------|--------|----------|
| BIMCANVAS.md | 身份、执行规范、禁止事项、任务分类 | 流程步骤、设计数值、模块尺寸 |
| generate SKILL.md | 步骤序列、入口/出口条件、检查清单、误判提醒 | 设计标准数值、房间设计经验 |
| placement_guide.md | 设计原则、房间布局规范、通道标准、布局方法论 | 模块尺寸约束、工作流步骤 |
| module_library.json | 模块尺寸/参数化、topology_rules、relation_rules | 房间整体策略、工作流步骤 |

## 关键规则跨文件追踪表

审查跨文件一致性时，优先检查以下高频同步项：

| 规则 | generate SKILL.md | placement_guide | module_library |
|------|-------------------|-----------------|----------------|
| 衣柜穷举墙面 | §5.6, S3 | §5.2, §7.4 | mod_cabinet_1500, mod_cabinet_custom_001 |
| 间距分配 | §6A.2 | §5.2, §5.3 | mod_bed_001 relation_rules |
| 侧对窗户 | S4 | §5.2, §5.3, §6.1 | mod_bed_001 relation_rules |
| 通道分类 | §6A.1, H5 | §4.1 | — |
| 床头柜成套 | S5 | §7.1 | mod_cabinet_001 relation_rules |
| 顶角规则 | S7 | §1 | mod_cabinet_custom_001, mod_cabinet_1500 |
