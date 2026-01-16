# Web 端多窗口功能实现现状汇报

> **日期**：2026-01-16
> **目标**：梳理当前 `AICommandCenter.vue` 中关于多窗口（Multi-Window）的实现逻辑，对比 `Flow_Git_Operations.md` 标准文档，明确差距与后续对接点。

---

## 一、 现状概述

当前 Web 端的多窗口功能处于 **"UI 逻辑完备，后端未对接"** 的中间状态（Mock/Hybrid）。
- **UI 交互**：已实现窗口切换、新建窗口、关闭窗口、分支独占过滤等核心交互。
- **数据流**：
    - **真窗口 (Primary)**：已对接 `gitStore`，可真实切换分支。
    - **虚拟窗口 (Virtual)**：仅在前端维护 `windows` 数组状态，**尚未调用后端 API 创建 Worktree**。

---

## 二、 核心代码实现位置

所有相关逻辑位于 `src/components/UI/AICommandCenter.vue`。

### 2.1 数据模型

```typescript
// 行 100-105
interface ChatWindow {
  id: string;
  name: string;
  branchId: string; // 实际存储的是 branch.name (如 "master")
  messages: ChatMessage[];
  isPrimary: boolean; // 区分真窗口与虚拟窗口
}

// 行 108
const windows = ref<ChatWindow[]>([]);
```

### 2.2 核心操作方法

#### 1. 初始化默认窗口 (Primary Window)
- **位置**：`initDefaultWindow` (行 112)
- **逻辑**：创建唯一的 `isPrimary: true` 窗口，默认绑定到 `currentBranch`。
- **同步机制**：
    - **Watcher** (行 126)：监听 `gitStore.currentBranch` 变化，自动同步给真窗口的 `branchId`。
    - **注意**：此 Watcher 曾导致逻辑冲突，后续需根据架构调整（真窗口应主动控制分支，而非被动同步）。

#### 2. 切换分支 (仅真窗口)
- **位置**：`selectBranch` (行 422)
- **逻辑**：
    1. 调用 `gitStore.checkout(branchId)`。
    2. 处理未提交更改的弹窗逻辑 (`handleCheckoutConfirm`)。
- **现状**：这是目前唯一真实触发后端 Git 操作的入口。

#### 3. 新建虚拟窗口
- **位置**：`addWindow` (行 211)
- **逻辑**：
    1. 接收 `branchName`。
    2. 创建新的 `ChatWindow` 对象，`isPrimary: false`。
    3. 推入 `windows` 数组并切换焦点。
- **缺失**：**未调用 `POST /api/git/worktrees`**。目前只是在前端"假装"开了一个窗口，实际上后端并没有为该分支创建 Worktree 环境。

#### 4. 分支独占过滤
- **位置**：`availableBranches` (行 161)
- **逻辑**：
    - 计算 `occupiedBranchNames`（所有窗口占用的分支）。
    - 过滤出未被占用的分支列表。
- **现状**：逻辑正确，实现了"一个分支只能被一个窗口打开"的前端约束。

#### 5. 关闭窗口
- **位置**：`closeWindow` (行 167)
- **逻辑**：
    - 禁止关闭真窗口 (`isPrimary` 检查)。
    - 从 `windows` 数组移除对象。
    - 自动切换焦点到相邻窗口。
- **缺失**：**未调用 `DELETE /api/git/worktrees`**。后端 Worktree 未被清理。

---

## 三、 与标准架构的差距分析

对比 `docs/Flow_Git_Operations.md`，主要差距如下：

| 功能点 | 当前实现 (Web UI) | 目标架构 (Standard) | 差距/待办 |
|--------|-------------------|---------------------|-----------|
| **虚拟窗口创建** | 仅前端 `windows.push()` | 调用 `POST /api/git/worktrees` | 需在 `addWindow` 中增加 API 调用，等待后端返回成功后再更新 UI。 |
| **虚拟窗口关闭** | 仅前端 `windows.splice()` | 调用 `DELETE /api/git/worktrees` | 需在 `closeWindow` 中增加 API 调用。 |
| **分支标识符** | 混用 ID/Name (已修正为 Name) | 统一使用 Branch Name | 需确保后端 API 参数统一。 |
| **真窗口同步** | Watcher 自动同步 | 用户主动切换 | 需评估 Watcher 是否必要，避免覆盖用户意图。 |
| **Worktree 隔离** | 无 (所有窗口共享同一后端环境) | 每个虚拟窗口对应独立 Worktree | 需后端支持 Worktree 路径的动态切换/指定 (Agent 运行环境)。 |

---

## 四、 后续实施建议

在后端 "Git 分支管理" 和 "Worktree API" 就绪后，按以下步骤升级 Web 端：

1.  **引入 API Client**：在 `gitStore` 或独立 Service 中封装 `createWorktree` 和 `removeWorktree` 方法。
2.  **改造 `addWindow`**：
    ```typescript
    const addWindow = async (branchName: string) => {
        // 1. Call API
        await gitApi.createWorktree(branchName);
        // 2. Update UI (existing logic)
        // ...
    };
    ```
3.  **改造 `closeWindow`**：
    ```typescript
    const closeWindow = async (id: string) => {
        // 1. Call API
        await gitApi.removeWorktree(windows.value.find(w => w.id === id).branchId);
        // 2. Update UI (existing logic)
        // ...
    };
    ```
4.  **完善错误处理**：处理后端创建 Worktree 失败（如分支冲突、磁盘不足）的情况。
