# BIMCanvas.Server 实施计划

> **版本**：v1.0
> **更新日期**：2025-12-10
> **状态**：Phase 1 待开发

---

## 一、项目概述

### 1.1 核心定位

**BIMCanvas.Server 是统一后端服务**

BIMCanvas.Server 负责接收 Revit 导出的原始数据，计算设计区（Zone）、禁区（ExclusionArea）、墙面完成面（WallFinish），并通过 REST/SignalR/SSE 向 Web 前端和 Agent 提供数据。

**核心职责**：
- **数据接收**：接收 Revit 导出的精简版 CanvasDocument
- **Zone 计算**：rooms → zones，计算 innerBoundary、exclusionAreas、wallFinishes
- **实时推送**：通过 SignalR 向 Web 推送更新
- **事件发布**：通过 SSE 向 Agent 发布事件
- **状态管理**：内存存储 + 版本控制

### 1.2 职责边界

#### ✅ BIMCanvas.Server 负责

| 功能类别 | 具体内容 | 输入 | 输出 |
|----------|----------|------|------|
| **数据接收** | POST /api/canvas 接收 JSON | CanvasDocument (精简版) | 存储成功 |
| **Zone 计算** | rooms → zones 转换 | Room[] | Zone[] |
| **InnerBoundary** | rawBoundary - wallFinishes | Zone.RawBoundary | Zone.InnerBoundary |
| **ExclusionAreas** | 门扇开启禁区计算 | Opening[] | ExclusionArea[] |
| **WallFinish** | 墙面完成面生成 | Room.Type + Zone.Tags | WallFinish[] |
| **实时推送** | SignalR broadcast | 更新事件 | 推送到 Web |
| **事件发布** | SSE 事件流 | EventBus 事件 | 推送到 Agent |

#### ❌ BIMCanvas.Server 不负责

| 功能 | 负责方 | 原因 |
|------|--------|------|
| Revit 数据提取 | BIMCanvas.Revit | Revit API 访问 |
| AI 布置规划 | BIMCanvas.Agent | Agent SDK 独立进程 |
| SVG 渲染 | BIMCanvas.Web | 前端职责 |
| 用户交互 | BIMCanvas.Web | 前端职责 |

### 1.3 系统中的位置

```
BIMCanvas 系统架构
├── BIMCanvas.Core (.NET Standard 2.0)   ← 数据模型 + 算法库
├── BIMCanvas.Revit (.NET FW 4.7.2)      ← Revit 插件
├── BIMCanvas.Server (.NET 6+)           ← 本项目：后端服务
├── BIMCanvas.Agent (Python 3.10+)       ← AI 布置代理
└── BIMCanvas.Web (Vue 3 + TS)           ← Web 前端
```

### 1.4 数据流

```
【接收流程】
Revit 导出 → 本地 .json 文件
    ↓ [POST /api/canvas]
BIMCanvas.Server
    ↓ [输入校验：coordinateSystem、多边形合法性]
    ↓ [ZoneCalculator: rooms → zones]
    ↓ [计算 innerBoundary、exclusionAreas、wallFinishes]
    ↓ [CanvasStateManager: 存储 + version++]
完整版 CanvasDocument
    ↓ [SignalR Hub: BroadcastDocument]
    ↓ [EventBus: Publish(canvas_ready)]
BIMCanvas.Web + BIMCanvas.Agent
```

---

## 二、功能规格

### 2.1 Phase 1：核心功能

#### 2.1.1 CanvasController - REST API

**位置**：`Controllers/CanvasController.cs`

```csharp
[ApiController]
[Route("api/[controller]")]
public class CanvasController : ControllerBase
{
    // 接收 Revit 导出数据，触发 Zone 计算
    [HttpPost]
    public async Task<ActionResult<CanvasDocument>> CreateCanvas(CanvasDocument input);

    // 获取完整 CanvasDocument
    [HttpGet("{id}")]
    public ActionResult<CanvasDocument> GetCanvas(string id);

    // 预留：提交变更（Phase 1 返回 501）
    [HttpPost("{id}/commit")]
    public ActionResult CommitChanges(string id, ChangeSet changeSet);
}
```

**输入校验**：
- `coordinateSystem` 必须为 `"y-up"`
- `outline.boundaries` 每个多边形至少 3 个顶点
- `outline.boundaries` 多边形不能自交
- `rooms` 每个房间必须有有效边界

#### 2.1.2 EventsController - SSE 端点

**位置**：`Controllers/EventsController.cs`

```csharp
[ApiController]
[Route("api/[controller]")]
public class EventsController : ControllerBase
{
    // SSE 事件流
    [HttpGet]
    public async Task GetEventStream(CancellationToken cancellationToken);
}
```

**事件格式**：
```
event: canvas_ready
data: {"eventType":"canvas_ready","canvasId":"xxx","version":1}
```

#### 2.1.3 CanvasHub - SignalR Hub

**位置**：`Hubs/CanvasHub.cs`

```csharp
public class CanvasHub : Hub
{
    // 客户端加入画布组
    public async Task JoinCanvas(string canvasId);

    // 客户端离开画布组
    public async Task LeaveCanvas(string canvasId);

    // 服务端调用：广播文档更新
    public async Task BroadcastDocument(string canvasId, CanvasDocument document);
}
```

#### 2.1.4 ZoneCalculator - Zone 计算服务

**位置**：`Services/ZoneCalculator.cs`

```csharp
public class ZoneCalculator
{
    private readonly IOptions<WallFinishRules> _finishRules;

    // 主计算方法
    public CanvasDocument Calculate(CanvasDocument input);

    // 1. rooms → zones
    private List<Zone> GenerateZones(List<Room> rooms);

    // 2. 生成墙面完成面
    private List<WallFinish> GenerateWallFinishes(List<Room> rooms, List<Zone> zones);

    // 3. 计算 innerBoundary = rawBoundary - wallFinishes
    private Polygon2D ComputeInnerBoundary(Zone zone, List<WallFinish> finishes);

    // 4. 计算门扇禁区
    private List<ExclusionArea> ComputeDoorExclusions(List<Opening> openings);
}
```

**计算流程**：
1. 遍历 rooms，为每个 room 创建 zone（RoomId, RawBoundary = room.Boundary）
2. 根据 room.Type 推断 zone.Tags
3. 根据 room.Type + zone.Tags 生成 wallFinishes
4. 计算 zone.InnerBoundary = RawBoundary - wallFinishes.ExclusionBoundary
5. 计算门扇 exclusionAreas

**约束**：只调用 Core.Algorithms，禁止 Math.自定义逻辑

#### 2.1.5 CanvasStateManager - 状态管理

**位置**：`Services/CanvasStateManager.cs`

```csharp
public class CanvasStateManager
{
    private readonly ConcurrentDictionary<string, CanvasDocument> _documents;

    // 存储文档（自动递增版本号）
    public CanvasDocument Store(CanvasDocument document);

    // 获取文档
    public CanvasDocument? Get(string id);

    // 检查版本冲突
    public bool CheckVersion(string id, int expectedVersion);
}
```

#### 2.1.6 EventBus - 事件总线

**位置**：`Services/EventBus.cs`

```csharp
public class EventBus
{
    private readonly Channel<CanvasEvent> _channel;

    // 发布事件
    public void Publish(CanvasEvent evt);

    // 订阅事件流（供 SSE 使用）
    public IAsyncEnumerable<CanvasEvent> Subscribe(CancellationToken ct);
}

public record CanvasEvent(string EventType, string CanvasId, int Version);
```

### 2.2 Phase 2：提交机制

#### 2.2.1 ChangeSetService

```csharp
public class ChangeSetService
{
    // 验证变更集
    public ValidationResult Validate(ChangeSet changeSet);

    // 应用变更
    public CanvasDocument Apply(CanvasDocument document, ChangeSet changeSet);
}
```

---

## 三、技术设计

### 3.1 项目配置

| 配置项 | 值 | 说明 |
|--------|-----|------|
| 目标框架 | .NET 6+ | LTS 版本 |
| 依赖项目 | BIMCanvas.Core | .NET Standard 2.0 兼容 |
| 实时通信 | SignalR | 内置支持 |
| SSE | 原生实现 | text/event-stream |

### 3.2 csproj 配置

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\BIMCanvas.Core\BIMCanvas.Core.csproj" />
  </ItemGroup>
</Project>
```

### 3.3 模块结构

```
BIMCanvas.Server/
├── BIMCanvas.Server.csproj
├── Program.cs                           入口 + 中间件配置
├── appsettings.json                     配置文件
│
├── Controllers/                      【API 层】
│   ├── CanvasController.cs              REST API
│   └── EventsController.cs              SSE 端点
│
├── Hubs/                             【实时通信】
│   └── CanvasHub.cs                     SignalR Hub
│
├── Services/                         【业务逻辑】
│   ├── ZoneCalculator.cs                Zone 计算
│   ├── CanvasStateManager.cs            状态管理
│   ├── EventBus.cs                      事件总线
│   └── ChangeSetService.cs              变更服务（Phase 2）
│
├── Models/                           【配置模型】
│   └── WallFinishRules.cs               完成面规则配置
│
├── Validation/                       【校验】
│   └── CanvasValidator.cs               输入校验
│
└── wwwroot/                          【静态资源】
    └── (Vue 构建产物)
```

### 3.4 配置文件 (appsettings.json)

```json
{
  "WallFinishRules": {
    "RoomDefaults": {
      "bathroom": { "type": "tile", "thickness": 50 },
      "kitchen": { "type": "tile", "thickness": 50 },
      "bedroom": { "type": "latex", "thickness": 5 },
      "livingRoom": { "type": "latex", "thickness": 5 }
    },
    "ZoneOverrides": {
      "tv_media": { "type": "panel", "thickness": 80 },
      "sleep": { "type": "fabric", "thickness": 60 }
    }
  },
  "GridLevels": [
    { "name": "coarse", "interval": 600 },
    { "name": "medium", "interval": 300 },
    { "name": "fine", "interval": 100 }
  ]
}
```

### 3.5 Program.cs 配置

```csharp
var builder = WebApplication.CreateBuilder(args);

// 服务注册
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddSingleton<CanvasStateManager>();
builder.Services.AddSingleton<EventBus>();
builder.Services.AddScoped<ZoneCalculator>();
builder.Services.Configure<WallFinishRules>(
    builder.Configuration.GetSection("WallFinishRules"));

var app = builder.Build();

// 中间件
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
app.MapHub<CanvasHub>("/hubs/canvas");
app.MapFallbackToFile("index.html");

app.Run();
```

---

## 四、实施计划

### 4.1 开发阶段

#### Phase 1A：基础设施

| 步骤 | 任务 | 产出 |
|------|------|------|
| 1.1 | 项目初始化 + 引用 Core | csproj + Program.cs |
| 1.2 | CanvasStateManager 实现 | 内存存储 + 版本控制 |
| 1.3 | EventBus 实现 | Channel 事件流 |
| 1.4 | CanvasValidator 实现 | 输入校验逻辑 |

#### Phase 1B：API 层

| 步骤 | 任务 | 产出 |
|------|------|------|
| 1.5 | CanvasController POST/GET | REST 端点 |
| 1.6 | CanvasHub 实现 | SignalR 广播 |
| 1.7 | EventsController 实现 | SSE 事件流 |

#### Phase 1C：计算服务

| 步骤 | 任务 | 产出 |
|------|------|------|
| 1.8 | WallFinishRules 配置模型 | IOptions 绑定 |
| 1.9 | ZoneCalculator 实现 | 完整计算逻辑 |
| 1.10 | 集成测试 | 端到端验证 |

### 4.2 验收标准

#### Phase 1 验收

| 检查项 | 标准 |
|--------|------|
| 编译 | `dotnet build` 通过 |
| POST /api/canvas | 接收 JSON，返回完整 CanvasDocument |
| GET /api/canvas/{id} | 返回存储的文档 |
| /hubs/canvas | SignalR 连接成功，收到 broadcast |
| /api/events | SSE 连接成功，收到 canvas_ready |
| Zone 计算 | zones[] 正确生成 |
| InnerBoundary | 正确计算（rawBoundary - wallFinishes） |
| ExclusionAreas | 门扇禁区正确生成 |
| 版本控制 | version 自动递增 |
| 输入校验 | 非法输入返回 400 |

---

## 五、附录

### 5.1 相关文档

| 文档 | 路径 | 内容 |
|------|------|------|
| 架构文档 | `docs/Architecture.md` | 系统架构 |
| JSON Schema | `docs/Schema-JSON.md` | v2.5 数据模型 |
| 评审文档 | `reviews/ServerWeb_Implementation_Review.md` | 共识总结 |
| Core 层 | `BIMCanvas.Core/README.md` | 数据模型 + 算法 |

### 5.2 进度追踪

| 阶段 | 状态 | 更新时间 |
|------|------|----------|
| Phase 1A: 基础设施 | ⬜ 待开始 | - |
| Phase 1B: API 层 | ⬜ 待开始 | - |
| Phase 1C: 计算服务 | ⬜ 待开始 | - |
| Phase 2: 提交机制 | ⬜ 待开始 | - |

### 5.3 变更日志

| 日期 | 版本 | 变更内容 |
|------|------|----------|
| 2025-12-10 | v1.0 | 计划创建，基于共识文档 |
