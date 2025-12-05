# BIMCanvas.Core 代码生成计划

> 基于评审共识，严格遵循已确定的目录结构和技术选型
>
> **相关文档**：
> - [Architecture.md](./Architecture.md) - 系统架构
> - [Schema-JSON.md](./Schema-JSON.md) - JSON Schema 规范
> - [BIMCanvas_Core_Implementation_Review.md](../reviews/BIMCanvas_Core_Implementation_Review.md) - 评审记录

---

## 一、项目初始化

### 1.1 创建解决方案文件

```bash
dotnet new sln -n BIMCanvas
```

### 1.2 创建 BIMCanvas.Core 项目

```bash
dotnet new classlib -n BIMCanvas.Core -f netstandard2.0
dotnet sln add BIMCanvas.Core/BIMCanvas.Core.csproj
```

### 1.3 csproj 配置

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <LangVersion>8.0</LangVersion>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
    <PackageReference Include="NetTopologySuite" Version="2.5.0" />
  </ItemGroup>
</Project>
```

---

## 二、文件生成顺序与规格

### 第一批：Models/Primitives（几何基元）

**依赖关系**：无外部依赖，最先生成

| 文件 | 类型 | 说明 |
|------|------|------|
| `Point2D.cs` | readonly struct | 二维坐标点，JSON: `[x, y]` |
| `Vec2D.cs` | readonly struct | 二维向量，含 `Normalize()` |
| `Line2D.cs` | class | 二维线段，JSON: `[[x1,y1], [x2,y2]]` |
| `AABB.cs` | readonly struct | 轴对齐包围盒 |
| `Polygon2D.cs` | class | 多边形，封装 `Point2D[]` |

### 第二批：Models/Document（业务模型）

| 文件 | 核心字段 |
|------|----------|
| `Facing.cs` | 联合类型：语义字符串 或 Vec2D |
| `Metadata.cs` | RevitViewId, LevelId, GridSize? |
| `Wall.cs` | Id, Polygon |
| `Opening.cs` | Id, Type (door/window), Line |
| `Outline.cs` | Walls[], Openings[] |
| `ExclusionArea.cs` | Id, Type, Boundary |
| `Zone.cs` | Id, Name, Function, InnerBoundary, ExclusionAreas[], Openings[] |
| `ModuleItem.cs` | FamilyId, Offset, Role? |
| `Module.cs` | Id, ModuleId, ModuleName?, Bounds, Facing, ZoneId, Items[] |
| `CanvasDocument.cs` | Id, Version, CoordinateSystem, Metadata, Outline, Zones[], Modules[] |

### 第三批：Converters/Json

| 文件 | 功能 |
|------|------|
| `Point2DConverter.cs` | Point2D ↔ `[x, y]` |
| `FacingConverter.cs` | Facing ↔ `"north"` 或 `[dx, dy]` |

### 第四批：Algorithms/Geometry

| 文件 | 功能 |
|------|------|
| `GeometryHelper.cs` | AABB 计算、中心点、旋转 |
| `NtsAdapter.cs` | Polygon2D ↔ NTS Polygon（internal） |

### 第五批：Algorithms/Spatial

| 文件 | 功能 |
|------|------|
| `FacingHelper.cs` | 语义方向 ↔ Vec2D |
| `GeometryNormalizer.cs` | AI 意图 → Polygon2D |
| `CollisionDetector.cs` | 碰撞检测（调用 NTS） |
| `PlacementValidator.cs` | 布置验证（只验证，不修正） |

### 第六批：Converters

| 文件 | 功能 |
|------|------|
| `UnitConverter.cs` | 单位转换（feet↔mm, rad↔deg） |
| `Revit/RevitToJsonConverter.cs` | Revit数据 → JSON（占位） |
| `Revit/JsonToRevitConverter.cs` | JSON → Revit数据（占位） |

### 第七批：Validation

| 文件 | 功能 |
|------|------|
| `Result.cs` | Result<T, TError> + ValidationResult |

---

## 三、关键实现细节

### 3.1 Facing 设计原则

**核心约定**：
- ❌ 不存储数值角度
- ✅ 存储语义字符串（`"north"`）或 Vec2D 向量（`[0.866, 0.5]`）

**语义 → 向量映射**：
| 语义 | 向量 |
|------|------|
| north | (0, 1) |
| south | (0, -1) |
| east | (1, 0) |
| west | (-1, 0) |
| northeast | normalize(1, 1) |
| northwest | normalize(-1, 1) |
| southeast | normalize(1, -1) |
| southwest | normalize(-1, -1) |

### 3.2 NTS 使用方式

```csharp
// NtsAdapter.cs - internal
internal static NTS.Polygon ToNtsPolygon(Polygon2D polygon)
{
    var factory = new GeometryFactory();
    var coords = polygon.Vertices
        .Select(p => new Coordinate(p.X, p.Y))
        .ToList();
    coords.Add(coords[0]); // NTS 需要闭合
    return factory.CreatePolygon(coords.ToArray());
}
```

---

## 四、验收标准

1. **编译通过**：`dotnet build` 无错误
2. **JSON 序列化正确**：Point2D 输出 `[x, y]`，Facing 输出 `"north"` 或 `[dx, dy]`
3. **碰撞检测可用**：`CollisionDetector.Intersects()` 正确调用 NTS
4. **单元测试覆盖**：核心类型和算法有测试用例

---

## 五、进度追踪

> 此章节用于记录代码生成进度，支持跨对话继续

### 当前状态

| 阶段 | 状态 | 更新时间 |
|------|------|----------|
| 一. 项目初始化 | ✅ 已完成 | 2025-12-04 |
| 二. Models/Primitives | ✅ 已完成 | 2025-12-04 |
| 三. Models/Document | ✅ 已完成 | 2025-12-04 |
| 四. Converters/Json | ✅ 已完成 | 2025-12-04 |
| 五. Validation | ✅ 已完成 | 2025-12-04 |
| 六. Algorithms/Geometry | ✅ 已完成 | 2025-12-04 |
| 七. Algorithms/Spatial | ✅ 已完成 | 2025-12-04 |
| 八. Converters (单位+Revit) | ✅ 已完成 | 2025-12-04 |
| **九. v2.5 设计变更** | ✅ 已完成 | 2025-12-04 |

### 已完成文件

```
BIMCanvas.sln
BIMCanvas.Core/BIMCanvas.Core.csproj

Models/Primitives/
├── Point2D.cs
├── Vec2D.cs
├── Line2D.cs
├── AABB.cs
└── Polygon2D.cs

Models/Document/
├── Facing.cs                 (v2.5 更新：_semantic 改为 FacingDirection 枚举)
├── FacingDirection.cs        (v2.5 新增：朝向方向枚举)
├── Metadata.cs
├── Wall.cs
├── Opening.cs
├── Outline.cs
├── Room.cs                   (v2.5 新增：物理房间)
├── RoomType.cs               (v2.5 新增：房间类型枚举)
├── ExclusionArea.cs
├── Zone.cs                   (v2.5 更新：移除 Function，新增 RoomId/Tags/RawBoundary)
├── ZoneTag.cs                (v2.5 新增：区域功能标签枚举)
├── WallFinish.cs             (v2.5 新增：墙面完成面)
├── FinishSource.cs           (v2.5 新增：完成面来源枚举)
├── ModuleItem.cs
├── Module.cs
└── CanvasDocument.cs         (v2.5 更新：新增 Rooms/WallFinishes)

Converters/Json/
├── Point2DConverter.cs
├── Vec2DConverter.cs
├── FacingConverter.cs
├── Polygon2DConverter.cs
├── Line2DConverter.cs
└── AABBConverter.cs

Validation/
└── Result.cs

Algorithms/Geometry/
├── GeometryHelper.cs
└── NtsAdapter.cs (internal)

Algorithms/Spatial/
├── FacingHelper.cs
├── GeometryNormalizer.cs
├── CollisionDetector.cs
├── PlacementValidator.cs
└── FinishRules.cs            (v2.5 新增：特殊完成面规则表)

Converters/
├── UnitConverter.cs
└── Revit/
    ├── RevitToJsonConverter.cs (占位)
    └── JsonToRevitConverter.cs (占位)
```

### 待处理问题

```
（暂无 - 所有计划任务已完成）
```

### 变更日志

| 时间 | 变更内容 |
|------|----------|
| 2025-12-04 | 计划创建 |
| 2025-12-04 | 完成项目初始化 + Models/Primitives + Models/Document |
| 2025-12-04 | 完成 Converters/Json（6个转换器） |
| 2025-12-04 | 完成 Validation + Algorithms + Converters，全部代码生成完毕 |
| 2025-12-04 | **v2.5 设计变更**：新增 Room/WallFinish 概念，ZoneTag 多标签，Facing 枚举化 |

---

## 六、v2.5 设计变更

> 基于讨论共识，更新数据模型和代码

### 6.1 变更总结

| 变更项 | 内容 |
|--------|------|
| **Facing 枚举化** | `_semantic` 从 `string?` 改为 `FacingDirection?`，JSON 保持 `"north"` 格式 |
| **新增 Room 概念** | 物理房间（对应 Revit Room），Zone 属于 Room |
| **Zone 功能标签** | 移除 `Function` 枚举，新增 `Tags` 列表（支持多标签） |
| **WallFinish 墙面完成面** | 新增数据结构，作为禁区轮廓参与布置验证 |

### 6.2 新增枚举

```csharp
// FacingDirection.cs - 8 个朝向方向
public enum FacingDirection
{
    [EnumMember(Value = "north")] North,
    [EnumMember(Value = "south")] South,
    [EnumMember(Value = "east")] East,
    [EnumMember(Value = "west")] West,
    [EnumMember(Value = "northeast")] Northeast,
    [EnumMember(Value = "northwest")] Northwest,
    [EnumMember(Value = "southeast")] Southeast,
    [EnumMember(Value = "southwest")] Southwest
}

// RoomType.cs - 房间类型
public enum RoomType
{
    LivingRoom, DiningRoom, MasterBedroom, Bedroom, Study,
    Kitchen, Bathroom, Entrance, Balcony, Corridor, Storage
}

// ZoneTag.cs - 区域功能标签（细粒度）
public enum ZoneTag
{
    TvMedia, AudioVideo,                    // 多媒体
    Sleep, Rest, Reading,                   // 休息
    Work, Study,                            // 工作
    WardrobeStorage, ShoeStorage, GeneralStorage,  // 收纳
    Dining, Cooking, FoodPrep, Bar,         // 餐饮
    Shower, Bathtub, Toilet, Washing, Laundry,     // 卫浴
    Vanity, Entry, Passage, Display, Plants        // 其他
}

// FinishSource.cs - 完成面来源追踪
public enum FinishSource
{
    RoomDefault,    // 房间类型默认值
    ZoneOverride,   // 工作区标签覆盖
    UserOverride    // 用户手动设置
}
```

### 6.3 新增类

```csharp
// Room.cs - 物理房间
public class Room
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public RoomType Type { get; set; }
    public Polygon2D? Boundary { get; set; }
}

// WallFinish.cs - 墙面完成面
public class WallFinish
{
    public string Id { get; set; } = string.Empty;
    public Line2D? LocationLine { get; set; }      // 定位线
    public double Thickness { get; set; }          // 厚度（mm）
    public string? FinishModuleId { get; set; }    // 模块库 ID
    public Polygon2D? ExclusionBoundary { get; set; }  // 禁区轮廓
    public string WallId { get; set; } = string.Empty;
    public string RoomId { get; set; } = string.Empty;
    public FinishSource Source { get; set; }
}
```

### 6.4 修改类

```csharp
// Facing.cs - _semantic 改为枚举类型
public readonly struct Facing
{
    private readonly FacingDirection? _semantic;  // 从 string? 改为枚举
    private readonly Vec2D? _vector;
    // ...
}

// Zone.cs - 移除 Function，新增 RoomId + Tags
public class Zone
{
    public string RoomId { get; set; } = string.Empty;    // 新增
    public List<ZoneTag> Tags { get; set; } = new();      // 替代 Function
    public Polygon2D? RawBoundary { get; set; }           // 新增
    // ...
}

// CanvasDocument.cs - 新增 Rooms + WallFinishes
public class CanvasDocument
{
    public List<Room> Rooms { get; set; } = new();             // 新增
    public List<WallFinish> WallFinishes { get; set; } = new();  // 新增
    // ...
}
```

### 6.5 新增算法

```csharp
// FinishRules.cs - 特殊完成面规则表
public static class FinishRules
{
    public static readonly Dictionary<ZoneTag, double> SpecialFinishThickness = new()
    {
        { ZoneTag.TvMedia, 80.0 },  // 电视区 → 80mm
        { ZoneTag.Sleep, 60.0 },    // 睡眠区 → 60mm
        { ZoneTag.Bar, 40.0 },      // 吧台区 → 40mm
    };

    public static bool TriggersSpecialFinish(ZoneTag tag)
        => SpecialFinishThickness.ContainsKey(tag);
}

// GeometryHelper.cs - 新增 ComputeExclusionBoundary
public static Polygon2D ComputeExclusionBoundary(Line2D locationLine, double thickness)
{
    var direction = (locationLine.End - locationLine.Start).Normalize();
    var normal = new Vec2D(-direction.Y, direction.X);

    var p1 = locationLine.Start;
    var p2 = locationLine.End;
    var p3 = p2 + normal * thickness;
    var p4 = p1 + normal * thickness;

    return new Polygon2D(new[] { p1, p2, p3, p4 });
}
```
