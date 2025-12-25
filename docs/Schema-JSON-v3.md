# BIMCanvas JSON Schema 规范 v3.0

> 版本：v3.0
> 更新日期：2025-12-25
> 状态：设计定稿（Multi-Repo Collection 架构）
>
> **相关文档**：
> - [Schema-JSON.md](./Schema-JSON.md) - v2.9 单一 JSON 结构（兼容期保留）
> - [Architecture.md](./Architecture.md) - 系统架构
> - [ProjectStructure_Demo/](../ProjectStructure_Demo/) - 完整示例项目
>
> **v3.0 变更要点**：
> - 从单一 JSON 文件升级为多文件夹结构（Multi-Repo Collection）
> - 策略（Strategy）作为独立 Git 仓库，支持并行开发
> - 变体（Variant）通过 Git 分支管理，支持线性回溯
> - 新增 dirty 机制：`lastValidatedBaselineHash` + `status`
> - 新增 origin 字段追踪衍生策略
> - finishes 使用绝对 mm 值的 range 表示

---

## 1. 设计原则

### 1.1 核心设计约束

> **AI = OBB 规划师**：AI 只操作矩形包围盒 (OBB)，不计算精确几何。

| 原则 | 说明 |
|------|------|
| **AI 决策位置** | AI 输出 `center + size + facing`，Core 负责转换为精确几何 |
| **Polygon2D 是真理** | JSON 存储精确几何，AABB 仅作运行时优化 |
| **多样化交互** | AI 可用 Semantic / Vec2D / Polygon2D 任意格式输出 |

### 1.2 Multi-Repo Collection 架构

> **核心概念**：用物理隔离表达"可并行"，用逻辑隔离表达"可回溯"

| 层级 | 物理载体 | 开发模式 | Git 角色 |
|------|----------|----------|----------|
| **策略 (Strategy)** | 独立文件夹 | 并行开发 | 独立仓库 |
| **变体 (Variant)** | Git 分支 | 线性回溯 | 分支 |

**设计原理**：
- 不同策略开发是**经常并行的**，互不影响开发进度 → 用独立文件夹隔离
- 不同变体的开发是**线性的**，变体产生的原因是重大选择之前的存档 → 用 Git 分支表达

### 1.3 数据分层架构

```
┌─────────────────────────────────────────────────────────────────┐
│                     项目文件夹结构 (v3.0)                         │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│   【项目入口】                                                   │
│   ┌─────────────────────────────────────────────────────────┐   │
│   │  project.json: 项目元数据 + 策略列表 + 激活策略            │   │
│   └─────────────────────────────────────────────────────────┘   │
│                                                                 │
│   【baseline/: 基准层】只读，Revit 导出后锁定                    │
│   ┌─────────────────────────────────────────────────────────┐   │
│   │  • metadata.json: 坐标转换参数                           │   │
│   │  • architecture.json: 墙、柱（物理构造）                  │   │
│   │  • openings.json: 门窗                                   │   │
│   │  • rooms.json: 房间边界                                  │   │
│   │  • location_lines.json: 完成面定位线                     │   │
│   └─────────────────────────────────────────────────────────┘   │
│                                                                 │
│   【context/: 上下文层】设计知识                                 │
│   ┌─────────────────────────────────────────────────────────┐   │
│   │  • requirements.md: 用户需求                             │   │
│   │  • standards.md: 设计规范（可选）                         │   │
│   │  • analysis.md: AI 项目分析（可选）                       │   │
│   └─────────────────────────────────────────────────────────┘   │
│                                                                 │
│   【schemes/: 策略集合】每个策略是独立 Git 仓库                   │
│   ┌─────────────────────────────────────────────────────────┐   │
│   │  schemes/{s}/                                            │   │
│   │  ├── strategy.json: 策略元数据（含 origin, status）      │   │
│   │  ├── zones.json: 分区数据                                │   │
│   │  ├── finishes.json: 完成面配置                           │   │
│   │  └── modules.json: 家具布置                              │   │
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

### 1.5 几何图元 (Geometry Primitives)

| 类型 | 格式 | 示例 | 说明 |
|------|------|------|------|
| **Point2D** | `[x, y]` | `[3000.5, 2500.0]` | 双精度坐标点（**绝对位置**） |
| **Vec2D** | `[dx, dy]` | `[-600.0, 0.0]` | 相对偏移向量 |
| **Line2D** | `[[x1,y1], [x2,y2]]` | `[[2000,0], [2900,0]]` | 线段（起终点） |
| **Polygon2D** | `[[x,y], ...]` | `[[0,0], [6000,0], ...]` | 多边形（隐式闭合） |

---

## 2. 完整文件夹结构

```
{项目名称}/                              # 项目根目录（非 Git 仓库）
│
├── project.json                         # 项目入口
├── .gitignore                           # 忽略 Assets/, derived/
│
├── baseline/                            # 【基准层】只读
│   ├── metadata.json                    # 坐标转换参数
│   ├── architecture.json                # 墙、柱
│   ├── openings.json                    # 门窗
│   ├── rooms.json                       # 房间边界
│   └── location_lines.json              # 完成面定位线
│
├── context/                             # 【上下文层】设计知识
│   └── requirements.md                  # 用户需求
│
├── schemes/                             # 【策略集合】
│   ├── s1_Flow/                         # 策略1（独立 Git 仓库）
│   │   ├── .git/                        # Git 仓库
│   │   ├── .gitignore
│   │   ├── strategy.json                # 策略元数据
│   │   ├── zones.json                   # 分区数据
│   │   ├── finishes.json                # 完成面配置
│   │   └── modules.json                 # 家具布置
│   │
│   └── s2_Derived/                      # 策略2（衍生策略）
│       └── ...
│
├── Assets/                              # 【资产层】截图等（不进 Git）
│   └── .gitkeep
│
└── derived/                             # 【缓存层】可重算（不进 Git）
```

---

## 3. 项目入口 (project.json)

```json
{
  "id": "proj_demo_001",
  "name": "理想户型A - 三室两厅",
  "version": "3.0",
  "createdAt": "2025-12-24T10:00:00Z",
  "updatedAt": "2025-12-24T21:00:00Z",
  "coordinateSystem": "cartesian_mm_yUp",
  "activeSchemeId": "s1_Flow",
  "schemes": [
    { "id": "s1_Flow", "path": "./schemes/s1_Flow", "name": "动线优先" },
    { "id": "s2_Derived", "path": "./schemes/s2_Derived", "name": "衍生策略示例" }
  ]
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `id` | string | 是 | 项目唯一标识 |
| `name` | string | 是 | 项目名称 |
| `version` | string | 是 | Schema 版本，固定 `"3.0"` |
| `createdAt` | string | 是 | 创建时间（ISO 8601） |
| `updatedAt` | string | 是 | 最后更新时间 |
| `coordinateSystem` | string | 是 | 固定值：`cartesian_mm_yUp` |
| `activeSchemeId` | string | 否 | 当前激活策略 ID |
| `schemes` | array | 是 | 策略列表 |

---

## 4. 基准层 (baseline/)

### 4.1 metadata.json（坐标转换）

```json
{
  "exportDate": "2025-12-24T09:00:00Z",
  "revitVersion": "2024",
  "placementElevation": 3000,
  "origin": [0, 0, 0],
  "rotation": 0,
  "transformMethod": "projectBasePoint",
  "unitSystem": "metric_mm"
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `exportDate` | string | 导出时间 |
| `revitVersion` | string | Revit 版本 |
| `placementElevation` | number | 布置高度（mm） |
| `origin` | number[] | 坐标原点 [x, y, z] |
| `rotation` | number | 视图旋转角度（弧度） |
| `transformMethod` | string | 原点计算方法 |
| `unitSystem` | string | 单位系统 |

### 4.2 architecture.json（建筑构造）

```json
{
  "walls": [
    {
      "id": "w1",
      "elementId": 100001,
      "thickness": 200,
      "isStructural": false,
      "polygon": [[0, 0], [5000, 0], [5000, 200], [0, 200]]
    }
  ],
  "columns": [
    {
      "id": "c1",
      "elementId": 100010,
      "isStructural": true,
      "polygon": [[2400, 1900], [2600, 1900], [2600, 2100], [2400, 2100]]
    }
  ]
}
```

#### Wall 字段

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | string | 墙体 ID |
| `elementId` | number | Revit 元素 ID |
| `thickness` | number | 墙厚（mm） |
| `isStructural` | boolean | 是否结构墙 |
| `polygon` | Polygon2D | 墙体轮廓 |

#### Column 字段

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | string | 柱子 ID |
| `elementId` | number | Revit 元素 ID |
| `isStructural` | boolean | 是否结构柱 |
| `polygon` | Polygon2D | 柱子轮廓 |

### 4.3 openings.json（门窗）

```json
{
  "openings": [
    {
      "id": "o1",
      "elementId": 200001,
      "type": "door",
      "wallId": "w1",
      "line": [[1000, 0], [1900, 0]],
      "width": 900,
      "height": 2100,
      "facingDirection": [0, 1],
      "handDirection": [1, 0],
      "openingType": "single_swing"
    },
    {
      "id": "o2",
      "elementId": 200002,
      "type": "window",
      "wallId": "w3",
      "line": [[1000, 3800], [3000, 3800]],
      "width": 2000,
      "height": 1500,
      "sillHeight": 900,
      "facingDirection": [0, -1]
    }
  ]
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | string | 开口 ID |
| `elementId` | number | Revit 元素 ID |
| `type` | string | 类型：`door` / `window` |
| `wallId` | string | 所属墙体 ID |
| `line` | Line2D | 定位线段 |
| `width` | number | 宽度（mm） |
| `height` | number | 高度（mm） |
| `facingDirection` | Vec2D | 朝向（室内方向） |
| `handDirection` | Vec2D | 把手方向（仅门） |
| `openingType` | string | 开启方式（仅门） |
| `sillHeight` | number | 窗台高度（仅窗） |

### 4.4 rooms.json（房间）

```json
{
  "rooms": [
    {
      "id": "room1",
      "elementId": 300001,
      "name": "主卧",
      "type": "master_bedroom",
      "area": 20000000,
      "boundary": [[200, 200], [4800, 200], [4800, 3800], [200, 3800]]
    }
  ]
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | string | 房间 ID |
| `elementId` | number | Revit 元素 ID |
| `name` | string | 房间名称 |
| `type` | RoomType | 房间类型枚举 |
| `area` | number | 面积（mm²） |
| `boundary` | Polygon2D | 房间边界 |

#### RoomType 枚举

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

### 4.5 location_lines.json（完成面定位线）

```json
{
  "lines": [
    {
      "id": "ll1",
      "wallId": "w1",
      "roomId": "room1",
      "side": "interior",
      "line": [[200, 200], [4800, 200]],
      "length": 4600
    }
  ]
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | string | 定位线 ID |
| `wallId` | string | 所属墙体 ID |
| `roomId` | string | 所属房间 ID |
| `side` | string | 哪一侧：`interior` / `exterior` |
| `line` | Line2D | 定位线坐标 |
| `length` | number | 线段长度（mm），冗余存储便于计算 |

---

## 5. 策略层 (schemes/{s}/)

### 5.1 strategy.json（策略元数据）

```json
{
  "id": "s1_Flow",
  "name": "动线优先策略",
  "approach": "circulation_first",
  "description": "从入口动线出发，优化各功能区到达路径",
  "createdAt": "2025-12-24T10:00:00Z",
  "updatedAt": "2025-12-24T21:00:00Z",
  "origin": null,
  "lastValidatedBaselineHash": "sha256:a1b2c3d4e5f6789012345678901234567890abcd",
  "status": "valid"
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `id` | string | 是 | 策略 ID |
| `name` | string | 是 | 策略名称 |
| `approach` | string | 是 | 设计方法（见枚举） |
| `description` | string | 否 | 策略描述 |
| `createdAt` | string | 是 | 创建时间 |
| `updatedAt` | string | 是 | 更新时间 |
| `origin` | object | 否 | 衍生来源（原创策略为 null） |
| `lastValidatedBaselineHash` | string | 是 | 底图哈希值 |
| `status` | string | 是 | 状态：`valid` / `dirty` / `invalid` |

#### approach 枚举

| 值 | 说明 |
|-----|------|
| `circulation_first` | 动线优先 |
| `furniture_first` | 主家具优先 |
| `space_efficiency` | 空间利用率优先 |
| `style_variation` | 风格变体 |
| `custom` | 自定义 |

#### origin 字段（衍生策略）

```json
{
  "origin": {
    "sourceStrategyId": "s1_Flow",
    "sourceRepo": "./schemes/s1_Flow",
    "sourceBranch": "main",
    "sourceCommit": "abc123def456",
    "derivedAt": "2025-12-24T14:00:00Z",
    "derivationReason": "用户喜欢动线分区，但想换成北欧风格"
  }
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `sourceStrategyId` | string | 来源策略 ID |
| `sourceRepo` | string | 来源仓库路径 |
| `sourceBranch` | string | 来源分支 |
| `sourceCommit` | string | 来源提交哈希 |
| `derivedAt` | string | 衍生时间 |
| `derivationReason` | string | 衍生原因 |

#### Dirty 机制

| status | 允许操作 | 禁止操作 | 触发动作 |
|--------|----------|----------|----------|
| `valid` | 全部 | - | - |
| `dirty` | 编辑、保存 | 导出到 Revit | 提示"底图已变更" |
| `invalid` | 查看 | 编辑、导出 | 强制进入修复模式 |

### 5.2 zones.json（分区数据）

```json
{
  "zones": [
    {
      "id": "z1",
      "name": "睡眠区",
      "type": "designable",
      "roomId": "room1",
      "tags": ["sleep", "bedhead_wall"],
      "rawBoundary": [[200, 200], [3000, 200], [3000, 2500], [200, 2500]],
      "computedBoundary": [[250, 250], [2950, 250], [2950, 2450], [250, 2450]],
      "exclusionAreas": [],
      "openings": [],
      "reason": "根据床头靠北墙原则划分"
    }
  ]
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `id` | string | 是 | 区域 ID |
| `name` | string | 是 | 区域名称 |
| `type` | string | 是 | 类型：`exclusion` / `circulation` / `designable` |
| `roomId` | string | 是 | 所属房间 ID |
| `tags` | string[] | 是 | 功能标签列表（ZoneTag 枚举） |
| `rawBoundary` | Polygon2D | 是 | 原始边界 |
| `computedBoundary` | Polygon2D | 否 | 计算后边界（扣除禁区） |
| `exclusionAreas` | array | 是 | 禁区列表 |
| `openings` | string[] | 是 | 关联开口 ID 列表 |
| `reason` | string | 是 | 划分原因（给 AI 看） |

#### ZoneTag 枚举

| 标签 | 说明 |
|------|------|
| `sleep` | 睡眠区 |
| `bedhead_wall` | 床头背景墙 |
| `wardrobe` | 衣柜区 |
| `storage` | 收纳区 |
| `tv_media` | 电视多媒体区 |
| `rest` | 休憩区 |
| `passage` | 通道区 |
| `main_circulation` | 主动线 |

### 5.3 finishes.json（完成面配置）

```json
{
  "segments": [
    {
      "id": "fs1",
      "sourceLineId": "ll1",
      "range": [0, 4600],
      "finishModuleId": "latex_paint_white",
      "thickness": 0,
      "source": "room_default",
      "reason": "北墙基础乳胶漆"
    },
    {
      "id": "fs2",
      "sourceLineId": "ll1",
      "range": [500, 2500],
      "finishModuleId": "bedhead_panel_modern_001",
      "thickness": 30,
      "source": "zone_override",
      "zoneId": "z1",
      "reason": "床头背景墙护墙板"
    }
  ]
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `id` | string | 是 | 完成面段 ID |
| `sourceLineId` | string | 是 | 引用的定位线 ID |
| `range` | [number, number] | 是 | 范围 [起点偏移, 终点偏移]，**绝对 mm 值** |
| `finishModuleId` | string | 是 | 完成面模块 ID |
| `thickness` | number | 是 | 厚度（mm） |
| `source` | string | 是 | 来源：`room_default` / `zone_override` / `user_override` |
| `zoneId` | string | 否 | 触发区域 ID（仅 zone_override） |
| `reason` | string | 否 | 配置原因 |

#### range 表示法

**使用绝对 mm 值**，如 `[500, 2500]` 表示从定位线起点偏移 500mm 到 2500mm 的区间。

**设计理由**：
- baseline 不可变，定位线长度不会变
- AI 计算更直观（"从墙角偏移 500mm 开始"）
- 调试时一眼能和图纸对照

### 5.4 modules.json（家具布置）

```json
{
  "modules": [
    {
      "id": "m1",
      "moduleId": "bed_modern_1800",
      "moduleName": "现代双人床 1.8m",
      "zoneId": "z1",
      "bounds": [[600, 400], [2400, 400], [2400, 2400], [600, 2400]],
      "facing": "north",
      "items": [
        {
          "familyId": "fam_bed_001",
          "familyName": "现代双人床",
          "offset": [0, 0],
          "rotation": 0,
          "role": "主体"
        },
        {
          "familyId": "fam_nightstand_001",
          "familyName": "床头柜",
          "offset": [-550, 200],
          "rotation": 0,
          "role": "左床头柜"
        }
      ],
      "placementReason": "床头靠北墙居中，与背景墙对齐"
    }
  ]
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `id` | string | 是 | 模块实例 ID |
| `moduleId` | string | 是 | 模块库类型 ID |
| `moduleName` | string | 否 | 可读名称 |
| `zoneId` | string | 是 | 所属区域 ID |
| `bounds` | Polygon2D | 是 | 边界多边形（4 顶点矩形） |
| `facing` | Facing | 是 | 朝向（语义或向量） |
| `items` | array | 否 | 内部家具清单 |
| `placementReason` | string | 否 | 布置原因 |

#### Facing 类型

支持两种格式：

**语义字符串**（标准场景）：

| 语义 | 对应向量 | 说明 |
|------|----------|------|
| `north` | `[0, 1]` | Y 轴正向 |
| `south` | `[0, -1]` | Y 轴负向 |
| `east` | `[1, 0]` | X 轴正向 |
| `west` | `[-1, 0]` | X 轴负向 |
| `northeast` | `[0.707, 0.707]` | 东北 |
| `northwest` | `[-0.707, 0.707]` | 西北 |
| `southeast` | `[0.707, -0.707]` | 东南 |
| `southwest` | `[-0.707, -0.707]` | 西南 |

**Vec2D 向量**（任意角度）：

```json
{ "facing": [0.866, 0.5] }  // 30° 方向
```

#### ModuleItem 字段

| 字段 | 类型 | 说明 |
|------|------|------|
| `familyId` | string | 族库 ID |
| `familyName` | string | 族名称 |
| `offset` | Vec2D | 相对模块中心偏移 |
| `rotation` | number | 相对旋转角度（度） |
| `role` | string | 角色（主体/左床头柜等） |

---

## 6. 典型工作流

### 6.1 新建策略

```bash
mkdir schemes/s3_Space
cd schemes/s3_Space
git init
# 创建 strategy.json, zones.json, finishes.json, modules.json
git add . && git commit -m "初始化策略"
```

### 6.2 创建变体（存档）

```bash
cd schemes/s1_Flow
git branch v1_backup    # 在重大修改前存档
```

### 6.3 切换变体（回溯）

```bash
cd schemes/s1_Flow
git checkout v1_backup  # 回到之前的版本
```

### 6.4 变体升级为策略

```bash
cp -r schemes/s1_Flow schemes/s3_FromVariant
cd schemes/s3_FromVariant
rm -rf .git && git init
# 更新 strategy.json 的 origin 字段
```

---

## 7. 与 v2.9 的映射关系

| v2.9 DesignDocument 路径 | v3.0 文件位置 | 说明 |
|-------------------------|--------------|------|
| `id, projectName, version` | `project.json` | 项目元数据 |
| `exportDate` | `baseline/metadata.json` | 导出时间 |
| `revit.metadata` | `baseline/metadata.json` | 坐标转换 |
| `revit.walls, columns` | `baseline/architecture.json` | 建筑构造 |
| `revit.openings` | `baseline/openings.json` | 门窗 |
| `revit.rooms` | `baseline/rooms.json` | 房间边界 |
| `revit.finishLocationBoundaries` | `baseline/location_lines.json` | 完成面定位线 |
| `computed.zones` | `schemes/{s}/zones.json` | 策略级分区 |
| `computed.wallFinishes` | `schemes/{s}/finishes.json` | 完成面配置 |
| `layout.modules` | `schemes/{s}/modules.json` | 家具布置 |
| `layout.schemes` | `schemes/` 目录结构 | 策略集合 |
| *(新增)* | `context/*.md` | 设计知识 |
| *(新增)* | `strategy.json.origin` | 衍生追踪 |
| *(新增)* | `strategy.json.status` | dirty 机制 |

---

## 8. TypeScript 类型定义

```typescript
// ============================================
// 基础几何类型
// ============================================
type Point2D = [number, number];
type Vec2D = [number, number];
type Line2D = [Point2D, Point2D];
type Polygon2D = Point2D[];

// ============================================
// 项目入口
// ============================================
interface Project {
  id: string;
  name: string;
  version: "3.0";
  createdAt: string;
  updatedAt: string;
  coordinateSystem: "cartesian_mm_yUp";
  activeSchemeId?: string;
  schemes: SchemeRef[];
}

interface SchemeRef {
  id: string;
  path: string;
  name: string;
}

// ============================================
// 基准层
// ============================================
interface Metadata {
  exportDate: string;
  revitVersion: string;
  placementElevation: number;
  origin: [number, number, number];
  rotation: number;
  transformMethod: string;
  unitSystem: string;
}

interface Wall {
  id: string;
  elementId: number;
  thickness: number;
  isStructural: boolean;
  polygon: Polygon2D;
}

interface Column {
  id: string;
  elementId: number;
  isStructural: boolean;
  polygon: Polygon2D;
}

interface Opening {
  id: string;
  elementId: number;
  type: "door" | "window";
  wallId: string;
  line: Line2D;
  width: number;
  height: number;
  facingDirection: Vec2D;
  handDirection?: Vec2D;
  openingType?: string;
  sillHeight?: number;
}

interface Room {
  id: string;
  elementId: number;
  name: string;
  type: RoomType;
  area: number;
  boundary: Polygon2D;
}

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

interface LocationLine {
  id: string;
  wallId: string;
  roomId: string;
  side: "interior" | "exterior";
  line: Line2D;
  length: number;
}

// ============================================
// 策略层
// ============================================
interface Strategy {
  id: string;
  name: string;
  approach: StrategyApproach;
  description?: string;
  createdAt: string;
  updatedAt: string;
  origin: StrategyOrigin | null;
  lastValidatedBaselineHash: string;
  status: StrategyStatus;
}

type StrategyApproach =
  | "circulation_first"
  | "furniture_first"
  | "space_efficiency"
  | "style_variation"
  | "custom";

type StrategyStatus = "valid" | "dirty" | "invalid";

interface StrategyOrigin {
  sourceStrategyId: string;
  sourceRepo: string;
  sourceBranch: string;
  sourceCommit: string;
  derivedAt: string;
  derivationReason: string;
}

interface Zone {
  id: string;
  name: string;
  type: ZoneType;
  roomId: string;
  tags: ZoneTag[];
  rawBoundary: Polygon2D;
  computedBoundary?: Polygon2D;
  exclusionAreas: ExclusionArea[];
  openings: string[];
  reason: string;
}

type ZoneType = "exclusion" | "circulation" | "designable";

type ZoneTag =
  | "sleep"
  | "bedhead_wall"
  | "wardrobe"
  | "storage"
  | "tv_media"
  | "rest"
  | "passage"
  | "main_circulation";

interface ExclusionArea {
  id: string;
  type: string;
  polygon: Polygon2D;
  reason: string;
}

interface FinishSegment {
  id: string;
  sourceLineId: string;
  range: [number, number];  // 绝对 mm 值
  finishModuleId: string;
  thickness: number;
  source: FinishSource;
  zoneId?: string;
  reason?: string;
}

type FinishSource = "room_default" | "zone_override" | "user_override";

interface Module {
  id: string;
  moduleId: string;
  moduleName?: string;
  zoneId: string;
  bounds: Polygon2D;
  facing: Facing;
  items?: ModuleItem[];
  placementReason?: string;
}

type FacingSemantic =
  | "north"
  | "south"
  | "east"
  | "west"
  | "northeast"
  | "northwest"
  | "southeast"
  | "southwest";

type Facing = FacingSemantic | Vec2D;

interface ModuleItem {
  familyId: string;
  familyName?: string;
  offset: Vec2D;
  rotation: number;
  role?: string;
}
```

---

## 版本历史

| 版本 | 日期 | 变更内容 |
|------|------|----------|
| v3.0 | 2025-12-25 | **Multi-Repo Collection 架构**：从单一 JSON 升级为多文件夹结构；策略作为独立 Git 仓库；变体通过 Git 分支管理；新增 dirty 机制（lastValidatedBaselineHash + status）；新增 origin 字段追踪衍生策略；finishes 使用绝对 mm 值 range；新增 location_lines.json |
| v2.9 | 2025-12-22 | 单一 JSON 结构（见 Schema-JSON.md） |
