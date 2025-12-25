# BIMCanvas 执行流程文档

> 版本：v1.0
> 更新日期：2025-12-06
> 本文档描述 BIMCanvas 的端到端执行流程

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
│  │  Revit 提取 → outline + rooms                                           │ │
│  └─────────────────────────────────────────────────────────────────────────┘ │
│                                     │                                        │
│                                     ▼                                        │
│  ┌─────────────────────────────────────────────────────────────────────────┐ │
│  │  Phase 2: 数据处理                                                       │ │
│  │  Server 计算 → zones + innerBoundary + exclusionAreas + wallFinishes    │ │
│  └─────────────────────────────────────────────────────────────────────────┘ │
│                                     │                                        │
│                                     ▼                                        │
│  ┌─────────────────────────────────────────────────────────────────────────┐ │
│  │  Phase 3: 区域确认                                                       │ │
│  │  AI 推断 tags → 用户确认                                                 │ │
│  └─────────────────────────────────────────────────────────────────────────┘ │
│                                     │                                        │
│                                     ▼                                        │
│  ┌─────────────────────────────────────────────────────────────────────────┐ │
│  │  Phase 4: 方案生成                                                       │ │
│  │  PlacementAgent → modules[] 布置方案                                     │ │
│  └─────────────────────────────────────────────────────────────────────────┘ │
│                                     │                                        │
│                                     ▼                                        │
│  ┌─────────────────────────────────────────────────────────────────────────┐ │
│  │  Phase 5: 交互修改                                                       │ │
│  │  拖拽 / 对话 ←→ 循环迭代                                                 │ │
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
| Phase 1 | 用户点击"开始设计" | BIMCanvas.Revit | 精简版 CanvasDocument | 即时 |
| Phase 2 | Server 收到 POST | BIMCanvas.Server | 完整版 CanvasDocument | < 1s |
| Phase 3 | Web 收到推送 | PlacementAgent + 用户 | zones[].tags 确认 | 用户决定 |
| Phase 4 | 用户确认区域 | PlacementAgent | modules[] | 数秒 |
| Phase 5 | 用户操作 | Web + Server + AI | 更新的 modules[] | 循环 |
| Phase 6 | 用户点击"应用" | Revit-MCP | Revit 家具实例 | 数秒 |

### 0.3 组件交互概览

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
         │                       │     │  PlacementAgent (Python)           │
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
| 1.3 | Revit 插件 | 提取边界轮廓 | 视图中的 Wall/Column 元素 | outline.boundaries[] |
| 1.4 | Revit 插件 | 提取门窗线段 | Door/Window 元素 | outline.openings[] |
| 1.5 | Revit 插件 | 识别物理房间 | Room 元素 | rooms[] |
| 1.6 | Revit 插件 | 组装 JSON | 上述数据 | CanvasDocument (精简版) |
| 1.7 | Revit 插件 | POST 到 Server | JSON | HTTP 响应 |

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
   │                                  │  outline.boundaries[]            │
   │                                  │                                  │
   │                                  │  提取 Door/Window                 │
   │                                  ├─────────────┐                    │
   │                                  │             │                    │
   │                                  │<────────────┘                    │
   │                                  │  outline.openings[]              │
   │                                  │                                  │
   │                                  │  识别 Room 元素                   │
   │                                  ├─────────────┐                    │
   │                                  │             │                    │
   │                                  │<────────────┘                    │
   │                                  │  rooms[]                         │
   │                                  │                                  │
   │                                  │  POST CanvasDocument              │
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

**精简版 CanvasDocument**：

```json
{
  "metadata": { "version": "2.5", "levelId": "level_001" },
  "outline": {
    "walls": [[[0, 0], [10000, 0], [10000, 8000], [0, 8000], [0, 0]]],
    "openings": [{ "type": "door", "line": [[2000, 0], [3000, 0]] }]
  },
  "rooms": [
    { "id": "room_001", "name": "客厅", "type": "living", "boundary": [...] }
  ],
  "zones": [],
  "wallFinishes": [],
  "modules": []
}
```

---

## 2. Phase 2: 数据处理

### 触发条件

> Server 收到 Revit 提交的 CanvasDocument POST 请求

### 子流程

| 步骤 | 执行者 | 动作 | 输入 | 输出 |
|------|--------|------|------|------|
| 2.1 | CanvasController | 接收 POST 请求 | 精简版 JSON | 反序列化对象 |
| 2.2 | ZoneCalculator | 从 rooms 生成 zones | rooms[] | zones[] (初始) |
| 2.3 | ZoneCalculator | 计算 innerBoundary | zone.rawBoundary + wallFinishes | zone.innerBoundary |
| 2.4 | ZoneCalculator | 计算门扇禁区 | outline.openings (door) | zone.exclusionAreas[] |
| 2.5 | ZoneCalculator | 生成墙面完成面 | rooms + 规则表 | wallFinishes[] |
| 2.6 | CanvasStateManager | 存储画布状态 | 完整 CanvasDocument | canvasId |
| 2.7 | CanvasHub | WebSocket 推送 | CanvasDocument | 前端接收 |

### 流程图

```
Server 接收 POST
       │
       ▼
┌──────────────────────────────────────────────────────────────┐
│  ZoneCalculator.Process(document)                            │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│  // 1. 生成 zones（复制 rawBoundary）                         │
│  for each room in rooms:                                     │
│      zone = CreateZoneFromRoom(room)                         │
│      zone.rawBoundary = room.boundary  // 结构墙内表面        │
│      zone.tags = InferTagsFromRoomType(room.type)            │
│                                                              │
│  // 2. 生成 wallFinishes（RoomDefault 规则）                  │
│  for each wall adjacent to rooms:                            │
│      finish.locationLine = wall.innerSurface                 │
│      finish.thickness = LookupRoomDefault(room.type)         │
│      finish.exclusionBoundary = Expand(locationLine, thickness)
│      wallFinishes.Add(finish)                                │
│                                                              │
│  // 3. 计算 innerBoundary                                    │
│  for each zone in zones:                                     │
│      finishes = GetFinishesOnZoneBoundary(zone)              │
│      zone.innerBoundary = Shrink(rawBoundary, finishes)      │
│                                                              │
│  // 4. 计算门扇禁区                                           │
│  for each opening in outline.openings where type == "door":  │
│      zone = FindContainingZone(opening)                      │
│      exclusion = ComputeDoorSwingRect(opening)               │
│      zone.exclusionAreas.Add(exclusion)                      │
│                                                              │
└──────────────────────────────────────────────────────────────┘
       │
       ▼
WebSocket 推送完整版 CanvasDocument
       │
       ▼
Web 前端渲染户型底图
```

### 关键代码路径

- `BIMCanvas.Server/Controllers/CanvasController.cs` - REST 入口
- `BIMCanvas.Server/Services/ZoneCalculator.cs` - 核心计算逻辑
- `BIMCanvas.Server/Services/CanvasStateManager.cs` - 状态管理
- `BIMCanvas.Server/Hubs/CanvasHub.cs` - WebSocket 推送

### 输出

**完整版 CanvasDocument** 新增字段：

```json
{
  "zones": [
    {
      "id": "zone_001",
      "roomId": "room_001",
      "tags": ["sitting", "entertainment"],
      "rawBoundary": [...],
      "innerBoundary": [...],
      "exclusionAreas": [
        { "type": "doorSwing", "boundary": [[2000, 0], [3000, 0], [3000, 800], [2000, 800]] }
      ]
    }
  ],
  "wallFinishes": [
    { "locationLine": [[0, 0], [0, 8000]], "thickness": 50, "exclusionBoundary": [...] }
  ]
}
```

---

## 3. Phase 3: 区域确认

### 触发条件

> Web 前端收到 WebSocket 推送的完整版 CanvasDocument

### 子流程

| 步骤 | 执行者 | 动作 | 输入 | 输出 |
|------|--------|------|------|------|
| 3.1 | Web 前端 | 渲染户型底图 | CanvasDocument | SVG 显示 |
| 3.2 | Web 前端 | 显示 zone 功能标签 | zones[].tags | UI 标签 |
| 3.3 | 用户 | 查看/修改功能分配 | 当前 tags | 确认或修改 |
| 3.4 | Web 前端 | 发送 tags 更新 | 修改后的 tags | WebSocket 消息 |
| 3.5 | Server | 更新 zones[].tags | 新 tags | 状态更新 |
| 3.6 | Server | 检查 ZoneOverride 规则 | 新 tags + 规则表 | 受影响的墙面 |
| 3.7 | ZoneCalculator | 更新 wallFinishes | ZoneOverride 规则 | 新 thickness |
| 3.8 | ZoneCalculator | 重算 innerBoundary | 更新的 wallFinishes | 新 innerBoundary |
| 3.9 | CanvasHub | 广播变更 | zones + wallFinishes | WebSocket 推送 |

### 流程图

```
Web 前端                              用户                              Server
   │                                   │                                  │
   │  渲染户型图 + zone 标签            │                                  │
   ├──────────────────────────────────>│                                  │
   │                                   │                                  │
   │                                   │  查看功能标签                     │
   │                                   │  (sitting, entertainment, ...)   │
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

### 关键代码路径

- `BIMCanvas.Web/src/components/Canvas/SvgCanvas.vue` - 画布渲染
- `BIMCanvas.Web/src/components/ZoneEditor.vue` - 区域编辑
- `BIMCanvas.Server/Hubs/CanvasHub.cs` - 消息处理

### 输出

用户确认的 zones[].tags + 更新的 wallFinishes[] + innerBoundary，进入 Phase 4

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

**处理流程**

1. **Phase 2 初始计算**：使用 RoomDefault 规则，根据 Room.type 查询默认完成面厚度
2. **Phase 3 tags 变化**：检查 ZoneOverride 规则，若匹配则更新 thickness
3. **重算链路**：thickness 变化 → exclusionBoundary 变化 → innerBoundary 变化
4. **广播变更**：推送更新的 zones[] 和 wallFinishes[] 到 Web 端

**ZoneOverride 常见规则**

| Tag | 完成面类型 | Thickness | 说明 |
|-----|-----------|-----------|------|
| tv_media | 护墙板 | 80mm | 电视背景墙 |
| storage | 柜体 | 600mm | 嵌入式收纳 |

---

## 4. Phase 4: 方案生成

### 触发条件

> 用户确认区域后，或点击"一键布置"按钮

### 触发方式分支

详见 [§7 触发机制详解](#7-触发机制详解)

### 子流程

| 步骤 | 执行者 | 动作 | 输入 | 输出 |
|------|--------|------|------|------|
| 4.1 | 触发源 | 发送布置请求 | canvasId + zoneIds | 事件/调用 |
| 4.2 | PlacementAgent | 读取画布状态 | canvasId | CanvasDocument |
| 4.3 | PlacementAgent | 根据 tags 选择模块 | zone.tags | 候选模块列表 |
| 4.4 | PlacementAgent | 计算布置位置 | innerBoundary + exclusionAreas | 位置方案 |
| 4.5 | PlacementAgent | 验证约束合规 | bounds vs constraints | 验证结果 |
| 4.6 | PlacementAgent | 调用 module_add | 验证通过的模块 | modules[] 更新 |
| 4.7 | Server | 广播变更 | 新 CanvasDocument | WebSocket 推送 |

### 流程图

```
触发源                            PlacementAgent                        Server
   │                                   │                                  │
   │  布置请求 (canvasId, zoneIds)      │                                  │
   ├──────────────────────────────────>│                                  │
   │                                   │                                  │
   │                                   │  MCP: canvas_describe             │
   │                                   ├─────────────────────────────────>│
   │                                   │                                  │
   │                                   │  返回 CanvasDocument              │
   │                                   │<─────────────────────────────────┤
   │                                   │                                  │
   │                                   │  分析 zone.tags                   │
   │                                   │  选择适用模块                      │
   │                                   │  计算放置位置                      │
   │                                   ├─────────────┐                    │
   │                                   │             │                    │
   │                                   │<────────────┘                    │
   │                                   │                                  │
   │                                   │  循环: for each module            │
   │                                   │    验证: bounds ⊆ innerBoundary   │
   │                                   │    验证: bounds ∩ exclusion = ∅   │
   │                                   │    验证: bounds ∩ others = ∅      │
   │                                   │                                  │
   │                                   │  MCP: module_add                  │
   │                                   ├─────────────────────────────────>│
   │                                   │                                  │
   │                                   │                      WebSocket 广播
   │                                   │                                  │
```

### 约束验证规则

```
对于每个要放置的模块 M:
  1. M.bounds ⊆ zone.innerBoundary    (完全在可用空间内)
  2. M.bounds ∩ zone.exclusionAreas = ∅  (不与禁区重叠)
  3. M.bounds ∩ otherModules.bounds = ∅  (不与其他模块重叠)
```

### 关键代码路径

- `BIMCanvas.Agent/placement_agent.py` - Agent 主逻辑
- `BIMCanvas.Server/McpTools/ModuleTools.cs` - module_add 实现
- `BIMCanvas.Core/Algorithms/Spatial/PlacementValidator.cs` - 约束验证

### 输出

填充的 modules[] 数组：

```json
{
  "modules": [
    {
      "id": "m_001",
      "zoneId": "zone_001",
      "templateId": "sofa_l_3000",
      "bounds": [[1500, 2000], [4500, 2000], [4500, 4500], [1500, 4500]],
      "facing": "north",
      "items": [
        { "familyName": "沙发", "offset": [0, 0], "rotation": 0 }
      ]
    }
  ]
}
```

---

## 5. Phase 5: 交互修改

### 触发条件

> 用户在 Web 画布拖拽家具，或通过对话指示 AI 修改

### 5.1 子流程：拖拽修改

| 步骤 | 执行者 | 动作 | 输入 | 输出 |
|------|--------|------|------|------|
| 5.1.1 | 用户 | 拖拽家具 | 鼠标事件 | 新位置 |
| 5.1.2 | Web 前端 | 更新本地 JSON | 新坐标 | 本地状态更新 |
| 5.1.3 | Web 前端 | 发送位置变更 | module.bounds | WebSocket 消息 |
| 5.1.4 | Server | 验证约束 | 新 bounds | 验证结果 |
| 5.1.5 | Server | 广播/拒绝 | 验证结果 | 状态同步 |

### 5.2 子流程：对话修改

| 步骤 | 执行者 | 动作 | 输入 | 输出 |
|------|--------|------|------|------|
| 5.2.1 | 用户 | 输入指令 | "把床转90度" | 文本 |
| 5.2.2 | AI | 解析意图 | 文本 | 操作意图 |
| 5.2.3 | AI | 调用 MCP 工具 | module_rotate | 执行结果 |
| 5.2.4 | Server | 执行修改 | 旋转参数 | 状态更新 |
| 5.2.5 | Server | 广播变更 | 新状态 | WebSocket 推送 |

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
   │                                   ├─────────────────────────────────>│
   │                                   │                                  │
   │                                   │                      验证约束     │
   │                                   │                                  │
   │                                   │  成功: 广播新状态                 │
   │                                   │  失败: 回滚 + 错误提示            │
   │                                   │<─────────────────────────────────┤
```

### 流程图：对话修改

```
用户                                  Claude Code                         Server
   │                                   │                                  │
   │  "把床转90度"                      │                                  │
   ├──────────────────────────────────>│                                  │
   │                                   │                                  │
   │                                   │  解析意图                         │
   │                                   │  → module_rotate(id, 90)         │
   │                                   │                                  │
   │                                   │  MCP: module_rotate               │
   │                                   ├─────────────────────────────────>│
   │                                   │                                  │
   │                                   │                      执行旋转     │
   │                                   │                      验证约束     │
   │                                   │                      广播变更     │
   │                                   │                                  │
   │                                   │  返回成功                         │
   │                                   │<─────────────────────────────────┤
   │                                   │                                  │
   │  "床已旋转90度"                    │                                  │
   │<──────────────────────────────────┤                                  │
```

### 关键代码路径

- `BIMCanvas.Web/src/components/Canvas/ModuleDrag.vue` - 拖拽交互
- `BIMCanvas.Server/Hubs/CanvasHub.cs` - 实时同步
- `BIMCanvas.Server/McpTools/ModuleTools.cs` - MCP 工具

### 输出

更新后的 modules[]，循环迭代直到用户满意

---

## 6. Phase 6: 回写 Revit

### 触发条件

> 用户点击"应用到 Revit"按钮

### 子流程

| 步骤 | 执行者 | 动作 | 输入 | 输出 |
|------|--------|------|------|------|
| 6.1 | 用户 | 点击"应用到 Revit" | - | 触发命令 |
| 6.2 | Server | 导出最终 JSON | CanvasDocument | JSON 数据 |
| 6.3 | Revit-MCP | 解析 modules | modules[] | 家具列表 |
| 6.4 | Revit-MCP | 加载族 | familyName | 族已加载 |
| 6.5 | Revit-MCP | 创建元素 | position + rotation | Revit 元素 |
| 6.6 | Revit | 显示结果 | 新元素 | 模型更新 |

### 流程图

```
用户                              Server                            Revit-MCP / Revit
   │                                │                                     │
   │  点击"应用到 Revit"             │                                     │
   ├───────────────────────────────>│                                     │
   │                                │                                     │
   │                                │  导出 CanvasDocument                 │
   │                                ├─────────────────────────────────────>│
   │                                │                                     │
   │                                │                    解析 modules[]    │
   │                                │                                     │
   │                                │                    for each module:  │
   │                                │                      for each item:  │
   │                                │                        load_family   │
   │                                │                        create_element│
   │                                │                                     │
   │                                │                    返回创建结果       │
   │                                │<─────────────────────────────────────┤
   │                                │                                     │
   │  显示成功消息                   │                                     │
   │<───────────────────────────────┤                                     │
   │                                │                                     │
   │                                │                    Revit 模型已更新  │
```

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

### 输出

Revit 模型中创建的家具实例

---

## 7. 触发机制详解

PlacementAgent 支持三种触发方式：

### 7.1 AI 对话触发

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│  用户           │     │  Claude Code    │     │ PlacementAgent  │
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
│  Web 前端       │     │  BIMCanvas.Server│     │ PlacementAgent  │
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
         │                       │                       │  PlacementAgent
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
│  Server         │     │  EventBus       │     │ PlacementAgent  │
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
   │  返回最新 CanvasDocument                                      │
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
│      "canvasId": "canvas_001",                                             │
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
│  1. 调用 canvas_describe() 获取最新状态                                     │
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

## 附录 A: 相关文档

| 文档 | 路径 | 内容 |
|------|------|------|
| 系统架构 | `docs/Architecture.md` | 模块设计、技术决策 |
| 数据模型 | `docs/Schema-JSON.md` | JSON 字段定义 |
| MCP 工具 | `docs/MCP-Tools-Spec.md` | 工具 API 规范 |
| 产品需求 | `docs/PRD.md` | 业务需求 |

---

## 附录 B: 版本历史

| 版本 | 日期 | 变更 |
|------|------|------|
| v1.0 | 2025-12-06 | 初始版本，从 Architecture.md 提取并扩展 |

---

## 9. Project Structure Workflows (v3.0)

> Context: [Schema-v3.0.md](./Schema-v3.0.md)

### 9.1 Strategy Management

#### 9.1.1 Create New Strategy (Parallel Development)

**Goal**: Start a new design direction (e.g., "Space First") independent of others.

1.  **Create Folder**: `mkdir schemes/s2_Space`
2.  **Initialize Git**: `cd schemes/s2_Space && git init`
3.  **Link Baseline**: Create `strategy.json` with `baselineRef` pointing to `../../baseline`.
4.  **Initial Commit**: `git add . && git commit -m "Initial commit"`

#### 9.1.2 Promote Variant to Strategy (Derivation)

**Goal**: Upgrade a successful variant (e.g., `v1_backup`) to a full independent strategy.

1.  **Copy Folder**: `cp -r schemes/s1_Flow schemes/s3_FromV1`
2.  **Checkout Branch**: `cd schemes/s3_FromV1 && git checkout v1_backup`
3.  **Reset Branch**: `git branch -m v1_backup main` (Optional: make it the new main)
4.  **Update Metadata**: Edit `strategy.json` to add `origin` info (Source Repo/Commit).
5.  **Register**: Add `s3_FromV1` to `project.json`.

### 9.2 Variant Management

#### 9.2.1 Create Variant (Linear History)

**Goal**: Save a snapshot or try a sub-idea without affecting the main strategy.

1.  **Branch**: `git checkout -b v1_experiment`
2.  **Modify**: Edit `modules.json` or `zones.json`.
3.  **Commit**: `git commit -am "Try open kitchen layout"`

#### 9.2.2 Switch Variant (Backtracking)

**Goal**: Revert to a previous state.

1.  **Checkout**: `git checkout main` or `git checkout <commit_hash>`
2.  **Reload**: App reloads data from the file system.

### 9.3 Baseline Management

#### 9.3.1 Update Baseline

**Goal**: Sync with latest Revit model changes.

1.  **Export**: Revit plugin exports to `baseline/` folder.
2.  **Validation**:
    *   App calculates new Hash of `baseline/`.
    *   App compares with `strategy.json`'s `lastValidatedBaselineHash`.
    *   If mismatch, mark strategy as `dirty`.
3.  **Resolution**: User manually verifies strategies and updates `lastValidatedBaselineHash`.

