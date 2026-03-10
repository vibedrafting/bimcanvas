# Agent 工作流重构 —— 指挥部记忆

> **定位**：重构指挥部的持久化记忆文件。新窗口读取本文档即可恢复完整上下文。
> **分支**：`refactor/workflow-zoning`
> **指挥部职责**：讨论架构 → 拆分任务 → 写计划文档 → 验收执行结果。不执行代码修改。

---

## 〇、进展总览

| 任务 | 状态 | 计划文档 | 下一步 |
|------|------|---------|--------|
| T1：工作流 + Agent 定义 + 卧室 | ✅ 已完成 | `T1-agent-workflow.md` ✅ | 验收通过，已提交 `c2c1f3d` |
| T2：知识体系 + 其余房间 | ✅ 已完成 | `T2-knowledge-system.md` ✅ | 验收通过，已补充 WHY |
| T3：分区架构 + zoning Skill | ✅ 已完成 | `T3-zoning-architecture.md` ✅ | 验收通过 |
| 全面审查 + 文档清理 | ✅ 已完成 | — | commit `bbca6fe` |

**当前状态**：T1-T3 全部完成并通过全面审查。旧体系清理完毕（18 个文件，placement_guide 引用全部更新）。重构闭环。

**执行顺序**：~~T1（主框架+卧室）~~ → ~~T2（知识+其余房间）~~ → ~~T3（分区架构+zoning Skill，全栈交付）~~ → ~~全面审查+文档清理~~

### T1 执行摘要

- **commit**: `c2c1f3d`（+299/-653，净减 354 行）
- **交付文件**：
  - `generate-workflow/SKILL.md`：601→144行，五阶段框架（感知→理解→策略→执行→审查→汇报）
  - `BIMCANVAS.md`：142→91行，全屋协调者+用户代言人+对话通用能力
  - `layout-agent.md`：52→80行，单房间设计专家+Skill自主加载+分歧上报增强
  - `generate-bedroom/SKILL.md`：新建123行，卧室策略Skill（决策链+示例锚定）
- **P0 验收**：全部通过（无信息重复、WHY全覆盖、行数达标、五阶段完整、策略声明示例、对话能力、三约束明确）

### T2 执行摘要

- **交付文件**：
  - `design_principles.md`：新建91行，从652行placement_guide提炼跨房间通用原则（原理+速查混合模式）
  - `generate-bathroom/SKILL.md`：新建139行，卫生间策略Skill（5种模式决策树+模板匹配工作流+策略声明示例）
  - `generate-livingroom/SKILL.md`：新建81行，客餐厅策略Skill框架级（核心决策链+待充实标注）
  - `generate-workflow/SKILL.md`：136行（-7行），引用placement_guide→design_principles，映射表补充bathroom/livingroom，删除卫生间过渡段
  - `BIMCANVAS.md`：引用更新（2处 placement_guide→design_principles/房间Skill）
  - `generate-bedroom/SKILL.md`：移除placement_guide §1.3引用，改为自包含WHY描述
  - `placement_guide.md`：已从源码删除（内容分散到 design_principles.md + 各房间 Skill）
- **注意力优化效果**：旧体系652行全局加载 → 新体系按房间类型加载215-230行（减少60-67%）
- **已知缺口**：钻石型淋浴房模块缺失（module_library待补充，不在T2范围）
- **P0 验收**：✅ 通过（11项中9项完全通过，2项经复核降级——信息重复为有意的通用→特化设计模式，WHY缺失已补充）
- **信息安全检查**：✅ 通过（§1.2特色资源→T3、§4.3使用距离5/6迁移完成、§5.3互斥依赖已覆盖、§12常见错误8/8覆盖）

### T3 执行摘要

- **交付文件**（11 个文件，6 层变更）：
  - `Zone.cs`：新增 SubZones 嵌套字段
  - `ProjectService.cs`：CreateZoneDirectories 递归化（private→internal）
  - `ValidationController.cs`：LoadZoneData 展平嵌套 + LoadAllModules 递归遍历 + PersistModules 嵌套路径 + FlattenToLeafZones/ResolveZoneDirectory 新方法
  - `ProjectWatcherService.cs`：注入 ProjectService + zones.json 变更触发目录刷新
  - `canvas.ts`：Zone 接口新增 optionalTags + subZones
  - `ZoneBuilder.ts`：容器 zone 轮廓线渲染 + 子 zone 递归渲染
  - `generate-zoning/SKILL.md`：新建 97 行，统一三步框架 + 2 个策略声明示例
  - `generate-workflow/SKILL.md`：142 行，3 处激活（理解/执行/审查）
  - `generate-livingroom/SKILL.md`：81→118 行，框架级→完整版（分区逻辑+策略声明示例+电视墙优先级链）
  - `layout-agent.md`：清理"T3 后可用"残留
  - `zone_tools.py`：递归查找 + get_leaf_zones 新函数
  - `placement_tools.py`：嵌套路径写入/读取支持
- **编译验证**：Core ✅ / Server ✅ / Web(vue-tsc) ✅
- **残留检查**：无"T3 后可用"或"待充实"残留

### 全面审查 + 文档清理摘要

- **commit**: `bbca6fe`（+166/-78，18 个文件）
- **审查结论**：T1-T3 核心交付物全部达标（提示词体系 ✅ / 代码实现 ✅ / 设计哲学合规 ✅）
- **发现并修复的问题**：
  - docs/ 文档（7 个）：添加过时提醒头 + 更新 placement_guide 引用
  - Claude Code Skills（rule-review / rule-tuning）：更新文件路径、职责表、注入点表
  - `canvas.py`：清除已迁移的 `get_workflow_guide` 注释代码
  - `README.md` / `Templates/README.md`：更新知识库目录结构
  - `.agents/skills/`：同步更新
- **无 P0 问题**，所有发现均为文档层面的旧引用残留

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

## 三、核心设计决策

> 以下决策均经过讨论达成共识，后续新决策追加到此。

| # | 决策 | 说明 | 背景 |
|---|------|------|------|
| D1 | 混合模式 | 简单场景快速执行，复杂场景先对话再执行 | 避免一刀切：简单卧室不需要对话，L形客餐厅需要 |
| D2 | 彻底重写 | 从旧 Skill 提取有用内容，但不受旧结构约束 | 旧流程线性僵化，修补不如重建 |
| D3 | 空间画像 | 理解阶段输出自然语言空间分析 | 替代旧"骨架规划"的机械步骤，给 Agent 全局视角 |
| D4 | 一次性放置后验证 | 信赖前端决策质量，不逐件放置验证 | 理解+策略阶段投入足够，执行阶段专注精确计算 |
| D5 | 扩展 zones.json | 分区能力与 Server 数据深度集成 | 分区不只是 Agent 提示词概念，需要数据层支撑 |
| D6 | 分区是独立 Skill | generate-zoning 跨房间类型，Agent 自主判断是否加载 | 封闭空间（L形卧室）也可能需要分区，不应绑定特定空间类型 |
| D7 | 对话是主控通用能力 | 由 BIMCANVAS.md 定义，layout-agent 静默执行 | 对话不是工作流固定阶段，而是 Agent 在任何阶段可触发的行为 |
| D8 | layout-agent = 单房间设计专家 | 运行完整五阶段，三个核心约束：静默/单房间验证/不派发 | 区别于旧的"单区布置执行者"，具备独立设计判断力 |

---

## 四、新工作流架构

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
| **验证范围** | 全屋验证：`validate_layout()` | 单房间验证：`validate_layout(zoneIds=[指定zone])` |
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
旧：SKILL.md(601行) + placement_guide(652行) = 1253 行全部加载
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

### 各阶段设计意图

> 各阶段的详细定义（职责、输出、数据流）见 `T1-agent-workflow.md` §2.1。此处仅记录设计意图（WHY）。

| 阶段 | 设计意图 |
|------|---------|
| 感知 | 纯机械操作，不占认知资源，快速完成 |
| **理解** | Agent 花最多思考力的地方。理解空间本质，而非直接进入"放家具"模式 |
| 策略 | 全局决策的记录，让 Agent 执行时有明确方向，不陷入局部优化 |
| 执行 | 信赖前面阶段的决策质量，专注于精确计算 |
| 审查 | 像设计评审一样审视方案，而非像质检员一样逐项检查 |
| 汇报 | 简洁交付 |

---

## 五、文件架构

### 新文件体系

```
Agent 定义
├── BIMCANVAS.md                                 主控 Agent 身份 + 对话能力 + 全局约束
├── agents/layout-agent.md                       单房间设计专家定义

Skills
├── skills/generate-workflow/SKILL.md            主工作流框架（五阶段）
├── skills/generate-zoning/SKILL.md              分区能力 Skill（条件加载）
├── skills/generate-bedroom/SKILL.md             卧室策略 Skill
├── skills/generate-bathroom/SKILL.md            卫生间策略 Skill
├── skills/generate-livingroom/SKILL.md          客厅策略 Skill

知识库
├── knowledge/design_principles.md               通用设计原则
└── modules/module_library.json                  家具规则库
```

### 职责边界

| 文件 | 管什么 | 不管什么 |
|------|--------|---------|
| **BIMCANVAS.md** | 主控 Agent 身份、对话能力、任务路由、多房间派发、全局验证 | 具体流程、设计知识、单房间执行 |
| **layout-agent.md** | 单房间设计专家：静默执行、单房间验证、上报分歧、Skill 自主加载 | 用户沟通、任务派发、全局验证 |
| **generate-workflow** | 五阶段流程、数据读取、截图/验证、Skill 加载逻辑、修正循环 | 任何设计知识或房间策略 |
| **generate-zoning** | 空间拆解方法、子空间识别、功能定义 | 房间策略、家具配置 |
| **generate-bedroom** | 卧室的策略生成、家具配置、关键约束 | 分区逻辑、通用流程 |
| **design_principles** | 动线原理、通道标准、采光原则、三级约束 | 房间特定策略、执行步骤 |
| **module_library** | 家具尺寸、拓扑规则、关系规则 | 房间策略、设计原理 |

### 核心原则

- **BIMCANVAS.md** 管"**怎么协调和沟通**"
- **layout-agent.md** 管"**怎么做单房间设计专家**"
- **generate-workflow** 管"**怎么工作**"
- **generate-zoning** 管"**怎么拆分空间**"
- **room Skill** 管"**怎么设计家具方案**"

---

## 六、空间类型策略差异

> 详见 `plans/Space_Type_Workflow_Vision.md`

| 空间类型 | 布置本质 | Agent 角色 | 自由度 | 对应 Skill |
|---------|---------|-----------|--------|-----------|
| 封闭空间（卧室、书房） | 空间规划 | 设计师 | 高 | generate-bedroom |
| 特殊空间（卫生间） | 模板匹配 | 工程师 | 极低 | generate-bathroom |
| 开放空间（客餐厅） | 先分区后规划 | 规划师+设计师 | 最高 | generate-livingroom |

**分区是通用能力**（generate-zoning）：不限于开放空间。封闭空间（如 L 形卧室）也可能需要分区设计。分区需求由 Agent 自主判断。

---

## 七、跨任务设计原则

### 原则 1：注意力预算

每个 Skill/文档都有注意力预算。精简不是删减信息，而是提升信息密度。

- **generate-workflow**：≤ 150 行
- **generate-zoning**：≤ 100 行
- **房间 Skill**：≤ 150 行/个
- **design_principles**：≤ 100 行
- **BIMCANVAS.md**：≤ 100 行

### 原则 2：WHY 优先

每条规则必须回答"为什么"。没有 WHY 的规则只能被机械执行；有 WHY 的规则能被灵活应用。

### 原则 3：示例锚定基准

示例是最强的模式锚定（机制 4）。规则+WHY 告诉 Agent "为什么这么做"，示例告诉 Agent "做出来是什么样"。

- **房间 Skill 必须包含至少 1 个策略声明示例**：展示完整的空间画像→策略推导过程
- **示例锚定基准，WHY 覆盖边界**：示例校准典型场景，WHY 处理示例未覆盖的变体
- 能用示例替代文字规则时，优先用示例

### 原则 4：三级约束分明

| 层级 | 语气 | Agent 行为 |
|------|------|-----------|
| 硬约束 | 必须/禁止 | 无条件遵守 |
| 软指导 | 应/建议 | 默认遵守，可说明理由后偏离 |
| 自由区域 | （不写规则） | Agent 自主决策 |

### 原则 5：职责单一

每条知识**只在一个文件中定义**。禁止跨文件重复。

### 原则 6：留白是设计选择

自由区域是"经过深思熟虑后决定不写规则的地方"。Agent 在此展现设计判断力。

---

## 八、从旧体系保留的核心机制

| 机制 | 来源 | 保留原因 |
|------|------|---------|
| `validate_layout` 编译检查 | 旧 SKILL.md | 每次写入后的硬性验证，防止物理错误。新增 zoneIds 参数支持单分区验证 |
| 截图审查 | 旧 SKILL.md | 视觉验证是唯一能发现"看起来不对"的手段 |
| 家具优先级 | 旧 placement_guide | 锚点→主要→辅助的放置顺序经过验证有效 |
| 先读后写 | 旧 BIMCANVAS.md | 防止覆盖已有数据的安全机制 |
| SubAgent 并行 | 旧 BIMCANVAS.md | 多分区并行派发 layout-agent 的架构保留 |

---

## 九、任务拆分与进展

### T1：Agent 工作流 + 双层 Agent 定义 + 卧室策略

- **状态**：✅ 已完成（验收通过，commit `c2c1f3d`）
- **计划文档**：`plans/workflow-refactor/T1-agent-workflow.md`
- **范围**：只处理简单矩形卧室（不含分区场景）
- **交付文件**：generate-workflow/SKILL.md + BIMCANVAS.md + layout-agent.md + generate-bedroom/SKILL.md
- **附录 A**：AskUserQuestion 驱动机制调研（逆向分析 Claude Code 系统提示词，为 D7 对话能力设计提供实施依据）
- **与 T2 联动**：T1 定义房间 Skill 接口规范和范例（generate-bedroom），T2 按此编写其余房间 Skill
- **与 T3 联动**：T1 预留分区接口（"加载 generate-zoning"），T3 完成后自动解锁

### T2：设计知识体系重构

- **状态**：✅ 已完成（验收通过）
- **计划文档**：`plans/workflow-refactor/T2-knowledge-system.md`
- **范围**：design_principles.md + generate-bathroom/SKILL.md + generate-livingroom/SKILL.md（框架级） + generate-workflow 引用更新
- **依赖**：T1 完成后开始（基于 T1 的框架和范例）
- **交付文件**：design_principles.md + generate-bathroom/SKILL.md + generate-livingroom/SKILL.md + generate-workflow 更新
- **关键决策**：
  - design_principles = 原理+速查混合模式（WHY + 关键数值）
  - generate-bathroom = 模板匹配工作流（5种模式决策树）
  - generate-livingroom = 框架级定义（待 T3 后充实）
  - module_library.json 不调整
  - placement_guide 已删除（内容分散到 design_principles.md + 各房间 Skill）
- **与 T1 联动**：T1 定义接口规范，T2 实现内容；T2 更新 generate-workflow 中的过渡引用
- **与 T3 联动**：generate-livingroom 预留分区接口，T3 完成后从框架升级为完整版

### T3：分区架构 + zoning Skill（全栈交付）

- **状态**：✅ 已完成（验收通过）
- **计划文档**：`plans/workflow-refactor/T3-zoning-architecture.md`
- **范围**：Zone.cs 扩展（SubZones 嵌套）+ Server 适配 + Web 适配 + generate-zoning/SKILL.md + generate-workflow 更新 + generate-livingroom 升级
- **依赖**：T1/T2 完成后开始
- **交付文件**（12 个文件，Core/Server/Web/Agent 四层）：
  - Core：`Zone.cs`（+SubZones 字段）
  - Server：`ProjectService.cs`（递归目录创建）、`ValidationController.cs`（展平嵌套+递归遍历+嵌套路径写回）、`ProjectWatcherService.cs`（zones.json 触发目录刷新）
  - Web：`canvas.ts`（+optionalTags/subZones）、`ZoneBuilder.ts`（容器轮廓线+子 zone 递归渲染）
  - Agent：`generate-zoning/SKILL.md`（新建 97 行）、`generate-workflow/SKILL.md`（142 行，3 处激活）、`generate-livingroom/SKILL.md`（81→118 行，完整版）、`layout-agent.md`（清理残留）、`zone_tools.py`（递归查找+get_leaf_zones）、`placement_tools.py`（嵌套路径读写）
- **关键决策**：
  - Zone 新增 SubZones 嵌套字段，子分区复用 Zone 类型
  - 只有叶子 zone 放置家具，目录结构跟随嵌套
  - Agent 推理坐标完成分区边界，零新增 Server API
  - 跳过 openingIds（Agent 按坐标匹配，性价比更高）
  - 统一方法论覆盖异形拆解 + 功能分区（空间降维）
- **与 T1 联动**：激活 generate-workflow 分区接口、layout-agent 分区加载描述
- **与 T2 联动**：升级 generate-livingroom 为完整版（分区逻辑+策略声明示例+电视墙优先级链）

### 依赖关系

```
T1（主工作流 + 卧室）
  ↓ T2 基于 T1 的框架编写其余内容
T2（知识 + 其余房间）
  ↓ T3 基于 T1/T2 的分区设计实现数据层 + 分区 Skill
T3（分区架构 + generate-zoning）
```

---

## 十、参考文档

| 文档 | 路径 | 用途 |
|------|------|------|
| 提示词设计哲学 | `docs/Agent_Prompt_Design_Philosophy.md` | 设计理论基础 |
| 空间类型工作流架构 | `plans/Space_Type_Workflow_Vision.md` | 空间类型差异分析 |
| 规则体系地图 | `.claude/skills/rule-tuning/references/rule-system-map.md` | 文件关系和数据流 |
| Agent 工作流架构 | `docs/Agent_Workflows.md` | 运行时架构（已添加过时提醒头） |
| Agent 架构设计 | `docs/Agent_Design.md` | SubAgent 机制（已添加过时提醒头） |
| generate-workflow | `BIMCanvas.Agent/templates/skills/generate-workflow/SKILL.md` | 五阶段主工作流 |
| design_principles | `BIMCanvas.Server/Templates/knowledge/design_principles.md` | 跨房间通用设计原则 |
| BIMCANVAS.md | `BIMCanvas.Agent/templates/BIMCANVAS.md` | 主控 Agent 身份定义 |
| module_library | `BIMCanvas.Server/Templates/modules/module_library.json` | 家具规则库 |
