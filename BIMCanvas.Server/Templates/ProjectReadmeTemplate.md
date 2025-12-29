# {PROJECT_NAME} - BIMCanvas 项目工作区

> 本文档由 BIMCanvas Server 自动生成，帮助 AI/用户理解项目结构和数据格式。
>
> **生成时间**: {EXPORT_DATE}
> **数据版本**: v3.0 (File-Driven Architecture)

---

## 1. 快速导航

| 数据类型 | 文件位置 | 读写属性 | 用途 |
|----------|----------|----------|------|
| 建筑轮廓 | `baseline/walls.json` | 只读 | Revit 导出的墙体几何 |
| 柱子轮廓 | `baseline/columns.json` | 只读 | Revit 导出的柱子几何 |
| 门窗开口 | `baseline/openings.json` | 只读 | 门窗定位线和类型 |
| 物理房间 | `baseline/rooms.json` | 只读 | Revit Room 边界和类型 |
| 定位线 | `baseline/locationLines.json` | 只读 | 完成面定位基准线 |
| 设计区域 | `schemes/{id}/zones.json` | 读写 | 功能分区和标签 |
| 完成面 | `schemes/{id}/finishes.json` | 读写 | 墙面完成面分段 |
| **布置模块** | `schemes/{id}/modules.json` | **读写** | **家具布置信息** |
| 禁区 | `computed/exclusions.json` | 自动生成 | 门扇禁区等 |

---

## 2. 项目文件结构

```
{PROJECT_FOLDER}/
├── manifest.json              # 项目元数据
├── README.md                  # 本文档
│
├── baseline/                  # 【底层】建筑基础数据（只读）
│   ├── walls.json             # 墙体轮廓
│   ├── columns.json           # 柱子轮廓
│   ├── openings.json          # 门窗开口
│   ├── rooms.json             # 物理房间
│   └── locationLines.json     # 完成面定位线
│
├── schemes/                   # 【中层】方案数据
│   └── {schemeId}/            # 每个方案一个文件夹
│       ├── zones.json         # 设计区域划分
│       ├── finishes.json      # 完成面配置
│       └── modules.json       # 家具布置模块
│
└── computed/                  # 【顶层】计算派生数据（自动生成）
    └── exclusions.json        # 禁区数据
```

### 2.1 三层数据模型

| 层级 | 文件夹 | 读写属性 | 说明 |
|:---:|--------|----------|------|
| **底层** | `baseline/` | 只读 | Revit 导出的原始建筑数据，作为静态背景 |
| **中层** | `schemes/{id}/` | 读写 | AI/用户可编辑的设计方案数据 |
| **顶层** | `computed/` | 自动生成 | Server 计算的派生数据 |

---

## 3. 坐标系统

- **类型**: 笛卡尔坐标系 (Cartesian)
- **原点**: 视图左下角
- **X 轴**: 向右为正
- **Y 轴**: 向上为正 (CAD 标准)
- **单位**: 毫米 (mm)

```
        Y ↑
          │
          │
          └───────→ X
       原点 (0,0)
```

---

## 4. baseline/ - 建筑基础数据

### 4.1 walls.json - 墙体轮廓

```json
[
  {
    "id": "wall_001",
    "elementId": 12345,
    "polygon": [[0, 0], [6000, 0], [6000, 200], [0, 200]]
  }
]
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | string | 墙体 ID，格式: `wall_{序号}` |
| `elementId` | number | Revit 元素 ID |
| `polygon` | number[][] | 墙体轮廓顶点 `[[x1,y1], [x2,y2], ...]` |

### 4.2 columns.json - 柱子轮廓

```json
[
  {
    "id": "col_001",
    "elementId": 23456,
    "isStructural": true,
    "polygon": [[3000, 0], [3500, 0], [3500, 500], [3000, 500]]
  }
]
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | string | 柱子 ID，格式: `col_{序号}` 或 `scol_{序号}` |
| `elementId` | number | Revit 元素 ID |
| `isStructural` | boolean | `true` = 结构柱 |
| `polygon` | number[][] | 柱子轮廓顶点 |

### 4.3 openings.json - 门窗开口

```json
[
  {
    "id": "d1",
    "type": "door",
    "line": [[2000, 0], [2900, 0]],
    "direction": [0, 1],
    "swingDirection": "inward"
  },
  {
    "id": "win1",
    "type": "window",
    "line": [[3500, 6000], [5300, 6000]]
  }
]
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | string | 门: `d{序号}`，窗: `win{序号}` |
| `type` | string | `door` 或 `window` |
| `line` | number[][] | 定位线段 `[[x1,y1], [x2,y2]]` |
| `direction` | number[] | 开启方向向量 (仅门) |
| `swingDirection` | string | `inward`/`outward` (仅门) |

### 4.4 rooms.json - 物理房间

```json
[
  {
    "id": "r1",
    "name": "主卧",
    "type": "master_bedroom",
    "boundary": [[0, 0], [6000, 0], [6000, 6000], [0, 6000]]
  }
]
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | string | 房间 ID，格式: `r{序号}` |
| `name` | string | 房间名称 |
| `type` | string | 房间类型 (见下表) |
| `boundary` | number[][] | 房间边界顶点 |

**房间类型 (RoomType)**:

| 值 | 说明 | 值 | 说明 |
|---|---|---|---|
| `living_room` | 客厅 | `kitchen` | 厨房 |
| `dining_room` | 餐厅 | `bathroom` | 卫生间 |
| `master_bedroom` | 主卧 | `entrance` | 玄关 |
| `bedroom` | 次卧 | `balcony` | 阳台 |
| `study` | 书房 | `corridor` | 走廊 |

### 4.5 locationLines.json - 完成面定位线

```json
[
  {
    "id": "ll_001",
    "wallId": "wall_001",
    "roomId": "r1",
    "line": [[200, 200], [200, 5800]],
    "normal": [1, 0]
  }
]
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | string | 定位线 ID |
| `wallId` | string | 关联墙体 ID |
| `roomId` | string | 关联房间 ID |
| `line` | number[][] | 定位线段 |
| `normal` | number[] | 法向量 (指向房间内部) |

---

## 5. schemes/{schemeId}/ - 方案数据

每个设计方案拥有独立的文件夹，包含该方案的所有可编辑数据。

### 5.1 zones.json - 设计区域

```json
[
  {
    "id": "z1",
    "name": "主卧",
    "type": "room",
    "reason": "从 Revit Room 自动转换",
    "rawBoundary": [[200, 200], [5800, 200], [5800, 5800], [200, 5800]],
    "computedBoundary": [[220, 220], [5780, 220], [5780, 5780], [220, 5780]],
    "tags": [],
    "roomId": "r1"
  },
  {
    "id": "z2",
    "name": "睡眠区",
    "type": "designable",
    "reason": "AI 划分的功能区",
    "rawBoundary": [[220, 3000], [5780, 3000], [5780, 5780], [220, 5780]],
    "computedBoundary": [[220, 3000], [5780, 3000], [5780, 5780], [220, 5780]],
    "tags": ["sleep", "bedhead_wall"],
    "roomId": "r1"
  }
]
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | string | 区域 ID，格式: `z{序号}` |
| `name` | string | 区域名称 |
| `type` | string | 区域类型: `exclusion`/`room`/`designable` |
| `reason` | string | 产生原因说明 |
| `rawBoundary` | number[][] | 原始边界 |
| `computedBoundary` | number[][] | 计算边界 (扣除完成面后) |
| `tags` | string[] | 功能标签列表 |
| `roomId` | string | 所属房间 ID |

**区域类型 (ZoneType)**:

| 类型 | 说明 | 可布置家具 |
|------|------|:---:|
| `exclusion` | 禁区 (门扇开启区等) | ❌ |
| `room` | 房间 (Revit Room 转换) | ✅ |
| `designable` | 设计区 (功能分区) | ✅ |

**常用功能标签 (ZoneTag)**:

| 标签 | 说明 | 标签 | 说明 |
|------|------|------|------|
| `sleep` | 睡眠区 | `tv_media` | 电视区 |
| `bedhead_wall` | 床头背景墙 | `rest` | 休憩区 |
| `reading` | 阅读区 | `work` | 工作区 |
| `wardrobe_storage` | 衣柜区 | `dining` | 用餐区 |

### 5.2 finishes.json - 完成面分段

```json
[
  {
    "id": "wf1",
    "locationLineId": "ll_001",
    "startT": 0.0,
    "endT": 1.0,
    "finishModuleId": "finish_paint_01",
    "thickness": 20,
    "source": "room_default"
  }
]
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | string | 完成面 ID |
| `locationLineId` | string | 关联定位线 ID |
| `startT` | number | 起始参数 (0.0-1.0) |
| `endT` | number | 结束参数 (0.0-1.0) |
| `finishModuleId` | string | 完成面类型 ID |
| `thickness` | number | 厚度 (mm) |
| `source` | string | 来源: `room_default`/`zone_override`/`user_override` |

### 5.3 modules.json - 布置模块 ⭐

**这是 AI/用户最常操作的文件，用于定义家具布置信息。**

```json
[
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
```

#### 5.3.1 Module 字段说明

| 字段 | 类型 | 必填 | 说明 |
|------|------|:---:|------|
| `id` | string | ✅ | 模块实例 ID，格式: `m{序号}` |
| `moduleId` | string | ✅ | 模块库中的类型 ID |
| `moduleName` | string | ❌ | 可读名称 |
| `bounds` | number[][] | ✅ | 边界多边形 (矩形 4 顶点) |
| `facing` | string/number[] | ✅ | 朝向 (见下方说明) |
| `zoneId` | string | ✅ | 所属区域 ID |
| `items` | object[] | ❌ | 模块内部家具清单 |

#### 5.3.2 bounds - 边界定义

`bounds` 是矩形的 4 个顶点，**逆时针排列**:

```
      [1]─────────[2]
       │           │
       │  模块区域  │
       │           │
      [0]─────────[3]

bounds = [[x0,y0], [x1,y1], [x2,y2], [x3,y3]]
```

**示例**: 一个 3000×2500mm 的模块，左下角在 (1500, 2000):
```json
"bounds": [[1500, 2000], [1500, 4500], [4500, 4500], [4500, 2000]]
```

#### 5.3.3 facing - 朝向定义

朝向支持两种格式:

**1. 语义字符串** (推荐):

| 语义 | 向量 | 角度 |
|------|------|------|
| `north` | [0, 1] | 90° |
| `south` | [0, -1] | 270° |
| `east` | [1, 0] | 0° |
| `west` | [-1, 0] | 180° |
| `northeast` | [0.707, 0.707] | 45° |
| `southeast` | [0.707, -0.707] | 315° |
| `southwest` | [-0.707, -0.707] | 225° |
| `northwest` | [-0.707, 0.707] | 135° |

**2. Vec2D 向量** (任意角度):
```json
"facing": [0.866, 0.5]  // 30° 方向
```

#### 5.3.4 items - 家具清单

用于回写 Revit 时创建具体家具实例:

```json
{
  "familyId": "bed_double_01",  // 族库中的 Family ID
  "offset": [0, 0],             // 相对模块中心的偏移 [dx, dy]
  "role": "主体"                // 在模块中的角色
}
```

---

## 6. computed/ - 计算派生数据

### 6.1 exclusions.json - 禁区数据

```json
[
  {
    "id": "ex1",
    "sourceType": "door_swing",
    "sourceId": "d1",
    "boundary": [[2000, 200], [2900, 200], [2900, 1100], [2000, 1100]],
    "reason": "门 d1 的开启扫过区域"
  }
]
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | string | 禁区 ID |
| `sourceType` | string | 来源类型: `door_swing`/`passage`/`finish` |
| `sourceId` | string | 来源元素 ID |
| `boundary` | number[][] | 禁区边界 |
| `reason` | string | 产生原因 |

---

## 7. 布置约束规则

在添加或移动模块时，必须遵守以下约束:

### 7.1 核心约束

```
对于每个要放置的模块:
1. ✅ bounds 必须完全在 zone.computedBoundary 内
2. ❌ bounds 不能与任何 exclusions 重叠
3. ❌ bounds 不能与其他已放置 modules 重叠
```

### 7.2 碰撞检测逻辑

```javascript
function canPlaceModule(module, zone, exclusions, existingModules) {
  // 约束1: 必须在区域内
  if (!isInsidePolygon(module.bounds, zone.computedBoundary)) {
    return false;
  }

  // 约束2: 不能与禁区重叠
  for (const ex of exclusions) {
    if (polygonsIntersect(module.bounds, ex.boundary)) {
      return false;
    }
  }

  // 约束3: 不能与其他模块重叠
  for (const other of existingModules) {
    if (polygonsIntersect(module.bounds, other.bounds)) {
      return false;
    }
  }

  return true;
}
```

---

## 8. 如何添加布置模块

### 8.1 空白 modules.json 模板

如果方案文件夹中没有 `modules.json` 或内容为空，创建以下结构:

```json
[]
```

### 8.2 添加单个家具示例

添加一个 1800×2000mm 的双人床，朝北:

```json
[
  {
    "id": "m1",
    "moduleId": "bed_double_01",
    "moduleName": "双人床",
    "bounds": [[2000, 3500], [2000, 5500], [3800, 5500], [3800, 3500]],
    "facing": "north",
    "zoneId": "z1",
    "items": [
      { "familyId": "bed_double_01", "offset": [0, 0], "role": "主体" }
    ]
  }
]
```

### 8.3 添加组合模块示例

添加一个睡眠组合 (床 + 两个床头柜):

```json
[
  {
    "id": "m1",
    "moduleId": "sleep_master_01",
    "moduleName": "主卧睡眠组合",
    "bounds": [[1000, 3000], [1000, 5500], [4000, 5500], [4000, 3000]],
    "facing": "north",
    "zoneId": "z1",
    "items": [
      { "familyId": "bed_double_01", "offset": [0, 0], "role": "主体" },
      { "familyId": "nightstand_01", "offset": [-1200, 0], "role": "左床头柜" },
      { "familyId": "nightstand_01", "offset": [1200, 0], "role": "右床头柜" }
    ]
  }
]
```

### 8.4 多个模块示例

```json
[
  {
    "id": "m1",
    "moduleId": "sleep_master_01",
    "moduleName": "睡眠组合",
    "bounds": [[1000, 3000], [1000, 5500], [4000, 5500], [4000, 3000]],
    "facing": "north",
    "zoneId": "z1",
    "items": [
      { "familyId": "bed_double_01", "offset": [0, 0], "role": "主体" }
    ]
  },
  {
    "id": "m2",
    "moduleId": "wardrobe_01",
    "moduleName": "衣柜",
    "bounds": [[4500, 500], [4500, 3000], [5500, 3000], [5500, 500]],
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
    "bounds": [[300, 3500], [300, 4500], [1000, 4500], [1000, 3500]],
    "facing": "east",
    "zoneId": "z1",
    "items": [
      { "familyId": "dresser_modern_01", "offset": [0, 0], "role": "主体" }
    ]
  }
]
```

---

## 9. 常见问题

### Q1: 如何确定模块应该放在哪个 Zone?

查看 `zones.json`，找到 `type` 为 `room` 或 `designable` 的区域，使用其 `id` 作为 `zoneId`。

### Q2: 如何避免与门扇区域冲突?

查看 `computed/exclusions.json`，确保模块的 `bounds` 不与任何禁区的 `boundary` 重叠。

### Q3: bounds 的顶点顺序重要吗?

是的，必须按**逆时针**顺序排列，且形成一个**封闭**的矩形。

### Q4: 可以使用 45° 以外的角度吗?

可以，使用 Vec2D 向量格式:
```json
"facing": [0.866, 0.5]  // cos(30°), sin(30°)
```

---

## 10. 数据校验

修改 `modules.json` 后，Server 会自动校验:

| 检查项 | 错误类型 | 说明 |
|--------|----------|------|
| bounds 格式 | `INVALID_BOUNDS` | 必须是 4 个顶点的多边形 |
| zoneId 存在 | `ZONE_NOT_FOUND` | 必须引用存在的区域 |
| 区域内 | `OUT_OF_BOUNDS` | 模块必须在区域边界内 |
| 禁区冲突 | `EXCLUSION_CONFLICT` | 与禁区重叠 |
| 模块冲突 | `MODULE_CONFLICT` | 与其他模块重叠 |

---

## 附录: manifest.json 结构

```json
{
  "id": "{PROJECT_ID}",
  "name": "{PROJECT_NAME}",
  "version": 1,
  "exportDate": "{EXPORT_DATE}",
  "coordinateSystem": "cartesian_mm_yUp",
  "schemes": [
    {
      "id": "default",
      "name": "默认方案",
      "description": "初始方案"
    }
  ],
  "activeSchemeId": "default"
}
```

---

*本文档由 BIMCanvas Server 自动生成。如需更新，请删除本文件后重新打开项目。*
