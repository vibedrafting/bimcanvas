# BIMCanvas.Revit 实施计划

> **版本**：v1.2
> **更新日期**：2025-12-06
> **状态**：Phase 1 待开发

---

## 一、项目概述

### 1.1 核心定位

**BIMCanvas.Revit 是一个独立运行的 Revit 插件**

BIMCanvas.Revit 本身是一个完整的、可独立运行的 Revit 插件，通过本地 JSON 文件与其他系统交换数据。MCP 只是后期阶段让 AI Agent 调用该插件功能的一种手段，不是核心依赖。

**核心职责**：
- **数据提取（导出）**：从 Revit 模型提取原始建筑数据 → 保存为 JSON 文件
- **数据回写（导入）**：读取布置结果 JSON → 创建 Revit 家具实例
- **UI 交互**：提供 Ribbon 面板、配置窗口、文件对话框

### 1.2 职责边界

#### ✅ BIMCanvas.Revit 负责

| 功能类别 | 具体内容 | 输入 | 输出 |
|----------|----------|------|------|
| **边界提取** | 提取平面视图中的边界轮廓（墙体 + 柱子） | Revit Wall/Column 元素 | `Boundary[]` |
| **门窗提取** | 提取门窗的定位线段 | Revit Door/Window | `Line2D[]` + type |
| **房间提取** | 提取房间边界和名称 | Revit Room 元素 | `Room[]` |
| **类型推断** | 基于名称关键词推断 RoomType | 房间名称 | RoomType 枚举 |
| **用户确认** | 导出前确认房间类型 | 推断结果 | 用户确认结果 |
| **坐标转换** | Revit ↔ BIMCanvas 坐标系 | XYZ (feet) | Point2D (mm) |
| **JSON 导出** | 序列化并保存文件 | CanvasDocument | .json 文件 |
| **布置回写** | 解析 modules 创建家具 | .json 文件 | FamilyInstance |

#### ❌ BIMCanvas.Revit 不负责

| 功能 | 负责方 | 原因 |
|------|--------|------|
| Zone 划分计算 | Server.ZoneCalculator | 需要项目配置（完成面厚度等） |
| InnerBoundary 计算 | Server.ZoneCalculator | 复杂几何逻辑集中管理 |
| ExclusionArea 计算 | Server.ZoneCalculator | 门扇开启角度等参数 |
| WallFinish 生成 | Server.ZoneCalculator | 需要项目全局配置 |
| 碰撞检测 | Core.CollisionDetector | 算法复用 |
| AI 布置规划 | BIMCanvas.Agent | Agent SDK 独立进程 |
| JSON→SVG 渲染 | BIMCanvas.Web | 前端职责 |

### 1.3 系统中的位置

```
BIMCanvas 系统架构
├── BIMCanvas.Core (.NET Standard 2.0)   ← 数据模型 + 算法库
├── BIMCanvas.Revit (.NET FW 4.7.2)      ← 本项目：Revit 插件
├── BIMCanvas.Server (.NET 6+)           ← 后端服务
├── BIMCanvas.Agent (Python 3.10+)       ← AI 布置代理
└── BIMCanvas.Web (Vue 3 + TS)           ← Web 前端
```

### 1.4 数据流

```
【导出流程】
Revit 模型
    ↓ [BIMCanvas.Revit: 提取原始数据]
    ↓ [弹出 ConfigWindow: 用户确认房间类型]
精简版 CanvasDocument (无 Zone)
    ↓ [用户选择保存路径 → 保存 .json 文件]
本地 .json 文件
    ↓ [BIMCanvas.Server: 读取文件]
    ↓ [ZoneCalculator: 计算 Zone/InnerBoundary/ExclusionAreas]
完整版 CanvasDocument
    ↓ [BIMCanvas.Web: 显示画布]
    ↓ [用户/AI 交互布置]
更新后的 CanvasDocument (含 modules)
    ↓ [保存 .json 文件]

【回写流程】
BIMCanvas.Revit
    ↓ [用户选择 .json 文件]
    ↓ [LayoutApplyService: 解析 modules]
    ↓ [计算坐标 + 加载族 + 创建实例]
Revit FamilyInstance
```

### 1.5 输出格式

Revit 层输出**精简版 CanvasDocument**（zones/wallFinishes/modules 为空）：

```json
{
  "id": "canvas_xxx",
  "version": 1,
  "coordinateSystem": "cartesian_mm_yUp",
  "metadata": { "placementElevation": 0 },
  "outline": {
    "boundaries": [[x1,y1], [x2,y2], ...],
    "openings": [{ "line": [[x1,y1], [x2,y2]], "type": "door" }, ...]
  },
  "rooms": [
    { "id": "room_1", "name": "客厅", "type": "LivingRoom", "boundary": [...] }
  ],
  "zones": [],
  "wallFinishes": [],
  "modules": []
}
```

---

## 二、功能规格

### 2.1 Phase 1：导出功能（核心）

#### 2.1.1 CoordinateAdapter - 坐标系转换

**职责**：Revit XYZ (feet, 项目坐标系) ↔ BIMCanvas Point2D (mm, 视图坐标系)

```csharp
public class CoordinateAdapter
{
    private readonly XYZ _viewOrigin;       // 视图裁剪框左下角（世界坐标）
    private readonly double _viewRotation;  // 视图旋转角度（弧度）

    public CoordinateAdapter(View view);

    // Revit XYZ → BIMCanvas Point2D
    public Point2D ToPoint2D(XYZ revitPoint);

    // BIMCanvas Point2D → Revit XYZ
    public XYZ ToXYZ(Point2D point, double elevation = 0);
}
```

**转换流程**：
1. 计算相对于视图原点的偏移
2. 应用视图旋转（如果有）
3. 单位转换：feet ↔ mm（调用 Core.UnitConverter）

#### 2.1.2 Metadata 构建

**职责**：构建画布元数据（只包含布置高度）

```csharp
// 在 CanvasExportService 中直接构建
var metadata = new Metadata
{
    PlacementElevation = 0  // 暂时使用固定值 0mm（地面高度）
};
```

**说明**：
- 当前阶段只考虑单层精装平面布置
- 布置高度暂时固定为 0mm（地面高度）
- 回写时用于确定家具实例的 Z 坐标

#### 2.1.3 BoundaryAdapter - 边界轮廓提取

**职责**：从平面视图提取边界轮廓多边形（包括墙体和柱子）

```csharp
public class BoundaryAdapter
{
    public BoundaryAdapter(CoordinateAdapter coordAdapter);

    // 提取视图中所有边界的轮廓（墙体 + 柱子）
    public List<Boundary> ExtractBoundaries(View view);
}
```

**提取内容**：
- 墙体（Wall）
- 结构柱（StructuralColumns）

**输出**：封闭多边形列表，用于 `outline.boundaries`

#### 2.1.4 OpeningAdapter - 门窗线段提取

**职责**：从视图提取门窗的定位线段

```csharp
public class OpeningAdapter
{
    public OpeningAdapter(CoordinateAdapter coordAdapter);

    // 提取视图中所有门窗的定位线段
    public List<Opening> ExtractOpenings(View view);
}

public class Opening
{
    public Line2D Line { get; set; }
    public string Type { get; set; }  // "door" | "window"
}
```

**输出**：线段列表，用于 `outline.openings`

#### 2.1.5 RoomAdapter - 房间边界提取

**职责**：从视图提取房间边界和名称

```csharp
public class RoomAdapter
{
    public RoomAdapter(CoordinateAdapter coordAdapter);

    // 提取视图中所有房间
    public List<Room> ExtractRooms(View view);
}
```

**输出**：房间列表，包含边界和名称

#### 2.1.6 RoomTypeInferrer - 房间类型推断

**职责**：基于房间名称关键词自动推断 RoomType

```csharp
public static class RoomTypeInferrer
{
    // 根据名称关键词推断房间类型
    public static RoomType InferFromName(string roomName);
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

#### 2.1.7 ConfigWindow - 房间类型确认界面

**职责**：导出前让用户确认/修改房间类型

**界面元素**：
- 房间列表（显示名称 + 推断类型）
- 类型下拉框（RoomType 枚举）
- 确认/取消按钮

**交互流程**：
1. 自动推断所有房间类型
2. 弹出 ConfigWindow 显示推断结果
3. 用户可修改任意房间的类型
4. 用户点击确认后继续导出

#### 2.1.8 CanvasExportService - 导出服务

**职责**：组装 CanvasDocument 并保存文件

```csharp
public class CanvasExportService
{
    public CanvasDocument ExportFromView(View view, ExportOptions options);
}
```

**导出流程**：
```
1. 创建 CoordinateAdapter
2. 构建 Metadata（PlacementElevation = 0）
3. 调用 BoundaryAdapter.ExtractBoundaries()
4. 调用 OpeningAdapter.ExtractOpenings()
5. 调用 RoomAdapter.ExtractRooms()
6. 调用 RoomTypeInferrer 推断房间类型
7. 弹出 ConfigWindow 用户确认
8. 应用用户确认的房间类型
9. 组装精简版 CanvasDocument
10. 弹出保存对话框
11. JsonConvert.SerializeObject() 保存文件
```

#### 2.1.9 ExportCanvasCommand - 导出命令

**职责**：Revit Ribbon 按钮入口

```csharp
[Transaction(TransactionMode.ReadOnly)]
public class ExportCanvasCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements);
}
```

### 2.2 Phase 2：回写功能

#### 2.2.1 LayoutApplyService - 布置应用服务

**职责**：解析 JSON 文件中的 modules，创建 Revit 家具实例

```csharp
public class LayoutApplyService
{
    public void ApplyFromJson(string jsonPath);
    private void PlaceItem(Module module, ModuleItem item, ElementId levelId);
}
```

**回写流程**：
```
1. 反序列化 CanvasDocument
2. 遍历 modules[].items
3. 对每个 item：
   a. 计算世界坐标 = bounds 中心 + item.offset
   b. 转换朝向角度 = Facing.ToAngleRadians()
   c. 转换坐标单位 = mm → feet
   d. 加载族 = LoadFamilySymbol(familyId)
   e. 创建实例 = doc.Create.NewFamilyInstance()
4. 提交事务
```

#### 2.2.2 ApplyLayoutCommand - 应用命令

**职责**：Revit Ribbon 按钮入口

```csharp
[Transaction(TransactionMode.Manual)]
public class ApplyLayoutCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements);
}
```

### 2.3 Phase 3：MCP 集成（后期阶段）

> **当前状态**：暂不实现，待核心功能稳定后考虑

将核心功能封装为 MCP 工具：

| MCP 工具 | 对应功能 |
|----------|----------|
| `canvas_export` | CanvasExportService.ExportFromView() |
| `canvas_apply` | LayoutApplyService.ApplyFromJson() |

---

## 三、技术设计

### 3.1 项目配置

| 配置项 | 值 | 说明 |
|--------|-----|------|
| 目标框架 | .NET Framework 4.7.2 | Revit 2019 API 限制 |
| Revit 版本 | Revit 2019 | 用户指定 |
| 依赖项目 | BIMCanvas.Core | .NET Standard 2.0 兼容 |
| UI 框架 | WPF | MVVM 模式 |

### 3.2 csproj 配置

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <LangVersion>8.0</LangVersion>
    <Nullable>enable</Nullable>
    <UseWPF>true</UseWPF>
    <AssemblyName>BIMCanvas.Revit</AssemblyName>
    <RootNamespace>BIMCanvas.Revit</RootNamespace>
  </PropertyGroup>

  <!-- Revit 2019 API 引用 -->
  <PropertyGroup>
    <RevitApiPath Condition="'$(RevitApiPath)' == ''">C:\Program Files\Autodesk\Revit 2019</RevitApiPath>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="RevitAPI">
      <HintPath>$(RevitApiPath)\RevitAPI.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="RevitAPIUI">
      <HintPath>$(RevitApiPath)\RevitAPIUI.dll</HintPath>
      <Private>False</Private>
    </Reference>
  </ItemGroup>

  <!-- BIMCanvas.Core 引用 -->
  <ItemGroup>
    <ProjectReference Include="..\BIMCanvas.Core\BIMCanvas.Core.csproj" />
  </ItemGroup>
</Project>
```

### 3.3 模块结构

```
BIMCanvas.Revit/
├── BIMCanvas.Revit.csproj
├── Properties/
│   └── AssemblyInfo.cs
├── Adapters/                         【适配器层】
│   ├── CoordinateAdapter.cs             坐标系转换
│   ├── BoundaryAdapter.cs               边界轮廓提取（墙体 + 柱子）
│   ├── OpeningAdapter.cs                门窗线段提取
│   └── RoomAdapter.cs                   房间边界提取
├── Services/                         【服务层】
│   ├── CanvasExportService.cs           画布导出
│   ├── LayoutApplyService.cs            布置应用
│   ├── RoomTypeInferrer.cs              房间类型推断
│   └── ExportOptions.cs                 导出配置
├── Commands/                         【命令层】
│   ├── App.cs                           IExternalApplication
│   ├── ExportCanvasCommand.cs           导出命令
│   └── ApplyLayoutCommand.cs            应用命令
└── Views/                            【UI 层】
    ├── ConfigWindow.xaml                配置窗口
    ├── ConfigWindow.xaml.cs
    └── ViewModels/
        └── ConfigViewModel.cs           MVVM ViewModel
```

### 3.4 依赖的 Core 组件

| Core 组件 | 用途 |
|-----------|------|
| `UnitConverter.ToMillimeters()` | feet → mm 转换 |
| `UnitConverter.ToFeet()` | mm → feet 转换 |
| `Facing.ToAngleRadians()` | 朝向 → 弧度角 |
| `GeometryHelper.ComputeCenter()` | 计算 bounds 中心点 |
| `CanvasDocument` 模型 | 数据结构定义 |
| `Room`, `Polygon2D`, `Line2D` | 几何类型 |

---

## 四、实施计划

### 4.1 开发阶段

#### Phase 1：核心导出功能

| 步骤 | 任务 | 产出 |
|------|------|------|
| 1.1 | 项目初始化 + Revit API 引用 | csproj + AssemblyInfo |
| 1.2 | CoordinateAdapter 实现 | 坐标转换功能 |
| 1.3 | BoundaryAdapter 实现 | 边界轮廓提取（墙体 + 柱子） |
| 1.4 | OpeningAdapter 实现 | 门窗线段提取 |
| 1.5 | RoomAdapter 实现 | 房间边界提取 |
| 1.6 | RoomTypeInferrer 实现 | 类型推断逻辑 |
| 1.7 | ConfigWindow + ViewModel | 用户确认界面 |
| 1.8 | CanvasExportService 实现 | 导出服务（含 Metadata 构建） |
| 1.9 | App + ExportCanvasCommand | Ribbon 集成 |
| 1.10 | 集成测试 | 完整导出流程验证 |

#### Phase 2：回写功能

| 步骤 | 任务 | 产出 |
|------|------|------|
| 2.1 | LayoutApplyService 实现 | 布置应用服务 |
| 2.2 | ApplyLayoutCommand 实现 | Ribbon 按钮 |
| 2.3 | 族加载逻辑 | FamilySymbol 管理 |
| 2.4 | 事务管理 | 批量创建优化 |
| 2.5 | 回写测试 | 完整回写流程验证 |

#### Phase 3：MCP 集成（后期）

| 步骤 | 任务 | 产出 |
|------|------|------|
| 3.1 | MCP 工具封装 | canvas_export 工具 |
| 3.2 | MCP 工具封装 | canvas_apply 工具 |
| 3.3 | 集成测试 | AI Agent 调用验证 |

### 4.2 验收标准

#### Phase 1 验收

| 检查项 | 标准 |
|--------|------|
| 编译 | `dotnet build` 通过，无错误无警告 |
| 插件加载 | Revit 启动后显示 BIMCanvas Ribbon 面板 |
| 坐标转换 | 手动验证：Revit 坐标 → JSON 坐标正确 |
| 边界提取 | JSON 中 `outline.boundaries` 轮廓封闭，包含墙体和柱子 |
| 门窗提取 | JSON 中 `outline.openings` 线段位置正确 |
| 房间提取 | JSON 中 `rooms` 边界完整 |
| 类型推断 | 关键词匹配正确率 > 80% |
| 用户确认 | ConfigWindow 显示正常，可修改类型 |
| JSON 格式 | 符合 Schema-JSON.md v2.5 规范 |

#### Phase 2 验收

| 检查项 | 标准 |
|--------|------|
| 文件读取 | 正确解析 CanvasDocument JSON |
| 坐标转换 | 家具位置与 JSON 中一致 |
| 朝向转换 | 家具朝向正确 |
| 族加载 | 能加载指定族文件 |
| 实例创建 | 家具实例创建成功 |

---

## 五、附录

### 5.1 相关文档

| 文档 | 路径 | 内容 |
|------|------|------|
| 架构文档 | `docs/Architecture.md` | 系统架构（§6.2 BIMCanvas.Revit 设计） |
| JSON Schema | `docs/Schema-JSON.md` | v2.5 数据模型定义 |
| Core 实现计划 | `plans/Core_Implementation_Plan.md` | Core 层实现参考 |

### 5.2 进度追踪

| 阶段 | 状态 | 更新时间 |
|------|------|----------|
| Phase 1: 项目初始化 | ⬜ 待开始 | - |
| Phase 1: Adapters 层 | ⬜ 待开始 | - |
| Phase 1: Services 层 | ⬜ 待开始 | - |
| Phase 1: Commands 层 | ⬜ 待开始 | - |
| Phase 1: Views 层 | ⬜ 待开始 | - |
| Phase 2: 回写功能 | ⬜ 待开始 | - |
| Phase 3: MCP 集成 | ⬜ 待开始 | - |

### 5.3 变更日志

| 日期 | 版本 | 变更内容 |
|------|------|----------|
| 2025-12-05 | v1.0 | 计划创建，确定 Revit 2019 + 先导出流程 |
| 2025-12-05 | v1.1 | 架构共识：Revit 层只输出精简版 CanvasDocument；后端合并为 Server |
| 2025-12-06 | v1.2 | 职责定位优化：明确独立运行插件；导出为本地 JSON；房间类型用户确认；MCP 移至后期 |
