# AI 并行设计模式与架构哲学 (AI Parallel Design Patterns)

> **核心理念**：将"文件驱动"、"异步协作"与"并行生成"相结合，把 AI 从单一的对话者升级为**"拥有无限分身的并发设计团队"**。

**实现状态**: ✅ v3.1 已完成核心架构实现
**相关代码**: `BIMCanvas.Server/Services/GitWorktreeService.cs`, `StrategyService.cs`

---

## 1. 架构哲学：从“聊天机器人”到“数字化员工集群”

在 BIMCanvas 的架构中，AI 的角色发生了质的飞跃。借助 Git 分支的低成本特性，系统可以瞬间唤醒多个 AI 实例，在平行的“宇宙”中同时探索不同的设计可能性。

### 三大支柱

1.  **文件驱动 (File-Driven)**
    *   **真理源**：每个分支都是一套完整、真实、可独立运行的文件系统。
    *   **无状态**：AI 不需要记忆复杂的上下文，文件本身就是状态。

2.  **异步协作 (Async Collaboration)**
    *   **非阻塞**：用户无需等待 AI 生成，AI 在后台分支默默工作。
    *   **提交即交付**：AI 通过 Commit 和 Pull Request 交付成果，而非流式文本。

3.  **并行生成 (Parallel Generation)**
    *   **算力换广度**：利用并发能力，同时探索 N 种可能性，打破人类设计师的线性工作限制。

---

## 2. 核心场景推演 (Scenarios)

以下三个场景展示了这种架构如何彻底改变设计流程：

### 场景 A：策略分叉 (The "Strategy Fork")
> **目标**：风格与策略的 A/B 测试

**用户指令**：“给我的客厅出三个方案：一个是‘极致收纳’，一个是‘动线优先’，还有一个‘极简留白’。”

**系统行为**：
1.  **分支裂变**：Server 瞬间基于 `main` 创建三个分支：
    *   `feat/ai-living-storage`
    *   `feat/ai-living-flow`
    *   `feat/ai-living-minimal`
2.  **并发执行**：三个 AI Agent 实例同时启动，加载同一份 `baseline/` 数据，但注入不同的**策略参数 (Strategy Config)**。
3.  **独立产出**：
    *   AI-1 (收纳)：在 `schemes/` 中生成了满墙柜体，牺牲了部分通道宽度。
    *   AI-2 (动线)：保留了宽敞的回游动线，减少了非必要家具。
    *   AI-3 (极简)：只保留了核心家具，大量留白。

**对 AI 的能力要求**：
*   **策略参数化**：AI 的入口必须支持配置权重（如 `storage_weight=0.9`, `flow_weight=0.2`）。
*   **自我辩护**：提交方案时需附带 Markdown 设计说明，解释“为了达成策略，我做出了哪些权衡”。

### 场景 B：布局求解器 (The "Layout Solver")
> **目标**：硬约束下的局部最优解暴力搜索

**用户指令**：“这个卫生间太小了，帮我看看能不能塞进一个浴缸和淋浴房。”

**系统行为**：
1.  **沙盒模式**：AI 创建一个临时分支 `feat/ai-bathroom-solver`。
2.  **蒙特卡洛搜索**：AI 在后台进行高频迭代。
    *   尝试 1：失败（浴缸挡门）。
    *   尝试 2：失败（淋浴房与马桶重叠）。
    *   ...
    *   尝试 99：失败。
    *   尝试 100：**成功**（找到了唯一可行的极限布局）。
3.  **结果交付**：只有验证成功的第 100 号方案会被提交，之前的 99 次失败对用户透明。

**对 AI 的能力要求**：
*   **沙盒模拟**：具备在不污染主分支的情况下进行“试错-回滚”的能力。
*   **失败感知**：能读懂 Server 的验证错误（Validation Error），并将其转化为下一次尝试的约束条件。

### 场景 C：主编式合并 (The "Editorial Merge")
> **目标**：用户作为总设计师的方案融合

**用户指令**：用户看着三个平行方案，觉得“方案 A 的沙发摆得好，但方案 B 的电视柜设计更合理”。

**系统行为**：
1.  **可视化对比**：前端通过“三联屏”或“多层叠加”展示不同分支的渲染结果。
2.  **区域级选择**：用户勾选方案 A 的 `Zone: SofaArea` 和方案 B 的 `Zone: TVArea`。
3.  **Cherry-pick**：Server 执行精确的 JSON 合并，将两个分支的特定片段融合到 `main` 分支。

**对 AI 的能力要求**：
*   **解耦设计**：AI 生成的方案应高度模块化，避免强耦合（例如：沙发和电视柜若有强视距关联，需在元数据中标记 `DependencyGroup`，提示用户成套采纳）。

---

## 3. Git 翻译层 (The Git Translation Layer)

> **核心挑战**：如何将用户模糊的自然语言指令（如“把客厅设计得温馨一点”）转化为精确的 Git 操作序列？

这需要一个专门的 **“Git 翻译层”**，它包含三个步骤：

### 3.1 意图解析 (Intent Parsing)
将自然语言转化为结构化的 **“设计意图对象” (Design Intent Object)**。

*   **输入**：“给我的客厅出三个方案：一个是‘极致收纳’，一个是‘动线优先’，还有一个‘极简留白’。”
*   **输出**：
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

### 3.2 操作编排 (Operation Orchestration)
将意图对象转化为具体的 **Git 命令序列**。

*   **转换逻辑**：
    1.  `git checkout main` (确保基准正确)
    2.  `git pull` (同步最新状态)
    3.  **Loop for each branch**:
        *   `git checkout -b feat/ai-living-{name}`
        *   `Agent.run(strategy)` -> 生成 JSON 文件
        *   `git add .`
        *   `git commit -m "Design: Living Room with {name} strategy"`

### 3.3 语义化提交 (Semantic Commits)
AI 必须学会写“人话”Commit Message，而不是机器码。

*   **差评**：`Update modules.json`
*   **好评**：`feat(living-room): Maximize storage by adding full-wall cabinets, sacrificing 200mm aisle width`

---

## 4. Git 架构选型 (Git Architecture Selection)

> **核心问题**：不同的 Git 分支就能实现并行开发吗？

在传统的 Git 使用场景中，同一时间只能 Checkout 一个分支。如果强行在同一个文件夹里切来切去，确实无法实现“并行”。
为了实现真正的**物理隔离**和**并发读写**，我们采用 **“混合架构 (Hybrid Approach)”**：

### 4.1 核心解密：Git Worktree (多工作树)

通常我们认为：`1 个 Git 仓库 = 1 个文件夹 = 1 个当前分支`。
但实际上，Git 支持：`1 个 Git 仓库 = N 个文件夹 = N 个并行分支`。

**`git worktree`** 允许你从同一个 `.git` 仓库中，“映射”出多个独立的文件夹，每个文件夹对应不同的分支。

### 4.2 架构方案对比

| 架构方案 | 概念 | 物理结构 | 适用场景 | 结论 |
| :--- | :--- | :--- | :--- | :--- |
| **多分支 (Multi-Branch)** | 标准 Git | 1 个文件夹，内容切换 | 单人串行工作 | ❌ 无法并行 |
| **多仓库 (Multi-Repo)** | 分布式 | N 个文件夹，独立历史 | 完全独立的项目 | ❌ 合并困难，空间浪费 |
| **多工作树 (Multi-Worktree)** | **链接克隆** | **N 个文件夹，共享历史** | **单机并行工作** | **✅ 最佳选择** |

### 4.3 混合架构落地 (The Hybrid Approach)

> ✅ **已在 v3.1 中实现** - 见 `GitWorktreeService.cs` 和 `StrategyService.cs`

1.  **存储层 (Storage Layer)**：
    *   使用 **单仓库 + 多分支**。
    *   所有数据都在一个 `.git` 历史中，高效且标准。
    *   `main` 分支是用户的当前状态。
    *   `scheme/{id}` 分支存储保存的设计方案。
    *   `feat/ai-{jobId}-{name}` 分支存储 AI 的临时提案。

2.  **执行层 (Execution Layer)**：
    *   使用 **Git Worktree** 处理 *临时 (Ephemeral)* 任务。
    *   当 AI 启动时：`git worktree add .worktrees/ai-job-1 feat/ai-proposal`。
    *   当 AI 完成时：`git worktree remove .worktrees/ai-job-1`。

**实现代码示例**：

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

### 4.4 为什么这能解决问题？

*   **对于并行生成**：AI-1 和 AI-2 分别在 `.worktrees/ai-job-1` 和 `.worktrees/ai-job-2` 两个物理隔离的文件夹中工作，互不干扰，可以同时写入。
*   **对于 Web 对比**：Web Server 可以同时读取各个 worktree 中的 `schemes/active/modules.json`，从而在前端渲染出"左右分屏"的对比效果。

---

## 5. 架构启示 (Implications)

在这种模式下，AI 的核心竞争力不再是“画得有多快”，而是：

1.  **多样性 (Diversity)**：
    如果 AI 生成的三个方案大同小异，并行就失去了意义。AI 必须学会通过调整温度 (Temperature)、惩罚系数或设计权重来确保输出的差异化。

2.  **Git 即记忆 (Git as Memory)**：
    AI 不再需要维护复杂的上下文窗口。Git 的 Commit History 就是它的思考轨迹，Branch 就是它的不同思路。

3.  **可解释性 (Explainability)**：
    因为是异步交付，AI 必须通过 Commit Message 或 Markdown 文档，向用户“推销”它的设计理念，这就要求 AI 具备极强的表达能力。

---

> **总结**：
> 这套架构将软件工程中成熟的 **“分支管理 (Branching)”** 哲学，完美映射到了 **“设计探索 (Design Exploration)”** 的过程中，让 AI 真正成为了人类设计师的“并发增强器”。
