# BIMCanvas JSON Schema 规范

> 版本：v1.0
> 更新日期：2025-12-02
> 状态：已定稿

---

## 1. 设计理念

### 1.1 核心原则："JSON 为骨，SVG 为皮"

| 层面 | 格式 | 职责 |
|------|------|------|
| **数据层（骨）** | JSON | 存储、传输、AI 交互、业务逻辑 |
| **视图层（皮）** | SVG | 渲染、显示、视觉反馈 |

**为什么选择 JSON 作为核心数据格式？**

| 对比项 | JSON | SVG |
|--------|------|-----|
| Token 效率 | 高（结构紧凑） | 低（大量标签冗余） |
| AI 理解能力 | 强（键值对直观） | 弱（需解析路径数据） |
| 空间推理 | 易（坐标直接可读） | 难（需解析 transform） |
| 自定义属性 | 原生支持 | 需命名空间，兼容性差 |
| 版本控制 | 易 diff | 难 diff |

### 1.2 数据流

```
【AI 操作画布】
AI 调用 MCP 工具 → 修改 JSON 数据 → WebSocket 推送 → 前端渲染 SVG

【用户操作画布】
用户交互 → 前端修改本地 JSON → 点击同步 → 服务端更新 → AI 感知变更

【AI 视觉验证】
JSON 数据 → 服务端渲染 → PNG 截图 → 发送给 AI（多模态）
```

### 1.3 坐标系统

- **类型**：笛卡尔坐标系（Cartesian）
- **原点**：视图裁剪框左下角
- **X 轴**：向右为正
- **Y 轴**：向上为正（与数学直觉一致）
- **单位**：毫米 (mm)
- **旋转**：顺时针为正，单位为度

> ⚠️ **重要**：这是 CAD 标准坐标系，与 Web 屏幕坐标系（Y 向下）相反。
> 前端渲染时必须进行显式坐标转换：`y_screen = canvasHeight - y_model * scale`
> **禁止**使用 CSS `scaleY(-1)` 翻转，会导致文字倒置等副作用。

### 1.4 网格系统

- **默认网格大小**：500mm × 500mm
- **网格坐标计算**：
  - `col = Math.floor(x / gridSize)`  // 列号，从左往右递增
  - `row = Math.floor(y / gridSize)`  // 行号，从下往上递增（Y 向上）
- **用途**：帮助 AI 更直观地理解空间位置

---

## 2. CanvasDocument 根对象

### 2.1 完整结构

```json
{
  "id": "canvas_001",
  "name": "客厅设计方案",
  "version": 42,
  "createdAt": "2025-12-02T10:00:00Z",
  "updatedAt": "2025-12-02T15:30:00Z",

  "metadata": {
    "sourceType": "revit",
    "revitProjectId": "project_abc",
    "revitViewId": 12345,
    "designIntent": "现代简约风格三口之家客厅",
    "projectConfig": {
      "style": "modern",
      "spaceType": "livingRoom",
      "budget": "standard",
      "familyMembers": { "adults": 2, "children": 1 }
    },
    "revitMapping": {
      "projectBaseOffset": { "x": -12000, "y": -8000 },
      "rotationToTrueNorth": 0
    }
  },

  "coordinateSystem": {
    "origin": "viewBoundingBoxBottomLeft",
    "xAxis": "right",
    "yAxis": "up",
    "unit": "mm"
  },

  "aiHints": {
    "northDirection": "up",
    "entranceDirection": "south",
    "primaryViewingAngle": "north"
  },

  "bounds": {
    "width": 8000,
    "height": 6000
  },

  "grid": {
    "size": 500,
    "visible": true
  },

  "structure": { ... },
  "zones": [ ... ],
  "elements": [ ... ],
  "spatialRelations": [ ... ],
  "pendingCommits": [ ... ]
}
```

### 2.2 字段说明

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `id` | string | 是 | 画布唯一标识，格式：`canvas_{uuid}` |
| `name` | string | 是 | 画布名称，用户可见 |
| `version` | number | 是 | 版本号，每次修改递增，用于乐观锁 |
| `createdAt` | string | 是 | 创建时间，ISO 8601 格式 |
| `updatedAt` | string | 是 | 最后更新时间 |
| `metadata` | object | 是 | 元数据，见 §2.3 |
| `coordinateSystem` | object | 是 | 坐标系定义，见 §2.4 |
| `aiHints` | object | 否 | AI 辅助信息（方向标注等），见 §2.5 |
| `bounds` | object | 是 | 画布边界尺寸 (mm) |
| `grid` | object | 否 | 网格配置 |
| `structure` | object | 是 | 建筑结构（墙/门/窗） |
| `zones` | array | 否 | 功能区域定义 |
| `elements` | array | 是 | 家具元素列表 |
| `spatialRelations` | array | 否 | 空间关系列表 |
| `pendingCommits` | array | 否 | 待处理的用户提交 |

### 2.3 Metadata 元数据

```json
{
  "metadata": {
    "sourceType": "revit",
    "revitProjectId": "project_abc",
    "revitViewId": 12345,
    "designIntent": "现代简约风格三口之家客厅",
    "projectConfig": {
      "style": "modern",
      "spaceType": "livingRoom",
      "budget": "standard",
      "familyMembers": {
        "adults": 2,
        "children": 1,
        "elderly": 0,
        "pets": false
      },
      "specialRequirements": "需要阅读角"
    },
    "revitMapping": {
      "projectBaseOffset": { "x": -12000, "y": -8000 },
      "rotationToTrueNorth": 0
    }
  }
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `sourceType` | string | 来源类型：`revit` / `import` / `blank` |
| `revitProjectId` | string | Revit 项目标识（如有） |
| `revitViewId` | number | Revit 视图 ID（如有） |
| `designIntent` | string | 用户的设计诉求描述 |
| `projectConfig` | object | 项目配置参数 |
| `revitMapping` | object | Revit 坐标映射信息，见下文 |

**revitMapping 字段说明**：

| 字段 | 类型 | 说明 |
|------|------|------|
| `projectBaseOffset` | Point2D | 视图原点相对于 Revit 项目基点的偏移量 (mm)，用于回写时还原坐标 |
| `rotationToTrueNorth` | number | 视图相对于真北的旋转角度（度），Phase 1 默认为 0 |

### 2.4 CoordinateSystem 坐标系定义

定义画布使用的坐标系统，采用 **CAD 标准坐标系**（笛卡尔坐标系）。

```json
{
  "coordinateSystem": {
    "origin": "viewBoundingBoxBottomLeft",
    "xAxis": "right",
    "yAxis": "up",
    "unit": "mm"
  }
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `origin` | string | 原点位置：`viewBoundingBoxBottomLeft`（视图裁剪框左下角） |
| `xAxis` | string | X 轴正方向：`right`（向右） |
| `yAxis` | string | Y 轴正方向：`up`（向上，CAD 标准） |
| `unit` | string | 单位：`mm`（毫米） |

> ⚠️ **注意**：Y 轴向上与 Web 屏幕坐标系相反。前端渲染时需进行转换。

### 2.5 AIHints AI 辅助信息

帮助 AI 理解画布的方向和空间语义，用于生成更准确的自然语言描述。

```json
{
  "aiHints": {
    "northDirection": "up",
    "entranceDirection": "south",
    "primaryViewingAngle": "north"
  }
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `northDirection` | string | 真北方向对应的视觉方向：`up` / `down` / `left` / `right` |
| `entranceDirection` | string | 主入口方向（可选） |
| `primaryViewingAngle` | string | 主要观看角度，如电视墙方向（可选） |

> 💡 **用途**：AI 在生成自然语言描述时使用，如"沙发面向北侧窗户"。

---

## 3. 建筑结构 (structure)

建筑结构从 Revit 导出，**锁定不可编辑**，仅供 AI 分析空间使用。

### 3.1 完整结构

```json
{
  "structure": {
    "walls": [ ... ],
    "doors": [ ... ],
    "windows": [ ... ],
    "columns": [ ... ]
  }
}
```

### 3.2 WallElement 墙体

```json
{
  "id": "wall_001",
  "type": "wall",
  "revitElementId": 123456,
  "geometry": {
    "startPoint": { "x": 0, "y": 0 },
    "endPoint": { "x": 6000, "y": 0 },
    "thickness": 200,
    "height": 2800
  },
  "material": "concrete",
  "isExterior": true
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | string | 墙体 ID，格式：`wall_{序号}` |
| `type` | string | 固定值：`wall` |
| `revitElementId` | number | 对应的 Revit 元素 ID |
| `geometry.startPoint` | Point2D | 墙体起点坐标 (mm) |
| `geometry.endPoint` | Point2D | 墙体终点坐标 (mm) |
| `geometry.thickness` | number | 墙体厚度 (mm) |
| `geometry.height` | number | 墙体高度 (mm) |
| `material` | string | 材质类型 |
| `isExterior` | boolean | 是否为外墙 |

### 3.3 DoorElement 门

```json
{
  "id": "door_001",
  "type": "door",
  "revitElementId": 123457,
  "hostWallId": "wall_001",
  "geometry": {
    "position": { "x": 2000, "y": 0 },
    "width": 900,
    "height": 2100,
    "openingDirection": "inward",
    "hingeSide": "left"
  },
  "doorType": "single"
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `hostWallId` | string | 所属墙体 ID |
| `geometry.position` | Point2D | 门中心点在墙上的位置 |
| `geometry.width` | number | 门宽 (mm) |
| `geometry.height` | number | 门高 (mm) |
| `geometry.openingDirection` | string | 开启方向：`inward` / `outward` |
| `geometry.hingeSide` | string | 铰链侧：`left` / `right` |
| `doorType` | string | 门类型：`single` / `double` / `sliding` |

### 3.4 WindowElement 窗

```json
{
  "id": "window_001",
  "type": "window",
  "revitElementId": 123458,
  "hostWallId": "wall_002",
  "geometry": {
    "position": { "x": 4000, "y": 1000 },
    "width": 1800,
    "height": 1500,
    "sillHeight": 900
  },
  "windowType": "casement"
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `geometry.sillHeight` | number | 窗台高度 (mm) |
| `windowType` | string | 窗类型：`casement` / `sliding` / `fixed` |

---

## 4. 功能区域 (zones)

功能区域帮助 AI 理解空间的逻辑划分。

### 4.1 Zone 定义

```json
{
  "zones": [
    {
      "id": "zone_living",
      "name": "客厅区域",
      "function": "living",
      "boundary": [
        { "x": 0, "y": 0 },
        { "x": 6000, "y": 0 },
        { "x": 6000, "y": 4000 },
        { "x": 0, "y": 4000 }
      ],
      "area": 24000000,
      "revitRoomId": 12345,
      "suggestedFurniture": ["sofa", "coffeeTable", "tvStand", "floorLamp"]
    },
    {
      "id": "zone_dining",
      "name": "餐厅区域",
      "function": "dining",
      "boundary": [ ... ],
      "area": 12000000,
      "revitRoomId": 12346,
      "suggestedFurniture": ["diningTable", "diningChair", "sideboard"]
    }
  ]
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | string | 区域 ID，格式：`zone_{功能}` |
| `name` | string | 区域名称，用户可见 |
| `function` | string | 功能类型，见下表 |
| `boundary` | Point2D[] | 区域边界多边形顶点（顺时针） |
| `area` | number | 面积 (mm²) |
| `revitRoomId` | number | 对应的 Revit 房间 ID（如有） |
| `suggestedFurniture` | string[] | 建议的家具类型 |

### 4.2 功能类型 (function)

| 值 | 说明 |
|-----|------|
| `living` | 客厅/起居室 |
| `dining` | 餐厅 |
| `bedroom` | 卧室 |
| `study` | 书房 |
| `kitchen` | 厨房 |
| `bathroom` | 卫生间 |
| `entrance` | 玄关 |
| `balcony` | 阳台 |
| `corridor` | 走廊 |
| `storage` | 储物间 |

---

## 5. 家具元素 (elements)

### 5.1 FurnitureElement 完整定义

```json
{
  "id": "f_001",
  "type": "furniture",
  "familyId": "sofa_3seat_modern",
  "familyName": "三人沙发-现代款",
  "category": "seating",

  "position": { "x": 3000, "y": 2000 },
  "gridPosition": { "row": 4, "col": 6 },
  "rotation": 90,

  "bounds": {
    "width": 2100,
    "depth": 900,
    "height": 850
  },

  "zoneId": "zone_living",

  "visual": {
    "svgSymbolId": "sofa_3seat_modern",
    "svgAvailable": true,
    "quality": "high"
  },

  "revitMapping": {
    "revitTypeId": 337201,
    "revitFamilyName": "JZ-I-三人沙发",
    "synced": false
  },

  "metadata": {
    "addedBy": "ai",
    "addedAt": "2025-12-02T14:30:00Z",
    "intent": "在窗边放置主沙发，面向电视墙",
    "confidence": 0.85
  }
}
```

### 5.2 字段说明

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `id` | string | 是 | 元素 ID，格式：`f_{序号}` |
| `type` | string | 是 | 固定值：`furniture` |
| `familyId` | string | 是 | 族库中的 ID |
| `familyName` | string | 是 | 族名称，用户可见 |
| `category` | string | 是 | 家具类别，见下表 |
| `position` | Point2D | 是 | 中心点位置 (mm) |
| `gridPosition` | GridPos | 是 | 网格坐标（AI 友好） |
| `rotation` | number | 是 | 旋转角度（度） |
| `bounds` | Bounds3D | 是 | 包围盒尺寸 (mm) |
| `zoneId` | string | 否 | 所属功能区域 ID |
| `visual` | object | 是 | 视觉信息 |
| `revitMapping` | object | 否 | Revit 映射信息 |
| `metadata` | object | 是 | 元数据 |

### 5.3 家具类别 (category)

| 值 | 说明 | 典型家具 |
|----|------|---------|
| `seating` | 座椅类 | 沙发、椅子、凳子 |
| `table` | 桌类 | 茶几、餐桌、书桌 |
| `storage` | 储物类 | 柜子、架子、衣柜 |
| `bed` | 床类 | 单人床、双人床 |
| `lighting` | 灯具类 | 落地灯、台灯 |
| `decor` | 装饰类 | 地毯、挂画、绿植 |
| `appliance` | 电器类 | 电视、冰箱 |

### 5.4 Visual 视觉信息

```json
{
  "visual": {
    "svgSymbolId": "sofa_3seat_modern",
    "svgAvailable": true,
    "quality": "high",
    "placeholderSvg": null
  }
}
```

| 字段 | 说明 |
|------|------|
| `svgSymbolId` | SVG Symbol ID，用于渲染 |
| `svgAvailable` | 是否有真实 SVG 图形 |
| `quality` | 图形质量：`high` / `medium` / `low` / `placeholder` |
| `placeholderSvg` | 占位 SVG（当 svgAvailable 为 false 时使用） |

### 5.5 Metadata 元数据

```json
{
  "metadata": {
    "addedBy": "ai",
    "addedAt": "2025-12-02T14:30:00Z",
    "modifiedBy": "user",
    "modifiedAt": "2025-12-02T15:00:00Z",
    "intent": "在窗边放置主沙发，面向电视墙",
    "confidence": 0.85
  }
}
```

| 字段 | 说明 |
|------|------|
| `addedBy` | 添加者：`ai` / `user` |
| `addedAt` | 添加时间 |
| `modifiedBy` | 最后修改者 |
| `modifiedAt` | 最后修改时间 |
| `intent` | AI 添加时的意图说明（必填，AI 友好） |
| `confidence` | AI 的置信度（0-1） |

---

## 6. 空间关系 (spatialRelations)

空间关系帮助 AI 理解元素之间的位置关系，减少推理负担。

### 6.1 四类关系体系

```json
{
  "spatialRelations": [
    {
      "type": "geometric",
      "subject": "f_001",
      "relation": "facing",
      "object": "window_001"
    },
    {
      "type": "regional",
      "subject": "f_001",
      "relation": "inZone",
      "zoneId": "zone_living",
      "zoneName": "客厅区域"
    },
    {
      "type": "semantic",
      "subject": "f_001",
      "relation": "servingZone",
      "targetZone": "zone_living"
    },
    {
      "type": "distance",
      "subject": "f_001",
      "relation": "within",
      "object": "f_002",
      "threshold": 800,
      "actualDistance": 650
    }
  ]
}
```

### 6.2 几何关系 (geometric)

描述元素之间的几何位置关系。

| relation 值 | 说明 |
|-------------|------|
| `leftOf` | 在左边 |
| `rightOf` | 在右边 |
| `above` | 在上方（平面图中的北侧） |
| `below` | 在下方（平面图中的南侧） |
| `facing` | 面向 |
| `backTo` | 背向 |
| `alignedWith` | 对齐 |
| `parallel` | 平行 |
| `perpendicular` | 垂直 |
| `adjacentTo` | 相邻 |

```json
{
  "type": "geometric",
  "subject": "f_001",
  "relation": "leftOf",
  "object": "f_002"
}
```

### 6.3 区域归属 (regional)

描述元素与功能区域的归属关系。

```json
{
  "type": "regional",
  "subject": "f_001",
  "relation": "inZone",
  "zoneId": "zone_living",
  "zoneName": "客厅区域"
}
```

| relation 值 | 说明 |
|-------------|------|
| `inZone` | 在区域内 |
| `atBoundary` | 在区域边界 |
| `spanningZones` | 跨越多个区域 |

### 6.4 功能语义 (semantic)

描述元素的功能语义关系。

```json
{
  "type": "semantic",
  "subject": "f_001",
  "relation": "servingZone",
  "targetZone": "zone_living"
}
```

| relation 值 | 说明 |
|-------------|------|
| `servingZone` | 服务于某区域 |
| `blockingPath` | 阻挡通行路径 |
| `nearWindow` | 靠近窗户 |
| `nearDoor` | 靠近门 |
| `awayFromDoor` | 远离门 |
| `facingEntrance` | 面向入口 |
| `backToWall` | 背靠墙 |
| `centerOfRoom` | 位于房间中心 |
| `cornerPlacement` | 角落放置 |

### 6.5 距离约束 (distance)

描述元素之间的距离关系。

```json
{
  "type": "distance",
  "subject": "f_001",
  "relation": "within",
  "object": "f_002",
  "threshold": 800,
  "actualDistance": 650,
  "unit": "mm"
}
```

| relation 值 | 说明 |
|-------------|------|
| `within` | 在阈值距离内 |
| `beyond` | 超出阈值距离 |
| `exactlyAt` | 精确距离 |
| `tooClose` | 过近（可能碰撞） |
| `tooFar` | 过远（不便使用） |

---

## 7. 版本控制与变更追踪

### 7.1 版本号机制

- 每次画布修改，`version` 递增 1
- 用于乐观锁，防止并发冲突

```json
{
  "version": 42
}
```

### 7.2 乐观锁使用

AI 调用修改工具时携带 `expectedVersion`：

```json
{
  "tool": "element_add",
  "params": {
    "canvasId": "canvas_001",
    "expectedVersion": 42,
    "familyId": "sofa_3seat",
    "position": { "x": 3000, "y": 2000 }
  }
}
```

如果版本不匹配，返回错误：

```json
{
  "success": false,
  "error": "VERSION_CONFLICT",
  "currentVersion": 43,
  "hint": "请调用 canvas_describe() 获取最新状态后重试"
}
```

### 7.3 变更集 (ChangeSet)

用户点击"同步"按钮后生成的变更集：

```json
{
  "changeSetId": "cs_001",
  "timestamp": "2025-12-02T15:30:00Z",
  "summary": "移动了沙发到窗边",
  "userId": "user_abc",
  "baseVersion": 40,
  "targetVersion": 42,
  "changes": [
    {
      "action": "move",
      "elementId": "f_001",
      "from": { "x": 3000, "y": 2000 },
      "to": { "x": 5000, "y": 2000 }
    },
    {
      "action": "rotate",
      "elementId": "f_001",
      "from": 90,
      "to": 180
    }
  ]
}
```

### 7.4 待处理提交 (pendingCommits)

画布中的待处理用户提交：

```json
{
  "pendingCommits": [
    {
      "changeSetId": "cs_001",
      "summary": "移动了沙发到窗边",
      "timestamp": "2025-12-02T15:30:00Z",
      "changesCount": 2,
      "acknowledged": false
    }
  ]
}
```

AI 处理完成后调用 `canvas_ack_commits` 确认。

---

## 8. Visual Fallback 机制

当族库无法提供精确的 2D 预览图时，使用 Visual Fallback。

### 8.1 Fallback 规则

| 条件 | 处理方式 |
|------|---------|
| 族库有 SVG Symbol | 直接使用，quality = "high" |
| 族库有简化 SVG | 使用简化版，quality = "medium" |
| 族库只有尺寸 | 生成占位矩形，quality = "placeholder" |
| 无任何信息 | 生成默认占位符，quality = "placeholder" |

### 8.2 占位符生成规则

```json
{
  "visual": {
    "svgAvailable": false,
    "quality": "placeholder",
    "placeholderSvg": "<rect x='0' y='0' width='2100' height='900' rx='50' fill='#E0E0E0' stroke='#999' stroke-width='2'/><text x='1050' y='450' text-anchor='middle' dominant-baseline='middle' font-size='120' fill='#666'>三人沙发</text><text x='1050' y='600' text-anchor='middle' font-size='80' fill='#999'>2100×900</text>"
  }
}
```

### 8.3 质量等级

| 等级 | 说明 | 来源 |
|------|------|------|
| `high` | 高质量精确图形 | 族库官方 SVG |
| `medium` | 中等质量简化图形 | 族库简化 SVG |
| `low` | 低质量示意图形 | 自动生成的通用图标 |
| `placeholder` | 占位矩形 | 根据尺寸自动生成 |

---

## 9. 完整示例

### 9.1 典型客厅画布

```json
{
  "id": "canvas_001",
  "name": "客厅设计方案A",
  "version": 15,
  "createdAt": "2025-12-02T10:00:00Z",
  "updatedAt": "2025-12-02T15:30:00Z",

  "metadata": {
    "sourceType": "revit",
    "revitProjectId": "project_abc",
    "revitViewId": 12345,
    "designIntent": "现代简约风格，三口之家，需要阅读角",
    "projectConfig": {
      "style": "modern",
      "spaceType": "livingRoom",
      "budget": "standard",
      "familyMembers": { "adults": 2, "children": 1 }
    },
    "revitMapping": {
      "projectBaseOffset": { "x": -12000, "y": -8000 },
      "rotationToTrueNorth": 0
    }
  },

  "coordinateSystem": {
    "origin": "viewBoundingBoxBottomLeft",
    "xAxis": "right",
    "yAxis": "up",
    "unit": "mm"
  },

  "aiHints": {
    "northDirection": "up",
    "entranceDirection": "east",
    "primaryViewingAngle": "south"
  },

  "bounds": { "width": 8000, "height": 6000 },
  "grid": { "size": 500, "visible": true },

  "structure": {
    "walls": [
      {
        "id": "wall_001",
        "type": "wall",
        "revitElementId": 100001,
        "geometry": {
          "startPoint": { "x": 0, "y": 0 },
          "endPoint": { "x": 8000, "y": 0 },
          "thickness": 200,
          "height": 2800
        },
        "isExterior": true
      },
      {
        "id": "wall_002",
        "type": "wall",
        "revitElementId": 100002,
        "geometry": {
          "startPoint": { "x": 8000, "y": 0 },
          "endPoint": { "x": 8000, "y": 6000 },
          "thickness": 200,
          "height": 2800
        },
        "isExterior": false
      }
    ],
    "doors": [
      {
        "id": "door_001",
        "type": "door",
        "revitElementId": 100010,
        "hostWallId": "wall_002",
        "geometry": {
          "position": { "x": 8000, "y": 3000 },
          "width": 900,
          "height": 2100,
          "openingDirection": "inward",
          "hingeSide": "left"
        },
        "doorType": "single"
      }
    ],
    "windows": [
      {
        "id": "window_001",
        "type": "window",
        "revitElementId": 100020,
        "hostWallId": "wall_001",
        "geometry": {
          "position": { "x": 4000, "y": 0 },
          "width": 2400,
          "height": 1800,
          "sillHeight": 900
        },
        "windowType": "casement"
      }
    ]
  },

  "zones": [
    {
      "id": "zone_living",
      "name": "客厅主区",
      "function": "living",
      "boundary": [
        { "x": 0, "y": 0 },
        { "x": 6000, "y": 0 },
        { "x": 6000, "y": 6000 },
        { "x": 0, "y": 6000 }
      ],
      "area": 36000000,
      "suggestedFurniture": ["sofa", "coffeeTable", "tvStand"]
    },
    {
      "id": "zone_reading",
      "name": "阅读角",
      "function": "study",
      "boundary": [
        { "x": 6000, "y": 0 },
        { "x": 8000, "y": 0 },
        { "x": 8000, "y": 3000 },
        { "x": 6000, "y": 3000 }
      ],
      "area": 6000000,
      "suggestedFurniture": ["armchair", "floorLamp", "bookshelf"]
    }
  ],

  "elements": [
    {
      "id": "f_001",
      "type": "furniture",
      "familyId": "sofa_3seat_modern",
      "familyName": "三人沙发-现代款",
      "category": "seating",
      "position": { "x": 3000, "y": 4500 },
      "gridPosition": { "row": 9, "col": 6 },
      "rotation": 0,
      "bounds": { "width": 2100, "depth": 900, "height": 850 },
      "zoneId": "zone_living",
      "visual": {
        "svgSymbolId": "sofa_3seat_modern",
        "svgAvailable": true,
        "quality": "high"
      },
      "revitMapping": {
        "revitTypeId": 337201,
        "revitFamilyName": "JZ-I-三人沙发",
        "synced": false
      },
      "metadata": {
        "addedBy": "ai",
        "addedAt": "2025-12-02T14:30:00Z",
        "intent": "在客厅中央放置主沙发，面向电视墙方向",
        "confidence": 0.92
      }
    },
    {
      "id": "f_002",
      "type": "furniture",
      "familyId": "coffee_table_round",
      "familyName": "圆形茶几",
      "category": "table",
      "position": { "x": 3000, "y": 3500 },
      "gridPosition": { "row": 7, "col": 6 },
      "rotation": 0,
      "bounds": { "width": 800, "depth": 800, "height": 450 },
      "zoneId": "zone_living",
      "visual": {
        "svgSymbolId": "coffee_table_round",
        "svgAvailable": true,
        "quality": "high"
      },
      "metadata": {
        "addedBy": "ai",
        "addedAt": "2025-12-02T14:31:00Z",
        "intent": "在沙发前方放置茶几，方便使用",
        "confidence": 0.95
      }
    },
    {
      "id": "f_003",
      "type": "furniture",
      "familyId": "armchair_reading",
      "familyName": "阅读单椅",
      "category": "seating",
      "position": { "x": 7000, "y": 1500 },
      "gridPosition": { "row": 3, "col": 14 },
      "rotation": 225,
      "bounds": { "width": 800, "depth": 850, "height": 1000 },
      "zoneId": "zone_reading",
      "visual": {
        "svgSymbolId": "armchair_reading",
        "svgAvailable": false,
        "quality": "placeholder",
        "placeholderSvg": "<rect width='800' height='850' rx='30' fill='#E0E0E0'/><text x='400' y='425' text-anchor='middle' font-size='80'>阅读椅</text>"
      },
      "metadata": {
        "addedBy": "ai",
        "addedAt": "2025-12-02T14:35:00Z",
        "intent": "在阅读角放置舒适单椅，利用自然光线",
        "confidence": 0.88
      }
    }
  ],

  "spatialRelations": [
    {
      "type": "geometric",
      "subject": "f_001",
      "relation": "facing",
      "object": "wall_001"
    },
    {
      "type": "geometric",
      "subject": "f_002",
      "relation": "adjacentTo",
      "object": "f_001"
    },
    {
      "type": "regional",
      "subject": "f_001",
      "relation": "inZone",
      "zoneId": "zone_living",
      "zoneName": "客厅主区"
    },
    {
      "type": "regional",
      "subject": "f_003",
      "relation": "inZone",
      "zoneId": "zone_reading",
      "zoneName": "阅读角"
    },
    {
      "type": "semantic",
      "subject": "f_003",
      "relation": "nearWindow",
      "object": "window_001"
    },
    {
      "type": "distance",
      "subject": "f_001",
      "relation": "within",
      "object": "f_002",
      "threshold": 1000,
      "actualDistance": 650
    }
  ],

  "pendingCommits": []
}
```

---

## 10. 附录：字段速查表

### 10.1 元素类型

| type | 说明 | 所属节点 |
|------|------|---------|
| `wall` | 墙体 | structure.walls |
| `door` | 门 | structure.doors |
| `window` | 窗 | structure.windows |
| `column` | 柱 | structure.columns |
| `furniture` | 家具 | elements |

### 10.2 通用类型定义

```typescript
// 2D 点
interface Point2D {
  x: number;  // mm
  y: number;  // mm
}

// 网格位置
interface GridPosition {
  row: number;
  col: number;
}

// 3D 尺寸
interface Bounds3D {
  width: number;   // mm (X 方向)
  depth: number;   // mm (Y 方向)
  height: number;  // mm (Z 方向)
}

// 线段
interface Line2D {
  startPoint: Point2D;
  endPoint: Point2D;
}

// 坐标系定义
interface CoordinateSystem {
  origin: "viewBoundingBoxBottomLeft";
  xAxis: "right";
  yAxis: "up";  // CAD 标准，Y 轴向上
  unit: "mm";
}

// AI 辅助信息
interface AIHints {
  northDirection: "up" | "down" | "left" | "right";
  entranceDirection?: string;
  primaryViewingAngle?: string;
}

// Revit 坐标映射
interface RevitMapping {
  projectBaseOffset: Point2D;  // 视图原点相对于项目基点的偏移
  rotationToTrueNorth: number; // 视图相对于真北的旋转角度（度）
}
```

### 10.3 空间关系速查

| 类型 | 可用关系 |
|------|---------|
| geometric | leftOf, rightOf, above, below, facing, backTo, alignedWith, parallel, perpendicular, adjacentTo |
| regional | inZone, atBoundary, spanningZones |
| semantic | servingZone, blockingPath, nearWindow, nearDoor, awayFromDoor, facingEntrance, backToWall, centerOfRoom, cornerPlacement |
| distance | within, beyond, exactlyAt, tooClose, tooFar |

---

## 版本历史

| 版本 | 日期 | 变更内容 |
|------|------|----------|
| v1.1 | 2025-12-02 | 坐标系变更为 CAD 标准（Y-up），新增 coordinateSystem、aiHints 字段，更新 revitMapping |
| v1.0 | 2025-12-02 | 初始版本，基于专家评审讨论结果定稿 |
