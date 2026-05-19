---
name: edit-workflow
description: |
  BIMCanvas 编辑任务工作流。
  当用户需要"移动"、"删除"、"旋转"、"调整"等单一修改操作时使用此工作流。
allowed-tools: Read, Write, Edit, Glob, Grep, mcp__canvas__validate_layout, mcp__canvas__request_background_screenshot, AskUserQuestion
---

# Edit 工作流（小范围设计决策）

> Edit 是 placement 在已有布置上的局部小型版——**决策依据与 placement 同源**：模块自身规则（`module_library.json[moduleId].agent_config`）+ 房间策略（`references/{room}.md`）。差别只是触发范围（单一/少数模块）。

**触发条件**：关键词"移动 / 删除 / 旋转 / 调整"。

---

## 第一步：复杂度判定（不读文件，纯语义）

| 简单任务（fast path） | 复杂任务（full path） |
|---------------------|---------------------|
| 用户提供 spatialMarks AABB | 用户用模糊词："调整 / 优化 / 不合理 / 不舒服 / 看看怎么改" |
| 用户给精确位置语义："靠 X 墙"/"X 旁边"/"X 上面" | 没给目标位置 / 方向 |
| 单模块 + 单动作（移动/删除/旋转 N 度） | 多模块协同 / 涉及战略取舍 |

WHY：用户已做完决策时，Agent 只需"执行 + 兜住领域硬约束"；用户期望 Agent 帮忙决策时，才需要更多上下文。

**两条路径共享同一套领域规则**——区别只在读多少上下文、是否截图、是否触发 `AskUserQuestion`。

---

## 简单路径（fast path）

### 必读集

按动作类型最小化读取，**不要读全部**：

| 动作 | 必读 | 何时再加 `references/{room}.md` |
|------|------|------------------------------|
| 删除 | `zones.json` + 目标 `modules.json` | 永不需要 |
| 旋转 N 度 | `zones.json` + 目标 `modules.json` | 永不需要 |
| 移动（非 parametric 模块） | `zones.json` + 目标 `modules.json` + `module_library[moduleId].agent_config` | 用户位置语义模糊（"靠右上"等）时 |
| 移动（parametric 模块） | 上述三项；尺寸算法 + 强先验已内置 SKILL.md Step 2，**不读 `references/{room}.md`** | 只在复杂路径读 |

**跳过**：`README.md`、`computed/exclusions.json`、修改前后截图。
WHY：决策面已被"用户输入 + 模块规则 + 房间策略"覆盖。`exclusions` 由 `validate_layout` 兜底。

### 核心约束（一条）

**Edit 落位与尺寸决策由"模块自身规则 + 房间策略"共同决定，与 placement 同源；不要绕开领域规则自己发明算法。**

WHY：edit 的本质是"小范围设计决策"，不是"机械搬运"。`agent_config.topology_rules`（如"靠墙"）+ `relation_rules`（如顶角规则）+ `references/{room}.md` 的尺寸算法已经覆盖 90% 决策；自己重新推导既慢又易遗漏。

### 流程

1. 读必读集（按上表）。
2. 调 §AABB 落位 + 尺寸决策。
3. 写 `facing.semantic`（推荐，由 validate 归一化为 `value`）。
4. 用 `Write` / `Edit` 直接编辑 `modules.json`（保留 `schemeMetadata` 字段）→ `validate_layout()`。
   - 通过 → 完成。
   - 失败 → 此时才按需读 `computed/exclusions.json`，做几何级修正（同墙微调 / `limits` 内收缩 / 收缩附属件），重新 `Write` → validate。
5. 不主动截图。

**【必须】**修改 modules.json 用 `Write` / `Edit` 工具；编辑 `modules` 数组时**必须保留外层 `schemeMetadata` 字段**——canonical 默认 `{summary: ""}`，variant 已含 register_variant 写入的 summary，都不要清空。

---

## 复杂路径（full path）

【追加读取】

- `references/{room}.md` 全文（不只是单模块段落）
- 按需 `mcp__canvas__request_background_screenshot` 看相邻关系

【追加机制】

- 领域规则标记为"战略选择"时 → `AskUserQuestion`（与 placement 一致）。
- 修正循环可做"几何级"，**不做语义级重设计**——edit 范围始终限于用户提到的模块及其直接邻接关系。

【流程】简单路径流程 + 上述追加项。

---

## AABB 落位 + 尺寸决策

输入：`AABB`（或精确位置语义）/ `moduleId` / 目标 zone 几何（来自 `zones.json`）。

**Step 1 — 落位（找墙边）**

- 读 `module_library[moduleId].agent_config.topology_rules`。
- "【必须】靠墙" → 在 AABB 邻域（向外扩 ≤ 200mm）找最近实墙边，模块对应边对齐到 mm 级；朝向取墙的内法向。
- "居中" → 取 AABB 几何中心；朝向参考相邻锚点。
- "成组" → 锚定 `relation_rules` 主件后推导。

**Step 2 — 尺寸（仅 parametric 模块；强默认：尺寸最大化）**

⚠️ **不要回到 default size。** `module_library[moduleId].size` 只是占位值，不是设计答案——Edit parametric 模块时必须执行"尺寸最大化"路径。

WHY：parametric 模块的 default size 是 `limits.min`（最小合法值），Agent 容易把它当"标准答案"凭感觉用——但 default 是占位，不是设计。让模块由空间决定尺寸而非直觉，是 parametric 字段存在的全部意义。

算法（3 步）：

1. 计算贴墙后该墙段的有效长度（扣除相邻家具占用）。
2. 取 `min(有效长度, limits.max)` 作为目标 `width`/`depth`。
3. 顶角规则：若另一侧距相邻锚点（墙/家具）< 600mm 且非门口/通道侧 → 在 `limits` 内扩展消除窄缝。

非 parametric 模块跳过 Step 2，保持原 `size`。

**Step 3 — 碰撞规避**

- 与同 zone 相邻模块碰撞 → 沿"贴墙方向 90°"平移最小距离至无冲突。
- 不主动检查 `exclusions`；交给 `validate_layout` 兜底（失败再回补救）。

WHY：用户标注表达"我希望 X 在这一带"，落位语义表达"X 应该如何使用"，尺寸语义表达"占满有效段而非默认值"。三者结合才是合理的位姿；任一缺位都退化为合规式居中或机械平移。

---

### Worked example：parametric 模块 + AABB（流程决策范本）

输入：

- `moduleId` = `mod_vanity_custom_001`（parametric: width [800, 2000], depth enum [400, 600], 【必须】靠墙）
- AABB = `[13650, 4200, 14100, 5100]`
- 邻居：右墙 X≈14100（实墙），上方柜体底 Y≈5150

❌ **陷阱推理**（不要这么做）：

```
"width 默认 800 → 居中放 AABB 内 Y=4250–5050"
→ 上方距柜体底 100mm 窄缝、下方距墙拐角 50mm 窄缝；未触发顶角规则；落到 default 的合规式居中
```

✅ **正确决策链**：

```
1. 找墙 → 右墙 X=14100，模块右边对齐 14100，朝向 west
2. 算有效段 → 上下锚点：下=4200（墙拐角）、上=5150（柜体底）→ 有效段 = 950mm
3. width = min(950, limits.max=2000) = 950
4. depth 取 400（贴近 AABB 暗示的 450mm 深度，enum 内较小值）→ X = [13700, 14100]
5. bounds = [[13700, 4200], [14100, 4200], [14100, 5150], [13700, 5150]]
   facing.semantic = "west"
6. 顶角已自动消除（两端贴齐）
```

**关键直觉**：见 parametric + AABB → 立刻走"尺寸最大化"路径；不要先看 default size 再考虑要不要扩。

---

## 示例

> 仅决策结构骨架。具体家具示例见 `references/{room}.md` 与 `module_library.json[moduleId].agent_config`。

简单移动型（parametric 模块）：

```
判定：简单（有 AABB + 单模块单动作）
读 zones / modules / module_library[moduleId]（不读 references/{room}.md）
Step 1 找墙边贴齐 → Step 2 尺寸最大化（min(有效段, limits.max) + 顶角）→ Step 3 碰撞规避
Write modules.json（保留 schemeMetadata） → validate_layout
```

简单移动型（非 parametric 模块）：

```
判定：简单
读 zones / modules / module_library[moduleId]
Step 1 找墙边贴齐 → Step 3 碰撞规避（跳过 Step 2）
Write modules.json（保留 schemeMetadata） → validate_layout
```

复杂调整型（"调整下梳妆台位置"）：

```
判定：复杂 → 读 zones / modules / module_library / references/{room}.md 全文
（必要时）截图 / AskUserQuestion 确认意图
按 §AABB 落位 + 尺寸决策 + 战略规则
Write modules.json（保留 schemeMetadata） → validate_layout
```

删除 / 旋转：

```
判定：简单 → 读 zones / modules → 调整 modules 数组 → Write modules.json（保留 schemeMetadata） → validate_layout
```

---

## facing 字段写入约定

- `value`：常规读取阶段的真理（数值方向向量）。
- `semantic`：AI 输入槽（"north"/"south"/"east"/"west"/"northeast" 等）。
- 同时存在时常规读取只认 `value`；`validate_layout` 会用有效 `semantic` 覆盖 `value` 并清空 `semantic`。
- **推荐**：默认写 `semantic`，由 validate 归一化到 `value`。
