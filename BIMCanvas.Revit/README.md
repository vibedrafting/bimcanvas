# BIMCanvas.Revit

> **版本**：v1.0
> **更新日期**：2025-12-10
> **状态**：Phase 1 核心导出功能已完成

---

## 一、项目概述

### 1.1 核心定位

**BIMCanvas.Revit 是一个独立运行的 Revit 插件**，通过本地 JSON 文件与 BIMCanvas 系统交换数据。

**核心职责**：
- **数据提取（导出）**：从 Revit 模型提取建筑数据 → 保存为 JSON 文件
- **数据回写（导入）**：读取布置结果 JSON → 创建 Revit 家具实例（Phase 2）
- **UI 交互**：提供 Ribbon 面板、配置窗口、文件对话框

### 1.2 技术栈

| 配置项 | 值 | 说明 |
|--------|-----|------|
| 目标框架 | .NET Framework 4.7.2 | Revit 2019 API 限制 |
| Revit 版本 | Revit 2019 | 用户指定 |
| 依赖项目 | BIMCanvas.Core | .NET Standard 2.0 兼容 |
| UI 框架 | WPF | MVVM 模式 |
| 几何库 | NetTopologySuite 2.6.0 | 中间几何处理 |

---

## 二、项目结构

```
BIMCanvas.Revit/
├── BIMCanvas.Revit.csproj           项目文件
├── BIMCanvas.addin                  Revit 插件注册文件
├── README.md                        本文档
│
├── Commands/                     【命令层】Ribbon 入口
│   ├── App.cs                       IExternalApplication 入口
│   └── ExportCanvasCommand.cs       导出命令
│
├── Adapters/                     【适配器层】数据提取
│   ├── BoundaryAdapter.cs           边界轮廓提取（墙体 + 柱子）
│   ├── OpeningAdapter.cs            门窗数据提取
│   └── RoomAdapter.cs               房间数据提取
│
├── Models/                       【模型层】中间数据结构
│   ├── RevitBoundary.cs             边界中间模型
│   ├── RevitOpening.cs              门窗中间模型
│   └── RevitRoom.cs                 房间中间模型
│
├── Services/                     【服务层】业务逻辑
│   ├── CanvasExportService.cs       导出服务（6阶段流程）
│   ├── CoordinateTransformer.cs     坐标转换器
│   ├── RoomTypeInferrer.cs          房间类型推断
│   └── ExportOptions.cs             导出配置（支持 JSON 配置文件）
│
├── ExportOptions.json            【配置文件】导出配置（随项目输出）
│
├── Utilities/                    【工具层】通用功能
│   ├── OutlineExtractor.cs          轮廓提取（几何切割）
│   ├── OpeningDirectionAnalyzer.cs  门窗方向分析
│   ├── RevitNtsGeometryConverter.cs 类型转换扩展
│   ├── PrefixId.cs                  ID 生成器
│   ├── TransactionHelper.cs         事务处理
│   └── DebugViewer.cs               调试可视化
│
├── Views/                        【视图层】UI
│   ├── ConfigWindow.xaml            配置窗口
│   ├── ConfigWindow.xaml.cs
│   └── ViewModels/
│       ├── ConfigViewModel.cs       MVVM ViewModel
│       └── RelayCommand.cs          命令基类
│
└── Test/                         【测试】
    ├── Test.cs                      通用测试框架
    ├── GetRoomBoundaryTest.cs       房间边界测试
    ├── OpeningInfoTest.cs           门窗信息测试
    └── WallSolidUnionTest.cs        墙体合并测试
```

---

## 三、执行流程

### 3.1 完整数据流

```
【6阶段导出流程】

Phase 1: 提取原始数据
┌─────────────────────────────────────────────────────────────┐
│  Revit API (Wall, Column, Door, Window, Room)               │
│      ↓                                                      │
│  BoundaryAdapter.ExtractBoundaries() → List<RevitBoundary>  │
│  OpeningAdapter.ExtractOpenings()    → List<RevitOpening>   │
│  RoomAdapter.ExtractRooms()          → List<RevitRoom>      │
│      ↓                                                      │
│  NTS 格式 (Polygon, LineSegment) | feet | Revit项目坐标     │
└─────────────────────────────────────────────────────────────┘

Phase 2: 计算包围盒原点
┌─────────────────────────────────────────────────────────────┐
│  所有 NTS Polygon                                           │
│      ↓ Envelope.Union()                                     │
│  origin = (MinX, MinY)  | feet | Revit项目坐标              │
└─────────────────────────────────────────────────────────────┘

Phase 3: 创建坐标转换器
┌─────────────────────────────────────────────────────────────┐
│  new CoordinateTransformer(origin, viewRotation)            │
│      - origin: 原点位置                                      │
│      - rotation: 视图旋转角度（弧度）                         │
└─────────────────────────────────────────────────────────────┘

Phase 4: 统一坐标转换
┌─────────────────────────────────────────────────────────────┐
│  RevitBoundary (NTS Polygon)  → Boundary (Polygon2D, mm)    │
│  RevitOpening (NTS LineSegment) → Opening (Line2D, mm)      │
│  RevitRoom (NTS Polygon)      → Room (Polygon2D, mm)        │
│      ↓                                                      │
│  transformer.TransformPolygon() / TransformLineSegment()    │
│  NtsConverter.FromNtsPolygon() / FromNtsLineSegment()       │
│  归一化坐标系 | mm | 原点左下角                               │
└─────────────────────────────────────────────────────────────┘

Phase 5: 用户确认房间类型
┌─────────────────────────────────────────────────────────────┐
│  RoomTypeInferrer.InferFromName() 自动推断                   │
│      ↓                                                      │
│  ConfigWindow 显示推断结果                                   │
│      ↓                                                      │
│  用户确认/修改房间类型                                        │
└─────────────────────────────────────────────────────────────┘

Phase 6: 组装 CanvasDocument
┌─────────────────────────────────────────────────────────────┐
│  new CanvasDocument {                                       │
│      Id, Version, CoordinateSystem,                         │
│      Metadata: { PlacementElevation, CoordinateTransform }, │
│      Outline: { Boundaries, Openings },                     │
│      Rooms,                                                 │
│      Zones: [],        // 精简版为空                         │
│      WallFinishes: [], // 精简版为空                         │
│      Modules: []       // 精简版为空                         │
│  }                                                          │
│      ↓ JsonConvert.SerializeObject()                        │
│  保存 .json 文件                                             │
└─────────────────────────────────────────────────────────────┘
```

### 3.2 入口和触发

```
Revit 启动
    ↓ App.OnStartup()
创建 Ribbon 面板 "BIMCanvas"
    ↓ 用户点击 "导出画布" 按钮
ExportCanvasCommand.Execute()
    ↓ 检查当前视图是否为 ViewPlan
    ↓ 创建 CanvasExportService
    ↓ 调用 exportService.ExportFromView(view, options)
    ↓ 弹出保存文件对话框
    ↓ 保存 JSON 文件
完成
```

---

## 四、自定义数据结构

### 4.1 Revit 中间模型（Models/）

保留 Revit 原生数据，延迟坐标转换，便于追溯和调试。

#### RevitBoundary

```csharp
public class RevitBoundary
{
    public string Id { get; set; }                // "boundary_001", "boundary_002"
    public List<int> ElementIds { get; set; }     // 构成边界的 Revit 元素 ID
    public Polygon Boundary { get; set; }         // NTS Polygon (feet, 项目坐标)
}
```

#### RevitOpening

```csharp
public class RevitOpening
{
    public string Id { get; set; }                     // "d001", "win001"
    public int ElementId { get; set; }                 // Revit 元素 ID
    public OpeningType Type { get; set; }              // Door | Window
    public Coordinate LocationPoint { get; set; }      // NTS Coordinate (feet)
    public LineSegment LocationLine { get; set; }      // NTS LineSegment (feet)
    public Vector2D FacingDirection { get; set; }      // 面向方向 (单位向量)
    public List<Vector2D> HandDirections { get; set; } // 开启方向 (单位向量列表)
}
```

#### RevitRoom

```csharp
public class RevitRoom
{
    public string Id { get; set; }        // "room_001", "room_002"
    public int ElementId { get; set; }    // Revit Room 元素 ID
    public string Name { get; set; }      // 房间名称
    public Polygon Boundary { get; set; } // NTS Polygon (feet, 项目坐标)
}
```

### 4.2 服务类（Services/）

#### CoordinateTransformer

NTS 几何对象坐标变换器，负责 NTS (feet, 项目坐标系) → NTS (mm, 归一化坐标系) 的变换。

```csharp
public class CoordinateTransformer
{
    private readonly Coordinate _origin;  // 原点 (feet)
    private readonly double _rotation;    // 旋转角度 (弧度)

    // 坐标变换方法（输入 NTS feet，输出 NTS mm）
    public Coordinate TransformCoordinate(Coordinate coord);
    public Polygon TransformPolygon(Polygon ntsPolygon);      // 支持内环
    public LineSegment TransformLineSegment(LineSegment segment);
}
```

**变换流程**（所有方法统一执行）：
```
1. 原点偏移：dx = x - origin.X, dy = y - origin.Y
2. 旋转归一化：localX = dx * cos(-rotation) - dy * sin(-rotation)
                localY = dx * sin(-rotation) + dy * cos(-rotation)
3. 单位转换：x_mm = localX × 304.8, y_mm = localY × 304.8
```

**职责边界**：只做坐标变换，不做类型转换。类型转换由 RevitNtsConverter 和 NtsConverter 负责。

### 4.3 转换器层（Converters/）

#### RevitNtsConverter

Revit API ↔ NTS 类型转换扩展方法（静态，无状态）。

```csharp
public static class RevitNtsConverter
{
    // XYZ ↔ Coordinate
    public static XYZ ToXYZ(this Coordinate coord, double z = 0);
    public static Coordinate ToCoordinate(this XYZ point);

    // Line ↔ LineSegment
    public static Line ToLine(this LineSegment segment, double z = 0);
    public static LineSegment ToLineSegment(this Line line);

    // CurveLoop → Polygon
    public static Polygon ToPolygon(this CurveLoop curveLoop);
}
```

#### ExportOptions

导出配置支持从 JSON 文件加载，配置文件 `ExportOptions.json` 随 DLL 一起输出。

**配置文件格式** (`ExportOptions.json`)：
```json
{
  "showConfigWindow": true,
  "defaultSavePath": null,
  "placementElevation": 0,
  "boundaryCutHeightMm": 100,
  "exportBoundarys": true,
  "exportOpenings": true,
  "exportRooms": true
}
```

**配置项说明**：
| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| showConfigWindow | bool | true | 显示房间类型确认窗口 |
| defaultSavePath | string? | null | 默认保存路径 |
| placementElevation | double | 0 | 布置高度 (mm) |
| boundaryCutHeightMm | double | 100 | 边界切割高度 (mm) |
| exportBoundarys | bool | true | 导出边界 |
| exportOpenings | bool | true | 导出门窗 |
| exportRooms | bool | true | 导出房间 |

**使用方式**：
```csharp
// 从程序集目录加载配置
var options = ExportOptions.Load();

// 从指定路径加载
var options = ExportOptions.LoadFrom("path/to/config.json");

// 保存配置
options.Save();
```

#### RoomTypeInferrer

```csharp
public static class RoomTypeInferrer
{
    public static RoomType InferFromName(string? roomName);
    public static string GetDisplayName(RoomType type);
    public static IEnumerable<RoomType> GetAllTypes();
}
```

**关键词映射表**：

| 关键词 | RoomType |
|--------|----------|
| 客厅/living/起居 | LivingRoom |
| 餐厅/dining | DiningRoom |
| 主卧/master | MasterBedroom |
| 卧室/bedroom/次卧 | Bedroom |
| 书房/study/办公 | Study |
| 厨房/kitchen | Kitchen |
| 卫生间/bathroom/洗手间 | Bathroom |
| 门厅/entrance/玄关 | Entrance |
| 阳台/balcony | Balcony |
| 走廊/corridor/过道 | Corridor |
| 储藏/storage | Storage |

### 4.3 工具类（Utilities/）

#### PrefixId - ID 生成器

```csharp
// 生成带前缀的顺序 ID
PrefixId.Reset("boundary_");
var id1 = PrefixId.NewId("boundary_", 3); // "boundary_001"
var id2 = PrefixId.NewId("boundary_", 3); // "boundary_002"
```

#### TransactionHelper - 事务处理

```csharp
public enum FailureLevel
{
    IgnoreWarningsAndErrors,              // 忽略所有
    LogWarningsAndRollback,               // 记录后回滚
    LogWarningsAndContinueWithRollback,   // 记录继续，错误回滚
    LogWarningsAndThrowException          // 记录后抛异常
}

// 使用方式
using (var trans = new Transaction(doc, "操作名"))
{
    trans.Start();
    trans.IgnoreFailure(FailureLevel.IgnoreWarningsAndErrors);
    // ... 操作代码 ...
    trans.Commit();
}
```

---

## 五、几何数据类型转换链

### 5.1 数据类型演进

```
┌─────────────────────────────────────────────────────────────────┐
│  阶段           数据类型                    单位    坐标系      │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  Revit API      XYZ, Solid, CurveLoop,      feet   项目坐标     │
│                 BoundarySegment                                 │
│       ↓                                                         │
│                                                                 │
│  NTS 中间层     Polygon, LineSegment,       feet   项目坐标     │
│                 Coordinate, Vector2D                            │
│       ↓                                                         │
│                                                                 │
│  Core 层        Polygon2D, Line2D,          mm     归一化坐标   │
│                 Point2D, Facing                                 │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### 5.2 各类型详解

#### Revit API 原生类型

| 类型 | 用途 | 说明 |
|------|------|------|
| `XYZ` | 3D 点/向量 | Revit 基础几何类型 |
| `Solid` | 几何实体 | 墙/柱的实体几何 |
| `CurveLoop` | 封闭曲线环 | 轮廓提取结果 |
| `BoundarySegment` | 房间边界段 | 房间边界组成部分 |
| `FamilyInstance` | 族实例 | 门窗实例 |

#### NTS 中间类型（NetTopologySuite）

| 类型 | 用途 | 说明 |
|------|------|------|
| `Polygon` | 多边形 | 支持外环和内环（孔洞） |
| `LineSegment` | 线段 | 门窗定位线 |
| `Coordinate` | 2D/3D 点 | NTS 基础点类型 |
| `Vector2D` | 2D 向量 | 方向计算用 |

#### BIMCanvas.Core 最终类型

| 类型 | 用途 | JSON 表示 |
|------|------|-----------|
| `Polygon2D` | 多边形 | `[[x1,y1], [x2,y2], ...]` |
| `Line2D` | 线段 | `{ "start": {...}, "end": {...} }` |
| `Point2D` | 点 | `{ "x": 100, "y": 200 }` |
| `Facing` | 朝向 | `"north"` 或 `[0.707, 0.707]` |

### 5.3 转换函数映射

| 源类型 | 目标类型 | 转换方法 |
|--------|----------|----------|
| `XYZ` | `Coordinate` | `point.ToCoordinate()` |
| `CurveLoop` | `Polygon` | `curveLoop.ToPolygon()` |
| `Polygon` (feet) | `Polygon` (mm) | `transformer.TransformPolygon()` |
| `LineSegment` (feet) | `LineSegment` (mm) | `transformer.TransformLineSegment()` |
| `Polygon` (NTS) | `Polygon2D` | `NtsConverter.FromNtsPolygon()` |
| `LineSegment` (NTS) | `Line2D` | `NtsConverter.FromNtsLineSegment()` |
| `XYZ` (向量) | `Vector2D` | `xyz.ToVector2D()` |

### 5.4 转换器分层架构（必须严格遵守）

```
转换链路：
Revit API ↔ NTS              (BIMCanvas.Revit/Converters/RevitNtsConverter)
     ↓
NTS (feet) → NTS (mm)        (BIMCanvas.Revit/Services/CoordinateTransformer)
     ↓
NTS ↔ Core.Models            (BIMCanvas.Core/Converters/NtsConverter)

⛔ 禁止：Revit 层直接输出 Core.Models 几何类型
```

| 转换器 | 位置 | 职责 | 特点 |
|--------|------|------|------|
| `RevitNtsConverter` | Revit/Converters | Revit API ↔ NTS 类型转换 | 静态扩展方法，无状态 |
| `CoordinateTransformer` | Revit/Services | 坐标变换（原点偏移+旋转+单位） | 实例类，有状态 |
| `NtsConverter` | Core/Converters | NTS ↔ Core.Models 类型转换 | 静态类，无状态 |

---

## 六、关键算法

### 6.1 边界轮廓提取（OutlineExtractor）

在指定高度切割建筑构件几何体，提取轮廓：

```
选中构件类别 (墙、柱)
    ↓
合并所有 Solid (BooleanOperationsUtils.Union)
    ↓
在 Z=1200mm 高度创建切割平面 (法向量朝下)
    ↓
执行 CutWithHalfSpace (保留下方部分)
    ↓
遍历切割后 Solid 的所有 Face
    ├─ 检查是否为 PlanarFace 且法向量朝上 (Z > 0.9)
    ├─ 检查 Face 高度是否 ≈ 1200mm
    └─ 提取 Face 的所有 EdgeLoop
    ↓
输出：List<CurveLoop>，每个代表一个封闭轮廓
```

### 6.2 门窗开启方向分析（OpeningDirectionAnalyzer）

通过 IFC 导出工具获取门的开启弧线，计算开启方向：

```
FamilyInstance (门)
    ↓ ExporterIFCUtils.GetDoor2DArcsFromFamily()
获取开启弧线列表
    ↓ 应用坐标变换 (GetDoorInstanceTransformWithFlipping)
世界坐标系中的弧线
    ↓
对每条弧线：
    ├─ 计算垂直于面向方向的左右向量
    ├─ 获取弧线端点和圆心
    ├─ 通过点积判断关闭位置
    └─ 计算开启方向向量
    ↓
判断单开/双开：
    ├─ 半径相等 → 双开门
    └─ 半径不等 → 子母门（取大半径弧线）
    ↓
输出：(FacingDirection, List<HandDirections>)
```

---

## 七、依赖关系

### 7.1 项目依赖

```
BIMCanvas.Revit (.NET Framework 4.7.2)
│
├─ BIMCanvas.Core (.NET Standard 2.0)
│  └─ 数据模型：CanvasDocument, Boundary, Opening, Room 等
│
├─ Revit API (Revit 2019)
│  ├─ RevitAPI.dll
│  ├─ RevitAPIUI.dll
│  └─ Revit.IFC.Export.dll (用于门的弧线提取)
│
└─ NetTopologySuite (2.6.0)
   └─ 几何库：Polygon, LineSegment, Coordinate, Vector2D
```

### 7.2 BIMCanvas.addin 配置

```xml
<RevitAddIns>
  <AddIn Type="Application">
    <Name>BIMCanvas</Name>
    <Assembly>BIMCanvas.Revit.dll</Assembly>
    <FullClassName>BIMCanvas.Revit.Commands.App</FullClassName>
    <ClientId>3A8E5F2D-1B4C-4D6E-9F0A-7C2B8E4D3F1A</ClientId>
  </AddIn>
</RevitAddIns>
```

---

## 八、开发状态

### 8.1 已完成 (Phase 1)

| 功能 | 文件 | 状态 |
|------|------|------|
| Ribbon 面板注册 | Commands/App.cs | ✅ |
| 导出命令入口 | Commands/ExportCanvasCommand.cs | ✅ |
| 边界轮廓提取 | Adapters/BoundaryAdapter.cs | ✅ |
| 门窗数据提取 | Adapters/OpeningAdapter.cs | ✅ |
| 房间数据提取 | Adapters/RoomAdapter.cs | ✅ |
| 坐标转换器 | Services/CoordinateTransformer.cs | ✅ |
| 房间类型推断 | Services/RoomTypeInferrer.cs | ✅ |
| 导出服务 | Services/CanvasExportService.cs | ✅ |
| 配置窗口 | Views/ConfigWindow.xaml | ✅ |
| 轮廓提取工具 | Utilities/OutlineExtractor.cs | ✅ |
| 门窗方向分析 | Utilities/OpeningDirectionAnalyzer.cs | ✅ |

### 8.2 待开发 (Phase 2)

| 功能 | 文件 | 状态 |
|------|------|------|
| 布置应用服务 | Services/LayoutApplyService.cs | ⬜ |
| 应用命令 | Commands/ApplyLayoutCommand.cs | ⬜ |
| 族加载逻辑 | Services/FamilyLoader.cs | ⬜ |

---

## 九、使用指南

### 9.1 安装

1. 编译项目生成 `BIMCanvas.Revit.dll`
2. 将 `BIMCanvas.addin` 复制到 Revit 插件目录：
   - `%AppData%\Autodesk\Revit\Addins\2019\`
3. 修改 `BIMCanvas.addin` 中的 Assembly 路径指向 dll 位置
4. 启动 Revit 2019

### 9.2 使用

1. 打开包含房间的 Revit 项目
2. 切换到平面视图
3. 点击 Ribbon 面板中的 "导出画布" 按钮
4. 在弹出的配置窗口中确认房间类型
5. 选择保存路径，导出 JSON 文件

### 9.3 输出示例

```json
{
  "id": "canvas_abc123...",
  "version": 1,
  "coordinateSystem": "cartesian_mm_yUp",
  "metadata": {
    "placementElevation": 0,
    "coordinateTransform": {
      "origin": [1000.5, 2000.3, 0],
      "rotation": 0,
      "method": "boundingBox"
    }
  },
  "outline": {
    "boundarys": [
      { "id": "boundary_001", "polygon": [[0,0], [5000,0], [5000,4000], [0,4000]] }
    ],
    "openings": [
      { "id": "d001", "type": "door", "line": { "start": {"x":2000,"y":0}, "end": {"x":2900,"y":0} } }
    ]
  },
  "rooms": [
    { "id": "room_001", "name": "客厅", "type": "livingRoom", "boundary": [[...]] }
  ],
  "zones": [],
  "wallFinishes": [],
  "modules": []
}
```

---

## 十、相关文档

| 文档 | 路径 | 内容 |
|------|------|------|
| 系统架构 | `docs/Architecture.md` | 完整系统架构 |
| JSON Schema | `docs/Schema-JSON.md` | 数据模型定义 |
| 实施计划 | `plans/Revit_Implementation_Plan.md` | 开发计划和验收标准 |
| 执行流程 | `docs/Workflows.md` | 端到端工作流程 |
