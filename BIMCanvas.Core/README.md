# BIMCanvas.Core

> BIMCanvas 系统的核心层，提供数据模型、空间算法和 JSON 序列化能力。

**运行时**：.NET Standard 2.0（跨框架兼容）
**数据模型版本**：v3.0

---

## 目录

- [项目结构](#项目结构)
- [v3.0 架构变更](#v30-架构变更)
- [核心概念](#核心概念)
- [数据模型](#数据模型)
- [算法层](#算法层)
- [JSON 序列化](#json-序列化)
- [API 参考](#api-参考)
- [使用示例](#使用示例)
- [设计决策](#设计决策)

---

## 项目结构

```
BIMCanvas.Core/
├── Models/
│   ├── Geometry/                基础几何类型
│   │   ├── Point2D.cs              坐标点
│   │   ├── Vec2D.cs                向量
│   │   ├── Polygon2D.cs            多边形
│   │   ├── AABB.cs                 轴对齐包围盒
│   │   └── Line2D.cs               线段
│   │
│   ├── Project/                 【v3.0 新增】项目结构类
│   │   ├── Project.cs              项目入口（对应 project.json）
│   │   ├── SchemeRef.cs            策略轻量引用
│   │   ├── Strategy.cs             策略元数据（对应 strategy.json）
│   │   ├── StrategyOrigin.cs       衍生来源追踪
│   │   └── BaselineManifest.cs     Baseline 元数据
│   │
│   ├── Revit/                   Baseline 层数据（只读，Revit 导出）
│   │   ├── Architecture.cs         建筑构造容器（walls + columns）
│   │   ├── Wall.cs                 墙体轮廓
│   │   ├── Column.cs               柱子轮廓
│   │   ├── Opening.cs              门窗数据
│   │   ├── Room.cs                 房间边界
│   │   └── LocationLine.cs         【v3.0 新增】完成面定位线
│   │
│   ├── Computed/                计算派生数据（Server 生成）
│   │   ├── Zone.cs                 设计区域
│   │   ├── FinishSegment.cs        【v3.0 重构】完成面段（range 表示法）
│   │   ├── ExclusionArea.cs        【v3.0 新增】禁区模型
│   │   └── FinishRequirement.cs    完成面需求
│   │
│   ├── Layout/                  方案数据（AI 生成）
│   │   ├── Module.cs               布置模块
│   │   └── ModuleItem.cs           模块内家具
│   │
│   ├── Semantic/                语义类型
│   │   ├── Facing.cs               朝向（联合类型）
│   │   └── FacingDirection.cs      朝向枚举
│   │
│   └── Shared/                  共享枚举
│       ├── RoomType.cs             房间类型
│       ├── ZoneTag.cs              区域标签
│       ├── ZoneType.cs             区域类型
│       ├── FinishType.cs           完成面类型
│       ├── FinishSource.cs         完成面来源
│       ├── StrategyStatus.cs       【v3.0 新增】策略状态枚举
│       └── StrategyApproach.cs     【v3.0 新增】设计方法枚举
│
├── Algorithms/
│   ├── Geometries/              几何算法
│   │   ├── GeometryHelper.cs       几何运算工具
│   │   └── NtsAdapter.cs           NTS 适配器
│   │
│   └── Spatial/                 空间算法
│       ├── SchemeValidator.cs      方案级全量验证（布局编译器）
│       ├── PlacementValidator.cs   单模块布置验证
│       ├── CollisionDetector.cs    碰撞检测
│       ├── FacingHelper.cs         朝向转换
│       ├── GeometryNormalizer.cs   几何规范化
│       └── FinishRules.cs          完成面规则
│
├── Converters/
│   ├── Json/                    JSON 转换器
│   │   ├── StrategyStatusConverter.cs    【v3.0 新增】snake_case 序列化
│   │   ├── StrategyApproachConverter.cs  【v3.0 新增】snake_case 序列化
│   │   └── FinishSourceConverter.cs      【v3.0 新增】snake_case 序列化
│   └── UnitConverter.cs         单位转换
│
├── Services/
│   └── BaselineHashService.cs   【v3.0 新增】Baseline 哈希计算
│
└── Validation/
    ├── Result.cs                验证结果类型（单模块级）
    ├── Diagnostic.cs            诊断项 + 错误代码常量（方案级）
    └── SchemeValidationReport.cs 方案验证报告
```

---

## v3.0 架构变更

### 从 DesignDocument 到多文件结构

v3.0 将原有的单一 `DesignDocument.json` 拆分为多文件夹结构：

| v2.9 (旧) | v3.0 (新) | 说明 |
|-----------|-----------|------|
| `DesignDocument` | 已删除 | 使用多文件结构替代 |
| `RevitData` | 已删除 | 拆分为独立 JSON 文件 |
| `Metadata` | `BaselineManifest` | 移至 `Models/Project/` |
| `WallFinish` | `FinishSegment` | 使用 range 表示法 |
| `FinishLocationBoundary` | `LocationLine` | 分离为独立实体 |

### 三层汉堡模型

v3.0 采用"文件驱动架构"，数据分为三个物理层级：

| 层级 | 文件夹路径 | 内容 | 读写属性 |
|------|-----------|------|----------|
| **底层 (Baseline)** | `baseline/` | 墙、柱、门窗、房间 | **只读** (Revit 导出) |
| **中层 (Schemes)** | `schemes/{s}/` | 功能分区、完成面、家具模块 | **混合** (AI/Server) |
| **顶层 (Computed)** | `computed/` | 禁区、缓存数据 | **自动生成** (Server) |

### 删除的类

以下类在 v3.0 中已被删除：

- `Models/Document/DesignDocument.cs`
- `Models/Revit/RevitData.cs`
- `Models/Revit/Metadata.cs`
- `Models/Revit/FinishLocationBoundary.cs`
- `Models/Computed/ComputedData.cs`
- `Models/Computed/WallFinish.cs`
- `Models/Layout/LayoutData.cs`
- `Models/Layout/Scheme.cs`

### 新增的类

| 类 | 文件 | 说明 |
|----|------|------|
| `Project` | `Models/Project/Project.cs` | 项目入口，对应 project.json |
| `SchemeRef` | `Models/Project/SchemeRef.cs` | 策略轻量引用 |
| `Strategy` | `Models/Project/Strategy.cs` | 策略元数据，对应 strategy.json |
| `StrategyOrigin` | `Models/Project/StrategyOrigin.cs` | 衍生来源追踪 |
| `BaselineManifest` | `Models/Project/BaselineManifest.cs` | Baseline 元数据 |
| `Architecture` | `Models/Revit/Architecture.cs` | 建筑构造容器 |
| `LocationLine` | `Models/Revit/LocationLine.cs` | 完成面定位线 |
| `ExclusionArea` | `Models/Computed/ExclusionArea.cs` | 禁区模型 |
| `FinishSegment` | `Models/Computed/FinishSegment.cs` | 完成面段 |
| `StrategyStatus` | `Models/Shared/StrategyStatus.cs` | 策略状态枚举 |
| `StrategyApproach` | `Models/Shared/StrategyApproach.cs` | 设计方法枚举 |
| `BaselineHashService` | `Services/BaselineHashService.cs` | 哈希计算服务 |

---

## 核心概念

### 设计原则

**AI = OBB 规划师**：AI 只操作矩形包围盒（Oriented Bounding Box），不计算精确几何。Core 层负责转换和验证。

### 坐标系统

- **坐标系**：CAD 标准（原点左下角，Y 轴向上）
- **单位**：毫米 (mm)
- **标识符**：`coordinateSystem: "cartesian_mm_yUp"`

### 命名空间边界

```
BIMCanvas.Core.*     → 所有 .NET 项目可引用
BIMCanvas.Revit.*    → 仅 Revit 插件内部使用
```

**禁止**：Core 层不引入 Revit API，确保跨框架兼容性。

---

## 数据模型

### 基础几何类型

| 类型 | 用途 | JSON 格式 | 特性 |
|------|------|----------|------|
| `Point2D` | 坐标点 | `[x, y]` | struct，支持向量运算 |
| `Vec2D` | 向量 | `[dx, dy]` | struct，支持 Normalize/Dot/Cross |
| `Line2D` | 线段 | `[[x1,y1], [x2,y2]]` | Length/Midpoint/Direction |
| `Polygon2D` | 多边形 | `[[x,y], ...]` | ComputeAABB/ComputeCenter |
| `AABB` | 包围盒 | `[minX, minY, maxX, maxY]` | struct，Contains/Intersects |

### v3.0 项目结构

项目文件夹结构对应的 Core 类型：

```
{项目文件夹}/
├── project.json           → Project 类
├── baseline/
│   ├── metadata.json      → BaselineManifest 类
│   ├── architecture.json  → Architecture 类 (walls[] + columns[])
│   ├── openings.json      → Opening[]
│   ├── rooms.json         → Room[]
│   └── location_lines.json → LocationLine[]
├── schemes/{策略}/
│   ├── strategy.json      → Strategy 类
│   ├── zones.json         → Zone[]
│   ├── finishes.json      → FinishSegment[]
│   └── modules.json       → Module[]
└── computed/
    ├── exclusions.json    → ExclusionArea[]
    └── computed.manifest  → 键值对哈希文件
```

### 策略状态枚举 (StrategyStatus)

```csharp
public enum StrategyStatus
{
    Valid,   // 策略与 baseline 一致
    Dirty,   // baseline 已变更，需要重新验证
    Invalid  // 策略数据无效
}
```

### 设计方法枚举 (StrategyApproach)

```csharp
public enum StrategyApproach
{
    CirculationFirst,  // 流线优先
    FurnitureFirst,    // 家具优先
    Manual             // 手动布置
}
```

### LocationLine（完成面定位线）

从墙面提取的定位线，用于计算完成面：

```csharp
public class LocationLine
{
    public string Id { get; set; }           // ll_{序号}
    public string WallId { get; set; }       // 所属墙体
    public string RoomId { get; set; }       // 所属房间
    public string Side { get; set; }         // "interior" | "exterior"
    public Line2D Line { get; set; }         // 定位线坐标
    public double Length { get; set; }       // 冗余存储便于计算
}
```

### FinishSegment（完成面段）

使用 range 表示法的完成面定义：

```csharp
public class FinishSegment
{
    public string Id { get; set; }           // fs_{序号}
    public string SourceLineId { get; set; } // 引用 LocationLine.Id
    public double[] Range { get; set; }      // [起点mm, 终点mm] 绝对值
    public string FinishModuleId { get; set; }
    public double Thickness { get; set; }
    public FinishSource Source { get; set; } // room_default/zone_override/user_override
    public string? ZoneId { get; set; }      // 仅 zone_override 时有值
    public string? Reason { get; set; }
}
```

### ExclusionArea（禁区）

由 Server 计算生成的禁区：

```csharp
public class ExclusionArea
{
    public string Id { get; set; }           // excl_{类型}_{序号}
    public string Type { get; set; }         // "door_swing" | "window_sill"
    public Polygon2D Boundary { get; set; }  // 禁区轮廓（矩形）
    public string SourceId { get; set; }     // 来源元素 ID
}
```

### 朝向系统 (Facing)

支持两种格式：

| 格式 | 示例 | 说明 |
|------|------|------|
| 语义字符串 | `"north"` | 8 个标准方向 |
| Vec2D | `[0.707, 0.707]` | 任意角度单位向量 |

**语义方向映射**：

| 方向 | 角度 | 方向 | 角度 |
|------|------|------|------|
| north | 0° | south | 180° |
| east | 90° | west | 270° |
| northeast | 45° | southwest | 225° |
| southeast | 135° | northwest | 315° |

---

## 算法层

### 几何算法 (GeometryHelper)

```csharp
// 包围盒计算
AABB aabb = GeometryHelper.ComputeAABB(polygon);

// 中心点计算
Point2D center = GeometryHelper.ComputeCenter(polygon);

// 创建矩形
Polygon2D rect = GeometryHelper.CreateRectangle(center, width, height);

// 旋转多边形
Polygon2D rotated = GeometryHelper.RotatePolygon(polygon, angleRad, center);

// 距离计算
double dist = GeometryHelper.Distance(p1, p2);
double distToLine = GeometryHelper.DistanceToLine(point, line);
```

### 碰撞检测 (CollisionDetector)

```csharp
// 相交检测（包括边界接触）
bool intersects = CollisionDetector.Intersects(poly1, poly2);

// 重叠检测（有共同面积）
bool overlaps = CollisionDetector.Overlaps(poly1, poly2);

// 包含检测
bool within = CollisionDetector.IsWithin(inner, outer);
bool contains = CollisionDetector.Contains(polygon, point);

// AABB 快速预检测
bool mayIntersect = CollisionDetector.AABBIntersects(poly1, poly2);
```

### 方案级全量验证 (SchemeValidator) — 布局编译器

类比 C# 编译器，一次性检查整个方案中所有模块的合法性：

```csharp
SchemeValidationReport report = SchemeValidator.Validate(
    modules,           // 所有模块
    designZones,       // 合法放置区域（Room + Designable）
    exclusionZones,    // 禁区（Exclusion）
    walls,             // 墙体（baseline）
    columns            // 柱子（baseline）
);

if (!report.IsValid)
{
    Console.WriteLine($"验证失败: {report.ErrorCount} 个错误 ({report.ElapsedMs}ms)");
    foreach (var d in report.Diagnostics)
        Console.WriteLine(d);  // [E001_OUT_OF_BOUNDS] m3: 模块不在任何设计区域内
}
```

**检查项（6 种错误代码）**：

| 错误代码 | 说明 | ConflictType |
|---------|------|-------------|
| `E001_OUT_OF_BOUNDS` | 模块不在任何设计区/房间区域内 | — |
| `E002_WALL_OVERLAP` | 模块与墙体重叠 | `wall` |
| `E003_COLUMN_OVERLAP` | 模块与柱子重叠 | `column` |
| `E004_EXCLUSION_OVERLAP` | 模块与禁区重叠（门扇等） | `exclusion` |
| `E005_MODULE_OVERLAP` | 模块之间互相重叠 | `module` |
| `E006_MISSING_BOUNDS` | 模块缺少 Bounds 定义 | — |

**算法流程**：3 阶段，全程 AABB 预检加速
1. Phase 1: 预计算所有几何体的 AABB
2. Phase 2: 逐模块检查（边界、墙/柱碰撞、禁区碰撞）
3. Phase 3: 模块间两两重叠检查（O(n²)，双向记录）

### 单模块布置验证 (PlacementValidator)

验证单个模块在指定区域内的布置合法性（用于 Agent 放置前即时检查）：

```csharp
ValidationResult result = PlacementValidator.Validate(
    moduleBounds,      // 模块边界
    zone,              // 目标区域
    existingModules    // 已有模块列表
);

if (!result.IsValid)
{
    foreach (var violation in result.Violations)
        Console.WriteLine($"[{violation.Code}] {violation.Message}");
}
```

**验证规则**：
1. 模块必须完全在 `zone.ComputedBoundary` 内
2. 模块不能与禁区重叠
3. 模块不能与其他已放置模块重叠

### SchemeValidator vs PlacementValidator

```
CollisionDetector (底层碰撞检测)
  ├── SchemeValidator    (全方案 → 所有 zone/墙/柱, 方案完成后编译检查)
  └── PlacementValidator (单模块 → 指定 zone, Agent 放置前即时验证)
```

### Baseline 哈希服务 (BaselineHashService)

v3.0 新增的一致性验证服务：

```csharp
var hashService = new BaselineHashService();

// 计算 baseline 目录的联合哈希
string hash = hashService.ComputeBaselineHash(baselinePath);
// 返回: "sha256:abc123..."

// 验证策略与 baseline 的一致性
StrategyStatus status = hashService.ValidateStrategy(strategy, baselinePath);
```

---

## JSON 序列化

### 自动转换

所有几何类型和枚举都注册了 `JsonConverter` 特性：

```csharp
// 序列化
string json = JsonConvert.SerializeObject(project);

// 反序列化
Project project = JsonConvert.DeserializeObject<Project>(json);
```

### 枚举序列化 (snake_case)

v3.0 枚举使用 snake_case 格式：

| 枚举值 | JSON 格式 |
|--------|----------|
| `StrategyStatus.Valid` | `"valid"` |
| `StrategyApproach.CirculationFirst` | `"circulation_first"` |
| `FinishSource.RoomDefault` | `"room_default"` |

### JSON 格式对照

| 类型 | JSON 示例 |
|------|----------|
| Point2D | `[100, 200]` |
| Vec2D | `[0.707, 0.707]` |
| Line2D | `[[0, 0], [100, 100]]` |
| Polygon2D | `[[0, 0], [100, 0], [100, 100], [0, 100]]` |
| AABB | `[0, 0, 100, 100]` |
| Facing (语义) | `"north"` |
| Facing (向量) | `[0.707, 0.707]` |

---

## API 参考

### 核心类型

#### Point2D

```csharp
public readonly struct Point2D
{
    double X { get; }
    double Y { get; }

    // 运算符
    static Point2D operator +(Point2D p, Vec2D v)
    static Point2D operator -(Point2D p, Vec2D v)
    static Vec2D operator -(Point2D a, Point2D b)
}
```

#### Vec2D

```csharp
public readonly struct Vec2D
{
    double X { get; }
    double Y { get; }
    double Length { get; }

    Vec2D Normalize()
    static double Dot(Vec2D a, Vec2D b)
    static double Cross(Vec2D a, Vec2D b)

    // 运算符
    static Vec2D operator +(Vec2D a, Vec2D b)
    static Vec2D operator -(Vec2D a, Vec2D b)
    static Vec2D operator *(Vec2D v, double scalar)
    static Vec2D operator /(Vec2D v, double scalar)
}
```

#### Project (v3.0)

```csharp
public class Project
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }
    public List<SchemeRef> Schemes { get; set; }
    public string? ActiveSchemeId { get; set; }
}
```

#### Strategy (v3.0)

```csharp
public class Strategy
{
    public string Id { get; set; }
    public string Name { get; set; }
    public StrategyStatus Status { get; set; }
    public StrategyApproach Approach { get; set; }
    public string LastValidatedBaselineHash { get; set; }
    public StrategyOrigin? Origin { get; set; }
}
```

---

## 使用示例

### 加载 v3.0 项目

```csharp
using BIMCanvas.Core.Models.Project;
using BIMCanvas.Core.Models.Revit;
using Newtonsoft.Json;

// 加载项目入口
string projectJson = File.ReadAllText("project.json");
var project = JsonConvert.DeserializeObject<Project>(projectJson);

// 加载 baseline 数据
string archJson = File.ReadAllText("baseline/architecture.json");
var architecture = JsonConvert.DeserializeObject<Architecture>(archJson);

Console.WriteLine($"项目: {project.Name}");
Console.WriteLine($"墙体数量: {architecture.Walls.Count}");
Console.WriteLine($"柱子数量: {architecture.Columns.Count}");
```

### 验证策略一致性

```csharp
using BIMCanvas.Core.Services;
using BIMCanvas.Core.Models.Shared;

var hashService = new BaselineHashService();
var status = hashService.ValidateStrategy(strategy, baselinePath);

switch (status)
{
    case StrategyStatus.Valid:
        Console.WriteLine("策略有效");
        break;
    case StrategyStatus.Dirty:
        Console.WriteLine("Baseline 已变更，需要重新验证");
        break;
    case StrategyStatus.Invalid:
        Console.WriteLine("策略数据无效");
        break;
}
```

### 碰撞检测工作流

```csharp
using BIMCanvas.Core.Algorithms.Spatial;

// 快速 AABB 预检测
if (CollisionDetector.AABBIntersects(newBounds, existingBounds))
{
    // 精确重叠检测
    if (CollisionDetector.Overlaps(newBounds, existingBounds))
    {
        // 存在碰撞
        return false;
    }
}

// 检查是否在区域内
if (!CollisionDetector.IsWithin(newBounds, zone.InnerBoundary))
{
    // 超出边界
    return false;
}
```

---

## 设计决策

### 为什么使用多文件结构

v3.0 采用多文件结构替代单一 JSON 的原因：

- **Git 友好**：每个策略可以有独立的版本历史
- **并发安全**：不同文件可以被不同进程独立修改
- **增量加载**：只加载需要的部分，减少内存占用
- **AI 协作**：支持分支合并工作流

### 为什么使用 struct

`Point2D`、`Vec2D`、`AABB`、`Facing` 使用 `struct` 而非 `class`：

- **性能**：栈分配，避免 GC 压力
- **语义**：值类型语义，不可变
- **场景**：高频创建和销毁（碰撞检测、几何运算）

### 为什么使用 NetTopologySuite

- 成熟的几何库，处理复杂多边形运算
- JTS (Java Topology Suite) 的 .NET 移植
- 支持精确的碰撞检测、包含判断
- NtsAdapter 隔离依赖，便于替换

### 为什么 Facing 支持双格式

- **语义格式**：可读性强，适合 AI 理解
- **向量格式**：精确控制任意角度
- **自动转换**：Core 层内部统一处理

---

## 依赖项

| 包 | 版本 | 用途 |
|----|------|------|
| Newtonsoft.Json | 13.0.3 | JSON 序列化 |
| NetTopologySuite | 2.5.0 | 几何算法 |

---

## 相关文档

- [架构文档](../docs/Architecture.md) - 系统整体架构
- [JSON Schema v3](../docs/Schema-JSON-v3.md) - v3.0 数据模型定义
- [文件驱动架构](../docs/FileDrivenArchitecture.md) - "文件播放器"模式说明
- [升级进度](../plans/V3_Upgrade_Progress_Report.md) - v3.0 升级进度报告
