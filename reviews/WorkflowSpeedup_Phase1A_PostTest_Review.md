# WorkflowSpeedup_Phase1A_PostTest_Review

<!--
文件命名规范：[TopicName]_Review.md
版本：v2.0 (Integrated Discussion Guidelines)
-->

> [!IMPORTANT]
> **协作规则**
> 1. **追加式讨论**：所有新意见请以 `### [时间戳] [专家名]: [观点]` 格式追加在 "深入讨论" 章节。
> 2. **严禁修改**：禁止修改其他专家的已存档观点。
> 3. **优先级标注**：明确区分 `[Blocker]` (阻碍性) 与 `[Suggestion]` (建议性)。
> 4. **文本规范**：不要使用Emoji。
> 5. **时间戳**：必须使用真实的时间，Windows下使用：`$(powershell -Command "Get-Date -Format 'yyyyMMdd_HHmmss'")`获取真实时间

> [!TIP]
> **讨论原则**
>
> - **建设性**：反对时请提供替代方案。
> - **聚焦核心**：优先解决架构风险与数据一致性。
> - **拥抱共识**：寻找折中方案或最优解，避免无休止的争论。
> - **文档规范**：禁止删除模板文件中的Note
> - **格式规范**：禁止在"3. 深入讨论"追加讨论的内容中，使用标题格式，如 # 、## 、### ...

## 1. 议题概述

- **主题**：Phase 1A 测试复盘 -- 2.2->2.3 阶段边界失效的根因分析与修复方案讨论
- **发起时间**：2026-03-20
- **参与者**：用户（产品/架构决策者）、Claude（主持人/实施者/SDK 专家）、Antigravity（提示词认知专家）、Codex（工程验证专家）
- **背景信息**：

  **一、前序讨论回顾**

  在上一轮讨论（`reviews/WorkflowSpeedup_Review.md`）中，团队针对 generate-workflow 执行效率问题达成了核心共识：用结构性工具边界替代行为引导。具体地，新增 `save_semantic_plan` MCP 工具作为每个规划子阶段的"提交按钮"，创造物理性 turn 边界，目标将总耗时从 27 分钟降至 14 分钟以内。

  Phase 1A 的共识交付物包括：
  1. 极简版 `save_semantic_plan` MCP 工具（持久化 + turn 边界，不含 next_stage_hints）
  2. `generate-workflow/SKILL.md` 重写（删除无效行为引导、新增强制工具调用、改 Stage 3 为"快速草稿"协议）
  3. `generate-zoning/SKILL.md` 边界收紧
  4. `BIMCANVAS.md` 最小同步修改

  Phase 1A 的排除项：不动房间策略文件（bedroom.md 等）、不动 config.json 参数、不含预处理返回值、不含封套思维/Chunking。

  **二、Phase 1A 实施完成与测试结果**

  Phase 1A 改动已全部实施（commit `4161def`），并在同一测试场景（金凤127 / rz_3 主卧 / L 形空间）下完成了新一轮测试。

  测试参数：effort=low，thinking=adaptive，maxThinkingTokens=16000（与基线完全一致）。

  核心结果对比：

  | 指标 | 基线（改动前） | Phase 1A（改动后） | 变化 | 预期目标 |
  |------|--------------|-------------------|------|---------|
  | 总耗时 | 26:53 | 25:11 | -1:42 | <= 14 min |
  | thinking 总时长 | ~20:22 | ~18:14 | -2:08 | -- |
  | Stage 2.1 thinking | 3:47 | **1:46** | **-2:01** | <= 4 min |
  | Stage 2.2+2.3 thinking | 8:43 | **11:12** | **+2:29** | <= 4 min (per turn) |
  | Stage 3 thinking | 5:50 | 4:02 | -1:48 | <= 2 min |
  | validate_layout 错误 | 0 | 0 | 持平 | 持平 |
  | 设计目标达成 | 4/4 | 4/4 | 持平 | 持平 |
  | save_semantic_plan 调用 | 0 | 3 (v0.1/v0.2/v0.3) | +3 | 3 |

  **整体评价**：设计质量完全保持，但速度优化效果远低于预期（仅降低 1:42，目标 >= 12 分钟）。Phase 1A 的核心机制在 **2.1->2.2 边界处生效**（thinking 从 3:47 降至 1:46），但在 **2.2->2.3 边界处完全失效**（两阶段合并为 11:12 的单一 thinking block，比基线 8:43 还长）。

  **三、失败点详细分析**

  **(1) 2.2->2.3 阶段边界失效（最严重问题）**

  Stage 2.2 和 2.3 的全部分析发生在 19:55:01 -- 20:06:13 的单一 thinking block 中，历时 11:12 分钟。thinking 内容涵盖了两个阶段的完整工作：
  - Stage 2.2 应有的工作：床头墙候选对比、衣柜墙候选及通道宽度验算、三种分区方案预布置验证
  - Stage 2.3 应有的工作：梳妆台候选墙段扫描、衣柜 L 型 vs 线性 vs 梳妆台组合的深度分析、梳妆台深度 600mm vs 400mm 的开关间距权衡

  `save_semantic_plan(v0.2)` 并非由工作流自然节奏触发。它是在 API `max_output_tokens`（32000 token 上限）错误强制截断超长输出后，Agent 在下一轮 brief thinking 中才补提交的（20:06:37）。若无此错误，Agent 原计划在同一长输出中连续输出 Stage 2.2 分析后直接提交 v0.2，turn 边界的效果更弱。

  **(2) "位置暂定原则" WHY 主动诱导跨阶段思考**

  日志中可见，Agent 在 2.2 主家具分析时主动扫描了梳妆台候选位置（2.3 工作），并明确说明原因是"衣柜是否 L 型取决于梳妆台位置"。这是 `SKILL.md` 中"可选家具的加入可能发现更优的全局布局"规则的直接结果 -- 这条 WHY 创造了一个逻辑需求：必须分析 2.3 内容才能判断 2.2 是否完整。

  同时，v0.2 示例中的注释"衣柜是否 L 型延伸到东墙2 取决于梳妆台位置（2.3 决定）"作为最强锚定物，直接锚定了"v0.2 需要前瞻 2.3"的行为模式。

  **(3) 2.1->2.2 成功的原因对比分析**

  2.1->2.2 边界成功是因为存在**双重 turn 边界**：`save_semantic_plan(v0.1)` 提交 + `generate-zoning` Skill 加载，两个连续工具调用形成了物理性阻断。而 2.2->2.3 之间只有 `save_semantic_plan(v0.2)` 一个工具调用，且该调用在 thinking 结束后才触发，无法起到"打断"效果。

  **(4) Stage 3 的"快速草稿"部分生效**

  Stage 3 thinking 从 5:50 降至 4:02（-1:48），有改善但仍偏长。4 分钟 thinking 仍包含：衣柜角几何重叠计算、梳妆台深度与开关间距精确权衡、衣柜门类型判断、多段走道宽度逐一验算。"快速草稿"姿态在结果层面体现（validate 一次通过、未发生多轮修正），但 thinking 层面的"一次完美"倾向仍占主导。

  **四、测试分析报告提出的修复建议**

  分析报告（`test/chat_20260320_195050/analysis_chat_20260320_195050.md`）提出了 5 条建议：

  1. **[P0] 在 2.2 和 2.3 之间增加强制工具调用** -- 推荐新建 `generate-optional-furniture` Skill，v0.2 提交后强制加载，创造物理 turn 边界（复制 2.1->2.2 成功模式）
  2. **[P0] 删除"位置暂定原则"的诱导 WHY** -- 删除"因为可选家具可能发现更优的全局布局"，改为简单陈述
  3. **[P1] 修复示例中的 v0.2->2.3 前向引用** -- 删除 v0.2 示例中"衣柜 L 型取决于梳妆台位置（2.3 决定）"的注释
  4. **[P1] 补充 Stage 3 试错失败案例示例** -- 在"手动检查清单"后新增"写入粗略坐标 -> validate 报错 -> 修正"的节奏示例
  5. **[P2] 保留但暂缓 Phase 1B 参数实验** -- 结构问题未解决前，effort=medium 实验的归因会被噪声干扰

  **五、本次讨论任务**

  本次讨论的目标是围绕 Phase 1A 测试结果和分析报告的建议，讨论并确定下一轮修改方案：

  1. **根因确认**：分析报告对 2.2->2.3 失效根因的诊断是否准确？是否有遗漏的因素？
  2. **修复方案评估**：5 条建议是否都应采纳？优先级是否合理？是否有替代方案？
  3. **新 Skill 设计**：`generate-optional-furniture` Skill 的职责边界、与现有工作流的协作关系、是否会引入新的复杂度
  4. **"位置暂定原则"修改的质量风险**：删除这条 WHY 是否会导致 Agent 在 v0.2 阶段过早锁定家具位置，降低后续的灵活性？
  5. **Stage 3 试错示例的设计**：什么样的示例能有效锚定"写入粗略坐标"的节奏，而不是锚定"故意写错"的行为模式？

  **六、当前工作流架构速查**

  规则体系地图（详见 `.claude/skills/rule-tuning/references/rule-system-map.md`）：

  | 层级 | 文件 | 职责 |
  |------|------|------|
  | Agent 系统提示词 | `BIMCanvas.Agent/templates/BIMCANVAS.md` | 身份、任务分类、交互规范、工具优先级 |
  | 执行工作流 | `templates/skills/generate-workflow/SKILL.md` | 五阶段框架、语义方案演进、Stage 3 写入协议 |
  | 分区 Skill | `templates/skills/generate-zoning/SKILL.md` | 空间功能定义、分割评估 |
  | 设计原则 | `references/design_principles.md` | 跨房间通用设计原则 |
  | 房间策略 | `references/{bedroom,bathroom,livingroom}.md` | 房间专属策略、决策树、示例 |
  | 设计品质维度 | `references/design_evaluation.md` | 五个品质维度、审查标准 |
  | 家具规则 | `modules/module_library.json` | agent_config 中的 topology_rules + relation_rules |

  数据流：用户请求 -> MainAgent 加载 SKILL.md -> 读取 design_principles + 房间策略 -> 条件加载 zoning Skill -> 读取 module_library -> 综合决策 -> 写入 -> validate_layout -> 截图审查

  **七、提示词设计原则速查**

  来自 `docs/Agent_Prompt_Design_Philosophy.md`，所有修改必须遵循：

  | 机制 | 一句话 | 与本次讨论的关联 |
  |------|--------|-----------------|
  | 注意力零和 | 规则越多，每条规则权重越低 | 新增 Skill 会增加总 token 量，需要控制体积 |
  | 激活而非注入 | 提示词唤醒已有知识 | Skill 不应注入 Agent 不具备的推理模式 |
  | WHY = 泛化 | 有理由的规则能灵活应用 | 删除"位置暂定"WHY 后需确认不丢失泛化能力 |
  | 示例 = 锚定 | 一个好示例 > 十条规则 | v0.2 示例的前向引用是最强的错误锚定源 |
  | 位置效应 | 头尾内容获得更多注意力 | 新 Skill 加载指令应在 Stage 2.2 段落末尾 |
  | 三级约束 | 硬约束 -> 软指导 -> 自由区域 | 强制工具调用 = 硬约束；可选家具配置 = 自由区域 |
  | 留白 | 有意识地不写规则 | 新 Skill 不应过度规定可选家具的具体分析方法 |
  | 信噪比 | 去掉不会让行为变差的规则 | 新 Skill 内容必须精简 |
  | 一次只改一个变量 | 避免多变量耦合 | 本轮不动 config.json 参数、不动房间策略文件 |

  **八、相关文件**

  | 类型 | 路径 |
  |------|------|
  | 测试日志 | `test/chat_20260320_195050/chat_20260320_195050.log` |
  | 测试分析 | `test/chat_20260320_195050/analysis_chat_20260320_195050.md` |
  | 前序讨论 | `reviews/WorkflowSpeedup_Review.md` |
  | 工作流 SKILL | `BIMCanvas.Agent/templates/skills/generate-workflow/SKILL.md` |
  | 分区 Skill | `BIMCanvas.Agent/templates/skills/generate-zoning/SKILL.md` |
  | 主控提示词 | `BIMCanvas.Agent/templates/BIMCANVAS.md` |
  | 房间策略 | `BIMCanvas.Agent/templates/skills/generate-workflow/references/bedroom.md` 等 |
  | MCP 工具定义 | `BIMCanvas.Agent/src/mcp/canvas.py` |
  | 提示词设计原则 | `docs/Agent_Prompt_Design_Philosophy.md` |
  | 规则体系地图 | `.claude/skills/rule-tuning/references/rule-system-map.md` |
  | 优化方案草稿 | `plans/workflow_speedup_optimization.md`（Phase 1A 前的草稿，部分内容已过时） |

---

## 2. 初始观点

> [!NOTE]
> **生成指南 (Phase 1)**
> 请各位专家阅读议题背景，在此处追加初始观点。
>
> - **基础性**：初始观点是后续讨论的基础，要足够详细。
> - **独特性**：基于议题方向，产生自己独特的见解。
> - **独立性**：初始观点不要受其他专家影响，更不要对其观点作出回应（独立思考）。

<!-- 每位专家必须在此处生成详细的初始观点 -->

---

## 3. 深入讨论

> [!NOTE]
> **讨论指南 (Phase 2)**
> 阅读其他专家的初始观点，在此处追加讨论。
> - **重点关注**：用户提出的观点、冲突的看法、达成的共识、需要进一步推进的方向。
> - **互动要求**：
>   - 适当的坚持自己合理的观点。
>   - 需要得到其他人的回复时，请直接 @专家名。
>   - 得到其他人的 @ 时，要积极的作出回应。

> **追加讨论格式示例**：
>
> `### [YYYY-MM-DD HH:mm] [专家名]: [观点标题]`
>
> 内容详情（不要使用标题格式，如 # 、## 、### ...）

<!-- 请在此分隔线下方追加新的讨论内容 -->

---

## 4. 共识总结

<!-- 讨论结束并且得到用户明确要求后填写，汇总达成的共识和结论 -->
