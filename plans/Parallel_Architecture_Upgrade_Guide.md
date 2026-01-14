# BIMCanvas 并行架构升级指南

> **版本**：v1.2 | **更新日期**：2026-01-14
> **状态**：待实施
> **关联文档**：[Arch_Parallel_Development.md](../docs/Arch_Parallel_Development.md)

---

## 一、升级概述

### 1.1 目标

将现有 BIMCanvas 系统升级为支持：
- 多窗口并行对话
- Git 分支隔离的多策略管理
- SubAgent 按分区并行布置
- 可视化 Merge UI

### 1.2 升级阶段

| 阶段 | 名称 | 核心内容 | 依赖 |
|------|------|----------|------|
| **Phase 1** | 目录结构升级 | schemes/ 分区子目录 | 无 |
| **Phase 2** | Server Git 服务 | Worktree 管理、分支操作 | Phase 1 |
| **Phase 3** | Web 多窗口 | 真窗口/虚拟窗口、窗口管理 | Phase 2 |
| **Phase 4** | Agent 并行 | SubAgent 分区布置、Worktree 隔离 | Phase 2 |
| **Phase 5** | 可视化 Merge | 评审模式、分区级 Diff | Phase 3, 4 |

---

## 二、Phase 1：目录结构升级

### 2.1 目标

将 `schemes/modules.json` 改为按分区子目录存储。

### 2.2 影响范围

| 组件 | 影响 |
|------|------|
| **Schema.md** | 更新目录结构说明 |
| **BIMCanvas.Core** | Module 模型可能需要调整 |
| **BIMCanvas.Server** | 文件读写逻辑 |
| **BIMCanvas.Web** | 数据加载逻辑 |

### 2.3 实施步骤

#### Step 1.1：更新文档

修改以下文档中的目录结构描述：
- `docs/Schema.md` §1.3, §2, §6.4
- `docs/Architecture.md` §2.3, §2.4

#### Step 1.2：Server 文件服务升级

**文件**：`BIMCanvas.Server/Services/ProjectContext.cs`

```csharp
// TODO: 实现分区 modules 读取
public class ProjectContext
{
    /// <summary>
    /// 递归读取所有分区的 modules.json
    /// </summary>
    /// <returns>分区ID -> Module列表 的字典</returns>
    public async Task<Dictionary<string, List<Module>>> LoadAllModulesAsync()
    {
        var result = new Dictionary<string, List<Module>>();
        var schemesPath = Path.Combine(_projectPath, "schemes");

        await LoadModulesRecursiveAsync(schemesPath, "", result);
        return result;
    }

    private async Task LoadModulesRecursiveAsync(
        string basePath,
        string zoneId,
        Dictionary<string, List<Module>> result)
    {
        // 递归遍历所有 rz_* 和 dz_* 目录
        // 读取每个目录下的 modules.json
        throw new NotImplementedException("待实现");
    }
}
```

#### Step 1.3：Web 数据加载升级

**文件**：`BIMCanvas.Web/src/stores/canvasStore.ts`

```typescript
// TODO: 实现分区按需加载
interface ZoneModules {
  zoneId: string;
  modules: Module[];
  loaded: boolean;
}

// 懒加载分区 modules
async function loadZoneModules(zoneId: string): Promise<Module[]> {
  // 调用 API 按需加载
  throw new Error('待实现');
}
```

#### Step 1.4：迁移脚本

**文件**：`scripts/migrate_modules_to_zones.ps1`

```powershell
# 迁移脚本：将单一 modules.json 拆分到分区目录
param(
    [Parameter(Mandatory=$true)]
    [string]$ProjectPath
)

$schemesPath = Join-Path $ProjectPath "schemes"
$modulesPath = Join-Path $schemesPath "modules.json"
$zonesPath = Join-Path $schemesPath "zones.json"

# 1. 读取现有 modules.json
if (-not (Test-Path $modulesPath)) {
    Write-Host "modules.json 不存在，跳过迁移"
    exit 0
}

$modulesContent = Get-Content $modulesPath -Raw | ConvertFrom-Json
$zonesContent = Get-Content $zonesPath -Raw | ConvertFrom-Json

# 2. 按 zoneId 分组
$modulesByZone = @{}
foreach ($module in $modulesContent.modules) {
    $zoneId = $module.zoneId
    if (-not $modulesByZone.ContainsKey($zoneId)) {
        $modulesByZone[$zoneId] = @()
    }
    $modulesByZone[$zoneId] += $module
}

# 3. 创建分区目录并写入
foreach ($zoneId in $modulesByZone.Keys) {
    $zonePath = Join-Path $schemesPath $zoneId
    New-Item -ItemType Directory -Force -Path $zonePath | Out-Null

    $zoneModulesPath = Join-Path $zonePath "modules.json"
    $zoneModules = @{
        modules = $modulesByZone[$zoneId]
    }
    $zoneModules | ConvertTo-Json -Depth 10 | Set-Content $zoneModulesPath -Encoding UTF8

    Write-Host "已创建: $zoneModulesPath"
}

# 4. 备份并删除原 modules.json
$backupPath = Join-Path $schemesPath "modules.json.bak"
Move-Item $modulesPath $backupPath
Write-Host "原文件已备份至: $backupPath"

Write-Host "迁移完成！"
```

### 2.4 验证清单

- [ ] 现有项目可正常打开
- [ ] 新建项目自动创建分区目录
- [ ] modules 数据按分区正确读写
- [ ] Web 端正常渲染所有分区的 modules

---

## 三、Phase 2：Server Git 服务

### 3.1 目标

实现 Server 端的 Git 操作能力：
- Worktree 创建/删除
- 分支创建/切换/合并
- 分支锁管理

### 3.2 ⚠️ Git 核心限制（重要）

> **Git 不允许两个 Worktree 检出同一个分支**

这意味着：
- 如果 Worktree-A 检出了 `branch-A`
- 则任何其他 Worktree **不能**再检出 `branch-A`
- Agent 必须创建新分支（如 `branch-A-agent-job-1`）

**关键技术发现**：合并操作是在分支上进行的，不是在 Worktree 之间进行的。

可以直接在用户 Worktree 中执行合并：
```bash
cd /path/to/worktree-A
git merge branch-A-agent-job-1
# ✅ Worktree-A 的文件自动更新为合并结果
# ✅ 如有冲突，冲突标记直接出现在 Worktree-A 的文件中
```

**架构简化**：
- Canvas 始终渲染用户 Worktree，无需临时切换渲染目标
- 合并后 Worktree 文件自动更新，无需额外"传递"操作

### 3.3 ⚠️ 架构变更：Git 工具集成到 Agent 进程

> **用户决策（2026-01-14）**：Agent 项目已支持 MCP 功能，Git 工具直接集成到 Agent 进程，不需要独立的 Server 端服务。

**架构对比**：

| 原设计 | 新设计 |
|--------|--------|
| Server 端 GitService | Agent 进程内 Git 工具 |
| Agent 调用 Server REST API | Agent 直接调用 subprocess |
| 需要进程间通信 | 进程内调用，零延迟 |

**新架构**：

```
Agent (Python)
    │
    ├─► Git 工具（进程内）
    │   ├─► worktree/create
    │   ├─► worktree/remove
    │   ├─► branch/merge
    │   └─► commit/commit
    │
    └─► subprocess 调用 Git CLI
```

**Server 端保留**：

```
BIMCanvas.Server/
└── Services/
    └── Git/
        └── BranchLockManager.cs        # 分支锁管理（多窗口互斥）
```

> **注意**：Server 端仅保留 BranchLockManager 用于多窗口分支互斥，Git 操作逻辑迁移到 Agent 进程内。

### 3.4 接口定义

**文件**：`BIMCanvas.Server/Services/Git/IGitService.cs`

```csharp
public interface IGitService
{
    // 分支操作
    Task<string> GetCurrentBranchAsync();
    Task<IEnumerable<string>> ListBranchesAsync();
    Task CreateBranchAsync(string branchName, string? baseBranch = null);
    Task CheckoutBranchAsync(string branchName);
    Task DeleteBranchAsync(string branchName);

    // 提交操作
    Task StageAllAsync();
    Task CommitAsync(string message);

    // 合并操作（关键：在用户 Worktree 中直接执行）
    // ⚠️ 注意：合并是分支级操作，执行后 Worktree 文件自动更新
    Task<MergeResult> MergeAsync(string sourceBranch);
    Task<bool> HasConflictsAsync();
}

public interface IWorktreeService
{
    Task<string> CreateWorktreeAsync(string name, string branch);
    Task RemoveWorktreeAsync(string name);
    Task<IEnumerable<WorktreeInfo>> ListWorktreesAsync();
    Task<bool> ExistsAsync(string name);
}

public class BranchLockManager
{
    // 分支 -> 窗口ID 的映射
    private readonly ConcurrentDictionary<string, string> _locks = new();

    public bool TryAcquire(string branch, string windowId);
    public void Release(string branch, string windowId);
    public string? GetOwner(string branch);
    public IEnumerable<string> GetLockedBranches();
}
```

### 3.5 实现方案

**方案选择**：使用 `LibGit2Sharp` 库实现 Git 操作。

```powershell
# 安装依赖
dotnet add BIMCanvas.Server package LibGit2Sharp
```

**文件**：`BIMCanvas.Server/Services/Git/GitService.cs`

```csharp
using LibGit2Sharp;

public class GitService : IGitService
{
    private readonly string _repoPath;
    private readonly ILogger<GitService> _logger;

    public GitService(string repoPath, ILogger<GitService> logger)
    {
        _repoPath = repoPath;
        _logger = logger;
    }

    public async Task<string> GetCurrentBranchAsync()
    {
        // TODO: 实现
        // using var repo = new Repository(_repoPath);
        // return repo.Head.FriendlyName;
        throw new NotImplementedException();
    }

    // ... 其他方法实现
}
```

### 3.6 Worktree 命令封装

> **注意**：LibGit2Sharp 对 Worktree 支持有限，可能需要直接调用 Git CLI。

**文件**：`BIMCanvas.Server/Services/Git/WorktreeService.cs`

```csharp
public class WorktreeService : IWorktreeService
{
    private readonly string _repoPath;
    private readonly ILogger<WorktreeService> _logger;

    public async Task<string> CreateWorktreeAsync(string name, string branch)
    {
        var worktreePath = Path.Combine(_repoPath, ".worktrees", name);

        // 使用 Git CLI
        var result = await RunGitCommandAsync(
            $"worktree add \"{worktreePath}\" {branch}"
        );

        if (result.ExitCode != 0)
        {
            throw new GitOperationException($"Failed to create worktree: {result.Error}");
        }

        return worktreePath;
    }

    public async Task RemoveWorktreeAsync(string name)
    {
        var worktreePath = Path.Combine(_repoPath, ".worktrees", name);

        // 使用 Git CLI
        await RunGitCommandAsync($"worktree remove \"{worktreePath}\"");
    }

    private async Task<GitCommandResult> RunGitCommandAsync(string arguments)
    {
        // TODO: 实现 Git CLI 调用
        // 使用 System.Diagnostics.Process 执行 git 命令
        throw new NotImplementedException();
    }
}
```

### 3.7 Git CLI 封装脚本（备选方案）

如果 LibGit2Sharp 不满足需求，可以直接封装 Git CLI：

**文件**：`scripts/git_operations.ps1`

```powershell
# Git 操作封装脚本
# 供 Server 通过 Process 调用

param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("worktree-add", "worktree-remove", "worktree-list",
                 "branch-create", "branch-checkout", "branch-list",
                 "commit", "merge")]
    [string]$Operation,

    [Parameter(Mandatory=$true)]
    [string]$RepoPath,

    [Parameter()]
    [string]$Arg1,

    [Parameter()]
    [string]$Arg2
)

Set-Location $RepoPath

switch ($Operation) {
    "worktree-add" {
        # $Arg1 = worktree名称, $Arg2 = 分支名
        $wtPath = Join-Path $RepoPath ".worktrees" $Arg1
        git worktree add $wtPath $Arg2
        if ($LASTEXITCODE -eq 0) {
            Write-Output $wtPath
        } else {
            exit 1
        }
    }

    "worktree-remove" {
        # $Arg1 = worktree名称
        $wtPath = Join-Path $RepoPath ".worktrees" $Arg1
        git worktree remove $wtPath --force
    }

    "worktree-list" {
        git worktree list --porcelain
    }

    "branch-create" {
        # $Arg1 = 分支名, $Arg2 = 基础分支(可选)
        if ($Arg2) {
            git checkout -b $Arg1 $Arg2
        } else {
            git checkout -b $Arg1
        }
    }

    "branch-checkout" {
        # $Arg1 = 分支名
        git checkout $Arg1
    }

    "branch-list" {
        git branch --list --format="%(refname:short)"
    }

    "commit" {
        # $Arg1 = 提交信息
        git add .
        git commit -m $Arg1
    }

    "merge" {
        # $Arg1 = 源分支
        git merge $Arg1 --no-ff
    }
}
```

### 3.8 验证清单

- [ ] 可创建/删除 Worktree
- [ ] 可创建/切换/删除分支
- [ ] 分支锁正常工作（互斥）
- [ ] Git 操作错误正确捕获和报告
- [ ] 合并操作可在用户 Worktree 中直接执行

---

## 四、Phase 3：Web 多窗口

### 4.1 目标

实现 Web 端多窗口对话系统：
- 真窗口（主窗口）
- 虚拟窗口（通过 Worktree）
- 窗口激活/切换机制

### 4.2 影响范围

| 组件 | 影响 |
|------|------|
| **canvasStore** | 窗口状态管理 |
| **gitStore** | 分支状态、锁状态 |
| **AICommandCenter** | 多窗口实例管理 |
| **路由** | 窗口切换逻辑 |

### 4.3 状态管理设计

**文件**：`BIMCanvas.Web/src/stores/windowStore.ts`

```typescript
// TODO: 新建窗口状态管理 Store
import { defineStore } from 'pinia';

interface WindowState {
  id: string;
  type: 'primary' | 'virtual';
  branchName: string;
  worktreePath?: string;  // 虚拟窗口才有
  agentThreadId?: string;
  isActive: boolean;
  createdAt: Date;
}

export const useWindowStore = defineStore('window', {
  state: () => ({
    windows: [] as WindowState[],
    activeWindowId: null as string | null,
  }),

  getters: {
    primaryWindow: (state) => state.windows.find(w => w.type === 'primary'),
    virtualWindows: (state) => state.windows.filter(w => w.type === 'virtual'),
    activeWindow: (state) => state.windows.find(w => w.id === state.activeWindowId),
    occupiedBranches: (state) => state.windows.map(w => w.branchName),
  },

  actions: {
    // 创建虚拟窗口
    async createVirtualWindow(branchName: string): Promise<string> {
      // 1. 调用 Server API 创建 Worktree
      // 2. 启动 Agent 线程
      // 3. 添加窗口状态
      throw new Error('待实现');
    },

    // 关闭虚拟窗口
    async closeVirtualWindow(windowId: string, merge: boolean): Promise<void> {
      // 1. 如果 merge，执行合并
      // 2. 调用 Server API 删除 Worktree
      // 3. 停止 Agent 线程
      // 4. 移除窗口状态
      throw new Error('待实现');
    },

    // 激活窗口
    activateWindow(windowId: string): void {
      // 更新 activeWindowId
      // 通知 Canvas 切换渲染数据
      throw new Error('待实现');
    },

    // 真窗口切换分支
    async switchBranch(branchName: string): Promise<void> {
      // 仅真窗口可用
      // 检查分支是否被占用
      throw new Error('待实现');
    },
  },
});
```

### 4.4 组件升级

**文件**：`BIMCanvas.Web/src/components/chat/WindowTabs.vue`

```vue
<!-- TODO: 新建窗口标签页组件 -->
<template>
  <div class="window-tabs">
    <div
      v-for="window in windows"
      :key="window.id"
      :class="['tab', { active: window.isActive }]"
      @click="activateWindow(window.id)"
    >
      <span class="branch-name">{{ window.branchName }}</span>
      <span v-if="window.type === 'virtual'" class="badge">虚拟</span>
      <button
        v-if="window.type === 'virtual'"
        class="close-btn"
        @click.stop="closeWindow(window.id)"
      >×</button>
    </div>
    <button class="add-btn" @click="createWindow">+</button>
  </div>
</template>

<script setup lang="ts">
// TODO: 实现窗口标签页逻辑
</script>
```

### 4.5 API 接口

**Server 端新增 API**：

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/api/windows` | 创建虚拟窗口 |
| DELETE | `/api/windows/{id}` | 关闭虚拟窗口 |
| GET | `/api/windows` | 获取所有窗口 |
| POST | `/api/windows/{id}/activate` | 激活窗口 |
| POST | `/api/windows/primary/switch-branch` | 真窗口切换分支 |

### 4.6 验证清单

- [ ] 可创建虚拟窗口
- [ ] 可关闭虚拟窗口（合并/丢弃）
- [ ] 窗口切换正常
- [ ] 分支互斥锁正常工作
- [ ] 真窗口可切换分支（排除已占用）

---

## 五、Phase 4：Agent 并行

### 5.1 目标

实现 Agent 的并行布置能力：
- SubAgent 按分区并行
- Worktree 隔离编辑
- 任务状态跟踪

### 5.2 SubAgent 分区分配

**设计原则**：
- 每个 SubAgent 负责一个或多个分区
- 分区之间无依赖时可并行
- 有依赖时串行执行

```
MainAgent
    │
    ├─► SubAgent-1 (rz_1, rz_2)  ──► Worktree-1
    │
    ├─► SubAgent-2 (rz_3)        ──► Worktree-2
    │
    └─► SubAgent-3 (rz_6)        ──► Worktree-3
```

### 5.3 Agent 工作流程（修正版）

> **关键变更**：Agent 不能直接检出用户的分支，必须创建新分支。合并在用户 Worktree 中直接执行。

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                       Agent 并行布置完整流程                                  │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  【前提】                                                                    │
│  • 用户在窗口 A → Worktree-A（检出 branch-A）                                │
│  • Canvas 渲染 Worktree-A                                                   │
│                                                                             │
│  【Step 1: 保存用户当前改动】                                                │
│  cd Worktree-A                                                              │
│  git add . && git commit -m "WIP: 用户改动"                                 │
│                                                                             │
│  【Step 2: 为 SubAgent 创建独立分支 + Worktree】← 关键！                      │
│  # 基于 branch-A 创建新分支（不是检出 branch-A）                              │
│  git branch branch-A-agent-sub1 branch-A                                    │
│  git branch branch-A-agent-sub2 branch-A                                    │
│  git branch branch-A-agent-sub3 branch-A                                    │
│                                                                             │
│  # 创建对应 Worktree（平级目录）                                              │
│  git worktree add .worktrees/agent-sub1 branch-A-agent-sub1                 │
│  git worktree add .worktrees/agent-sub2 branch-A-agent-sub2                 │
│  git worktree add .worktrees/agent-sub3 branch-A-agent-sub3                 │
│                                                                             │
│  【Step 3: SubAgent 并行工作】                                               │
│  ├─► SubAgent-1: 在 .worktrees/agent-sub1 中布置 rz_1, rz_2                 │
│  ├─► SubAgent-2: 在 .worktrees/agent-sub2 中布置 rz_3                       │
│  └─► SubAgent-3: 在 .worktrees/agent-sub3 中布置 rz_6                       │
│                                                                             │
│  【Step 4: 各 SubAgent 提交】                                                │
│  cd .worktrees/agent-sub1 && git add . && git commit -m "feat: rz_1,rz_2"  │
│  cd .worktrees/agent-sub2 && git add . && git commit -m "feat: rz_3"       │
│  cd .worktrees/agent-sub3 && git add . && git commit -m "feat: rz_6"       │
│                                                                             │
│  【Step 5: 在用户 Worktree 中合并】← 关键！直接合并，无需绕道                  │
│  cd Worktree-A                                                              │
│  git merge branch-A-agent-sub1    # Worktree-A 文件自动更新                  │
│  git merge branch-A-agent-sub2    # 如有冲突，标记出现在 Worktree-A          │
│  git merge branch-A-agent-sub3                                              │
│                                                                             │
│  【Step 6: 可视化解决冲突（如有）】                                           │
│  # Canvas 始终渲染 Worktree-A，用户看到带冲突标记的文件                       │
│  # 用户通过 UI 选择保留哪些改动                                               │
│  # 解决后提交：                                                              │
│  git add . && git commit -m "merge: 合并 AI 方案"                           │
│                                                                             │
│  【Step 7: 清理】                                                            │
│  git worktree remove .worktrees/agent-sub1                                  │
│  git worktree remove .worktrees/agent-sub2                                  │
│  git worktree remove .worktrees/agent-sub3                                  │
│  git branch -d branch-A-agent-sub1                                          │
│  git branch -d branch-A-agent-sub2                                          │
│  git branch -d branch-A-agent-sub3                                          │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

**与旧设计的差异**：

| 旧设计 | 修正后 |
|--------|--------|
| SubAgent 检出用户分支 | SubAgent 创建独立分支 |
| 合并需要绕道到"本地分支" | 直接在用户 Worktree 中合并 |
| 需要临时切换 Canvas 渲染目标 | Canvas 始终渲染用户 Worktree |
| 合并后需要"传递"给用户 Worktree | 合并自动更新用户 Worktree 文件 |

### 5.4 Agent 端实现（预留）

**文件**：`BIMCanvas.Agent/main_agent.py`

```python
# TODO: 实现 SubAgent 并行布置
class MainAgent:
    async def parallel_layout(self, zones: list[str], strategy: dict):
        """
        并行布置多个分区

        Args:
            zones: 分区ID列表
            strategy: 策略配置
        """
        # 1. 分配分区给 SubAgent
        assignments = self._assign_zones_to_subagents(zones)

        # 2. 为每个 SubAgent 创建 Worktree
        worktrees = await self._create_worktrees(assignments)

        # 3. 并行执行
        tasks = []
        for subagent_id, (zone_ids, worktree_path) in worktrees.items():
            task = self._run_subagent(subagent_id, zone_ids, worktree_path, strategy)
            tasks.append(task)

        results = await asyncio.gather(*tasks)

        # 4. 合并结果
        await self._merge_results(worktrees)

        # 5. 清理
        await self._cleanup_worktrees(worktrees)

        return results

    def _assign_zones_to_subagents(self, zones: list[str]) -> dict:
        """分配分区到 SubAgent，考虑依赖关系"""
        raise NotImplementedError()

    async def _create_worktrees(self, assignments: dict) -> dict:
        """为每个 SubAgent 创建 Worktree"""
        raise NotImplementedError()

    async def _run_subagent(self, ...):
        """执行单个 SubAgent"""
        raise NotImplementedError()

    async def _merge_results(self, worktrees: dict):
        """合并所有 SubAgent 的结果"""
        raise NotImplementedError()

    async def _cleanup_worktrees(self, worktrees: dict):
        """清理 Worktree"""
        raise NotImplementedError()
```

### 5.5 验证清单

- [ ] SubAgent 可并行执行
- [ ] 每个 SubAgent 在独立 Worktree 中工作
- [ ] 结果正确合并
- [ ] Worktree 正确清理

---

## 六、Phase 5：可视化 Merge

### 6.1 目标

实现分区级别的可视化差异对比和选择性合并。

### 6.2 UI 设计

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           评审模式                                           │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌─────────────────────────────┐   ┌─────────────────────────────┐         │
│  │        我的方案              │   │        AI 方案               │         │
│  │                             │   │                             │         │
│  │  ┌───────────────────────┐  │   │  ┌───────────────────────┐  │         │
│  │  │     Canvas 渲染       │  │   │  │     Canvas 渲染       │  │         │
│  │  │                       │  │   │  │                       │  │         │
│  │  │                       │  │   │  │                       │  │         │
│  │  └───────────────────────┘  │   │  └───────────────────────┘  │         │
│  └─────────────────────────────┘   └─────────────────────────────┘         │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │  分区选择                                                                ││
│  │  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐      ││
│  │  │ rz_1     │ │ rz_2     │ │ rz_3     │ │ rz_6_1   │ │ rz_6_2   │      ││
│  │  │ [我的]   │ │ [AI]     │ │ [我的]   │ │ [AI]     │ │ [无变化] │      ││
│  │  └──────────┘ └──────────┘ └──────────┘ └──────────┘ └──────────┘      ││
│  └─────────────────────────────────────────────────────────────────────────┘│
│                                                                             │
│  ┌─────────────────────┐  ┌─────────────────────┐                          │
│  │      确认合并        │  │      丢弃全部        │                          │
│  └─────────────────────┘  └─────────────────────┘                          │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 6.3 组件设计

**文件**：`BIMCanvas.Web/src/components/merge/MergeReviewPanel.vue`

```vue
<!-- TODO: 新建合并评审面板 -->
<template>
  <div class="merge-review-panel">
    <!-- 双栏对比 -->
    <div class="compare-view">
      <div class="my-version">
        <h3>我的方案</h3>
        <CanvasRenderer :data="myData" />
      </div>
      <div class="ai-version">
        <h3>AI 方案</h3>
        <CanvasRenderer :data="aiData" />
      </div>
    </div>

    <!-- 分区选择器 -->
    <div class="zone-selector">
      <ZoneSelectCard
        v-for="zone in zones"
        :key="zone.id"
        :zone="zone"
        :has-changes="zone.hasChanges"
        v-model="selections[zone.id]"
      />
    </div>

    <!-- 操作按钮 -->
    <div class="actions">
      <button @click="confirmMerge">确认合并</button>
      <button @click="discardAll">丢弃全部</button>
    </div>
  </div>
</template>

<script setup lang="ts">
// TODO: 实现合并评审逻辑
</script>
```

### 6.4 Diff 计算

**文件**：`BIMCanvas.Server/Services/MergeService.cs`

```csharp
// TODO: 实现分区级 Diff 计算
public class MergeService
{
    /// <summary>
    /// 计算两个分支之间各分区的差异
    /// </summary>
    public async Task<Dictionary<string, ZoneDiff>> ComputeZoneDiffsAsync(
        string baseBranch,
        string compareBranch)
    {
        // 1. 读取两个分支的所有分区 modules
        // 2. 逐分区比较
        // 3. 返回差异列表
        throw new NotImplementedException();
    }

    /// <summary>
    /// 执行选择性合并
    /// </summary>
    public async Task ExecuteSelectiveMergeAsync(
        string sourceBranch,
        Dictionary<string, MergeChoice> selections)
    {
        // 1. 遍历选择
        // 2. 对于选择 AI 版本的分区，Cherry-pick
        // 3. 对于选择保留的分区，跳过
        throw new NotImplementedException();
    }
}

public class ZoneDiff
{
    public string ZoneId { get; set; }
    public bool HasChanges { get; set; }
    public int AddedModules { get; set; }
    public int RemovedModules { get; set; }
    public int ModifiedModules { get; set; }
}

public enum MergeChoice
{
    KeepMine,
    AcceptAI
}
```

### 6.5 验证清单

- [ ] 可显示双栏对比视图
- [ ] 分区差异正确计算
- [ ] 可按分区选择合并
- [ ] 合并结果正确

---

## 七、依赖关系图

```
Phase 1: 目录结构升级
    │
    ▼
Phase 2: Server Git 服务 ──────────────────────────────┐
    │                                                  │
    ├───────────────────┐                              │
    ▼                   ▼                              │
Phase 3: Web 多窗口    Phase 4: Agent 并行             │
    │                   │                              │
    └───────┬───────────┘                              │
            ▼                                          │
    Phase 5: 可视化 Merge ◄────────────────────────────┘
```

---

## 八、风险与缓解

| 风险 | 影响 | 缓解措施 |
|------|------|----------|
| **Git 操作失败** | 数据丢失 | 每次操作前自动备份；提供回滚机制 |
| **Worktree 残留** | 磁盘占用 | 定期清理脚本；Server 启动时检查 |
| **分支冲突** | 合并失败 | 可视化冲突解决；保守合并策略 |
| **Agent 线程泄漏** | 资源耗尽 | 超时机制；定期健康检查 |
| **多窗口状态同步** | 数据不一致 | WebSocket 实时同步；乐观锁 |

---

## 九、测试策略

### 9.1 单元测试

| 模块 | 测试重点 |
|------|----------|
| GitService | 分支操作正确性 |
| WorktreeService | 创建/删除/列表 |
| BranchLockManager | 并发安全性 |
| MergeService | Diff 计算准确性 |

### 9.2 集成测试

| 场景 | 验证点 |
|------|--------|
| 创建虚拟窗口 | Worktree 创建、Agent 启动、状态同步 |
| 关闭虚拟窗口 | 合并/丢弃、Worktree 清理、Agent 停止 |
| Agent 并行布置 | SubAgent 并行、Worktree 隔离、结果合并 |
| 可视化 Merge | Diff 显示、选择合并、最终结果 |

### 9.3 压力测试

| 场景 | 目标 |
|------|------|
| 多窗口并发 | 10 个虚拟窗口同时操作 |
| 多 SubAgent 并行 | 6 个 SubAgent 同时布置 |
| 频繁切换 | 快速切换窗口不丢数据 |

---

## 十、里程碑

| 里程碑 | 完成标准 | 目标周期 |
|--------|----------|----------|
| **M1** | Phase 1 完成，现有功能不受影响 | - |
| **M2** | Phase 2 完成，Git 操作可用 | - |
| **M3** | Phase 3 完成，多窗口基本可用 | - |
| **M4** | Phase 4 完成，Agent 并行可用 | - |
| **M5** | Phase 5 完成，可视化 Merge 可用 | - |

---

## 版本历史

| 版本 | 日期 | 变更内容 |
|------|------|----------|
| v1.2 | 2026-01-14 | §3.3 Git 工具集成到 Agent 进程（架构变更） |
| v1.1 | 2026-01-14 | Git 核心限制说明、Agent 工作流程修正 |
| v1.0 | 2026-01-14 | 初版：5 阶段升级计划 |
