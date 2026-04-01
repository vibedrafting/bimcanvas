# BIMCanvas.Revit

> **版本**：v3.0
> **更新日期**：2026-03-05
> **状态**：Phase 1 核心导出功能已完成（支持 v3.0 .bcp 格式）

---

## 一、项目概述

### 1.1 核心定位

**BIMCanvas.Revit 是一个独立运行的 Revit 插件**，通过 `.bcp` 压缩包与 BIMCanvas 系统交换数据。

**核心职责**：
- **数据提取（导出）**：从 Revit 模型提取建筑数据 → 保存为 `.bcp` 压缩包（v3.0 格式）
- **数据回写（导入）**：读取布置结果 JSON → 创建 Revit 家具实例（Phase 2，待开发）
- **UI 交互**：提供 Ribbon 面板、配置窗口、文件对话框

### 1.2 v3.0 变更说明

| 变更项 | v2.9 (旧) | v3.0 (新) |
|--------|-----------|-----------|
| 输出格式 | 单一 `.json` 文件 | `.bcp` 压缩包（多文件夹结构） |
| 数据结构 | `DesignDocument` 单一对象 | `baseline/` 多文件 + `project.json` |
| 策略创建 | Revit 创建 | **Server 层负责** |
| 新增数据 | - | `LocationLine` 定位线 |

### 1.3 技术栈

| 配置项 | 值 | 说明 |
|--------|-----|------|
| 目标框架 | .NET Framework 4.7.2 | Revit 2019 API 限制 |
| Revit 版本 | Revit 2019 | 用户指定 |
| 依赖项目 | BIMCanvas.Core | .NET Standard 2.0 兼容 |
| UI 框架 | WPF | MVVM 模式 |
| 几何库 | NetTopologySuite 2.6.0 | 中间几何处理 |

> 构建约定：Revit 2019 API 引用统一使用仓库内相对路径 `../libs/revit/2019/`，避免绑定开发机绝对路径。

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
│   └── ExportCanvasCommand.cs       导出命令（v3.0 .bcp 格式）
│
├── Converters/                   【转换器层】类型转换
│   └── RevitNtsConverter.cs       Revit API ↔ NTS 类型转换
│
├── Adapters/                     【适配器层】数据提取
│   ├── BoundaryAdapter.cs           墙/柱单独轮廓提取
│   ├── WallFinishAdapter.cs         完成面定位边界提取
│   ├── OpeningAdapter.cs            门窗数据提取
│   ├── RoomAdapter.cs               房间数据提取
│   └── LocationLineAdapter.cs       【v3.0 新增】定位线提取
│
├── Models/                       【模型层】中间数据结构
│   ├── RevitWall.cs                 墙体轮廓中间模型
│   ├── RevitColumn.cs               柱子轮廓中间模型
│   ├── RevitWallFinish.cs           完成面定位边界中间模型
│   ├── RevitOpening.cs              门窗中间模型
│   └── RevitRoom.cs                 房间中间模型
│
├── Services/                     【服务层】业务逻辑
│   ├── CanvasExportService.cs       导出服务（v3.0 多文件结构）
│   ├── BcpExporter.cs               【v3.0 新增】.bcp 压缩包导出
│   ├── CoordinateTransformer.cs     坐标转换器
│   ├── RoomTypeInferrer.cs          房间类型推断
│   └── ExportOptions.cs             导出配置
│
├── ExportOptions.json            【配置文件】导出配置
│
├── Utilities/                    【工具层】通用功能
│   ├── OutlineExtractor.cs          轮廓提取（几何切割）
│   ├── OpeningDirectionAnalyzer.cs  门窗方向分析
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
    └── ...
```

---

## 三、v3.0 导出流程

### 3.1 导出文件结构

Revit 导出生成的 `.bcp` 压缩包结构：

```
{项目名}.bcp (ZIP 压缩包)
├── project.json                    # 项目入口（Schemes 为空，由 Server 填充）
└── baseline/
    ├── metadata.json               # 坐标变换参数 + BaselineHash
    ├── architecture.json           # 墙体 + 柱子
    ├── openings.json               # 门窗
    ├── rooms.json                  # 房间
    └── location_lines.json         # 【v3.0 新增】完成面定位线
```

> **职责分离**：`schemes/` 和 `context/` 由 Server 层在项目打开时创建，Revit 只负责导出原始建筑数据。

### 3.2 完整数据流

```
【6阶段导出流程 - v3.0】

Phase 1: 提取原始数据
┌─────────────────────────────────────────────────────────────┐
│  Revit API (Wall, Column, Door, Window, Room)               │
│      ↓                                                      │
│  BoundaryAdapter.ExtractBoundaries()                        │
│      → (List<RevitWall>, List<RevitColumn>)                 │
│  WallFinishAdapter.ExtractWallFinishes()                    │
│      → List<RevitWallFinish>                                │
│  OpeningAdapter.ExtractOpenings()  → List<RevitOpening>     │
│  RoomAdapter.ExtractRooms()        → List<RevitRoom>        │
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

Phase 4: 统一坐标转换 + 定位线提取
┌─────────────────────────────────────────────────────────────┐
│  RevitWall → Wall (Polygon2D, mm)                           │
│  RevitColumn → Column (Polygon2D, mm)                       │
│  RevitWallFinish → LocationLine【v3.0 新增】                │
│  RevitOpening → Opening (Line2D, mm)                        │
│  RevitRoom → Room (Polygon2D, mm)                           │
│      ↓ FilterExteriorEdges()                                │
│  过滤外墙边（只保留与房间关联的内侧定位线）                    │
│      ↓ LocationLineAdapter.ExtractLocationLines()           │
│  完成面定位边界 → LocationLine[]                             │
│      ↓                                                      │
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

Phase 6: 组装并导出 .bcp【v3.0 变更】
┌─────────────────────────────────────────────────────────────┐
│  BcpExporter.ExportToBcp(outputPath, ...)                   │
│      ↓ 创建临时目录                                          │
│  写入 project.json (Schemes 为空)                            │
│  写入 baseline/metadata.json (含 BaselineHash)              │
│  写入 baseline/architecture.json (walls + columns)          │
│  写入 baseline/openings.json                                │
│  写入 baseline/rooms.json                                   │
│  写入 baseline/location_lines.json                          │
│      ↓ ZipFile.CreateFromDirectory()                        │
│  输出 .bcp 压缩包                                            │
└─────────────────────────────────────────────────────────────┘
```

### 3.3 BcpExporter（v3.0 新增）

负责将数据导出为 `.bcp` 压缩包：

```csharp
public class BcpExporter
{
    /// <summary>
    /// 导出 .bcp 格式（多文件夹 + ZIP 打包）
    /// </summary>
    public void ExportToBcp(
        string outputPath,           // 输出路径（不含扩展名）
        string projectName,          // 项目名称
        BaselineManifest manifest,   // 元数据（含 BaselineHash）
        Architecture architecture,   // 墙体 + 柱子
        List<Opening> openings,      // 门窗
        List<Room> rooms,            // 房间
        List<LocationLine> locationLines  // 定位线
    );
}
```

### 3.4 LocationLineAdapter（v3.0 新增）

从墙面完成面边界提取定位线：

```csharp
public class LocationLineAdapter
{
    /// <summary>
    /// 从完成面边界提取定位线，关联墙体和房间
    /// </summary>
    public List<LocationLine> ExtractLocationLines(
        List<RevitWallFinish> filteredWallFinishes,
        List<RevitRoom> revitRooms,
        List<RevitWall> revitWalls    // 用于查找所属墙体
    );
}
```

---

## 四、导出数据结构

### 4.1 project.json

```json
{
  "id": "proj_abc123",
  "name": "金凤127",
  "version": "3.0",
  "createdAt": "2025-12-25T10:30:00+08:00",
  "modifiedAt": "2025-12-25T10:30:00+08:00",
  "schemes": [],
  "activeSchemeId": null
}
```

### 4.2 baseline/metadata.json

```json
{
  "placementElevation": 0,
  "origin": [1000.5, 2000.3, 0],
  "rotation": 0,
  "method": "boundingBox",
  "baselineHash": "sha256:abc123..."
}
```

### 4.3 baseline/architecture.json

```json
{
  "walls": [
    {
      "id": "w_1",
      "elementId": 12345,
      "isStructural": false,
      "polygon": [[0, 0], [5000, 0], [5000, 200], [0, 200]]
    }
  ],
  "columns": [
    {
      "id": "c_1",
      "elementId": 23456,
      "isStructural": true,
      "polygon": [[2500, 0], [2700, 0], [2700, 400], [2500, 400]]
    }
  ]
}
```

### 4.4 baseline/location_lines.json（v3.0 新增）

```json
[
  {
    "id": "ll_1",
    "wallId": "w_1",
    "roomId": "r_1",
    "side": "interior",
    "line": [[0, 200], [5000, 200]],
    "length": 5000
  }
]
```

---

## 五、导出配置

### 5.1 ExportOptions.json

配置文件位置：`BIMCanvas.Revit/ExportOptions.json`

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| showConfigWindow | bool | true | 显示房间类型确认窗口 |
| defaultSavePath | string | null | 默认保存路径 |
| placementElevation | double | 0 | 布置基准高度 (mm) |
| boundaryCutHeightMm | double | 2000 | 边界轮廓切割高度 (mm) |
| wallFinishCutHeightMm | double | 200 | 完成面切割高度 (mm) |
| exportBoundarys | bool | true | 导出墙柱轮廓 |
| exportOpenings | bool | true | 导出门窗数据 |
| exportRooms | bool | true | 导出房间数据 |
| exportElementOutlines | bool | true | 导出单构件轮廓 |

---

## 六、坐标转换

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

### 5.2 CoordinateTransformer

```csharp
public class CoordinateTransformer
{
    private readonly Coordinate _origin;  // 原点 (feet)
    private readonly double _rotation;    // 旋转角度 (弧度)

    // 坐标变换方法（输入 NTS feet，输出 NTS mm）
    public Coordinate TransformCoordinate(Coordinate coord);
    public Polygon TransformPolygon(Polygon ntsPolygon);
    public LineSegment TransformLineSegment(LineSegment segment);
    public Vector2D TransformVector2D(Vector2D vector);  // 仅旋转，不平移（用于门窗方向向量）
}
```

**变换流程**：
```
1. 原点偏移：dx = x - origin.X, dy = y - origin.Y
2. 旋转归一化：localX = dx * cos(-rotation) - dy * sin(-rotation)
                localY = dx * sin(-rotation) + dy * cos(-rotation)
3. 单位转换：x_mm = localX × 304.8, y_mm = localY × 304.8
```

---

## 七、开发状态

### 6.1 已完成 (Phase 1)

| 功能 | 文件 | 状态 |
|------|------|------|
| Ribbon 面板注册 | Commands/App.cs | ✅ |
| 导出命令入口 | Commands/ExportCanvasCommand.cs | ✅ v3.0 |
| 墙/柱轮廓提取 | Adapters/BoundaryAdapter.cs | ✅ |
| 完成面定位边界提取 | Adapters/WallFinishAdapter.cs | ✅ |
| 定位线提取 | Adapters/LocationLineAdapter.cs | ✅ v3.0 新增 |
| 门窗数据提取 | Adapters/OpeningAdapter.cs | ✅ |
| 房间数据提取 | Adapters/RoomAdapter.cs | ✅ |
| 坐标转换器 | Services/CoordinateTransformer.cs | ✅ |
| 房间类型推断 | Services/RoomTypeInferrer.cs | ✅ |
| 导出服务 | Services/CanvasExportService.cs | ✅ v3.0 重构 |
| .bcp 导出器 | Services/BcpExporter.cs | ✅ v3.0 新增 |
| 配置窗口 | Views/ConfigWindow.xaml | ✅ |

### 6.2 待开发 (Phase 2)

| 功能 | 文件 | 状态 |
|------|------|------|
| 布置应用服务 | Services/LayoutApplyService.cs | ⬜ |
| 应用命令 | Commands/ApplyLayoutCommand.cs | ⬜ |
| 族加载逻辑 | Services/FamilyLoader.cs | ⬜ |

---

## 八、命名空间冲突处理

在 Revit 层使用别名解决与 Revit API 的类型冲突：

```csharp
using CoreWall = BIMCanvas.Core.Models.Revit.Wall;
using CoreColumn = BIMCanvas.Core.Models.Revit.Column;
using CoreOpening = BIMCanvas.Core.Models.Revit.Opening;
using CoreRoom = BIMCanvas.Core.Models.Revit.Room;
using CoreArchitecture = BIMCanvas.Core.Models.Revit.Architecture;
using CoreLocationLine = BIMCanvas.Core.Models.Revit.LocationLine;
```

---

## 九、使用指南

### 8.1 安装

1. 编译项目生成 `BIMCanvas.Revit.dll`
2. 将 `BIMCanvas.addin` 复制到 Revit 插件目录：
   - `%AppData%\Autodesk\Revit\Addins\2019\`
3. 修改 `BIMCanvas.addin` 中的 Assembly 路径指向 dll 位置
4. 启动 Revit 2019

### 8.2 使用

1. 打开包含房间的 Revit 项目
2. 切换到平面视图
3. 点击 Ribbon 面板中的 "导出画布" 按钮
4. 在弹出的配置窗口中确认房间类型
5. 选择保存路径，导出 `.bcp` 文件

### 8.3 输出示例

v3.0 导出 `.bcp` 压缩包，解压后结构：

```
金凤127.bcp/
├── project.json
└── baseline/
    ├── metadata.json
    ├── architecture.json
    ├── openings.json
    ├── rooms.json
    └── location_lines.json
```

---

## 十、相关文档

| 文档 | 路径 | 内容 |
|------|------|------|
| 系统架构 | `docs/Architecture.md` | 完整系统架构 |
| JSON Schema v3 | `docs/Schema-JSON-v3.md` | v3.0 数据模型定义 |
| 文件驱动架构 | `docs/FileDrivenArchitecture.md` | "文件播放器"模式 |
| 升级进度 | `plans/V3_Upgrade_Progress_Report.md` | v3.0 升级进度 |
| 升级计划 | `plans/V3_Architecture_Upgrade_Plan.md` | 完整升级计划 |
