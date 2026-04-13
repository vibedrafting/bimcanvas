---
name: generate-planning
description: |
  Generate 规划 Skill。用于主动设计（derived）、参考启发式设计（reference-informed-derived）和参考图分析（reference-analysis）路径的 Stage 1 + Stage 2。
  根据是否存在 referenceAnalysis 自动切换 free mode 或 constrained mode。
---

# Generate 规划

> 你在本 Skill 中是设计师，不是翻译官。你的职责是基于空间约束主动推导布局图纸，并把图纸分阶段提交为语义方案。

## 职责与输出

- 输入：用户需求、项目 README、设计区几何数据、房间策略文件、模块库、可选家具规则、可选的 referenceAnalysis
- 输出：
  - `v0.1` 空间骨架
  - `v0.2` 主要家具墙面归属 + 分区结论
  - `v0.3` 完整图纸
- 每次提交都必须调用：
  - `save_semantic_plan({ zoneId, version, planType: “derived” | “reference”, content })`

**【必须】**本 Skill 只负责 Stage 1 + Stage 2，不负责坐标化施工。

---

## 入场动作

1. 调用 `load_semantic_plan(zoneId)` 读取语义方案
2. 检查返回值中的 `referenceAnalysis` 字段：
   - 若存在 → 进入 **constrained mode**（有参考图）
   - 若不存在 → 进入 **free mode**（无参考图）
3. 若 constrained mode，提取约束包：
   - 从 `referenceAnalysis.content` 中识别硬约束（confirmedConstraints）和软提示（referenceHints）
   - 记录已知差异（knownDifferences）
   - 记录用户确认（userConfirmations）

---

## 1. 感知

1. **单独调用截图**：`mcp__canvas__request_background_screenshot`
   - 截图返回后，先确认截图方位：在截图中定位门窗，与 zone boundaries 交叉验证方向，再继续后续读取
2. 并行读取：
   - `references/design_principles.md`
   - `references/design_evaluation.md`
   - `modules/module_library.json`
   - `schemes/zones.json`
   - `computed/exclusions.json`
3. 调用 `mcp__canvas__get_zone_boundaries` 获取当前设计区边界语义
4. 根据当前 zone tags 读取对应房间策略文件：
   - 卧室：`references/bedroom.md`
   - 卫生间：`references/bathroom.md`
   - 客餐厅：`references/livingroom.md`

**参考启发式设计（reference-informed-derived）规则**：
- 如果用户附带图片但未要求忠实还原，只把图片当作补充上下文
- 图片可用于理解风格、家具偏好、功能需求
- 图片不是图纸原文，不可直接绑定墙面归属

判断示例：

- 用户说"参考这张图的感觉，卧室要温馨一点" + 附图是酒店卧室 → 图片提供风格参考（暖色调、对称布局偏好），但不绑定具体墙面归属 → 仍走主动设计，AI 根据当前空间条件自主决定家具位置

---

## 2. 规划

### 2.1 空间骨架 -> v0.1

**目标**：独立分析当前项目户型，建立空间理解

**【必须】**无论有无参考图，都独立分析当前项目户型。referenceAnalysis 不得覆盖 v0.1 结论。

以 `design_evaluation.md` 的品质维度完成空间阅读：

- 动线方向
- 纵深层次
- 采光轴
- 初步设计意图

`v0.1` 只写空间骨架，不写具体家具坐标。

示例（矩形主卧 3600×4200，北门南窗）：

```markdown
### 空间骨架
- 动线：北→南，入口在北墙偏西
- 纵深：前场（入口区）→ 中场（活动区）→ 远端（窗前）
- 采光：南窗全面采光，窗前适合日间活动
- 初步意图：床靠东墙（最长无门窗墙面），衣柜北墙（入口同侧，动线前段解决收纳）
```

**【必须】**调用：

```text
save_semantic_plan({ zoneId, version: “v0.1”, planType: “derived” | “reference”, content })
```

**planType 选择规则**：
- free mode → `planType: “derived”`
- constrained mode → `planType: “reference”`

---

### 2.2 主要家具 + 分区结论 -> v0.2

#### 执行模式判断

- **free mode**（无参考图）：走现有流程
- **constrained mode**（有参考图）：走受约束流程

---

#### free mode 执行流程

1. 基于房间策略确定主要家具墙面归属
2. **【必须】**加载 `generate-zoning`
3. 先用主要家具墙面需求过滤分区方案，再确定功能定义
4. 产出：
   - 主要家具墙面归属
   - 分区结论或”不分割”
   - 设计目标

**战略选择规则**：
- 若当前执行者具备 `AskUserQuestion`，遇到会显著影响日常体验的多方案，必须向用户确认
- 若当前执行者不具备 `AskUserQuestion`，选择当前推荐方案继续，并在最终汇报中上报”自动代决”

---

#### constrained mode 执行流程

**Step 1: 读取硬约束**

从 `referenceAnalysis.content` 中提取硬约束（confirmedConstraints 章节）：
- 非侵占细节（negativeSpace）
- 关键锚点（anchorPoint）
- 确认的家具选型（furnitureSelection）

**作用**：作为可行解空间过滤器，不可静默违反。

**示例**：
```
硬约束：
- 门侧留白 >= 600mm（非侵占细节，用户确认）
- 1800大床（家具选型，用户确认）
- 床头靠东墙（关键锚点，用户确认）
```

**Step 2: 执行主干逻辑**

仍然执行：
- 基于房间策略确定主要家具墙面归属
- 加载 `generate-zoning`
- 先用主要家具墙面需求过滤分区方案
- 战略选择 / AskUserQuestion

**关键差异**：
- 在确定主要家具墙面归属时，优先采纳硬约束中的锚点
- 若硬约束与房间策略冲突 → AskUserQuestion 确认（交互模式）或标注偏离（自主模式）
- 若硬约束与几何条件冲突（如墙段不够长）→ AskUserQuestion 确认是否调整

**Step 3: 参考软提示**

从 `referenceAnalysis.content` 中提取软提示（referenceHints 章节）：
- 分区意图（zoningIntent）
- 设计理念（designPrinciple）
- 家具关系（furnitureRelation）

**作用**：候选方案排序依据，可被户型条件覆盖但需标注偏离。

**示例**：
```
软提示：
- 睡眠区在房间深处靠窗侧（分区意图）
- L形衣柜沿入口侧两面墙展开（家具关系）
```

**处理方式**：
- 在候选方案中，优先尝试与软提示一致的方案
- 若户型条件不允许（如墙段不够长、有窗冲突）→ 放弃该 hint，但在 v0.2 中标注偏离原因
- 标注格式：`[偏离参考] 参考图建议 L 形衣柜，但北墙有窗无法放置，改为西墙直线衣柜`

**Step 4: 产出 v0.2**

保存 `save_semantic_plan(v0.2, planType=”reference”)`

**v0.2 内容结构**（与 free mode 完全一致）：
- 主要家具墙面归属
- 分区结论
- 设计目标
- [可选] 偏离参考的标注

---

#### 硬约束处理规则

**硬约束不可静默违反**：
- 若必须违反 → AskUserQuestion 征求用户意见（交互模式）
- 若用户同意违反 → 在 v0.2 中显式标注”经用户授权偏离”
- 若用户不同意 → 停止，要求用户重新提供参考图或调整约束

**软提示可放弃但需标注**：
- 放弃时必须在 v0.2 中标注偏离原因
- 偏离原因必须基于几何条件或房间策略，不得基于”更合理””更美观”

**已知差异必须考虑**：
- 在决策时主动检查 knownDifferences
- 若差异影响决策 → 在 v0.2 中说明如何适配

---

#### 优先级顺序（constrained mode）

1. 硬约束（最高优先级）
2. 几何硬约束（zone boundaries、exclusions）
3. 房间策略
4. 软提示（优先候选）
5. 设计原则（design_principles.md）

---

**【必须】**调用：

```text
save_semantic_plan({ zoneId, version: “v0.2”, planType: “derived” | “reference”, content })
```

**【必须】**`v0.2` 提交后，立即读取 `references/optional-furniture-rules.md`。

---

### 2.3 可选家具与完整图纸 -> v0.3

#### 执行模式判断

- **free mode**（无参考图）：走现有流程
- **constrained mode**（有参考图）：走受约束流程

---

#### free mode 执行流程

在不推翻 `v0.2` 主要家具墙面归属的前提下，补充：

- 可选家具
- 附属家具
- 完整设计目标

示例（承接 v0.2：床靠东墙、衣柜北墙、不分区）：

```markdown
### 主要家具（v0.2 已定）
- 床 1800×2100 → 东墙居中，面朝西
- 衣柜 → 北墙，门侧起始

### 可选家具（v0.3 补充）
- 床头柜 ×2 → 床两侧
- 梳妆台 → 西墙靠南角（避开入口动线）

### 设计目标
睡眠为核心，收纳集中北墙，西墙南段利用暗角放梳妆台
```

---

#### constrained mode 执行流程

**核心约束**：以参考图家具集合为上限

**Step 1: 提取参考图家具清单**

从 `referenceAnalysis.content` 中提取所有家具：
- 主家具（已在 v0.2 中确定）
- 可选家具（梳妆台、书桌、斗柜等）
- 附属家具（床头柜、窗帘等）

**Step 2: 补充可选家具**

规则：
- 参考图中有的家具 → 优先保留
- 参考图中没有的家具 → 不主动新增
- 若参考图中的家具放不下 → 尝试换小模块
- 若仍然放不下 → 在 v0.3 中标注”因空间限制未纳入：[家具名]”

**示例**：
```
参考图家具清单：
- 1800大床（已在 v0.2 确定）
- L形衣柜（已在 v0.2 确定）
- 梳妆台（可选）
- 床头柜 x2（附属）

v0.3 补充：
- 梳妆台 → 保留（参考图有）
- 床头柜 x2 → 保留（参考图有）
- 书桌 → 不添加（参考图没有）

[因空间限制未纳入] 无
```

**Step 3: 产出 v0.3**

保存 `save_semantic_plan(v0.3, planType=”reference”)`

---

#### 硬约束（constrained mode）

**不增原则**：
- 禁止添加参考图中没有的家具
- 即使房间策略建议标准配置，若参考图未显示也不得添加

**不减原则**：
- 尽量保留参考图中的所有家具
- 若确实放不下，必须在 v0.3 中显式标注

---

#### 兜底策略

**若参考图家具清单不完整**（如只标注了主家具）：
- 可补充常规附属家具（如床头柜）
- 但必须在 v0.3 中标注”参考图未明确，按常规配置补充”

---

**【必须】**继续遵守”战略选择 vs 战术选择”的区分：
- 战略选择：可影响日常体验，需要用户确认；无 `AskUserQuestion` 时按推荐方案自动代决并上报
- 战术选择：由规范直接决定，自主执行

**【必须】**调用：

```text
save_semantic_plan({ zoneId, version: “v0.3”, planType: “derived” | “reference”, content })
```

---

## 3. 约束

- `v0.2` 之后，主要家具墙面归属不可在本 Skill 内被推翻
- 本 Skill 可参考图片，但参考启发式设计（`reference-informed-derived`）仍然是主动设计，不是照图翻译
- constrained mode 下，硬约束不可静默违反，软提示可放弃但需标注
- 不在本 Skill 内写 `modules.json`
- 不在本 Skill 内调用 `load_semantic_plan`（除了入场动作）

**【自由区域】**

- 可选家具的取舍（在 optional-furniture-rules 框架内）
- 家具间精确间距
- 设计意图的表达方式
- constrained mode 下偏离标注的具体措辞
- v0.1 空间骨架的详细程度

---

## 4. 交接

本 Skill 完成后，由编排层路由到 `generate-placement` 进行施工。
