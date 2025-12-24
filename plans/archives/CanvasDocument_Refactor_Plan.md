# CanvasDocument 数据结构改造实施方案

> **目标**：重构 CanvasDocument 数据存储结构，为 AI 提供语义化的原建筑数据，同时支持墙面完成面定位线计算
>
> **实施文件**：实施时将复制到 `plans/CanvasDocument_Refactor_Plan.md`

---

## 1. 改造背景与目标

### 1.1 核心变更

| 数据类型 | 当前状态 | 改造后 | 用途 |
|----------|----------|--------|------|
| 墙柱连续组合轮廓 | ✅ `Outline.Boundarys` | ✅ `FinishLocationBoundaries` | 墙面完成面定位线 |
| 单独墙体轮廓 | ❌ 未存储 | ✅ `Walls` | Web 渲染 + AI 增强输入 |
| 单独柱子轮廓 | ❌ 未存储 | ✅ `Columns` | Web 渲染 + AI 核心输入 |
| 门窗开口 | ✅ `Outline.Openings` | ✅ `Openings` | Web 渲染 + AI 核心输入 |
| Outline 包装类 | ✅ 存在 | ❌ **删除** | 简化结构 |

### 1.2 关键原则：不重复造轮子

> ⚠️ **重要**：本次改造复用现有代码，不新增提取逻辑

| 数据 | 现有代码 | 复用方式 |
|------|----------|----------|
| 墙柱连续组合轮廓 | `BoundaryAdapter.cs` | 直接调用，结果过滤外墙边后存入 `FinishLocationBoundaries` |
| 单独墙体/柱子轮廓 | `ElementOutlineAdapter.cs` | 直接调用，按类型分别存入 `Walls` / `Columns` |
| 门窗开口 | `OpeningAdapter.cs` | 保持现有调用不变 |

---

## 2. 现有代码分析

### 2.1 BoundaryAdapter.cs（墙柱连续组合轮廓）

**位置**：`BIMCanvas.Revit/Adapters/BoundaryAdapter.cs`

**功能**：
- 提取墙体 + 柱子的布尔运算后的连续轮廓
- 已考虑门窗开口（轮廓在开口处断开）
- 返回 `List<RevitBoundary>`，包含 `Id`、`ElementIds`、`Boundary`（NTS Polygon）

**输出示例**：
```csharp
RevitBoundary {
    Id = "boundary_001",
    ElementIds = [12345, 23456, 34567],  // 相关墙/柱的 ElementId
    Boundary = Polygon(...)              // NTS Polygon，feet 单位
}
```

**复用方式**：
```csharp
var boundaryAdapter = new BoundaryAdapter(options);
var rawBoundaries = boundaryAdapter.ExtractBoundaries(view);
// 然后过滤外墙边...
```

### 2.2 ElementOutlineAdapter.cs（单独墙体/柱子轮廓）

**位置**：`BIMCanvas.Revit/Adapters/ElementOutlineAdapter.cs`

**功能**：
- 墙体：使用 Solid 切割提取轮廓，支持门洞分割成多段
- 柱子：使用 BoundingBox 生成矩形轮廓
- 返回 `List<ElementOutline>`，包含 `Id`、`ElementId`、`Type`、`Boundary`

**输出示例**：
```csharp
ElementOutline {
    Id = "wall_001",
    ElementId = 12345,
    Type = OutlineElementType.Wall,      // Wall / Column / StructuralColumn
    Boundary = Polygon(...)              // NTS Polygon，feet 单位
}
```

**复用方式**：
```csharp
var elementOutlineAdapter = new ElementOutlineAdapter(options);
var elementOutlines = elementOutlineAdapter.ExtractOutlines(view);

// 分离墙体和柱子
var wallOutlines = elementOutlines.Where(e => e.Type == OutlineElementType.Wall);
var columnOutlines = elementOutlines.Where(e => e.Type == OutlineElementType.Column
                                              || e.Type == OutlineElementType.StructuralColumn);
```

**注意**：该文件包含调试代码（第 81-99 行），需要移除：
- `doc.DisplayLine(...)` 调用
- `System.Windows.MessageBox.Show(...)` 调用

---

## 3. 外墙过滤逻辑

### 3.1 需求说明

墙柱连续组合轮廓（`FinishLocationBoundaries`）用于计算墙面完成面定位线。但外墙不需要完成面，应在导出时排除。

### 3.2 过滤原理

**判断方法**：轮廓的每条边，检查其"内侧"是否在任何 Room 内
- 如果内侧在 Room 内 → 内墙边 → 保留
- 如果内侧不在任何 Room 内 → 外墙边 → 排除

**图示**：
```
          外墙（排除）
    ┌────────────────────┐
    │                    │
外  │    Room 内部       │  外
墙  │                    │  墙
    │                    │
    └────────────────────┘
          内墙（保留）
```

### 3.3 实现方案

在 `CanvasExportService.cs` 中新增过滤方法：

```csharp
/// <summary>
/// 过滤外墙边，只保留内墙边构成的定位线
/// </summary>
/// <param name="boundaries">BoundaryAdapter 提取的原始轮廓</param>
/// <param name="rooms">RoomAdapter 提取的房间列表</param>
/// <returns>过滤后的轮廓（仅包含内墙边）</returns>
private List<RevitBoundary> FilterExteriorEdges(
    List<RevitBoundary> boundaries,
    List<RevitRoom> rooms)
{
    var result = new List<RevitBoundary>();

    foreach (var boundary in boundaries)
    {
        if (boundary.Boundary == null) continue;

        var shell = boundary.Boundary.Shell;
        var interiorSegments = new List<LineSegment>();

        // 遍历轮廓的每条边
        for (int i = 0; i < shell.NumPoints - 1; i++)
        {
            var p0 = shell.GetCoordinateN(i);
            var p1 = shell.GetCoordinateN(i + 1);

            // 判断是否为内墙边
            if (IsInteriorEdge(p0, p1, rooms))
            {
                interiorSegments.Add(new LineSegment(p0, p1));
            }
        }

        // 如果有内墙边，创建过滤后的结果
        if (interiorSegments.Count > 0)
        {
            // 将内墙边段组装为新的轮廓或线段列表
            // 注意：过滤后可能不再是封闭轮廓，而是多条线段
            result.Add(CreateFilteredBoundary(boundary, interiorSegments));
        }
    }

    return result;
}

/// <summary>
/// 判断边是否为内墙边（内侧在任何 Room 内）
/// </summary>
private bool IsInteriorEdge(Coordinate p0, Coordinate p1, List<RevitRoom> rooms)
{
    // 1. 计算边的中点
    var midX = (p0.X + p1.X) / 2;
    var midY = (p0.Y + p1.Y) / 2;

    // 2. 计算边的内侧法向（逆时针轮廓的右侧）
    var dx = p1.X - p0.X;
    var dy = p1.Y - p0.Y;
    var len = Math.Sqrt(dx * dx + dy * dy);
    if (len < 1e-6) return false;  // 忽略零长度边

    // 垂直于边的方向（右侧）
    var normalX = dy / len;
    var normalY = -dx / len;

    // 3. 在中点内侧方向偏移一小段距离（0.1 feet ≈ 30mm）
    var testPoint = new Point(new Coordinate(
        midX + normalX * 0.1,
        midY + normalY * 0.1
    ));

    // 4. 检查测试点是否在任何 Room 内
    foreach (var room in rooms)
    {
        if (room.Boundary != null && room.Boundary.Contains(testPoint))
        {
            return true;  // 在房间内，是内墙边
        }
    }

    return false;  // 不在任何房间内，是外墙边
}
```

### 3.4 特殊情况处理

| 情况 | 处理方式 |
|------|----------|
| 过滤后轮廓为空 | 跳过该 boundary，不添加到结果 |
| 过滤后不再封闭 | 可接受，完成面定位线本身可以是线段列表 |
| Room 边界为 null | 跳过该 Room 的检查 |
| 边长度为 0 | 跳过该边 |

---

## 4. 数据模型改造

### 4.1 Core 层新增模型

#### Wall.cs

```csharp
// BIMCanvas.Core/Models/Document/Wall.cs
using BIMCanvas.Core.Models.Primitives;

namespace BIMCanvas.Core.Models.Document
{
    /// <summary>
    /// 单独墙体轮廓
    /// </summary>
    public class Wall
    {
        public string Id { get; set; } = string.Empty;
        public int ElementId { get; set; }
        public Polygon2D? Polygon { get; set; }
    }
}
```

#### Column.cs

```csharp
// BIMCanvas.Core/Models/Document/Column.cs
using BIMCanvas.Core.Models.Primitives;

namespace BIMCanvas.Core.Models.Document
{
    /// <summary>
    /// 单独柱子轮廓
    /// </summary>
    public class Column
    {
        public string Id { get; set; } = string.Empty;
        public int ElementId { get; set; }
        public bool IsStructural { get; set; }  // true=结构柱, false=建筑柱
        public Polygon2D? Polygon { get; set; }
    }
}
```

#### FinishLocationBoundary.cs

```csharp
// BIMCanvas.Core/Models/Document/FinishLocationBoundary.cs
using System.Collections.Generic;
using BIMCanvas.Core.Models.Primitives;

namespace BIMCanvas.Core.Models.Document
{
    /// <summary>
    /// 完成面定位边界（墙柱连续组合轮廓，已过滤外墙）
    /// 用于 Server 端计算墙面完成面定位线
    /// </summary>
    public class FinishLocationBoundary
    {
        public string Id { get; set; } = string.Empty;
        public List<int> ElementIds { get; set; } = new();
        public Polygon2D? Polygon { get; set; }
    }
}
```

### 4.2 修改 CanvasDocument.cs

```csharp
// BIMCanvas.Core/Models/Document/CanvasDocument.cs
using System.Collections.Generic;

namespace BIMCanvas.Core.Models.Document
{
    public class CanvasDocument
    {
        // === 元数据 ===
        public string Id { get; set; } = string.Empty;
        public int Version { get; set; }
        public string CoordinateSystem { get; set; } = "cartesian_mm_yUp";
        public Metadata? Metadata { get; set; }

        // === 建筑构件（原 Outline 内容，提升到顶层）===
        public List<Wall> Walls { get; set; } = new();
        public List<Column> Columns { get; set; } = new();
        public List<Opening> Openings { get; set; } = new();
        public List<FinishLocationBoundary> FinishLocationBoundaries { get; set; } = new();

        // === 空间数据 ===
        public List<Room> Rooms { get; set; } = new();
        public List<Zone> Zones { get; set; } = new();
        public List<WallFinish> WallFinishes { get; set; } = new();
        public List<Module> Modules { get; set; } = new();
    }
}
```

### 4.3 删除文件

| 文件 | 原因 |
|------|------|
| `Models/Document/Outline.cs` | 不再需要包装类 |
| `Models/Document/Boundary.cs` | 被 FinishLocationBoundary 替代 |

---

## 5. Revit 层改造

### 5.1 修改 CanvasExportService.cs

**改造要点**：

1. **Phase 1**：新增 ElementOutlineAdapter 调用
2. **Phase 4**：新增转换逻辑 + 外墙过滤
3. **Phase 6**：直接组装到顶层（不再嵌套 Outline）

**完整改造代码**：

```csharp
public CanvasDocument ExportFromView(View view, ExportOptions options)
{
    // ===== Phase 1: 提取原始数据 =====
    var rawBoundaries = new List<RevitBoundary>();
    var rawOpenings = new List<RevitOpening>();
    var revitRooms = new List<RevitRoom>();
    var elementOutlines = new List<ElementOutline>();  // 新增

    if (options.ExportBoundarys)
    {
        var boundaryAdapter = new BoundaryAdapter(options);
        rawBoundaries = boundaryAdapter.ExtractBoundaries(view);
    }

    if (options.ExportOpenings)
    {
        var openingAdapter = new OpeningAdapter();
        rawOpenings = openingAdapter.ExtractOpenings(view);
    }

    if (options.ExportRooms)
    {
        var roomAdapter = new RoomAdapter();
        revitRooms = roomAdapter.ExtractRooms(view);
    }

    // 新增：提取单构件轮廓（复用 ElementOutlineAdapter）
    if (options.ExportElementOutlines)
    {
        var elementOutlineAdapter = new ElementOutlineAdapter(options);
        elementOutlines = elementOutlineAdapter.ExtractOutlines(view);
    }

    // ===== Phase 2-3: 计算原点、创建转换器（保持不变）=====
    // ...

    // ===== Phase 4: 统一坐标转换 =====

    // 新增：转换单独墙体轮廓
    var walls = elementOutlines
        .Where(e => e.Type == OutlineElementType.Wall)
        .Select(e => new Wall
        {
            Id = e.Id,
            ElementId = e.ElementId,
            Polygon = NtsConverter.FromNtsPolygon(transformer.TransformPolygon(e.Boundary))
        }).ToList();

    // 新增：转换单独柱子轮廓
    var columns = elementOutlines
        .Where(e => e.Type == OutlineElementType.Column || e.Type == OutlineElementType.StructuralColumn)
        .Select(e => new Column
        {
            Id = e.Id,
            ElementId = e.ElementId,
            IsStructural = e.Type == OutlineElementType.StructuralColumn,
            Polygon = NtsConverter.FromNtsPolygon(transformer.TransformPolygon(e.Boundary))
        }).ToList();

    // 门窗转换（保持不变）
    var openings = rawOpenings.Select(ro => new Opening { ... }).ToList();

    // 新增：过滤外墙边，只保留内墙边
    var filteredBoundaries = FilterExteriorEdges(rawBoundaries, revitRooms);

    // 新增：转换完成面定位边界
    var finishLocationBoundaries = filteredBoundaries.Select(rb => new FinishLocationBoundary
    {
        Id = rb.Id,
        ElementIds = rb.ElementIds,
        Polygon = NtsConverter.FromNtsPolygon(transformer.TransformPolygon(rb.Boundary))
    }).ToList();

    // 房间转换（保持不变）
    var rooms = revitRooms.Select(rr => new Room { ... }).ToList();

    // ===== Phase 5: 用户确认房间类型（保持不变）=====
    // ...

    // ===== Phase 6: 组装 CanvasDocument（修改：不再嵌套 Outline）=====
    return new CanvasDocument
    {
        Id = $"canvas_{Guid.NewGuid():N}",
        Version = 1,
        CoordinateSystem = "cartesian_mm_yUp",
        Metadata = metadata,

        // 建筑构件（直接在顶层）
        Walls = walls,
        Columns = columns,
        Openings = openings,
        FinishLocationBoundaries = finishLocationBoundaries,

        // 空间数据
        Rooms = rooms,
        Zones = new List<Zone>(),
        WallFinishes = new List<WallFinish>(),
        Modules = new List<Module>()
    };
}
```

### 5.2 修改 ExportOptions.cs

```csharp
// 新增选项
public bool ExportElementOutlines { get; set; } = true;
```

### 5.3 清理 ElementOutlineAdapter.cs 调试代码

**删除第 81-99 行**：

```csharp
// 删除以下代码
foreach (var item in result)
{
    switch (item.Type)
    {
        case OutlineElementType.Wall:
            doc.DisplayLine(item.Boundary, ColorType.Blue);
            break;
        case OutlineElementType.Column:
            doc.DisplayLine(item.Boundary, ColorType.Red);
            break;
        case OutlineElementType.StructuralColumn:
            doc.DisplayLine(item.Boundary, ColorType.Red);
            break;
        default:
            break;
    }
}
System.Windows.MessageBox.Show($"{result.Count}");
```

---

## 6. 文件改动清单

### 6.1 Core 层（BIMCanvas.Core）

| 操作 | 文件路径 | 说明 |
|------|----------|------|
| **新增** | `Models/Document/Wall.cs` | 单独墙体轮廓 |
| **新增** | `Models/Document/Column.cs` | 单独柱子轮廓 |
| **新增** | `Models/Document/FinishLocationBoundary.cs` | 完成面定位边界 |
| **修改** | `Models/Document/CanvasDocument.cs` | 移除 Outline，新增 4 个顶层字段 |
| **删除** | `Models/Document/Outline.cs` | 不再需要 |
| **删除** | `Models/Document/Boundary.cs` | 被替代 |

### 6.2 Revit 层（BIMCanvas.Revit）

| 操作 | 文件路径 | 说明 |
|------|----------|------|
| **修改** | `Services/CanvasExportService.cs` | 复用 Adapter、外墙过滤、组装逻辑 |
| **修改** | `Models/ExportOptions.cs` | 新增 ExportElementOutlines 选项 |
| **修改** | `Adapters/ElementOutlineAdapter.cs` | 移除调试代码（第 81-99 行）|

### 6.3 文档更新

| 操作 | 文件路径 | 说明 |
|------|----------|------|
| **修改** | `docs/Schema-JSON.md` | 更新顶层结构定义 |

---

## 7. 实施步骤

### Step 1: Core 层数据模型

1. 新增 `Wall.cs`、`Column.cs`、`FinishLocationBoundary.cs`
2. 修改 `CanvasDocument.cs`（移除 Outline，新增顶层字段）
3. 删除 `Outline.cs`、`Boundary.cs`
4. 编译验证

### Step 2: Revit 层导出逻辑

1. 修改 `ExportOptions.cs` 新增选项
2. 清理 `ElementOutlineAdapter.cs` 调试代码
3. 修改 `CanvasExportService.cs`：
   - Phase 1：复用 ElementOutlineAdapter
   - Phase 4：转换 + 外墙过滤（新增 FilterExteriorEdges 方法）
   - Phase 6：直接组装到顶层
4. 编译验证

### Step 3: 功能验证

1. 在 Revit 中运行导出命令
2. 检查导出的 JSON 结构：
   - `walls` 包含单独墙体
   - `columns` 包含单独柱子
   - `finishLocationBoundaries` 包含过滤后的内墙边
   - `openings` 包含门窗

### Step 4: 文档同步

1. 更新 `docs/Schema-JSON.md`

---

## 8. JSON 结构预览

```json
{
  "id": "canvas_001",
  "version": 1,
  "coordinateSystem": "cartesian_mm_yUp",
  "metadata": { ... },

  "walls": [
    { "id": "wall_001", "elementId": 12345, "polygon": [[0,0], [6000,0], [6000,200], [0,200]] }
  ],
  "columns": [
    { "id": "col_001", "elementId": 23456, "isStructural": true, "polygon": [[3000,0], [3500,0], [3500,500], [3000,500]] }
  ],
  "openings": [
    { "id": "d1", "type": "door", "line": [[2000,0], [2900,0]] }
  ],
  "finishLocationBoundaries": [
    { "id": "flb_001", "elementIds": [12345, 23456], "polygon": [[...]] }
  ],

  "rooms": [...],
  "zones": [],
  "wallFinishes": [],
  "modules": []
}
```

---

## 9. 用户已确认事项

| 问题 | 用户选择 |
|------|----------|
| 字段命名 | `finishLocationBoundaries` ✅ |
| 外墙过滤时机 | Revit 导出时过滤 ✅ |
| 柱子类型表示 | `bool IsStructural` ✅ |
| Outline 包装类 | 删除，字段提升到顶层 ✅ |
| 代码复用 | 复用 BoundaryAdapter / ElementOutlineAdapter ✅ |
