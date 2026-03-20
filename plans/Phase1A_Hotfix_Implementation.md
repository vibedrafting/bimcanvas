# Phase 1A-hotfix 实施计划

> 基于 `reviews/WorkflowSpeedup_Phase1A_PostTest_Review.md` 的共识总结（含第七节补充共识）。
> 目标：修复 Stage 2.2->2.3 阶段边界失效问题，将 11:12 的合并 thinking block 拆为两个独立 turn。

---

## Context

Phase 1A 验证了结构性边界机制的有效性（Stage 2.1 thinking 从 3:47 降至 1:46），但 Stage 2.2->2.3 边界完全失效——两个阶段合并为 11:12 的单一 thinking block（比基线 8:43 更长）。

根因分析确认了四个互相加强的因素：
1. 2.2->2.3 之间缺乏物理性 turn 边界（只有 save_semantic_plan 单个调用，不够强）
2. 决策循环依赖（床头墙↔衣柜形态↔梳妆台位置三角循环）
3. "位置暂定原则"WHY 主动诱导跨阶段前瞻思考
4. v0.2 示例中的前向引用锚定了"v0.2 阶段就要思考梳妆台"的行为

本次 hotfix 通过以下手段修复：新建 Reference File 作为 2.2->2.3 的双重工具调用边界、删除诱导 WHY、修复示例前向引用、确立"主家具霸权"、补充 Stage 3 试错示例、增强 v0.3 内容定义。

---

## 涉及文件

| 文件 | 改动性质 |
|------|---------|
| `templates/skills/generate-workflow/references/optional-furniture-rules.md` | **新建** |
| `templates/skills/generate-workflow/SKILL.md` | 修改（5 处） |
| `templates/BIMCANVAS.md` | 不动（本轮无需修改） |

> BIMCANVAS.md 无需修改：Reference File 通过 Read 调用加载，不涉及 Skill 注册或工具列表变更。save_semantic_plan 工具已在 Phase 1A 中注册完毕。

---

## 改动 1：新建 `references/optional-furniture-rules.md`

**路径**：`templates/skills/generate-workflow/references/optional-furniture-rules.md`

**目的**：
- 作为 save_semantic_plan(v0.2) 之后的第二个工具调用（Read），形成双重 turn 边界
- 携带"主家具霸权"规则和"禁止回流"规则，以 Read 返回值的形式成为新 turn 中最新鲜的内容

**内容草案**（不超过 30 行）：

```markdown
# Stage 2.3：在已确认骨架内补完可选家具

> v0.2 已提交的主要家具位置和分区结论是不可回滚的前提。
> 本阶段的任务是：在这个已确认的骨架内，为可选家具找到合适的位置。

## 禁止回流

- 不重新比较床头墙候选
- 不重新评估主衣柜的基本形态（线性/位置）是否成立
- 不重新评估分区是否应改变

WHY：v0.2 的决策已经提交。重新评估主要家具 = 推翻已提交的锚点 = 循环重评。

## 主家具霸权

v0.2 确认的主要家具位置不可推翻。可选家具无法安置时，优先调整可选家具自身（缩小尺寸/换位/剔除），而非回溯主要家具。

**唯一例外（窄门升级）**：仅当 v0.2 确认的主衣柜所在墙面不变、且新增段是主衣柜转角处的附加短边时，允许从线性升级为 L 型。不允许更换墙面、不允许调整主衣柜长度。

## v0.3 内容要求

v0.3 必须包含每件可选家具的完整选型结论：
- 模块 ID + 精确尺寸（宽×深）+ 朝向
- 不只是"梳妆台在东墙₂"，而是"mod_vanity_custom_001, 950×400mm, 面朝西"

WHY：Stage 3 只做坐标计算，不做模块选型。选型决策必须在 v0.3 中完成。
```

---

## 改动 2：SKILL.md — 删除"位置暂定原则"诱导 WHY

**文件**：`templates/skills/generate-workflow/SKILL.md`

**位置**：第 237-239 行

**当前内容**：
```markdown
**【必须】位置暂定原则**：v0.2 中主要家具标记为 `~`（暂定），因为可选家具的加入可能发现更优的全局布局。

WHY：顺序分配（锚点→主要→可选→间距）让先决策的大件占据最优位置，后决策的小件只能在剩余空间中选择。这制造了虚假的"唯一可行位"，遮蔽了"调整大件为小件腾位"可能带来的全局更优方案。
```

**替换为**：
```markdown
**【必须】主家具确认原则**：v0.2 中主要家具标记为 `✓`（确认）。v0.2 提交后，主要家具的墙面归属不可推翻。v0.3 可在不改变主要家具位置的前提下扩展（如衣柜从线性延伸为 L 型），但不可回溯主要家具决策。
```

**影响分析**：
- 删除了"可选家具可能发现更优的全局布局"——这正是诱导 Agent 在 2.2 阶段前瞻 2.3 内容的根源
- `~`（暂定）改为 `✓`（确认）——消除"暂定"概念对 Agent "什么时候算完"的判断干扰
- 保留了窄门升级例外（线性→L 型），但表述为"扩展"而非"修改"

---

## 改动 3：SKILL.md — Stage 2.2 结尾新增 Read 指令

**文件**：`templates/skills/generate-workflow/SKILL.md`

**位置**：第 232 行之后（`save_semantic_plan(v0.2)` 提交指令之后，`→ Skill 产出 subZones` 之前）

**当前内容**（第 232 行）：
```markdown
**【必须】**调用 `save_semantic_plan(zoneId, "v0.2", 方案文本)` 提交 v0.2——不在 thinking 中继续分析可选家具。v0.2 是锚定点：主要家具位置和分区结论一旦提交，后续的可选家具探索（v0.3）基于此展开，不回溯。
```

**替换为**：
```markdown
**【必须】**调用 `save_semantic_plan(zoneId, "v0.2", 方案文本)` 提交 v0.2。

**【必须】**提交 v0.2 后，立即读取 `references/optional-furniture-rules.md`。

WHY：Read 调用是进入 Stage 2.3 的入口——它携带了可选家具阶段的工作边界和禁止回流规则。读取后按照该文件的指引开始 v0.3 分析。
```

**影响分析**：
- 形成 save_semantic_plan(v0.2) + Read 的双重工具调用，复制 2.1→2.2 的成功模式
- Read 返回的 optional-furniture-rules.md 内容是新 turn 中最新鲜的信息（近因效应最强），"禁止回流"规则会直接影响 Agent 的下一段 thinking
- 明确了"Read 是入口"的语义，而非"读一个参考文件"

---

## 改动 4：SKILL.md — 修复 v0.2 示例前向引用

**文件**：`templates/skills/generate-workflow/SKILL.md`

**位置**：第 77-78 行

**当前内容**：
```markdown
  衣柜 ~ → 北墙门右段：~4070mm 连续实墙，入口侧，隐私序列正确
    ※ 衣柜是否 L 型延伸到东墙₂ 取决于梳妆台位置（2.3 决定）
```

**替换为**：
```markdown
  衣柜 ✓ → 北墙门右段：~3770mm 线性，入口侧，隐私序列正确
```

**影响分析**：
- 删除了"取决于梳妆台位置（2.3 决定）"——这是最强的前向引用锚定，直接诱导 Agent 在 v0.2 阶段思考梳妆台
- `~` 改为 `✓`，与改动 2 的"主家具确认原则"一致
- 衣柜长度从 4070mm 改为 3770mm（扣除 300mm 开关预留，与测试日志中 Agent 的实际计算一致）
- 删除 L 型延伸的提及——L 型问题完全推迟到 v0.3

**同步修改**：确定性标记说明（第 36-39 行）

当前内容：
```markdown
确定性标记：
- `✓` 已确认——不再变动
- `~` 暂定——后续可能因联动调整
- `→ 待用户确认` 战略选择，需要 AskUserQuestion
```

替换为：
```markdown
确定性标记：
- `✓` 已确认——不再变动（v0.2 提交后主要家具即为 ✓）
- `→ 待用户确认` 战略选择，需要 AskUserQuestion
```

> 删除 `~` 暂定标记——既然"主家具霸权"原则要求 v0.2 中主要家具直接确认，`~` 标记不再需要。

---

## 改动 5：SKILL.md — 补充 Stage 3 试错示例

**文件**：`templates/skills/generate-workflow/SKILL.md`

**位置**：第 293 行之后（"其余全部交给 validate_layout"段落之后，"放置"段落之前）

**新增内容**：
```markdown

### 试错节奏示例

> 先给出满足朝向与贴墙关系的粗坐标，碰撞与净距留给 validate_layout 检查。

```
Write modules.json（床靠西墙，衣柜靠北墙，按语义方案逐件定位）
→ validate_layout → E005: 衣柜西端与床使用侧柜重叠 85mm
→ 衣柜整体东移 100mm → Write → validate_layout → 0 errors
```

一次修正即完成。不需要在写入前预先计算每对家具之间的精确间距——validate_layout 会告诉你哪里撞了、撞了多少。
```

**影响分析**：
- 以工程化表达展示"写入→报错→最小位移→通过"的闭环
- 锚定的是"快速但有根据的首版"（按语义方案逐件定位），不是"随便乱填"
- 只覆盖 A 类问题（碰撞/越界），B 类问题（模块选型）已在改动 1 中前移到 v0.3

---

## 不改动的文件

| 文件 | 理由 |
|------|------|
| `templates/BIMCANVAS.md` | Reference File 通过 Read 加载，不需要 Skill 注册变更 |
| `templates/skills/generate-zoning/SKILL.md` | 属于第二批（内容减载 P1），本轮不动 |
| `references/bedroom.md` 等房间策略 | 属于 Phase 2，本轮不动 |
| `config.json` | 属于 Phase 1B（参数实验），本轮不动 |
| `src/mcp/canvas.py` | save_semantic_plan 已在 Phase 1A 实现，本轮不需要修改 |

---

## 验证

### 测试方式

同场景测试：金凤127 / rz_3 主卧，使用与 Phase 1A 完全相同的参数配置（effort=low, thinking=adaptive, maxThinkingTokens=16000）。

### 量化指标

| 指标 | Phase 1A 基线 | hotfix 预期 | 判定标准 |
|------|-------------|-----------|---------|
| Stage 2.2 thinking | 11:12（含 2.3） | 拆成两个独立 turn | 每个 turn <= 4 min |
| Stage 2.3 thinking | （合并在 2.2 中） | 独立 turn | <= 2 min |
| Stage 3 thinking | 4:02 | 缩短 | <= 2 min |
| 首次 Write 时间 | 第 22 分钟 | 前移 | <= 第 15 分钟 |
| validate 通过 | 首次通过 | 允许 1 轮修正 | 最终通过 |
| 设计目标达成 | 4/4 | 保持 | 不降级 |
| save_semantic_plan | 3 次（v0.2 由 API 错误触发） | 3 次（全部自然触发） | 非 API 错误触发 |
| max_output_tokens 错误 | 1 次 | 0 次 | 不再触发 |

### 日志审查检查清单

- [CHECK-1] Stage 2.2 thinking 中不出现梳妆台候选墙段扫描、400mm vs 600mm 深度比较、衣柜 L 型 vs 线性比较
- [CHECK-2] save_semantic_plan(v0.2) 在可选家具分析之前自然触发（不靠 API 错误补交）
- [CHECK-3] 无 max_output_tokens 错误
- [CHECK-4] Stage 3 首次 Write 前 thinking 中不出现逐件家具的精确间距验算

### 危险信号词表

Read optional-furniture-rules.md 之后的 thinking 首段不应出现：
- "重新考虑床"
- "回到主衣柜"
- "也许分区应该"
- "整体来看更优"

### 回退预案

如果测试日志显示 Read 调用的 turn 边界不够强（Agent 在 Read 后继续在同一 turn 中完成 2.3 分析而未形成新的独立 thinking block），则升级为 Skill 方案——将 optional-furniture-rules.md 从 Reference File 改为独立 Skill。
