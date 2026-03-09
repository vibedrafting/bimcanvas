# Agent 工作流重构 —— 统一说明文档

> 本文档是工作流重构的**跨任务共享上下文**。
> 所有任务计划文档都引用本文档作为设计依据。
> 分支：`refactor/workflow-zoning`

---

## 一、重构背景

### 当前工作流的核心问题

**问题 1：注意力严重稀释**

SKILL.md（601 行）+ placement_guide（652 行）——Agent 执行一个布置任务时需同时记住几十条规则、十几项检查清单、20 条常见错误。违反"注意力零和"原则。

**问题 2：全局视角缺失**

流程线性：截图 → 读数据 → 骨架 → A 阶段 → B 阶段。Agent 一头扎进细节后没有退出来看全局的机会。

**问题 3：开放/异形空间不支持**

开放空间（客餐厅一体）和异形空间（L/T/U 形）直接"暂不支持"，但是常见需求。

**问题 4：用户互动不足**

AskUserQuestion 被限制为极端场景。Agent 无法了解用户偏好、功能优先级、生活习惯。

**问题 5：规则和流程耦合**

SKILL.md 大量硬编码设计知识，与 placement_guide 信息冗余。

---

## 二、重构目标

1. **全局思考能力**：Agent 先"看懂"空间，形成完整认知，再做细节决策
2. **注意力聚焦**：大幅精简规则数量，用 WHY + 原则替代穷举式检查清单
3. **分区设计能力**：支持开放空间功能分区 + 异形房间子空间拆解（分区是通用能力，不限于特定空间类型）
4. **自适应互动**：简单场景快速执行，复杂场景主动与用户对话
5. **职责清晰**：消除文件间的信息冗余，每条知识只在一处定义

---

## 三、新工作流架构

### Agent 双层架构

```
MainAgent（全屋协调者 + 用户代言人）
│  BIMCANVAS.md 定义
│  职责：任务路由、多房间派发、用户对话、全局验证、结果汇总
│
└── layout-agent（单房间设计专家）×N（并行）
      layout-agent.md 定义
      职责：运行完整五阶段流程、自主加载 Skill、静默执行、上报分歧
```

| 维度 | MainAgent | layout-agent |
|------|-----------|-------------|
| **角色** | 全屋协调者 + 用户代言人 | 单房间设计专家 |
| **作用域** | 跨房间（全屋） | 单个房间 |
| **设计能力** | 不做具体设计 | 完整五阶段设计流程 |
| **用户沟通** | ✅ AskUserQuestion | ❌ 静默执行，上报分歧 |
| **派发能力** | ✅ 派发 layout-agent | ❌ 不能派发 |
| **验证范围** | 全屋验证 + 跨房间协调 | 单房间验证 |
| **Skill 加载** | 不加载房间 Skill | 自主加载 generate-zoning + 房间 Skill |

**单房间直接执行 vs 多房间派发**：
- 1 个房间 → MainAgent 直接运行 generate-workflow
- ≥2 个房间 → MainAgent 并行派发 layout-agent

**对话能力**：仅主控 Agent 拥有。layout-agent 遇到需要用户确认的设计分歧时，在任务输出中上报分歧详情，由 MainAgent 决定是否向用户提问。

### Skills 架构

```
Skills（主控和 layout-agent 共用）
├── generate-workflow/SKILL.md        主工作流框架（五阶段）
├── generate-zoning/SKILL.md          分区能力（条件加载）
├── generate-bedroom/SKILL.md         卧室策略
├── generate-bathroom/SKILL.md        卫生间策略
└── generate-livingroom/SKILL.md      客厅策略

知识库
├── knowledge/design_principles.md    通用设计原则
└── modules/module_library.json       家具规则库
```

**两类 Skill 的加载逻辑**：

| Skill 类型 | 加载条件 | 说明 |
|-----------|---------|------|
| 能力 Skill（generate-zoning） | Agent 判断空间需要分区时 | 跨房间类型的通用能力 |
| 房间 Skill（generate-bedroom 等） | 根据空间类型标签 | 房间特定的设计策略 |

### 注意力优化效果

```
当前：SKILL.md(601行) + placement_guide(652行) = 1253 行全部加载
新（简单场景）：generate-workflow(~150行) + generate-bedroom(~150行) + design_principles(~100行) = ~400 行
新（复杂场景）：上述 + generate-zoning(~100行) = ~500 行
                ↑ 只加载需要的 Skill
```

### 五阶段流程

```
感知(Perceive) → 理解(Understand) → 策略(Strategy) → 执行(Execute) → 审查(Review) → 汇报(Report)
    快速机械        全局思考          明确方向        专注放置         原则评估        简洁交付
                     ↑                 ↑
              对话可以发生在      对话可以发生在
              理解之后            策略之前
              （Agent 自主判断）
```

### 各阶段定义

#### 感知（Perceive）

**职责**：获取空间的全部原始信息
**动作**：截图 + 并行读取数据文件（design_principles、module_library、zones.json、exclusions.json、openings.json）
**输出**：原始数据就绪
**设计意图**：纯机械操作，不占认知资源，快速完成

#### 理解（Understand）—— 新核心

**职责**：构建对空间的完整认知 + 确定空间类型 + 按需加载 Skill
**输出**：**空间画像**（自然语言，类似设计师的踏勘分析）

空间画像包含：
1. **基本特征**：形状、面积、比例、朝向
2. **空间结构**：主体区域的几何特征
3. **动线感知**：入口位置 → 自然动线走向 → 纵深层次
4. **采光分析**：窗户位置 → 光线通路
5. **关键资源**：最长实墙、特殊空间特征
6. **空间类型判断** → 加载房间 Skill
7. **分区需求判断** → 如需要，加载 generate-zoning Skill

**空间类型 → 房间 Skill**：
| 空间类型 | 判断依据 | 加载 Skill |
|---------|---------|-----------|
| 封闭空间 | bedroom、study 等标签 | generate-bedroom / generate-study |
| 特殊空间 | shower/toilet/washing 标签 | generate-bathroom |
| 开放空间 | circulation 或跨功能区 | generate-livingroom |

**设计意图**：Agent 花最多"思考力"的地方。理解空间的本质，而非直接进入"放家具"模式。

#### 策略（Strategy）

**职责**：将空间理解 + 用户意图转化为可执行的布置方案
**输出**：策略声明（引导结构 + 自由补充）

策略声明的引导问题：
- 主通道怎么走？
- 最重要的墙给谁？
- 要放什么家具？优先级？
- （如有分区）各区功能和边界？

**设计意图**：策略是"全局决策的记录"，让 Agent 在执行时有明确方向，不陷入局部优化。

#### 执行（Execute）

**职责**：按策略放置全部家具，一次性写入并验证
**流程**：
```
按优先级逐件计算位置 → 全部写入 modules.json → validate_layout
  → 通过 → 进入审查
  → 失败 → 修正违规项 → 重新 validate（最多 2 轮）
  → 仍失败 → 移除违规家具，保留核心布局
```

**设计意图**：信赖前面阶段的决策质量，执行阶段专注于精确计算。

#### 审查（Review）

**职责**：截图后基于设计原则进行整体评估
**评估维度**（原则性，非清单式）：
- **动线**：从入口到各功能区的路径是否自然流畅？
- **均衡**：家具视觉重量是否平衡？
- **功能**：每个功能区是否达到策略预期？
- **品质**：整体感觉是"有设计意图"还是"机械填充"？

**设计意图**：Agent 像设计评审一样审视方案，而非像质检员一样逐项检查。

#### 汇报（Report）

**内容**：空间画像摘要 → 策略要点 → 放置结果 → 品质评估 → 妥协说明（如有）

---

## 四、文件架构

### 新文件体系

```
BIMCANVAS.md                                    Agent 身份 + 对话能力 + 全局约束
├── skills/generate-workflow/SKILL.md            主工作流框架（五阶段）
├── skills/generate-zoning/SKILL.md              分区能力 Skill（条件加载）
├── skills/generate-bedroom/SKILL.md             卧室策略 Skill
├── skills/generate-bathroom/SKILL.md            卫生间策略 Skill
├── skills/generate-livingroom/SKILL.md          客厅策略 Skill
├── knowledge/design_principles.md               通用设计原则
└── modules/module_library.json                  家具规则库
```

### 职责边界

| 文件 | 管什么 | 不管什么 |
|------|--------|---------|
| **BIMCANVAS.md** | Agent 身份、对话能力（何时与用户沟通）、全局硬约束、任务路由 | 具体流程、设计知识 |
| **generate-workflow** | 五阶段流程、数据读取、截图/验证、Skill 加载逻辑、修正循环 | 任何设计知识或房间策略 |
| **generate-zoning** | 空间拆解方法、子空间识别、功能定义 | 房间策略、家具配置 |
| **generate-bedroom** | 卧室的策略生成、家具配置、关键约束 | 分区逻辑、通用流程 |
| **design_principles** | 动线原理、通道标准、采光原则、三级约束 | 房间特定策略、执行步骤 |
| **module_library** | 家具尺寸、拓扑规则、关系规则 | 房间策略、设计原理 |

### 核心原则

- generate-workflow 管"**怎么工作**"
- generate-zoning 管"**怎么拆分空间**"
- room Skill 管"**怎么设计家具方案**"
- BIMCANVAS.md 管"**怎么与用户沟通**"

---

## 五、空间类型策略差异

> 详见 `plans/Space_Type_Workflow_Vision.md`

| 空间类型 | 布置本质 | Agent 角色 | 自由度 | 对应 Skill |
|---------|---------|-----------|--------|-----------|
| 封闭空间（卧室、书房） | 空间规划 | 设计师 | 高 | generate-bedroom |
| 特殊空间（卫生间） | 模板匹配 | 工程师 | 极低 | generate-bathroom |
| 开放空间（客餐厅） | 先分区后规划 | 规划师+设计师 | 最高 | generate-livingroom |

**分区是通用能力**（generate-zoning）：不限于开放空间。封闭空间（如 L 形卧室）也可能需要分区设计。分区需求由 Agent 自主判断。

---

## 六、跨任务设计原则

### 原则 1：注意力预算

每个 Skill/文档都有注意力预算。精简不是删减信息，而是提升信息密度。

- **generate-workflow**：≤ 150 行
- **generate-zoning**：≤ 100 行
- **房间 Skill**：≤ 150 行/个
- **design_principles**：≤ 100 行
- **BIMCANVAS.md**：≤ 100 行

### 原则 2：WHY 优先

每条规则必须回答"为什么"。没有 WHY 的规则只能被机械执行；有 WHY 的规则能被灵活应用。

### 原则 3：三级约束分明

| 层级 | 语气 | Agent 行为 |
|------|------|-----------|
| 硬约束 | 必须/禁止 | 无条件遵守 |
| 软指导 | 应/建议 | 默认遵守，可说明理由后偏离 |
| 自由区域 | （不写规则） | Agent 自主决策 |

### 原则 4：职责单一

每条知识**只在一个文件中定义**。禁止跨文件重复。

### 原则 5：留白是设计选择

自由区域是"经过深思熟虑后决定不写规则的地方"。Agent 在此展现设计判断力。

---

## 七、从旧体系保留的核心机制

| 机制 | 来源 | 保留原因 |
|------|------|---------|
| `validate_layout` 编译检查 | 旧 SKILL.md | 每次写入后的硬性验证，防止物理错误 |
| 截图审查 | 旧 SKILL.md | 视觉验证是唯一能发现"看起来不对"的手段 |
| 家具优先级 | 旧 placement_guide | 锚点→主要→辅助的放置顺序经过验证有效 |
| 先读后写 | 旧 BIMCANVAS.md | 防止覆盖已有数据的安全机制 |
| SubAgent 并行 | 旧 BIMCANVAS.md | 多分区并行派发 layout-agent 的架构保留 |

---

## 八、任务拆分与依赖

### 任务列表

| 任务 | 名称 | 核心交付 |
|------|------|---------|
| T1 | Agent 工作流 + 身份 + 卧室策略 | generate-workflow/SKILL.md + BIMCANVAS.md + generate-bedroom/SKILL.md |
| T2 | 通用设计原则 + 其余房间 Skill + module_library | design_principles.md + generate-bathroom/SKILL.md + generate-livingroom/SKILL.md + module_library.json |
| T3 | 分区数据架构 + 分区 Skill | zones.json 扩展 + Server 代码 + generate-zoning/SKILL.md |

### 依赖关系

```
T1（主工作流 + 卧室）
  ↓ T2 基于 T1 的框架编写其余内容
T2（知识 + 其余房间）
  ↓ T3 基于 T1/T2 的分区设计实现数据层 + 分区 Skill
T3（分区架构 + generate-zoning）
```

### 执行顺序

```
第一波：T1 — 主框架 + 卧室（简单矩形，不含分区）
第二波：T2 — 知识体系 + 其余房间
第三波：T3 — 分区数据架构 + generate-zoning Skill
```

---

## 九、参考文档

| 文档 | 路径 | 用途 |
|------|------|------|
| 提示词设计哲学 | `docs/Agent_Prompt_Design_Philosophy.md` | 所有任务的设计理论基础 |
| 空间类型工作流架构 | `plans/Space_Type_Workflow_Vision.md` | 空间类型差异分析 |
| 规则体系地图 | `.claude/skills/rule-tuning/references/rule-system-map.md` | 当前文件关系和数据流 |
| Agent 工作流架构 | `docs/Agent_Workflows.md` | 当前架构参考 |
| Agent 架构设计 | `docs/Agent_Design.md` | SubAgent 机制参考 |
| 当前 SKILL.md | `BIMCanvas.Agent/templates/skills/generate-workflow/SKILL.md` | 旧工作流参考 |
| 当前 placement_guide | `BIMCanvas.Server/Templates/knowledge/placement_guide.md` | 旧知识库参考 |
| 当前 BIMCANVAS.md | `BIMCanvas.Agent/templates/BIMCANVAS.md` | 旧身份定义参考 |
| 当前 module_library | `BIMCanvas.Server/Templates/modules/module_library.json` | 旧家具规则参考 |
