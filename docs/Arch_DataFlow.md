# BIMCanvas 数据流场景分析

> **版本**: v1.0
> **创建日期**: 2026-01-13
> **关联文档**: [Architecture.md](./Architecture.md)

本文档详细描述 BIMCanvas 的数据流机制，包括三层职责划分、核心组件、状态管理、典型场景分析和 API 参考。

---

## 1. 三层职责划分

### 1.1 架构概览

```
┌─────────────────────────────────────────────────────────────┐
│                        Web 端（浏览器）                       │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────────┐  │
│  │ canvasStore  │  │  Timeline    │  │   SignalRService │  │
│  │   (Pinia)    │  │   Manager    │  │     (Client)     │  │
│  └──────────────┘  └──────────────┘  └──────────────────┘  │
└─────────────────────────────────────────────────────────────┘
                              ↑ SignalR / REST API
                              ↓
┌─────────────────────────────────────────────────────────────┐
│                      Server 端（.NET）                        │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────────┐  │
│  │ ProjectCtrl  │  │ CanvasHub    │  │ ProjectWatcher   │  │
│  │  (REST API)  │  │  (SignalR)   │  │ (FileWatcher)    │  │
│  └──────────────┘  └──────────────┘  └──────────────────┘  │
│                          ↑                     ↑            │
│                    ProjectContext ────────────►            │
└─────────────────────────────────────────────────────────────┘
                              ↑ 文件读写
                              ↓
┌─────────────────────────────────────────────────────────────┐
│                      本地文件系统                              │
│  project.bcp (解压后)                                        │
│  ├── project.json          项目元数据                        │
│  ├── baseline/             建筑基础数据（只读）                │
│  ├── schemes/              方案设计数据（可写）                │
│  │   └── modules.json      家具布置 ◄── Agent/Web 修改       │
│  └── computed/             计算派生数据（自动生成）            │
└─────────────────────────────────────────────────────────────┘
```

### 1.2 职责划分表

| 层级 | 位置 | 持久化 | 核心职责 |
|------|------|--------|---------|
| **本地文件** | Server 端磁盘 | ✅ 是 | 唯一真理源，存储所有业务数据 |
| **Server** | .NET 进程内存 | ❌ 否 | 文件播放器、通信中枢、状态协调 |
| **Web** | 浏览器内存 | ❌ 否 | 渲染展示、用户交互、历史管理 |

### 1.3 三条核心数据流

| 数据流 | 方向 | 触发条件 | 关键组件 |
|--------|------|---------|---------|
| **用户编辑流** | Web → Server → 文件 | 用户拖动/修改模块 | canvasStore → REST API → File.WriteAllText |
| **文件同步流** | 文件 → Server → Web | 文件变化（Agent/外部编辑） | FileWatcher → SignalR → syncFromServer() |
| **项目加载流** | 文件 → Server → Web | 上传/切换项目 | REST API → loadProject() |

---

## 2. Server 核心组件

### 2.1 ProjectContext（项目上下文）

**文件**：`BIMCanvas.Server/Services/ProjectContext.cs`

```csharp
public class ProjectContext {
    public string? CurrentProjectPath { get; private set; }  // 当前项目路径
    public string? SourceBcpPath { get; private set; }       // BCP 源文件路径
    public bool IsLoaded => !string.IsNullOrEmpty(CurrentProjectPath);
    public bool IsGitOperationInProgress { get; set; }       // Git 操作锁
}
```

**职责**：
- 单项目模式状态管理（Server 一次只服务一个项目）
- Git 操作锁（防止 FileWatcher 在 Git 操作期间触发更新）

### 2.2 ProjectWatcherService（文件监听服务）

**文件**：`BIMCanvas.Server/Services/ProjectWatcherService.cs`

**职责**：
- 使用 `FileSystemWatcher` 监听 `schemes/` 目录下的 JSON 文件变化
- 500ms 防抖处理 + Git 感知（详见 [Architecture.md §5.1-5.2](./Architecture.md#51-防抖机制-500ms)）
- 通过 SignalR 广播变化通知给 Web 客户端

**监听文件列表**：`modules.json`, `zones.json`, `finishes.json`

**事件处理流程**：
```
文件变化事件 → OnFileChanged()
    ↓
检查是否在 WatchedFiles 中 → 否 → 忽略
    ↓ 是
检查 IsGitOperationInProgress → 是 → 忽略
    ↓ 否
ScheduleUpdate() → 500ms 防抖
    ↓
BroadcastUpdate() → SignalR 推送 "ReceiveUpdate"
```

### 2.3 CanvasHub（SignalR Hub）

**文件**：`BIMCanvas.Server/Hubs/CanvasHub.cs`

**路由**：`/hubs/canvas`

| 事件名 | 方向 | 用途 |
|--------|------|------|
| `ReceiveUpdate` | Server → Client | 文件变化通知 |
| `ReceiveGhostPatch` | Server → Client | Agent 实时预览 |
| `SendUpdate` | Client → Server → Others | 客户端广播 |

**消息格式**：
```json
{
  "type": "file_changed",
  "file": "modules.json",
  "timestamp": "2026-01-11T10:30:00Z",
  "action": "reload"
}
```

---

## 3. 前端状态管理

### 3.1 脏数据追踪 (Dirty Tracking)

**目的**：追踪内存中是否有未保存到文件的修改。

```typescript
const isDirty = ref(false);

const updateModule = (...) => {
    // ...更新逻辑
    isDirty.value = true;  // 标记为脏数据
};

const saveToServer = async () => {
    // ...保存逻辑
    isDirty.value = false;  // 清除脏标记
};
```

### 3.2 批量更新模式 (Batch Update)

**问题背景**：
拖动操作会频繁触发 `updateModule()`，每次都保存快照和持久化会造成历史栈污染和性能损耗。

**解决方案**：批量更新模式

```typescript
// 拖动开始
store.beginBatchUpdate();

// 拖动过程中...
store.updateModule(id, { position: newPos });  // 不触发保存

// 拖动结束
await store.endBatchUpdate();  // 一次性保存快照 + 持久化
```

### 3.3 历史快照结构 (HistorySnapshot)

```typescript
export interface HistorySnapshot {
  id: string;              // 快照唯一ID
  timestamp: number;       // 时间戳
  state: string;           // 序列化的 ProjectData JSON
  source: ChangeSource;    // 变更来源
  sourceId?: string;       // 来源标识
  description?: string;    // 人类可读描述
  changeType?: ChangeType; // 变更类型
  affectedIds?: string[];  // 受影响的对象ID
  metadata?: {
    branchName?: string;
    commitHash?: string;
    agentConversationId?: string;
  };
}
```

### 3.4 变更源追踪 (Change Source Tracking)

**问题背景**：
不同场景下加载项目需要不同的历史管理策略：
- 上传新项目：应清空历史
- Agent 修改：应保留历史（支持 Ctrl+Z 撤回）
- Git 切换分支：应清空历史但保持视图

**解决方案**：ChangeSource 枚举 + 策略配置

```typescript
export enum ChangeSource {
  // 用户操作
  UserEdit = 'user_edit',
  UserUpload = 'user_upload',
  UserUndo = 'user_undo',
  UserRedo = 'user_redo',
  // Git 操作
  GitCheckout = 'git_checkout',
  GitDiscard = 'git_discard',
  GitInit = 'git_init',
  // 远程同步
  AgentModify = 'agent_modify',
  ServerSync = 'server_sync',
  CollabSync = 'collab_sync',
  // 系统操作
  SystemInit = 'system_init',
  SystemRestore = 'system_restore'
}
```

**策略配置**：
```typescript
// 清空历史的来源
CLEAR_HISTORY_SOURCES = [UserUpload, GitInit, SystemInit]

// 保留历史的来源
PRESERVE_HISTORY_SOURCES = [AgentModify, ServerSync, UserEdit]

// 保持视图的来源
PRESERVE_VIEW_SOURCES = [GitCheckout, AgentModify, ServerSync]
```

### 3.5 Undo/Redo 与外部干扰规则

**机制**：
- 用户移动 A → B。Server 记录逆向操作 `{ cmd: "Move", from: B, to: A }` 入栈。
- 用户点击 Undo。Server 执行逆向操作，将 A **写入文件**。

**外部干扰规则**：
> 一旦检测到**非 Web 端发起**的文件变更（如 VS Code 手动修改），立即**清空**当前的 Undo 栈。

**理由**：外部修改切断了 Undo 链条，强行回滚会导致状态不一致。

---

## 4. 典型场景分析

### 4.1 场景一：Web 端拖动模块

**调用链**：

| 步骤 | 组件 | 方法 | 文件位置 |
|------|------|------|---------|
| 1 | DragManager | `onMouseUp()` | src/services/interaction/DragManager.ts |
| 2 | canvasStore | `beginBatchUpdate()` | src/stores/canvasStore.ts:442 |
| 3 | canvasStore | `updateModule()` | src/stores/canvasStore.ts:354 |
| 4 | canvasStore | `endBatchUpdate()` | src/stores/canvasStore.ts:446 |
| 5 | canvasStore | `saveToServer()` | src/stores/canvasStore.ts:498 |
| 6 | ProjectController | `SaveModules()` | Controllers/ProjectController.cs:417 |

**注意点**：防抖 500ms、脏数据标记、批量更新、Git 感知

### 4.2 场景二：Agent 修改家具布置

**调用链**：

| 步骤 | 组件 | 方法 | 文件位置 |
|------|------|------|---------|
| 1 | Agent | Write modules.json | (外部进程) |
| 2 | FileSystemWatcher | `OnFileChanged()` | Services/ProjectWatcherService.cs:130 |
| 3 | ProjectWatcherService | `ScheduleUpdate()` | Services/ProjectWatcherService.cs:175 |
| 4 | ProjectWatcherService | `BroadcastUpdate()` | Services/ProjectWatcherService.cs:206 |
| 5 | SignalRService | `ReceiveUpdate` | src/services/SignalRService.ts:25 |
| 6 | canvasStore | `syncFromServer()` | src/stores/canvasStore.ts:481 |
| 7 | TimelineManager | `push(ServerSync)` | src/services/state/TimelineManager.ts:89 |

**关键设计**：历史栈保留，用户可用 Ctrl+Z 撤回 Agent 修改！

### 4.3 场景三：外部编辑器修改 JSON

**场景描述**（代码式设计）：
- **操作**：用户在 VS Code 中打开 `modules.json`，手动修改床的坐标 `x: 3000` → `x: 4000` 并保存。
- **流转**：
  1. `FileSystemWatcher` 检测到文件变化
  2. Server 重新解析 JSON
  3. Server 通过 SignalR 广播 `LayoutUpdated` 事件
  4. Web 端收到事件，平滑动画将床移动到新位置

**价值**：实现"热重载"般的开发体验，便于调试和 AI 行为模拟。

### 4.4 场景四：Git 分支切换

**关键点**：
- `IsGitOperationInProgress = true` 阻止 FileWatcher 触发更新
- Git 操作完成后手动调用 `loadProject(GitCheckout)`
- 清空历史（新分支新起点），但保持视图

### 4.5 场景五：可视化设计（Web 编辑）

**场景描述**：
- **操作**：用户在 Web 端拖拽沙发到新位置。
- **流转**：
  1. Web 发送移动指令给 Server
  2. Server 验证通过后，**直接覆写**硬盘上的 `modules.json`
  3. 文件系统发生物理变更
  4. Server 再次广播更新（确认状态）

**价值**：Web 成为文件系统的可视化编辑器。

---

## 5. API 参考

### 5.1 REST API

| 端点 | 方法 | 请求体 | 响应 |
|------|------|-------|------|
| `/api/project` | GET | - | ProjectData |
| `/api/project/status` | GET | - | {isLoaded, projectPath} |
| `/api/project/upload` | POST | FormData(.bcp) | ProjectLoadResult |
| `/api/project/save` | POST | {modules: Module[]} | {success, modulesCount} |
| `/api/project/export` | GET | - | 文件流 |

### 5.2 SignalR 事件

| 事件名 | 方向 | 数据格式 | 触发条件 |
|--------|------|---------|---------|
| `ReceiveUpdate` | Server → Client | {type, file, action} | 文件变化 |
| `ReceiveGhostPatch` | Server → Client | patch | Agent 实时预览 |
| `SendUpdate` | Client → Server → Others | any | 客户端广播 |

### 5.3 全局事件

| 事件名 | 数据 | 触发条件 |
|--------|------|---------|
| `bimcanvas:server-update` | {type, file, action} | Server 文件变化推送 |
| `bimcanvas:connection-state` | Connected/Disconnected/Reconnecting | 连接状态变化 |

---

## 6. 组件参考

### 6.1 Server 端组件

| 组件 | 文件路径 | 核心方法 |
|------|----------|---------|
| ProjectContext | Services/ProjectContext.cs | SetProject(), IsGitOperationInProgress |
| ProjectWatcherService | Services/ProjectWatcherService.cs | OnFileChanged(), BroadcastUpdate() |
| CanvasHub | Hubs/CanvasHub.cs | SendUpdate() |
| ProjectController | Controllers/ProjectController.cs | GetProjectData(), SaveModules() |

### 6.2 Web 端组件

| 组件 | 文件路径 | 核心方法 |
|------|----------|---------|
| SignalRService | src/services/SignalRService.ts | start(), sendUpdate() |
| canvasStore | src/stores/canvasStore.ts | loadProject(), syncFromServer(), saveToServer() |
| TimelineManager | src/services/state/TimelineManager.ts | push(), undo(), redo() |
| gitStore | src/stores/gitStore.ts | checkout(), discard() |

### 6.3 类型定义

| 类型 | 文件路径 | 说明 |
|------|----------|------|
| ChangeSource | src/types/history.ts | 变更来源枚举 |
| HistorySnapshot | src/types/history.ts | 历史快照接口 |
| LoadOptions | src/types/history.ts | 加载选项接口 |
| ProjectData | src/types/canvas.ts | 聚合项目数据 |

---

## 7. 变更源策略速查表

| ChangeSource | 清空历史 | 保留历史 | 保持视图 |
|--------------|---------|---------|---------|
| SystemInit | ✅ | ❌ | ❌ |
| UserUpload | ✅ | ❌ | ❌ |
| GitInit | ✅ | ❌ | ❌ |
| GitCheckout | ✅ | ❌ | ✅ |
| GitDiscard | ✅ | ❌ | ✅ |
| AgentModify | ❌ | ✅ | ✅ |
| ServerSync | ❌ | ✅ | ✅ |
| UserEdit | ❌ | ✅ | - |

---

## 8. 调试技巧

### 8.1 查看历史栈状态

```javascript
const store = useCanvasStore();
console.log(store.canUndo, store.canRedo);
```

### 8.2 监控文件变化事件

```javascript
window.addEventListener('bimcanvas:server-update', e => console.log(e.detail));
```

---

## 9. Visual Merge UI（方案融合）

> 详见 [Architecture.md §6.3 Visual Merge UI](./Architecture.md#63-visual-merge-ui可视化冲突解决)

**简述**：当 AI Agent 完成方案生成后，系统进入"评审模式"，用户可按 Zone 颗粒度选择性合并 AI 方案与自己的方案。

---

## 10. 持久化双层策略

> 详见 [Architecture.md §5.5 持久化双层策略](./Architecture.md#55-持久化双层策略)

**简述**：采用"磁盘即时同步 + Git 周期存档"双层策略，确保外部工具实时同步且支持版本回溯。
