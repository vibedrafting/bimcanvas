# BIMCanvas.Server

> 统一后端服务 - 系统的状态中心与通信中枢

**运行时**: .NET 8.0
**数据模型版本**: v3.4
**状态**: 🔶 v3.4 可视化 Diff + 选择性合并已完成，遗留服务待迁移

---

## 0. 快速启动

### Development（Windows / 本机开发态）

```bash
cd BIMCanvas
dotnet run --project BIMCanvas.Server
```

#### 启动行为

1. 启动 HTTP 服务器（默认首选 `http://localhost:5000`，若端口被外部进程占用则顺序避让）
2. 检测 Python / Agent / CCR 依赖并按需安装
3. 自动初始化 `<BIMCANVAS_HOME>/` 下的全局配置模板（Server + Agent）
4. Development 模式下额外初始化 `config.dev.local.json` / `ccr_config.dev.local.json`，并仅在运行时配置首次创建时将其作为初始化种子导入
5. 自动启动 Agent 服务；若启用 CCR，则同时启动 CCR 网关
6. 自动查找并启动 Web 开发服务器（BIMCanvas.Web）
7. 等待 Web 服务就绪后打开浏览器
8. **v3.0**：通过 URL 参数 `?project={项目路径}` 加载项目

### Production / Docker

当前推荐运行方式：

```bash
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.local.yml up -d --build instance1
```

#### 启动行为

1. 以 `ASPNETCORE_ENVIRONMENT=Production` 启动发布版 `BIMCanvas.Server.dll`
2. Server 直接托管 `BIMCanvas.Web/dist`，默认首选对外提供 `http://localhost:5000`，若端口被外部进程占用则顺序避让
3. Docker 推荐通过内部网络连接独立 Agent 容器；Development 模式下仍可自动拉起本地 Agent
4. 不自动打开浏览器
5. 全局配置与项目数据统一落到 `BIMCANVAS_HOME`（容器默认 `/data`）
6. 若挂载目录里仍是旧版“Server 内嵌 Agent”配置，容器启动时会自动迁移为 Compose 指定的外部 Agent 拓扑

### 关键配置项

| 配置 | 位置 | 默认值 | 说明 |
|------|------|--------|------|
| API 端口 | `launchSettings.json` | `5000` | REST API 首选端口，运行时若被外部进程占用会顺序避让 |
| Web 开发端口 | 自动检测 | `5173` | Development 模式下 Vite dev server 首选端口，运行时会顺序避让 |
| 项目目录 | `<BIMCANVAS_HOME>/Projects/` | Windows: `Documents/BIMCanvas/Projects/` | 项目解压目录 |
| CCR 配置 | `<BIMCANVAS_HOME>/server_config.json` | `enabled=false` | 网关启用、端口 |
| CCR Router 配置 | `<BIMCANVAS_HOME>/ccr_config.json` | 自动初始化 | 供应商 / 模型路由映射 |
| 开发态直连种子 | `<BIMCANVAS_HOME>/config.dev.local.json` | Development 自动生成 | 本地私有 `baseUrl/apiKey` 初始化种子 |
| 开发态 CCR 种子 | `<BIMCANVAS_HOME>/ccr_config.dev.local.json` | Development 自动生成 | 本地私有 `Providers/Router` 初始化种子 |
| `BIMCANVAS_HOME` | 环境变量 | Windows: `Documents/BIMCanvas`；Docker: `/data` | 全局配置、项目、截图的根目录 |
| `BIMCANVAS_WEB_DIST` | 环境变量 | Docker: `/app/BIMCanvas.Web/dist` | Production 模式静态托管目录 |
| `BIMCANVAS_PYTHON_COMMAND` | 环境变量 | Docker: `python3` | Server 拉起本地 Agent 时使用的 Python |
| `ASPNETCORE_ENVIRONMENT` | 环境变量 | `Development` / `Production` | 控制是否启动 dev server、是否自动打开浏览器 |

### 实例配置

阶段三完成后，Server 已把统一配置能力视为正式功能：

- `GET /api/settings`：聚合读取 `server/web/agent/ccr` 四组实例配置
- `PUT /api/settings`：聚合写回四份实例配置 JSON，并返回哪些改动需要重启
- `POST /api/settings/restart`：触发实例优雅停机，由 Docker restart policy 接管重启
- `GET /api/web_config` / `POST /api/web_config`：保留兼容入口

其中：

- `web_config.json` 默认按热更新处理
- `config.json`、`server_config.json`、`ccr_config.json` 默认按“保存后需重启实例”处理
- `*.dev.local.json` 仅用于 Development 启动早期的首次初始化种子，不属于设置 UI 的回写目标
- `ccr_config.dev.local.json` 只负责在 `ccr_config.json` 首次创建时提供 provider/router 种子；是否启用 CCR 仍由 `server_config.json > ccr.enabled` 决定

### CCR 网关

Server 默认托管 CCR (Claude Code Router)，用于给 Agent SDK 提供统一的 Anthropic 风格入口，再转发到真实下游 provider。

- 模型路由由 `<BIMCANVAS_HOME>/ccr_config.json` 的 `Router` 字段配置（`default` / `think` / `background` / `longContext` 分别控制不同类型请求的供应商和模型）
- Web 对话默认模型统一由 `<BIMCANVAS_HOME>/web_config.json > defaultModel` 管理；Server 只负责网关连接与路由，不再持有默认模型
- 当 `ccr.enabled=false` 时，不启动 CCR，Agent 走直连模式（使用 `<BIMCANVAS_HOME>/config.json`）
- 切换供应商后需要重启 Server
- 如果 CCR 不可用，Server 与 Web 仍会启动，但 AI 请求会在运行时失败并输出明确日志
- 仓库模板中的 `ccr_config.json` 默认是安全空壳；Development 仅在 `ccr_config.json` 首次创建时通过 `ccr_config.dev.local.json` 注入 provider/router 种子，Production/Docker 应通过 `/data/ccr_config.json` 或设置 UI 维护正式 provider/router
- Docker 启动时，`CCR_API_KEY` / `CCR_API_BASE` 只会覆盖已存在 provider 条目；若模板仍为空，需要先通过 UI 或预置 `/data/ccr_config.json` 提供 provider 结构

### JSON 序列化

Server 使用 **Newtonsoft.Json**（与 BIMCanvas.Core 保持一致）：

```csharp
builder.Services.AddControllers()
    .AddNewtonsoftJson(options => {
        options.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
    });
```

> ⚠️ **重要**：不要改用 System.Text.Json，否则 Core 层的 `[JsonConverter]` 属性不会被识别，导致 Polygon2D 等类型序列化失败。

---

## 1. v3.0 架构变更

### 1.1 从"内存数据库"到"文件播放器"

v3.0 采用"文件驱动架构"，Server 从"内存数据库"模式转型为"文件播放器"模式：

| 模式 | v2.9 (旧) | v3.0 | v3.1 (新) |
|------|-----------|------|-----------|
| 数据存储 | 内存中的 `DesignDocument` | 磁盘上的项目文件夹 | 同 v3.0 |
| 状态来源 | Server 内存 | 文件系统 | 同 v3.0 |
| 变更同步 | 内存更新 → WebSocket | 文件写入 → FileWatcher | 同 v3.0 |
| 版本控制 | 外部 Git | ❌ 每个策略独立 Git | ✅ **单仓库 + 多分支 + Worktree** |
| 并行任务 | 不支持 | 不支持 | ✅ Git Worktree 物理隔离 |

### 1.2 新增服务

| 服务 | 文件 | 版本 | 职责 |
|------|------|------|------|
| `ManifestService` | `Services/ManifestService.cs` | v3.0 | `.manifest` 键值对文件读写 |
| `ComputedDataService` | `Services/ComputedDataService.cs` | v3.0 | 计算数据管理（禁区生成 + 验证） |
| `GitWorktreeService` | `Services/GitWorktreeService.cs` | v3.1 | Git 仓库 + 分支 + Worktree 管理 |
| `StrategyService` | `Services/StrategyService.cs` | v3.1 | 策略管理（分支模式） |
| `ProjectService` | `Services/ProjectService.cs` | v3.1 | 项目加载（含 Git 初始化） |
| `ProjectWatcherService` | `Services/ProjectWatcherService.cs` | v3.2 | 文件监听 + SignalR 推送（防抖 500ms） |
| `BranchLockManager` | `Services/Git/BranchLockManager.cs` | v3.3 | 分支互斥锁（多窗口并行） |
| `WorktreeMetadataService` | `Services/WorktreeMetadataService.cs` | v3.3 | Worktree 元数据读写（intent / baseBranch） |
| `BackgroundScreenshotService` | `Services/BackgroundScreenshotService.cs` | v3.3 | Playwright 无头截图（支持批量 + 视口自适应） |
| `ProjectSnapshotService` | `Services/ProjectSnapshotService.cs` | v3.3 | 项目数据快照读取（供截图服务使用） |
| `ChatAttachmentService` | `Services/ChatAttachmentService.cs` | v3.4 | 对话图片资源化落盘 + `_chat_attachments.json` manifest 管理 |
| `SchemeDataService` | `Services/SchemeDataService.cs` | v3.4 | 跨分支/Worktree 模块数据读写 |
| `MergeService` | `Services/MergeService.cs` | v3.4 | 分区级差异计算 + 选择性/覆盖合并 |
| `ProjectController` | `Controllers/ProjectController.cs` | v3.0 | `/api/project` 端点 |

### 1.3 遗留服务（待迁移）

以下服务仍使用 v2.9 数据结构，已重命名为 `.legacy`：

| 文件 | 当前状态 | 迁移方向 |
|------|----------|----------|
| `CanvasStateManager.cs.legacy` | 使用 `DesignDocument` | 改为读取项目文件夹 |
| `ZoneCalculator.cs.legacy` | 使用 `DesignDocument` | 改为读取 baseline/ 和 schemes/ |
| `CanvasController.cs.legacy` | 使用 `DesignDocument` | 改为使用 ProjectService |

> **当前状态**：遗留服务功能已被新服务完全替代，暂无迁移计划，保留仅供参考。

---

## 2. 项目结构

```
BIMCanvas.Server/
├── Program.cs                    ✅ 入口（REST Host + 自动启动 Web）
├── Properties/
│   └── launchSettings.json       ✅ 启动配置（端口 5000）
│
├── Controllers/                  【REST API】
│   ├── ProjectController.cs      ✅【v3.0】项目数据聚合 API
│   ├── GitController.cs          ✅【v3.1】Git 分支管理 API
│   ├── ValidationController.cs   ✅【v3.2】布局全量验证（布局编译器）
│   ├── WindowsController.cs      ✅【v3.3】多窗口分支锁 + Worktree 隔离
│   ├── WorktreeController.cs     ✅【v3.3】Worktree 元数据查询 API
│   ├── BackgroundScreenshotController.cs ✅ 后台截图 API（Playwright）
│   ├── ChatAttachmentsController.cs ✅ 对话附件上传 / 预览 / 删除 / 提交
│   ├── MergeController.cs        ✅【v3.4】分区级差异对比 + 选择性合并
│   ├── ModulesController.cs      ✅ 跨分支模块数据读写 API
│   ├── SchemeController.cs       ✅ 方案管理 API
│   ├── SemanticPlanController.cs ✅【v3.5】语义方案 + 参考分析 API
│   ├── NotificationController.cs ✅ 通知 API
│   ├── WebConfigController.cs    ✅ Web 配置 API
│   └── CanvasController.cs.legacy   ⚠️ 遗留 v2.9 API，待迁移
│
├── Dtos/                         【v3.0 新增】数据传输对象
│   ├── ProjectData.cs            ✅ v3.0 项目数据 DTO
│   └── GitBranchInfo.cs          ✅【v3.1 新增】Git 分支信息 DTO
│
├── Services/                     【业务服务】
│   ├── ManifestService.cs        ✅【v3.0】.manifest 文件读写
│   ├── ComputedDataService.cs    ✅【v3.0】计算数据管理
│   ├── GitWorktreeService.cs     ✅【v3.1】Git 仓库 + Worktree 管理
│   ├── StrategyService.cs        ✅【v3.1】策略分支管理
│   ├── ProjectService.cs         ✅【v3.1】项目加载 + Git 初始化
│   ├── ProjectContext.cs         ✅ 单项目模式上下文（含多窗口 Worktree 映射）
│   ├── ProjectWatcherService.cs  ✅【v3.2】文件监听 + SignalR 推送
│   ├── ProjectSnapshotService.cs ✅ 项目快照读取（供截图服务使用）
│   ├── ChatAttachmentService.cs  ✅ 对话附件资源化 + manifest 管理
│   ├── SchemeDataService.cs      ✅ 跨分支/Worktree 模块数据读写
│   ├── BackgroundScreenshotService.cs ✅ 后台截图（Playwright 无头浏览器）
│   ├── MergeService.cs           ✅【v3.4】分区级差异计算 + 选择性/覆盖合并
│   ├── WorktreeMetadataService.cs ✅【v3.3】Worktree 元数据管理
│   ├── IWorktreeMetadataServiceFactory.cs ✅ 元数据服务工厂接口
│   ├── PlacementService.cs       ✅ 布置逻辑
│   ├── ModuleLibraryService.cs   ✅ 模块库服务
│   ├── ConfigService.cs          ✅ 配置服务
│   ├── RoomTypeTagMappingService.cs ✅ 房间类型标签映射
│   ├── Git/                      【Git 子服务】
│   │   └── BranchLockManager.cs  ✅【v3.3】分支互斥锁（多窗口并行）
│   ├── CanvasStateManager.cs.legacy  ⚠️ 遗留，待迁移
│   └── ZoneCalculator.cs.legacy      ⚠️ 遗留，待迁移
│
├── Hubs/                         【SignalR Hub】✅ v3.2 已完成
│   └── CanvasHub.cs              ✅ 画布实时通信（窗口注册 + 分支锁 + 状态推送）
│
└── （MCP 工具由 BIMCanvas.Agent 端提供，Server 通过 REST API 响应 Agent 的工具调用）
```

### 对话附件资源化

阶段 2 起，AI Command Center 的图片输入不再直接把 base64 转发给 Agent，而是统一先落到当前项目目录：

```text
{projectPath}/screenshots/
    chat_{windowId}_{attachmentId}.png|jpg|webp
    _chat_attachments.json
```

关键约定：

- 普通截图与聊天附件混放在 `screenshots/` 根目录
- 聊天附件通过 `chat_` 文件名前缀和 `_chat_attachments.json` 区分
- Server 负责上传、预览、删除、提交四个动作：
  - `POST /api/chat/attachments`
  - `GET /api/chat/attachments/{attachmentId}/content`
  - `DELETE /api/chat/attachments/{attachmentId}`
  - `POST /api/chat/attachments/commit`
- Agent 只接收 `attachmentIds`，再按 manifest 解析稳定本地路径

---

## 3. v3.1 Git Worktree 架构

### 3.1 架构概述

v3.1 采用"单仓库 + 多分支 + Worktree"架构，实现：
- **版本控制**：项目根目录是单一 Git 仓库
- **策略管理**：不同策略通过 Git 分支表示（替代独立目录）
- **并行任务**：通过 Git Worktree 实现物理隔离的并发工作

```
项目目录/
├── .git/                    # 单一 Git 仓库
├── .gitignore               # 忽略 .worktrees/ 等
├── project.json
├── baseline/                # 建筑基础（只读）
├── computed/                # 计算缓存
├── context/                 # 设计需求
├── schemes/
│   └── active/              # 当前激活策略的工作目录
└── .worktrees/              # Git Worktree 临时目录（并行任务）
    ├── ai-storage/          # → feat/ai-storage-xxx 分支
    └── ai-flow/             # → feat/ai-flow-xxx 分支
```

### 3.2 分支命名约定

| 分支模式 | 示例 | 说明 |
|----------|------|------|
| `main` | `main` | 用户当前接受的状态 |
| `scheme/{id}` | `scheme/s1_Default` | 保存的设计方案 |
| `feat/ai-{jobId}-{name}` | `feat/ai-storage-MaxStorage` | AI 临时工作分支 |

### 3.3 GitWorktreeService API

```csharp
// 仓库管理
bool IsGitRepository(string projectPath);
bool InitializeRepository(string projectPath);

// 分支管理
string GetCurrentBranch(string projectPath);
List<string> GetAllBranches(string projectPath);
void CreateBranch(string projectPath, string branchName);
void CheckoutBranch(string projectPath, string branchName);
MergeResult MergeBranch(string projectPath, string branchName);

// Worktree 管理（并行任务核心）
string CreateWorktree(string projectPath, string worktreeName, string branchName);
void RemoveWorktree(string projectPath, string worktreeName);
List<WorktreeInfo> GetWorktrees(string projectPath);

// AI 任务支持
string CreateAiJobWorktree(string projectPath, string jobId, string strategyName);
void CompleteAiJob(string projectPath, string jobId, string commitMessage);
MergeResult AcceptAiJob(string projectPath, string jobId);
```

### 3.4 并行策略生成示例

```csharp
// 场景 A：策略分叉 - 同时生成三个方案
var strategies = new List<ParallelStrategyRequest>
{
    new() { Name = "极致收纳", Approach = StrategyApproach.StorageFirst },
    new() { Name = "动线优先", Approach = StrategyApproach.CirculationFirst },
    new() { Name = "极简留白", Approach = StrategyApproach.MinimalistFirst }
};

// 创建三个并行 Worktree
var worktrees = strategyService.CreateParallelStrategies(projectPath, strategies);
// worktrees = {
//   "极致收纳": "C:/.../project/.worktrees/ai-极致收纳",
//   "动线优先": "C:/.../project/.worktrees/ai-动线优先",
//   "极简留白": "C:/.../project/.worktrees/ai-极简留白"
// }

// 三个 AI 实例可以同时在各自 worktree 中工作...

// 用户选择后，合并到 main
var result = strategyService.AcceptParallelStrategy(projectPath, "动线优先");
```

---

## 4. v3.0 项目加载流程

### 4.1 ProjectService.LoadProject()

完整的项目加载流程：

```
输入：.bcp 压缩包路径

1. 解压 .bcp
   └─ 目标：用户文档/BIMCanvas/Projects/{名称}_{时间戳}/
   └─ 兼容 ZIP 条目中的 Windows `\` 路径分隔符，导入时统一规范化为目录结构

2. 计算 Baseline 哈希
   └─ 读取 architecture.json + rooms.json + openings.json
   └─ 计算 SHA256 联合哈希
   └─ 写入 baseline/baseline.manifest

3. 创建 Context 目录
   └─ 创建 context/requirements.md 模板

4. 创建默认策略
   └─ 创建 schemes/s1_Default/ 目录
   └─ 写入 strategy.json（含 lastValidatedBaselineHash）
   └─ 写入 zones.json（空数组）
   └─ 写入 finishes.json（空数组）
   └─ 写入 modules.json（空数组）

5. 更新 project.json
   └─ 添加 Schemes 引用
   └─ 设置 activeSchemeId

6. 生成 Computed 数据
   └─ 计算门扇禁区 → exclusions.json
   └─ 写入 computed/computed.manifest

输出：项目文件夹路径
```

### 4.2 门扇禁区计算

```
对每扇门：
1. 读取 openings.json 中的门数据
2. 获取门宽度 = |line.end - line.start|
3. 计算禁区矩形：
   - 尺寸：doorWidth × doorWidth
   - 偏移：facingDirection × doorWidth
   - 4 个顶点：门线起点、门线终点、终点+偏移、起点+偏移
4. 生成 Zone 对象（Type = Exclusion）
5. 写入 computed/exclusions.json
```

### 4.3 目录结构（完整）

```
C:\Users\{username}\Documents\BIMCanvas\Projects\
└── demo_1_20251225_143025/
    ├── project.json                    # Revit 导出，Server 更新 Schemes 引用
    ├── baseline/                       # 只读，来自 Revit
    │   ├── metadata.json
    │   ├── architecture.json
    │   ├── openings.json
    │   ├── rooms.json
    │   ├── location_lines.json
    │   └── baseline.manifest           # Server 生成的哈希文件
    ├── context/                        # Server 创建
    │   └── requirements.md             # 设计需求模板
    ├── schemes/                        # Server 创建
    │   └── s1_Default/                 # 默认策略
    │       ├── strategy.json           # 策略元数据
    │       ├── zones.json              # 功能分区（空）
    │       ├── finishes.json           # 完成面（空）
    │       └── modules.json            # 家具模块（空）
    └── computed/                       # 计算缓存
        ├── exclusions.json             # 门扇禁区数据
        └── computed.manifest           # 哈希验证文件
```

---

## 5. REST API

### 5.1 v3.0 新增端点

| 端点 | 方法 | 功能 | 状态 |
|------|------|------|------|
| `/api/project` | GET | 聚合项目数据返回 `ProjectData` | ✅ |

**`GET /api/project?path={项目路径}`**

聚合以下文件返回 `ProjectData` DTO：

```
project.json           → project
baseline/*.json        → baseline
schemes/{activeId}/*.json → activeScheme
computed/*.json        → computed
```

#### modules.json 的 `facing` 规范

- Server 对外统一返回对象形态：`{ "value": [x, y] | null, "semantic": string | null }`
- `GET /api/project`、截图、快照、Merge 等常规读取路径只消费 `facing.value`
- `POST /api/project/save` 只接受 Web 规范输入：`facing.value` 必须有效，`facing.semantic` 必须显式为 `null`
- `validate_layout(zoneIds?)` 是唯一会消费 `facing.semantic` 的入口：它会先把有效语义方向转换成 `value`，再把 `semantic` 清空为 `null`，然后回写目标 `modules.json`
- 方向归一化仍沿用 `FileSystemWatcher + SignalR` 被动刷新机制，因此 AI 直写文件后可能出现两次 reload：第一次来自 AI 写入，第二次来自 `validate_layout` 归一化回写

#### semantic_plan / reference_analysis API（v3.5）

| 端点 | 方法 | 功能 |
|------|------|------|
| `/api/semantic-plan/save` | POST | 保存 `v0.1/v0.2/v0.3` 语义方案，支持可选 `referenceAnalysisVersion` |
| `/api/semantic-plan/{zoneId}` | GET | 读取当前生效的 `v0.3` 语义合同 |
| `/api/semantic-plan/save-reference-analysis` | POST | 追加保存独立的 `reference_analysis.json` 完整版本快照 |
| `/api/semantic-plan/{zoneId}/reference-analysis` | GET | 读取最新或指定版本的参考分析 |

关键约定：

- `semantic_plan.json` 只保存语义方案版本数组
- `reference_analysis.json` 独立保存参考分析版本数组
- `reference_analysis` 的每个版本都是完整快照，planning 默认读取最新定稿版本
- 新流程统一写 `planType="derived"`
- 旧 `planType=reference` 且缺少 `v0.3` 的数据会被视为 legacy，需要重新规划

### 5.2 Git 分支管理 API

| 端点 | 方法 | 功能 | 状态 |
|------|------|------|------|
| `/api/git/branches` | GET | 获取分支列表 | ✅ |
| `/api/git/checkout` | POST | 切换/新建分支（支持原子操作） | ✅ |
| `/api/git/commit` | POST | 提交当前更改 | ✅ |
| `/api/git/discard` | POST | 放弃所有未提交更改 | ✅ |
| `/api/git/status` | GET | 获取工作区状态 | ✅ |
| `/api/git/current` | GET | 获取当前分支 | ✅ |

#### `GET /api/git/branches`

返回项目的所有 Git 分支列表，包含最新提交信息：

```json
[
  {
    "id": "main",
    "name": "main",
    "isCurrent": true,
    "commit": {
      "hash": "a62ff49",
      "message": "功能：重构AICommandCenter",
      "time": "2 hours ago",
      "author": "张三"
    }
  },
  {
    "id": "feature/new-layout",
    "name": "feature/new-layout",
    "isCurrent": false,
    "commit": { ... }
  }
]
```

#### `POST /api/git/checkout`

切换分支或新建分支。支持三种模式处理未提交更改：

**请求体**：
```json
{
  "branchName": "feature/new-layout",
  "createIfNotExist": false,      // 分支不存在时自动创建
  "commitBeforeCheckout": false,  // 切换前自动提交更改
  "discardBeforeCheckout": false, // 切换前放弃更改（原子操作）
  "commitMessage": "..."          // commitBeforeCheckout 时的提交信息
}
```

**更改处理优先级**：`discardBeforeCheckout` > `commitBeforeCheckout` > 返回 409 冲突

**响应**（成功 200）：
```json
{
  "success": true,
  "currentBranch": "feature/new-layout",
  "created": true   // 是否新建了分支
}
```

**错误响应**：
- `404` - 分支不存在且 `createIfNotExist=false`
- `409` - 存在未提交的更改，需指定处理方式

#### `POST /api/git/commit`

提交当前所有更改：

**请求体**：
```json
{
  "message": "功能描述"  // 可选，为空则自动生成时间戳
}
```

#### `POST /api/git/discard`

放弃所有未提交的更改（执行 `git checkout .` + `git clean -fd`）。

#### `GET /api/git/status`

返回工作区状态：

```json
{
  "isLoaded": true,
  "isGitRepo": true,
  "hasUncommittedChanges": false,
  "currentBranch": "main"
}
```

#### `GET /api/git/current`

返回当前分支名：

```json
{
  "branch": "main"
}
```

### 5.3 布局验证 API

| 端点 | 方法 | 功能 | 状态 |
|------|------|------|------|
| `/api/validation/layout` | POST | 全量验证布局合法性（布局编译器） | ✅ v3.2 |

验证三类错误：模块超出设计区域、模块与墙体/柱子/禁区重叠、模块间互相重叠。

### 5.4 窗口管理 API

| 端点 | 方法 | 功能 | 状态 |
|------|------|------|------|
| `/api/windows/locks` | GET | 获取所有分支锁 | ✅ v3.3 |
| `/api/windows/locks/{branch}` | GET | 获取指定分支锁信息 | ✅ v3.3 |
| `/api/windows/lock` | POST | 申请分支锁 | ✅ v3.3 |
| `/api/windows/lock` | DELETE | 释放分支锁 | ✅ v3.3 |
| `/api/windows/locks/window/{windowId}` | GET | 获取窗口锁定的分支 | ✅ v3.3 |
| `/api/windows/locks/window/{windowId}` | DELETE | 释放窗口所有锁 | ✅ v3.3 |
| `/api/windows/available/{branch}` | GET | 检查分支可用性 | ✅ v3.3 |
| `/api/windows/activate` | POST | 激活窗口（切换 Worktree） | ✅ v3.3 |
| `/api/windows/register-worktree` | POST | 注册窗口 Worktree | ✅ v3.3 |
| `/api/windows/worktree/{windowId}` | DELETE | 注销窗口 Worktree | ✅ v3.3 |
| `/api/windows/active` | GET | 获取当前激活窗口信息 | ✅ v3.3 |

### 5.5 Worktree 元数据 API

| 端点 | 方法 | 功能 | 状态 |
|------|------|------|------|
| `/api/worktree/metadata` | GET | 获取完整 Worktree 元数据 | ✅ v3.3 |
| `/api/worktree/batch-resolve` | POST | 批量解析 worktree 名称到分支名称 | ✅ v3.3 |

### 5.6 截图 API

| 端点 | 方法 | 功能 | 状态 |
|------|------|------|------|
| `/api/screenshot/render` | POST | 后台截图（Playwright 无头渲染） | ✅ |
| `/api/screenshot/render-batch` | POST | 批量截图（多视口并行） | ✅ |

### 5.7 合并 API

| 端点 | 方法 | 功能 | 状态 |
|------|------|------|------|
| `/api/merge/diff` | GET | 获取分区级差异 | ✅ v3.4 |
| `/api/merge/selective` | POST | 执行选择性合并 | ✅ v3.4 |
| `/api/merge/overwrite` | POST | 执行覆盖合并 | ✅ v3.4 |
| `/api/merge/branches` | GET | 获取可合并分支列表 | ✅ v3.4 |

### 5.8 通知与配置 API

| 端点 | 方法 | 功能 | 状态 |
|------|------|------|------|
| `/api/notification/agent` | POST | Agent 通知推送（通过 SignalR 转发给 Web） | ✅ |
| `/api/notification/data-changed` | POST | 数据变更通知 | ✅ |
| `/api/settings` | GET | 聚合读取 `server/web/agent/ccr` 四组实例配置 | ✅ |
| `/api/settings` | PUT | 聚合写回四份实例配置 JSON，并返回重启提示 | ✅ |
| `/api/settings/restart` | POST | 触发实例优雅停机，由 Docker restart policy 接管重启 | ✅ |
| `/api/web_config` | GET | 获取 Web 配置 | ✅ |
| `/api/web_config` | POST | 更新 Web 配置 | ✅ |
| `/api/modules/library` | GET | 获取模块库列表 | ✅ |
| `/api/modules/svg/{moduleId}` | GET | 获取模块 SVG 缩略图 | ✅ |
| `/api/scheme` | GET | 获取方案数据 | ✅ |

### 5.9 Git 扩展 API（v3.3+）

| 端点 | 方法 | 功能 | 状态 |
|------|------|------|------|
| `/api/git/worktrees` | GET | 获取 Worktree 列表 | ✅ v3.3 |
| `/api/git/ai-job` | POST | 创建 AI Job（Git Worktree 隔离） | ✅ v3.3 |
| `/api/git/ai-job/{name}/complete` | POST | 完成 AI Job | ✅ v3.3 |

### 5.10 遗留端点（待迁移）

| 端点 | 方法 | 功能 | 状态 |
|------|------|------|------|
| `/health` | GET | 健康检查 | ✅ |
| `/api/canvas` | GET | 获取所有画布 ID | ⚠️ 遗留 |
| `/api/canvas/{id}` | GET | 获取指定画布 | ⚠️ 遗留 |
| `/api/canvas` | POST | 创建/更新画布 | ⚠️ 遗留 |
| `/api/canvas/load` | POST | 加载并处理画布 | ⚠️ 遗留 |

---

## 6. v3.0 数据传输对象

### ProjectData

```csharp
public class ProjectData
{
    public ProjectInfo Project { get; set; }
    public BaselineData Baseline { get; set; }
    public SchemeData ActiveScheme { get; set; }
    public ComputedData Computed { get; set; }
}

public class BaselineData
{
    public BaselineManifest Metadata { get; set; }
    public List<Wall> Walls { get; set; }
    public List<Column> Columns { get; set; }
    public List<Opening> Openings { get; set; }
    public List<Room> Rooms { get; set; }
    public List<LocationLine> LocationLines { get; set; }
}

public class SchemeData
{
    public Strategy Strategy { get; set; }
    public List<Zone> Zones { get; set; }
    public List<FinishSegment> Finishes { get; set; }
    public List<Module> Modules { get; set; }
}

public class ComputedData
{
    public List<Zone> RoomZones { get; set; }   // Type = ZoneType.Room
    public List<Zone> Exclusions { get; set; }  // Type = ZoneType.Exclusion
}
```

---

## 7. .manifest 文件格式

`.manifest` 文件使用简单的键值对格式（非 JSON）：

```
# Generated at 2025-12-25T14:30:25
version=1
generatedAt=2025-12-25T14:30:25+08:00
baselineHash=sha256:abc123def456...
```

### ManifestService API

```csharp
public class ManifestService
{
    // 读取 .manifest 文件
    public Dictionary<string, string> ReadManifest(string manifestPath);

    // 写入 .manifest 文件
    public void WriteManifest(string manifestPath, Dictionary<string, string> values);

    // 从 baseline/ 读取 baselineHash
    public string? GetBaselineHash(string baselinePath);

    // 写入 baseline.manifest
    public void WriteBaselineManifest(string baselinePath, string baselineHash);
}
```

---

## 8. 角色定位

### 8.1 组件角色对比

| 组件 | 比喻 | 核心职责 |
|------|------|----------|
| **BIMCanvas.Server** | **心脏 + 神经系统** | 状态管理、几何计算、通信中枢、事件分发 |
| **BIMCanvas.Agent** | **大脑** | 智能决策、理解意图、规划布置方案 |
| **BIMCanvas.Core** | **骨骼** | 数据结构、基础算法、类型定义 |
| **BIMCanvas.Web** | **皮肤 + 眼睛** | 渲染展示、用户交互 |
| **BIMCanvas.Revit** | **手臂** | 从 Revit 抓取数据、回写 Revit |

### 8.2 Server vs Agent 职责边界

| 职责 | Server | Agent |
|------|--------|-------|
| **状态持有** | ✅ 管理项目文件夹 | ❌ 无状态 |
| **几何计算** | ✅ Zone/禁区/完成面计算 | ❌ 不做几何 |
| **智能决策** | ❌ 不决定"放哪里" | ✅ 规划布置方案 |
| **通信中枢** | ✅ 连接所有组件 | ❌ 只通过 MCP/SSE |
| **约束验证** | ✅ 边界/碰撞检查 | ❌ 依赖 Server |

**关键原则**：
- **Server 不做决策**：它不决定"沙发放哪里"，只执行验证和计算
- **Agent 不持有状态**：它只发指令，状态由 Server 管理
- **Server 是通信中枢**：所有组件通过它交换数据（REST/WebSocket/SSE/MCP）

---

## 9. 通信架构

```
                    ┌─────────────────────────────────┐
                    │        BIMCanvas.Server         │
                    │         （通信中枢）             │
                    └─────────────────────────────────┘
                              │
        ┌─────────────────────┼─────────────────────┐
        │                     │                     │
        ▼                     ▼                     ▼
┌───────────────┐    ┌───────────────┐    ┌───────────────┐
│   REST API    │    │   WebSocket   │    │     SSE       │
│   /api/...    │    │  SignalR Hub  │    │  /api/events  │
├───────────────┤    ├───────────────┤    ├───────────────┤
│ • 项目加载    │    │ • Web 实时    │    │ • Agent 事件  │
│ • Web 查询    │    │ • 状态推送    │    │ • 单向推送    │
│ • 导出/截图   │    │ • 双向通信    │    │               │
└───────────────┘    └───────────────┘    └───────────────┘
        │                     │                     │
        ▼                     ▼                     ▼
    Revit 插件            Web 前端            PlacementAgent
```

---

## 10. 开发状态

### 10.1 已完成

| 功能 | 文件 | 状态 |
|------|------|------|
| REST Host | Program.cs | ✅ |
| 健康检查 | /health | ✅ |
| .manifest 读写 | ManifestService.cs | ✅ v3.0 |
| 计算数据管理 | ComputedDataService.cs | ✅ v3.0 |
| 策略目录管理 | StrategyService.cs | ✅ v3.0 |
| 项目加载流程 | ProjectService.cs | ✅ v3.0 |
| 项目数据 API | ProjectController.cs | ✅ v3.0 |
| 项目数据 DTO | Dtos/ProjectData.cs | ✅ v3.0 |
| Git 分支管理 API | GitController.cs | ✅ v3.1 |
| Git 分支信息 DTO | Dtos/GitBranchInfo.cs | ✅ v3.1 |
| SignalR Hub | Hubs/CanvasHub.cs | ✅ v3.2 |
| 文件监听 | ProjectWatcherService.cs | ✅ v3.2 |
| 多窗口分支锁 | Git/BranchLockManager.cs | ✅ v3.3 |
| Worktree 元数据 | WorktreeMetadataService.cs | ✅ v3.3 |
| 窗口管理 API | WindowsController.cs | ✅ v3.3 |
| Worktree 元数据 API | WorktreeController.cs | ✅ v3.3 |
| 后台截图服务 | BackgroundScreenshotService.cs | ✅ v3.3 |
| 后台截图 API | BackgroundScreenshotController.cs | ✅ v3.3 |
| 项目快照 | ProjectSnapshotService.cs | ✅ v3.3 |
| 布局验证 API | ValidationController.cs | ✅ v3.2 |
| 跨分支数据读写 | SchemeDataService.cs | ✅ v3.4 |
| 分区级差异对比 | MergeService.cs | ✅ v3.4 |
| 合并 API | MergeController.cs | ✅ v3.4 |

### 10.2 待开发

| 功能 | 文件 | 状态 |
|------|------|------|
| 遗留服务迁移 | CanvasStateManager.cs.legacy | ⬜ |
| 遗留服务迁移 | ZoneCalculator.cs.legacy | ⬜ |
| 遗留服务迁移 | CanvasController.cs.legacy | ⬜ |
| SSE 端点 | Controllers/EventsController.cs | ⬜ |
| ~~MCP 工具~~ | ~~McpTools/*.cs~~ | 已取消（由 Agent 端提供） |

### 10.3 版本演进

```
v3.0: File-Driven Architecture（文件播放器模式）
v3.1: + Git Worktree 基础架构（单仓库 + 多分支 + Worktree）
v3.2: + SignalR 实时通信 + 文件监听 + 布局验证
v3.3: + 多窗口并行（BranchLockManager + WorktreeMetadata + 截图服务）
v3.4: + 可视化 Diff（MergeService + 选择性/覆盖合并）← 当前
```

---

## 11. 相关文档

| 文档 | 路径 | 内容 |
|------|------|------|
| 系统架构 | `docs/Architecture.md` | 整体架构、模块设计 |
| 文件驱动架构 | `docs/FileDrivenArchitecture.md` | "文件播放器"模式 |
| 执行流程 | `docs/Workflows.md` | 端到端流程详解 |
| JSON Schema v3 | `docs/Schema-JSON-v3.md` | v3.0 数据模型定义 |
| 升级进度 | `plans/V3_Upgrade_Progress_Report.md` | v3.0 升级进度 |
| 升级计划 | `plans/V3_Architecture_Upgrade_Plan.md` | 完整升级计划 |
| Core 层 | `BIMCanvas.Core/README.md` | 数据模型 + 算法 |
