# BIMCanvas 数据流指南

> 版本：v1.0 | 更新日期：2026-01-11

本文档详细说明 BIMCanvas 中本地文件、Server 端、Web 端之间的数据变更传递流程。

## 1. 概述

### 1.1 三层架构

| 层级 | 位置 | 持久化 | 职责 |
|------|------|--------|------|
| 本地文件 | Server 端磁盘 | 是 | 唯一真理源 |
| Server | .NET 进程内存 | 否 | 文件播放器、通信中枢 |
| Web | 浏览器内存 | 否 | 渲染展示、用户交互 |

### 1.2 核心组件

**Server 端**：
- ProjectWatcherService: FileSystemWatcher 监听 schemes/*.json (500ms防抖)
- CanvasHub: SignalR Hub (/hubs/canvas)
- ProjectController: REST API
- ProjectContext: 单项目状态管理

**Web 端**：
- SignalRService: 监听 ReceiveUpdate
- canvasStore: Pinia 状态管理
- TimelineManager: 历史快照管理
- gitStore: Git 分支操作

### 1.3 关键数据流

1. **Web -> Server -> 文件**: updateModule() -> saveToServer() -> POST /api/project/save
2. **文件 -> Server -> Web**: FileWatcher -> SignalR -> syncFromServer()
3. **Agent -> 文件 -> Web**: Agent写文件 -> FileWatcher -> SignalR -> Web重载

## 2. 场景一：Web 端拖动模块

**调用链**：
1. DragManager.onMouseUp() [src/services/interaction/DragManager.ts:132]
2. canvasStore.updateModule() [src/stores/canvasStore.ts:354]
3. canvasStore.saveToServer() [src/stores/canvasStore.ts:498]
4. ProjectController.SaveModules() [Controllers/ProjectController.cs:417]
5. ProjectWatcherService.BroadcastUpdate() [Services/ProjectWatcherService.cs:206]

**注意点**：防抖500ms、脏数据标记、批量更新、Git感知

## 3. 场景二：Agent 修改家具布置

**调用链**：
1. Agent 写入 modules.json
2. ProjectWatcherService.OnFileChanged() [Services/ProjectWatcherService.cs:130]
3. SignalRService.ReceiveUpdate [src/services/SignalRService.ts:24]
4. canvasStore.syncFromServer() [src/stores/canvasStore.ts:481]

**关键点**：ServerSync 来源保留历史，支持 Ctrl+Z 撤回 Agent 修改！

## 4. 场景三：上传新项目

**调用链**：
1. ProjectController.UploadProject() [Controllers/ProjectController.cs:93]
2. ProjectService.LoadProject() [Services/ProjectService.cs:84]
3. canvasStore.loadProject(UserUpload) 清空历史

**8步加载流程**：解压->哈希->context->schemes->project.json->computed->modules->Git

## 5. 场景四：Git 分支切换

**调用链**：
1. gitStore.checkout() [src/stores/gitStore.ts:185]
2. GitController.Checkout() [Controllers/GitController.cs:69]
3. canvasStore.loadProject(GitCheckout) 清空历史

**关键点**：双重脏检查、Git感知、视图保持

## 6. 场景五：导出项目

**调用链**：
1. ProjectController.ExportProject() [Controllers/ProjectController.cs:374]
2. ZipFile.CreateFromDirectory() -> 返回文件流

## 7. API 端点参考

| 端点 | 方法 | 功能 |
|------|------|------|
| /api/project | GET | 获取项目数据 |
| /api/project/save | POST | 保存模块 |
| /api/project/upload | POST | 上传BCP |
| /api/project/export | GET | 导出BCP |
| /api/git/checkout | POST | 切换分支 |

## 8. 变更源策略

- 清空历史: UserUpload, GitInit, SystemInit
- 保留历史: AgentModify, ServerSync, UserEdit
- 保持视图: GitCheckout, AgentModify, ServerSync

---

详见 reports/Change_Source_Tracking_Implementation_Report.md
