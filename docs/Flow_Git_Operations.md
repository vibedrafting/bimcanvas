# Git 标准工作流

> **版本**：v1.3 | **更新日期**：2026-01-15
> **目的**：标准化 Agent 和 Web 用户的 Git 操作流程，提高准确率和效率

---

## 一、概述

### 1.1 文档目的

本文档定义了 BIMCanvas 中所有 Git 操作的标准流程，包括：
- **Agent 工作流**：策略分叉、布局求解、主编式合并、查看/编辑任务
- **Web 用户操作**：切换分支、保存、回档、新建策略/变体、多开窗口

### 1.2 核心架构

```
Agent (Python)
    │
    ├─► @mcp_tool() 装饰器定义 Git 工具
    │   └─► mcp/tools/git_tools.py
    │
    ▼
MCP 工具（进程内，SDK MCP Server）
    │
    ├─► HTTP 调用 Server REST API
    │   └─► api/git/checkout, api/git/commit, ...
    │
    ▼
Server (GitWorktreeService.cs)
    │
    └─► subprocess 调用 Git CLI
```

### 1.3 安全约束

| 操作 | Agent 可用 | Web 可用 | 说明 |
|------|------------|----------|------|
| status | ✅ | ✅ | 只读 |
| commit | ✅ | ✅ | 安全写入 |
| checkout | ✅ | ✅ | 安全切换 |
| merge | ✅ | ✅ | 禁止 --force |
| worktree create/remove | ✅ | ❌ | 仅 Server 内部 |
| push | ❌ | ❌ | 不暴露 |
| reset --hard | ❌ | ❌ | 不暴露 |
| rebase | ❌ | ❌ | 不暴露 |

### 1.4 核心概念：策略 vs 变体 vs Worktree

> 详细定义见 [Arch_Parallel_Development.md §5](./Arch_Parallel_Development.md#五worktree-架构)

```
策略 (Strategy)
    │
    ├─► 定义：项目初期确定的设计理念/边界条件
    │         影响全局（整个户型），长期保留、独立演进
    ├─► 存储：baseline/strategies/ (模板) + schemes/strategy.json (当前分支副本)
    ├─► 分支：scheme/{strategyName}
    └─► 示例：极致收纳、动线优先、极简留白

变体 (Variant)
    │
    ├─► 定义：策略下的局部差异尝试
    │         影响局部（某个区域），可能被采纳或丢弃
    ├─► 分支：scheme/{strategyName}-{variantName}
    └─► 示例：scheme/极致收纳-方案A
```

#### Worktree 两种使用场景

> **Git Worktree 核心限制**：同一个分支不能被两个 Worktree 同时检出

| 场景 | 目的 | 分支来源 | 删除时分支处理 |
|------|------|----------|----------------|
| **并行开发** | 多开 AI 对话窗口 | 检出**已有分支** | 保留分支 |
| **隔离环境** | SubAgent 执行写任务 | 创建**临时分支** | 删除临时分支 |

**场景 A：并行开发（虚拟窗口）**

```
用户新建窗口，选择 scheme/极致收纳 分支
    │
    ▼
git worktree add .worktrees/window-2 scheme/极致收纳
    │
    ▼
用户关闭窗口 → 删除 Worktree，保留分支
```

**场景 B：隔离环境（Agent 任务）**

```
虚拟窗口 A 在 scheme/极致收纳 分支
用户请求："重新布置客厅"
    │
    ▼
基于当前分支创建临时分支 feat/ai-layout-xxx
git worktree add .worktrees/agent-job-1 -b feat/ai-layout-xxx
    │
    ▼
SubAgent 工作 → commit → 合并回 scheme/极致收纳
    │
    ▼
清理：删除 Worktree + 删除临时分支
```

**核心原则**：
- **虚拟窗口 Worktree**：检出已有分支，删除时保留分支
- **Agent 任务 Worktree**：创建临时分支，删除时清理分支
- **Branch = 持久版本**：用户确认的设计状态，长期保存

### 1.5 策略配置结构

策略配置采用**两层存储**：
- `baseline/strategies/*.json`：策略模板（项目级，可复用）
- `schemes/strategy.json`：当前分支使用的策略副本

```json
{
  "id": "strategy_001",
  "name": "极致收纳",
  "description": "最大化储物空间，适合物品较多的家庭",

  // 设计权重（参数化评分）
  "weights": {
    "storage": 0.9,      // 储物优先
    "circulation": 0.3,  // 通道宽度可略窄
    "aesthetics": 0.5,   // 美观适中
    "comfort": 0.6       // 舒适度适中
  },

  // 设计规则（规范约束）
  "designRules": {
    "source": "万科设计规范v2.0",
    "minAisleWidth": 600,       // 允许较窄通道
    "minBedClearance": 500
  },

  // 用户约束（人为边界）
  "userConstraints": [
    { "type": "tv_wall", "zoneId": "rz_living", "wall": "north" },
    { "type": "bed_orientation", "zoneId": "rz_master", "facing": "south" }
  ],

  // AI 提示词（设计倾向指导）
  "designHints": "优先选择带储物功能的家具，允许通道略窄（≥600mm），充分利用墙角空间"
}
```

### 1.6 任务类型与 Worktree 关系

**关键决策**：所有 AI 写操作都需要 Worktree 隔离

| 任务类型 | 示例 | 需要 Worktree | 说明 |
|----------|------|---------------|------|
| **query** | 统计家具数量、查看布置 | ❌ 不需要 | 只读操作，直接读取当前分支 |
| **execute（简单编辑）** | 移动床 50cm | ✅ 需要 | 创建 Worktree → 编辑 → 合并 |
| **execute（复杂生成）** | 重新布置整个卧室 | ✅ 需要 | 创建 Worktree → 生成 → 合并 |

---

## 二、存档系统

### 2.1 存档类型

| 类型 | 触发条件 | commit message | 用户交互 |
|------|----------|----------------|----------|
| **自动存档** | Server API 层拦截（创建 Worktree 前） | `自动存档_{timestamp}` | 无（静默执行） |
| **手动存档** | 1. 用户点击保存按钮<br>2. 用户切换分支时弹窗选择「保存」 | 默认格式或自定义 | 可输入 |

### 2.2 自动存档触发的操作

| API | 触发自动存档 | 说明 |
|-----|-------------|------|
| `POST /api/git/worktrees` | ✅ 创建前自动存档 | 检测到未提交更改时静默执行 |
| `POST /api/git/checkout` | ✅ 弹窗确认 | 用户选择「保存」时执行 |
| 其他只读/清理操作 | ❌ 不需要 | 不影响数据完整性 |

### 2.3 自动存档机制

```
用户请求创建 Worktree
    │
    ▼
Server 检测工作区状态
    │
    ├─► 工作区干净（无未提交更改）
    │       │
    │       ▼
    │   直接创建 Worktree
    │
    └─► 工作区不干净（有未提交更改）
            │
            ▼
        自动执行：git add . && git commit -m "自动存档_{timestamp}"
            │
            ▼
        创建 Worktree
```

**核心原则**：
- 自动存档对用户透明，不阻塞操作
- 保护用户数据，避免因 Git 操作丢失未保存的更改
- commit message 格式：`自动存档_{yyyyMMdd_HHmmss}`

### 2.4 手动存档流程

**场景 A：用户点击保存按钮**

```
用户点击「保存」
    │
    ▼
POST /api/git/commit { message: "用户自定义信息" }
    │
    ▼
显示保存结果
```

**场景 B：切换分支时弹窗确认**

```
用户尝试切换分支
    │
    ▼
检测到未提交更改
    │
    ▼
弹窗：「存在未保存的更改」
    │
    ├─► 用户选择「保存」→ 执行手动存档后切换
    ├─► 用户选择「放弃」→ 丢弃更改后切换
    └─► 用户选择「取消」→ 不执行操作
```

---

## 三、已有实现清单

### 3.1 Server REST API（已实现）

| 端点 | 方法 | 功能 | 文件位置 |
|------|------|------|----------|
| `api/git/branches` | GET | 获取所有分支列表 | `GitController.cs:33` |
| `api/git/current` | GET | 获取当前分支 | `GitController.cs:321` |
| `api/git/status` | GET | 获取工作区状态 | `GitController.cs:271` |
| `api/git/checkout` | POST | 切换分支 | `GitController.cs:71` |
| `api/git/commit` | POST | 提交更改 | `GitController.cs:173` |
| `api/git/discard` | POST | 放弃更改 | `GitController.cs:234` |
| `api/git/worktrees` | GET | 获取 Worktree 列表 | `GitController.cs:355` |
| `api/git/worktrees` | POST | 创建 Worktree（含自动存档） | `GitController.cs:402` |
| `api/git/worktrees/{name}` | DELETE | 删除 Worktree | `GitController.cs:460` |
| `api/git/merge` | POST | 合并分支 | `GitController.cs:505` |

### 3.2 Server 内部方法（GitWorktreeService.cs）

| 方法 | 功能 | 说明 |
|------|------|------|
| `CreateWorktree()` | 创建 Worktree | 智能判断分支是否存在，支持 baseBranch 参数 |
| `RemoveWorktree()` | 删除 Worktree | 支持 deleteBranch 参数 |
| `GetWorktrees()` | 列出所有 Worktree | - |
| `MergeBranch()` | 合并分支 | - |
| `CreateBranch()` | 创建分支 | 支持 baseBranch 参数 |
| `DeleteBranch()` | 删除分支 | - |
| `Commit()` | 提交更改 | - |
| `HasUncommittedChanges()` | 检查未提交更改 | - |
| `DiscardChanges()` | 放弃更改 | - |

---

## 四、Agent 工作流 Git 操作

### 4.1 场景 A：策略分叉 (Strategy Fork)

**用户指令**："给我的客厅出三个方案：一个是'极致收纳'，一个是'动线优先'，还有一个'极简留白'。"

| 阶段 | 操作 | Git 命令 | 调用方 | API/方法 |
|------|------|----------|--------|----------|
| **准备** | 创建 3 个 Worktree | `git worktree add .worktrees/ai-{id} -b feat/ai-{name}` | Server | `CreateAiJobWorktree()` |
| **执行** | Agent 在 Worktree 中工作 | 文件读写 | Agent | - |
| **提交** | 提交方案 | `git add . && git commit -m "..."` | Agent (MCP) | `POST /api/git/commit` |
| **验证** | Server 验证结果 | - | Server | 验证服务 |
| **交付** | 用户选择后合并 | `git merge feat/ai-{name}` | Server | `AcceptAiJob()` |
| **清理** | 删除 Worktree + 分支 | `git worktree remove && git branch -d` | Server | `AcceptAiJob(deleteAfterMerge: true)` |

**流程图**：

```
用户请求 "出三个方案"
    │
    ▼
┌───────────────────────────────────────────────────────────────┐
│  【准备阶段】Server 编排                                        │
├───────────────────────────────────────────────────────────────┤
│  CreateAiJobWorktree("storage", "极致收纳")                    │
│      → .worktrees/ai-storage (feat/ai-storage-极致收纳)       │
│  CreateAiJobWorktree("flow", "动线优先")                       │
│      → .worktrees/ai-flow (feat/ai-flow-动线优先)             │
│  CreateAiJobWorktree("minimal", "极简留白")                    │
│      → .worktrees/ai-minimal (feat/ai-minimal-极简留白)       │
└───────────────────────────────────────────────────────────────┘
    │
    ▼ 并行启动 3 个 SubAgent
┌───────────────────────────────────────────────────────────────┐
│  【执行阶段】SubAgent × 3 并行                                  │
├───────────────────────────────────────────────────────────────┤
│  SubAgent-1 @ .worktrees/ai-storage                           │
│      → 读取数据 → 布置决策 → 写入 schemes/**/*.json           │
│      → git add . && git commit -m "feat(layout): 极致收纳"    │
│                                                               │
│  SubAgent-2 @ .worktrees/ai-flow                              │
│      → 读取数据 → 布置决策 → 写入 schemes/**/*.json           │
│      → git add . && git commit -m "feat(layout): 动线优先"    │
│                                                               │
│  SubAgent-3 @ .worktrees/ai-minimal                           │
│      → 读取数据 → 布置决策 → 写入 schemes/**/*.json           │
│      → git add . && git commit -m "feat(layout): 极简留白"    │
└───────────────────────────────────────────────────────────────┘
    │
    ▼
┌───────────────────────────────────────────────────────────────┐
│  【验证阶段】Server 验证                                        │
├───────────────────────────────────────────────────────────────┤
│  读取各 Worktree 的 Commit 内容                                │
│  执行约束验证（碰撞检测、边界检查等）                           │
│  ✅ 验证通过 → 通知前端展示三联屏                               │
│  ❌ 验证失败 → 通知 SubAgent 修正                               │
└───────────────────────────────────────────────────────────────┘
    │
    ▼
┌───────────────────────────────────────────────────────────────┐
│  【交付阶段】用户选择                                           │
├───────────────────────────────────────────────────────────────┤
│  用户在三联屏中选择 "动线优先" 方案                             │
│  Server: AcceptAiJob("flow", deleteAfterMerge: true)          │
│      → git checkout main                                      │
│      → git merge feat/ai-flow-动线优先                        │
│      → git worktree remove .worktrees/ai-flow                 │
│      → git branch -d feat/ai-flow-动线优先                    │
│  清理其他未采纳的 Worktree                                     │
└───────────────────────────────────────────────────────────────┘
```

### 4.2 场景 B：布局求解器 (Layout Solver)

**用户指令**："这个卫生间太小了，帮我看看能不能塞进一个浴缸和淋浴房。"

| 阶段 | 操作 | Git 命令 | 调用方 | API/方法 |
|------|------|----------|--------|----------|
| **准备** | 创建 1 个 Worktree | `git worktree add .worktrees/ai-solver -b feat/ai-solver` | Server | `CreateAiJobWorktree()` |
| **迭代** | Agent 迭代尝试 | 文件读写 + 验证 | Agent | - |
| **提交** | 验证成功后提交 | `git add . && git commit` | Agent (MCP) | `POST /api/git/commit` |
| **交付** | 合并到用户分支 | `git merge feat/ai-solver` | Server | `AcceptAiJob()` |
| **清理** | 删除 Worktree | `git worktree remove` | Server | `AcceptAiJob(deleteAfterMerge: true)` |

**流程图**：

```
用户请求 "布置浴缸和淋浴房"
    │
    ▼
┌───────────────────────────────────────────────────────────────┐
│  【准备阶段】Server 编排                                        │
├───────────────────────────────────────────────────────────────┤
│  CreateAiJobWorktree("solver", "layout-solve")                │
│      → .worktrees/ai-solver (feat/ai-solver-layout-solve)     │
└───────────────────────────────────────────────────────────────┘
    │
    ▼
┌───────────────────────────────────────────────────────────────┐
│  【迭代阶段】SubAgent 循环尝试                                  │
├───────────────────────────────────────────────────────────────┤
│  Loop:                                                        │
│      → 读取约束条件                                            │
│      → 尝试布置方案                                            │
│      → 请求 Server 验证                                        │
│          ❌ 失败 → 调整参数，继续循环                          │
│          ✅ 成功 → 跳出循环                                    │
└───────────────────────────────────────────────────────────────┘
    │
    ▼ 验证成功
┌───────────────────────────────────────────────────────────────┐
│  【提交阶段】                                                   │
├───────────────────────────────────────────────────────────────┤
│  git add .                                                    │
│  git commit -m "feat(layout): 卫生间布置方案"                  │
└───────────────────────────────────────────────────────────────┘
    │
    ▼
┌───────────────────────────────────────────────────────────────┐
│  【交付阶段】                                                   │
├───────────────────────────────────────────────────────────────┤
│  Server: AcceptAiJob("solver", deleteAfterMerge: true)        │
│      → git merge feat/ai-solver-layout-solve                  │
│      → git worktree remove .worktrees/ai-solver               │
│      → git branch -d feat/ai-solver-layout-solve              │
└───────────────────────────────────────────────────────────────┘
```

### 4.3 场景 C：主编式合并 (Editorial Merge)

**用户操作**：用户看着三个平行方案，觉得"方案 A 的沙发摆得好，但方案 B 的电视柜设计更合理"。

| 阶段 | 操作 | Git 命令 | 调用方 | API/方法 |
|------|------|----------|--------|----------|
| **展示** | 三联屏对比 | - | Web | - |
| **选择** | 用户勾选区域 | - | Web | - |
| **合并** | Cherry-pick 合并 | `git checkout --patch` 或 JSON 合并 | Server | 自定义合并逻辑 |
| **清理** | 删除未采纳方案 | `git worktree remove && git branch -d` | Server | `RemoveWorktree()` + `DeleteBranch()` |

**流程图**：

```
用户在三联屏中进行区域级选择
    │
    ├─► 勾选方案 A 的 Zone: SofaArea
    └─► 勾选方案 B 的 Zone: TVArea
    │
    ▼
┌───────────────────────────────────────────────────────────────┐
│  【合并阶段】Server 执行 JSON 级合并                            │
├───────────────────────────────────────────────────────────────┤
│  从方案 A 提取: schemes/rz_living/modules.json (SofaArea)     │
│  从方案 B 提取: schemes/rz_living/modules.json (TVArea)       │
│  合并到 main 分支                                              │
│                                                               │
│  检查依赖冲突：                                                 │
│      如有 DependencyGroup 被拆分 → 警告用户                    │
└───────────────────────────────────────────────────────────────┘
    │
    ▼
┌───────────────────────────────────────────────────────────────┐
│  【清理阶段】                                                   │
├───────────────────────────────────────────────────────────────┤
│  删除方案 A Worktree: git worktree remove .worktrees/ai-a     │
│  删除方案 B Worktree: git worktree remove .worktrees/ai-b     │
│  删除方案 C Worktree: git worktree remove .worktrees/ai-c     │
│  删除对应分支                                                  │
└───────────────────────────────────────────────────────────────┘
```

### 4.4 场景 D：查看任务 (Query Task)

**用户指令**："当前卧室布置了什么？" / "统计一下家具数量"

**特点**：query 任务**不需要 Worktree**，直接读取当前分支数据。

| 阶段 | 操作 | Git 命令 | 调用方 |
|------|------|----------|--------|
| **读取** | 直接读取当前分支文件 | 无 | Agent |
| **返回** | 返回结果给用户 | 无 | Agent |

```
用户问 "当前卧室有多少家具"
    │
    ▼
Agent 判断任务类型: query（只读）
    │
    ▼
直接读取 schemes/rz_master/modules.json
    │
    ▼
返回结果: "主卧当前布置了 5 件家具：..."
```

### 4.5 场景 E：简单编辑 (Simple Edit)

**用户指令**："把床向右移动 50cm" / "删除那个床头柜"

**特点**：所有 execute 任务都需要 Worktree 隔离，即使是简单编辑。

| 阶段 | 操作 | Git 命令 | 调用方 |
|------|------|----------|--------|
| **准备** | 创建 Worktree | `git worktree add .worktrees/ai-edit -b feat/ai-edit` | Server |
| **编辑** | 修改 JSON | 文件写入 | Agent |
| **提交** | 提交更改 | `git add . && git commit` | Agent (MCP) |
| **合并** | 合并到用户分支 | `git merge feat/ai-edit` | Server |
| **清理** | 删除 Worktree | `git worktree remove` | Server |

```
用户请求 "把床向右移动 50cm"
    │
    ▼
Agent 判断任务类型: execute（可写）
    │
    ▼
Server 创建 Worktree: .worktrees/ai-edit
    │
    ▼
Agent 修改 schemes/rz_master/modules.json
    │
    ▼
git add . && git commit -m "edit: 移动床位置"
    │
    ▼
合并到用户分支 → 可视化 diff 确认 → 清理 Worktree
```

### 4.6 场景 F：多窗口独立任务 (Multi-Window Independent)

**场景**：用户开了两个窗口，分别操作不同的策略分支。

| 窗口 | 分支 | 任务 | Worktree |
|------|------|------|----------|
| 窗口 1 | scheme/极致收纳 | "布置主卧" | .worktrees/ai-win1-job |
| 窗口 2 | scheme/动线优先 | "布置主卧" | .worktrees/ai-win2-job |

**特点**：各窗口在各自分支的 Worktree 中独立工作，互不干扰。

```
窗口 1 请求 "布置主卧"（scheme/极致收纳）
    │
    ▼
Server 创建: .worktrees/ai-win1-job (feat/ai-win1-master-bedroom)
    │
    ▼
Agent 在 Worktree 中工作...

                    同时

窗口 2 请求 "布置主卧"（scheme/动线优先）
    │
    ▼
Server 创建: .worktrees/ai-win2-job (feat/ai-win2-master-bedroom)
    │
    ▼
Agent 在 Worktree 中工作...
```

### 4.7 场景 G：同分支多窗口（禁止）

**核心规则**：**一个分支只能被一个窗口打开**

```
窗口 1 已打开 scheme/极致收纳
    │
    ▼
窗口 2 尝试打开 scheme/极致收纳
    │
    ▼
❌ Server 拒绝: "该分支已在窗口 1 中打开"
```

**原因**：
- Git 不允许两个 Worktree 检出同一分支
- 避免并发修改导致的冲突

### 4.8 Agent 任务合并注意事项（关键）

#### 合并流程要点

| 步骤 | 操作 | 说明 |
|------|------|------|
| 1 | SubAgent 在临时 Worktree 中完成工作 | `feat/ai-xxx` 临时分支 |
| 2 | SubAgent 提交更改 | `git commit` |
| 3 | **在用户 Worktree 中执行 merge** | 关键：不是在 Agent Worktree 中 |
| 4 | **Canvas 强制刷新** | 显示合并后的最新状态 |
| 5 | 清理：删除 Worktree + 临时分支 | 先删 Worktree，再删分支 |

#### 合并执行位置

```
❌ 错误：在 Agent Worktree 中合并
   cd .worktrees/agent-job-1
   git merge ...  ← 不对

✅ 正确：在用户 Worktree 中合并
   cd .worktrees/window-2           # 用户的虚拟窗口
   git merge feat/ai-layout-xxx     # 合并 Agent 的临时分支
   # 文件自动更新为合并结果
```

#### Canvas 刷新机制

**合并完成后，必须强制刷新 Canvas**：

```
合并前：Canvas 渲染 .worktrees/window-2 目录
    │
    ▼
执行 git merge feat/ai-xxx
    │
    ▼
.worktrees/window-2 的文件内容已变化
    │
    ▼
Canvas 必须重新读取目录并刷新显示  ← 重要！
    │
    ▼
用户看到合并后的最新结果
```

**注意**：虚拟窗口不能手动切换分支，但合并操作会更新分支内容，所以 Canvas 需要感知文件变化并刷新。

#### 清理顺序

```bash
# 1. 先删除 Worktree
git worktree remove .worktrees/agent-job-1

# 2. 再删除临时分支
git branch -d feat/ai-layout-xxx
```

**原因**：如果先删分支，Worktree 会处于"detached HEAD"状态，可能导致问题。

---

## 五、Web 用户操作 Git 流程

### 5.1 切换分支 (Switch Branch)

**触发**：用户点击「切换分支」按钮

```
用户点击「切换分支」
    │
    ▼
GET /api/git/status
    │
    ├─► hasUncommittedChanges: false
    │       │
    │       ▼
    │   POST /api/git/checkout { branchName: "target-branch" }
    │       │
    │       ▼
    │   Canvas 刷新渲染
    │
    └─► hasUncommittedChanges: true
            │
            ▼
        弹窗：「存在未保存的更改」
            │
            ├─► 用户选择「保存」
            │       │
            │       ▼
            │   POST /api/git/commit { message: "自动存档_切换分支前" }
            │       │
            │       ▼
            │   POST /api/git/checkout { branchName: "target-branch" }
            │
            ├─► 用户选择「放弃」
            │       │
            │       ▼
            │   POST /api/git/checkout {
            │       branchName: "target-branch",
            │       discardBeforeCheckout: true
            │   }
            │
            └─► 用户选择「取消」
                    │
                    ▼
                关闭弹窗，不执行操作
```

**API 参数说明**：

```typescript
// POST /api/git/checkout
interface CheckoutBranchRequest {
  branchName: string;           // 目标分支名
  createIfNotExist?: boolean;   // 分支不存在时是否创建
  commitBeforeCheckout?: boolean;  // 切换前自动提交
  discardBeforeCheckout?: boolean; // 切换前放弃更改
  commitMessage?: string;       // 自动提交的信息
}
```

### 5.2 执行保存 (Save/Commit)

**触发**：用户点击「保存」按钮

```
用户点击「保存」
    │
    ▼
POST /api/git/commit { message: "用户手动保存" }
    │
    ├─► committed: true
    │       │
    │       ▼
    │   显示「保存成功」提示
    │   更新 UI 状态（最后保存时间）
    │
    └─► committed: false, message: "没有需要提交的更改"
            │
            ▼
        显示「没有需要保存的更改」提示
```

**API 参数说明**：

```typescript
// POST /api/git/commit
interface CommitRequest {
  message?: string;  // 可选，默认为 "自动存档_{timestamp}"
}

// Response
interface CommitResponse {
  success: boolean;
  committed: boolean;  // 是否实际执行了提交
  message: string;
  commit?: {
    hash: string;
    message: string;
    time: string;
    author: string;
  };
}
```

### 5.3 回档 (Rollback)

**触发**：用户点击「回档」或「恢复到历史版本」

```
用户点击「回档」
    │
    ▼
GET /api/git/branches
    │
    ▼
展示历史提交列表
    │
    ▼
用户选择目标版本 (commitHash)
    │
    ▼
确认弹窗：「当前更改将丢弃，确认回档？」
    │
    ├─► 用户确认
    │       │
    │       ▼
    │   POST /api/git/discard
    │       │
    │       ▼
    │   POST /api/git/checkout { branchName: commitHash }
    │       │
    │       ▼
    │   Canvas 刷新渲染
    │
    └─► 用户取消
            │
            ▼
        关闭弹窗，不执行操作
```

**注意事项**：
- 回档操作会丢弃当前所有未提交的更改
- 回档实际上是 checkout 到指定的 commit hash
- 建议在回档前提醒用户保存重要更改

### 5.4 查看历史保存 (View History)

**触发**：用户点击「历史记录」

```
用户点击「历史记录」
    │
    ▼
GET /api/git/branches
    │
    ▼
展示提交列表
    ┌──────────────────────────────────────┐
    │ hash    │ message        │ time     │
    ├─────────┼────────────────┼──────────┤
    │ a1b2c3d │ 用户手动保存   │ 2分钟前  │
    │ e4f5g6h │ 自动存档_...   │ 10分钟前 │
    │ i7j8k9l │ feat: AI布置   │ 1小时前  │
    └──────────────────────────────────────┘
    │
    ▼
用户可选择：
    ├─► 「切换到此版本」→ 执行 4.3 回档流程
    └─► 「对比差异」→ 展示可视化 diff
```

### 5.5 新建策略 (Create Strategy)

**触发**：用户点击「新建策略」

```
用户点击「新建策略」
    │
    ▼
输入策略名称: "现代简约"
    │
    ▼
POST /api/git/checkout {
    branchName: "scheme/现代简约",
    createIfNotExist: true
}
    │
    ▼
Canvas 刷新（新分支，继承当前方案数据）
```

**分支命名规范**：
- 策略分支前缀：`scheme/`
- 完整格式：`scheme/{strategyName}`
- 示例：`scheme/极致收纳`、`scheme/动线优先`、`scheme/现代简约`

### 5.6 新建变体 (Create Variant)

**触发**：用户在当前策略下点击「新建变体」

```
用户在 "scheme/现代简约" 分支下点击「新建变体」
    │
    ▼
输入变体名称: "方案A"
    │
    ▼
POST /api/git/checkout {
    branchName: "scheme/现代简约-方案A",
    createIfNotExist: true
}
    │
    ▼
Canvas 刷新（基于当前策略的新分支）
```

**分支命名规范**：
- 变体分支格式：`{currentBranch}-{variantName}`
- 示例：
  - 基于 `scheme/现代简约` 创建变体 `方案A`
  - 结果分支：`scheme/现代简约-方案A`

### 5.7 多开窗口 (Multi-Window)

**触发**：用户点击「新建窗口」

```
用户点击「新建窗口」
    │
    ▼
选择要打开的分支（排除已被其他窗口占用的分支）
    │
    ▼
┌───────────────────────────────────────────────────────────────┐
│  Server 创建虚拟窗口                                           │
├───────────────────────────────────────────────────────────────┤
│  生成窗口 ID: window-{uuid}                                   │
│  创建 Worktree:                                               │
│      git worktree add .worktrees/window-{id} {branch}         │
│  启动新 Agent 线程，工作目录指向 Worktree 路径                 │
└───────────────────────────────────────────────────────────────┘
    │
    ▼
新窗口渲染 Worktree 数据
```

**关闭虚拟窗口**：

```
用户关闭虚拟窗口
    │
    ▼
检查是否有未提交的更改
    │
    ├─► 无更改
    │       │
    │       ▼
    │   直接清理 Worktree
    │       git worktree remove .worktrees/window-{id}
    │
    └─► 有更改
            │
            ▼
        弹窗：「窗口有未保存的更改」
            │
            ├─► 用户选择「合并到主分支」
            │       │
            │       ▼
            │   git add . && git commit -m "WIP: 窗口 {id} 改动"
            │       │
            │       ▼
            │   在主窗口分支中执行:
            │       git merge {worktree-branch}
            │       │
            │       ├─► 合并成功
            │       │       │
            │       │       ▼
            │       │   清理 Worktree
            │       │
            │       └─► 有冲突
            │               │
            │               ▼
            │           展示可视化 diff 解决冲突
            │               │
            │               ▼
            │           解决后清理 Worktree
            │
            ├─► 用户选择「放弃更改」
            │       │
            │       ▼
            │   直接清理 Worktree
            │       git worktree remove --force .worktrees/window-{id}
            │
            └─► 用户选择「取消」
                    │
                    ▼
                关闭弹窗，保持窗口打开
```

**窗口类型对比**：

| 窗口类型 | 创建方式 | 操作目标 | 生命周期 | 分支切换 |
|----------|----------|----------|----------|----------|
| **真窗口** | 首个窗口 | 主项目目录 | 不可关闭 | 可自由切换 |
| **虚拟窗口** | 新建窗口 | Git Worktree | 可关闭 | 不可切换 |

### 5.8 多窗口协作规则

**核心规则**：一个分支只能被一个窗口打开

```
用户点击「新建窗口」
    │
    ▼
显示分支选择列表（排除已被占用的分支）
    │
    ▼
用户选择分支
    │
    ▼
检查分支是否已被占用
    │
    ├─► 未被占用
    │       │
    │       ▼
    │   创建 Worktree + 启动窗口
    │
    └─► 已被占用
            │
            ▼
        提示「该分支已在窗口 X 中打开」
        用户可选择：
            ├─► 切换到该窗口
            └─► 选择其他分支
```

**分支占用状态管理**：

| 状态 | 描述 | 可操作 |
|------|------|--------|
| **空闲** | 无窗口打开此分支 | 任何窗口可打开 |
| **占用** | 有窗口正在使用此分支 | 其他窗口不可打开 |
| **锁定** | AI 正在执行任务（Worktree 存在） | 该窗口不可关闭，其他窗口不可打开 |

---

## 六、MCP 工具定义

### 6.1 工具清单

| 工具名 | 参数 | 对应 Server API | 状态 |
|--------|------|-----------------|------|
| `git_status` | - | `GET /api/git/status` | API 已有 |
| `git_commit` | message | `POST /api/git/commit` | API 已有 |
| `git_checkout` | branchName | `POST /api/git/checkout` | API 已有 |
| `git_branches` | - | `GET /api/git/branches` | API 已有 |
| `git_worktree_create` | name, branch | 需补充 API | 待实现 |
| `git_worktree_remove` | name | 需补充 API | 待实现 |
| `git_worktree_list` | - | 需补充 API | 待实现 |
| `git_merge` | sourceBranch | 需补充 API | 待实现 |

### 6.2 工具定义示例

```python
# mcp/tools/git_tools.py

from ..decorators import mcp_tool
import httpx

SERVER_BASE_URL = "http://localhost:5000"

@mcp_tool()
async def git_status(args: dict) -> dict:
    """获取 Git 工作区状态"""
    async with httpx.AsyncClient() as client:
        response = await client.get(f"{SERVER_BASE_URL}/api/git/status")
        return {
            "content": [{"type": "text", "text": str(response.json())}]
        }

@mcp_tool()
async def git_commit(args: dict) -> dict:
    """提交当前更改"""
    message = args.get("message", f"Agent 自动提交")
    async with httpx.AsyncClient() as client:
        response = await client.post(
            f"{SERVER_BASE_URL}/api/git/commit",
            json={"message": message}
        )
        return {
            "content": [{"type": "text", "text": str(response.json())}]
        }

@mcp_tool()
async def git_checkout(args: dict) -> dict:
    """切换分支"""
    branch_name = args.get("branchName")
    create_if_not_exist = args.get("createIfNotExist", False)
    async with httpx.AsyncClient() as client:
        response = await client.post(
            f"{SERVER_BASE_URL}/api/git/checkout",
            json={
                "branchName": branch_name,
                "createIfNotExist": create_if_not_exist
            }
        )
        return {
            "content": [{"type": "text", "text": str(response.json())}]
        }
```

### 6.3 Worktree 工具参数说明

| API 端点 | 方法 | 请求体 | 响应 |
|----------|------|--------|------|
| `/api/git/worktree` | POST | `{ name, branch }` | `{ path, branch }` |
| `/api/git/worktree/{name}` | DELETE | - | `{ success }` |
| `/api/git/worktrees` | GET | - | `[{ path, branch, commitHash }]` |
| `/api/git/merge` | POST | `{ sourceBranch, message? }` | `{ success, hasConflicts }` |

---

## 七、分支命名规范

### 7.1 命名约定

| 类型 | 前缀 | 示例 | 说明 |
|------|------|------|------|
| 主分支 | - | `main` / `master` | 用户当前接受的状态 |
| 策略分支 | `scheme/` | `scheme/极致收纳` | 设计策略 |
| 变体分支 | - | `scheme/极致收纳-方案A` | 策略下的变体 |
| AI 工作分支 | `feat/ai-` | `feat/ai-storage-极致收纳` | AI 临时工作分支 |

### 7.2 分支层级关系

```
main
├── scheme/极致收纳
│   ├── scheme/极致收纳-方案A
│   └── scheme/极致收纳-方案B
├── scheme/动线优先
│   └── scheme/动线优先-变体1
└── scheme/极简留白
```

### 7.3 Worktree 命名规范

| 类型 | 目录名格式 | 示例 | 用途 |
|------|------------|------|------|
| **虚拟窗口** | `window-{uuid}` | `.worktrees/window-a1b2c3d4` | 用户多窗口并行 |
| **AI 单任务** | `ai-{taskId}` | `.worktrees/ai-solver-001` | 单个 Agent 任务 |
| **AI 策略分叉** | `ai-{taskId}-{strategyName}` | `.worktrees/ai-fork-极致收纳` | 多策略并行生成 |

**命名对应关系**：

```
Worktree 目录                          对应分支
─────────────────────────────────────────────────────────────────
.worktrees/window-a1b2c3d4      →      scheme/极致收纳 (直接检出)
.worktrees/ai-solver-001        →      feat/ai-solver-001 (新建分支)
.worktrees/ai-fork-极致收纳     →      feat/ai-fork-极致收纳 (新建分支)
```

**核心规则**：
- 虚拟窗口 Worktree 直接检出用户选择的分支
- AI 任务 Worktree 必须创建新分支（Git 不允许多个 Worktree 检出同一分支）
- AI 分支以 `feat/ai-` 为前缀，便于识别和清理

---

## 八、错误处理

### 8.1 常见错误及处理

| 错误场景 | 返回状态 | 处理方式 |
|----------|----------|----------|
| 分支不存在 | 404 | 提示用户，或设置 `createIfNotExist: true` |
| 有未提交更改 | 409 | 提示用户选择：保存/放弃/取消 |
| 合并冲突 | 200 + `hasConflicts: true` | 展示可视化 diff，让用户解决 |
| Worktree 已存在 | 400 | 先删除再创建，或使用已有 |
| 项目未加载 | 400 | 提示用户先打开项目 |
| 非 Git 仓库 | 400 | 提示用户初始化仓库 |

### 8.2 Agent 错误恢复

```python
@mcp_tool()
async def git_commit(args: dict) -> dict:
    """提交当前更改（带错误恢复）"""
    try:
        async with httpx.AsyncClient() as client:
            response = await client.post(...)
            if response.status_code == 400:
                # 无更改可提交
                return {
                    "content": [{"type": "text", "text": "没有需要提交的更改"}]
                }
            return {
                "content": [{"type": "text", "text": "提交成功"}]
            }
    except Exception as e:
        return {
            "content": [{"type": "text", "text": f"提交失败: {str(e)}"}],
            "is_error": True
        }
```

---

## 九、相关文档

| 文档 | 路径 | 说明 |
|------|------|------|
| Agent 设计 | `docs/Agent_Design.md` | Agent 工作场景定义（§4 核心工作场景） |
| 并行架构 | `docs/Arch_Parallel_Development.md` | Worktree 技术细节 |
| MCP 框架 | `docs/Arch_MCP_Tools.md` | MCP 工具定义规范 |

---

## 十、版本历史

| 版本 | 日期 | 变更内容 |
|------|------|----------|
| v1.3 | 2026-01-15 | 新增 §2 存档系统章节（自动存档、手动存档）、更新 §3 已有实现清单（移除复杂封装方法） |
| v1.2 | 2026-01-15 | §1.4 明确 Worktree 两种使用场景（并行开发 vs 隔离环境）、新增 §4.8 合并注意事项（Canvas 刷新、清理顺序） |
| v1.1 | 2026-01-14 | 补充核心概念（策略/变体/Worktree）、策略配置结构、任务类型分类、多窗口协作规则、Worktree 命名规范 |
| v1.0 | 2026-01-14 | 初始版本：Agent 工作流、Web 用户操作、MCP 工具定义 |
