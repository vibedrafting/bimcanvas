# {PROJECT_NAME} - BIMCanvas 项目工作区

> 本文档帮助 AI 快速理解项目结构。详细数据格式请直接读取对应文件。
>
> **生成时间**: {EXPORT_DATE} | **数据版本**: v3.0

---

## 1. 文件导航

| 数据类型 | 文件位置 | 读写 | 说明 |
|----------|----------|:----:|------|
| 项目配置 | `project.json` | 读写 | 项目元数据 |
| 墙柱轮廓 | `baseline/architecture.json` | 只读 | Revit 导出 |
| 门窗开口 | `baseline/openings.json` | 只读 | 门窗定位线 |
| 物理房间 | `baseline/rooms.json` | 只读 | 房间边界 |
| 定位线 | `baseline/location_lines.json` | 只读 | 完成面定位 |
| 设计区域 | `computed/room_zones.json` | 自动 | 派生区域 |
| 禁区 | `computed/exclusions.json` | 自动 | 门扇禁区等 |
| 设计需求 | `context/requirements.md` | 读写 | 用户需求 |
| 方案配置 | `schemes/strategy.json` | 读写 | 策略参数 |
| 区域配置 | `schemes/zones.json` | 读写 | 设计分区 |
| **布置模块** | `schemes/rz_*/modules.json` | **读写** | **家具布置** |

---

## 2. 目录结构

```
{PROJECT_FOLDER}/
├── project.json                # 项目元数据
├── baseline/                   # 【底层】建筑数据（只读）
│   ├── architecture.json       # 墙体 + 柱子
│   ├── openings.json           # 门窗开口
│   ├── rooms.json              # 物理房间
│   └── location_lines.json     # 定位线
├── computed/                   # 【中层】派生数据（自动）
│   ├── room_zones.json         # 设计区域
│   └── exclusions.json         # 禁区
├── context/                    # 设计上下文
│   └── requirements.md         # 用户需求
└── schemes/                    # 【顶层】策略数据（读写）
    ├── strategy.json           # 方案配置
    ├── zones.json              # 设计分区
    ├── finishes.json           # 完成面
    └── rz_*/modules.json       # 各分区布置
```

**三层架构**: baseline（只读）→ computed（自动）→ schemes（读写）

---

## 3. 坐标系统

- **原点**: 左下角 | **X**: 向右 | **Y**: 向上 | **单位**: mm

---

## 4. 布置约束

```
对于每个要放置的模块:
1. bounds 必须完全在 zones[].rawBoundary 内
2. bounds 不能与任何 exclusions[].rawBoundary 重叠
3. bounds 不能与其他已放置 modules[] 重叠
```

---

*本文档由 BIMCanvas Server 自动生成*

### 5.1 metadata.json - 坐标变换参数

```json
{
  "exportDate": "2025-12-25T21:28:00+08:00",
  "revitVersion": "2019",
  "placementElevation": 0.0,
  "origin": [0.0, 0.0, 0.0],
  "rotation": 0.0,
  "transformMethod": "boundingBox",
  "unitSystem": "metric_mm"
}
```

### 5.2 architecture.json - 墙体和柱子

```json
{
  "walls": [
    {
      "id": "w_1",
      "elementId": 431228,
      "isStructural": false,
      "polygon": [[200, 200], [200, 5900], [0, 5900], [0, 200]]
    }
  ],
  "columns": [
    {
      "id": "col_1",
      "elementId": 12345,
      "isStructural": true,
      "polygon": [[3000, 0], [3500, 0], [3500, 500], [3000, 500]]
    }
  ]
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | string | 墙体/柱子 ID |
| `elementId` | number | Revit 元素 ID |
| `isStructural` | boolean | 是否为结构构件 |
| `polygon` | number[][] | 轮廓顶点 `[[x1,y1], [x2,y2], ...]` |

### 5.3 openings.json - 门窗开口

```json
[
  {
    "id": "d_1",
    "type": 0,
    "line": [[2260, 8950], [3260, 8950]],
    "facingDirection": [0, 1],
    "handDirections": [[1, 0]]
  },
  {
    "id": "wi_1",
    "type": 1,
    "line": [[3000, 800], [8000, 800]],
    "facingDirection": [0, 1]
  }
]
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | string | 门: `d_{序号}`，窗: `wi_{序号}` |
| `type` | number | **0** = 门，**1** = 窗 |
| `line` | number[][] | 定位线段 `[[x1,y1], [x2,y2]]` |
| `facingDirection` | number[] | 开口朝向向量 |
| `handDirections` | number[][] | 门扇开启方向（仅门，可多个） |

### 5.4 rooms.json - 物理房间

```json
[
  {
    "id": "r_1",
    "name": "次卧一",
    "type": 3,
    "boundary": [[9400, 10500], [6600, 10500], [6600, 7100], [9400, 7100]]
  },
  {
    "id": "r_3",
    "name": "主卧",
    "type": 2,
    "boundary": [[14100, 5750], [9100, 5750], [9100, 900], [12400, 900], [12400, 4200], [14100, 4200]]
  }
]
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | string | 房间 ID，格式: `r_{序号}` |
| `name` | string | 房间名称 |
| `type` | number | 房间类型（见下表） |
| `boundary` | number[][] | 房间边界顶点 |

**房间类型 (RoomType)**:

| 值 | 类型 | 值 | 类型 |
|:---:|------|:---:|------|
| 0 | LivingRoom (客厅) | 5 | Study (书房) |
| 1 | DiningRoom (餐厅) | 6 | Bathroom (卫生间) |
| 2 | MasterBedroom (主卧) | 7 | Entrance (玄关) |
| 3 | Bedroom (次卧) | 8 | Balcony (阳台) |
| 4 | Kitchen (厨房) | 9 | Corridor (走廊) |

### 5.5 location_lines.json - 定位线

```json
[
  {
    "id": "ll001",
    "wallId": "w_42",
    "roomId": "r_6",
    "side": "interior",
    "line": [[2100, 1773], [2100, 2700]],
    "length": 927.29
  }
]
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | string | 定位线 ID |
| `wallId` | string | 关联墙体 ID |
| `roomId` | string | 关联房间 ID（可为空字符串） |
| `side` | string | 侧面类型: `interior` |
| `line` | number[][] | 定位线段 |
| `length` | number | 线段长度 (mm) |

---

## 6. computed/ - 计算派生数据

### 6.1 room_zones.json - 房间区域

从房间自动派生的设计区域。

```json
[
  {
    "id": "rz_1",
    "name": "次卧一",
    "roomId": "r_1",
    "type": 1,
    "reason": "room:Bedroom",
    "rawBoundary": [[9400, 10500], [6600, 10500], [6600, 7100], [9400, 7100]],
    "computedBoundary": null,
    "tags": [],
    "finishRequirements": [],
    "schemeId": null
  }
]
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | string | 区域 ID，格式: `rz_{序号}` (room zone) |
| `name` | string | 区域名称 |
| `roomId` | string | 关联房间 ID |
| `type` | number | **0** = 禁区，**1** = 房间/设计区 |
| `reason` | string | 产生原因 (如 `room:Bedroom`) |
| `rawBoundary` | number[][] | 原始边界 |
| `computedBoundary` | number[][] | 计算边界（扣除完成面后） |
| `tags` | string[] | 功能标签 |

### 6.2 exclusions.json - 禁区

门扇开启区域等禁止布置区。

```json
[
  {
    "id": "ez_1",
    "name": "门扇禁区",
    "roomId": "",
    "type": 0,
    "reason": "door_swing:门 d_1 的开启扫过区域",
    "rawBoundary": [[2260, 8950], [3260, 8950], [3260, 9950], [2260, 9950]],
    "computedBoundary": null,
    "tags": [],
    "finishRequirements": [],
    "schemeId": null
  }
]
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | string | 禁区 ID，格式: `ez_{序号}` (exclusion zone) |
| `type` | number | 固定为 **0** (禁区) |
| `reason` | string | 产生原因 (如 `door_swing:门 d_1 的开启扫过区域`) |
| `rawBoundary` | number[][] | 禁区边界 |

---

## 7. schemes/ - 策略数据

分区子目录架构：布置数据按房间分区组织，每个 `rz_*/` 目录存放该分区的 `modules.json`，多策略通过 Git 分支切换。

### 7.1 strategy.json - 方案配置

包含方案元数据和 AI 布置时的策略参数。

```json
{
  "id": "default",
  "name": "默认方案",
  "description": "从 demo_1.json 迁移的家具布置方案",
  "createdAt": "2025-12-25T21:28:00+08:00",
  "baselineHash": "",
  "strategy": {
    "approach": "balanced",
    "storageWeight": 0.5,
    "circulationWeight": 0.5,
    "furnitureCount": "normal"
  }
}
```

#### 元数据字段

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | string | 方案 ID |
| `name` | string | 方案名称 |
| `description` | string | 方案描述 |
| `createdAt` | string | 创建时间 |
| `baselineHash` | string | 关联的基线版本哈希 |

#### 策略参数 (strategy)

**AI 在布置时应读取这些参数，指导布置决策。**

| 字段 | 类型 | 取值范围 | 说明 |
|------|------|----------|------|
| `approach` | string | `storage` / `circulation` / `minimal` / `balanced` | 总体策略倾向 |
| `storageWeight` | number | 0.0 ~ 1.0 | 收纳优先权重（越高越倾向增加储物家具） |
| `circulationWeight` | number | 0.0 ~ 1.0 | 动线优先权重（越高越倾向保留通道宽度） |
| `furnitureCount` | string | `min` / `normal` / `max` | 家具数量偏好 |

**策略参数影响布置决策**：

| 场景 | storageWeight | circulationWeight | 布置倾向 |
|------|---------------|-------------------|----------|
| 极致收纳 | 0.9 | 0.2 | 增加柜体，牺牲部分通道宽度 |
| 动线优先 | 0.2 | 0.9 | 保留宽敞通道，减少非必要家具 |
| 极简留白 | 0.2 | 0.5 | `furnitureCount=min`，仅保留核心家具 |
| 均衡方案 | 0.5 | 0.5 | 平衡收纳与动线 |

### 7.2 rz_*/modules.json - 分区布置模块 ⭐

**每个分区子目录下都有独立的 modules.json，用于定义该分区的家具布置信息。**

**文件路径示例**：
- `schemes/rz_1/modules.json` - 次卧一的布置
- `schemes/rz_2/modules.json` - 次卧二的布置
- `schemes/rz_3/modules.json` - 主卧的布置

**内容示例** (`schemes/rz_3/modules.json` - 主卧)：

```json
[
  {
    "id": "m_1",
    "moduleId": "bed_king",
    "moduleName": "King Bed",
    "bounds": [[9100, 1750], [11100, 1750], [11100, 3750], [9100, 3750]],
    "facing": "east",
    "zoneId": "rz_3",
    "items": []
  },
  {
    "id": "m_2",
    "moduleId": "nightstand",
    "moduleName": "Nightstand Left",
    "bounds": [[9100, 3750], [9600, 3750], [9600, 4250], [9100, 4250]],
    "facing": "east",
    "zoneId": "rz_3",
    "items": []
  }
]
```

#### 7.2.1 Module 字段说明

| 字段 | 类型 | 必填 | 说明 |
|------|------|:---:|------|
| `id` | string | ✅ | 模块实例 ID，格式: `m_{序号}` |
| `moduleId` | string | ✅ | 模块库中的类型 ID (如 `bed_king`) |
| `moduleName` | string | ✅ | 模块可读名称 |
| `bounds` | number[][] | ✅ | 边界多边形 (矩形 4 顶点) |
| `facing` | string | ✅ | 朝向 (见下方说明) |
| `zoneId` | string | ✅ | 所属分区 ID (如 `rz_3`)，与所在目录名一致 |
| `items` | array | ✅ | 模块内部家具清单 (可为空数组) |

#### 7.2.2 bounds - 边界定义

`bounds` 是矩形的 4 个顶点，按顺序排列:

```
      [0]─────────[1]
       │           │
       │  模块区域  │
       │           │
      [3]─────────[2]

bounds = [[x0,y0], [x1,y1], [x2,y2], [x3,y3]]
```

**示例**: 一个 2000×2000mm 的床，左下角在 (9100, 1750):
```json
"bounds": [[9100, 1750], [11100, 1750], [11100, 3750], [9100, 3750]]
```

#### 7.2.3 facing - 朝向定义

使用语义化方向字符串：

| 语义 | 向量 | 说明 |
|------|------|------|
| `north` | [0, 1] | 朝北（Y 轴正向） |
| `south` | [0, -1] | 朝南 |
| `east` | [1, 0] | 朝东（X 轴正向） |
| `west` | [-1, 0] | 朝西 |
| `northeast` | [0.707, 0.707] | 东北 |
| `southeast` | [0.707, -0.707] | 东南 |
| `southwest` | [-0.707, -0.707] | 西南 |
| `northwest` | [-0.707, 0.707] | 西北 |

#### 7.2.4 items - 家具清单

用于回写 Revit 时创建具体家具实例。当前可为空数组：

```json
{
  "items": [
    { "familyId": "bed_double_01", "offset": [0, 0], "role": "主体" }
  ]
}
```

### 7.3 zones.json - 方案特定区域

方案特定的功能区划分（可为空数组）：
```json
[]
```

### 7.4 finishes.json - 完成面配置

墙面完成面配置（可为空数组）：
```json
[]
```

---

## 8. context/ - 设计上下文

### 8.1 requirements.md - 设计需求

用户可编辑的设计需求文档：

```markdown
# 设计需求

## 项目概述
（在此描述项目的基本情况）

## 功能需求
-

## 风格偏好
-

## 特殊要求
-
```

---

## 9. 布置约束规则

在添加或移动模块时，必须遵守以下约束:

### 9.1 核心约束

```
对于每个要放置的模块:
1. ✅ bounds 必须完全在 zones[].rawBoundary 内
2. ❌ bounds 不能与任何 exclusions[].rawBoundary 重叠
3. ❌ bounds 不能与其他已放置 modules[] 重叠
```

### 9.2 zoneId 与目录对应

`zoneId` 使用分区 ID（`rz_*` 格式），必须与 `modules.json` 所在目录名一致：

| 分区 ID | 目录位置 | 对应房间 |
|---------|----------|----------|
| `rz_1` | `schemes/rz_1/modules.json` | 次卧一 |
| `rz_2` | `schemes/rz_2/modules.json` | 次卧二 |
| `rz_3` | `schemes/rz_3/modules.json` | 主卧 |
| `rz_4` | `schemes/rz_4/modules.json` | 主卫 |
| `rz_5` | `schemes/rz_5/modules.json` | 公卫 |
| `rz_6` | `schemes/rz_6/modules.json` | 公共空间 |

---

## 10. 如何添加布置模块

### 10.1 空白 modules.json 模板

每个分区目录下的 `modules.json` 初始为空数组：

```json
[]
```

### 10.2 添加单个家具示例

在主卧 (rz_3) 添加一个床，编辑 `schemes/rz_3/modules.json`：

```json
[
  {
    "id": "m_1",
    "moduleId": "bed_king",
    "moduleName": "King Bed",
    "bounds": [[9100, 1750], [11100, 1750], [11100, 3750], [9100, 3750]],
    "facing": "east",
    "zoneId": "rz_3",
    "items": []
  }
]
```

### 10.3 添加多个家具示例

在主卧添加床 + 床头柜组合，编辑 `schemes/rz_3/modules.json`：

```json
[
  {
    "id": "m_1",
    "moduleId": "bed_king",
    "moduleName": "King Bed",
    "bounds": [[9100, 1750], [11100, 1750], [11100, 3750], [9100, 3750]],
    "facing": "east",
    "zoneId": "rz_3",
    "items": []
  },
  {
    "id": "m_2",
    "moduleId": "nightstand",
    "moduleName": "Nightstand Left",
    "bounds": [[9100, 3750], [9600, 3750], [9600, 4250], [9100, 4250]],
    "facing": "east",
    "zoneId": "rz_3",
    "items": []
  },
  {
    "id": "m_3",
    "moduleId": "nightstand",
    "moduleName": "Nightstand Right",
    "bounds": [[9100, 1250], [9600, 1250], [9600, 1750], [9100, 1750]],
    "facing": "east",
    "zoneId": "rz_3",
    "items": []
  }
]
```

### 10.4 常用模块类型

| moduleId | 名称 | 适用房间 |
|----------|------|----------|
| `bed_king` | King Bed | 主卧 |
| `bed_queen` | Queen Bed | 次卧 |
| `nightstand` | Nightstand | 卧室 |
| `wardrobe` | Wardrobe | 卧室 |
| `sofa_main` | Sofa Main | 客厅 |
| `sofa_chaise` | Sofa Chaise | 客厅 |
| `tv_unit_full_wall` | TV Unit | 客厅 |
| `shower_corner` | Corner Shower | 卫生间 |
| `toilet` | Toilet | 卫生间 |
| `vanity_sink` | Vanity Sink | 卫生间 |
| `vanity_sink_double` | Double Sink Vanity | 主卫 |

---

## 11. 常见问题

### Q1: 如何确定模块应该放在哪个分区?

查看 `schemes/zones.json`，找到对应房间的分区 ID（`rz_*`），在该分区的 `modules.json` 中添加模块。

### Q2: 如何避免与门扇区域冲突?

查看 `computed/exclusions.json`，确保模块的 `bounds` 不与任何禁区的 `rawBoundary` 重叠。

### Q3: bounds 的顶点顺序是什么?

顶点按矩形的顺序排列：左下 → 右下 → 右上 → 左上（或类似的连续顺序）。

### Q4: items 数组可以为空吗?

是的，`items` 可以是空数组 `[]`，后续由 Server 或回写流程填充。

### Q5: 如何创建新方案?

多策略通过 Git 分支实现：

1. 使用 `git checkout -b scheme/新方案名` 创建新分支
2. 修改 `schemes/strategy.json` 中的 `id` 和 `name`
3. 在该分支上修改各 `schemes/rz_*/modules.json` 进行布置
4. 切换分支即可切换方案

> **注意**：AI 并行生成多方案时，Server 会自动使用 Git Worktree 创建隔离的工作副本。

---

*本文档由 BIMCanvas Server 自动生成。如需更新，请删除本文件后重新打开项目。*
