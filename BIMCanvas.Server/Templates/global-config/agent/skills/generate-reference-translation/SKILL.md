---
name: generate-reference-translation
description: |
  Generate 参考图翻译 Skill。用于 reference-translation 路径的 Stage 1 + Stage 2。
  当用户明确要求“照这个来”“按这张图还原”“忠实落地参考图”时加载。
---

# Generate 参考图翻译

> 你在本 Skill 中是翻译官。你的职责是把参考图中的布局意图翻译成语义方案图纸，而不是主动重设计。

## 输入白名单

在 `save_semantic_plan(v0.2)` 之前，只允许使用以下输入：

- 用户文字
- 用户参考图片
- 项目 `README.md`
- `schemes/zones.json`
- `computed/exclusions.json`
- `mcp__canvas__get_zone_boundaries`
- `modules/module_library.json` 的契约层字段：`id / tags / size / limits`

**【禁止】**在本 Skill 中读取：

- `references/design_principles.md`
- `references/design_evaluation.md`
- `references/bedroom.md`
- `references/bathroom.md`
- `references/livingroom.md`
- `module_library.json` 中的 `agent_config`
- `generate-zoning`

WHY：本 Skill 需要“只有眼睛和尺子，没有设计教科书”。

---

## 1. 感知与对比

1. 读取当前设计区信息与边界语义
2. 对照参考图做户型对比，至少检查：
   - 墙数
   - 转角形状
   - 门窗数量
   - 门窗相对位置
3. 先建立墙面对应表，再识别家具

墙面对应表格式：

```markdown
| 参考图方位 | 当前 zone 墙面 | 对应依据 |
|-----------|---------------|---------|
| 图中上方   | 北墙          | ... |
| 图中右侧   | 东墙          | ... |
```

---

## 2. v0.1 视觉原文

逐件记录：

- 家具名称或候选类别
- 靠哪面墙
- 朝向
- 视觉依据
- 场景类型 A/B

格式要求：

```text
[家具] -> [墙面]，[朝向/位置]（视觉依据：...）
```

**【必须】**关键锚点必须明确到墙面名，不允许只写“偏左”“偏南”。

**歧义处理**：
- 当前执行者具备 `AskUserQuestion`：列出候选 + 视觉依据，向用户确认；用户仍不确定则要求补图，并停止在 v0.1
- 当前执行者不具备 `AskUserQuestion`：保留歧义记录，进入 v0.2 的工程兜底

**【必须】**调用：

```text
save_semantic_plan({ zoneId, version: "v0.1", planType: "reference", content })
```

---

## 3. v0.2 完整映射

将 v0.1 的家具映射到模块库，并确定最终墙面归属。

映射依据顺序：

1. 视觉特征
2. zone tags 语境
3. 模块尺寸兼容性

### 主控 Agent 模式

- 若关键锚点歧义仍存在，必须 `AskUserQuestion`
- 若 `v0.2` 与 `v0.1` 视觉原文发生墙面不一致，也必须 `AskUserQuestion`
- 用户仍无法确认时：要求补图，停止，不提交 `v0.2`

### layout-agent 模式

当前执行者若不具备 `AskUserQuestion`，进入工程兜底：

1. 优先保留 `v0.1` 视觉原文墙面
2. 若该墙面不可施工，避开窗墙
3. 避开门段及门段缓冲
4. 优先有效长度足够的候选墙段
5. 优先不破坏主通行
6. 优先满足当前 room type 的基础可施工性

**【必须】**所有自动改写都要在 `v0.2` 中显式标记为“自动适配”，并说明 WHY。

**【必须】**调用：

```text
save_semantic_plan({ zoneId, version: "v0.2", planType: "reference", content })
```

---

## 4. 约束

- 本 Skill 不写 `modules.json`
- 本 Skill 不读取任何外部设计 references
- 本 Skill 不进入 `generate-zoning`
- 本 Skill 完成后由 `generate-placement` 负责落地

---

## 5. 交接

本 Skill 完成后，下一步应进入 `generate-placement`。

`generate-placement` 会：

1. 显式调用 `load_semantic_plan`
2. 把当前生效图纸转成坐标
3. 在 validate 闭环中落地
