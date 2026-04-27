---
name: edit-workflow
description: |
  BIMCanvas 编辑任务工作流。
  当用户需要"移动"、"删除"、"旋转"、"调整"等单一修改操作时使用此工作流。
allowed-tools: Read, Write, Edit, Glob, Grep, mcp__canvas__validate_layout, mcp__canvas__request_background_screenshot, AskUserQuestion
---

# Edit 工作流（小范围设计决策）

> Edit 是 placement 在已有布置上的局部小型版——同样要落位贴墙、参数化适配、避让禁区。差别只是触发范围（单一/少数模块）。
> 知识入口与 placement 同源：`module_library.json[moduleId].agent_config` 与 `references/{room}.md`。

**触发条件**：关键词"移动 / 删除 / 旋转 / 调整"。

---

## 第一步：复杂度判定（不读文件，纯语义）

| 简单任务（fast path） | 复杂任务（full path） |
|---------------------|---------------------|
| 用户提供 spatialMarks AABB | 用户用模糊词："调整 / 优化 / 不合理 / 不舒服 / 看看怎么改" |
| 用户给精确位置语义："靠 X 墙"/"X 旁边"/"X 上面" | 没给目标位置 / 方向 |
| 单模块 + 单动作（移动/删除/旋转 N 度） | 多模块协同 / 涉及战略取舍 |

走简单路径还是复杂路径，由以上信号决定。**两条路径共享相同的领域硬约束**——区别只在读多少上下文、是否截图、是否触发 `AskUserQuestion`。

WHY：用户已做完决策时，Agent 只需要"执行 + 兜住领域硬约束"；用户期望 Agent 帮忙决策时，才需要更多上下文。一个流程同时覆盖两者会让简单任务变重，让复杂任务变浅。

---

## 简单路径（fast path）

【必读最小集】

1. `schemes/zones.json` — 定位目标叶子分区
2. `schemes/{leaf}/modules.json` — 目标模块 + 相邻家具
3. `modules/module_library.json[moduleId].agent_config` — `topology_rules` / `morphology` / `relation_rules`

【明确跳过】`README.md`、`references/{room}.md`、`computed/exclusions.json`、修改前后截图。

WHY：简单任务的决策面已被"用户输入 + 模块自身规则"完全覆盖。`exclusions` 由 `validate_layout` 兜底；截图在没有视觉反馈链路时不影响决策。

【质量硬约束（不因简化而豁免）】

- **必须**执行 `module_library[moduleId].agent_config.topology_rules` 中的【必须】项（如"靠墙"）。
- **必须**对 `morphology.strategy = "parametric"` 的模块按 §AABB 落位算法 决定 `size`，不得保留 default size 上画布。
- **必须**执行 `relation_rules` 中的"顶角规则"（剩余 < 600mm 非门口/通道侧 → `limits` 内扩宽或贴齐）。

WHY：edit 是"小范围设计决策"，不是"机械搬运"。质量底线由模块自身规则决定，与流程长短无关。

【流程】

1. 读最小集。
2. 按 §AABB 落位算法 或精确位置语义重算 `bounds`。
3. parametric 模块在 `limits` 内取 `min(可用墙段, limits.width.max)`，执行顶角规则。
4. 写 `facing.semantic`（推荐）或 `facing.value`，不同时写两个有效值。
5. Write → `validate_layout`。
   - 通过 → 完成。
   - 失败 → **此时**才按需读 `computed/exclusions.json`，做几何级修正（同墙微调 / `limits` 内收缩 / 收缩附属件），重新 Write → validate。
6. 不主动截图；用户后续可在画布观察。

---

## 复杂路径（full path）

【追加读取】

- `references/{room}.md` — 房间级战略规则（如卧室 L 形组合、采光偏好）
- 按需 `mcp__canvas__request_background_screenshot` 看相邻关系

【追加机制】

- 领域规则标记为"战略选择"时 → `AskUserQuestion`（与 placement 一致的边界）。
- 修正循环可做"几何级"，**不做语义级重设计**——edit 范围始终限于用户提到的模块及其直接邻接关系。

【流程】简单路径流程 + 上述追加项。

---

## AABB 落位算法（机制）

输入：`AABB` / `moduleId` / 目标 zone 几何（来自 `zones.json`）。

1. 读 `module_library[moduleId].agent_config.topology_rules`，识别使用方式（靠墙 / 居中 / 成组）。
2. 在 AABB 邻域（向外扩 ≤ 200mm）内查找匹配几何特征：
   - "【必须】靠墙" → 找最近实墙边对齐到 mm 级；朝向取墙的内法向。
   - "居中" → 取 AABB 几何中心。
   - "成组" → 锚定 `relation_rules` 主件后推导。
3. 与相邻模块碰撞检查；冲突 → 沿"贴墙方向 90°"平移最小距离至无冲突。
4. parametric + 贴齐后另一侧距相邻锚点（墙/家具）< 600mm 且非门口/通道侧 → 执行顶角规则，在 `limits` 内**扩展尺寸**消除窄缝。

WHY：用户标注表达"我希望 X 在这一带"，落位语义表达"X 应该如何使用"。两者结合才是合理的位姿；缺任一侧都退化为合规式居中。

---

## 示例

> 仅示意决策结构。具体家具示例见 `references/{room}.md` 与 `module_library.json[moduleId].agent_config`。

简单移动型：

```
判定：简单（有 AABB） → 读 zones / modules / module_library[moduleId]
按 AABB 落位算法重算 bounds（贴墙 + 顶角扩展尺寸）
Write → validate_layout
```

复杂调整型（"调整下梳妆台位置"）：

```
判定：复杂 → 读 zones / modules / module_library / references/bedroom.md
（必要时）截图 / AskUserQuestion 确认意图
按落位算法 + 战略规则重算
Write → validate_layout
```

删除型：

```
判定：简单 → 读 zones / modules → 移除项 → Write → validate_layout
```

旋转型：

```
判定：简单 → 读 zones / modules → 修改 facing.semantic → Write → validate_layout
```

---

## facing 字段写入约定

- `value`：常规读取阶段的真理（数值方向向量）。
- `semantic`：AI 输入槽（"north"/"south"/"east"/"west"/"northeast" 等）。
- 同时存在时常规读取只认 `value`；`validate_layout` 会用有效 `semantic` 覆盖 `value` 并清空 `semantic`。
- **推荐**：默认写 `semantic`，由 validate 归一化到 `value`。
