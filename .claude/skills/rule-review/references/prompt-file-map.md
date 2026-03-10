# 提示词文件结构索引

> 审查前阅读此文件，快速了解四个被审查文件的当前结构和互联关系。

## 文件清单

| # | 文件 | 路径 | 角色 |
|---|------|------|------|
| 1 | BIMCANVAS.md | `BIMCanvas.Agent/templates/BIMCANVAS.md` | 系统身份 + 执行规范 |
| 2 | generate SKILL.md | `BIMCanvas.Agent/templates/skills/generate-workflow/SKILL.md` | 执行流程卡 |
| 3 | design_principles.md | `BIMCanvas.Server/Templates/knowledge/design_principles.md` | 跨房间通用设计原则 |
| 3b | 房间 Skills | `BIMCanvas.Agent/templates/skills/generate-{bedroom,bathroom,livingroom}/SKILL.md` | 房间专属策略 |
| 4 | module_library.json | `BIMCanvas.Server/Templates/modules/module_library.json` | 家具模块规格 |

## 文件间引用关系

```
BIMCANVAS.md
  ├── 定义任务类型 → 触发 generate-workflow SKILL.md
  ├── 定义禁止事项（全局生效）
  └── AskUserQuestion 核心原则（原则层：Why+What）

generate SKILL.md
  ├── 感知阶段 → 引用 design_principles.md
  ├── 理解阶段 → 按房间类型加载对应房间 Skill
  ├── 步骤 3 → 引用 module_library.json
  └── AskUserQuestion 使用规范（执行层：When+How）

design_principles.md
  └── 跨房间通用原则 ← 被各房间 Skill 特化

房间 Skills (generate-bedroom/bathroom/livingroom)
  └── 房间专属策略、决策树、示例

module_library.json
  └── 各模块 agent_config.relation_rules ← 细化 design_principles 的通用规则
```

## 职责边界速查

| 文件 | 应该有 | 不应该有 |
|------|--------|----------|
| BIMCANVAS.md | 身份、执行规范、禁止事项、任务分类、AskUserQuestion 原则+判断 | 流程步骤、设计数值、模块尺寸 |
| generate SKILL.md | 五阶段流程框架、Skill 加载映射、AskUserQuestion 执行+格式 | 设计标准数值、房间设计经验 |
| design_principles.md | 跨房间通用设计原则、通道标准 | 房间专属策略、工作流步骤 |
| 房间 Skills | 房间专属策略、决策树、示例锚定 | 跨房间通用原则、工作流步骤 |
| module_library.json | 模块尺寸/参数化、topology_rules、relation_rules | 房间整体策略、工作流步骤 |

## 关键规则跨文件追踪表

审查跨文件一致性时，优先检查以下高频同步项：

| 规则 | generate SKILL.md | design_principles / 房间 Skill | module_library |
|------|-------------------|-----------------|----------------|
| 衣柜穷举墙面 | 6A Step 3b, S3 | §7.4 | mod_cabinet_1500, mod_cabinet_custom_001 |
| 间距分配 | 6A Step 3 | §7.6 | mod_bed_001 relation_rules |
| 侧对窗户 | S4 | §7.3, §7.6 | mod_bed_001 relation_rules |
| 通道分类 | 6A Step 2, H5 | §4.1 | — |
| 床头柜成套 | S5 | §7.1 | mod_cabinet_001 relation_rules |
| 顶角规则 | S7 | §1 | mod_cabinet_custom_001, mod_cabinet_1500, mod_display_cabinet_custom_001 |
| 子空间组合 | 6.4 Q2, 常见错误表 | §1.3 | mod_display_cabinet_custom_001 |
| 必须家具优先 | 6B Step 3b | §5.2 | — |
