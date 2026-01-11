# BIMCanvas 数据流技术手册

> 版本：v2.0 | 更新日期：2026-01-11

本文档详细阐述 BIMCanvas 的文件驱动架构设计理念，以及本地文件、Server 端、Web 端之间的数据变更传递机制。

---

## 目录

1. [设计理念](#1-设计理念)
2. [系统架构](#2-系统架构)
3. [核心机制](#3-核心机制)
4. [数据模型](#4-数据模型)
5. [场景分析](#5-场景分析)
6. [API 参考](#6-api-参考)
7. [组件参考](#7-组件参考)
8. [附录](#8-附录)

---

## 1. 设计理念

### 1.1 文件驱动架构 (File-Driven Architecture)

BIMCanvas 采用**文件驱动架构**，其核心原则是：

> **文件是唯一真理源，Server 是"文件播放器"而非"内存数据库"**

这意味着：
- **持久化优先**：所有业务数据以 JSON 文件形式存储在磁盘
- **Server 无状态**：Server 不"拥有"数据，只负责读取、聚合、分发文件内容
- **变更可追溯**：任何外部进程（Agent、脚本、手工编辑）修改文件后，系统自动感知并同步

### 1.2 为什么选择文件驱动

| 传统架构 | 文件驱动架构 |
|---------|-------------|
| 数据库是真理源 | 文件是真理源 |
| 需要数据迁移 | 直接操作 JSON |
| Agent 需要 API 接口 | Agent 直接写文件 |
| 版本控制需要额外方案 | 天然支持 Git |
| 调试需要查数据库 | 调试直接看文件 |

**核心优势**：
1. **Git 原生集成**：项目文件即 Git 仓库，分支/回滚/协作开箱即用
2. **Agent 友好**：AI Agent 无需 API 即可直接修改 JSON 文件
3. **调试透明**：所有状态可直接通过文件系统查看
4. **离线可用**：文件在本地，无需网络即可编辑

### 1.3 三层数据模型

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

### 1.4 数据流向总览

**三条核心数据流**：

| 数据流 | 方向 | 触发条件 | 关键组件 |
|--------|------|---------|---------|
| **用户编辑流** | Web → Server → 文件 | 用户拖动/修改模块 | canvasStore → REST API → File.WriteAllText |
| **文件同步流** | 文件 → Server → Web | 文件变化（Agent/外部编辑） | FileWatcher → SignalR → syncFromServer() |
| **项目加载流** | 文件 → Server → Web | 上传/切换项目 | REST API → loadProject() |

---

## 2. 系统架构

### 2.1 三层职责划分

| 层级 | 位置 | 持久化 | 核心职责 |
|------|------|--------|---------|
| **本地文件** | Server 端磁盘 | ✅ 是 | 唯一真理源，存储所有业务数据 |
| **Server** | .NET 进程内存 | ❌ 否 | 文件播放器、通信中枢、状态协调 |
| **Web** | 浏览器内存 | ❌ 否 | 渲染展示、用户交互、历史管理 |

### 2.2 Server 端组件

#### 2.2.1 ProjectContext（项目上下文）

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

#### 2.2.2 ProjectWatcherService（文件监听服务）

**文件**：`BIMCanvas.Server/Services/ProjectWatcherService.cs`

**职责**：
- 使用 `FileSystemWatcher` 监听 `schemes/` 目录下的 JSON 文件变化
- 500ms 防抖处理（Agent 可能连续写入多个文件）
- Git 感知（`IsGitOperationInProgress` 为 true 时跳过事件）
- 通过 SignalR 广播变化通知给 Web 客户端

**监听文件列表**：
```csharp
private static readonly HashSet<string> WatchedFiles = new() {
    "modules.json", "zones.json", "finishes.json"
};
```

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

#### 2.2.3 CanvasHub（SignalR Hub）

**文件**：`BIMCanvas.Server/Hubs/CanvasHub.cs`

**路由**：`/hubs/canvas`

| 事件名 | 方向 | 用途 |
|--------|------|------|
| `ReceiveUpdate` | Server → Client | 文件变化通知 |
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

#### 2.2.4 ProjectController（REST API）

**文件**：`BIMCanvas.Server/Controllers/ProjectController.cs`

| 端点 | 方法 | 功能 |
|------|------|------|
| `/api/project` | GET | 获取聚合后的 ProjectData |
| `/api/project/status` | GET | 获取项目加载状态 |
| `/api/project/upload` | POST | 上传 BCP 文件 |
| `/api/project/save` | POST | 保存 modules 到文件 |
| `/api/project/export` | GET | 导出 BCP 文件 |

### 2.3 Web 端组件

#### 2.3.1 SignalRService（SignalR 客户端）

**文件**：`BIMCanvas.Web/src/services/SignalRService.ts`

**职责**：
- 维护与 Server 的 WebSocket 连接（自动重连）
- 监听 `ReceiveUpdate` 事件，分发到全局事件总线
- 连接状态管理（Connected / Disconnected / Reconnecting）

```typescript
this.connection.on("ReceiveUpdate", (data: any) => {
    window.dispatchEvent(new CustomEvent('bimcanvas:server-update', { detail: data }));
});
```

#### 2.3.2 canvasStore（Pinia 状态管理）

**文件**：`BIMCanvas.Web/src/stores/canvasStore.ts`

**核心状态**：
```typescript
const projectData = ref<ProjectData | null>(null);  // 项目数据
const isDirty = ref(false);                         // 脏数据标记
const selectedIds = ref<string[]>([]);              // 选中对象
const canUndo = ref(false);                         // 撤销可用性
const canRedo = ref(false);                         // 重做可用性
```

**核心方法**：
| 方法 | 功能 | 历史策略 |
|------|------|---------|
| `loadProject(source)` | 统一加载入口 | 根据 source 智能决策 |
| `syncFromServer()` | 远程同步（保留历史） | 保留历史 + 保持视图 |
| `saveToServer()` | 持久化到文件系统 | 不影响历史 |
| `undo() / redo()` | 撤销/重做 | 保持视图 |

#### 2.3.3 TimelineManager（历史管理器）

**文件**：`BIMCanvas.Web/src/services/state/TimelineManager.ts`

**职责**：
- 管理历史快照栈（支持 Undo/Redo）
- 根据变更来源智能决策历史策略
- 提供历史查询和统计功能

**策略配置**：
```typescript
// 清空历史的来源
CLEAR_HISTORY_SOURCES = [UserUpload, GitInit, SystemInit]

// 保留历史的来源
PRESERVE_HISTORY_SOURCES = [AgentModify, ServerSync, UserEdit]

// 保持视图的来源
PRESERVE_VIEW_SOURCES = [GitCheckout, AgentModify, ServerSync]
```

---

## 3. 核心机制

### 3.1 防抖机制 (Debounce)

**问题背景**：
Agent 执行布置任务时，可能在短时间内连续写入多个文件。如果每次文件变化都立即触发同步，会导致不必要的网络开销和历史栈膨胀。

**解决方案**：500ms 防抖

```csharp
private const int DebounceMs = 500;

private void ScheduleUpdate(string fileName) {
    lock (_lock) {
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        _ = Task.Run(async () => {
            await Task.Delay(DebounceMs, token);
            if (!token.IsCancellationRequested)
                await BroadcastUpdate(fileName);
        }, token);
    }
}
```

### 3.2 Git 感知机制

**问题背景**：
Git 操作会同时修改多个文件，如果 FileWatcher 在此期间触发更新，会导致中间状态被推送到 Web。

**解决方案**：Git 操作锁

```csharp
// ProjectContext.cs
public bool IsGitOperationInProgress { get; set; }

// ProjectWatcherService.cs
private void OnFileChanged(...) {
    if (_projectContext.IsGitOperationInProgress) {
        _logger.LogDebug("Git 操作进行中，跳过文件变化");
        return;
    }
    // ...正常处理
}
```

### 3.3 变更源追踪 (Change Source Tracking)

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

**策略决策表**：

| 场景 | ChangeSource | 清空历史 | 保留历史 | 保持视图 |
|------|-------------|---------|---------|---------|
| 系统初始化 | SystemInit | ✅ | ❌ | ❌ |
| 上传新项目 | UserUpload | ✅ | ❌ | ❌ |
| Git 切换分支 | GitCheckout | ✅ | ❌ | ✅ |
| Agent 修改 | AgentModify | ❌ | ✅ | ✅ |
| Server 推送 | ServerSync | ❌ | ✅ | ✅ |
| 用户编辑 | UserEdit | ❌ | ✅ | - |

### 3.4 脏数据追踪 (Dirty Tracking)

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

### 3.5 批量更新模式 (Batch Update)

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

### 3.6 历史快照结构

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

---

## 4. 数据模型

### 4.1 项目文件结构 (.bcp)

`.bcp` 文件是一个 ZIP 压缩包，解压后的目录结构：

```
project_name/
├── project.json           # 项目元数据
├── baseline/              # 建筑基础数据（只读）
│   ├── metadata.json
│   ├── architecture.json  # 墙体 + 柱子
│   ├── openings.json      # 门窗
│   ├── rooms.json         # 房间
│   └── location_lines.json
├── schemes/               # 方案设计数据（可写）
│   ├── strategy.json
│   ├── zones.json
│   ├── finishes.json
│   └── modules.json       # ← 核心数据文件
├── computed/              # 计算派生数据
│   ├── room_zones.json
│   └── exclusions.json
└── modules/               # 模块素材库
    ├── module_library.json
    └── assets/
```

### 4.2 ProjectData（聚合数据）

Server 通过 `GET /api/project` 返回：

```typescript
interface ProjectData {
  project: { name, version, activeSchemeId };
  baseline: { walls, columns, openings, rooms, locationLines };
  activeScheme: { strategy, zones, finishes, modules };
  computed: { roomZones, exclusions };
}
```

### 4.3 Module（布置模块）

```typescript
interface Module {
  id: string;           // 布置实例ID（m_1, m_2, ...）
  moduleId: string;     // 引用 module_library 的模块ID
  zoneId: string;       // 所属设计区域ID
  bounds: Point2D[];    // 4个顶点坐标（逆时针）
  facing: Facing;       // 朝向
  items: Item[];        // 子物品
}
```

---

## 5. 场景分析

### 5.1 场景一：Web 端拖动模块

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

### 5.2 场景二：Agent 修改家具布置

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

### 5.3 场景三：上传新项目

**调用链**：
1. `POST /api/project/upload` → ProjectController
2. `ProjectService.LoadProject()` → 解压 .bcp
3. `ProjectContext.SetProject(path)`
4. Web: `loadProject(ChangeSource.UserUpload)`
5. `Timeline.clear()` ← 清空历史

### 5.4 场景四：Git 分支切换

**关键点**：
- `IsGitOperationInProgress = true` 阻止 FileWatcher 触发更新
- Git 操作完成后手动调用 `loadProject(GitCheckout)`
- 清空历史（新分支新起点），但保持视图

### 5.5 场景五：导出项目

**调用链**：
1. `GET /api/project/export` → ProjectController
2. `ProjectService.SaveProject()` → ZipFile.CreateFromDirectory()
3. 返回文件流

---

## 6. API 参考

### 6.1 REST API

| 端点 | 方法 | 请求体 | 响应 |
|------|------|-------|------|
| `/api/project` | GET | - | ProjectData |
| `/api/project/status` | GET | - | {isLoaded, projectPath} |
| `/api/project/upload` | POST | FormData(.bcp) | ProjectLoadResult |
| `/api/project/save` | POST | {modules: Module[]} | {success, modulesCount} |
| `/api/project/export` | GET | - | 文件流 |

### 6.2 SignalR 事件

| 事件名 | 方向 | 数据格式 | 触发条件 |
|--------|------|---------|---------|
| `ReceiveUpdate` | Server→Client | {type, file, action} | 文件变化 |
| `ReceiveGhostPatch` | Server→Client | patch | Agent 实时预览 |
| `SendUpdate` | Client→Server→Others | any | 客户端广播 |

### 6.3 全局事件

| 事件名 | 数据 | 触发条件 |
|--------|------|---------|
| `bimcanvas:server-update` | {type, file, action} | Server 文件变化推送 |
| `bimcanvas:connection-state` | Connected/Disconnected/Reconnecting | 连接状态变化 |

---

## 7. 组件参考

### 7.1 Server 端组件

| 组件 | 文件路径 | 核心方法 |
|------|----------|---------|
| ProjectContext | Services/ProjectContext.cs | SetProject(), IsGitOperationInProgress |
| ProjectWatcherService | Services/ProjectWatcherService.cs | OnFileChanged(), BroadcastUpdate() |
| CanvasHub | Hubs/CanvasHub.cs | SendUpdate() |
| ProjectController | Controllers/ProjectController.cs | GetProjectData(), SaveModules() |

### 7.2 Web 端组件

| 组件 | 文件路径 | 核心方法 |
|------|----------|---------|
| SignalRService | src/services/SignalRService.ts | start(), sendUpdate() |
| canvasStore | src/stores/canvasStore.ts | loadProject(), syncFromServer(), saveToServer() |
| TimelineManager | src/services/state/TimelineManager.ts | push(), undo(), redo() |
| gitStore | src/stores/gitStore.ts | checkout(), discard() |

### 7.3 类型定义

| 类型 | 文件路径 | 说明 |
|------|----------|------|
| ChangeSource | src/types/history.ts | 变更来源枚举 |
| HistorySnapshot | src/types/history.ts | 历史快照接口 |
| LoadOptions | src/types/history.ts | 加载选项接口 |
| ProjectData | src/types/canvas.ts | 聚合项目数据 |

---

## 8. 附录

### 8.1 变更源策略速查表

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

### 8.2 调试技巧

**查看历史栈状态**：
```javascript
const store = useCanvasStore();
console.log(store.canUndo, store.canRedo);
```

**监控文件变化事件**：
```javascript
window.addEventListener('bimcanvas:server-update', e => console.log(e.detail));
```

### 8.3 相关文档

- 架构设计：`docs/Architecture.md`
- JSON Schema：`docs/Schema-JSON-v3.md`
- 变更源追踪实现：`reports/Change_Source_Tracking_Implementation_Report.md`

---

*文档结束*
