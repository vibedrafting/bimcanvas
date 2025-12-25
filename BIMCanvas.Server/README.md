# BIMCanvas.Server

> 统一后端服务 - 系统的状态中心与通信中枢

**运行时**: .NET 8.0
**数据模型版本**: v3.0
**状态**: 🔶 v3.0 项目加载已完成，遗留服务待迁移

---

## 0. 快速启动

### 启动命令

```bash
cd BIMCanvas
dotnet run --project BIMCanvas.Server
```

### 启动行为

1. 启动 HTTP 服务器（http://localhost:5000）
2. 自动查找并启动 Web 开发服务器（BIMCanvas.Web）
3. 等待 Web 服务就绪后打开浏览器
4. **v3.0**：通过 URL 参数 `?project={项目路径}` 加载项目

### 配置项

| 配置 | 位置 | 默认值 | 说明 |
|------|------|--------|------|
| API 端口 | `launchSettings.json` | `5000` | REST API 服务端口 |
| Web 端口 | 自动检测 | `5173` | Vite 开发服务器端口 |
| 项目目录 | 用户文档 | `Documents/BIMCanvas/Projects/` | v3.0 项目解压目录 |

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

| 模式 | v2.9 (旧) | v3.0 (新) |
|------|-----------|-----------|
| 数据存储 | 内存中的 `DesignDocument` | 磁盘上的项目文件夹 |
| 状态来源 | Server 内存 | 文件系统 |
| 变更同步 | 内存更新 → WebSocket 推送 | 文件写入 → FileWatcher → 推送 |
| 版本控制 | 外部 Git | 每个策略是独立 Git 仓库 |

### 1.2 新增服务

| 服务 | 文件 | 职责 |
|------|------|------|
| `ManifestService` | `Services/ManifestService.cs` | `.manifest` 键值对文件读写 |
| `ComputedDataService` | `Services/ComputedDataService.cs` | 计算数据管理（禁区生成 + 验证） |
| `StrategyService` | `Services/StrategyService.cs` | 策略目录管理（创建 + 查询） |
| `ProjectService` | `Services/ProjectService.cs` | 项目加载完整流程 |
| `ProjectController` | `Controllers/ProjectController.cs` | `/api/project` 端点 |

### 1.3 遗留服务（待迁移）

以下服务仍使用 v2.9 数据结构，已重命名为 `.legacy`：

| 文件 | 当前状态 | 迁移方向 |
|------|----------|----------|
| `CanvasStateManager.cs.legacy` | 使用 `DesignDocument` | 改为读取项目文件夹 |
| `ZoneCalculator.cs.legacy` | 使用 `DesignDocument` | 改为读取 baseline/ 和 schemes/ |
| `CanvasController.cs.legacy` | 使用 `DesignDocument` | 改为使用 ProjectService |

---

## 2. 项目结构

```
BIMCanvas.Server/
├── Program.cs                    ✅ 入口（REST Host + 自动启动 Web）
├── Properties/
│   └── launchSettings.json       ✅ 启动配置（端口 5000）
│
├── Controllers/                  【REST API】
│   ├── ProjectController.cs      ✅【v3.0 新增】项目数据聚合 API
│   ├── CanvasController.cs.legacy   ⚠️ 遗留 v2.9 API，待迁移
│   ├── EventsController.cs         SSE 端点 ⬜ 待开发
│   └── PlacementController.cs      布置 API ⬜ 待开发
│
├── Dtos/                         【v3.0 新增】数据传输对象
│   └── ProjectData.cs            ✅ v3.0 项目数据 DTO
│
├── Services/                     【业务服务】
│   ├── ManifestService.cs        ✅【v3.0 新增】.manifest 文件读写
│   ├── ComputedDataService.cs    ✅【v3.0 新增】计算数据管理
│   ├── StrategyService.cs        ✅【v3.0 新增】策略目录管理
│   ├── ProjectService.cs         ✅【v3.0 新增】项目加载流程
│   ├── CanvasStateManager.cs.legacy  ⚠️ 遗留，待迁移
│   ├── ZoneCalculator.cs.legacy      ⚠️ 遗留，待迁移
│   ├── PlacementService.cs         布置逻辑 ⬜ 待开发
│   ├── EventBus.cs                 事件总线 ⬜ 待开发
│   ├── ScreenshotService.cs        截图服务 ⬜ 待开发
│   └── ChangeSetService.cs         变更集服务 ⬜ 待开发
│
├── Hubs/                         【SignalR Hub】⬜ 待开发
│   └── CanvasHub.cs                画布实时通信
│
├── Mcp/                          【MCP 协议相关】⬜ 待开发
│   └── McpHost.cs                  MCP Server 宿主
│
└── McpTools/                     【MCP 工具实现】⬜ 待开发
    ├── CanvasTools.cs              画布管理工具
    ├── ModuleTools.cs              模块操作工具
    ├── PlacementTools.cs           布置工具
    └── QueryTools.cs               查询工具
```

---

## 3. v3.0 项目加载流程

### 3.1 ProjectService.LoadProject()

完整的项目加载流程：

```
输入：.bcp 压缩包路径

1. 解压 .bcp
   └─ 目标：用户文档/BIMCanvas/Projects/{名称}_{时间戳}/

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

### 3.2 门扇禁区计算

```
对每扇门：
1. 读取 openings.json 中的门数据
2. 获取门宽度 = |line.end - line.start|
3. 计算禁区矩形：
   - 尺寸：doorWidth × doorWidth
   - 偏移：facingDirection × doorWidth
   - 4 个顶点：门线起点、门线终点、终点+偏移、起点+偏移
4. 生成 ExclusionArea 对象
5. 写入 computed/exclusions.json
```

### 3.3 目录结构（完整）

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

## 4. REST API

### 4.1 v3.0 新增端点

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

### 4.2 遗留端点（待迁移）

| 端点 | 方法 | 功能 | 状态 |
|------|------|------|------|
| `/health` | GET | 健康检查 | ✅ |
| `/api/canvas` | GET | 获取所有画布 ID | ⚠️ 遗留 |
| `/api/canvas/{id}` | GET | 获取指定画布 | ⚠️ 遗留 |
| `/api/canvas` | POST | 创建/更新画布 | ⚠️ 遗留 |
| `/api/canvas/load` | POST | 加载并处理画布 | ⚠️ 遗留 |

---

## 5. v3.0 数据传输对象

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
    public List<ExclusionArea> Exclusions { get; set; }
}
```

---

## 6. .manifest 文件格式

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

## 7. 角色定位

### 7.1 组件角色对比

| 组件 | 比喻 | 核心职责 |
|------|------|----------|
| **BIMCanvas.Server** | **心脏 + 神经系统** | 状态管理、几何计算、通信中枢、事件分发 |
| **BIMCanvas.Agent** | **大脑** | 智能决策、理解意图、规划布置方案 |
| **BIMCanvas.Core** | **骨骼** | 数据结构、基础算法、类型定义 |
| **BIMCanvas.Web** | **皮肤 + 眼睛** | 渲染展示、用户交互 |
| **BIMCanvas.Revit** | **手臂** | 从 Revit 抓取数据、回写 Revit |

### 7.2 Server vs Agent 职责边界

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

## 8. 通信架构

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

## 9. 开发状态

### 9.1 已完成

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

### 9.2 待开发

| 功能 | 文件 | 状态 |
|------|------|------|
| 遗留服务迁移 | CanvasStateManager.cs | ⬜ |
| 遗留服务迁移 | ZoneCalculator.cs | ⬜ |
| 遗留服务迁移 | CanvasController.cs | ⬜ |
| SignalR Hub | Hubs/CanvasHub.cs | ⬜ |
| SSE 端点 | Controllers/EventsController.cs | ⬜ |
| MCP 工具 | McpTools/*.cs | ⬜ |
| 文件监听 | ProjectWatcherService.cs | ⬜ |
| 文件写入 | ProjectWriterService.cs | ⬜ |

---

## 10. 相关文档

| 文档 | 路径 | 内容 |
|------|------|------|
| 系统架构 | `docs/Architecture.md` | 整体架构、模块设计 |
| 文件驱动架构 | `docs/FileDrivenArchitecture.md` | "文件播放器"模式 |
| 执行流程 | `docs/Workflows.md` | 端到端流程详解 |
| JSON Schema v3 | `docs/Schema-JSON-v3.md` | v3.0 数据模型定义 |
| 升级进度 | `plans/V3_Upgrade_Progress_Report.md` | v3.0 升级进度 |
| 升级计划 | `plans/V3_Architecture_Upgrade_Plan.md` | 完整升级计划 |
| Core 层 | `BIMCanvas.Core/README.md` | 数据模型 + 算法 |
