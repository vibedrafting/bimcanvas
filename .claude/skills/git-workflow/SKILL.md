---
name: git-workflow
description: |
  MainAgent Git 工作流指导。当用户请求 execute 类任务（布置、添加、移动、删除、设计、创建）时，
  指导 MainAgent 先调用 ai_job_create 创建隔离环境，然后调用 SubAgent，最后调用 ai_job_complete。
  对于 query 任务（统计、查看、列出、有多少、当前状态），直接调用 SubAgent 即可。
---

# Git 标准工作流

> MainAgent 专用：教会何时/如何使用 ai_job_create/complete 为 SubAgent 创建隔离环境

## 核心决策：是否需要隔离环境

```
用户请求
    │
    ├─ query 任务（只读）？
    │   └─ 否 → 直接调用 SubAgent，不创建 AI Job
    │
    └─ execute 任务（可写）？
        └─ 是 → 先创建 AI Job，再调用 SubAgent
```

## Query 任务流程（无 Worktree）

**识别关键词**：统计、查看、列出、有多少、当前状态

**流程**：
1. 直接调用 layout-agent（在当前工作目录）
2. 等待执行完成
3. 汇报结果

## Execute 任务流程（需要 Worktree）

**识别关键词**：布置、添加、移动、删除、设计、创建

**流程**：
1. MainAgent 调用 `ai_job_create(name="layout-job-{timestamp}")`
2. 将返回的 `worktreePath` 传递给 SubAgent
3. SubAgent 在隔离环境中执行修改
4. SubAgent 返回后，MainAgent 收集结果并在对话中总结
5. MainAgent 调用 `ai_job_complete(name)` 通知 Web 端
6. Web 端打开 diff/merge 界面，用户手动审查/合并

## MCP 工具定位

### ai_job_create
- **调用者**：MainAgent
- **时机**：收到 execute 任务后，调用 SubAgent **之前**
- **用途**：为 SubAgent 创建隔离工作环境
- **参数**：`name`（必填），`base_branch`（可选）
- **返回**：`{ worktreePath, branchName }`

### ai_job_complete
- **调用者**：MainAgent
- **时机**：SubAgent 执行完毕并返回**之后**
- **用途**：通知 Web 端"这些 worktree/分支准备好了"
- **目的**：让 Web 端打开 diff/merge 可视化界面，供用户审查
- **参数**：`name`（必填）
- **不负责**：总结修改内容（MainAgent 自己在对话中向用户总结）

## 完整工作流（Execute 任务）

```
1. MainAgent 收到用户任务
        │
        ▼
2. MainAgent 调用 ai_job_create
   → 获得 { worktreePath, branchName }
        │
        ▼
3. MainAgent 调用 SubAgent，传递任务 + worktreePath
   【操作类型】: execute
   【工作目录】: {worktreePath}
   【用户需求】: {用户原始需求}
        │
        ▼
4. SubAgent 在 worktreePath 中执行修改
        │
        ▼
5. SubAgent 完成，返回结果给 MainAgent
        │
        ▼
6. MainAgent 在对话中向用户总结修改内容
        │
        ▼
7. MainAgent 调用 ai_job_complete(name)
   → 通知 Web 端
        │
        ▼
8. Web 端打开 diff/merge 界面
   用户手动审查/合并/丢弃
```

## 任务描述模板（调用 SubAgent 时）

**Query 任务**：
```
【操作类型】: query
【用户需求】: {用户原始需求}
【目标对象】: {目标房间/区域/文件}
```

**Execute 任务**：
```
【操作类型】: execute
【工作目录】: {worktreePath}
【用户需求】: {用户原始需求}
【目标对象】: {目标房间/区域}
【约束条件】: {特殊要求，如果有}
```
