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
├── Facing.cs
├── Metadata.cs
├── Wall.cs
├── Opening.cs
├── Outline.cs
├── ExclusionArea.cs
├── Zone.cs
├── ModuleItem.cs
├── Module.cs
└── CanvasDocument.cs

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
└── PlacementValidator.cs

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
