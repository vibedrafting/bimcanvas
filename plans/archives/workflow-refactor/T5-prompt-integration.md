# T5：工作流提示词重构 — 集成 boundarySegments

> **状态**：📋 计划完成，待审批
> **前置依赖**：T4 Zone 边界语义化（核心实现已完成）
> **分支**：`refactor/workflow-zoning`

---

## Context

T4 已交付 `get_zone_boundaries` MCP 工具，为每个 Zone 计算边界段语义分类（wall/passage/door/window）。但当前五个 Skill 提示词仍要求 Agent 从 rawBoundary + openings.json 手动推断墙面性质——这是不可靠且认知消耗大的操作。子分区场景下（如 dz_1 西边既有墙又有通道），Agent 完全无法正确推断。

本次重构将墙面分析从"AI 手动推断"升级为"数据驱动筛选"：Agent 直接使用 boundarySegments 的 wall/passage/door/window 分类，不再需要推断。

---

## 修改范围

| 文件 | 改动量 | 核心变更 |
|------|--------|----------|
| canvas.py `_format_zone_boundaries` | 中 | MCP 输出重构：按墙面分组 + 方位标签 + 实墙摘要 |
| generate-workflow/SKILL.md | 中 | 感知阶段新增 `get_zone_boundaries` 调用 |
| generate-bedroom/SKILL.md | 大 | 空间理解补充重写 + 示例改造 |
| generate-livingroom/SKILL.md | 小 | 电视墙候选改为数据筛选 + passage 约束 |
| generate-zoning/SKILL.md | 中 | passage 概念说明 + 分区后二次调用 |
| generate-bathroom/SKILL.md | 极小 | 门位置判断引用 boundarySegments |

不修改：BIMCANVAS.md、layout-agent.md、design_principles.md（不涉及空间分析数据源）

### 设计语言原则

提示词中的墙面分析存在两个层面：
- **数据获取层**（程序性）：告诉 Agent 如何从 boundarySegments 读取数据 → 使用数据术语（wall 段、door 段、passage 段）
- **设计推理层**（语义性）：告诉 Agent 如何用设计思维做决策 → 保留设计语言（实墙、窗墙、门段缓冲）

WHY：设计语言激活 AI 的室内设计知识（机制 2），数据语言仅指引操作步骤。全面替换为数据语言会削弱 AI 的设计推理能力。

---

## 步骤 0：MCP 输出格式优化（canvas.py）

**目标**：将 `_format_zone_boundaries` 的输出从"段序列平铺"重构为"按墙面分组 + 方位标签 + 实墙摘要"，让 AI 直接获得墙面级认知，无需自行分组。

### 当前输出格式（段序列平铺）

```
=== Zone 边界段数据（1 个 zone）===

--- rz_2 (8 段) ---
  wall: [11675,10700] → [11188,10700]
  window(wi_6): [11188,10700] → [9988,10700]
  wall: [9988,10700] → [9500,10700]
  wall: [9500,10700] → [9500,7900]
  ...
```

问题：AI 需要自行判断哪些段属于同一面墙、计算长度、确定方位——本质是把"墙面分组"这个计算任务推给了 AI。

### 新输出格式（墙面级认知）

```
=== rz_2 边界语义 (4 面墙) ===

北墙 | 总长 2175mm | 实墙 975mm
  wall 487mm [11675,10700]→[11188,10700]
  window(wi_6) 1200mm [11188,10700]→[9988,10700]
  wall 488mm [9988,10700]→[9500,10700]

西墙 | 总长 2800mm | 完整实墙
  wall 2800mm [9500,10700]→[9500,7900]

南墙 | 总长 2175mm | 实墙 975mm
  wall 488mm [9500,7900]→[9988,7900]
  door(d_3) 700mm [9988,7900]→[10688,7900]
  wall 487mm [10688,7900]→[11175,7900]

东墙 | 总长 2800mm | 完整实墙
  wall 2800mm [11175,7900]→[11175,10700]
```

### 设计要点

1. **方位自动标注**：根据段方向向量判断 → 东/南/西/北（非正交时用"斜边"）
2. **墙面分组**：方向变化处（拐角）自动断开为新墙面
3. **同方位多面墙**（L 形等复杂房间）：同一方位可能出现多面墙（如 L 形房间有两段东墙），加序号区分 → "东墙₁"、"东墙₂"；仅一面墙时不加序号
4. **每面墙摘要行**：`{方位} | 总长 {N}mm | {实墙描述}`
   - 完整实墙 → "完整实墙"
   - 有非 wall 段 → "实墙 {wall总长}mm"
   - 全 passage → "通道（→{adjacent}）"
5. **段格式**：`{type}({id}) {length}mm [{start}]→[{end}]`，wall 段省略 id

### 实现文件

`BIMCanvas.Agent/src/mcp/canvas.py` 中 `_format_zone_boundaries` 函数重写。

算法：
1. 遍历 segments，计算每段方向向量
2. 方向变化处（与前一段方向不同）断开为新墙面
3. 对每面墙：确定方位标签、计算总长和 wall 段总长、生成摘要
4. 按墙面输出格式化文本

---

## 步骤 1：generate-workflow/SKILL.md（基础设施）

**目标**：感知阶段新增 `get_zone_boundaries` 调用，为所有下游 Skill 提供数据基础。

### 1a. 感知阶段：新增边界语义调用

当前第 22 行：
```
2. **并行读取**：knowledge/design_principles.md + modules/module_library.json + schemes/zones.json + computed/exclusions.json + baseline/openings.json
```

改为：
```
2. **并行读取**：knowledge/design_principles.md + modules/module_library.json + schemes/zones.json + computed/exclusions.json
3. **边界语义**：调用 `get_zone_boundaries` — 获取每条边的类型（wall/passage/door/window）和坐标，直接用于墙面分析，不再从 rawBoundary + openings 手动推断
```

**关键决策**：移除 `baseline/openings.json`。理由：
- 门窗位置和坐标 → boundarySegments 的 door/window 段已提供
- 门开启排除区域 → exclusions.json 已提供
- openings.json 的唯一用途就是墙面分析中判断门窗，现在由 boundarySegments 替代

### 1b. 理解阶段：分区后二次调用

当前第 49 行：
```
- 执行分区 → 产出 subZones → 为每个叶子 zone 分别加载房间 Skill
```

改为：
```
- 执行分区 → 产出 subZones → 再次调用 `get_zone_boundaries` 获取子 zone 边界语义（含 passage 段）→ 为每个叶子 zone 分别加载房间 Skill
```

**行数预算**：143 → ~146 行（≤150）

---

## 步骤 2：generate-zoning/SKILL.md（passage 概念）

**目标**：定义 passage 概念（唯一定义点），引导分区后调用 get_zone_boundaries。

### 2a. 步骤 3 输出 subZones 后新增

在当前第 47 行"写入后 Server 自动创建子目录"之后新增：
```
- 写入后调用 `get_zone_boundaries(zoneIds=[新子zone列表])` — 返回子 zone 边界中的 passage 段（通向相邻分区的开放连通），后续房间 Skill 据此避免在通道处放大型家具

> WHY：子分区的 rawBoundary 不区分墙和通道。passage 段解决了"这条边到底是实墙还是通向 dz_2 的通道"的关键问题。
```

### 2b. 示例 1（L 形主卧）步骤 2a 墙面审视

当前第 65-68 行：
```
步骤 2a — 全局墙面审视：
- 主体区有 4 面墙（东4.9m/南3.4m/西2.9m/北=窗），衣柜可选东或南
- 延伸区有 3 面墙（西2.4m/南2.6m/北=通道侧），衣柜可选西
```

改为：
```
步骤 2a — 全局墙面审视（基于 get_zone_boundaries）：
- 主体 dz_1：东4.9m 实墙 / 南3.4m 实墙 / 西2.9m 实墙 / 北=窗墙
- 延伸 dz_2：西2.4m 实墙 / 南2.6m 实墙 / 北=通道(→dz_1) / 东=通道(→dz_1)
```

### 2c. 硬约束新增

在硬约束 ③ 后新增：
```
- ④ 子分区完成后必须调用 get_zone_boundaries — passage 数据是后续房间 Skill 正确工作的前提
```

**行数预算**：110 → ~115 行

---

## 步骤 3：generate-bedroom/SKILL.md（最大改动）

**目标**：空间理解补充从"推断规则"重写为"数据筛选"，示例展示新的推导链。

### 3a. 空间理解补充重写

当前第 16-22 行：
```
1. **床头墙候选**：列出所有实墙段（排除窗墙整面、门段=门洞+两侧各500mm），附每段长度
2. **衣柜墙候选**：列出所有非窗墙段，附有效段长度（有效段按 exclusion zone 扣减，不用门段缓冲）
3. **窗户朝向**：窗墙位置 → 决定"侧对窗户"的床头墙方向

> WHY：卧室策略的所有决策都从墙面资源出发...
```

改为：
```
从 `get_zone_boundaries` 返回的墙面清单构建候选（数据驱动，不需手动推断）：

1. **床头墙候选**：排除窗墙整面、排除门段区域（门洞+两侧各500mm），附候选墙实墙长度
2. **衣柜墙候选**：所有非窗墙面，有效长度 = 实墙总长 - exclusion zone 扣减
3. **窗户朝向**：窗墙位置 → 决定"侧对窗户"的床头墙方向
4. **通道识别**（子分区场景）：passage 段 = 与相邻分区的开放连通，**禁止靠通道放大型家具**

> WHY：`get_zone_boundaries` 已按墙面分组并标注实墙长度——直接筛选候选，不从 rawBoundary 推断。passage 在子分区场景中区分实墙和通道，靠通道放衣柜会阻断分区间通行。
```

注意："墙面清单分组"已由 MCP 输出完成（步骤 0），Skill 中不再需要指导 Agent 做分组。

### 3b. 衣柜线性布局简化

当前第 58 行：
```
1. **【必须】**穷举所有墙段（合并共线边），逐段评估
```

改为：
```
1. **【必须】**穷举 boundarySegments 中所有墙面，逐面评估
```

"合并共线边"不再需要——MCP 输出已按墙面分组。

### 3c. 策略声明示例更新

当前空间画像摘要（第 96-106 行）：
```
床头墙候选：
  东墙 3.6m 完整实墙（最长）
  西墙 3.6m 完整实墙
  北墙入口右侧 2.4m 实墙段

衣柜墙候选：
  北墙入口右侧 2.4m（有效段）
  东墙 3.6m（若不给床）
  西墙 3.6m（若不给床）
```

改为：
```
get_zone_boundaries 返回：
  北墙 | 总长 4000mm | 实墙 3200mm
    wall 1200mm ... + door(d_1) 800mm ... + wall 2000mm ...
  东墙 | 总长 3600mm | 完整实墙
  南墙 | 总长 3600mm | 实墙 800mm（窗墙）
    wall 400mm ... + window(wi_1) 2800mm ... + wall 400mm ...
  西墙 | 总长 3600mm | 完整实墙

床头墙候选（排除窗墙 + 门段缓冲）：
  东墙 3.6m 完整实墙（最长）| 西墙 3.6m 完整实墙 | 北墙右侧约 2.0m（扣门段500mm缓冲）

衣柜墙候选：
  北墙右侧 2.0m | 东墙 3.6m（若不给床）| 西墙 3.6m（若不给床）
```

示例展示：MCP 墙面数据 → 设计语言筛选 → 决策，数据获取和设计推理分层清晰。

**行数预算**：128 → ~134 行（≤150）

---

## 步骤 4：generate-livingroom/SKILL.md

**目标**：电视墙候选改为数据筛选，新增 passage 约束。

### 4a. 空间理解补充第 19 行

```
1. **电视墙候选**：最长连续实墙段（排除窗墙、门墙）
```
→
```
1. **电视墙候选**：从 boundarySegments 筛选——最长完整实墙（排除窗墙、门墙、通道墙）
```

### 4b. 电视墙选择优先级表第 44 行

```
| 1 | 最长连续实墙（排除窗墙整面、门段+两侧 500mm） | ... |
```
→
```
| 1 | 最长完整实墙（排除窗墙、门墙、通道墙） | ... |
```

### 4c. 软指导新增（第 112 行后）

```
- passage 段附近不放大型家具 — 通道畅通，分区间自然过渡
```

### 4d. 示例中 dz_1 电视墙理由

```
- 电视墙：西墙（最长连续实墙 4.2m，无窗无门）
```
→
```
- 电视墙：西墙（4.2m 完整实墙，无窗无门无通道）
```

**行数预算**：119 → ~120 行（≤150）

---

## 步骤 5：generate-bathroom/SKILL.md（极小改动）

**目标**：门位置判断引用 boundarySegments，其余不变。

### 5a. 门位置修正第 47 行

```
**门位置修正**（影响排列起点，不影响模式选择）：门在短边 → ...
```
→
```
**门位置修正**（从 boundarySegments 的 door 段确定门位置）：门在短边 → ...
```

### 5b. 模式 A 墙面选择第 54 行

```
**墙面选择**：门在长边 → 对面长墙；门在短边 → 距门洞中心较远的长墙。
```
→
```
**墙面选择**：从 boundarySegments 判断门所在边——门在长边 → 对面长墙；门在短边 → 距 door 段中心较远的长墙。
```

**行数预算**：140 行不变

---

## 跨文件一致性

| 概念 | 定义位置 | 使用位置 |
|------|---------|---------|
| `get_zone_boundaries` 调用时机 | workflow 感知阶段 | zoning 步骤 3 后二次调用 |
| MCP 输出格式（墙面分组） | canvas.py `_format_zone_boundaries` | 所有 Skill 隐式依赖 |
| passage 含义 | zoning（唯一定义） | bedroom/livingroom 引用 |
| door 段缓冲 500mm | bedroom 床头墙候选 | livingroom 电视墙同理 |
| openings.json | workflow 感知阶段移除 | 不再被任何 Skill 直接引用 |

**措辞分层**：
- **数据获取层**（程序性指令）：使用数据术语 → "boundarySegments"、"wall 段"、"door 段"、"passage 段"
- **设计推理层**（语义性推导）：保留设计语言 → "实墙"、"窗墙"、"完整实墙"、"门段缓冲"
- 旧："从 rawBoundary 推断" → 新："从 boundarySegments 筛选"
- 旧："合并共线边" → 删除（MCP 已完成分组）

---

## 验证方法

1. **MCP 输出验证**：调用 `get_zone_boundaries` 检查输出格式——应按墙面分组、含方位标签、含实墙摘要
2. **文本审查**：五个文件中搜索 "rawBoundary"——应仅在 generate-workflow 分区触发条件和 generate-zoning 触发条件中出现（用于几何判断，非墙面分析）
3. **文本审查**：搜索 "openings.json"——应不再出现在必读列表中
4. **设计语言检查**：设计推理部分应使用"实墙"、"窗墙"等设计语言，数据获取部分使用"wall 段"等数据术语
5. **行数检查**：所有 Skill 文件 ≤ 150 行
6. **概念一致性**：passage 仅在 generate-zoning 定义，其他文件引用
7. **端到端场景验证**：用金凤143项目实际运行 Agent，观察是否正确调用 get_zone_boundaries 并基于 passage 段避免在通道处放家具

---

## 实施顺序

0. canvas.py `_format_zone_boundaries`（MCP 输出格式，所有 Skill 的数据基础）
1. generate-workflow（感知阶段，所有下游的调用入口）
2. generate-zoning（passage 概念定义，下游引用的基础）
3. generate-bedroom（最大改动，示例改造）
4. generate-livingroom（与 bedroom 同模式）
5. generate-bathroom（极小改动）

---

## 修改文件清单

| 序号 | 文件路径 | 操作 |
|------|---------|------|
| 0 | `BIMCanvas.Agent/src/mcp/canvas.py` | 重写 `_format_zone_boundaries` |
| 1 | `BIMCanvas.Agent/templates/skills/generate-workflow/SKILL.md` | 修改 |
| 2 | `BIMCanvas.Agent/templates/skills/generate-zoning/SKILL.md` | 修改 |
| 3 | `BIMCanvas.Agent/templates/skills/generate-bedroom/SKILL.md` | 修改 |
| 4 | `BIMCanvas.Agent/templates/skills/generate-livingroom/SKILL.md` | 修改 |
| 5 | `BIMCanvas.Agent/templates/skills/generate-bathroom/SKILL.md` | 修改 |
| 6 | `plans/workflow-refactor/T4-zone-boundary-segments.md` | 更新进度 |
| 7 | `plans/workflow-refactor/overview.md` | 更新进展总览 |
