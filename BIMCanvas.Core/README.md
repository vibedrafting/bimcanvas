# BIMCanvas.Core

> BIMCanvas 系统的核心层，提供数据模型、空间算法和 JSON 序列化能力。

**运行时**：.NET Standard 2.0（跨框架兼容）
**数据模型版本**：v2.6

---

## 目录

- [项目结构](#项目结构)
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
│   ├── Primitives/              基础几何类型
│   │   ├── Point2D.cs              坐标点
│   │   ├── Vec2D.cs                向量
│   │   ├── Polygon2D.cs            多边形
│   │   ├── AABB.cs                 轴对齐包围盒
│   │   └── Line2D.cs               线段
│   │
│   ├── RevitSource/             Revit 提取的原始数据
│   │   ├── CanvasDocument.cs       文档根对象
│   │   ├── Metadata.cs             元数据（含坐标变换参数）
│   │   ├── Wall.cs                 墙体轮廓
│   │   ├── Column.cs               柱子轮廓
│   │   ├── Opening.cs              门窗数据
│   │   ├── Room.cs                 房间边界
│   │   └── FinishLocationBoundary.cs  完成面定位边界
│   │
│   ├── CanvasData/              画布独有数据（Server 计算）
│   │   ├── Zone.cs                 设计区域
│   │   ├── WallFinish.cs           墙面完成面
│   │   └── ExclusionArea.cs        禁区
│   │
│   ├── AIInput/                 AI 输入数据（预留）
│   ├── AIOutput/                AI 输出数据（预留）
│   │
│   ├── RevitWriteback/          Revit 回写数据
│   │   ├── Module.cs               布置模块
│   │   ├── ModuleItem.cs           模块内家具
│   │   ├── Facing.cs               朝向（联合类型）
│   │   └── FacingDirection.cs      朝向枚举
│   │
│   └── Shared/                  共享枚举
│       ├── RoomType.cs             房间类型
│       ├── ZoneTag.cs              区域标签
│       └── FinishSource.cs         完成面来源
│
├── Algorithms/
│   ├── Geometry/                几何算法
│   │   ├── GeometryHelper.cs       几何运算工具
│   │   └── NtsAdapter.cs           NTS 适配器
│   │
│   └── Spatial/                 空间算法
│       ├── PlacementValidator.cs   布置验证
│       ├── CollisionDetector.cs    碰撞检测
│       ├── FacingHelper.cs         朝向转换
│       ├── GeometryNormalizer.cs   几何规范化
│       └── FinishRules.cs          完成面规则
│
├── Converters/
│   ├── Json/                    JSON 转换器
│   ├── Revit/                   Revit 集成（占位）
│   └── UnitConverter.cs         单位转换
│
└── Validation/
    └── Result.cs                验证结果类型
```

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

### 文档结构 (CanvasDocument)

v2.6 采用扁平化结构，建筑构件直接放在顶层：

```
CanvasDocument
├── id                          画布唯一标识
├── version                     版本号
├── coordinateSystem            坐标系标识
├── metadata                    元数据（含坐标变换参数）
│
├── walls[]                     墙体轮廓（单独墙体）
├── columns[]                   柱子轮廓（含 isStructural）
├── openings[]                  门窗数据
├── finishLocationBoundaries[]  完成面定位边界（墙柱组合轮廓，已过滤外墙）
│
├── rooms[]                     物理房间
├── zones[]                     设计区域（AI 核心工作区）
│   ├── innerBoundary           可用空间轮廓
│   ├── exclusionAreas[]        禁区列表
│   └── openings[]              关联门窗 ID
├── wallFinishes[]              墙面完成面
└── modules[]                   布置模块
    ├── bounds                  精确边界 (4顶点矩形)
    ├── facing                  朝向 (语义|向量)
    └── items[]                 内部家具清单
```

**命名空间分组**：

| 分组 | 命名空间 | 说明 |
|------|----------|------|
| Primitives | `.Models.Primitives` | 几何基元 |
| RevitSource | `.Models.RevitSource` | Revit 导出的原始数据 |
| CanvasData | `.Models.CanvasData` | Server 计算生成的数据 |
| RevitWriteback | `.Models.RevitWriteback` | 回写 Revit 的数据 |
| Shared | `.Models.Shared` | 跨模块共享的枚举 |

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

### 枚举类型

**ZoneTag（区域功能标签）**：
- 视听：`TvMedia`, `AudioVideo`
- 休息：`Sleep`, `Rest`, `Reading`
- 工作：`Work`, `Study`
- 收纳：`WardrobeStorage`, `ShoeStorage`, `GeneralStorage`
- 餐饮：`Dining`, `Cooking`, `FoodPrep`, `Bar`
- 卫浴：`Shower`, `Bathtub`, `Toilet`, `Washing`, `Laundry`

**RoomType（房间类型）**：`LivingRoom`, `Bedroom`, `Kitchen`, `Bathroom` 等

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

// 完成面禁区
Polygon2D exclusion = GeometryHelper.ComputeExclusionBoundary(locationLine, thickness);
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

### 布置验证 (PlacementValidator)

验证模块布置是否合法：

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
1. 模块必须完全在 `zone.InnerBoundary` 内
2. 模块不能与 `zone.ExclusionAreas` 重叠
3. 模块不能与其他已放置模块重叠

### 朝向转换 (FacingHelper)

```csharp
// 语义 → 向量
Vec2D v = FacingHelper.SemanticToVector("north");  // [0, 1]

// 角度 → 向量
Vec2D v = FacingHelper.AngleToVector(45);  // [0.707, 0.707]

// 向量 → 角度
double angle = FacingHelper.VectorToAngle(new Vec2D(1, 0));  // 90

// 向量 → 语义（5° 容差）
string? semantic = FacingHelper.VectorToSemantic(vec);
```

### 几何规范化 (GeometryNormalizer)

将 AI 布置意图转换为精确几何：

```csharp
// 从中心点、尺寸、朝向创建模块边界
Polygon2D bounds = GeometryNormalizer.CreateModuleBounds(
    centerX: 500,
    centerY: 500,
    width: 1000,
    depth: 800,
    facing: FacingDirection.North
);

// 从 AABB 和朝向创建
Polygon2D bounds = GeometryNormalizer.CreateFromAABB(aabb, facing);
```

### 完成面规则 (FinishRules)

```csharp
// 查询特殊厚度
double? thickness = FinishRules.GetSpecialThickness(ZoneTag.TvMedia);  // 80mm

// 从标签列表获取最大厚度
double? maxThickness = FinishRules.GetMaxSpecialThickness(zone.Tags);

// 判断是否触发特殊完成面
bool triggers = FinishRules.TriggersSpecialFinish(ZoneTag.Sleep);  // true
```

**规则表**：
| 功能标签 | 厚度 |
|---------|------|
| TvMedia | 80mm |
| Sleep | 60mm |
| Bar | 40mm |

---

## JSON 序列化

### 自动转换

所有几何类型都注册了 `JsonConverter` 特性，使用 Newtonsoft.Json 时自动生效：

```csharp
// 序列化
string json = JsonConvert.SerializeObject(document);

// 反序列化
CanvasDocument doc = JsonConvert.DeserializeObject<CanvasDocument>(json);
```

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

### 单位转换 (UnitConverter)

```csharp
// 长度
double mm = UnitConverter.ToMillimeters(feet: 10);  // 3048
double feet = UnitConverter.ToFeet(mm: 3048);       // 10

// 角度
double rad = UnitConverter.ToRadians(degrees: 90);  // π/2
double deg = UnitConverter.ToDegrees(radians: Math.PI);  // 180
```

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

#### Polygon2D

```csharp
public class Polygon2D
{
    IReadOnlyList<Point2D> Vertices { get; }
    int VertexCount { get; }

    AABB ComputeAABB()
    Point2D ComputeCenter()
}
```

#### Facing

```csharp
public readonly struct Facing
{
    bool IsSemantic { get; }
    FacingDirection? Semantic { get; }
    Vec2D? Vector { get; }

    double ToAngleRadians()
    Vec2D GetVector()

    // 隐式转换
    static implicit operator Facing(FacingDirection d)
    static implicit operator Facing(Vec2D v)

    // 解析
    static FacingDirection ParseSemantic(string value)
}
```

#### ValidationResult

```csharp
public class ValidationResult
{
    bool IsValid { get; }
    IReadOnlyList<Violation> Violations { get; }

    static ValidationResult Success()
    static ValidationResult Failure(List<Violation> violations)
    static ValidationResult Failure(string message)
}

public class Violation
{
    string Message { get; }
    string? Code { get; }       // "OUT_OF_BOUNDS", "COLLISION", etc.
    string? ObjectId { get; }
}
```

---

## 使用示例

### 加载并验证文档

```csharp
using BIMCanvas.Core.Models.RevitSource;
using BIMCanvas.Core.Models.RevitWriteback;
using BIMCanvas.Core.Algorithms.Spatial;
using Newtonsoft.Json;

// 加载文档
string json = File.ReadAllText("canvas.json");
var document = JsonConvert.DeserializeObject<CanvasDocument>(json);

// 验证所有模块布置
foreach (var module in document.Modules)
{
    var zone = document.Zones.First(z => z.Id == module.ZoneId);
    var otherModules = document.Modules
        .Where(m => m.Id != module.Id && m.ZoneId == module.ZoneId)
        .ToList();

    var result = PlacementValidator.ValidateModule(module, zone, otherModules);

    if (!result.IsValid)
    {
        Console.WriteLine($"模块 {module.Id} 布置无效：");
        foreach (var v in result.Violations)
            Console.WriteLine($"  - [{v.Code}] {v.Message}");
    }
}
```

### 创建新模块

```csharp
using BIMCanvas.Core.Models.Primitives;
using BIMCanvas.Core.Models.RevitWriteback;
using BIMCanvas.Core.Algorithms.Spatial;

// 创建模块边界
var bounds = GeometryNormalizer.CreateModuleBounds(
    centerX: 2000,
    centerY: 3000,
    width: 1800,
    depth: 900,
    facing: FacingDirection.South
);

// 创建模块
var module = new Module
{
    Id = "m1",
    ModuleId = "sofa_3seat",
    ModuleName = "三人沙发",
    Bounds = bounds,
    Facing = FacingDirection.South,
    ZoneId = "z1"
};

// 验证后添加到文档
var zone = document.Zones.First(z => z.Id == "z1");
var result = PlacementValidator.Validate(bounds, zone, document.Modules);

if (result.IsValid)
{
    document.Modules.Add(module);
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

### 为什么验证只返回结果不修正

- **职责分离**：Core 层只做验证，修正逻辑由上层（AI/UI）决定
- **可预测性**：同样的输入总是得到同样的验证结果
- **灵活性**：不同场景可能需要不同的修正策略

---

## 依赖项

| 包 | 版本 | 用途 |
|----|------|------|
| Newtonsoft.Json | 13.0.3 | JSON 序列化 |
| NetTopologySuite | 2.5.0 | 几何算法 |

---

## 相关文档

- [架构文档](../docs/Architecture.md) - 系统整体架构
- [JSON Schema](../docs/Schema-JSON.md) - v2.5 数据模型定义
- [PRD](../docs/PRD.md) - 产品需求文档
