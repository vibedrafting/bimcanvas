# BIMCanvas 首页 — Server 端实施计划

> 基于 `Homepage_Functional_Design.md` 中定义的功能需求，本文档规划 Server 端的具体改造方案。

---

## 1. 现有代码资产盘点

以下现有代码可直接复用，无需重写：

| 现有代码 | 位置 | 复用方式 |
|----------|------|---------|
| `ProjectService.DefaultProjectsRoot` | `Services/ProjectService.cs:38` | 默认项目目录常量，ScanProjects 直接使用 |
| `ProjectService.EnsureProjectAssets()` | `Services/ProjectService.cs:129` | 打开已有文件夹时修复缺失资源（templates、git） |
| `ProjectService.EnsureComputedData()` | `Services/ProjectService.cs` | 打开文件夹时确保 computed 数据有效 |
| `ProjectService.EnsureZonesInitializedFromComputed()` | `Services/ProjectService.cs:148` | 缺失 zones.json 时初始化 |
| `ProjectService.CreateZoneDirectories()` | `Services/ProjectService.cs:173` | 确保分区目录结构完整 |
| `ProjectContext.Clear()` | `Services/ProjectContext.cs:59` | 关闭项目时清理状态 |
| `ProjectContext.SetProject()` | `Services/ProjectContext.cs:50` | 打开项目时设置状态 |
| `ProjectWatcherService` | `Services/ProjectWatcherService.cs` | 已有轮询 `IsLoaded` 的自适应机制，Clear 后自动停止 |
| `ConfigService.GetConfigDir()` | `Services/ConfigService.cs:159` | 获取 `~/Documents/BIMCanvas/` 路径 |
| `ConfigService` 的读写模式 | `Services/ConfigService.cs` | Load/Save JSON 的标准模式，RecentProjectsService 参照 |
| 冲突检测 + 解决流程 | `Controllers/ProjectController.cs` | 导入 .bcp 的完整流程已就绪，首页直接复用 |

---

## 2. 文件改造清单

### 新增文件

| 文件 | 职责 |
|------|------|
| `Services/RecentProjectsService.cs` | 管理 `~/Documents/BIMCanvas/recent_projects.json` 的读写 |

**RecentProjectsService 设计要点**：
- 参照 `ConfigService` 的 Load/Save 模式（`System.Text.Json`）
- 文件不存在时返回空列表（不报错）
- `RecordOpen` 需要线程安全（lock）
- 对外方法：`Load()`、`RecordOpen(name, folderPath)`、`Remove(folderPath)`、`LoadWithExistsCheck()`
- 数据模型：`RecentProjectEntry { Name, FolderPath, LastOpenedAt, OpenCount }`

### 修改文件

| 文件 | 改动内容 |
|------|---------|
| `Services/ProjectService.cs` | 新增 `ScanProjects()` / `OpenFolder()` / `DeleteProject()` 三个方法 |
| `Controllers/ProjectController.cs` | 新增 5 个端点（list / open-folder / close / delete / recent），构造函数增加依赖 |
| `Program.cs` | 移除启动时自动加载项目（176-238 行），改为空启动；延迟 ConversationLogger 和 Worktree 清理到 open-folder 时 |
| `Services/ConfigService.cs` | 新增 `GetRecentProjectsPath()` 路径常量 |

### 无需改动

| 文件 | 原因 |
|------|------|
| `Services/ProjectContext.cs` | `Clear()` 已足够完整 |
| `Services/ProjectWatcherService.cs` | 已有 `IsLoaded` 轮询自适应机制，无需改动 |

### 新增 DTO

`ProjectSummary`（放 `Dtos/` 目录）：
- `Name`, `FolderPath`, `CreatedAt`, `UpdatedAt`, `SchemeCount`, `ActiveScheme`, `Version`, `IsValid`, `ErrorMessage`

---

## 3. 各方法的关键逻辑

### `ProjectService.ScanProjects()`

- 遍历 `DefaultProjectsRoot` 下的子目录
- 每个目录只读 `project.json`（轻量），不加载 baseline/computed
- 解析失败的标记 `IsValid = false` + `ErrorMessage`

### `ProjectService.OpenFolder(folderPath)`

复用链：验证 → `EnsureProjectAssets()` → `EnsureComputedData()` → `EnsureZonesInitializedFromComputed()`（如缺失）→ `CreateZoneDirectories()`

需新增的验证：
- 路径必须在 `DefaultProjectsRoot` 下（防路径穿越）
- `project.json` 存在且可解析
- `baseline/` 目录存在

### `ProjectService.DeleteProject(projectName)`

- 路径穿越检查（`Path.GetFullPath` 比较）
- `Directory.Delete(path, recursive: true)`

### `ProjectController.CloseProject()`

- 调用 `GitWorktreeService.HasUncommittedChanges()` 检测未保存变更
- 无变更或 `force=true` 时调用 `ProjectContext.Clear()`
- Watcher 自动停止（无需手动处理）

### `Program.cs` 空启动

- 移除 176-238 行的自动加载代码块
- `ConversationLogger.Initialize` 延迟到 `OpenFolder` 端点
- Worktree 清理延迟到 `OpenFolder` 端点

---

## 4. DI 注册

`Program.cs` 服务注册区域新增：
```csharp
builder.Services.AddSingleton<RecentProjectsService>();
```

---

## 5. 实施步骤

按依赖顺序：

| 步骤 | 内容 | 依赖 |
|------|------|------|
| 1 | ConfigService 增加 `GetRecentProjectsPath()` | 无 |
| 2 | 新建 RecentProjectsService + DI 注册 | Step 1 |
| 3 | ProjectService 增加三个方法 + ProjectSummary DTO | 无 |
| 4 | ProjectController 增加 5 个端点 | Step 2, 3 |
| 5 | Program.cs 改为空启动 | Step 4 |
| 6 | 编译验证 | Step 5 |

---

## 6. 验证清单

| 验证项 | 方法 |
|--------|------|
| 项目扫描 | Swagger `GET /api/project/list`，确认列出 Projects/ 下所有项目 |
| 打开项目 | Swagger `POST /api/project/open-folder`，确认 `GET /api/project` 返回数据 |
| 关闭项目 | `POST /api/project/close`，确认 `GET /api/project/status` 显示 `isLoaded: false` |
| 删除项目 | `DELETE /api/project/{name}`，确认文件夹被删除 |
| 最近记录 | 打开两个项目后 `GET /api/project/recent`，确认顺序正确 |
| 导入集成 | `POST /api/project/upload` 导入 .bcp，确认最近记录自动更新 |
| 空启动 | 重启 Server，确认不自动加载项目 |
| 异常项目 | Projects/ 下放无 project.json 的文件夹，确认 list 返回 `isValid: false` |
| 安全检查 | 尝试删除当前打开的项目，确认返回 400 |
