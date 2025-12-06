# BIMCanvas.Revit 代码生成计划

> 实现 Revit 2019 插件，从 Revit 模型导出 CanvasDocument JSON
>
> **相关文档**：
> - [Architecture.md](../docs/Architecture.md) - 系统架构（§6.2 BIMCanvas.Revit 设计）
> - [Schema-JSON.md](../docs/Schema-JSON.md) - JSON Schema 规范 v2.5
> - [Core_Implementation_Plan.md](./Core_Implementation_Plan.md) - Core 层实现计划

---

## 零、核心定位与职责边界

### 0.1 核心定位

**BIMCanvas.Revit 是一个独立运行的 Revit 插件**

> **关键理解**：BIMCanvas.Revit 本身是一个完整的、可独立运行的 Revit 插件，通过本地 JSON 文件与其他系统交换数据。
> MCP 只是后期阶段让 AI Agent 调用该插件功能的一种手段，不是核心依赖。

**职责范围**：
- **数据提取（导出）**：从 Revit 模型提取原始建筑数据 → 保存为 JSON 文件
- **数据回写（导入）**：读取布置结果 JSON → 创建 Revit 家具实例
- **UI 交互**：提供 Ribbon 面板、配置窗口、文件对话框

### 0.2 项目职责划分

| 项目 | 职责 | 输入 | 输出 |
|------|------|------|------|
| **BIMCanvas.Revit** | 原始数据提取 + 回写 | Revit 模型 / JSON 文件 | **精简版** CanvasDocument JSON 文件 / Revit 元素 |
| **BIMCanvas.Server** | 数据处理 + 状态管理 | 精简版 JSON | 完整版 CanvasDocument（含 Zone） |
| **BIMCanvas.Core** | 算法库 | - | 被各项目引用 |
| **BIMCanvas.Web** | 前端渲染 | JSON | 画布显示 |

### 0.3 职责边界

#### ✅ Revit 层做什么

| 功能类别 | 具体内容 |
|----------|----------|
| **数据提取** | 墙体轮廓、门窗线段、房间边界、视图元数据 |
| **类型推断** | 基于名称关键词推断 RoomType + 用户确认 |
| **坐标转换** | Revit XYZ (feet) ↔ BIMCanvas Point2D (mm) |
| **JSON 输出** | 序列化 CanvasDocument 保存为本地文件 |
| **UI 交互** | Ribbon 面板、配置窗口、文件对话框 |
| **数据回写** | 解析 modules、创建 FamilyInstance（后续阶段） |

#### ❌ Revit 层不做什么

| 功能类别 | 负责方 | 原因 |
|----------|--------|------|
| Zone 划分计算 | Server.ZoneCalculator | 复杂逻辑集中管理 |
| InnerBoundary 计算 | Server.ZoneCalculator | 需要完成面厚度配置 |
| ExclusionArea 计算 | Server.ZoneCalculator | 门扇开启角度等参数 |
| WallFinish 生成 | Server.ZoneCalculator | 需要项目配置 |
| 碰撞检测 | Core.CollisionDetector | 算法复用 |
| AI 布置规划 | BIMCanvas.Agent | Agent SDK 实现 |
| JSON→SVG 渲染 | BIMCanvas.Web | 前端职责 |
| MCP 工具封装 | 后期阶段 | 当前非重点 |

### 0.4 Revit 层输出格式

Revit 层输出**不带 Zone** 的精简 CanvasDocument：

```json
{
  "id": "canvas_xxx",
  "version": 1,
  "coordinateSystem": "cartesian_mm_yUp",
  "metadata": { "revitViewId": 123, "levelId": 456 },
  "outline": {
    "walls": [...],
    "openings": [...]
  },
  "rooms": [...],
  "zones": [],
  "wallFinishes": [],
  "modules": []
}
```

### 0.3 后端项目简化

**原设计**：`BIMCanvas.MCP.Canvas` + `BIMCanvas.Web.Server`（两个项目）
**新设计**：合并为 `BIMCanvas.Server`（单一后端）

```
BIMCanvas.Server/                     【单一后端项目】(.NET 6+)
├── Program.cs                        入口
├── Hubs/
│   └── CanvasHub.cs                  SignalR 实时通信
├── Controllers/
│   └── CanvasController.cs           REST API
├── McpTools/                         MCP 工具（供 Claude Code 调用）
│   ├── CanvasTools.cs
│   └── ModuleTools.cs
├── Services/
│   ├── CanvasStateManager.cs         画布状态管理
│   ├── ZoneCalculator.cs             Zone 计算（Room → Zone + InnerBoundary）
│   ├── ModulePlacementService.cs     Module 放置验证
│   └── ScreenshotService.cs          截图服务
└── ...
```

### 0.6 数据流

**当前阶段**：通过本地 JSON 文件进行数据交换

```
【导出流程】
Revit 模型
    ↓ [BIMCanvas.Revit: 提取原始数据]
    ↓ [用户确认房间类型]
精简版 CanvasDocument (无 Zone)
    ↓ [保存为本地 .json 文件]
BIMCanvas.Server（读取文件）
    ↓ [ZoneCalculator: 计算 Zone/InnerBoundary/ExclusionAreas]
完整版 CanvasDocument
    ↓ [Web 端显示]
BIMCanvas.Web（前端渲染）
    ↓ [用户/AI 交互]
更新后的 CanvasDocument（含 modules）
    ↓ [保存为 .json 文件]

【回写流程】
BIMCanvas.Revit（读取 .json 文件）
    ↓ [解析 modules[].items]
    ↓ [计算坐标 + 加载族]
Revit 元素（FamilyInstance）
```

> **注**：后续阶段可扩展为直接 HTTP API 通信，但当前优先保证核心功能稳定。

### 0.5 PlacementAgent 机制（详见 reviews/PlacementAgent_Review.md）

**核心概念**：触发 AI 生成布置方案的是 PlacementAgent（子 Agent）

**三种触发方式**：
1. **AI 对话触发**：用户与 Claude Code 对话 → 调用 MCP 工具 → PlacementService
2. **Web 按钮触发**：用户点击"一键布置" → REST API → PlacementService
3. **自动修正触发**：Server 检测到布置错误 → PlacementService.AutoFix()

**架构位置**：Server 内部服务 + MCP 工具封装

```
BIMCanvas.Server/
├── Services/
│   ├── PlacementService.cs          ← 布置逻辑核心
│   └── PlacementAgentBridge.cs      ← AI Agent 桥接
├── McpTools/
│   └── PlacementTools.cs            ← MCP 工具封装
└── Controllers/
    └── PlacementController.cs       ← REST API
```

---

## 一、项目初始化

### 1.1 项目约束

| 约束项 | 值 | 说明 |
|--------|-----|------|
| 目标框架 | .NET Framework 4.7.2 | Revit 2019 API 限制 |
| Revit 版本 | Revit 2019 | 用户指定 |
| 依赖项目 | BIMCanvas.Core | .NET Standard 2.0 兼容 |
| 外部集成 | 无（当前阶段） | MCP 集成属于后期阶段 |

### 1.2 创建项目

```bash
# 在解决方案目录下创建项目文件夹
mkdir BIMCanvas.Revit

# 添加到解决方案
dotnet sln add BIMCanvas.Revit/BIMCanvas.Revit.csproj
```

### 1.3 csproj 配置

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

### 1.4 程序集信息

`Properties/AssemblyInfo.cs`:
```csharp
[assembly: AssemblyTitle("BIMCanvas.Revit")]
[assembly: AssemblyDescription("BIMCanvas Revit Plugin - AI-Powered Furniture Layout")]
[assembly: AssemblyVersion("1.0.0.0")]
```

---

## 二、文件生成顺序与规格

### 第一批：Adapters（适配器层）

**依赖关系**：依赖 Core 层的 UnitConverter 和 Models

| 文件 | 职责 | 核心方法 |
|------|------|----------|
| `CoordinateAdapter.cs` | 坐标系转换 | `ToPoint2D(XYZ)`, `ToXYZ(Point2D)` |
| `ViewAdapter.cs` | 视图信息提取 | `ExtractMetadata(View)` |
| `WallAdapter.cs` | 墙体轮廓提取 | `ExtractWalls(View)` → `List<Wall>` |
| `OpeningAdapter.cs` | 门窗线段提取 | `ExtractOpenings(View)` → `List<Opening>` |
| `RoomAdapter.cs` | 房间边界提取 | `ExtractRooms(View)` → `List<Room>` |

### 第二批：Services（服务层）

| 文件 | 职责 | 核心方法 |
|------|------|----------|
| `CanvasExportService.cs` | 画布导出 | `ExportFromView(View)` → `CanvasDocument` |
| `ExportOptions.cs` | 导出配置 | 完成面厚度、门扇禁区开关等 |

### 第三批：Commands（命令层）

| 文件 | 职责 | Revit 接口 |
|------|------|------------|
| `App.cs` | 插件入口 | `IExternalApplication` |
| `ExportCanvasCommand.cs` | 导出命令 | `IExternalCommand` |

### 第四批：Views（UI 层）

| 文件 | 职责 |
|------|------|
| `ConfigWindow.xaml` | 配置窗口 XAML |
| `ConfigWindow.xaml.cs` | 窗口代码 |
| `ViewModels/ConfigViewModel.cs` | MVVM ViewModel |

### 第五批：Core 层更新

| 文件 | 变更内容 |
|------|----------|
| `RevitToJsonConverter.cs` | 添加 `RevitExportData` DTO |
| `JsonToRevitConverter.cs` | 添加 `RevitImportData` DTO（后续） |

---

## 三、关键实现细节

### 3.1 CoordinateAdapter 设计

**核心职责**：Revit XYZ (feet, 项目坐标系) → BIMCanvas Point2D (mm, 视图坐标系)

```csharp
public class CoordinateAdapter
{
    private readonly XYZ _viewOrigin;      // 视图裁剪框左下角（世界坐标）
    private readonly double _viewRotation; // 视图旋转角度（弧度）

    public CoordinateAdapter(View view)
    {
        // 从视图裁剪框计算原点
        _viewOrigin = CalculateViewOrigin(view);
        _viewRotation = CalculateViewRotation(view);
    }

    /// <summary>
    /// Revit XYZ → BIMCanvas Point2D
    /// </summary>
    public Point2D ToPoint2D(XYZ revitPoint)
    {
        // 1. 相对于视图原点
        var relative = revitPoint - _viewOrigin;

        // 2. 应用视图旋转（如果有）
        var rotated = RotatePoint(relative, -_viewRotation);

        // 3. 单位转换 feet → mm (使用 Core 层 UnitConverter)
        return new Point2D(
            UnitConverter.ToMillimeters(rotated.X),
            UnitConverter.ToMillimeters(rotated.Y)
        );
    }

    /// <summary>
    /// BIMCanvas Point2D → Revit XYZ
    /// </summary>
    public XYZ ToXYZ(Point2D point, double elevation = 0)
    {
        // 1. 单位转换 mm → feet
        var x = UnitConverter.ToFeet(point.X);
        var y = UnitConverter.ToFeet(point.Y);

        // 2. 应用视图旋转
        var rotated = RotatePoint(new XYZ(x, y, 0), _viewRotation);

        // 3. 加上视图原点偏移
        return rotated + _viewOrigin + new XYZ(0, 0, elevation);
    }
}
```

### 3.2 RoomAdapter 设计

**职责**：从 Revit Room 提取 `Room` 数据（不创建 Zone，Zone 由 Server 计算）

**房间名称 → RoomType 映射表**：

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

**房间类型推断流程**：

1. **自动推断**：`RoomTypeInferrer.InferFromName(roomName)` 根据关键词匹配
2. **用户确认**：导出前弹出 `ConfigWindow`，列出所有房间及推断类型
3. **用户修改**：用户可在界面中修改任意房间的类型
4. **应用结果**：使用用户确认后的类型组装 CanvasDocument

**输出格式**：
```csharp
public class Room
{
    public string Id { get; set; }        // Revit Room ElementId
    public string Name { get; set; }      // Revit Room 名称
    public RoomType Type { get; set; }    // 推断的房间类型
    public Polygon2D Boundary { get; set; } // 房间边界
}
```

> **注意**：RoomType → ZoneTag 的推断逻辑移至 BIMCanvas.Server 的 ZoneCalculator

### 3.3 CanvasExportService 组装流程

**简化流程**（Zone 计算移至 Server）：

```
1. 创建 CoordinateAdapter (从视图)
2. 创建各元素适配器
3. viewAdapter.ExtractMetadata(view)     → metadata
4. wallAdapter.ExtractWalls(view)        → walls[]
5. openingAdapter.ExtractOpenings(view)  → openings[]
6. roomAdapter.ExtractRooms(view)        → rooms[]
7. 组装精简版 CanvasDocument {
     id, version, coordinateSystem,
     metadata, outline: { walls, openings },
     rooms,
     zones: [],           // 空，由 Server 计算
     wallFinishes: [],    // 空，由 Server 计算
     modules: []          // 空，待 AI 布置
   }
8. 弹出 ConfigWindow 显示房间列表，用户确认/修改房间类型
9. 应用用户确认后的房间类型到 rooms[]
10. 弹出保存对话框，用户选择路径
11. JsonConvert.SerializeObject() 保存到本地 .json 文件
```

**关键变更**：
- ❌ 不再计算 Zone/InnerBoundary/ExclusionAreas
- ❌ 不再计算 WallFinish
- ✅ 只提取原始建筑数据

### 3.4 数据格式说明

**Revit 层直接输出 CanvasDocument**（精简版，无需中间 DTO）：

由于 Revit 层现在只负责提取原始数据，不再需要 `RevitExportData` 中间 DTO。直接使用 Core 层的 `CanvasDocument` 模型即可。

**回写时使用 RevitImportData**（后续实现）：
```csharp
public class RevitImportData
{
    public long LevelId { get; set; }
    public List<FurnitureItemData> Items { get; set; }
}

public class FurnitureItemData
{
    public string FamilyId { get; set; }
    public Point2D Position { get; set; }  // mm 单位
    public double RotationRadians { get; set; }
}
```

---

## 四、验收标准

### 4.1 Phase 1 验收

| 检查项 | 标准 |
|--------|------|
| 编译 | `dotnet build` 通过，无错误无警告 |
| 插件加载 | Revit 启动后显示 BIMCanvas Ribbon 面板 |
| 坐标转换 | 手动验证：Revit 坐标 → JSON 坐标正确 |

### 4.2 Phase 2 验收

| 检查项 | 标准 |
|--------|------|
| 墙体提取 | JSON 中 `outline.walls` 轮廓封闭 |
| 门窗提取 | JSON 中 `outline.openings` 线段位置正确 |
| 房间提取 | JSON 中 `rooms` 边界完整，类型正确 |

### 4.3 Phase 3 验收

| 检查项 | 标准 |
|--------|------|
| 配置窗口 | 显示正常，MVVM 绑定工作 |
| 导出流程 | 点击按钮 → 选择路径 → 生成 JSON |
| JSON 格式 | 符合 Schema-JSON.md v2.5 规范 |

---

## 五、进度追踪

### 当前状态

**Phase 1: 核心导出功能**

| 阶段 | 状态 | 更新时间 |
|------|------|----------|
| 一. 项目初始化 | ⬜ 待开始 | - |
| 二. Adapters 层（坐标/墙体/门窗/房间） | ⬜ 待开始 | - |
| 三. Services 层（导出 + 类型推断） | ⬜ 待开始 | - |
| 四. Commands 层（导出命令） | ⬜ 待开始 | - |
| 五. Views 层（房间类型确认窗口） | ⬜ 待开始 | - |

**Phase 2: 回写功能**

| 阶段 | 状态 | 更新时间 |
|------|------|----------|
| 六. LayoutApplyService | ⬜ 待开始 | - |
| 七. ApplyLayoutCommand | ⬜ 待开始 | - |

**Phase 3: 后期阶段**

| 阶段 | 状态 | 更新时间 |
|------|------|----------|
| 八. MCP 集成 | ⬜ 待开始 | - |

### 待生成文件

```
BIMCanvas.Revit/
├── BIMCanvas.Revit.csproj
├── Properties/
│   └── AssemblyInfo.cs
├── Adapters/
│   ├── CoordinateAdapter.cs
│   ├── ViewAdapter.cs
│   ├── WallAdapter.cs
│   ├── OpeningAdapter.cs
│   └── RoomAdapter.cs
├── Commands/
│   ├── App.cs
│   └── ExportCanvasCommand.cs
├── Views/
│   ├── ConfigWindow.xaml
│   ├── ConfigWindow.xaml.cs
│   └── ViewModels/
│       └── ConfigViewModel.cs
├── Services/
│   ├── CanvasExportService.cs
│   ├── LayoutApplyService.cs
│   ├── RoomTypeInferrer.cs
│   └── ExportOptions.cs
└── README.md

Core 层更新:
├── Converters/Revit/
│   ├── RevitToJsonConverter.cs (更新：添加 DTO)
│   └── JsonToRevitConverter.cs (后续更新)
```

### 变更日志

| 时间 | 变更内容 |
|------|----------|
| 2025-12-05 | 计划创建，确定 Revit 2019 + 先导出流程 |
| 2025-12-05 | **架构讨论共识**：Revit 层只输出精简版 CanvasDocument（无 Zone）；后端项目合并为 BIMCanvas.Server；PlacementAgent 机制 |
| 2025-12-06 | **职责定位优化**：明确 Revit 为独立运行插件；导出为本地 JSON 文件；新增房间类型用户确认流程；扩展回写功能设计；MCP 集成移至后期阶段 |

---

## 六、回写功能详细设计（Phase 2）

### 6.1 回写流程

```
用户点击"应用布置"按钮
    → ApplyLayoutCommand.Execute()
    → 弹出文件选择对话框，选择 .json 文件
    → LayoutApplyService.ApplyFromJson(json)
        → JsonConvert.DeserializeObject<CanvasDocument>()
        → 遍历 modules[].items
            → 计算世界坐标 (bounds 中心 + item.offset)
            → 转换朝向角度 (facing → radians)
            → 转换坐标单位 (mm → feet)
            → LoadFamily(familyId)
            → CreateFamilyInstance(...)
    → Revit 显示布置结果
```

### 6.2 LayoutApplyService 设计

```csharp
public class LayoutApplyService
{
    private readonly Document _doc;
    private readonly CoordinateAdapter _coordAdapter;

    public void ApplyFromJson(string jsonPath)
    {
        var canvas = JsonConvert.DeserializeObject<CanvasDocument>(File.ReadAllText(jsonPath));
        var levelId = new ElementId(canvas.Metadata.LevelId);

        using (var trans = new Transaction(_doc, "Apply Layout"))
        {
            trans.Start();

            foreach (var module in canvas.Modules)
            {
                foreach (var item in module.Items)
                {
                    PlaceItem(module, item, levelId);
                }
            }

            trans.Commit();
        }
    }

    private void PlaceItem(Module module, ModuleItem item, ElementId levelId)
    {
        // 1. 计算世界坐标
        var center = GeometryHelper.ComputeCenter(module.Bounds);
        var worldPos = new Point2D(center.X + item.Offset.X, center.Y + item.Offset.Y);

        // 2. 转换为 Revit 坐标
        var revitPos = _coordAdapter.ToXYZ(worldPos);

        // 3. 计算旋转角度
        var angle = module.Facing.ToAngleRadians();

        // 4. 加载族并创建实例
        var symbol = LoadFamilySymbol(item.FamilyId);
        _doc.Create.NewFamilyInstance(revitPos, symbol, levelId, ...);
    }
}
```

### 6.3 关键依赖

| Core 组件 | 用途 |
|-----------|------|
| `GeometryHelper.ComputeCenter()` | 计算 bounds 中心点 |
| `Facing.ToAngleRadians()` | 朝向 → 弧度角 |
| `UnitConverter.ToFeet()` | mm → feet 转换 |
| `CoordinateAdapter.ToXYZ()` | 视图坐标 → Revit 坐标 |

---

## 七、后期阶段（Phase 3+）

### 7.1 MCP 集成（最后期）

> **当前状态**：BIMCanvas.Revit 作为独立插件运行，MCP 集成属于后期阶段。

将核心功能封装为 MCP 工具，供 AI Agent 调用：

| MCP 工具 | 对应功能 |
|----------|----------|
| `canvas_export` | CanvasExportService.ExportFromView() |
| `canvas_apply` | LayoutApplyService.ApplyFromJson() |

### 7.2 其他后续功能

| 功能 | 说明 | 优先级 |
|------|------|--------|
| 门扇禁区计算 | `ExclusionArea.Type = DoorSwing`（Server 端） | P2 |
| 墙面完成面 | `WallFinish` 数据（Server 端） | P2 |
| AI 启动服务 | `AiLauncherService` 启动 Claude Code | P3 |
