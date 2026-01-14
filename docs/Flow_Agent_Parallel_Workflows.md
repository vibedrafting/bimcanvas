# BIMCanvas 并行设计模式

> **版本**：v1.0 | **更新日期**：2026-01-13
> **目的**：详细描述 BIMCanvas 的并行设计架构哲学、核心场景、Git 翻译层及 Worktree 实现
> **实现状态**：✅ v3.1 已完成核心架构实现
> **关联代码**：`BIMCanvas.Server/Services/GitWorktreeService.cs`, `StrategyService.cs`
> **关联文档**：[Agent_SDK.md](./Agent_SDK.md)

---

## 一、架构哲学

### 1.1 从"聊天机器人"到"数字化员工集群"

在 BIMCanvas 的架构中，AI 的角色发生了质的飞跃。借助 Git 分支的低成本特性，系统可以瞬间唤醒多个 AI 实例，在平行的"宇宙"中同时探索不同的设计可能性。

> **核心理念**：将"文件驱动"、"异步协作"与"并行生成"相结合，把 AI 从单一的对话者升级为**"拥有无限分身的并发设计团队"**。

### 1.2 三大支柱

| 支柱 | 核心思想 | 实现方式 |
|------|----------|----------|
| **文件驱动 (File-Driven)** | 文件是唯一真理源，AI 无需记忆复杂上下文 | 每个分支是完整、独立的文件系统状态 |
| **异步协作 (Async Collaboration)** | 用户无需等待，AI 在后台默默工作 | Commit 和 Pull Request 交付成果 |
| **并行生成 (Parallel Generation)** | 算力换广度，同时探索 N 种可能性 | Git Worktree 物理隔离并发 |

### 1.3 文件驱动 (File-Driven)

- **真理源**：每个分支都是一套完整、真实、可独立运行的文件系统
- **无状态**：AI 不需要记忆复杂的上下文，文件本身就是状态
- **可追溯**：所有变更通过 Git 记录，可回滚、可对比

### 1.4 异步协作 (Async Collaboration)

- **非阻塞**：用户无需等待 AI 生成，AI 在后台分支默默工作
- **提交即交付**：AI 通过 Commit 和 Pull Request 交付成果，而非流式文本
- **可中断**：用户可以随时切换关注点，AI 任务独立进行

### 1.5 并行生成 (Parallel Generation)

- **算力换广度**：利用并发能力，同时探索 N 种可能性
- **打破线性限制**：突破人类设计师的线性工作模式
- **多样性保证**：通过不同策略参数确保输出差异化

---

## 二、核心场景

### 2.1 场景 A：策略分叉 (Strategy Fork)

> **目标**：风格与策略的 A/B 测试

**用户指令**："给我的客厅出三个方案：一个是'极致收纳'，一个是'动线优先'，还有一个'极简留白'。"

**系统行为**：

1. **分支裂变**：Server 瞬间基于 `main` 创建三个分支：
   - `feat/ai-living-storage`
   - `feat/ai-living-flow`
   - `feat/ai-living-minimal`

2. **并发执行**：三个 AI Agent 实例同时启动，加载同一份 `baseline/` 数据，但注入不同的**策略参数 (Strategy Config)**

3. **独立产出**：
   - AI-1 (收纳)：生成满墙柜体，牺牲部分通道宽度
   - AI-2 (动线)：保留宽敞的回游动线，减少非必要家具
   - AI-3 (极简)：只保留核心家具，大量留白

**对 Agent 的能力要求**：
- **策略参数化**：AI 的入口必须支持配置权重（如 `storage_weight=0.9`, `flow_weight=0.2`）
- **自我辩护**：提交方案时需附带 Markdown 设计说明，解释"为了达成策略，我做出了哪些权衡"

### 2.2 场景 B：布局求解器 (Layout Solver)

> **目标**：硬约束下的局部最优解暴力搜索

**用户指令**："这个卫生间太小了，帮我看看能不能塞进一个浴缸和淋浴房。"

**系统行为**：

1. **沙盒模式**：AI 创建一个临时分支 `feat/ai-bathroom-solver`

2. **蒙特卡洛搜索**：AI 在后台进行高频迭代
   - 尝试 1：失败（浴缸挡门）
   - 尝试 2：失败（淋浴房与马桶重叠）
   - ...
   - 尝试 99：失败
   - 尝试 100：**成功**（找到唯一可行的极限布局）

3. **结果交付**：只有验证成功的第 100 号方案会被提交，之前的 99 次失败对用户透明

**对 Agent 的能力要求**：
- **沙盒模拟**：具备在不污染主分支的情况下进行"试错-回滚"的能力
- **失败感知**：能读懂 Server 的验证错误（Validation Error），并将其转化为下一次尝试的约束条件

### 2.3 场景 C：主编式合并 (Editorial Merge)

> **目标**：用户作为总设计师的方案融合

**触发方式**：用户 UI 操作（非自然语言指令）

**用户操作**：用户看着三个平行方案，觉得"方案 A 的沙发摆得好，但方案 B 的电视柜设计更合理"。

**系统行为**：

1. **可视化对比**（Server + Web）：
   - 前端通过"三联屏"展示不同 Worktree 的渲染结果
   - 用户可以切换、对比各方案

2. **区域级选择**（用户 + Web）：
   - 用户勾选方案 A 的 `Zone: SofaArea`
   - 用户勾选方案 B 的 `Zone: TVArea`
   - 前端生成合并请求

3. **Cherry-pick 合并**（Server）：
   - 执行精确的 JSON 合并
   - 检查依赖冲突（如有 `DependencyGroup` 被拆分则警告）
   - 合并到 `main` 分支

4. **清理**（Server）：
   - 删除未被采纳的 Worktree
   - 保留合并后的最终方案

**对 Agent 的前置要求**（在生成阶段）：
- **解耦设计**：生成的方案应高度模块化，避免强耦合
- **依赖标记**：强关联的家具需标记 `DependencyGroup`，提示用户成套采纳

> 注：此场景中 Agent 不直接参与执行，但其在场景 A/B 中的设计质量决定了合并的可行性。

---

## 三、Git 翻译层

> **核心挑战**：如何将用户模糊的自然语言指令（如"把客厅设计得温馨一点"）转化为精确的 Git 操作序列？

### 3.1 完整执行链路

```
┌──────────────────────────────────────────────────────────────────────────┐
│                     从用户指令到并行执行的完整链路                          │
├──────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  【1. 用户输入】                                                          │
│  "给我的客厅出三个方案：极致收纳、动线优先、极简留白"                      │
│                              ↓                                           │
│  【2. 意图解析】(Agent 负责)                                              │
│  Agent 理解自然语言，输出结构化的"设计意图对象"：                         │
│  {                                                                       │
│    "action": "parallel_generate",                                        │
│    "target_zone": "living_room",                                         │
│    "branches": [                                                         │
│      { "name": "storage", "strategy": { "storage_weight": 0.9 } },       │
│      { "name": "flow", "strategy": { "circulation_weight": 0.9 } },      │
│      { "name": "minimal", "strategy": { "furniture_count": "min" } }     │
│    ]                                                                     │
│  }                                                                       │
│                              ↓                                           │
│  【3. 操作编排】(Server 负责)                                             │
│  Server 根据意图对象，执行 Git 操作：                                     │
│  • git worktree add .worktrees/ai-living-storage feat/ai-storage         │
│  • git worktree add .worktrees/ai-living-flow feat/ai-flow               │
│  • git worktree add .worktrees/ai-living-minimal feat/ai-minimal         │
│  • 将策略配置写入各 Worktree 的 strategy.json                             │
│                              ↓                                           │
│  【4. 并发执行】(Agent 负责)                                              │
│  三个 Agent 实例在各自 Worktree 中执行布置决策                            │
│                              ↓                                           │
│  【5. 提交交付】(Agent 负责)                                              │
│  各 Agent 执行 git add && git commit                                     │
│                              ↓                                           │
│  【6. 验证展示】(Server 负责)                                             │
│  Server 验证结果，通知前端展示三个方案供用户选择                          │
│                                                                          │
└──────────────────────────────────────────────────────────────────────────┘
```

### 3.2 意图解析 (Intent Parsing)

将自然语言转化为结构化的 **"设计意图对象" (Design Intent Object)**。

**输入**："给我的客厅出三个方案：一个是'极致收纳'，一个是'动线优先'，还有一个'极简留白'。"

**输出**：
```json
{
  "action": "parallel_generate",
  "target_zone": "living_room",
  "branches": [
    { "name": "storage", "strategy": { "storage_weight": 0.9 } },
    { "name": "flow", "strategy": { "path_width_weight": 0.9 } },
    { "name": "minimal", "strategy": { "furniture_count": "min" } }
  ]
}
```

**意图识别规则**：

| 用户表达 | 识别为 | action |
|----------|--------|--------|
| "出三个方案" / "给我几个选择" | 并行生成 | `parallel_generate` |
| "设计一下" / "帮我布置" | 单次生成 | `single_generate` |
| "能不能塞进去" / "试试看" | 布局求解 | `layout_solve` |

### 3.3 意图分类系统

> **历史演进**：早期设计使用 `action` 字段进行三分类，后续简化为 `task_type` 二分法。

#### action 与 task_type 映射

| action（设计意图） | task_type（执行分类） | 说明 |
|-------------------|----------------------|------|
| `parallel_generate` | `execute` | 并行生成多方案，需要写入文件 |
| `single_generate` | `execute` | 单次生成方案，需要写入文件 |
| `layout_solve` | `execute` | 布局求解，需要迭代写入 |
| *(查询类操作)* | `query` | 只读查询，不修改文件 |

#### 两套分类的适用场景

| 分类系统 | 使用位置 | 目的 |
|----------|----------|------|
| **action 三分类** | 设计意图对象 | 描述用户意图的业务语义 |
| **task_type 二分法** | 工具调用判断 | 区分读操作和写操作 |

**使用原则**：
- Agent 解析用户意图时，输出 `action` 字段（业务语义）
- Server 根据 `action` 判断对应的 `task_type`（执行分类）
- 所有三种 action 都属于 `execute` 类型，因为都涉及文件写入

### 3.4 操作编排 (Operation Orchestration)

将意图对象转化为具体的 **Git 命令序列**。

**转换逻辑**：
1. `git checkout main` (确保基准正确)
2. `git pull` (同步最新状态)
3. **Loop for each branch**:
   - `git checkout -b feat/ai-living-{name}`
   - `Agent.run(strategy)` -> 生成 JSON 文件
   - `git add .`
   - `git commit -m "Design: Living Room with {name} strategy"`

### 3.5 语义化提交 (Semantic Commits)

AI 必须学会写"人话"Commit Message，而不是机器码。

- **差评**：`Update modules.json`
- **好评**：`feat(living-room): Maximize storage by adding full-wall cabinets, sacrificing 200mm aisle width`

### 3.6 Agent 与 Server 分工

> **核心原则**：意图解析是 Agent 职责，Git 操作是 Server 职责。

| 步骤 | 性质 | 执行者 |
|------|------|--------|
| **意图解析** | 智能理解（需要 LLM） | Agent |
| **操作编排** | 系统编排（Git 操作） | Server |
| **并行执行** | 智能决策 | Agent (× N) |
| **语义化提交** | 智能表达 | Agent |
| **验证/合并** | 系统操作 | Server |

**协作流程**：
1. 用户与 Agent 对话
2. Agent 解析意图，输出"设计意图对象"（JSON）
3. Agent 将意图对象发送给 Server（通过 MCP 工具调用）
4. Server 根据意图对象创建 Worktree、写入策略配置
5. Server 启动 N 个 Agent 实例，传入各自的 Worktree 路径
6. Agent 在 Worktree 中执行布置决策并 Commit
7. Server 验证结果，通知前端展示

---

## 四、Git Worktree 架构

### 4.1 核心概念

**传统认知**：`1 个 Git 仓库 = 1 个文件夹 = 1 个当前分支`

**实际上**：`1 个 Git 仓库 = N 个文件夹 = N 个并行分支`

**`git worktree`** 允许从同一个 `.git` 仓库中，"映射"出多个独立的文件夹，每个文件夹对应不同的分支。

### 4.2 三种架构方案对比

| 架构方案 | 概念 | 物理结构 | 适用场景 | 结论 |
|----------|------|----------|----------|------|
| **多分支 (Multi-Branch)** | 标准 Git | 1 个文件夹，内容切换 | 单人串行工作 | ❌ 无法并行 |
| **多仓库 (Multi-Repo)** | 分布式 | N 个文件夹，独立历史 | 完全独立的项目 | ❌ 合并困难，空间浪费 |
| **多工作树 (Multi-Worktree)** | **链接克隆** | **N 个文件夹，共享历史** | **单机并行工作** | **✅ 最佳选择** |

### 4.3 混合架构落地

> ✅ **已在 v3.1 中实现** - 见 `GitWorktreeService.cs` 和 `StrategyService.cs`

**1. 存储层 (Storage Layer)**：

使用 **单仓库 + 多分支**：
- 所有数据都在一个 `.git` 历史中，高效且标准
- `main` 分支是用户的当前状态
- `scheme/{id}` 分支存储保存的设计方案
- `feat/ai-{jobId}-{name}` 分支存储 AI 的临时提案

**2. 执行层 (Execution Layer)**：

使用 **Git Worktree** 处理临时任务：
- 当 AI 启动时：`git worktree add .worktrees/ai-job-1 feat/ai-proposal`
- 当 AI 完成时：`git worktree remove .worktrees/ai-job-1`

**目录结构**：

```
项目根目录/
├── .git/                      # 共享的 Git 历史
├── project.json               # 项目元数据
├── baseline/                  # 建筑基础数据（只读）
│   ├── metadata.json
│   ├── architecture.json
│   ├── openings.json
│   ├── rooms.json
│   └── location_lines.json
├── computed/                  # 计算派生数据（自动生成）
│   ├── room_zones.json
│   └── exclusions.json
├── schemes/                   # 方案设计数据（无子目录）
│   ├── strategy.json
│   ├── zones.json
│   ├── finishes.json
│   └── modules.json
├── context/                   # 上下文信息
│   └── requirements.md
├── knowledge/                 # 知识库
│   └── placement_guide.md
├── modules/                   # 模块素材库
│   ├── module_library.json
│   └── assets/
└── .worktrees/                # 临时工作树目录（并行执行时创建）
    ├── ai-job-1/              # Worktree 1 → feat/ai-storage
    │   ├── baseline/          # 继承 main 的基础数据
    │   ├── computed/          # 继承 main 的计算数据
    │   ├── schemes/           # AI-1 的独立方案
    │   └── ...                # 其他目录同主项目
    └── ai-job-2/              # Worktree 2 → feat/ai-flow
        └── ...
```

> **注意**：`schemes/` 目录下没有子目录，文件直接存放。多策略通过 Git 分支隔离，每个 Worktree 对应一个策略分支。

### 4.4 C# 实现示例

```csharp
// 场景 A：策略分叉 - 创建三个并行方案
var strategies = new List<ParallelStrategyRequest>
{
    new() { Name = "极致收纳", Approach = StrategyApproach.StorageFirst },
    new() { Name = "动线优先", Approach = StrategyApproach.CirculationFirst },
    new() { Name = "极简留白", Approach = StrategyApproach.MinimalistFirst }
};

// StrategyService 调用 GitWorktreeService 创建并行 Worktree
var worktrees = strategyService.CreateParallelStrategies(projectPath, strategies);

// 三个 AI 实例可以同时在各自 worktree 中工作...
// worktrees["极致收纳"] = "C:/.../project/.worktrees/ai-极致收纳"

// 用户选择后，合并到 main
var result = strategyService.AcceptParallelStrategy(projectPath, "动线优先");
```

### 4.5 为什么这能解决问题？

- **对于并行生成**：AI-1 和 AI-2 分别在 `.worktrees/ai-job-1` 和 `.worktrees/ai-job-2` 两个物理隔离的文件夹中工作，互不干扰，可以同时写入
- **对于 Web 对比**：Web Server 可以同时读取各个 worktree 中的 `schemes/active/modules.json`，从而在前端渲染出"左右分屏"的对比效果

---

## 五、架构启示

在这种模式下，AI 的核心竞争力不再是"画得有多快"，而是：

### 5.1 多样性 (Diversity)

如果 AI 生成的三个方案大同小异，并行就失去了意义。AI 必须学会通过调整温度 (Temperature)、惩罚系数或设计权重来确保输出的差异化。

**实现方式**：
- 为不同 Agent 实例注入不同的策略参数
- 在 System Prompt 中强调差异化要求
- 通过 `strategy.json` 配置权重和约束

### 5.2 Git 即记忆 (Git as Memory)

AI 不再需要维护复杂的上下文窗口。Git 的 Commit History 就是它的思考轨迹，Branch 就是它的不同思路。

**优势**：
- 每个分支是完整的状态快照
- 可以随时回滚到任意历史点
- Commit Message 记录决策理由

### 5.3 可解释性 (Explainability)

因为是异步交付，AI 必须通过 Commit Message 或 Markdown 文档，向用户"推销"它的设计理念，这就要求 AI 具备极强的表达能力。

**要求**：
- 每个 Commit 附带清晰的设计说明
- 生成 `schemes/{s}/README.md` 解释权衡取舍
- 支持用户追溯决策历史

---

## 附录 A: 完整工作流程图

```
┌─────────────────────────────────────────────────────────────────────────┐
│                     Agent + Git 完整工作流                               │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  【触发阶段】                                                            │
│  用户请求 ──→ Agent 解析意图 ──→ 创建策略配置                            │
│                                                                         │
│  【准备阶段】                                                            │
│  Server ──→ git worktree add .worktrees/ai-job-{id} feat/ai-{name}     │
│         ──→ 将策略配置写入 Worktree                                     │
│         ──→ 启动 Agent 进程，传入 Worktree 路径                         │
│                                                                         │
│  【执行阶段】                                                            │
│  Agent ──→ 在 Worktree 中读取数据                                       │
│        ──→ 执行布置决策                                                  │
│        ──→ 写入 schemes/{s}/*.json                                      │
│        ──→ 写入设计说明 schemes/{s}/README.md                           │
│        ──→ git add . && git commit -m "feat(layout): ..."              │
│                                                                         │
│  【验证阶段】                                                            │
│  Server ──→ 读取 Commit 内容                                            │
│         ──→ 执行约束验证                                                 │
│         ──→ 验证通过：通知前端展示                                       │
│         ──→ 验证失败：通知 Agent 修正                                    │
│                                                                         │
│  【交付阶段】                                                            │
│  用户选择 ──→ Server 执行 git merge feat/ai-{name} 到 main             │
│           ──→ git worktree remove .worktrees/ai-job-{id}               │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 附录 B: 相关文档

| 文档 | 路径 | 内容 |
|------|------|------|
| Agent SDK 技术指南 | [Agent_SDK.md](./Agent_SDK.md) | SDK API、SubAgent 实现 |
| Agent 架构设计 | [Agent_Design.md](./Agent_Design.md) | SubAgent 架构、提示词设计 |
| 系统架构 | [Architecture.md](./Architecture.md) | 整体架构、数据流 |
| 业务流程 | [Flow_Workflows.md](./Flow_Workflows.md) | 端到端工作流 |

---

> **总结**：这套架构将软件工程中成熟的 **"分支管理 (Branching)"** 哲学，完美映射到了 **"设计探索 (Design Exploration)"** 的过程中，让 AI 真正成为了人类设计师的"并发增强器"。
