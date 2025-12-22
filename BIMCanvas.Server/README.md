# BIMCanvas.Server

> 统一后端服务 - 系统的状态中心与通信中枢

**运行时**: .NET 8.0
**状态**: 🔶 基础功能已完成（REST API + Zone 计算）

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
4. **默认加载文件**：通过 URL 参数 `?file=demo_1` 控制

### 配置项

| 配置 | 位置 | 默认值 | 说明 |
|------|------|--------|------|
| 默认数据文件 | `Program.cs:112` | `demo_1` | 启动时加载的 demo 文件（不含 .json 后缀） |
| API 端口 | `launchSettings.json` | `5000` | REST API 服务端口 |
| Web 端口 | 自动检测 | `5173` | Vite 开发服务器端口 |

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

## 1. 角色定位

### 1.1 组件角色对比

| 组件 | 比喻 | 核心职责 |
|------|------|----------|
| **BIMCanvas.Server** | **心脏 + 神经系统** | 状态管理、几何计算、通信中枢、事件分发 |
| **BIMCanvas.Agent** | **大脑** | 智能决策、理解意图、规划方案 |
| **BIMCanvas.Core** | **骨骼** | 数据结构、基础算法、类型定义 |
| **BIMCanvas.Web** | **皮肤 + 眼睛** | 渲染展示、用户交互 |
| **BIMCanvas.Revit** | **手臂** | 从 Revit 抓取数据、回写 Revit |

### 1.2 Server vs Agent 职责边界

| 职责 | Server | Agent |
|------|--------|-------|
| **状态持有** | ✅ 持有 CanvasDocument | ❌ 无状态 |
| **几何计算** | ✅ Zone/禁区/完成面计算 | ❌ 不做几何 |
| **智能决策** | ❌ 不决定"放哪里" | ✅ 规划布置方案 |
| **通信中枢** | ✅ 连接所有组件 | ❌ 只通过 MCP/SSE |
| **约束验证** | ✅ 边界/碰撞检查 | ❌ 依赖 Server 验证 |

**关键原则**：
- **Server 是「指挥中心」**：协调各方、管理状态、执行验证
- **Agent 是「设计师」**：理解需求、做出决策、发出指令

---

## 2. 核心职责

### 2.1 状态管理（CanvasStateManager）

Server 是 CanvasDocument 的**唯一真理来源**：

```
CanvasStateManager
├── 存储画布状态（内存 + 可选持久化）
├── 版本控制（乐观锁防并发冲突）
├── 变更追踪（ChangeSet 记录用户修改）
└── 状态快照（支持撤销/重做）
```

### 2.2 几何计算（ZoneCalculator）

| 功能 | 输入 | 输出 | 触发时机 |
|------|------|------|----------|
| **Zone 生成** | rooms[] | zones[]（含 tags 推断） | Phase 2 |
| **innerBoundary 计算** | rawBoundary + wallFinishes | 可用空间轮廓 | Phase 2/3 |
| **门扇禁区计算** | openings[] | exclusionAreas[] | Phase 2 |
| **窗台禁区计算** | openings[]（window） | exclusionAreas[] | Phase 2 |
| **完成面禁区计算** | wallFinishes[] | exclusionBoundary | Phase 2/3 |
| **Zone 拆分/合并** | 分割线/合并指令 | 更新的 zones[] | Phase 3 |

### 2.3 通信中枢

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
│ • Revit POST  │    │ • Web 实时    │    │ • Agent 事件  │
│ • Web 查询    │    │ • 状态推送    │    │ • 单向推送    │
│ • 导出/截图   │    │ • 双向通信    │    │               │
└───────────────┘    └───────────────┘    └───────────────┘
        │                     │                     │
        ▼                     ▼                     ▼
    Revit 插件            Web 前端            PlacementAgent
```

### 2.4 事件分发（EventBus）

```csharp
// Server 发布的事件类型
PlacementRequestEvent    // "一键布置"按钮点击
ValidationFailedEvent    // 模块越界/碰撞检测失败
ZoneTagsChangedEvent     // 用户修改区域标签
CanvasUpdatedEvent       // 画布状态变更通知
UserCommitEvent          // 用户提交修改
```

---

## 3. 目录结构

```
BIMCanvas.Server/
├── Program.cs                    ✅ 入口（REST Host + 自动启动 Web）
├── Properties/
│   └── launchSettings.json       ✅ 启动配置（端口 5000）
├── Mcp/                          【MCP 协议相关】⬜ 待开发
│   └── McpHost.cs                  MCP Server 宿主
├── McpTools/                     【MCP 工具实现】⬜ 待开发
│   ├── CanvasTools.cs              画布管理工具
│   ├── ModuleTools.cs              模块操作工具
│   ├── PlacementTools.cs           布置工具
│   └── QueryTools.cs               查询工具
├── Controllers/                  【REST API】
│   ├── CanvasController.cs       ✅ 画布 API（CRUD + load）
│   ├── EventsController.cs         SSE 端点 ⬜ 待开发
│   └── PlacementController.cs      布置 API ⬜ 待开发
├── Hubs/                         【SignalR Hub】⬜ 待开发
│   └── CanvasHub.cs                画布实时通信
└── Services/                     【业务服务】
    ├── CanvasStateManager.cs     ✅ 画布状态管理（内存存储）
    ├── ZoneCalculator.cs         ✅ Zone 计算（Room + Exclusion）
    ├── PlacementService.cs         布置逻辑核心 ⬜ 待开发
    ├── EventBus.cs                 事件总线 ⬜ 待开发
    ├── ScreenshotService.cs        截图服务 ⬜ 待开发
    └── ChangeSetService.cs         变更集服务 ⬜ 待开发
```

### 已实现的 REST API

| 端点 | 方法 | 功能 |
|------|------|------|
| `/health` | GET | 健康检查 |
| `/api/canvas` | GET | 获取所有画布 ID |
| `/api/canvas/{id}` | GET | 获取指定画布 |
| `/api/canvas` | POST | 创建/更新画布 |
| `/api/canvas/load` | POST | **加载并处理画布**（计算 Zone） |
| `/api/canvas/{id}` | DELETE | 删除画布 |

---

## 4. MCP 工具列表

### 4.1 画布管理

| 工具名 | 功能 | 参数 |
|--------|------|------|
| `canvas_create` | 创建画布 | `revitViewId`, `levelId`, `outline`, `zones` |
| `canvas_describe` | 获取画布描述（AI 友好） | `canvasId` |
| `canvas_get_state` | 获取完整 JSON 状态 | `canvasId` |
| `canvas_screenshot` | 获取画布截图 | `canvasId`, `format?` |
| `canvas_export` | 导出 JSON 文件 | `canvasId`, `filePath` |

### 4.2 模块操作

| 工具名 | 功能 | 参数 |
|--------|------|------|
| `module_add` | 添加模块 | `canvasId`, `expectedVersion`, `moduleId`, `bounds`, `facing`, `zoneId` |
| `module_move` | 移动模块 | `canvasId`, `expectedVersion`, `id`, `bounds` |
| `module_rotate` | 旋转模块 | `canvasId`, `expectedVersion`, `id`, `facing` |
| `module_delete` | 删除模块 | `canvasId`, `expectedVersion`, `id` |
| `module_list` | 列出模块 | `canvasId`, `zoneId?` |

### 4.3 版本控制

| 工具名 | 功能 | 参数 |
|--------|------|------|
| `canvas_get_changes` | 获取待处理变更 | `canvasId` |
| `canvas_ack_commits` | 确认已处理变更 | `canvasId`, `changeSetIds` |

### 4.4 查询分析

| 工具名 | 功能 | 参数 |
|--------|------|------|
| `module_at` | 查询位置模块 | `canvasId`, `position` |
| `space_analyze` | 空间分析 | `canvasId`, `zoneId` |

---

## 5. 核心场景

### 5.1 墙面完成面计算

Server 负责将完成面转化为禁区，让 AI 只需关注"哪里能放"：

```
结构墙内表面
      │
      │← WallFinish.locationLine
      │
      │    +------------------------------------------+
      │    │  WallFinish.exclusionBoundary (禁区)     │
      │    +------------------------------------------+
      │
      │    +==========================================+
      │    ║  Zone.innerBoundary (AI 可用布置空间)    ║
      │    +==========================================+
```

**三层来源机制**：

| 来源 | 触发时机 | 示例 |
|------|----------|------|
| RoomDefault | Phase 2 初始计算 | bedroom → 乳胶漆 → 0mm |
| ZoneOverride | Phase 3 tags 变化 | tv_media → 护墙板 → 80mm |
| UserOverride | 用户手动设置 | 选择石材 → 30mm |

### 5.2 门扇禁区计算

```
【Revit 导出】
openings[] = [
  {
    type: "door",
    line: [[2000, 0], [2900, 0]],
    facingDirection: [0, 1],
    openingAngle: 90
  }
]
           │
           ▼
【Server 计算】
doorWidth = 900mm
exclusionBoundary = 向房间内扩展 900mm 的矩形禁区
           │
           ▼
【输出】
zone.exclusionAreas[] = [
  { type: "doorSwing", boundary: [[2000,0], [2900,0], [2900,900], [2000,900]] }
]
```

### 5.3 Zone 生成流程

#### Zone 类型定义

| 类型 | 描述 | ID 格式 | RawBoundary | ComputedBoundary |
|------|------|---------|-------------|------------------|
| **Exclusion** | 门扇禁区（禁止布置家具） | `excl_door_{doorId}` | 禁区矩形 | null |
| **Room** | 房间边界（由 Revit Room 转换） | **使用源 Room ID** | 房间轮廓 | null |
| **Designable** | 设计区（Agent 划分，当前未实现） | 待定 | 原始轮廓 | 扣除完成面后 |

#### RawBoundary vs ComputedBoundary

| 字段 | 用途 | 计算时机 |
|------|------|----------|
| **RawBoundary** | 原始几何轮廓，不考虑完成面扣减 | Zone 创建时 |
| **ComputedBoundary** | 扣除完成面后的可用空间轮廓 | 完成面确定后（Phase 3+） |

**渲染规则**：Web 端使用 `computedBoundary ?? rawBoundary`

- Exclusion/Room Zone：仅有 RawBoundary，使用 RawBoundary 渲染
- Designable Zone：两者都有，优先使用 ComputedBoundary 渲染

#### Server 端生成流程 (ZoneCalculator)

```
ZoneCalculator.Process(document)
    │
    ├─ 1. 检查是否已有 Room Zone
    │      └─ 没有则从 revit.rooms[] 创建
    │           • Id = room.Id（使用源 Room ID）
    │           • RawBoundary = room.Boundary
    │           • ComputedBoundary = null
    │
    ├─ 2. 移除自动生成的禁区（ID 以 excl_ 开头）
    │      └─ 保留用户自定义禁区
    │
    └─ 3. 重新计算门扇禁区
             └─ 遍历 revit.openings[]（仅 Door 类型）
                  • Id = excl_door_{door.Id}
                  • RawBoundary = 门宽 × 门宽 矩形
                  • ComputedBoundary = null
```

#### Web 端渲染流程 (ZoneBuilder)

```
ZoneBuilder.buildZones(doc)
    │
    ├─ 读取 doc.computed.zones[]
    │
    └─ 对每个 Zone 调用 createZoneMesh()
         ├─ boundary = zone.computedBoundary ?? zone.rawBoundary
         ├─ 创建 THREE.ShapeGeometry
         └─ 根据 zone.type 设置材质和 Y 层级：
              • Exclusion: 红色填充, y=10
              • Room: 浅色填充, y=3
              • Designable: 绿色填充, y=5
```

---

### 5.4 Zone 拆分场景

用户可能需要将一个大房间划分为多个功能区：

```
【用户操作】
选择客厅 Zone → 绘制分界线 → 创建"会客区"和"阅读区"

【Server 计算】
1. 几何分割：GeometrySplit(zone.RawBoundary, splitLine)
2. 重算 innerBoundary
3. 重新分配门扇禁区
4. 检查现有模块归属
```

---

## 6. 输入输出

### 6.1 输入来源

| 来源 | 协议 | 数据 |
|------|------|------|
| Revit 插件 | REST POST | 精简版 CanvasDocument |
| Web 前端 | WebSocket | 用户操作（拖拽、修改） |
| Claude Code | MCP | 工具调用（module_add 等） |
| PlacementAgent | MCP | 工具调用（module_add 等） |

### 6.2 输出目标

| 目标 | 协议 | 数据 |
|------|------|------|
| Web 前端 | WebSocket | 完整版 CanvasDocument |
| Claude Code | MCP Response | 工具执行结果 |
| PlacementAgent | SSE | 事件流（placement_request 等） |
| Revit-MCP | JSON Export | 最终布置方案 |

---

## 7. 执行流程中的角色

```
Phase 1: Revit 导出
         Revit ───POST───→ [Server 接收并存储]

Phase 2: 数据处理
         [Server 计算 zones/exclusions/wallFinishes]
         Server ───WebSocket───→ Web 渲染

Phase 3: 区域确认
         Web ───WebSocket───→ [Server 更新 tags]
         [Server 重算 innerBoundary]
         Server ───WebSocket───→ Web 重新渲染

Phase 4: 方案生成
         Web "一键布置" → [Server 发布事件]
         Server ───SSE───→ Agent
         Agent ───MCP───→ [Server 执行 module_add]
         [Server 验证 + 存储]
         Server ───WebSocket───→ Web 渲染

Phase 5: 交互修改
         用户拖拽 → Web ───WebSocket───→ [Server 验证 + 存储]
         用户对话 → Agent ───MCP───→ [Server 执行操作]
         [Server 广播变更]

Phase 6: 回写 Revit
         [Server 导出 JSON] → Revit-MCP → Revit
```

---

## 8. 相关文档

| 文档 | 路径 | 内容 |
|------|------|------|
| 系统架构 | `docs/Architecture.md` | 整体架构、模块设计 |
| 执行流程 | `docs/Workflows.md` | 端到端流程详解 |
| JSON Schema | `docs/Schema-JSON.md` | 数据模型定义 |
| Core 层 | `BIMCanvas.Core/README.md` | 数据模型 + 算法 |
