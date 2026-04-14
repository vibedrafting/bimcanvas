---
name: generate-planning
description: |
  Generate 规划 Skill。用于主动设计（derived）、参考启发式设计（reference-informed-derived）
  以及消费已冻结 reference_analysis 的 constrained planning。
---

# Generate 规划

> 你在本 Skill 中是设计师，不是翻译官。你的职责是把空间约束压缩成自包含的语义合同，并分阶段提交 `v0.1 / v0.2 / v0.3`。

## 职责与输出

- 输入：用户需求、项目 README、设计区几何数据、房间策略文件、模块库、可选家具规则、可选的 `reference_analysis`
- 输出：
  - `v0.1` 空间骨架
  - `v0.2` 主要家具墙面归属 + 分区结论
  - `v0.3` 完整设计合同（placement 唯一输入）

**【必须】**planning 是唯一的语义压缩点：

- 读取 reference_analysis
- 消化约束与建议
- 把已采纳的参考决策显式写入 semantic plan
- 把未采纳的条目标成 `[偏离参考]` 或 `[未采纳参考项]`

**【必须】**本 Skill 只负责 Stage 1 + Stage 2，不负责坐标化施工。

---

## 入场动作

先根据当前任务语义判断执行模式：

### free mode

适用于：
- `derived`
- `reference-informed-derived`
- 或当前任务没有冻结的 reference_analysis

### constrained mode

适用于：
- 主控已先执行 `generate-reference-analysis`
- 当前任务明确要求消费已冻结的 reference_analysis

**constrained mode 入场动作**：

1. 调用 `load_reference_analysis(zoneId)` 读取最新参考分析
2. `status=ok` → 进入 constrained mode
3. `status=missing` → 停止并上报“缺少已冻结的 reference_analysis，不能进入受约束规划”

**free mode 禁止事项**：

- 不得因为历史目录里碰巧存在 `reference_analysis.json` 就自动切到 constrained mode
- `reference-informed-derived` 的图片只作上下文，不作图纸原文

---

## 1. 感知

1. 调用 `mcp__canvas__request_background_screenshot`
   - 截图返回后，先确认方位：在截图中定位门窗，与 zone boundaries 交叉验证方向
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

---

## 2. 规划

### 2.1 空间骨架 -> v0.1

**目标**：独立分析当前项目户型，建立空间理解。

**【必须】**无论是否有 reference_analysis，都独立分析当前户型。reference 输入不能覆盖 `v0.1` 结论。

以 `design_evaluation.md` 的品质维度完成空间阅读：

- 动线方向
- 纵深层次
- 采光轴
- 初步设计意图

`v0.1` 只写空间骨架，不写具体家具坐标。

**保存规则**：

```text
save_semantic_plan({ zoneId, version: "v0.1", planType: "derived", content })
```

`v0.1` 可以不写 `referenceAnalysisVersion`。

---

### 2.2 主要家具 + 分区结论 -> v0.2

#### free mode

1. 基于房间策略确定主要家具墙面归属
2. **【必须】**加载 `generate-zoning`
3. 先用主要家具墙面需求过滤分区方案，再确定功能定义
4. 产出：
   - 主要家具墙面归属
   - 分区结论或“不分割”
   - 设计目标

#### constrained mode

**Step 1：提取硬约束**

从 `reference_analysis.content` 中识别：
- 已确认的关键锚点
- 已确认的家具选型
- 已确认的非侵占细节

**作用**：作为可行解空间过滤器，不可静默违反。

**Step 2：执行主干逻辑**

仍然执行：
- 房间策略
- `generate-zoning`
- 战略选择 / AskUserQuestion

但要遵守：
- 优先采纳已确认锚点与选型
- 若硬约束与几何条件冲突，必须 AskUserQuestion（主控）或显式标注 `[偏离参考]`
- 不得把“看起来更合理”当作偏离理由；偏离理由必须是几何条件、建筑锚点或房间策略冲突

**Step 3：吸收软提示**

从 `reference_analysis.content` 中识别：
- 分区意图
- 设计理念
- 家具关系

**作用**：候选排序依据，不是不可违反的合同。

若无法采纳：
- 在 `v0.2` 中标注 `[偏离参考]`
- 或标注 `[未采纳参考项]`

#### 优先级顺序

1. 已确认硬约束
2. 几何硬约束（zone boundaries、exclusions）
3. 房间策略
4. 参考建议（软提示）
5. 设计原则

#### 战略选择规则

- 若当前执行者具备 `AskUserQuestion`，遇到显著影响日常体验的多方案，必须向用户确认
- 若当前执行者不具备 `AskUserQuestion`，选择推荐方案继续，并在最终结果中上报“自动代决”

#### 保存规则

```text
save_semantic_plan({
  zoneId,
  version: "v0.2",
  planType: "derived",
  content,
  referenceAnalysisVersion: "vN"   # constrained mode 必填
})
```

`v0.2` 提交后，立即读取 `references/optional-furniture-rules.md`。

---

### 2.3 可选家具与完整图纸 -> v0.3

#### free mode

在不推翻 `v0.2` 主要家具墙面归属的前提下，补充：

- 可选家具
- 附属家具
- 完整设计目标

#### constrained mode

**核心要求**：`v0.3` 必须是自包含合同，不允许 placement 再去理解 raw reference。

把以下内容显式写进 `v0.3`：

- 已采纳的参考决策
  - 关键锚点
  - 关键选型
  - 保留空段 / 关键留白
  - 关键家具关系
- 未采纳的参考条目及原因
  - `[偏离参考] ...`
  - `[未采纳参考项] ...`

**【必须】**删除“参考图家具集合为上限”“参考图没有的不主动新增”这类翻译式规则。现在的目标不是忠实复刻，而是带着参考意图做主动设计。

**可接受的补充方式**：

- 若参考分析明确了特殊家具或功能关系，优先保留
- 若参考分析只提供了部分锚点，可以按房间策略补齐常规附属家具
- 所有补齐项必须与已确认锚点和空间意图一致

#### 保存规则

```text
save_semantic_plan({
  zoneId,
  version: "v0.3",
  planType: "derived",
  content,
  referenceAnalysisVersion: "vN"   # constrained mode 必填
})
```

---

## 3. 约束

- `v0.2` 之后，主要家具墙面归属不可在本 Skill 内被推翻
- `reference-informed-derived` 仍然是主动设计，不是照图翻译
- constrained mode 下，硬约束不可静默违反；软提示可放弃但需标注
- 不在本 Skill 内写 `modules.json`
- 不在本 Skill 内调用 `load_semantic_plan`

**【自由区域】**

- 可选家具的取舍（在 optional-furniture-rules 框架内）
- 家具间精确间距
- 设计意图的措辞
- `v0.1` 的展开程度

---

## 4. 交接

本 Skill 完成后，由编排层路由到 `generate-placement` 进行施工。
