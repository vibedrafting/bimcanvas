# Agent Git 工作流架构

> 版本：v1.0
> 更新日期：2026-01-20
> 状态：已定稿
>
> **设计目标**：Agent 只需最少 MCP 工具，复杂逻辑由 Server 处理
> **核心原则**：用户保持控制权，决定是否接受 AI 的修改

---

## 一、概述

### 1.1 设计目标

本架构定义 Agent 执行 Git 操作所需的工具链，遵循以下原则：

| 原则 | 说明 |
|------|------|
| **极简工具** | Agent 只需 2 个 MCP 工具 |
| **Server 内置** | 复杂逻辑（自动存档、获取当前分支）由 Server API 内部处理 |
| **用户控制** | 合并/清理由用户通过 Web UI 决定，Agent 不直接调用 |

### 1.2 Agent 工具清单

| MCP 工具 | 对应 Server API | 职责 |
|----------|----------------|------|
| `ai_job_create` | `POST /api/git/ai-job` | 为 SubAgent 创建隔离工作环境 |
| `ai_job_complete` | `POST /api/git/ai-job/{name}/complete` | 标记完成，通知 Web 端供用户审查 |

### 1.3 Agent 不负责的操作

| 操作 | 处理方式 | 说明 |
|------|----------|------|
| 获取当前分支 | Server API 内部 | `ai_job_create` 不传 baseBranch 时自动获取 |
| 自动存档 | Server API 内部 | 创建 Worktree 前自动执行 |
| git commit | 合并 API 内部 | 用户点击合并时自动执行 |
| 合并 (`merge`) | 用户通过 Web UI | Agent 生成总结后，用户决定是否合并 |
| 清理 (`worktrees DELETE`) | 用户通过 Web UI | 合并后或丢弃时清理 |

---

## 二、MCP 工具定义

### 2.1 ai_job_create - 创建 AI Job

为 SubAgent 创建隔离工作环境。

**Server API**：`POST /api/git/ai-job`

**请求参数**：

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `name` | string | ✅ | AI Job 名称，如 "layout-job-1" |
| `baseBranch` | string | ❌ | 基准分支，不传则 Server 自动使用当前分支 |

**返回值**：

```json
{
    "worktreePath": "E:/project/.worktrees/layout-job-1",
    "branchName": "feat/layout-job-1-20260120-143052"
}
```

**Server 内部逻辑**：

1. `baseBranch` 为空 → 调用 `GET /api/git/current` 获取当前分支
2. 检测未提交更改 → 自动存档（`git add . && git commit`）
3. 创建临时分支 + Worktree

**MCP 工具定义（Python）**：

```python
@mcp_tool()
async def ai_job_create(name: str, base_branch: str = None) -> dict:
    """
    为 SubAgent 创建隔离工作环境

    Args:
        name: AI Job 名称，如 "layout-job-1"
        base_branch: 基准分支（可选，不传则 Server 自动使用当前分支）

    Returns:
        {
            "worktreePath": "E:/project/.worktrees/layout-job-1",
            "branchName": "feat/layout-job-1-20260120-143052"
        }
    """
    # POST /api/git/ai-job { name, baseBranch? }
```

### 2.2 ai_job_complete - 标记 AI Job 完成

标记 AI Job 完成，通知 Web 端供用户审查。

**Server API**：`POST /api/git/ai-job/{name}/complete`

**请求参数**：

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `summary` | string | ✅ | 修改总结（展示给用户） |

**返回值**：

```json
{
    "success": true,
    "message": "AI Job 已完成，等待用户审查"
}
```

**Server 内部逻辑**：

1. 在 Worktree 中执行 `git add . && git commit`（提交未暂存的修改）
2. 通知 Web 端该 AI Job 已完成
3. Web 端展示 diff/合并按钮
4. 用户点击合并 → 调用合并 API

**MCP 工具定义（Python）**：

```python
@mcp_tool()
async def ai_job_complete(name: str, summary: str) -> dict:
    """
    标记 AI Job 完成，通知 Web 端供用户审查

    Args:
        name: AI Job 名称（同 ai_job_create 返回的 name）
        summary: 修改总结（展示给用户）

    Returns:
        {
            "success": true,
            "message": "AI Job 已完成，等待用户审查"
        }
    """
    # POST /api/git/ai-job/{name}/complete { summary }
```

---

## 三、业务场景工作流

### 3.1 Query 任务（只读）

**无需 Git 操作**，Agent 直接读取当前工作目录的文件。

### 3.2 Execute 任务 - 真窗口（场景 R）

```
┌─────────────────────────────────────────────────────────────────┐
│  【R1: 创建 AI Job】主控 Agent 调用                              │
├─────────────────────────────────────────────────────────────────┤
│  MCP: ai_job_create(name="layout-job-1")                        │
│  → { worktreePath, branchName }                                 │
│  → Server 内部自动：                                             │
│      1. baseBranch 为空时获取当前分支                            │
│      2. 检测未提交更改 → 自动存档                                │
│      3. 创建临时分支 → 创建 Worktree                             │
│                                                                 │
│  【R2: SubAgent 工作】                                           │
├─────────────────────────────────────────────────────────────────┤
│  SubAgent 在 worktreePath 中修改文件                             │
│                                                                 │
│  【R3: 标记完成】主控 Agent 调用                                 │
├─────────────────────────────────────────────────────────────────┤
│  MCP: ai_job_complete(                                          │
│    name="layout-job-1",                                         │
│    summary="已完成主卧床的布置，放置在北墙居中位置。"             │
│  )                                                              │
│  → Server 内部：                                                 │
│      1. 在 Worktree 中执行 git commit                           │
│      2. 通知 Web 端                                              │
│                                                                 │
│  ★ Agent 对话结束，不调用 merge/cleanup                         │
│                                                                 │
│  【R4: 用户审查】用户通过 Web UI                                 │
├─────────────────────────────────────────────────────────────────┤
│  Web 端展示 diff/合并按钮                                        │
│  用户点击【合并】→ POST /api/git/merge                          │
│  用户点击【丢弃】→ DELETE /api/git/worktrees/{name}             │
└─────────────────────────────────────────────────────────────────┘
```

### 3.3 Execute 任务 - 虚拟窗口（场景 V）

```
┌─────────────────────────────────────────────────────────────────┐
│  【V1: 创建虚拟窗口】Web 用户行为（非 Agent）                    │
├─────────────────────────────────────────────────────────────────┤
│  POST /api/git/worktrees                                        │
│  {                                                              │
│    "name": "virtual-window",                                    │
│    "branch": "test/virtual-window"                              │
│  }                                                              │
│                                                                 │
│  【V2: 创建 AI Job】主控 Agent 调用                              │
├─────────────────────────────────────────────────────────────────┤
│  MCP: ai_job_create(                                            │
│    name="ai-job-v",                                             │
│    base_branch="{虚拟窗口的分支}"  ← 注意：基于虚拟窗口分支     │
│  )                                                              │
│  → 返回: { worktreePath, branchName }                           │
│                                                                 │
│  【V3: SubAgent 工作】                                           │
├─────────────────────────────────────────────────────────────────┤
│  SubAgent 在 worktreePath 中修改文件                             │
│                                                                 │
│  【V4: 标记完成】主控 Agent 调用                                 │
├─────────────────────────────────────────────────────────────────┤
│  MCP: ai_job_complete(name="ai-job-v", summary="...")           │
│                                                                 │
│  【V5: 用户审查】★ 关键区别 ★                                   │
├─────────────────────────────────────────────────────────────────┤
│  POST /api/git/merge                                            │
│  {                                                              │
│    "sourceBranch": "{branchName}",                              │
│    "worktreeName": "virtual-window"  ← 在虚拟窗口中执行合并！   │
│  }                                                              │
│                                                                 │
│  【V6: 清理】用户手动触发                                        │
├─────────────────────────────────────────────────────────────────┤
│  DELETE /api/git/worktrees/ai-job-v?deleteBranch=true           │
└─────────────────────────────────────────────────────────────────┘
```

### 3.4 真窗口 vs 虚拟窗口 关键区别

| 步骤 | 真窗口 | 虚拟窗口 |
|------|--------|----------|
| baseBranch | 主仓库当前分支 | 虚拟窗口绑定的分支 |
| 合并参数 | `targetBranch` | `worktreeName` |
| 合并位置 | 主仓库目录 | 虚拟窗口 Worktree 目录 |

**Agent 如何知道自己在真窗口还是虚拟窗口？**

**答案**：Agent **不需要知道**。
- `git_current` 返回的就是"当前窗口"的分支
- Server 启动 Agent 时已经设置好工作目录上下文
- 合并操作由用户通过 Web UI 触发，Web 知道窗口类型

### 3.5 并行生成多方案

```
用户："给我出3个方案：极致收纳、动线优先、极简留白"
    │
    ▼
主控 Agent 循环调用:
    │
    ├─► ai_job_create("storage", baseBranch) → worktreePath1
    ├─► ai_job_create("flow", baseBranch)    → worktreePath2
    └─► ai_job_create("minimal", baseBranch) → worktreePath3
    │
    ▼
并行派发 3 个 SubAgent:
    │
    ├─► SubAgent(prompt="..., 工作目录: {worktreePath1}")
    ├─► SubAgent(prompt="..., 工作目录: {worktreePath2}")
    └─► SubAgent(prompt="..., 工作目录: {worktreePath3}")
    │
    ▼
各自完成后调用 ai_job_complete(name, summary)
    │
    ▼
Web 端三联屏展示，用户选择方案后合并
```

---

## 四、Server 内部处理

### 4.1 自动存档逻辑

在 `CreateAiJob` API 中实现：

```csharp
// 自动存档：创建前检测到未提交更改，静默执行存档
if (_gitService.HasUncommittedChanges(projectPath))
{
    _gitService.Commit(projectPath, $"自动存档_{DateTime.Now:yyyyMMdd_HHmmss}");
    _logger.LogInformation("创建 AI Job 前自动存档");
}
```

### 4.2 获取当前分支逻辑

在 `CreateAiJob` API 中实现：

```csharp
// baseBranch 为空时自动获取当前分支
if (string.IsNullOrEmpty(request.BaseBranch))
{
    request.BaseBranch = _gitService.GetCurrentBranch(projectPath);
    _logger.LogInformation("自动使用当前分支作为基准: {Branch}", request.BaseBranch);
}
```

### 4.3 Complete API 内置 git commit

在 `CompleteAiJob` API 中实现：

```csharp
// 1. 在 Worktree 中提交未暂存的修改
var worktreePath = Path.Combine(_gitService.GetWorktreesDir(projectPath), name);
_gitService.TryCommit(worktreePath, $"AI Job 完成: {summary}");

// 2. 通知 Web 端
await _hubContext.Clients.All.SendAsync("AiJobCompleted", new
{
    Name = name,
    Summary = summary,
    BranchName = branchName
});
```

---

## 五、已有 Server REST API

> 来源：`GitController.cs` 已实现

| 端点 | 方法 | 功能 | Agent 需要 |
|------|------|------|-----------|
| `api/git/status` | GET | 获取工作区状态 | ⚪ 可选 |
| `api/git/current` | GET | 获取当前分支 | ⚪ 可选（ai_job_create 内部调用） |
| `api/git/branches` | GET | 获取所有分支列表 | ⚪ 可选 |
| `api/git/worktrees` | GET | 获取 Worktree 列表 | ⚪ 可选 |
| `api/git/ai-job` | POST | 创建 AI Job | ✅ **必需** |
| `api/git/ai-job/{name}/complete` | POST | 标记 AI Job 完成 | ✅ **必需** |
| `api/git/commit` | POST | 提交更改 | ❌ Agent 不调用 |
| `api/git/merge` | POST | 合并分支 | ❌ Agent 不调用 |
| `api/git/worktrees` | POST | 创建 Worktree | ❌ Agent 不调用 |
| `api/git/worktrees/{name}` | DELETE | 删除 Worktree | ❌ Agent 不调用 |
| `api/git/checkout` | POST | 切换分支 | ❌ Agent 不调用 |
| `api/git/discard` | POST | 放弃更改 | ❌ Agent 不调用 |

---

## 六、用户操作（Web UI）

### 6.1 合并流程

用户点击【合并】按钮后：

```
POST /api/git/merge
{
  "sourceBranch": "feat/layout-job-1-20260120-xxx",
  "targetBranch": "master"           // 真窗口
  // 或 "worktreeName": "window-2"   // 虚拟窗口
}
    │
    ▼
DELETE /api/git/worktrees/layout-job-1?deleteBranch=true
    │
    ▼
Canvas 刷新，显示合并后的结果
```

### 6.2 丢弃流程

用户点击【丢弃】按钮后：

```
DELETE /api/git/worktrees/layout-job-1?deleteBranch=true
    │
    ▼
AI Job 的 Worktree 和临时分支被删除
```

---

## 七、总结

**Agent 只需 2 个 MCP 工具**：

| 工具 | 用途 | 调用时机 |
|------|------|----------|
| `ai_job_create` | 创建隔离环境 | execute 任务开始时 |
| `ai_job_complete` | 标记完成，通知 Web 端供用户审查 | SubAgent 完成后 |

**设计优势**：

| 优势 | 说明 |
|------|------|
| **Agent 逻辑简单** | 只关注"创建环境 → 执行任务 → 通知完成" |
| **用户保持控制权** | 决定是否接受 AI 的修改 |
| **Server 处理复杂性** | Agent 无需关心 Git 操作细节 |

---

## 版本历史

| 版本 | 日期 | 变更内容 |
|------|------|----------|
| v1.0 | 2026-01-20 | 初版：定义 Agent Git 工具体系 |
