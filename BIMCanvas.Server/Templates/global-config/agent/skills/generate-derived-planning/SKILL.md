---
name: generate-derived-planning
description: |
  Generate 推导规划 Skill。用于 derived 与 reference-informed-derived 路径的 Stage 1 + Stage 2。
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

**reference-informed-derived 规则**：
- 如果用户附带图片但未要求忠实还原，只把图片当作补充上下文
- 图片可用于理解风格、家具偏好、功能需求
- 图片不是图纸原文，不可直接绑定墙面归属

---

## 2. 规划

### 2.1 空间骨架 -> v0.1

以 `design_evaluation.md` 的品质维度完成空间阅读：

- 动线方向
- 纵深层次
- 采光轴
- 初步设计意图

`v0.1` 只写空间骨架，不写具体家具坐标。

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

**【必须】**继续遵守“战略选择 vs 战术选择”的区分：
- 战略选择：可影响日常体验，需要用户确认；无 `AskUserQuestion` 时按推荐方案自动代决并上报
- 战术选择：由规范直接决定，自主执行

**【必须】**调用：

```text
save_semantic_plan({ zoneId, version: "v0.3", planType: "derived", content })
```

---

## 3. 约束

- `v0.2` 之后，主要家具墙面归属不可在本 Skill 内被推翻
- 本 Skill 可参考图片，但 reference-informed-derived 仍然是主动设计，不是照图翻译
- 不在本 Skill 内写 `modules.json`
- 不在本 Skill 内调用 `load_semantic_plan`

---

## 4. 交接

本 Skill 完成后，下一步应进入 `generate-placement`。

`generate-placement` 会：

1. 重新加载当前生效图纸
2. 读取施工规范
3. 将语义图纸转成坐标落地
