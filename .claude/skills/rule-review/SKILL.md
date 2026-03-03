---
name: rule-review
description: |
  BIMCanvas Agent 提示词体系审查与优化工作流。
  当用户需要审查、评估或优化 Agent 端提示词设计时使用。
  触发场景：(1) 提示词审查/提示词 review (2) 审查提示词体系/全面审查
  (3) 检查刚修改的提示词/检查 diff (4) 提示词优化建议 (5) 评估提示词质量
  (6) 提示词变更影响分析。
  包含：两种审查模式（全面审查/局部变更）、六原则检查清单、
  职责边界验证、跨文件一致性检查、改进建议生成。
---

## §1 审查体系概览

### 被审查文件（4 个）

| 文件 | 路径 | 角色 |
|------|------|------|
| 系统提示词 | `BIMCanvas.Agent/templates/BIMCANVAS.md` | 身份 + 规范 |
| 执行工作流 | `BIMCanvas.Agent/templates/skills/generate-workflow/SKILL.md` | 流程卡 |
| 设计指南 | `BIMCanvas.Server/Templates/knowledge/placement_guide.md` | 规范手册 |
| 家具规则库 | `BIMCanvas.Server/Templates/modules/module_library.json` | 模块规格 |

### 审查依据

Read `docs/Prompt_Design_Philosophy.md`（执行前**必须**读取）。
核心内容：§2 六原则、§3 职责边界、§5 演进方向、§6 反模式清单。

### 两种模式

- **模式 A（全面审查）**：逐文件、逐原则检查 → 生成审查报告 → 见 §3
- **模式 B（局部变更）**：分析 diff → 检查合规性 + 影响面 → 生成变更评审 → 见 §4

---

## §2 通用准备步骤

### 步骤 1：读取设计哲学文档（必须）

Read `docs/Prompt_Design_Philosophy.md`
→ 重点：§2 六原则定义、§3 职责边界、§6 反模式清单

### 步骤 2：读取文件结构索引（必须）

Read [references/prompt-file-map.md](references/prompt-file-map.md)
→ 了解四文件的职责边界、引用关系、跨文件规则追踪表

### 步骤 3：读取审查清单（必须）

Read [references/review-checklist.md](references/review-checklist.md)
→ 加载六原则逐项检查清单 C1.1 ~ C7.2

### 模式判断

| 用户输入特征 | 模式 |
|-------------|------|
| 提供了 git diff 或说"刚改了 XX" | 模式 B（局部变更） |
| "全面审查" / "审查提示词体系" | 模式 A（全面审查） |
| 未明确说明 | 询问用户意图 |

---

## §3 模式 A：全面审查工作流

### A1. 逐文件阅读

按以下顺序读取，每读完一个文件记录初步观察：

1. Read `BIMCanvas.Agent/templates/BIMCANVAS.md`
2. Read `BIMCanvas.Agent/templates/skills/generate-workflow/SKILL.md`
3. Read `BIMCanvas.Server/Templates/knowledge/placement_guide.md`
4. Read `BIMCanvas.Server/Templates/modules/module_library.json`

### A2. 逐原则检查

对照 `references/review-checklist.md`，逐项检查。按原则（而非按文件）组织检查，以便跨文件发现同一原则的违反模式：

| 原则 | 检查项 | 主要关注文件 |
|------|--------|-------------|
| 结构化分层 | C1.1-C1.3 | 四个文件均检查 |
| 示例代替描述 | C2.1-C2.2 | placement_guide, module_library |
| 禁止项优先 | C3.1-C3.2 | BIMCANVAS.md, SKILL.md |
| 提供原因 | C4.1-C4.2 | placement_guide, module_library |
| 渐进信任 | C5.1-C5.2 | 三个规则文件 |
| 信噪比 | C6.1-C6.3 | 四个文件均检查 |

### A3. 职责边界验证

对照 `docs/Prompt_Design_Philosophy.md` §3 和 `references/prompt-file-map.md` 职责边界表：

- [ ] SKILL.md 中是否包含了应属于 placement_guide 的设计标准数值？
- [ ] placement_guide 中是否包含了应属于 SKILL.md 的工作流步骤？
- [ ] module_library 中是否包含了应属于 placement_guide 的通用设计原则？
- [ ] 同一规则是否在多个文件中重复表述？（列出具体条目）
- [ ] BIMCANVAS.md 是否保持了纯"身份 + 规范"定位？

### A4. 跨文件一致性检查

对照 `references/prompt-file-map.md` 跨文件追踪表：

1. 选取表中 6 条高频同步项（衣柜选墙、间距分配、侧对窗户、通道分类、床头柜成套、顶角规则）
2. 追踪每条规则在各文件中的表述
3. 标记不一致之处（用词差异、数值差异、适用范围差异）

### A5. 生成审查报告

按以下格式输出：

```
## 审查报告

### 总体评估
- 体系成熟度：[评级 + 一句话]
- 最大优势：[...]
- 最大风险：[...]

### 问题清单（按严重程度排序）

| # | 严重度 | 文件 | 位置 | 问题描述 | 建议修复 |
|---|--------|------|------|---------|---------|
| 1 | 高 | ... | §X.X | ... | ... |

严重度定义：
- 高：可能导致 Agent 错误决策（规则冲突、关键缺失）
- 中：降低提示词效率（冗余、职责越界）
- 低：风格/一致性问题（用词不统一、格式不规范）

### 改进建议（按优先级排序）
1. [建议]：涉及文件 + 预期效果
```

---

## §4 模式 B：局部变更检查工作流

### B1. 获取变更内容

两种方式（任选）：
- 用户直接提供 diff 文本
- 用户告知修改了哪个文件 → 执行 `git diff HEAD~1 -- <file_path>` 获取

### B2. 变更归类

| 变更类型 | 判断依据 | 检查重点 |
|---------|---------|---------|
| 新增规则 | diff 中有纯新增内容 | §6 新增规则 5 问 |
| 修改规则 | diff 中有替换内容 | 语义变化 + 跨文件同步 |
| 删除规则 | diff 中有纯删除内容 | 删除后是否留下引用悬空 |
| 结构调整 | 章节移动/合并/拆分 | 职责边界是否仍正确 |

### B3. 对照设计原则检查

1. **职责归属**：变更内容是否放在了正确的文件中？（对照 §3 职责边界）
2. **六原则合规**：从 `references/review-checklist.md` 中选取与变更相关的检查项执行
3. **新增规则 5 问**（仅新增规则时，引自 `docs/Prompt_Design_Philosophy.md` §6）：
   - 解决的是已发生的问题还是假想问题？
   - 放在了正确的文件中吗？
   - 去掉这条规则，Agent 会犯错吗？
   - 与现有规则有冲突吗？
   - 需要解释原因吗？

### B4. 跨文件影响分析

对照 `references/prompt-file-map.md` 跨文件追踪表和引用关系图，判断哪些文件需要同步：

| 变更了 | 检查同步 |
|--------|---------|
| placement_guide | SKILL.md 检查清单 + module_library relation_rules |
| module_library | placement_guide 的相关规则描述 |
| generate SKILL.md | placement_guide 的自检清单（§9.2） |
| BIMCANVAS.md | 检查禁止事项是否与其他文件矛盾 |

读取可能受影响的关联文件，确认是否需要同步更新。

### B5. 生成变更评审

```
## 变更评审

### 变更概要
- 文件：[路径]
- 类型：[新增/修改/删除/结构调整]
- 摘要：[一句话]

### 合规检查

| 检查项 | 结果 | 说明 |
|--------|------|------|
| 职责归属正确 | PASS/FAIL | ... |
| 无规则冲突 | PASS/FAIL | ... |
| 跨文件一致 | PASS/FAIL/需同步 | ... |
| 信噪比合理 | PASS/FAIL | ... |

### 需要同步的文件（如有）

| 文件 | 具体位置 | 需要做什么 |
|------|---------|-----------|

### 改进建议（如有）
1. ...
```

---

## §5 审查质量准则

1. **问题必须具体**：指出文件名、章节号、具体内容，不要笼统说"结构不够清晰"
2. **建议必须可执行**：说明"把 X 从 A 文件移到 B 文件的 §Y 章节"，而非"建议优化结构"
3. **区分"不好"与"不同"**：审查者的偏好不等于设计缺陷，以六原则为判断标准
4. **关注实际影响**：优先报告可能导致 Agent 错误决策的问题，风格问题其次
5. **不替代 rule-tuning**：发现具体布置规则有误时，建议用户启动 `/rule-tuning` 工作流处理

---

## §6 参考资料（按需读取）

| 资料 | 路径 | 何时读取 |
|------|------|---------|
| 审查清单 | [references/review-checklist.md](references/review-checklist.md) | 准备阶段（必读） |
| 文件结构索引 | [references/prompt-file-map.md](references/prompt-file-map.md) | 准备阶段（必读） |
| 设计哲学文档 | `docs/Prompt_Design_Philosophy.md` | 准备阶段（必读） |
| 规则体系地图 | `.claude/skills/rule-tuning/references/rule-system-map.md` | 需要理解规则注入机制时 |
| 历史调优案例 | `.claude/skills/rule-tuning/references/case-studies.md` | 需要了解规则演进脉络时 |
