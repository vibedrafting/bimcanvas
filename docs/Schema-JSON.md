# BIMCanvas JSON Schema 规范

> 版本：v2.0 (极简版)
> 更新日期：2025-12-02
> 状态：已定稿（基于业务专家评审）

---

## 1. 设计原则

### 1.1 核心原则：KISS (Keep It Simple, Stupid)

| 决策点 | 选择 | 理由 |
|--------|------|------|
| **坐标系** | Y-Up (笛卡尔) | 符合 CAD/BIM/数学直觉，只在前端渲染时转换 |
| **数据分层** | Layer 1 (AI 上下文) | Token 效率，职责清晰 |
| **墙体表示** | 封闭轮廓多边形 | AI 不需要理解墙体结构，只需知道空间边界 |
| **门窗表示** | 简化为线段 | 厚度不影响家具布置 |
| **门扇区域** | 预计算为矩形禁区（AABB） | AI 只需知道"这里不能放" |
| **房间结构** | 只有 zones，无 rooms | 单一数据源原则，zones 是设计概念 |
| **标高信息** | 全局 levelId | 一张平面图对应一个 Level |
| **布置单元** | modules（模块） | 支持单一家具或组合（如睡眠模块=床+床头柜） |
| **模块位置** | AABB 包围盒 | 直观显示占用空间，碰撞检测简单 |
| **模块朝向** | 语义化方向 | AI 友好，插件端转换为角度 |

### 1.2 数据分层架构

```
┌─────────────────────────────────────────────────────────────────┐
│                     数据分层架构                                 │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│   【Layer 1: AI 上下文】- CanvasDocument.json                    │
│   ┌─────────────────────────────────────────────────────────┐   │
│   │  • outline: 墙轮廓多边形 + 门窗线段（仅几何，无属性）       │   │
│   │  • zones: 可用空间 + 禁区（innerBoundary + exclusionAreas） │   │
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

### 1.3 坐标系统

- **类型**：笛卡尔坐标系（Cartesian）
- **原点**：视图裁剪框左下角
- **X 轴**：向右为正
- **Y 轴**：向上为正（CAD 标准）
- **单位**：毫米 (mm)

> **重要**：这是 CAD 标准坐标系，与 Web 屏幕坐标系（Y 向下）相反。
> 前端渲染时必须进行显式坐标转换：`y_screen = canvasHeight - y_model * scale`

### 1.4 单位规范

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

### 1.5 几何图元 (Geometry Primitives)

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

### 1.6 数据流

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

  "zones": [
    {
      "id": "z1",
      "name": "主卧",
      "function": "master_bedroom",
      "innerBoundary": [ [50,50], [5950,50], [5950,5950], [50,5950] ],
      "exclusionAreas": [
        {
          "id": "ex1",
          "type": "door_swing",
          "rect": [2000, 0, 2900, 900]
        }
      ],
      "openings": ["d1", "win1"]
    }
  ],

  "modules": [
    {
      "id": "m1",
      "moduleId": "sleep_master_01",
      "moduleName": "主卧睡眠模块",
      "bounds": [1500, 2000, 4500, 4500],
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
| `zones` | array | 是 | 设计区域列表 |
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

## 5. zones（设计区域）

AI 的核心工作区。每个 zone 定义一个可布置空间及其约束。

### 5.1 Zone 完整定义

```json
{
  "zones": [
    {
      "id": "z1",
      "name": "主卧",
      "function": "master_bedroom",
      "innerBoundary": [ [50,50], [5950,50], [5950,5950], [50,5950] ],
      "exclusionAreas": [
        {
          "id": "ex1",
          "type": "door_swing",
          "rect": [2000, 0, 2900, 900]
        }
      ],
      "openings": ["d1", "win1"]
    }
  ]
}
```

### 5.2 Zone 字段说明

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `id` | string | 是 | 区域 ID，格式：`z{序号}` |
| `name` | string | 是 | 区域名称（用户可见） |
| `function` | string | 是 | 功能类型 |
| `innerBoundary` | number[][] | 是 | **可用空间轮廓**（已扣除完成面） |
| `exclusionAreas` | object[] | 否 | **禁止布置区**（门扇、必要通道等） |
| `openings` | string[] | 否 | 关联的门窗 ID |

### 5.3 function（功能类型）

| 值 | 说明 |
|-----|------|
| `living` | 客厅/起居室 |
| `dining` | 餐厅 |
| `master_bedroom` | 主卧 |
| `bedroom` | 次卧 |
| `study` | 书房 |
| `kitchen` | 厨房 |
| `bathroom` | 卫生间 |
| `entrance` | 玄关 |
| `balcony` | 阳台 |
| `corridor` | 走廊 |
| `storage` | 储物间 |

### 5.4 innerBoundary 计算规则

**插件端在导出时自动计算：**

```
innerBoundary = Revit房间边界 - 各边墙体完成面厚度
```

AI 直接使用 `innerBoundary`，无需理解完成面概念。

### 5.5 exclusionAreas（禁止布置区）

```json
{
  "exclusionAreas": [
    {
      "id": "ex1",
      "type": "door_swing",
      "rect": [2000, 0, 2900, 900]
    },
    {
      "id": "ex2",
      "type": "passage",
      "rect": [0, 2500, 500, 3500]
    }
  ]
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | string | 禁区 ID |
| `type` | string | 类型：`door_swing` / `passage` / `other` |
| `rect` | number[] | **简化矩形（AABB）**：`[minX, minY, maxX, maxY]` |

**type 可选值：**
- `door_swing`：门扇开启区域
- `passage`：必要通道
- `other`：其他禁区

---

## 6. modules（布置模块）

模块是最小布置单元，可以是单一家具或家具组合。

### 6.1 Module 完整定义

```json
{
  "modules": [
    {
      "id": "m1",
      "moduleId": "sleep_master_01",
      "moduleName": "主卧睡眠模块",
      "bounds": [1500, 2000, 4500, 4500],
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

### 6.2 Module 字段说明

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `id` | string | 是 | 模块实例 ID，格式：`m{序号}` |
| `moduleId` | string | 是 | 模块库中的模块类型 ID |
| `moduleName` | string | 否 | 可读名称（如"主卧睡眠模块"） |
| `bounds` | number[] | 是 | AABB 包围盒：`[minX, minY, maxX, maxY]` |
| `facing` | string | 是 | 语义化朝向 |
| `zoneId` | string | 是 | 所属区域 ID |
| `items` | object[] | 否 | 模块内部家具清单（回写 Revit 用） |

### 6.3 facing（语义化朝向）

| 值 | 含义 | 插件转换角度 |
|----|------|-------------|
| `north` | 朝北 | 0° |
| `east` | 朝东 | 90° |
| `south` | 朝南 | 180° |
| `west` | 朝西 | 270° |
| `northeast` | 朝东北 | 45° |
| `southeast` | 朝东南 | 135° |
| `southwest` | 朝西南 | 225° |
| `northwest` | 朝西北 | 315° |

**插件端转换规则：**
```csharp
// 语义方向 → 旋转角度
north → 0°     south → 180°
east → 90°     west → 270°
```

### 6.4 items（模块内部家具）

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

---

## 7. AI 布置逻辑

### 7.1 核心约束规则

```
对于每个要放置的模块：
1. 模块 bounds 必须完全在 zone.innerBoundary 内
2. 模块 bounds 不能与任何 zone.exclusionAreas 重叠
3. 模块 bounds 不能与其他已放置模块重叠
```

### 7.2 碰撞检测伪代码

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

## 8. 版本控制

### 8.1 版本号机制

- 每次画布修改，`version` 递增 1
- 用于乐观锁，防止并发冲突

### 8.2 乐观锁使用

AI 调用修改工具时携带 `expectedVersion`：

```json
{
  "tool": "module_add",
  "params": {
    "canvasId": "canvas_001",
    "expectedVersion": 42,
    "moduleId": "sleep_master_01",
    "bounds": [1500, 2000, 4500, 4500],
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

## 9. 完整示例

### 9.1 典型卧室布置

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
          "rect": [2000, 250, 2900, 1150]
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
      "bounds": [1500, 3500, 4500, 5500],
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
      "bounds": [4500, 500, 5500, 3000],
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
      "bounds": [300, 3500, 1200, 4500],
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

## 10. 附录：类型定义

### 10.1 TypeScript 类型

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
  zones: Zone[];
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

// 设计区域
interface Zone {
  id: string;
  name: string;
  function: ZoneFunction;
  innerBoundary: Polygon2D;
  exclusionAreas?: ExclusionArea[];
  openings?: string[];
}

// 禁止布置区
interface ExclusionArea {
  id: string;
  type: "door_swing" | "passage" | "other";
  rect: AABB;
}

// 布置模块
interface Module {
  id: string;
  moduleId: string;
  moduleName?: string;
  bounds: AABB;
  facing: Facing;
  zoneId: string;
  items?: ModuleItem[];
}

// 模块内部家具
interface ModuleItem {
  familyId: string;
  offset: Vec2D;  // 相对模块中心的偏移
  role?: string;
}

// 功能类型
type ZoneFunction =
  | "living"
  | "dining"
  | "master_bedroom"
  | "bedroom"
  | "study"
  | "kitchen"
  | "bathroom"
  | "entrance"
  | "balcony"
  | "corridor"
  | "storage";

// 朝向
type Facing =
  | "north"
  | "south"
  | "east"
  | "west"
  | "northeast"
  | "southeast"
  | "southwest"
  | "northwest";
```

---

## 版本历史

| 版本 | 日期 | 变更内容 |
|------|------|----------|
| v2.1 | 2025-12-03 | 新增 §1.4 单位规范、§1.5 几何图元；明确 Point2D/Vec2D/Line2D/Polygon2D/AABB 类型定义 |
| v2.0 | 2025-12-02 | **重大重构**：采用极简设计，outline + zones + modules 三层结构，AABB 包围盒，语义化朝向 |
| v1.1 | 2025-12-02 | 坐标系变更为 CAD 标准（Y-up） |
| v1.0 | 2025-12-02 | 初始版本 |
