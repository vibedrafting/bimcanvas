# BIMCanvas JSON Schema 规范

> 版本：v2.5
> 更新日期：2025-12-05
> 状态：已定稿（新增 Room/WallFinish 概念，完善完成面设计）
>
> **相关文档**：
> - [Architecture.md](./Architecture.md) - 系统架构（含 Core 层详细设计）
> - [reviews/BIMCanvas_Core_Implementation_Review.md](../reviews/BIMCanvas_Core_Implementation_Review.md) - Core 实现方案评审记录

---

## 1. 设计原则

### 1.1 核心设计约束

> **AI = OBB 规划师**：AI 只操作矩形包围盒 (OBB)，不计算精确几何。

| 原则 | 说明 |
|------|------|
| **AI 决策位置** | AI 输出 `center + size + facing`，Core 负责转换为精确几何 |
| **Polygon2D 是真理** | JSON 存储精确几何，AABB 仅作运行时优化 |
| **多样化交互** | AI 可用 Semantic / Vec2D / Polygon2D 任意格式输出 |

### 1.2 KISS 原则 (Keep It Simple, Stupid)

| 决策点 | 选择 | 理由 |
|--------|------|------|
| **坐标系** | Y-Up (笛卡尔) | 符合 CAD/BIM/数学直觉，只在前端渲染时转换 |
| **数据分层** | Layer 1 (AI 上下文) | Token 效率，职责清晰 |
| **墙体表示** | 封闭轮廓多边形 | AI 不需要理解墙体结构，只需知道空间边界 |
| **门窗表示** | 简化为线段 | 厚度不影响家具布置 |
| **门扇区域** | 预计算为禁区（Polygon2D） | AI 只需知道"这里不能放"，支持异形禁区 |
| **房间结构** | rooms + zones 分层 | Room 是物理房间，Zone 是 Room 下的功能分区 |
| **完成面机制** | 类似门扇禁区 | 完成面生成禁区，裁剪 Zone.innerBoundary |
| **标高信息** | 全局 levelId | 一张平面图对应一个 Level |
| **布置单元** | modules（模块） | 支持单一家具或组合（如睡眠模块=床+床头柜） |
| **模块位置** | Polygon2D 边界 | 精确几何，支持倾斜场景，NTS 兼容 |
| **模块朝向** | 语义化方向 | AI 友好，插件端转换为角度 |

### 1.3 数据分层架构

```
┌─────────────────────────────────────────────────────────────────┐
│                     数据分层架构                                 │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│   【Layer 1: AI 上下文】- CanvasDocument.json                    │
│   ┌─────────────────────────────────────────────────────────┐   │
│   │  • outline: 墙轮廓多边形 + 门窗线段（仅几何，无属性）       │   │
│   │  • rooms: 物理房间（对应 Revit Room）                      │   │
│   │  • zones: 可用空间 + 禁区（innerBoundary + exclusionAreas） │   │
│   │  • wallFinishes: 墙面完成面配置                            │   │
│   │  • modules: 家具模块列表                                   │   │
│   │  → 用途：AI 布置计算、前端渲染                              │   │
│   └─────────────────────────────────────────────────────────┘   │
│                                                                 │
│   【Layer 2: Revit 详细数据】- Phase 1 暂缓                      │
│   ┌─────────────────────────────────────────────────────────┐   │
│   │  • revitElementId、墙体厚度、门窗开启方向等                 │   │
│   │  → 用途：高级功能（吸附到墙等）、Web端展示                  │   │
│   │  → Phase 1 不实现，按需扩展                                │   │
│   └─────────────────────────────────────────────────────────┘   │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### 1.4 坐标系统

- **类型**：笛卡尔坐标系（Cartesian）
- **原点**：视图裁剪框左下角
- **X 轴**：向右为正
- **Y 轴**：向上为正（CAD 标准）
- **单位**：毫米 (mm)

> **重要**：这是 CAD 标准坐标系，与 Web 屏幕坐标系（Y 向下）相反。
> 前端渲染时必须进行显式坐标转换：`y_screen = canvasHeight - y_model * scale`

### 1.5 单位规范

| 类型 | 单位 | 精度 | 换算公式 |
|------|------|------|----------|
| 长度 | 毫米 (mm) | Double | `mm = feet × 304.8` |
| 角度 | 度 (degrees) | Double | `deg = rad × (180/π)` |

**核心原则**：
- **保留原始精度**：不做四舍五入，避免多次转换的累积误差
- **Core 层是唯一真理来源**：单位转换逻辑在 `BIMCanvas.Core.Converters.UnitConverter` 中实现

**数据流中的单位**：
```
Revit API (feet, radians)
    ↓ [插件层调用 Core.UnitConverter]
JSON (mm, degrees)
    ↓ [回写时调用 Core.UnitConverter]
Revit API (feet, radians)
```

### 1.6 几何图元 (Geometry Primitives)

定义全局通用的几何数据结构，采用**纯数组格式**以节省 Token（比对象格式节省约 50%）。

| 类型 | 格式 | 示例 | 说明 |
|------|------|------|------|
| **Point2D** | `[x, y]` | `[3000.5, 2500.0]` | 双精度坐标点（**绝对位置**） |
| **Vec2D** | `[dx, dy]` | `[-600.0, 0.0]` | 相对偏移向量（**结构同 Point2D**） |
| **Line2D** | `[[x1,y1], [x2,y2]]` | `[[2000,0], [2900,0]]` | 线段（起终点） |
| **Polygon2D** | `[[x,y], ...]` | `[[0,0], [6000,0], ...]` | 多边形（隐式闭合） |
| **AABB** | `[minX, minY, maxX, maxY]` | `[2000, 0, 2900, 900]` | 轴对齐包围盒 |

#### Point2D vs Vec2D

> **关键区分**：结构完全相同，但语义不同。
> - `Point2D` 是**绝对量**，表示"在哪里"（位置）
> - `Vec2D` 是**相对量**，表示"移动多少"（偏移）

**使用场景**：
- `Point2D`：`polygon` / `innerBoundary` 的顶点
- `Vec2D`：`items[].offset` 模块内部家具相对模块中心的偏移

#### Polygon2D 规则

- 最少 3 个顶点
- **隐式闭合**：首尾自动连接，不重复首点
- 顶点按**逆时针**排列（CAD 惯例）

#### AABB 计算

```
宽度 = maxX - minX
高度 = maxY - minY
中心 = [(minX + maxX) / 2, (minY + maxY) / 2]
```

### 1.7 数据流

```
【AI 操作画布】
AI 调用 MCP 工具 → 修改 JSON 数据 → WebSocket 推送 → 前端渲染 SVG

【用户操作画布】
用户交互 → 前端修改本地 JSON → 点击同步 → 服务端更新 → AI 感知变更

【AI 视觉验证】
JSON 数据 → 服务端渲染 → PNG 截图 → 发送给 AI（多模态）
```

---

## 2. 完整 JSON 结构

```json
{
  "id": "canvas_001",
  "version": 1,
  "coordinateSystem": "cartesian_mm_yUp",

  "metadata": {
    "revitViewId": 12345,
    "levelId": 67890,
    "gridSize": 500
  },

  "outline": {
    "walls": [
      { "id": "w1", "polygon": [[0,0], [6000,0], [6000,200], [0,200]] }
    ],
    "openings": [
      { "id": "d1", "type": "door", "line": [[2000,0], [2900,0]] },
      { "id": "win1", "type": "window", "line": [[3500,6000], [5300,6000]] }
    ]
  },

  "rooms": [
    {
      "id": "r1",
      "name": "主卧",
      "type": "master_bedroom",
      "boundary": [[0,0], [6000,0], [6000,6000], [0,6000]]
    }
  ],

  "zones": [
    {
      "id": "z1",
      "name": "睡眠区",
      "roomId": "r1",
      "tags": ["sleep"],
      "rawBoundary": [[200,200], [5800,200], [5800,5800], [200,5800]],
      "innerBoundary": [[220,220], [5780,220], [5780,5780], [220,5780]],
      "exclusionAreas": [
        {
          "id": "ex1",
          "type": "door_swing",
          "boundary": [[2000, 200], [2900, 200], [2900, 1100], [2000, 1100]]
        }
      ],
      "openings": ["d1", "win1"]
    }
  ],

  "wallFinishes": [
    {
      "id": "wf1",
      "locationLine": [[200, 200], [200, 5800]],
      "finishModuleId": "finish_paint_01",
      "thickness": 20,
      "exclusionBoundary": [[200, 200], [220, 200], [220, 5800], [200, 5800]],
      "wallId": "w4",
      "roomId": "r1",
      "source": "room_default"
    }
  ],

  "modules": [
    {
      "id": "m1",
      "moduleId": "sleep_master_01",
      "moduleName": "主卧睡眠模块",
      "bounds": [[1500, 2000], [4500, 2000], [4500, 4500], [1500, 4500]],
      "facing": "north",
      "zoneId": "z1",
      "items": [
        { "familyId": "bed_double_01", "offset": [0, 0], "role": "主体" },
        { "familyId": "nightstand_01", "offset": [-600, 0], "role": "左床头柜" },
        { "familyId": "nightstand_01", "offset": [600, 0], "role": "右床头柜" }
      ]
    }
  ]
}
```

---

## 3. 根对象字段说明

### 3.1 顶层字段

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `id` | string | 是 | 画布唯一标识，格式：`canvas_{uuid}` |
| `version` | number | 是 | 版本号，每次修改递增，用于乐观锁 |
| `coordinateSystem` | string | 是 | 固定值：`cartesian_mm_yUp` |
| `metadata` | object | 是 | 元数据 |
| `outline` | object | 是 | 可视化底图（墙体轮廓 + 门窗线段） |
| `rooms` | array | 是 | 物理房间列表（对应 Revit Room） |
| `zones` | array | 是 | 设计区域列表（属于 Room 的功能分区） |
| `wallFinishes` | array | 是 | 墙面完成面配置列表 |
| `modules` | array | 是 | 布置模块列表 |

### 3.2 metadata（元数据）

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `revitViewId` | number | 是 | 来源 Revit 视图 ID |
| `levelId` | number | 是 | 标高 ID，家具回写依赖 |
| `gridSize` | number | 否 | 网格大小，默认 500mm |

---

## 4. outline（可视化底图）

用于前端绘制"户型图"给用户看，以及 AI 辅助参考。

### 4.1 outline.walls（墙体轮廓）

墙体仅记录轮廓多边形，不记录厚度/材质等详细属性。

```json
{
  "walls": [
    { "id": "w1", "polygon": [[0,0], [6000,0], [6000,200], [0,200]] },
    { "id": "w2", "polygon": [[6000,0], [6200,0], [6200,4000], [6000,4000]] }
  ]
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | string | 墙体 ID，格式：`w{序号}` |
| `polygon` | number[][] | 墙体轮廓多边形顶点，格式：`[[x1,y1], [x2,y2], ...]` |

### 4.2 outline.openings（门窗）

门窗仅记录线段，用于视觉定位。

```json
{
  "openings": [
    { "id": "d1", "type": "door", "line": [[2000,0], [2900,0]] },
    { "id": "win1", "type": "window", "line": [[3500,6000], [5300,6000]] }
  ]
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | string | 门窗 ID，格式：`d{序号}` 或 `win{序号}` |
| `type` | string | 类型：`door` / `window` |
| `line` | number[][] | 线段，格式：`[[x1,y1], [x2,y2]]` |

---

## 5. rooms（物理房间）

Room 表示物理房间，对应 Revit 中的 Room 元素。Zone 是 Room 下的功能分区。

### 5.1 Room 完整定义

```json
{
  "rooms": [
    {
      "id": "r1",
      "name": "主卧",
      "type": "master_bedroom",
      "boundary": [[0,0], [6000,0], [6000,6000], [0,6000]]
    }
  ]
}
```

### 5.2 Room 字段说明

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `id` | string | 是 | 房间 ID，格式：`r{序号}` |
| `name` | string | 是 | 房间名称（用户可见） |
| `type` | string | 是 | 房间类型（RoomType 枚举） |
| `boundary` | number[][] | 是 | 房间边界（Revit Room 边界） |

### 5.3 type（房间类型）

| 值 | 说明 |
|-----|------|
| `living_room` | 客厅 |
| `dining_room` | 餐厅 |
| `master_bedroom` | 主卧 |
| `bedroom` | 次卧 |
| `study` | 书房 |
| `kitchen` | 厨房 |
| `bathroom` | 卫生间 |
| `entrance` | 玄关 |
| `balcony` | 阳台 |
| `corridor` | 走廊 |
| `storage` | 储物间 |

### 5.4 Room 与完成面厚度的关系

Room.type 决定该房间墙面的**默认完成面类型**，进而决定默认完成面厚度。

例如：
- `bathroom` → 默认瓷砖完成面 → 厚度 50mm
- `master_bedroom` → 默认乳胶漆完成面 → 厚度 20mm

具体映射关系通过项目配置文件管理，支持自定义。

---

## 6. zones（设计区域）

Zone 是 Room 下的功能分区，是 AI 的核心工作区。每个 Zone 定义一个可布置空间及其约束。

### 6.1 Zone 完整定义

```json
{
  "zones": [
    {
      "id": "z1",
      "name": "睡眠区",
      "roomId": "r1",
      "tags": ["sleep", "bedhead_wall"],
      "rawBoundary": [[200,200], [5800,200], [5800,5800], [200,5800]],
      "innerBoundary": [[220,220], [5780,220], [5780,5780], [220,5780]],
      "exclusionAreas": [
        {
          "id": "ex1",
          "type": "door_swing",
          "boundary": [[2000, 200], [2900, 200], [2900, 1100], [2000, 1100]]
        }
      ],
      "openings": ["d1", "win1"]
    }
  ]
}
```

### 6.2 Zone 字段说明

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `id` | string | 是 | 区域 ID，格式：`z{序号}` |
| `name` | string | 是 | 区域名称（用户可见） |
| `roomId` | string | 是 | 所属房间 ID |
| `tags` | string[] | 是 | 功能标签列表（ZoneTag 枚举） |
| `rawBoundary` | number[][] | 是 | **原始边界**（未扣除完成面） |
| `innerBoundary` | number[][] | 是 | **可用空间轮廓**（已扣除完成面禁区） |
| `exclusionAreas` | object[] | 否 | **禁止布置区**（门扇、必要通道等） |
| `openings` | string[] | 否 | 关联的门窗 ID |

### 6.3 tags（功能标签）

Zone 使用标签系统替代单一功能类型，支持多标签组合。

| 标签 | 说明 |
|------|------|
| `tv_media` | 电视多媒体区 |
| `audio_video` | 视听娱乐区 |
| `sleep` | 睡眠区（床区） |
| `bedhead_wall` | 床头背景墙区（触发特殊完成面） |
| `rest` | 休憩区（沙发等） |
| `reading` | 阅读区 |
| `work` | 工作区 |
| `study` | 学习区 |
| `wardrobe_storage` | 衣物收纳区 |
| `shoe_storage` | 鞋柜收纳区 |
| `general_storage` | 通用收纳区 |
| `dining` | 用餐区 |
| `cooking` | 烹饪区 |
| `food_prep` | 备餐区 |
| `bar` | 吧台区 |
| `shower` | 淋浴区 |
| `bathtub` | 浴缸区 |
| `toilet` | 如厕区 |
| `washing` | 洗漱区 |
| `laundry` | 洗衣区 |
| `vanity` | 梳妆区 |
| `entry` | 入口区 |
| `passage` | 通道区 |
| `display` | 展示区 |
| `plants` | 绿植区 |

### 6.4 tags 与完成面的关系

部分 Zone 标签会触发相邻墙面的完成面类型覆盖：

| 标签 | 完成面类型 | 典型厚度 |
|------|-----------|----------|
| `tv_media` | 护墙板 + 灯带槽 | 80mm |
| `bedhead_wall` | 软包/硬包 | 60mm |
| `bar` | 吧台背景 | 40mm |

**流程**：划分 Zone 后 → 检测标签 → 查找相邻墙面 → 更新 WallFinish（source = zone_override）

### 6.5 innerBoundary 计算规则

```
innerBoundary = rawBoundary - 所有相关完成面禁区（wallFinishes[].exclusionBoundary）
```

- `rawBoundary`：Zone 的原始边界（划分工作区时确定）
- 完成面禁区：由 WallFinish 根据 locationLine + thickness 动态计算
- AI 直接使用 `innerBoundary`，无需理解完成面计算逻辑

### 6.6 exclusionAreas（禁止布置区）

```json
{
  "exclusionAreas": [
    {
      "id": "ex1",
      "type": "door_swing",
      "boundary": [[2000, 0], [2900, 0], [2900, 900], [2000, 900]]
    },
    {
      "id": "ex2",
      "type": "passage",
      "boundary": [[0, 2500], [500, 2500], [500, 3500], [0, 3500]]
    }
  ]
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | string | 禁区 ID |
| `type` | string | 类型：`door_swing` / `passage` / `other` |
| `boundary` | number[][] | **禁区边界（Polygon2D）**：`[[x1,y1], [x2,y2], ...]` |

**type 可选值：**
- `door_swing`：门扇开启区域
- `passage`：必要通道
- `other`：其他禁区

---

## 7. wallFinishes（墙面完成面）

墙面完成面是一种禁区机制，与门扇禁区类似。完成面类型决定厚度，厚度决定禁区范围。

### 7.1 核心设计

> **三层来源机制 → 完成面类型 → 完成面厚度**

完成面的类型和厚度由三层来源机制决定，优先级从高到低：

| 优先级 | Source | 说明 |
|--------|--------|------|
| 1（最高）| `user_override` | 用户手动修改 |
| 2 | `zone_override` | Zone 标签触发（如 tv_media → 护墙板） |
| 3（最低）| `room_default` | Room.type 默认值（如 bathroom → 瓷砖） |

### 7.2 WallFinish 完整定义

```json
{
  "wallFinishes": [
    {
      "id": "wf1",
      "locationLine": [[200, 200], [200, 5800]],
      "finishModuleId": "finish_paint_01",
      "thickness": 20,
      "exclusionBoundary": [[200, 200], [220, 200], [220, 5800], [200, 5800]],
      "wallId": "w4",
      "roomId": "r1",
      "source": "room_default"
    },
    {
      "id": "wf2",
      "locationLine": [[200, 4000], [200, 5800]],
      "finishModuleId": "finish_tv_wall_01",
      "thickness": 80,
      "exclusionBoundary": [[200, 4000], [280, 4000], [280, 5800], [200, 5800]],
      "wallId": "w4",
      "roomId": "r1",
      "source": "zone_override"
    }
  ]
}
```

### 7.3 WallFinish 字段说明

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `id` | string | 是 | 完成面 ID，格式：`wf{序号}` |
| `locationLine` | number[][] | 是 | 定位线（靠墙侧，方向顺房间） |
| `finishModuleId` | string | 否 | 完成面类型（模块库 ID），决定做法和厚度 |
| `thickness` | number | 是 | 厚度（mm），由 finishModuleId 查模块库获得 |
| `exclusionBoundary` | number[][] | 是 | 禁区轮廓（由 locationLine + thickness 计算） |
| `wallId` | string | 是 | 关联墙体 ID |
| `roomId` | string | 是 | 关联房间 ID（决定是墙的哪一侧） |
| `source` | string | 是 | 来源：`room_default` / `zone_override` / `user_override` |

### 7.4 计算流程

```
┌────────────────────────────────────────────────────────────────┐
│ Phase 1: 初始化（Revit 导出时）                                  │
├────────────────────────────────────────────────────────────────┤
│ 1. 根据 Room.type 查项目配置，获取默认 finishModuleId            │
│ 2. 根据 finishModuleId 查模块库，获取 thickness                  │
│ 3. 为 Room 边界相邻的每面墙创建 WallFinish                       │
│    { locationLine, finishModuleId, thickness, source: "room_default" }
└────────────────────────────────────────────────────────────────┘
                               ↓
┌────────────────────────────────────────────────────────────────┐
│ Phase 2: Zone 标签覆盖（划分工作区后）                            │
├────────────────────────────────────────────────────────────────┤
│ 1. 检测 Zone.tags 是否匹配特殊完成面规则（如 tv_media）           │
│ 2. 查找 Zone.rawBoundary 与哪些墙共享边                          │
│ 3. 更新对应 WallFinish（如果 source != user_override）          │
│    { finishModuleId: 新类型, thickness: 新厚度, source: "zone_override" }
└────────────────────────────────────────────────────────────────┘
                               ↓
┌────────────────────────────────────────────────────────────────┐
│ Phase 3: 生成禁区 & 裁剪边界                                     │
├────────────────────────────────────────────────────────────────┤
│ 1. 根据 locationLine + thickness 计算 exclusionBoundary          │
│ 2. Zone.innerBoundary = Zone.rawBoundary - 所有相关完成面禁区     │
└────────────────────────────────────────────────────────────────┘
```

### 7.5 与门扇禁区的对比

| 维度 | 门扇禁区 | 完成面禁区 |
|------|----------|------------|
| 存储位置 | Zone.exclusionAreas | wallFinishes[] |
| 类型字段 | type: "door_swing" | finishModuleId |
| 来源 | 固定（门扇几何） | 三层来源机制 |
| 可编辑性 | 不可修改 | 用户可动态调整 |

两者都是"禁止布置区"的概念，但完成面禁区支持动态调整（用户可修改 finishModuleId/thickness）。

### 7.6 finishModuleId 示例

| finishModuleId | 做法 | 典型厚度 |
|----------------|------|----------|
| `finish_paint_01` | 乳胶漆 | 20mm |
| `finish_tile_01` | 瓷砖 | 50mm |
| `finish_tv_wall_01` | 护墙板 + 灯带槽 | 80mm |
| `finish_soft_01` | 软包 | 60mm |
| `finish_hard_01` | 硬包 | 50mm |

---

## 8. modules（布置模块）

模块是最小布置单元，可以是单一家具或家具组合。

### 8.1 Module 完整定义

```json
{
  "modules": [
    {
      "id": "m1",
      "moduleId": "sleep_master_01",
      "moduleName": "主卧睡眠模块",
      "bounds": [[1500, 2000], [4500, 2000], [4500, 4500], [1500, 4500]],
      "facing": "north",
      "zoneId": "z1",
      "items": [
        { "familyId": "bed_double_01", "offset": [0, 0], "role": "主体" },
        { "familyId": "nightstand_01", "offset": [-600, 0], "role": "左床头柜" },
        { "familyId": "nightstand_01", "offset": [600, 0], "role": "右床头柜" }
      ]
    }
  ]
}
```

### 8.2 Module 字段说明

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `id` | string | 是 | 模块实例 ID，格式：`m{序号}` |
| `moduleId` | string | 是 | 模块库中的模块类型 ID |
| `moduleName` | string | 否 | 可读名称（如"主卧睡眠模块"） |
| `bounds` | number[][] | 是 | Polygon2D 边界：`[[x1,y1], [x2,y2], ...]`（矩形 4 顶点） |
| `facing` | string \| number[] | 是 | 朝向：语义字符串或 Vec2D 向量（见 §8.3） |
| `zoneId` | string | 是 | 所属区域 ID |
| `items` | object[] | 否 | 模块内部家具清单（回写 Revit 用） |

### 8.3 facing（朝向 - 联合类型）

`facing` 支持两种格式：**语义字符串**（标准场景）和 **Vec2D 向量**（任意角度）。

> **核心原则**：**向量 (Vec2D) 是唯一真理**。语义字符串仅为常用向量的别名。

#### 8.3.1 语义字符串 (Semantic Alias)

语义字符串是常用方向的快捷方式，Core 层会自动将其转换为对应的单位向量。

| 语义 | 对应向量 (Vec2D) | 说明 |
|------|------------------|------|
| `east` | `[1, 0]` | **X 轴正向 (基准)** |
| `north` | `[0, 1]` | **Y 轴正向** |
| `west` | `[-1, 0]` | X 轴负向 |
| `south` | `[0, -1]` | Y 轴负向 |
| `northeast` | `[0.707, 0.707]` | 东北 (45°) |
| `northwest` | `[-0.707, 0.707]` | 西北 (135°) |
| `southeast` | `[0.707, -0.707]` | 东南 (-45°) |
| `southwest` | `[-0.707, -0.707]` | 西南 (-135°) |

#### 8.3.2 Vec2D 向量（任意角度）

当需要非 45° 增量的角度时，直接使用 Vec2D 单位向量：

```json
{
  "facing": [0.866, 0.5]   // 30° 方向向量 (cos30°, sin30°)
}
```

**向量处理规则**：
- 向量应为**单位向量**（长度 ≈ 1）
- 若 `|v| < 0.5`，视为无效，回退到模块默认朝向
- Core 层自动归一化并保留 6 位小数

#### 8.3.3 为什么不用 Angle（数值角度）

讨论中明确**反对**使用数值角度（如 `rotation: 30`）：

- **歧义性**：角度依赖于 0° 定义（是北还是东？）和旋转方向（顺时针还是逆时针？）。
- **唯一性**：Vec2D 向量具有**唯一确定的几何意义**，对 AI 更友好。

#### 8.3.4 插件端转换 (Revit 兼容)

Revit API 使用 X 轴 (East) 为 0°，逆时针旋转，与本定义的向量数学逻辑完全一致。

```csharp
// Vec2D → Revit 旋转角度
// facing = [dx, dy]
angle = Math.Atan2(dy, dx) * (180 / Math.PI)

// 示例：
// North [0, 1] -> Atan2(1, 0) = 90°
// East  [1, 0] -> Atan2(0, 1) = 0°
```

### 8.4 items（模块内部家具）

用于回写 Revit 时创建具体家具实例。

```json
{
  "items": [
    { "familyId": "bed_double_01", "offset": [0, 0], "role": "主体" },
    { "familyId": "nightstand_01", "offset": [-600, 0], "role": "左床头柜" },
    { "familyId": "nightstand_01", "offset": [600, 0], "role": "右床头柜" }
  ]
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `familyId` | string | 族库中的 Family ID |
| `offset` | number[] | 相对模块中心的偏移：`[dx, dy]` |
| `role` | string | 在模块中的角色（如"主体"、"左床头柜"） |

### 8.5 计算属性 (_computed)

AI 输入时，Canvas-MCP 会为每个 Module 动态生成计算属性 `_computed`，方便 AI 理解空间状态。

**重要**：`_computed` **不持久化到 JSON**，仅在 AI 交互时动态生成。

```json
{
  "id": "m1",
  "moduleId": "sleep_master_01",
  "bounds": [[1500, 2000], [4500, 2000], [4500, 4500], [1500, 4500]],
  "facing": "north",
  "zoneId": "z1",
  "_computed": {
    "center": [3000, 3250],
    "size": [3000, 2500]
  }
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `center` | number[] | 模块中心点 `[x, y]`，由 bounds 计算 |
| `size` | number[] | 模块尺寸 `[width, height]`，由 bounds 计算 |

**设计理由**：
- **避免数据冗余**：`center/size` 与 `bounds` 存在计算关系，同时存储会导致不一致风险
- **AI 友好**：提供语义化信息，AI 无需自行推算
- **单一真理来源**：`bounds: Polygon2D` 是唯一存储格式

---

## 9. AI 布置逻辑

### 9.1 核心约束规则

```
对于每个要放置的模块：
1. 模块 bounds 必须完全在 zone.innerBoundary 内
2. 模块 bounds 不能与任何 zone.exclusionAreas 重叠
3. 模块 bounds 不能与其他已放置模块重叠
```

### 9.2 碰撞检测伪代码

```javascript
function canPlaceModule(module, zone, existingModules) {
  const bounds = module.bounds; // [minX, minY, maxX, maxY]

  // 约束1: 必须在 innerBoundary 内
  if (!isInsidePolygon(bounds, zone.innerBoundary)) {
    return false;
  }

  // 约束2: 不能与禁区重叠
  for (const exclusion of zone.exclusionAreas) {
    if (aabbIntersects(bounds, exclusion.rect)) {
      return false;
    }
  }

  // 约束3: 不能与其他模块重叠
  for (const existing of existingModules) {
    if (aabbIntersects(bounds, existing.bounds)) {
      return false;
    }
  }

  return true;
}

function aabbIntersects(a, b) {
  // a, b 均为 [minX, minY, maxX, maxY]
  return !(a[2] < b[0] || a[0] > b[2] || a[3] < b[1] || a[1] > b[3]);
}
```

---

## 10. 版本控制

### 10.1 版本号机制

- 每次画布修改，`version` 递增 1
- 用于乐观锁，防止并发冲突

### 10.2 乐观锁使用

AI 调用修改工具时携带 `expectedVersion`：

```json
{
  "tool": "module_add",
  "params": {
    "canvasId": "canvas_001",
    "expectedVersion": 42,
    "moduleId": "sleep_master_01",
    "bounds": [[1500, 2000], [4500, 2000], [4500, 4500], [1500, 4500]],
    "facing": "north",
    "zoneId": "z1"
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

---

## 11. 完整示例

### 11.1 典型卧室布置

```json
{
  "id": "canvas_bedroom_001",
  "version": 5,
  "coordinateSystem": "cartesian_mm_yUp",

  "metadata": {
    "revitViewId": 12345,
    "levelId": 67890,
    "gridSize": 500
  },

  "outline": {
    "walls": [
      { "id": "w1", "polygon": [[0,0], [6000,0], [6000,200], [0,200]] },
      { "id": "w2", "polygon": [[5800,0], [6000,0], [6000,6000], [5800,6000]] },
      { "id": "w3", "polygon": [[0,5800], [6000,5800], [6000,6000], [0,6000]] },
      { "id": "w4", "polygon": [[0,0], [200,0], [200,6000], [0,6000]] }
    ],
    "openings": [
      { "id": "d1", "type": "door", "line": [[2000,0], [2900,0]] },
      { "id": "win1", "type": "window", "line": [[1500,6000], [4500,6000]] }
    ]
  },

  "zones": [
    {
      "id": "z1",
      "name": "主卧",
      "function": "master_bedroom",
      "innerBoundary": [
        [250, 250],
        [5750, 250],
        [5750, 5750],
        [250, 5750]
      ],
      "exclusionAreas": [
        {
          "id": "ex1",
          "type": "door_swing",
          "boundary": [[2000, 250], [2900, 250], [2900, 1150], [2000, 1150]]
        }
      ],
      "openings": ["d1", "win1"]
    }
  ],

  "modules": [
    {
      "id": "m1",
      "moduleId": "sleep_master_01",
      "moduleName": "主卧睡眠组合",
      "bounds": [[1500, 3500], [4500, 3500], [4500, 5500], [1500, 5500]],
      "facing": "north",
      "zoneId": "z1",
      "items": [
        { "familyId": "bed_double_01", "offset": [0, 0], "role": "主体" },
        { "familyId": "nightstand_01", "offset": [-1200, 0], "role": "左床头柜" },
        { "familyId": "nightstand_01", "offset": [1200, 0], "role": "右床头柜" }
      ]
    },
    {
      "id": "m2",
      "moduleId": "wardrobe_01",
      "moduleName": "衣柜",
      "bounds": [[4500, 500], [5500, 500], [5500, 3000], [4500, 3000]],
      "facing": "west",
      "zoneId": "z1",
      "items": [
        { "familyId": "wardrobe_sliding_01", "offset": [0, 0], "role": "主体" }
      ]
    },
    {
      "id": "m3",
      "moduleId": "dresser_01",
      "moduleName": "梳妆台",
      "bounds": [[300, 3500], [1200, 3500], [1200, 4500], [300, 4500]],
      "facing": "east",
      "zoneId": "z1",
      "items": [
        { "familyId": "dresser_modern_01", "offset": [0, 0], "role": "主体" },
        { "familyId": "stool_round_01", "offset": [300, 0], "role": "凳子" }
      ]
    }
  ]
}
```

---

## 12. 附录：类型定义

### 12.1 TypeScript 类型

```typescript
// ============================================
// 基础几何类型 (Geometry Primitives)
// ============================================
type Point2D = [number, number];              // [x, y] 绝对位置
type Vec2D = [number, number];                // [dx, dy] 相对偏移（结构同 Point2D，语义不同）
type Line2D = [Point2D, Point2D];             // [[x1,y1], [x2,y2]] 线段
type Polygon2D = Point2D[];                   // [[x,y], ...] 多边形（隐式闭合）
type AABB = [number, number, number, number]; // [minX, minY, maxX, maxY] 包围盒

// ============================================
// 数据模型
// ============================================

// 画布文档
interface CanvasDocument {
  id: string;
  version: number;
  coordinateSystem: "cartesian_mm_yUp";
  metadata: Metadata;
  outline: Outline;
  rooms: Room[];
  zones: Zone[];
  wallFinishes: WallFinish[];
  modules: Module[];
}

// 元数据
interface Metadata {
  revitViewId: number;
  levelId: number;
  gridSize?: number;
}

// 可视化底图
interface Outline {
  walls: Wall[];
  openings: Opening[];
}

// 墙体轮廓
interface Wall {
  id: string;
  polygon: Polygon2D;
}

// 门窗
interface Opening {
  id: string;
  type: "door" | "window";
  line: Line2D;
}

// 物理房间
interface Room {
  id: string;
  name: string;
  type: RoomType;
  boundary: Polygon2D;
}

// 房间类型
type RoomType =
  | "living_room"
  | "dining_room"
  | "master_bedroom"
  | "bedroom"
  | "study"
  | "kitchen"
  | "bathroom"
  | "entrance"
  | "balcony"
  | "corridor"
  | "storage";

// 设计区域
interface Zone {
  id: string;
  name: string;
  roomId: string;
  tags: ZoneTag[];
  rawBoundary: Polygon2D;
  innerBoundary: Polygon2D;
  exclusionAreas?: ExclusionArea[];
  openings?: string[];
}

// 功能标签
type ZoneTag =
  | "tv_media"
  | "audio_video"
  | "sleep"
  | "bedhead_wall"
  | "rest"
  | "reading"
  | "work"
  | "study"
  | "wardrobe_storage"
  | "shoe_storage"
  | "general_storage"
  | "dining"
  | "cooking"
  | "food_prep"
  | "bar"
  | "shower"
  | "bathtub"
  | "toilet"
  | "washing"
  | "laundry"
  | "vanity"
  | "entry"
  | "passage"
  | "display"
  | "plants";

// 墙面完成面
interface WallFinish {
  id: string;
  locationLine: Line2D;
  finishModuleId?: string;
  thickness: number;
  exclusionBoundary: Polygon2D;
  wallId: string;
  roomId: string;
  source: FinishSource;
}

// 完成面来源
type FinishSource = "room_default" | "zone_override" | "user_override";

// 禁止布置区
interface ExclusionArea {
  id: string;
  type: "door_swing" | "passage" | "other";
  boundary: Polygon2D;        // 禁区边界（支持异形）
}

// 布置模块
interface Module {
  id: string;
  moduleId: string;
  moduleName?: string;
  bounds: Polygon2D;           // 精确边界（矩形 4 顶点）
  facing: Facing;              // 语义朝向或向量
  zoneId: string;
  items?: ModuleItem[];
}

// 模块内部家具
interface ModuleItem {
  familyId: string;
  offset: Vec2D;  // 相对模块中心的偏移
  role?: string;
}

// 朝向 - 联合类型（语义字符串 | Vec2D 向量）
type FacingSemantic =
  | "north"
  | "south"
  | "east"
  | "west"
  | "northeast"
  | "southeast"
  | "southwest"
  | "northwest";

type Facing = FacingSemantic | Vec2D;  // 语义字符串 或 单位向量 [dx, dy]
```

---

## 13. 模块库 Schema（待补充）

> **状态**：待用户提供模块库数据结构后补充

模块库 (Library-MCP) 为 AI 提供设计素材，每个模块定义包含：

### 13.1 预期结构

```typescript
interface ModuleDefinition {
  moduleId: string;              // 模块唯一标识
  moduleName: string;            // 可读名称
  category: string;              // 分类（bedroom/living/...）

  // 几何定义
  canonicalPolygon: Polygon2D;   // 局部坐标系下的精确轮廓
  obbSize: [number, number];     // 矩形最大包围盒尺寸 [width, height]

  // 参数化接口
  parameters?: {
    [key: string]: {
      min: number;
      max: number;
      default: number;
    };
  };

  // 内部家具清单
  items: ModuleItemDefinition[];
}
```

### 13.2 AI 使用流程

```
1. AI 调用 Library-MCP 搜索/获取模块定义
2. AI 根据 obbSize 进行布置决策
3. AI 输出 Intent: { moduleId, params?, center, facing }
4. Core Normalizer 根据 canonicalPolygon + params 生成精确 Polygon2D
```

### 13.3 待定义内容

- [ ] `ModuleDefinition` 完整字段
- [ ] `ModuleItemDefinition` 结构
- [ ] Library-MCP 工具接口（`module_search`, `module_get`）
- [ ] 参数化驱动机制

---

## 版本历史

| 版本 | 日期 | 变更内容 |
|------|------|----------|
| v2.5 | 2025-12-05 | **数据模型增强**：新增 Room（物理房间）；Zone 添加 roomId/tags/rawBoundary；新增 WallFinish（墙面完成面禁区机制）；完善完成面三层来源机制设计 |
| v2.4 | 2025-12-04 | **同步评审共识**：添加评审文档引用；JSON 数据结构保持不变，C# 实现细节见 Architecture.md §6.1 |
| v2.3 | 2025-12-03 | **落实讨论结论**：补充 §6.3.3 为什么不用 Angle；新增 §6.5 计算属性 (_computed)；新增 §11 模块库 Schema 占位章节 |
| v2.2 | 2025-12-03 | **几何类型架构升级**：Module.bounds 改为 Polygon2D；ExclusionArea.rect 改为 boundary: Polygon2D；Facing 支持联合类型（string \| Vec2D）；新增 §1.1 核心设计约束（AI = OBB 规划师） |
| v2.1 | 2025-12-03 | 新增 §1.5 单位规范、§1.6 几何图元；明确 Point2D/Vec2D/Line2D/Polygon2D/AABB 类型定义 |
| v2.0 | 2025-12-02 | **重大重构**：采用极简设计，outline + zones + modules 三层结构，AABB 包围盒，语义化朝向 |
| v1.1 | 2025-12-02 | 坐标系变更为 CAD 标准（Y-up） |
| v1.0 | 2025-12-02 | 初始版本 |
