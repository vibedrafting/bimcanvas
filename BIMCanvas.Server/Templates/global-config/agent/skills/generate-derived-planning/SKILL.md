---
name: generate-derived-planning
description: |
  Generate 推导规划 Skill。用于主动设计（derived）与参考启发式设计（reference-informed-derived）路径的 Stage 1 + Stage 2。
  当主控 Agent 已判定为“主动设计”或“图片只作参考”时加载。
---

# Generate 推导规划

> 你在本 Skill 中是设计师，不是翻译官。你的职责是基于空间约束主动推导布局图纸，并把图纸分阶段提交为语义方案。

## 职责与输出

- 输入：用户需求、项目 README、设计区几何数据、房间策略文件、模块库、可选家具规则
- 输出：
  - `v0.1` 空间骨架
  - `v0.2` 主要家具墙面归属 + 分区结论
  - `v0.3` 完整图纸
- 每次提交都必须调用：
  - `save_semantic_plan({ zoneId, version, planType: "derived", content })`

**【必须】**本 Skill 只负责 Stage 1 + Stage 2，不负责坐标化施工。

---

## 1. 感知

1. **单独调用截图**：`mcp__canvas__request_background_screenshot`
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
save_semantic_plan({ zoneId, version: "v0.1", planType: "derived", content })
```

### 2.2 主要家具 + 分区结论 -> v0.2

1. 基于房间策略确定主要家具墙面归属
2. **【必须】**加载 `generate-zoning`
3. 先用主要家具墙面需求过滤分区方案，再确定功能定义
4. 产出：
   - 主要家具墙面归属
   - 分区结论或“不分割”
   - 设计目标

**战略选择规则**：
- 若当前执行者具备 `AskUserQuestion`，遇到会显著影响日常体验的多方案，必须向用户确认
- 若当前执行者不具备 `AskUserQuestion`，选择当前推荐方案继续，并在最终汇报中上报“自动代决”

**【必须】**调用：

```text
save_semantic_plan({ zoneId, version: "v0.2", planType: "derived", content })
```

**【必须】**`v0.2` 提交后，立即读取 `references/optional-furniture-rules.md`。

### 2.3 可选家具与完整图纸 -> v0.3

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

**【必须】**继续遵守”战略选择 vs 战术选择”的区分：
- 战略选择：可影响日常体验，需要用户确认；无 `AskUserQuestion` 时按推荐方案自动代决并上报
- 战术选择：由规范直接决定，自主执行

**【必须】**调用：

```text
save_semantic_plan({ zoneId, version: "v0.3", planType: "derived", content })
```

---

## 3. 约束

- `v0.2` 之后，主要家具墙面归属不可在本 Skill 内被推翻
- 本 Skill 可参考图片，但参考启发式设计（`reference-informed-derived`）仍然是主动设计，不是照图翻译
- 不在本 Skill 内写 `modules.json`
- 不在本 Skill 内调用 `load_semantic_plan`

**【自由区域】**

- 可选家具的取舍（在 optional-furniture-rules 框架内）
- 家具间精确间距
- 设计意图的表达方式
- v0.1 空间骨架的详细程度

---

## 4. 交接

本 Skill 完成后，由编排层路由到 `generate-derived-placement` 进行施工。
