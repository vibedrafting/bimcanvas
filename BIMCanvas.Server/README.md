# BIMCanvas.Server

> 统一后端服务 - 系统的状态中心与通信中枢

**运行时**: .NET 6+
**状态**: 待开发

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
├── Program.cs                    入口（MCP + Web Host）
├── Mcp/                          【MCP 协议相关】
│   └── McpHost.cs                  MCP Server 宿主
├── McpTools/                     【MCP 工具实现】
│   ├── CanvasTools.cs              画布管理工具
│   ├── ModuleTools.cs              模块操作工具
│   ├── PlacementTools.cs           布置工具
│   └── QueryTools.cs               查询工具
├── Controllers/                  【REST API】
│   ├── CanvasController.cs         画布 API
│   ├── EventsController.cs         SSE 端点
│   └── PlacementController.cs      布置 API
├── Hubs/                         【SignalR Hub】
│   └── CanvasHub.cs                画布实时通信
└── Services/                     【业务服务】
    ├── CanvasStateManager.cs       画布状态管理
    ├── ZoneCalculator.cs           Zone 计算
    ├── PlacementService.cs         布置逻辑核心
    ├── EventBus.cs                 事件总线
    ├── ScreenshotService.cs        截图服务
    └── ChangeSetService.cs         变更集服务
```

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

### 5.3 Zone 拆分场景

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
