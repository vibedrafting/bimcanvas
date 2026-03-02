# 规则体系地图

> 架构详解：`docs/Agent_Workflows.md`

## Agent 系统提示词

| 文件 | 源码路径 | 内容 |
|------|---------|------|
| MainAgent 提示词 | `BIMCanvas.Agent/templates/BIMCANVAS.md` | Agent 身份、任务分类、交互规范、工具优先级 |

## 规则注入的 3 个文件（调优直接目标）

| 注入点 | 源码路径 | 运行时路径 | 关键章节 |
|--------|---------|-----------|---------|
| **设计指南** | `BIMCanvas.Server/Templates/knowledge/placement_guide.md` | `{project}/knowledge/placement_guide.md` | §4.1 通道标准、§5 房间布置要点、§6 朝向逻辑 |
| **执行工作流** | `BIMCanvas.Agent/templates/skills/generate-workflow/SKILL.md` | `~/.bimcanvas/skills/generate-workflow/SKILL.md` | 6A 预检、8A/8B 检查清单(H4-S9)、误判提示 |
| **家具规则** | `BIMCanvas.Server/Templates/modules/module_library.json` | `{project}/modules/module_library.json` | agent_config.topology_rules + relation_rules |

## 其他 Skills（较少需要调优）

- `BIMCanvas.Agent/templates/skills/query-workflow/SKILL.md` — 只读查询
- `BIMCanvas.Agent/templates/skills/edit-workflow/SKILL.md` — 单一修改

## 数据流：规则 → Agent 决策

```
用户请求 "布置次卧"
  │
  ▼
MainAgent 加载 generate-workflow SKILL.md
  │  ← 定义执行步骤、预检规则、检查清单
  ▼
步骤2: 读取 placement_guide.md
  │  ← 通道标准(§4.1)、房间布置要点(§5)、朝向逻辑(§6)
  ▼
步骤4: 读取 module_library.json
  │  ← 家具尺寸 + agent_config（topology_rules + relation_rules）
  ▼
步骤6A: 综合三者做出布置决策
  │  → placement_guide 提供标准值
  │  → relation_rules 提供家具特定约束
  │  → SKILL.md 提供预检规则和误判提示
  ▼
写入 → validate_layout → 截图审查(检查清单 H4-S9)
```
