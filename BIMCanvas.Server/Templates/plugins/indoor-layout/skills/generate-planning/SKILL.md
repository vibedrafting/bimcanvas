---
name: generate-planning
description: |
  Generate 规划 Skill。负责把当前户型分析与定稿后的 `reference_analysis`
  压缩成自包含的 `semantic_plan spatial-skeleton / strategic-plan / construction-brief`，供 placement 施工。
  multi-plan 模式（`exploreMode=true`）下改产 canonical `multi-plan-overview` 并终止于此，
  construction-brief 由后续 `variant-design-agent` 在各变体目录内生成。
---

# Generate 规划

> 你在本 Skill 中是设计师。你的职责不是复刻参考图，而是把“当前户型 + 定稿参考分析 + 设计知识库”压缩成可施工的语义合同。

## 路径约定

- Skill 文件本体不决定业务文件根目录；以下相对路径均以当前项目目录为根目录
- `references/*.md` 是项目级运行时参考规则
- `modules/`、`computed/`、`schemes/` 也都是当前项目目录下的业务数据

## 最重要的规则

1. **`spatial-skeleton` 永远只分析当前户型，不读参考分析。**
2. **只有定稿后的 `reference_analysis` 才能进入 planning。**
3. **`strategic-plan` 是战略层方案，第一次正式消费 reference_analysis。**
4. **`construction-brief` 是完整施工简报，placement 只读 `construction-brief`。**
5. **若核心参考意图与当前几何冲突，必须先 Ask，再继续规划。**
6. **multi-plan canonical 模式（`exploreMode=true`）产 canonical `multi-plan-overview` 即终止本 Skill，不进入 `construction-brief`；`construction-brief` 由后续 `variant-design-agent` 触发本 Skill 的 variant-mode 分支后生成。multi-plan 与 `reference_analysis` 互斥。**
7. **variant-mode（`variantContext` 非空）由 `variant-design-agent` 调用，走 single-plan 主干流程但所有 `save_semantic_plan` 必须传 `variantId = variantContext.variantSlug`；不进入 multi-plan canonical 分支、不进入 generate-zoning、不重新写 canonical `spatial-skeleton`（走 load 视图）。**

**WHY**：
- `spatial-skeleton` 不独立成立，后面的参考消费就没有真实空间基线。
- planning 如果消费未定稿参考分析，就会把 analysis 阶段尚未冻结的歧义偷偷带进设计决策。
- placement 不应重新理解 raw reference；它只该施工。
- variant-mode 让单变体在 brief 方向 hint 下走完整规划流程。如果 SubAgent 绕过 SKILL 自己手写 strategic-plan / construction-brief，会丢失 SKILL 累积的所有 WHY、闭合预检、双候选评估、L 形门槛、阵列前置扣减等规则——这是 multi-plan 质量崩溃的根因。

---

## 职责与输出

- 输入：
  - 用户需求
  - 项目 README
  - 当前设计区几何
  - 房间策略文件
  - 模块库
  - 可选家具规则
  - 可选的、**已定稿**的 `reference_analysis`
- 输出（按入场模式分支）：
  - **自主规划 / 参考消费模式**：`spatial-skeleton` 空间骨架 → `strategic-plan` 战略层方案 → `construction-brief` 完整施工简报（均落 canonical）
  - **multi-plan canonical 模式（主控调用）**：`spatial-skeleton` 空间骨架 → `multi-plan-overview` 多方案战略层概述（canonical，含变体清单 YAML 头 + 每变体 brief）；**不产 `strategic-plan` / `construction-brief`**，由后续 `variant-design-agent` 调用本 Skill 的 variant-mode 分支生成
  - **multi-plan N=1 退化**：变体只剩 1 个 → 改写普通 `strategic-plan` → 继续 `construction-brief`，与自主规划模式同收尾
  - **variant-mode（variant-design-agent 调用）**：load canonical `spatial-skeleton`（不写）→ variant `strategic-plan` → variant `construction-brief`，所有 save 必传 `variantId`，落到 `schemes/{designZoneId}/variants/{variantSlug}/semantic_plan.json`

**【必须】**planning 是唯一的语义压缩点：
- 把当前户型读懂
- 把定稿 reference_analysis 消化掉
- 把真正采纳的参考理解写进 `semantic_plan`
- 把偏离与转译理由写进 `semantic_plan`

**【必须】**本 Skill 不负责坐标化施工，不写 `modules.json`。

---

## 入场判断

### 自主规划模式

适用于：
- 当前任务没有参考图
- 或当前任务没有已定稿的 `reference_analysis`
- 且**未携带** `exploreMode=true` 标记

### 参考消费模式

适用于：
- 当前任务已完成 `generate-reference-analysis`
- 当前设计区已经存在可供消费的最新定稿 `reference_analysis`
- 且**未携带** `exploreMode=true` 标记（参考消费与 multi-plan 互斥）

**参考消费模式入场动作**：
1. 调用 `load_reference_analysis(zoneId)` 读取最新版本
2. 若返回 `status=missing`，立即停止并说明“缺少已定稿的 reference_analysis，不能进入参考消费模式”
3. 复述当前消费的参考分析版本号

### multi-plan canonical 模式（主控调用）

适用于：
- 任务上下文携带 `exploreMode=true` 标记（由主控注入，已通过单设计区 + 与 reference 互斥两道前置检查）
- 且**没有**已定稿的 `reference_analysis`（multi-plan 与参考消费互斥，由主控前置检查保证；本 Skill 入场时再次确认一次）

**multi-plan canonical 模式入场动作**：
1. 复述上下文中的 `exploreMode=true` 标记，明确进入 multi-plan canonical 分支
2. 若同时检测到定稿 `reference_analysis` 存在，立即停止并报"主控前置检查失误：multi-plan 与 reference_analysis 互斥"
3. multi-plan canonical 模式产出 `multi-plan-overview` 即终止本 Skill；**不进入 construction-brief**（construction-brief 由后续 `variant-design-agent` 调用本 Skill 的 variant-mode 分支生成）

### variant-mode（variant-design-agent 内部专用）

适用于：
- 任务上下文携带非空的 `variantContext` 字段（结构：`{variantSlug, designZoneId, variantDirection, variantNarrative, variantAnchorSeed, variantAvoidance}`）
- 且 `exploreMode` 未设置（variant-mode 与 multi-plan canonical 互斥——前者是 subagent 在变体内部走 single-plan 流程，后者是主控产生变体清单）
- 且没有已定稿 `reference_analysis`（variant-mode 与参考消费互斥）

**variant-mode 入场动作**：
1. 复述 `variantContext.variantSlug` 与 `variantDirection`，明确进入 variant-mode 分支
2. 走 single-plan 主干流程（`spatial-skeleton` → `strategic-plan` → `construction-brief`），但：
   - `spatial-skeleton` 走 load 视图（`load_semantic_plan({zoneId, tag: "spatial-skeleton"})`），**不重新写入** —— canonical 已有的 spatial-skeleton 是所有变体共享的空间基线
   - 所有 `save_semantic_plan({tag: "strategic-plan" | "construction-brief"})` **必须传 `variantId = variantContext.variantSlug`**
   - `strategic-plan` 阶段把 `variantDirection` / `variantNarrative` 作为**探索方向输入**，只把 `variantAnchorSeed` 当硬约束，其余决策（衣柜墙、梳妆台位置、是否 L 形等）由 SKILL 在自主规划主干内自由判断
3. SKILL 内部的双候选评估、L 形门槛、阵列前置扣减、闭合预检等规则照常运行
4. 终止于 `construction-brief` 保存——**不进入** `generate-placement`（placement 由 variant-design-agent 自行加载 `generate-placement` Skill）

**【禁止】variant-mode 下**：
- 进入 multi-plan canonical 分支（不写 `multi-plan-overview`）
- 进入 generate-zoning 阶段（subZones 跟随 canonical，variant 不改 zoning）
- 写 canonical `spatial-skeleton` / `strategic-plan` / `construction-brief`（所有 save 必传 variantId）

**【关键】variantAnchorSeed 不成立时的认输路径**：
若发现 `variantAnchorSeed` 在当前几何下不成立（如锁定"床头墙=西墙"但西墙全是门窗），立即停止 strategic-plan 写入，输出 `[自动改图建议] 本变体 variantAnchorSeed 不成立：<具体原因 + 坐标证据>` 并终止本 Skill；调用方（variant-design-agent）应透传此建议给主控。

### 入场模式互斥关系总表

| 模式 | 触发上下文 | 调用者 | 终止于 |
|------|-----------|--------|--------|
| 自主规划 | 无标记 + 无定稿 reference + 无 variantContext | 主控 / layout-agent | `construction-brief`（canonical） |
| 参考消费 | 定稿 reference_analysis 存在 + 无 exploreMode + 无 variantContext | 主控 / layout-agent | `construction-brief`（canonical） |
| multi-plan canonical | `exploreMode=true` | 主控 Agent | `multi-plan-overview`（canonical） |
| variant-mode | `variantContext` 非空 | variant-design-agent | `construction-brief`（variant 路径） |

**【必须】**不要根据图片名称、用户措辞、主观印象给流程另起名字。

**【必须】**图片本身不是合同；定稿 `reference_analysis` 才是 planning 的正式参考输入。

**【必须】**四种入场模式严格互斥。优先级判定（同时存在多触发条件时）：`exploreMode=true` > `variantContext` 非空 > 定稿 reference_analysis > 默认自主规划。`exploreMode=true` 与 `variantContext` 非空同时存在视为编排错误，立即停止并报"模式冲突"。

---

## 1. 感知

1. 调用 `mcp__canvas__request_background_screenshot`
2. 并行读取：
   - `references/design_principles.md`
   - `references/design_evaluation.md`
   - `modules/module_library.json`
   - `schemes/zones.json`
   - `computed/exclusions.json`
3. 调用 `mcp__canvas__get_zone_boundaries`
4. 根据当前 zone tags 读取对应房间策略文件：
   - 卧室：`references/bedroom.md`
   - 卫生间：`references/bathroom.md`
   - 客餐厅：`references/livingroom.md`

---

## 2. 规划

### 2.1 空间骨架 -> `spatial-skeleton`

**目标**：独立理解当前项目户型。

**【必须】**无论是否存在 `reference_analysis`，`spatial-skeleton` 都只分析当前户型。

**【variant-mode 例外】**：variant-mode 下**不重新写入** spatial-skeleton——canonical 已有的 spatial-skeleton 是所有变体共享的空间基线，本节调用 `load_semantic_plan({zoneId, tag: "spatial-skeleton"})` 取得后直接进入 §2.2。

从 `design_evaluation.md` 的品质维度完成空间阅读：
- 动线方向
- 纵深层次
- 采光轴
- 当前空间潜力与风险

`spatial-skeleton` 只写空间骨架，不写具体家具坐标。

#### `spatial-skeleton` 推荐结构

```markdown
# spatial-skeleton 空间骨架

## 当前户型空间阅读
- ...

## 动线与纵深
- ...

## 采光与安静区判断
- ...

## 初步设计抓手
- ...
```

#### 保存规则

```text
save_semantic_plan({
  zoneId,
  tag: "spatial-skeleton",
  planType: "derived",
  content
})
```

`spatial-skeleton` 不写 `referenceAnalysisTag`。

**WHY**：
- `spatial-skeleton` 是后续所有设计决策的真实地基。
- 如果参考分析直接覆盖 `spatial-skeleton`，空间基线会被参考图幻觉污染。

---

### 2.2 战略层方案 -> `strategic-plan`

**目标**：锁定主家具、分区组织、关键空间关系，以及参考的战略级采纳与偏离。

#### 自主规划模式

1. 基于房间策略确定主要家具策略，并同步识别 `optionalTags` 是否会改变功能带、组合关系或用户偏好选择
2. **【必须】**加载 `generate-zoning`
3. 先用主要家具需求与可选功能潜力过滤分区方案，再确定功能定义
4. 输出战略层方案；若存在可选功能的战略候选集合或战略分歧，必须写入 `strategic-plan`

#### 参考消费模式

先读取最新定稿 `reference_analysis`，重点识别：
- 参考布局核心理解
- 与当前项目的关键差异
- 用户确认后的设计倾向
- 优先保留的参考意图
- 可调整或不要求忠实参考的部分
- 当前采用的转译原则

然后执行和自主规划模式相同的主干逻辑：
- 房间策略
- `generate-zoning`
- 主要家具策略
- 分区组织
- `optionalTags` 的战略候选发现
- 战略选择 / AskUserQuestion

**参考消费模式的核心要求**：
- 优先保留已经在参考分析中定稿的核心设计组织
- 把当前户型中的转译结果写清楚
- 不能把“看起来更合理”当作偏离理由
- 偏离理由必须来自几何、建筑锚点、功能冲突或用户最新确认

#### 核心冲突处理

若出现以下情况，必须先 Ask，再继续：
- 参考分析中明确要保留的核心设计意图无法兑现
- 当前项目只能在两种完全不同的战略方案之间选择（包括功能带归属、可选功能取舍或关键组合关系）
- 参考分析里“可调整”的表述仍不足以覆盖当前冲突

#### `strategic-plan` 推荐结构

```markdown
# strategic-plan 战略层方案

## 空间骨架承接
- ...

## 主要家具策略与分区
- ...

## 可选功能战略候选
- 候选集合：...
- 战略分歧：...

## 关键空间关系
- ...

## 参考采纳与偏离摘要
- 采纳：...
- 偏离：...
- 转译原则：...

## 自动标记
- ...
```

#### `strategic-plan` 示例：带参考分析

```markdown
# strategic-plan 战略层方案

## 空间骨架承接
- 当前户型存在前场与深处的层次，可形成清晰的功能递进。

## 主要家具策略与分区
- 前场承担过渡或辅助功能，深处承担核心功能。
- 主家具位于更稳定、干扰更少的功能带。

## 可选功能战略候选
- 候选 A：靠近核心功能带，使用便利但组合关系弱。
- 候选 B：靠近辅助功能带，组合关系强但环境条件较弱。
- 战略分歧：两者都可行，且会改变日常使用体验。

## 关键空间关系
- 保留“入口 -> 过渡功能 -> 核心功能”的递进关系。
- 辅助功能优先与主通行形成缓冲，而不是抢占核心功能区。

## 参考采纳与偏离摘要
- 采纳：保留前后场层次与核心功能后置。
- 偏离：原参考的展开方式因当前几何条件调整。
- 转译原则：优先保留空间组织，不强求原图构件方向。

## 自动标记
- 无
```

#### variant-mode

承接 canonical `spatial-skeleton`（已通过 `load_semantic_plan({tag: "spatial-skeleton"})` 取得），把 `variantContext` 4 字段作为方向输入：

- `variantDirection`：本变体的设计方向核心句——作为 strategic-plan "空间骨架承接"或"主要家具策略与分区"段的统领
- `variantNarrative`：方向 WHY——帮助 SKILL 在双候选评估、L 形/线性选择等决策点判断"该方向下最好的选择"
- `variantAnchorSeed`（**最多 1 条硬锚点**）：必须兑现的核心决策（如"床头墙=西墙"）——其余决策（衣柜墙、梳妆台位置、是否 L 形等）由 SKILL 在主干流程内自由判断
- `variantAvoidance`：方向反面——避免与兄弟变体雷同的反模式提示

**variant-mode 主干流程**：
1. 加载房间策略文件（同自主规划模式）
2. **不加载 `generate-zoning`**（subZones 跟随 canonical）
3. 主要家具策略：在 `variantAnchorSeed` 约束下确定，其余字段作为软 hint
4. 输出 strategic-plan，结构同上述"推荐结构"，但在"空间骨架承接"或独立的"## 变体方向"段开头复述 `variantDirection` + `variantAnchorSeed`

**variantAnchorSeed 不成立判定**：
若 `variantAnchorSeed` 在当前几何下不可兑现，**不保存 strategic-plan**，立即输出：

```text
[自动改图建议] 本变体 variantAnchorSeed 不成立：
  variantSlug: <slug>
  锁定决策: <variantAnchorSeed 内容>
  不成立原因: <具体原因 + 坐标证据>
建议主控：从 batch 中过滤本变体；若剩余变体 ≥ 2 正常展示，否则触发 N=1 退化。
```

调用方（variant-design-agent）应透传此建议给主控。

#### 保存规则

```text
save_semantic_plan({
  zoneId,
  tag: "strategic-plan",
  planType: "derived",
  content,
  variantId: variantContext.variantSlug,   # variant-mode 必填
  referenceAnalysisTag: "vN"               # 参考消费模式必填（与 variantId 互斥）
})
```

**【必须】**`variantId` 与 `referenceAnalysisTag` 互斥（variant-mode 与参考消费模式互斥）；
自主规划模式两者都不传；multi-plan canonical 模式不进入本节。

`strategic-plan` 提交后，立即读取 `references/optional-furniture-rules.md`，用于把已发现的可选功能候选收束为 `construction-brief` 施工表达；不得在 `construction-brief` 首次发明新的战略候选。

**WHY**：
- `strategic-plan` 的职责是先把“方向”锁死，再让 `construction-brief` 补足施工所需细节。
- 如果在 `strategic-plan` 不写战略级偏离，`construction-brief` 就会在已经落定主家具后再补写理由，因果顺序会倒置。
- 可选功能若会改变功能带或组合关系，属于战略变量；候选必须在冻结前出现，否则 AskUserQuestion 没有触发对象。

---

### 2.3 多方案战略层概述 -> `multi-plan-overview`（multi-plan canonical 模式专属）

**目标**：在多方案模式下，产出一份**多变体战略概述**——每个变体是一个**设计方向 hint**（不是具体方案细节），由后续 `variant-design-agent` 调用本 Skill 的 variant-mode 分支在该方向下做完整规划。本节是主控派发 `variant-design-agent` 的合同。

**【必须】**只有 multi-plan canonical 模式（`exploreMode=true` 且无定稿 `reference_analysis`）才进入本节。其他模式跳过本节，直接进入 `### 2.4`。

#### 设计哲学：差异化在方向层，不在配置层

multi-plan 的核心价值是"几个对等可行的方案让用户视觉决策"——差异化的本质是**设计哲学/叙事方向的差异**（睡眠优先 vs 换衣动线优先 vs 视觉叙事优先），不是**具体家具排列的差异**（A 方案把衣柜放北墙 vs B 方案把衣柜放东墙）。

主控写 brief 时只锁定 **1 个核心决策点**（如"床头墙=西墙"或"主收纳=延伸区"），其余决策（衣柜墙、梳妆台位置、是否 L 形、阵列方式等）**全部交给 variant-design-agent 在 variant-mode 内自由发挥**——SKILL 内部的双候选评估、L 形门槛、阵列前置扣减规则会自然激活，产出"该方向下最好的方案"。

WHY：主控写 brief 时若已经把"具体家具组合"写死，subagent 在 variant-mode 内只能"兑现 brief 已经决定的画面"，失去全局重判机会——这是 multi-plan 质量崩溃的根因。把 brief 提到方向层后，subagent 拿回完整决策权，规则自然激活。

#### 触发与互斥重申

- `exploreMode=true` 由主控前置检查通过后注入（已确认：单设计区 + 与 reference_analysis 互斥）
- 本节产出后 `save_semantic_plan({tag: "multi-plan-overview"})` 即终止本 Skill；**不进入 construction-brief**
- construction-brief 由后续 `variant-design-agent` 在每个变体目录内分别生成（写到 `schemes/{designZoneId}/variants/{slug}/semantic_plan.json`）

#### spatial-skeleton 共享声明

multi-plan 模式下 `spatial-skeleton` 的生成与单方案模式完全相同（参见 `### 2.1`）；所有变体共享 canonical `spatial-skeleton`，本 Skill 调 `save_semantic_plan({tag: "spatial-skeleton"})` 时**永远不传 `variantId`**（`spatial-skeleton` 是 canonical-only tag，Server 强制校验）。

#### multi-plan-overview canonical 模板

**【必须】**multi-plan-overview 的 content 必须严格遵守以下结构。它不是展示用排版，而是**主控解析变体清单 + 抽取每变体 brief 的合同**：

````markdown
# 多方案战略层概述（tag: multi-plan-overview）

## 共享语境

（承接 `spatial-skeleton` 的空间阅读：户型、动线、采光、关键墙面等。所有变体共享此段。）

## 变体清单

```yaml
variants:
  - slug: classic-west
    title: 西墙睡眠·自然轴线
  - slug: l-shaped-station
    title: 转角 L 形衣帽工作站
  - slug: closet-priority
    title: 收纳带动叙事
```

## 设计意图 briefs

### classic-west（示例：单家具锚点类型）

- variantDirection: 以最深处采光位为睡眠锚点，让收纳功能沿动线由静到动展开。
- variantNarrative: 床头墙锁定房间最长且远离门的连续实墙，天然成为睡眠区的"深度边界"。其他家具的具体配置由 SKILL 在 variant-mode 下决定（包括衣柜墙、梳妆台位置、是否 L 形）。
- variantAnchorSeed: 床头墙=最长安静实墙
- variantAvoidance: 不让主收纳远离主体区，把延伸区当辅助而非主战场。

### l-shaped-station（示例：家具组合关系类型）

- variantDirection: 衣柜与梳妆台在异形区/转角形成 L 形工作站，更衣与梳妆动线一体。
- variantNarrative: 用两件家具的**转角组合关系**定义功能聚合点——SubAgent 自由选择具体哪个转角（异形区 NE 角 / 主体区入口侧 / 床尾对面），由几何条件决定。床头墙、采光关系由 SKILL 在 strategic-plan 内判断。
- variantAnchorSeed: 衣柜+梳妆台=L 形组合（具体转角由 SubAgent 在 variant-mode 决定）
- variantAvoidance: 不让 L 形组合压缩通往卫浴的主要通道。

### closet-priority（示例：空间策略类型）

- variantDirection: 收纳功能主导动线，进门即被收纳包裹，释放睡眠区舒展度。
- variantNarrative: 用**"入口侧连续收纳带"空间策略**组织空间——SubAgent 自由决定收纳带跨越哪几段实墙（单段满墙 / 跨区域连续 / L 形包裹），按"填满有效段"原则取最长。
- variantAnchorSeed: 主收纳锚点=入口侧连续段（允许跨主体区+延伸区）
- variantAvoidance: 不让睡眠区被收纳挤到墙角失去舒展度。
````

#### brief 字段定义

| 字段 | 性质 | 内容要求 |
|------|------|---------|
| `variantDirection` | 一句话 | 本变体的**设计方向**——空间叙事的核心句。**禁止写具体家具配置**（"北墙满墙衣柜"是配置不是方向；"以收纳功能为视觉锚点"是方向）。 |
| `variantNarrative` | 一段话 | 本方向**为什么值得探索** + 设计哲学差异说明。这是给 variant-design-agent 在 SKILL 内做决策时的"WHY 输入"。 |
| `variantAnchorSeed` | **最多 1 条硬锚点** | 本变体的核心识别约束。**类型不限于"某家具固定到某墙面"**——参见下文"三种类型"段。 |
| `variantAvoidance` | 一句话 | 本方向应避免的反模式——通常用于区分本变体与兄弟变体的设计哲学。**禁止列具体家具禁止清单**。 |

#### variantAnchorSeed 的三种类型

AnchorSeed 是**1 条硬锚点**（数量约束），但**类型可以是任一种**——不限于"单家具的墙面位置"：

| 类型 | 锚定什么 | 字段填法模板 |
|------|---------|------------|
| **单家具锚点** | 一个主家具固定到某墙面/位置 | `<家具名>=<墙面/位置语义>` |
| **家具组合关系** | 两件家具的相对组合形态 | `<家具A>+<家具B>=<组合形态(L形/U形/对位/并排) + 位置>` |
| **空间策略** | 跨家具的整体空间组织逻辑 | `<策略名>=<空间区域语义>` |

具体的"哪种家具在哪面墙合理 / 哪种组合在该房型成立 / 哪种空间策略适合"由 SubAgent 在 variant-mode 内基于领域知识（房间策略文件、模块库规则）自由判断——**主控只负责锁"这个变体探索哪种类型的锚点"**，不替 SubAgent 做家具/墙面级别的决策。

WHY：用户期待的"多方案"是**几种思考方式的差异**，不是"同一思考方式下排列组合的结果"。三种 AnchorSeed 类型对应三种思考路径：

- **单家具锚点** = "我从睡眠/收纳/梳妆中**哪一件主家具开始锚**？"
- **家具组合关系** = "我让**哪两件家具形成什么组合**（L/U/对位）？"
- **空间策略** = "我用**什么空间组织逻辑**（释放主体区 / 入口包裹 / 异形区独立功能 / 更衣与睡眠融合）？"

如果主控的所有候选 AnchorSeed 都是单家具锚点（"床/衣柜/梳妆分别放哪"），必然漏掉"L 形衣帽工作站"、"开放式衣帽间"、"床+梳妆对位轴线"这类**组合/策略层面**的方案——这些方案在单家具锚点维度下无法表达。

#### 【必须】variantAnchorSeed 单决策点原则

每个变体只锁定 **1 条 AnchorSeed**（1 个核心决策点），不连续锁定 2-3 个决策。锁多了就退化回 v1 的"具体配置 brief"——subagent 失去全局重判机会，与单方案质量产生差距。

数量约束（1 条）与类型选择（三种之一）是独立维度——可以是"1 条单家具锚点"、"1 条组合关系锚点"或"1 条空间策略锚点"。

#### v1 反例 vs v2 正例

❌ **v1 反例**（不要用——把具体方案细节锁死在 brief）：

```markdown
### classic-west

- 核心意图：床靠西墙、北墙满墙衣柜、梳妆台靠东墙南端
- 锚点墙面/家具：床靠西墙，北墙东段衣柜，东墙梳妆台
- 必须保留的关系：1800 床 + 双床头柜，北墙 4070mm 满墙阵列
- 自由发挥空间：梳妆台具体宽度
- 必要的连带改动：无
```

问题：把 strategic-plan 阶段才该决定的事（衣柜墙、梳妆台位置）全部锁死在 brief，subagent 失去全局重判机会，且把"北墙 4070mm"这种未扣门侧净空的原始数据当成有效段。

✅ **v2 正例**（用这个——只锁 1 个核心决策点，其余交给 SKILL）：

```markdown
### classic-west

- variantDirection: 以最深处采光位为睡眠锚点，让收纳功能沿动线由静到动展开。
- variantNarrative: 床头墙锁定房间最长且远离门的连续实墙……
- variantAnchorSeed: 床头墙=最长安静实墙
- variantAvoidance: 不让主收纳远离主体区。
```

subagent 接收后，在 variant-mode 下自由决定衣柜墙、梳妆台位置、是否 L 形——SKILL 内部的双候选评估、L 形门槛、阵列前置扣减规则全部激活。

#### v2 内部反例 1：AnchorSeed 把"空间策略"狭化为"家具位置"

❌ **反例**：

```yaml
- slug: closet-extension
  variantDirection: 收纳功能整合至延伸区，释放主体区空间让睡眠区更舒展。
  variantAnchorSeed: 主衣柜=延伸区（东侧区域）   ← 单家具锚点（错误翻译）
```

问题：`variantDirection` 表达的是**空间策略**层面的方向（"释放主体区"），但 `variantAnchorSeed` 把它**翻译成"家具的具体墙面"**——语义被狭化。SubAgent 在 variant-mode 把"延伸区"当硬约束，搜索空间被限制在延伸区那一小段墙内（典型仅 1~2m），不会把衣柜阵列延伸到与延伸区**连续**的主体区墙段（虽然两者本是同一段连续实墙、合起来可达数米）。

✅ **正例**：

```yaml
- slug: closet-extension
  variantDirection: 收纳功能整合至延伸区，释放主体区空间让睡眠区更舒展。
  variantAnchorSeed: 主收纳锚点=入口侧连续段（允许跨主体区+延伸区）   ← 空间策略类型
```

把 AnchorSeed 的类型从"单家具锚点"提升到"空间策略"，让 SubAgent 自由决定具体阵列范围——配合 SKILL 内的"填满有效段"规则，自然产出最长阵列。

WHY：当 `variantDirection` 在"空间策略"层面表达方向时，`variantAnchorSeed` 应保持同层级（空间策略类型）；若降级翻译为"单家具锚点"，会让 SubAgent 的搜索空间被预设的"家具位置"提前缩小，违背"差异化在方向层"的设计哲学。

#### v2 内部反例 2：所有变体都是同一种 AnchorSeed 类型

❌ **反例**：

```yaml
variants:
  - slug: bed-west,        AnchorSeed: 床头墙=西墙              ← 单家具锚点
  - slug: vanity-window,   AnchorSeed: 梳妆台=南窗位             ← 单家具锚点
  - slug: closet-north,    AnchorSeed: 主衣柜=北墙               ← 单家具锚点
```

问题：3 个变体只是"把同一组家具在不同墙面上排列组合"——差异化退化为一维。漏掉"衣柜+梳妆台 L 形工作站"、"开放式衣帽间策略"、"睡眠+换衣融合区"等组合/策略层面的方向。

✅ **正例**（参见 §2.3 brief 模板示例：3 个示例跨 3 种类型）：

```yaml
variants:
  - slug: classic-west,        AnchorSeed: 床头墙=最长安静实墙       ← 单家具锚点
  - slug: l-shaped-station,    AnchorSeed: 衣柜+梳妆台=L 形组合      ← 家具组合关系
  - slug: closet-priority,     AnchorSeed: 主收纳锚点=入口侧连续段    ← 空间策略
```

3 个变体提供 3 种**思考方式**的差异化，而非 3 种**家具排列**的差异化——这是 multi-plan 应有的形态。违反此原则将被"AnchorSeed 类型必须至少跨 2 种"硬要求（下文）拒绝。

**slug 命名规则**（写在变体清单紧邻处的硬约束）：

- 字符集：`[a-z0-9-]`
- 长度：≤30 字符
- 语义清晰、可读（如 `dressing-front` 而非 `alt-1` / `variant-2`）
- 同一批次内**不重复**
- **不加 `alt-` 前缀**（`alt-{slug}` 是 module-relocation-agent 的命名约定，与本模式互斥）

**【必须】**`## 变体清单` 下必须用 ` ```yaml ``` ` fenced code block 包裹 `variants:` 列表，主控将用 yaml parser 解析。不要用纯缩进列表代替（缩进歧义会导致主控解析失败）。

**【必须】**`## 设计意图 briefs` 段的每个 brief 必须用 `### {slug}` 三级标题分割，**slug 与 YAML 头一致**。brief 内部禁止再用 `###` 三级标题（防止主控按标题切分时切碎一个 brief）；如需子结构请用 `####` 或无序列表。

**【必须】**每个 brief 必须**且仅**包含 v2 四字段（`variantDirection` / `variantNarrative` / `variantAnchorSeed` / `variantAvoidance`），用 `- 字段名: 内容` 的列表项形式列出。禁止使用 v1 字段（"核心意图" / "锚点墙面或家具" / "必须保留的关系" / "自由发挥空间" / "必要的连带改动"）——v1 字段鼓励写具体方案细节，会让 subagent 失去全局重判机会。

**【必须】**变体清单中各变体的 `variantAnchorSeed` **类型必须至少跨 2 种**（参见上文"AnchorSeed 三种类型"）。例如：

- ✅ 合法：3 个变体的 AnchorSeed 类型为 单家具锚点 + 家具组合关系 + 空间策略（跨 3 种）
- ✅ 合法：3 个变体的 AnchorSeed 类型为 单家具锚点 + 单家具锚点 + 家具组合关系（跨 2 种）
- ❌ 违规：3 个变体的 AnchorSeed 类型全是单家具锚点（仅 1 种）

WHY：仅锁数量（"每变体 1 条 AnchorSeed"）而不锁类型多样性，主控会全部用同一种类型枚举候选，产出"同一思考方式下的排列组合"而非"几种思考方式的差异"。multi-plan 的核心价值是让用户在"哲学不同"的方案间视觉决策，不是在"哲学相同但排列不同"的方案间挑数字。

**【建议】**3 个变体最佳是跨 3 种 AnchorSeed 类型——这是 multi-plan 最大化设计哲学差异化的形态。

#### F3 排除式 Ask 规则

multi-plan-overview 阶段如出现"显然不合理"的候选（如"床面对镜子是否接受"、"开门即对衣柜门是否接受"），**可以**用 `AskUserQuestion` **排除**该候选：

- **允许**问"排除哪种"（提供 a/b/c 选项，让用户勾掉显然不合理项）
- **【禁止】**问"选哪种"（终选交给 Web 端视觉决策；本 Skill 不替用户做终选）

WHY：multi-plan 的设计哲学是"用户在 Web 端看可视化方案后再做终选"；如果在 multi-plan-overview 阶段先让用户选定一种，等于把"视觉决策"提前到"文本决策"，违背设计意图。

#### N=1 退化路径

如果在排除式 Ask 后或自然推导后，变体清单候选**只剩 1 个**：

- **不写** `multi-plan-overview`
- **改写**普通 `strategic-plan`（按 `### 2.2` 节模板写完整战略层方案，不传 `variantId`，落到 canonical）
- 继续进入 `### 2.4` 写 `construction-brief` + 由编排层路由到 `generate-placement`

WHY：N=1 时 multi-plan 的并行价值消失，强写 multi-plan-overview + 派发单个 variant-design-agent 是无谓开销；退化为单方案模式让流程回归 single-plan 主干。

#### 退出声明

multi-plan 模式产出 `multi-plan-overview` 后：

```text
save_semantic_plan({
  zoneId,
  tag: "multi-plan-overview",
  planType: "derived",
  content: <上述 canonical 模板填充>
})
```

**【必须】**不传 `variantId`（`multi-plan-overview` 是 canonical-only tag，Server 强制校验，传 `variantId` 返回 400）。

**【必须】**`multi-plan-overview` 保存成功后**立即终止本 Skill**：

- 不进入 `### 2.4`（不写 construction-brief）
- 不调用 `load_semantic_plan` / `Write modules.json` / `validate_layout`
- 不派发任何 SubAgent（派发由主控负责）
- 等待主控读 canonical `multi-plan-overview`、提取 `variantSlugs[]`、并行派发 `variant-design-agent`

**WHY**：construction-brief 与 modules.json 是变体级产物，必须由 `variant-design-agent` 在各自的 `variants/{slug}/` 路径下生成；本 Skill 若继续写 canonical construction-brief，会污染所有变体的施工合同。

---

### 2.4 完整施工简报 -> `construction-brief`

**【必须】**若当前为 multi-plan canonical 模式（`exploreMode=true`），不应进入本节；construction-brief 由 `variant-design-agent` 调用本 Skill 的 variant-mode 分支生成。本节适用于：自主规划模式、参考消费模式、multi-plan N=1 退化路径、**variant-mode（`variantContext` 非空）**。

**目标**：把 `strategic-plan` 收束成 placement 唯一可读的完整合同。

#### 自主规划模式

在不推翻 `strategic-plan` 主要家具与分区前提下，补全：
- 可选家具（只能收束 `strategic-plan` 已发现的候选）
- 附属家具
- 关键留白
- 关键关系
- 最终落地表达
- 主家具扣减账本：原始墙段、扣减项、有效段、模块选择理由

#### 参考消费模式

在不推翻 `strategic-plan` 战略层方案前提下，补全：
- 最终家具体系
- 最终保留的空间关系
- 细节级适配理由
- 最终参考采纳与偏离摘要

**【必须】**`construction-brief` 必须让 placement 直接施工，不得要求 placement 回头解释 `reference_analysis`。

**【必须】**主家具条目必须写明关键尺寸推导：原始墙段、扣减项、有效段、选择该模块/尺寸等级的理由。若没有扣减，写“扣减项：无”。这不是坐标明细，而是让 placement 不再重新解释规则适用范围。

**【必须】**主家具锁定前必须完成“闭合施工预检”：从最终拟施工坐标/区间出发，把已选附属构件、主家具深度、门禁区、通道、相邻家具占用同时扣进可施工区间，不得只用单面墙原始长度判断成立。若预检失败，必须在 planning 阶段按房间策略 fallback，并把结果写入 `construction-brief`，不得把已知冲突留给 placement。

**WHY**：`construction-brief` 是施工合同。单个墙段长度只能证明某组家具本身可能放下，不能证明它与附属构件、必需功能和通道共同成立。闭合预检把“局部可放”升级为“全局可施工”，避免 placement 被迫改图。

**【必须】**若房间/家具策略定义了有序 fallback，且 `construction-brief` 选择的方案存在施工风险或允许现场适配，必须写独立章节 `## 合同内 fallback`。该章节只写三件事：触发条件、可自动执行的下一档方案、不可自动越界的边界；没有 fallback 时写“无”。

#### `construction-brief` canonical 结构

```markdown
# construction-brief 完整施工简报

## 主要家具
- [家具名]：墙面归属 / 朝向语义 / 尺寸等级或关键尺寸 / 原始墙段 -> 扣减项 -> 有效段 / 模块选择理由

## 可选/附属家具
- [家具名]：保留 / 补充 / 省略 + 锚点位置或原因

## 保留空段与关键留白
- [墙面或边段]：保留目的

## 关键关系与分区意图
- [邻接 / 对位 / 前后场 / 通行与静区关系]

## 合同内 fallback
- 触发条件：...
- 可自动执行的下一档方案：...
- 不可自动越界边界：...

## 参考采纳与偏离摘要
- [最终采纳项]
- [最终偏离项]
- [最终转译理由]

## 自动标记
- `[自动代决] ...` / `[自动适配] ...` / `- 无`
```

#### `construction-brief` 示例：带参考分析

```markdown
# construction-brief 完整施工简报

## 主要家具
- 主家具 A：核心功能带，墙面归属清楚，尺寸等级已定
- 主家具 B：辅助功能带，形成过渡界面

## 可选/附属家具
- 附属家具：保留，跟随主家具 A
- 可选家具：保留，位置来自 strategic-plan 已确认候选

## 保留空段与关键留白
- 主通行：保留前场到核心功能带的连续通道
- 关键环境面：保留必要留白，不布置高体量家具

## 关键关系与分区意图
- 动线为入口 -> 过渡功能 -> 核心功能
- 可选家具服从已确认的功能带归属，不改写 strategic-plan 战略

## 参考采纳与偏离摘要
- 采纳：前后场层次、核心功能后置、可选功能保留
- 偏离：原图展开方式按当前几何调整
- 转译理由：当前几何限制原图展开方式，优先保留空间组织和功能关系

## 自动标记
- [自动适配] 辅助家具同墙微调，但保持功能带关系不变
```

#### 保存规则

```text
save_semantic_plan({
  zoneId,
  tag: "construction-brief",
  planType: "derived",
  content,
  variantId: variantContext.variantSlug,   # variant-mode 必填
  referenceAnalysisTag: "vN"               # 参考消费模式必填（与 variantId 互斥）
})
```

**【必须】**`variantId` 与 `referenceAnalysisTag` 互斥（variant-mode 与参考消费模式互斥）；
自主规划模式与 multi-plan N=1 退化路径下两者都不传。

---

## 3. 约束

- `strategic-plan` 之后，主要家具策略与战略级分区不可在本 Skill 内被静默推翻
- `construction-brief` 只能补全，不得重新发明一套新的战略方向
- `placement` 只允许读取 `construction-brief`（multi-plan 模式下，`construction-brief` 由 `variant-design-agent` 触发 variant-mode 在各自 `variants/{slug}/` 目录内生成，placement 按 variant 路径读对应 `construction-brief`）
- 不在本 Skill 内写 `modules.json`
- 不在本 Skill 内调用 `load_semantic_plan`（**例外**：variant-mode 入场时 load canonical `spatial-skeleton` 取共享空间基线）
- multi-plan canonical 模式与 variant-mode 都不在本 Skill 内进入 `generate-placement`（multi-plan canonical 由主控派发 `variant-design-agent`；variant-mode 由 variant-design-agent 自行加载 `generate-placement`）

**【自由区域】**
- 可选家具的取舍（在 `optional-furniture-rules` 框架内）
- 家具间精确间距
- 设计意图措辞
- 细节级转译方式

**WHY**：
- 只把必须正确的阶段边界写成硬规则。
- 具体怎么把参考意图转译到当前户型，仍然留给设计判断，而不是把 prompt 写成分支爆炸的规则表。

---

## 4. 交接

按入场模式分支：

- **自主规划 / 参考消费模式**：本 Skill 完成 `construction-brief` 后，由编排层路由到 `generate-placement` 进行施工。
- **multi-plan canonical 模式**：本 Skill 完成 `multi-plan-overview` 后**即终止**，**不进入 `generate-placement`**。主控读 canonical `multi-plan-overview` 提取 `variantSlugs[]`，并行派发 `variant-design-agent`；每个 variant agent 调用本 Skill 的 variant-mode 分支完成自己变体的 strategic-plan / construction-brief，然后自行加载 `generate-placement`（详见 `variant-design-agent.md`）。
- **multi-plan N=1 退化**：变体只剩 1 个时，Skill 自动改写普通 `strategic-plan` → 继续 `construction-brief` → `generate-placement`，与自主规划模式收尾一致。
- **variant-mode（variant-design-agent 调用）**：本 Skill 完成 variant 的 `construction-brief`（带 variantId）后**即终止**。调用方（variant-design-agent）自行加载 `generate-placement` 进入施工——本 Skill 不路由到 placement。若 `variantAnchorSeed` 不成立则在 strategic-plan 阶段提前认输（`[自动改图建议]`），不写 construction-brief。
