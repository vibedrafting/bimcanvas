# BIMCanvas 端到端业务流程

> 版本：v2.0
> 更新日期：2026-01-13
> 本文档描述 BIMCanvas 的完整执行流程与协作规范

---

## 0. 全局流程总览

### 0.1 端到端流程图

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                           BIMCanvas 完整执行流程                              │
├──────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  ┌─────────────────────────────────────────────────────────────────────────┐ │
│  │  Phase 1: 数据准备                                                       │ │
│  │  Revit 提取 → baseline/ (walls, columns, rooms, openings)               │ │
│  └─────────────────────────────────────────────────────────────────────────┘ │
│                                     │                                        │
│                                     ▼                                        │
│  ┌─────────────────────────────────────────────────────────────────────────┐ │
│  │  Phase 2: 数据处理                                                       │ │
│  │  Server 计算 → computed/ (room_zones, exclusions) + tags 预分配         │ │
│  └─────────────────────────────────────────────────────────────────────────┘ │
│                                     │                                        │
│                                     ▼                                        │
│  ┌─────────────────────────────────────────────────────────────────────────┐ │
│  │  Phase 3: 区域确认                                                       │ │
│  │  Server 预计算 tags → 用户确认/调整                                      │ │
│  └─────────────────────────────────────────────────────────────────────────┘ │
│                                     │                                        │
│                                     ▼                                        │
│  ┌─────────────────────────────────────────────────────────────────────────┐ │
│  │  Phase 4: 方案生成                                                       │ │
│  │  MainAgent → schemes/{s}/modules.json 布置方案                      │ │
│  └─────────────────────────────────────────────────────────────────────────┘ │
│                                     │                                        │
│                                     ▼                                        │
│  ┌─────────────────────────────────────────────────────────────────────────┐ │
│  │  Phase 5: 交互修改                                                       │ │
│  │  拖拽 / 对话 ←→ 循环迭代（ChangeSource 追踪）                            │ │
│  └─────────────────────────────────────────────────────────────────────────┘ │
│                                     │                                        │
│                                     ▼                                        │
│  ┌─────────────────────────────────────────────────────────────────────────┐ │
│  │  Phase 6: 回写 Revit                                                     │ │
│  │  load_family → create_element → Revit 模型                               │ │
│  └─────────────────────────────────────────────────────────────────────────┘ │
│                                                                              │
└──────────────────────────────────────────────────────────────────────────────┘
```

### 0.2 阶段速查表

| 阶段 | 触发条件 | 执行者 | 输出 | 耗时 |
|------|----------|--------|------|------|
| Phase 1 | 用户点击"开始设计" | BIMCanvas.Revit | baseline/ 数据 | 即时 |
| Phase 2 | Server 收到 POST | BIMCanvas.Server | computed/ 数据 + tags | < 1s |
| Phase 3 | Web 收到推送 | 用户 | zones[].tags 确认 | 用户决定 |
| Phase 4 | 用户确认区域 | MainAgent | modules.json | 数秒 |
| Phase 5 | 用户操作 | Web + Server + AI | 更新的 modules.json | 循环 |
| Phase 6 | 用户点击"应用" | Revit-MCP | Revit 家具实例 | 数秒 |

### 0.3 三层汉堡模型

> 详见 [Architecture.md §2.3 三层汉堡模型](./Architecture.md#23-三层汉堡模型)，数据格式定义见 [Schema.md](./Schema.md)

**结构概要**：`baseline/`（建筑基础）→ `schemes/`（方案设计）→ `computed/`（派生数据）

> **v3.2 架构简化**：多策略通过 Git 分支隔离，而非子目录。

### 0.4 组件交互概览

```
┌─────────────────┐     ┌──────────────────┐     ┌─────────────────┐
│  BIMCanvas.Revit │     │  BIMCanvas.Server │     │  BIMCanvas.Web  │
│  (.NET FW 4.7.2) │     │  (.NET 6+)        │     │  (Vue 3 + TS)   │
└────────┬────────┘     └────────┬─────────┘     └────────┬────────┘
         │                       │                        │
         │  POST JSON (Phase 1)  │                        │
         ├──────────────────────>│                        │
         │                       │  WebSocket (Phase 2)   │
         │                       ├───────────────────────>│
         │                       │                        │
         │                       │     ┌──────────────────┴─────────────────┐
         │                       │     │  MainAgent (Python)           │
         │                       │     │  (Agent SDK)                       │
         │                       │     └──────────────────┬─────────────────┘
         │                       │   SSE 事件              │
         │                       ├───────────────────────>│
         │                       │   MCP 工具调用          │
         │                       │<───────────────────────┤
         │                       │                        │
         │  Revit-MCP (Phase 6)  │                        │
         │<──────────────────────┤                        │
         │                       │                        │
```

---

## 1. Phase 1: 数据准备

### 触发条件

> 用户在 Revit 中激活目标平面视图，点击"开始设计"按钮

### 子流程

| 步骤 | 执行者 | 动作 | 输入 | 输出 |
|------|--------|------|------|------|
| 1.1 | 用户 | 激活 Revit 平面视图 | - | 当前视图 |
| 1.2 | 用户 | 点击"开始设计"按钮 | - | 触发命令 |
| 1.3 | Revit 插件 | 提取边界轮廓 | 视图中的 Wall/Column 元素 | walls.json, columns.json |
| 1.4 | Revit 插件 | 提取门窗线段 | Door/Window 元素 | openings.json |
| 1.5 | Revit 插件 | 识别物理房间 | Room 元素 | rooms.json |
| 1.6 | Revit 插件 | 生成定位线 | 墙体内表面 | locationLines.json |
| 1.7 | Revit 插件 | 打包 baseline/ | 上述数据 | .bcp 文件 |
| 1.8 | Revit 插件 | POST 到 Server | .bcp 文件 | HTTP 响应 |

### 流程图

```
用户操作                          Revit 插件                           Server
   │                                  │                                  │
   │  激活平面视图                     │                                  │
   ├─────────────────────────────────>│                                  │
   │                                  │                                  │
   │  点击"开始设计"                   │                                  │
   ├─────────────────────────────────>│                                  │
   │                                  │                                  │
   │                                  │  提取边界元素（墙体/柱子）         │
   │                                  ├─────────────┐                    │
   │                                  │             │                    │
   │                                  │<────────────┘                    │
   │                                  │  walls.json, columns.json        │
   │                                  │                                  │
   │                                  │  提取 Door/Window                 │
   │                                  ├─────────────┐                    │
   │                                  │             │                    │
   │                                  │<────────────┘                    │
   │                                  │  openings.json                   │
   │                                  │                                  │
   │                                  │  识别 Room 元素                   │
   │                                  ├─────────────┐                    │
   │                                  │             │                    │
   │                                  │<────────────┘                    │
   │                                  │  rooms.json                      │
   │                                  │                                  │
   │                                  │  POST .bcp 项目文件               │
   │                                  ├─────────────────────────────────>│
   │                                  │                                  │
```

### 关键代码路径

- `BIMCanvas.Revit/Commands/ExportCanvasCommand.cs` - 导出命令入口
- `BIMCanvas.Revit/Services/CanvasExportService.cs` - 导出服务
- `BIMCanvas.Revit/Adapters/BoundaryAdapter.cs` - 边界轮廓提取（墙体 + 柱子）
- `BIMCanvas.Revit/Adapters/OpeningAdapter.cs` - 门窗线段提取
- `BIMCanvas.Revit/Adapters/RoomAdapter.cs` - 房间边界提取

### 输出

**baseline/ 目录结构**：

```json
// baseline/rooms.json
{
  "rooms": [
    { "id": "room_001", "name": "客厅", "type": "LivingRoom", "boundary": [...] },
    { "id": "room_002", "name": "主卧", "type": "MasterBedroom", "boundary": [...] }
  ]
}
```

---

## 2. Phase 2: 数据处理

### 触发条件

> Server 收到 Revit 提交的 .bcp 项目文件 POST 请求

### 子流程

| 步骤 | 执行者 | 动作 | 输入 | 输出 |
|------|--------|------|------|------|
| 2.1 | ProjectController | 接收 POST 请求 | .bcp 文件 | 解压并加载 |
| 2.2 | ZoneCalculator | 从 rooms 生成 zones | rooms.json | room_zones.json (初始) |
| 2.3 | ZoneCalculator | **预计算功能标签** | room.type | zones[].tags |
| 2.4 | ZoneCalculator | 计算 innerBoundary | zone.rawBoundary + finishes | zone.innerBoundary |
| 2.5 | ZoneCalculator | 计算门扇禁区 | openings.json (door) | exclusions.json |
| 2.6 | ProjectStateManager | 存储项目状态 | 完整项目数据 | projectId |
| 2.7 | ProjectHub | WebSocket 推送 | 项目数据 | 前端接收 |

### 功能标签预计算（关键步骤）

**设计原则**：Server 是约束管理者，负责房间类型→功能标签的映射；Agent 是智能决策者，只读取预计算好的 tags。

```
┌──────────────────────────────────────────────────────────────┐
│  ZoneCalculator.ComputeTags(rooms)                           │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│  // Server 持有的房间类型→功能标签对照表                      │
│  ROOM_TYPE_TAGS = {                                          │
│    "LivingRoom":     ["seating", "media", "storage", "lighting"],│
│    "MasterBedroom":  ["sleep", "storage", "dressing", "lighting"],│
│    "Bedroom":        ["sleep", "storage", "work", "lighting"],│
│    "DiningRoom":     ["dining", "storage", "lighting"],      │
│    "Kitchen":        ["appliance", "storage"],               │
│    "Bathroom":       ["appliance"],                          │
│    "Study":          ["work", "storage", "seating", "lighting"],│
│    "Balcony":        ["appliance", "seating"],               │
│  }                                                           │
│                                                              │
│  for each room in rooms:                                     │
│      zone.tags = ROOM_TYPE_TAGS.get(room.type, [])           │
│      zone.reason = f"room:{room.type}"                       │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

### 关键代码路径

- `BIMCanvas.Server/Controllers/ProjectController.cs` - REST 入口
- `BIMCanvas.Server/Services/ZoneCalculator.cs` - 核心计算逻辑
- `BIMCanvas.Server/Services/ProjectStateManager.cs` - 状态管理
- `BIMCanvas.Server/Hubs/ProjectHub.cs` - WebSocket 推送

### 输出

**computed/room_zones.json**：

```json
{
  "version": "1.0",
  "zones": [
    {
      "id": "z1",
      "roomId": "room_001",
      "reason": "room:LivingRoom",
      "tags": ["seating", "media", "storage"],
      "rawBoundary": [[0, 0], [6000, 0], [6000, 4000], [0, 4000]],
      "innerBoundary": [[50, 50], [5950, 50], [5950, 3950], [50, 3950]],
      "area_mm2": 24000000
    },
    {
      "id": "z2",
      "roomId": "room_002",
      "reason": "room:MasterBedroom",
      "tags": ["sleep", "storage", "dressing"],
      "rawBoundary": [[0, 0], [4000, 0], [4000, 3500], [0, 3500]],
      "innerBoundary": [[50, 50], [3950, 50], [3950, 3450], [50, 3450]],
      "area_mm2": 14000000
    }
  ]
}
```

**computed/exclusions.json**：

```json
{
  "exclusions": [
    {
      "id": "ex_door_001",
      "sourceType": "doorSwing",
      "sourceId": "door_001",
      "zoneId": "z1",
      "boundary": [[2000, 0], [3000, 0], [3000, 800], [2000, 800]]
    }
  ]
}
```

---

## 3. Phase 3: 区域确认

### 触发条件

> Web 前端收到 WebSocket 推送的项目数据

### 子流程

| 步骤 | 执行者 | 动作 | 输入 | 输出 |
|------|--------|------|------|------|
| 3.1 | Web 前端 | 渲染户型底图 | baseline/ 数据 | SVG 显示 |
| 3.2 | Web 前端 | 显示 zone 功能标签 | zones[].tags | UI 标签 |
| 3.3 | 用户 | 查看/修改功能分配 | 当前 tags | 确认或修改 |
| 3.4 | Web 前端 | 发送 tags 更新 | 修改后的 tags | WebSocket 消息 |
| 3.5 | Server | 更新 zones[].tags | 新 tags | 状态更新 |
| 3.6 | Server | 检查 ZoneOverride 规则 | 新 tags + 规则表 | 受影响的墙面 |
| 3.7 | ZoneCalculator | 更新 wallFinishes | ZoneOverride 规则 | 新 thickness |
| 3.8 | ZoneCalculator | 重算 innerBoundary | 更新的 wallFinishes | 新 innerBoundary |
| 3.9 | ProjectHub | 广播变更 | zones + wallFinishes | WebSocket 推送 |

### 流程图

```
Web 前端                              用户                              Server
   │                                   │                                  │
   │  渲染户型图 + zone 标签            │                                  │
   ├──────────────────────────────────>│                                  │
   │                                   │                                  │
   │                                   │  查看功能标签                     │
   │                                   │  (seating, media, ...)           │
   │                                   │                                  │
   │                                   │  点击修改标签                     │
   │<──────────────────────────────────┤                                  │
   │                                   │                                  │
   │  发送 tags 更新                    │                                  │
   ├──────────────────────────────────────────────────────────────────────>│
   │                                   │                                  │
   │                                   │               ┌──────────────────┴───────────────────┐
   │                                   │               │  CheckZoneOverride(newTags)          │
   │                                   │               ├──────────────────────────────────────┤
   │                                   │               │  if (tags 匹配 ZoneOverride 规则):    │
   │                                   │               │      更新 wallFinishes.thickness     │
   │                                   │               │      重算 exclusionBoundary          │
   │                                   │               │      重算 zone.innerBoundary         │
   │                                   │               └──────────────────┬───────────────────┘
   │                                   │                                  │
   │  WebSocket: 推送更新的 zones + wallFinishes                          │
   │<─────────────────────────────────────────────────────────────────────┤
   │                                   │                                  │
   │  重新渲染 innerBoundary           │                                  │
   │                                   │                                  │
   │                                   │  点击"确认区域"                   │
   │<──────────────────────────────────┤                                  │
   │                                   │                                  │
   │  触发 Phase 4                      │                                  │
   ├──────────────────────────────────────────────────────────────────────>│
```

### 墙面完成面处理流程

**设计意图**

墙面完成面（WallFinish）是一种禁区机制，用于预留墙面装饰所需的空间（如护墙板、石材）。家具不应贴着结构墙放置，而是要留出完成面的厚度。

**空间关系**

```
结构墙内表面
      |
      |<-- WallFinish.locationLine (与 Zone.rawBoundary 共线)
      |
      |    thickness (向房间内部扩展)
      |    |
      |    v
      |    +------------------------------------------+
      |    |  WallFinish.exclusionBoundary (禁区)     |
      |    +------------------------------------------+
      |
      |    +==========================================+
      |    ||  Zone.innerBoundary (可用布置空间)      ||
      |    +==========================================+
      |
```

**三层来源机制**

| 来源 | 计算时机 | 触发条件 | 示例 |
|------|----------|----------|------|
| RoomDefault | Phase 2 | Room.type | bedroom → 乳胶漆 → 0mm |
| ZoneOverride | Phase 3 | Zone.tags 变化 | tv_media → 护墙板 → 80mm |
| UserOverride | 任意时刻 | 用户手动设置 | 选择石材 → 30mm |

**ZoneOverride 常见规则**

| Tag | 完成面类型 | Thickness | 说明 |
|-----|-----------|-----------|------|
| tv_media | 护墙板 | 80mm | 电视背景墙 |
| storage | 柜体 | 600mm | 嵌入式收纳 |

---

## 4. Phase 4: 方案生成

### 触发条件

> 用户确认区域后，或点击"一键布置"按钮

### 核心设计原则

```
Server = 约束管理者 + 验证者（不做布置决策）
Agent = 智能决策者 + 规划者（不持有状态、不持有映射逻辑）
```

**关键职责边界**：
- **Server 职责**：房间类型→功能标签映射、约束预计算、验证
- **Agent 职责**：读取预计算数据、智能决策、布置规划

### 版本对比

| 维度 | MVP 版本 | 完整版 |
|------|----------|--------|
| **交互方式** | Agent 直接读写文件 | Agent 通过 MCP 工具调用 Server |
| **模块库访问** | 直接读取 `module_library.json` | Server 提供 `list_modules` 工具 |
| **功能标签** | Server 预计算写入 `room_zones.json` | Server 实时计算 |
| **约束数据** | 读取 `computed/*.json` 静态文件 | Server 实时计算并返回 |
| **验证时机** | 事后验证（Agent 提交后 Server 检查） | 实时验证（每次放置前检查） |
| **失败处理** | Server 通知 Agent 整体重做 | Server 返回冲突详情，Agent 局部调整 |
| **适用场景** | 快速验证、单机开发 | 生产环境、多 Agent 并行 |

---

### 4.0 Agent 三阶段工作流总览

> Agent 内部的工作流程分为三个阶段：分区设计 → 布置决策 → 提交交付

```
┌─────────────────────────────────────────────────────────────────┐
│                     PlacementAgent 工作流                        │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  【Phase A: 分区设计】                                           │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │ 输入：                                                    │   │
│  │   • computed/room_zones.json (Room Zone)                 │   │
│  │   • baseline/rooms.json (房间名称、类型)                  │   │
│  │   • 用户需求 + 策略参数                                   │   │
│  │                                                          │   │
│  │ AI 任务：                                                 │   │
│  │   1. 分析户型结构（几室几厅几卫）                         │   │
│  │   2. 读取每个 Room Zone 的功能标签 (tags，Server 预计算)  │   │
│  │   3. 根据策略调整标签权重                                 │   │
│  │   4. 生成 Designable Zone                                │   │
│  │   5. 细分设计区（如客厅分为沙发区、电视区）              │   │
│  │                                                          │   │
│  │ 输出：                                                    │   │
│  │   • schemes/{s}/zones.json (Designable Zone)             │   │
│  └─────────────────────────────────────────────────────────┘   │
│                              ↓                                  │
│  【Phase B: 布置决策】                                           │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │ 输入：                                                    │   │
│  │   • schemes/{s}/zones.json (Designable Zone)             │   │
│  │   • modules/module_library.json (模块元数据)             │   │
│  │   • baseline/openings.json (门窗位置)                    │   │
│  │   • computed/exclusions.json (禁区)                      │   │
│  │   • 策略参数                                              │   │
│  │                                                          │   │
│  │ AI 任务：                                                 │   │
│  │   1. 根据 tags + 策略过滤合适的模块                      │   │
│  │   2. 确定锚点家具位置                                     │   │
│  │   3. 围绕锚点布置主要家具                                 │   │
│  │   4. 填充辅助家具                                         │   │
│  │   5. 确定朝向                                            │   │
│  │                                                          │   │
│  │ 输出：                                                    │   │
│  │   • schemes/{s}/modules.json (布置结果)                  │   │
│  │   • schemes/{s}/README.md (设计说明)                     │   │
│  └─────────────────────────────────────────────────────────┘   │
│                              ↓                                  │
│  【Phase C: 提交交付】                                           │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │ AI 任务：                                                 │   │
│  │   1. 生成语义化 Commit Message                           │   │
│  │   2. 执行 git add && git commit                          │   │
│  │   3. 通知 Server 验证                                     │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

#### Phase A 详细步骤

**Step 1: 读取功能标签**

> **重要**：功能标签由 Server 预计算并写入 `computed/room_zones.json` 的 `tags` 字段。
> Agent 不再自行推断标签，只需读取 Server 预计算的 `zone.tags`。

以下为 Server 使用的"房间类型 → 功能标签"映射参考（Agent 无需了解此映射逻辑）：

| reason | name 关键词 | 预计算 tags |
|--------|-------------|-----------|
| room:LivingRoom | 客厅 | sitting, entertainment, tv_media |
| room:MasterBedroom | 主卧 | sleeping, rest, storage, dressing |
| room:Bedroom | 次卧/卧室 | sleeping, rest |
| room:Bathroom | 卫生间/主卫/公卫 | bathing, toilet |
| room:Kitchen | 厨房 | cooking, storage |
| room:DiningRoom | 餐厅 | dining |

**Step 2: 素材库过滤**

根据 tags + 风格 + 策略参数过滤合适的模块：
- 策略为"极致收纳"时，优先选择储物类家具
- 策略为"动线优先"时，减少大型家具数量
- 策略为"极简留白"时，只选择核心家具

**Step 3: 设计区划分**

将 Room Zone 进一步细分为 Designable Zone：
- 客厅 → 沙发区、电视区、通道区
- 卧室 → 睡眠区、储物区、梳妆区

---

### 4.1 MVP 版本：文件驱动工作流

**设计理念**

> Agent 是"独立设计师"，Server 是"事后审核员"
> **关键**：Server 在数据准备阶段完成所有映射和预计算，Agent 只负责读取和决策

```
Server 预计算（含标签分配）→ Agent 读取数据 → Agent 独立决策 → Server 事后验证
```

**三阶段工作流**

```
┌─────────────────────────────────────────────────────────────────┐
│              MVP 版本：文件驱动工作流（无 MCP）                    │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  【阶段1】数据准备（Server 预计算，含功能标签分配）               │
│  ┌─────────────────────────────────────────────────────────────┐
│  │ Server 在项目初始化时生成：                                  │
│  │                                                             │
│  │ • computed/room_zones.json  - 房间区域数据（含 tags 字段）  │
│  │ • computed/exclusions.json  - 禁区集合                      │
│  │                                                             │
│  │ Agent 直接读取：                                             │
│  │ • modules/module_library.json  - 模块库元数据               │
│  │ • baseline/openings.json       - 门窗数据                   │
│  └─────────────────────────────────────────────────────────────┘
│                              ↓                                  │
│  【阶段2】独立决策（Agent 自主完成）                             │
│  ┌─────────────────────────────────────────────────────────────┐
│  │ Agent 完全独立工作：                                         │
│  │                                                             │
│  │ 1. 读取设计区数据（已包含功能标签）                          │
│  │    zone = load_json("computed/room_zones.json")[zone_id]    │
│  │    tags = zone["tags"]  # Server 已预计算好                 │
│  │                                                             │
│  │ 2. 读取模块库                                                │
│  │    library = load_json("modules/module_library.json")       │
│  │                                                             │
│  │ 3. 根据功能标签过滤模块                                      │
│  │    modules = [m for m in library["modules"]                 │
│  │               if any(t in m["tags"] for t in tags)]         │
│  │                                                             │
│  │ 4. 读取禁区数据，自行规避                                    │
│  │    exclusions = load_json("computed/exclusions.json")       │
│  │                                                             │
│  │ 5. 执行布置决策（AI 推理）                                   │
│  │                                                             │
│  │ 6. 直接写入结果文件                                          │
│  │    write_json("schemes/{s}/modules.json", modules)          │
│  └─────────────────────────────────────────────────────────────┘
│                              ↓                                  │
│  【阶段3】事后验证（Server 检查）                                │
│  ┌─────────────────────────────────────────────────────────────┐
│  │ Server 读取 Agent 提交的 modules.json，执行验证：            │
│  │                                                             │
│  │ 1. moduleId 有效性检查                                       │
│  │ 2. 标签兼容性检查                                            │
│  │ 3. 空间约束检查                                              │
│  │ 4. 验证结果处理                                              │
│  │    ✓ 通过：通知前端展示                                     │
│  │    ✗ 失败：SSE 通知 Agent 重做                              │
│  └─────────────────────────────────────────────────────────────┘
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

**MVP 版本的约束**

| 约束 | 说明 |
|------|------|
| **无实时反馈** | Agent 不知道某个位置是否合法，只能提交后等待 Server 验证 |
| **简化碰撞检测** | Agent 只能做 AABB 检测，精确 Polygon 检测由 Server 完成 |
| **整体重做** | 验证失败时，Agent 需要重新生成整个方案 |
| **无并行感知** | 多 Agent 可能产生冲突 |

**MVP 版本 Agent 本地工具函数**

```python
# MVP 版本：Agent 内置的本地工具（非 MCP）
# 注意：Agent 不需要 ROOM_TYPE_TAGS 映射表，直接读取 zone.tags

def list_modules_by_zone(zone: dict) -> list[dict]:
    """根据 Zone 的功能标签过滤模块"""
    library = load_json("modules/module_library.json")
    modules = library["modules"]

    # 直接读取 Server 预计算好的 tags
    tags = zone.get("tags", [])

    # 过滤包含任一标签的模块
    return [m for m in modules if any(t in m["tags"] for t in tags)]

def list_modules(tags: list[str] = None) -> list[dict]:
    """直接按标签过滤模块"""
    library = load_json("modules/module_library.json")
    modules = library["modules"]
    if tags:
        modules = [m for m in modules if any(t in m["tags"] for t in tags)]
    return modules

def get_zone(zone_id: str) -> dict:
    """读取设计区数据（含预计算的 tags）"""
    zones = load_json("computed/room_zones.json")["zones"]
    return next((z for z in zones if z["id"] == zone_id), None)

def get_exclusions(zone_id: str) -> list[dict]:
    """读取禁区数据"""
    exclusions = load_json("computed/exclusions.json")
    return [e for e in exclusions if e.get("zoneId") == zone_id]

def check_overlap_simple(bounds1, bounds2) -> bool:
    """简单矩形重叠检测（Agent 自行实现）"""
    # AABB 碰撞检测
    aabb1 = compute_aabb(bounds1)
    aabb2 = compute_aabb(bounds2)
    return not (aabb1.max_x < aabb2.min_x or aabb1.min_x > aabb2.max_x or
                aabb1.max_y < aabb2.min_y or aabb1.min_y > aabb2.max_y)

def write_modules(scheme_id: str, modules: list[dict]):
    """直接写入布置结果"""
    write_json(f"schemes/{scheme_id}/modules.json", modules)
```

**Server 预计算逻辑（生成 room_zones.json）**

```python
# Server 端代码：生成 room_zones.json 时使用
# Agent 不需要这段代码，仅供 Server 实现参考

ROOM_TYPE_TAGS = {
    "room:LivingRoom":     ["seating", "media", "storage", "lighting"],
    "room:MasterBedroom":  ["sleep", "storage", "dressing", "lighting"],
    "room:Bedroom":        ["sleep", "storage", "work", "lighting"],
    "room:DiningRoom":     ["dining", "storage", "lighting"],
    "room:Kitchen":        ["appliance", "storage"],
    "room:Bathroom":       ["appliance"],
    "room:Study":          ["work", "storage", "seating", "lighting"],
    "room:Balcony":        ["appliance", "seating"],
}

def generate_room_zones(rooms: list[dict]) -> dict:
    """Server 生成 room_zones.json 时，自动分配功能标签"""
    zones = []
    for room in rooms:
        room_type = room.get("type", "")
        zone_id = f"z{room['id'].replace('room_', '')}"

        zones.append({
            "id": zone_id,
            "roomId": room["id"],
            "reason": f"room:{room_type}",
            "tags": ROOM_TYPE_TAGS.get(f"room:{room_type}", []),
            "rawBoundary": room["boundary"],
            "innerBoundary": compute_inner_boundary(room),  # Server 计算
            "area_mm2": compute_area(room["boundary"])
        })

    return {"version": "1.0", "zones": zones}
```

---

### 4.2 完整版：MCP 工具驱动工作流

**设计理念**

> Agent 是"协作设计师"，Server 是"实时顾问"

```
Agent 通过 MCP 查询 → Server 实时响应 → Agent 决策 → Server 即时验证
```

**五阶段工作流**

```
┌─────────────────────────────────────────────────────────────────┐
│              完整版：MCP 工具驱动工作流                          │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  【阶段1】Server 预计算约束数据                                  │
│  ┌─────────────────────────────────────────────────────────────┐
│  │ Server 在项目初始化时生成（与 MVP 相同）：                    │
│  │ • computed/room_zones.json    （含 tags 字段）              │
│  │ • computed/exclusions.json                                  │
│  │ • computed/module_index.json    ← 新增：模块库索引缓存       │
│  └─────────────────────────────────────────────────────────────┘
│                              ↓                                  │
│  【阶段2】Agent 查询可用模块（MCP 调用）                         │
│  ┌─────────────────────────────────────────────────────────────┐
│  │ Agent:                                                       │
│  │   result = mcp_call("list_compatible_modules", {             │
│  │     "zoneId": "z1",                                         │
│  │     "preferences": { "style": "modern" }                    │
│  │   })                                                         │
│  │                                                              │
│  │ Server 返回（实时计算，基于 zone.tags）：                     │
│  │   {                                                          │
│  │     "zone_tags": ["sleep", "storage", "dressing"],          │
│  │     "available": [                                           │
│  │       { "moduleId": "mod_bed_001", "score": 0.95 },         │
│  │       { "moduleId": "mod_bed_002", "score": 0.80 }          │
│  │     ],                                                       │
│  │     "constraints": {                                         │
│  │       "available_area_mm2": 28500000,                       │
│  │       "exclusions_count": 2                                 │
│  │     }                                                        │
│  │   }                                                          │
│  └─────────────────────────────────────────────────────────────┘
│                              ↓                                  │
│  【阶段3】Agent 做出布置决策（AI 推理）                          │
│  ┌─────────────────────────────────────────────────────────────┐
│  │ Agent 考虑：                                                 │
│  │ • 设计规则（床头靠墙、与背景墙对齐）                          │
│  │ • Server 返回的约束信息                                      │
│  │ • 用户偏好和策略参数                                         │
│  │                                                              │
│  │ 决策输出：                                                   │
│  │   moduleId = "mod_bed_001"                                  │
│  │   center = [3000, 3250]                                     │
│  │   facing = "north"                                          │
│  └─────────────────────────────────────────────────────────────┘
│                              ↓                                  │
│  【阶段4】Server 实时验证（MCP 调用）                            │
│  ┌─────────────────────────────────────────────────────────────┐
│  │ Agent:                                                       │
│  │   result = mcp_call("place_module", {                        │
│  │     "moduleId": "mod_bed_001",                              │
│  │     "zoneId": "z1",                                         │
│  │     "center": [3000, 3250],                                 │
│  │     "facing": "north"                                       │
│  │   })                                                         │
│  │                                                              │
│  │ 成功返回：                                                    │
│  │   { "success": true, "moduleId": "m1",                       │
│  │     "bounds": [[...]], "status": "placed" }                 │
│  │                                                              │
│  │ 失败返回：                                                    │
│  │   { "success": false, "conflicts": [                         │
│  │       { "type": "overlap", "with": "exclusion_door_1" }     │
│  │     ],                                                       │
│  │     "suggested_positions": [[3500, 3250], [2500, 3250]]     │
│  │   }                                                          │
│  └─────────────────────────────────────────────────────────────┘
│                              ↓                                  │
│  【阶段5】Server 广播更新 + Agent 提交                           │
│  ┌─────────────────────────────────────────────────────────────┐
│  │ 每次 place_module 成功后：                                   │
│  │ • Server 通过 WebSocket 推送更新到 Web 前端                  │
│  │ • 用户实时看到布置变化                                       │
│  └─────────────────────────────────────────────────────────────┘
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

### 4.3 约束验证规则

> PlacementValidator 设计原则见 [Architecture.md §5.6](./Architecture.md#56-placementvalidator-设计原则)

**三条核心约束**：模块必须在 zone.innerBoundary 内、不与 exclusionAreas 重叠、不与其他 modules 重叠。

### 4.4 版本选择建议

| 场景 | 推荐版本 | 原因 |
|------|----------|------|
| 快速原型验证 | MVP | 无需实现 Server MCP 接口 |
| 单机本地开发 | MVP | 简单直接，易于调试 |
| Claude Code 集成 | MVP | Claude Code 直接操作文件 |
| 生产环境部署 | 完整版 | 实时验证，用户体验好 |
| 多 Agent 并行 | 完整版 | Server 协调避免冲突 |
| Web 实时预览 | 完整版 | WebSocket 推送更新 |

### 关键代码路径

- `BIMCanvas.Agent/placement_agent.py` - Agent 主逻辑
- `BIMCanvas.Server/McpTools/ModuleTools.cs` - module_add 实现
- `BIMCanvas.Core/Algorithms/Spatial/PlacementValidator.cs` - 约束验证

### 输出

**schemes/{s}/modules.json**：

```json
{
  "modules": [
    {
      "id": "m_1",
      "moduleId": "mod_bed_001",
      "zoneId": "z1",
      "bounds": {
        "center": [3000, 3250],
        "size": [1800, 2000],
        "rotation": 0
      },
      "facing": "north",
      "items": []
    }
  ]
}
```

---

## 5. Phase 5: 交互修改

### 触发条件

> 用户在 Web 画布拖拽家具，或通过对话指示 AI 修改

### 5.1 ChangeSource 机制

> 详细定义见 [Architecture.md](./Architecture.md)

每次修改都会携带 `changeSource` 字段，用于控制 Undo/Redo 历史管理：

| ChangeSource | 触发源 | Undo/Redo 策略 |
|--------------|--------|----------------|
| `UserUpload` | 用户上传新项目 | **清空历史** |
| `UserDrag` | 拖拽交互 | 合并连续拖拽为单次 |
| `AgentModify` | Agent 修改 | 正常记录 |
| `ServerCompute` | Server 计算 | 不记录（派生数据） |

### 5.2 子流程：拖拽修改

| 步骤 | 执行者 | 动作 | 输入 | 输出 |
|------|--------|------|------|------|
| 5.2.1 | 用户 | 拖拽家具 | 鼠标事件 | 新位置 |
| 5.2.2 | Web 前端 | 更新本地 JSON | 新坐标 | 本地状态更新 |
| 5.2.3 | Web 前端 | 发送位置变更 | module.bounds + changeSource | WebSocket 消息 |
| 5.2.4 | Server | 验证约束 | 新 bounds | 验证结果 |
| 5.2.5 | Server | 广播/拒绝 | 验证结果 | 状态同步 |

### 5.3 子流程：对话修改

| 步骤 | 执行者 | 动作 | 输入 | 输出 |
|------|--------|------|------|------|
| 5.3.1 | 用户 | 输入指令 | "把床转90度" | 文本 |
| 5.3.2 | AI | 解析意图 | 文本 | 操作意图 |
| 5.3.3 | AI | 调用 MCP 工具 | module_rotate | 执行结果 |
| 5.3.4 | Server | 执行修改 | 旋转参数 | 状态更新 |
| 5.3.5 | Server | 广播变更 | 新状态 | WebSocket 推送 |

### 流程图：拖拽修改

```
用户拖拽                             Web 前端                            Server
   │                                   │                                  │
   │  mousedown + mousemove            │                                  │
   ├──────────────────────────────────>│                                  │
   │                                   │                                  │
   │                                   │  更新本地 SVG 位置               │
   │                                   │  (乐观更新)                      │
   │                                   │                                  │
   │  mouseup                          │                                  │
   ├──────────────────────────────────>│                                  │
   │                                   │                                  │
   │                                   │  发送 module_move                 │
   │                                   │  { changeSource: "UserDrag" }    │
   │                                   ├─────────────────────────────────>│
   │                                   │                                  │
   │                                   │                      验证约束     │
   │                                   │                                  │
   │                                   │  成功: 广播新状态                 │
   │                                   │  失败: 回滚 + 错误提示            │
   │                                   │<─────────────────────────────────┤
```

### 关键代码路径

- `BIMCanvas.Web/src/components/Canvas/ModuleDrag.vue` - 拖拽交互
- `BIMCanvas.Server/Hubs/ProjectHub.cs` - 实时同步
- `BIMCanvas.Server/McpTools/ModuleTools.cs` - MCP 工具

---

## 6. Phase 6: 回写 Revit

### 触发条件

> 用户点击"应用到 Revit"按钮

### 子流程

| 步骤 | 执行者 | 动作 | 输入 | 输出 |
|------|--------|------|------|------|
| 6.1 | 用户 | 点击"应用到 Revit" | - | 触发命令 |
| 6.2 | Server | 导出最终 JSON | 项目数据 | JSON 数据 |
| 6.3 | Revit-MCP | 解析 modules | modules.json | 家具列表 |
| 6.4 | Revit-MCP | 加载族 | familyName | 族已加载 |
| 6.5 | Revit-MCP | 创建元素 | position + rotation | Revit 元素 |
| 6.6 | Revit | 显示结果 | 新元素 | 模型更新 |

### 坐标转换

```
JSON 坐标 (mm, Y-up)  →  Revit 坐标 (feet, Y-up)

position_revit = bounds.center / 304.8  (mm → feet)
rotation_revit = FacingToAngle(facing)
level = FindLevelById(levelId)
```

### 关键代码路径

- `BIMCanvas.Core/Converters/Revit/JsonToRevitConverter.cs` - 坐标转换
- Revit-MCP: `load_family_from_library` - 族加载
- Revit-MCP: `create_element` - 元素创建

---

## 7. 触发机制详解

MainAgent 支持三种触发方式：

### 7.1 AI 对话触发

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│  用户           │     │  Claude Code    │     │ MainAgent  │
│                 │     │  (CLI)          │     │ (Python)        │
└────────┬────────┘     └────────┬────────┘     └────────┬────────┘
         │                       │                       │
         │  "帮我布置客厅"        │                       │
         ├──────────────────────>│                       │
         │                       │                       │
         │                       │  Agent.run(prompt)    │
         │                       ├──────────────────────>│
         │                       │                       │
         │                       │                       │  执行布置
         │                       │                       │  调用 MCP
         │                       │                       │
         │                       │  返回结果              │
         │                       │<──────────────────────┤
         │                       │                       │
         │  "客厅已布置完成"       │                       │
         │<──────────────────────┤                       │
```

**适用场景**：用户通过自然语言与 AI 对话

### 7.2 Web 按钮触发

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│  Web 前端       │     │  BIMCanvas.Server│     │ MainAgent  │
│                 │     │                 │     │ (SSE 监听)       │
└────────┬────────┘     └────────┬────────┘     └────────┬────────┘
         │                       │                       │
         │  点击"一键布置"        │                       │
         ├──────────────────────>│                       │
         │                       │                       │
         │                       │  EventBus.Publish     │
         │                       │  (PlacementRequested) │
         │                       ├──────────────────────>│  SSE: 事件推送
         │                       │                       │
         │                       │                       │  MainAgent
         │                       │                       │  处理事件
         │                       │                       │  调用 MCP
         │                       │                       │
         │  WebSocket: 状态更新   │                       │
         │<──────────────────────┤<──────────────────────┤
```

**适用场景**：用户在 Web 界面点击快捷按钮

### 7.3 自动修正触发

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│  Server         │     │  EventBus       │     │ MainAgent  │
│  (验证检测)      │     │                 │     │ (SSE 监听)       │
└────────┬────────┘     └────────┬────────┘     └────────┬────────┘
         │                       │                       │
         │  检测到约束违反        │                       │
         │  (bounds 越界)        │                       │
         │                       │                       │
         │  Publish(AutoFixReq)  │                       │
         ├──────────────────────>│                       │
         │                       │                       │
         │                       │  SSE: 事件推送        │
         │                       ├──────────────────────>│
         │                       │                       │
         │                       │                       │  自动修正
         │                       │                       │  重新计算
         │                       │                       │  调用 MCP
         │                       │                       │
         │                       │  修正完成             │
         │<──────────────────────┤<──────────────────────┤
```

**适用场景**：系统自动检测并修正布置错误

---

## 8. 错误处理流程

### 8.1 约束违反处理

```
┌────────────────────────────────────────────────────────────────────────────┐
│  约束违反检测流程                                                           │
├────────────────────────────────────────────────────────────────────────────┤
│                                                                            │
│  操作请求 (module_add / module_move)                                        │
│       │                                                                    │
│       ▼                                                                    │
│  ┌──────────────────────────────────────┐                                  │
│  │  PlacementValidator.Validate(bounds) │                                  │
│  └──────────────────────────────────────┘                                  │
│       │                                                                    │
│       ├─── 通过 ──────────────────────────────────> 执行操作               │
│       │                                                                    │
│       └─── 失败 ──────────────────────────────────> 返回错误               │
│                                                      │                     │
│                                                      ▼                     │
│                                          ┌────────────────────────┐        │
│                                          │  错误类型:             │        │
│                                          │  - OUTSIDE_BOUNDARY    │        │
│                                          │  - EXCLUSION_OVERLAP   │        │
│                                          │  - MODULE_COLLISION    │        │
│                                          └────────────────────────┘        │
│                                                      │                     │
│                                                      ▼                     │
│                                          ┌────────────────────────┐        │
│                                          │  处理方式:             │        │
│                                          │  - 拒绝操作            │        │
│                                          │  - 提示用户            │        │
│                                          │  - 触发自动修正        │        │
│                                          └────────────────────────┘        │
│                                                                            │
└────────────────────────────────────────────────────────────────────────────┘
```

### 8.2 连接断开恢复

```
Web 前端                                                        Server
   │                                                              │
   │  WebSocket 连接断开                                           │
   │                                                              │
   │  ┌─────────────────────────────────────────────────────────┐ │
   │  │  重连策略:                                              │ │
   │  │  1. 立即重试 (1次)                                       │ │
   │  │  2. 指数退避 (1s, 2s, 4s, 8s, 16s)                       │ │
   │  │  3. 最大重试 5 次                                        │ │
   │  │  4. 失败后显示"连接断开"提示                              │ │
   │  └─────────────────────────────────────────────────────────┘ │
   │                                                              │
   │  重连成功后                                                   │
   │  ├──────────────────────────────────────────────────────────>│
   │                                                              │
   │  请求完整状态同步                                             │
   │  ├──────────────────────────────────────────────────────────>│
   │                                                              │
   │  返回最新项目数据                                              │
   │  <──────────────────────────────────────────────────────────┤
   │                                                              │
   │  重新渲染画布                                                 │
   │                                                              │
```

### 8.3 版本冲突回滚

```
┌────────────────────────────────────────────────────────────────────────────┐
│  乐观锁冲突处理                                                             │
├────────────────────────────────────────────────────────────────────────────┤
│                                                                            │
│  AI 调用 MCP 工具                                                          │
│  {                                                                         │
│    "tool": "module_move",                                                  │
│    "params": {                                                             │
│      "projectId": "proj_001",                                              │
│      "expectedVersion": 42,  ← 期望版本                                    │
│      "moduleId": "m_001",                                                  │
│      "position": [5000, 3000]                                              │
│    }                                                                       │
│  }                                                                         │
│       │                                                                    │
│       ▼                                                                    │
│  Server 检查版本                                                           │
│       │                                                                    │
│       ├─── currentVersion == 42 ──────────> 执行操作，version++            │
│       │                                                                    │
│       └─── currentVersion == 43 ──────────> 返回冲突错误                   │
│                                              {                             │
│                                                "success": false,           │
│                                                "error": "VERSION_CONFLICT",│
│                                                "currentVersion": 43,       │
│                                                "hint": "请重新获取状态"     │
│                                              }                             │
│                                                                            │
│  AI 收到冲突后:                                                            │
│  1. 调用 project_describe() 获取最新状态                                    │
│  2. 重新评估操作是否仍然有效                                                │
│  3. 使用新版本号重试                                                       │
│                                                                            │
└────────────────────────────────────────────────────────────────────────────┘
```

### 8.4 Agent 异常恢复

| 异常类型 | 检测方式 | 恢复策略 |
|----------|----------|----------|
| Agent 进程崩溃 | 健康检查超时 | 自动重启 Agent |
| MCP 调用失败 | 工具返回错误 | 重试 + 降级 |
| SSE 断开 | 心跳超时 | 重新连接 |
| 布置超时 | 操作超时 | 取消 + 通知用户 |

---

## 9. 项目结构工作流 (v3.0)

> 上下文: [Schema.md](./Schema.md)

### 9.1 策略管理

#### 9.1.1 创建新策略（并行开发）

**目标**: 开始新的设计方向（如"空间优先"），独立于其他策略。

1. **创建文件夹**: `mkdir schemes/s2_Space`
2. **初始化 Git**: `cd schemes/s2_Space && git init`
3. **链接基线**: 创建 `strategy.json`，`baselineRef` 指向 `../../baseline`
4. **初始提交**: `git add . && git commit -m "Initial commit"`

#### 9.1.2 将变体提升为策略（派生）

**目标**: 将成功的变体（如 `v1_backup`）升级为完整独立策略。

1. **复制文件夹**: `cp -r schemes/s1_Flow schemes/s3_FromV1`
2. **切换分支**: `cd schemes/s3_FromV1 && git checkout v1_backup`
3. **重置分支**: `git branch -m v1_backup main`（可选：设为新主分支）
4. **更新元数据**: 编辑 `strategy.json` 添加 `origin` 信息
5. **注册**: 将 `s3_FromV1` 添加到 `manifest.json`

### 9.2 变体管理

#### 9.2.1 创建变体（线性历史）

**目标**: 保存快照或尝试子想法，不影响主策略。

1. **分支**: `git checkout -b v1_experiment`
2. **修改**: 编辑 `modules.json` 或 `zones.json`
3. **提交**: `git commit -am "Try open kitchen layout"`

#### 9.2.2 切换变体（回溯）

**目标**: 恢复到先前状态。

1. **切换**: `git checkout main` 或 `git checkout <commit_hash>`
2. **重新加载**: 应用从文件系统重新加载数据

### 9.3 基线管理

#### 9.3.1 更新基线

**目标**: 与最新 Revit 模型变更同步。

1. **导出**: Revit 插件导出到 `baseline/` 文件夹
2. **验证**:
    * 应用计算 `baseline/` 的新哈希值
    * 与 `strategy.json` 的 `lastValidatedBaselineHash` 比较
    * 如不匹配，标记策略为 `dirty`
3. **解决**: 用户手动验证策略并更新 `lastValidatedBaselineHash`

---

## 附录 A: 数据结构规范

### module_library.json vs modules.json

| 文件 | 职责 | 类比 | 读写属性 | 数据来源 |
|------|------|------|----------|----------|
| **module_library.json** | 设计素材库 | "家具目录" | 只读 | 预先准备的家具资源 |
| **modules.json** | 布置结果 | "装修清单" | 可写 | Agent 生成的布置方案 |

**module_library.json 结构**：
```json
{
  "modules": [
    {
      "id": "mod_bed_001",
      "name": "双人床",
      "tags": ["sleep"],
      "size": { "width": 1800, "depth": 2000 },
      "svgPath": "modules/assets/mod_bed_001.svg"
    }
  ]
}
```

**modules.json 结构**：
```json
{
  "modules": [
    {
      "id": "m_1",
      "moduleId": "mod_bed_001",
      "zoneId": "z1",
      "bounds": {
        "center": [3000, 3250],
        "size": [1800, 2000],
        "rotation": 0
      },
      "facing": "north",
      "items": []
    }
  ]
}
```

### 房间类型 → 功能标签对照表

> **注意**：此表由 Server 持有，Agent 只读取预计算好的 tags。

| 房间类型 | 功能标签 | 说明 |
|----------|----------|------|
| `LivingRoom` | seating, media, storage, lighting | 客厅 |
| `MasterBedroom` | sleep, storage, dressing, lighting | 主卧 |
| `Bedroom` | sleep, storage, work, lighting | 次卧 |
| `DiningRoom` | dining, storage, lighting | 餐厅 |
| `Kitchen` | appliance, storage | 厨房 |
| `Bathroom` | appliance | 卫生间 |
| `Study` | work, storage, seating, lighting | 书房 |
| `Balcony` | appliance, seating | 阳台 |

---

## 附录 B: 相关文档

| 文档 | 路径 | 内容 |
|------|------|------|
| 系统架构 | [Architecture.md](./Architecture.md) | 模块设计、数据流、File-Driven 架构 |
| 数据模型 | [Schema.md](./Schema.md) | JSON 字段定义 (v3.0) |
| MCP 工具 | [Arch_MCP_Tools.md](./Arch_MCP_Tools.md) | 工具 API 规范 |
| 产品需求 | [PRD.md](./PRD.md) | 业务需求 |

---

## 附录 C: 版本历史

| 版本 | 日期 | 变更 |
|------|------|------|
| v1.0 | 2025-12-06 | 初始版本，从 Architecture.md 提取并扩展 |
| v2.0 | 2026-01-13 | 合并 Server_Agent_Workflow，补充 MVP/完整版工作流、ChangeSource 机制 |
