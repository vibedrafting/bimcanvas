# BIMCanvas 项目指令

> 在用户提供的建筑平面内，布置符合设计逻辑的家具组合。

**数据模型版本**: v3.0 (File-Driven Architecture + .bcp 项目格式)

---

## 快速导航

### 文档索引

| 文档 | 路径 | 内容 |
|------|------|------|
| 架构文档 | `docs/Architecture.md` | 系统架构、数据流 |
| 执行流程 | `docs/Workflows.md` | 端到端执行流程、触发机制 |
| JSON Schema | `docs/Schema-JSON.md` | v3.0 数据模型定义 |
| PRD | `docs/PRD.md` | 产品需求、工作流程 |
| Core 层 | `BIMCanvas.Core/README.md` | 数据模型 + 空间算法实现 |
| Revit 插件 | `BIMCanvas.Revit/README.md` | Revit 导出/回写实现细节 |
| Server 层 | `BIMCanvas.Server/README.md` | 统一后端服务、状态管理、通信中枢 |
| Core 实现计划 | `plans/Core_Implementation_Plan.md` | Core 层代码生成计划 |
| PlacementAgent 评审 | `reviews/PlacementAgent_Review.md` | Agent SDK 架构决策讨论 |

### 模块速查

| 项目 | 运行时 | 职责 | 状态 |
|------|--------|------|------|
| BIMCanvas.Core | .NET Standard 2.0 | 数据模型 + 空间算法 | ✅ 已完成 |
| BIMCanvas.Revit | .NET FW 4.7.2 | Revit 插件（导出 + 回写） | 🔶 导出完成，回写待开发 |
| BIMCanvas.Agent | Python 3.10+ | PlacementAgent（Agent SDK） | ⬜ 待开发 |
| BIMCanvas.Server | .NET 6+ | 统一后端（MCP + REST + SignalR + SSE） | ⬜ 待开发 |
| BIMCanvas.Web | Vue 3 + TS | Web 前端 | ⬜ 待开发 |

> **当前阶段**：Core 层已完成，Revit 导出功能已完成，下一步开发 Revit 回写或 Server/Agent 层

### 组件角色定位

| 组件 | 比喻 | 核心职责 |
|------|------|----------|
| **Server** | 心脏 + 神经系统 | 状态管理、几何计算、通信中枢、事件分发 |
| **Agent** | 大脑 | 智能决策、理解意图、规划方案 |
| **Core** | 骨骼 | 数据结构、基础算法、类型定义 |
| **Web** | 皮肤 + 眼睛 | 渲染展示、用户交互 |
| **Revit** | 手臂 | 从 Revit 抓取数据、回写 Revit |

**关键区分**：
- **Server 是「指挥中心」**：协调各方、管理状态、执行验证，但**不做布置决策**
- **Agent 是「设计师」**：理解需求、做出决策、发出指令，但**不持有状态**

---

## 核心约束

### 命名空间边界

```
BIMCanvas.Core.*     → 所有 .NET 项目可引用
BIMCanvas.Revit.*    → 仅 Revit 插件内部使用
```

**禁止**：MCP Server 或 Web Server 引用 `BIMCanvas.Revit` 命名空间（会导致运行时错误）

### .NET 版本规则

- **Core 层**：必须使用 .NET Standard 2.0（跨框架兼容）
- **Revit 层**：必须使用 .NET FW 4.7.2（Revit API 限制）
- **其他层**：使用 .NET 6+

### 禁止事项

- Core 层引用 Revit API
- 直接让 AI 操作 SVG 代码（应操作 JSON）
- 使用 CSS `scaleY(-1)` 做坐标翻转

---

## PlacementAgent 架构速查

> **架构决策**：PlacementAgent 基于 Anthropic Agent SDK 实现，作为独立 Python 进程运行，通过 SSE 接收事件触发。

### 架构概览

```
BIMCanvas.Agent (Python 3.10+)
├── PlacementAgent (Agent SDK)
├── EventListener (SSE 客户端)
└── MCP 工具集成
         ↑ SSE 事件           ↓ MCP/HTTP 调用
         │                    │
BIMCanvas.Server (.NET 6+)
├── EventBus (事件总线)
├── EventsController (SSE 端点)
└── McpTools/ (Canvas-MCP)
```

### 三种触发方式

| 触发方式 | 触发源 | 数据流 |
|----------|--------|--------|
| AI 对话 | 用户输入 | 用户 → Agent Chat → PlacementAgent.run() |
| Web 按钮 | 前端 UI | Web → Server EventBus → SSE → Agent |
| 自动修正 | Server 检测 | Server 验证 → EventBus → SSE → Agent |

### Agent SDK 要点

- 安装：`pip install anthropic`（Agent SDK 包含在 anthropic 包中）
- 长期运行：Agent 持续监听 SSE 事件流
- 工具调用：通过 MCP 协议调用 Canvas-MCP 工具
- 详细设计见 `docs/Architecture.md` §6.4

---

## BIMCanvas.Revit 速查

> **项目状态**：Phase 1（导出）✅ 完成，Phase 2（回写）⬜ 待开发

### 已实现功能（Phase 1）

| 模块 | 关键文件 | 功能 |
|------|----------|------|
| 命令层 | `Commands/ExportCanvasCommand.cs` | Ribbon 面板 + 导出命令 |
| 适配器层 | `Adapters/BoundaryAdapter.cs` | 边界轮廓提取（墙体+柱子几何切割） |
| | `Adapters/OpeningAdapter.cs` | 门窗数据提取（定位线、方向、开启方式） |
| | `Adapters/RoomAdapter.cs` | 房间边界提取（自动设置柱子为边界） |
| 服务层 | `Services/CanvasExportService.cs` | 6 阶段导出流程 |
| | `Services/CoordinateTransformer.cs` | 坐标系转换（Revit ↔ BIMCanvas） |
| | `Services/RoomTypeInferrer.cs` | 房间类型智能推断（中英文关键词） |
| 工具层 | `Utilities/OutlineExtractor.cs` | Boolean 运算合并 Solid + 平面切割 |
| | `Utilities/OpeningDirectionAnalyzer.cs` | IFC 工具提取门弧线 + 方向计算 |
| 视图层 | `Views/ConfigWindow.xaml` | 房间类型确认界面（WPF MVVM） |

### 待开发功能（Phase 2）

| 功能 | 计划文件 | 描述 |
|------|----------|------|
| 布置应用服务 | `Services/LayoutApplyService.cs` | 读取 JSON → 创建 FamilyInstance |
| 应用命令 | `Commands/ApplyLayoutCommand.cs` | 导入布置结果命令 |
| 族加载逻辑 | `Services/FamilyLoader.cs` | 自动加载/匹配家具族文件 |

### 坐标转换

```
Revit (feet, 项目坐标)  ←→  BIMCanvas (mm, 归一化坐标)
         ↓                        ↓
  CoordinateTransformer.ToPoint2D()  /  ToXYZ()
```

- 详细设计见 `BIMCanvas.Revit/README.md`

---

## v3.0 数据模型速查

### 核心设计原则

> **File-Driven Architecture**：文件是唯一真理源，Server 是"文件播放器"而非"内存数据库"
> **AI = OBB 规划师**：AI 只操作矩形包围盒，不计算精确几何。Core 层负责转换。

### 三层汉堡模型 (.bcp 项目结构)

```
project.bcp (ZIP)
├── manifest.json           项目元数据 + 方案列表
├── baseline/               【底层】建筑基础数据（只读，Revit 导出）
│   ├── walls.json          墙体轮廓 Polygon2D
│   ├── columns.json        柱子轮廓 Polygon2D
│   ├── openings.json       门窗 Line2D + type + direction
│   ├── rooms.json          物理房间 { id, name, type, boundary }
│   └── locationLines.json  完成面定位线 { wallId, roomId, line, normal }
├── schemes/{strategyId}/   【中层】方案设计数据（AI/Server 可写）
│   ├── zones.json          设计区域 { roomId, tags[], innerBoundary, openings[] }
│   ├── finishes.json       完成面分段 { locationLineId, startT, endT, thickness }
│   └── modules.json        布置模块 { bounds, facing, items[] }
└── computed/               【顶层】计算派生数据（自动生成）
    └── exclusions.json     禁区 { sourceType, sourceId, boundary }
```

### 关键模型变化 (v2.x → v3.0)

| v2.x | v3.0 | 说明 |
|------|------|------|
| DesignDocument | Project | 根对象重构 |
| WallFinish | FinishSegment | 完成面分段化 |
| - | LocationLine | 新增定位线模型 |
| - | ExclusionArea | 禁区独立类 |
| - | Strategy | 多方案支持 |

### AI 布置约束

```
对于每个要放置的模块：
1. bounds 必须完全在 computed.zones[].innerBoundary 内
2. bounds 不能与任何 computed.zones[].exclusionAreas 重叠
3. bounds 不能与其他已放置 modules[] 重叠
```

### Facing 类型 (语义朝向)

| 格式 | 示例 | 说明 |
|------|------|------|
| 语义字符串 | `"north"` | 标准 8 方向 |
| Vec2D | `[0.707, 0.707]` | 任意角度单位向量 |

**语义字符串 → 角度转换**：

| 朝向 | 角度 | 朝向 | 角度 |
|------|------|------|------|
| north | 0° | south | 180° |
| east | 90° | west | 270° |
| northeast | 45° | southwest | 225° |
| southeast | 135° | northwest | 315° |

---

## 开发规范

### 数据格式

- **存储/传输**：JSON（DesignDocument）
- **AI 交互**：纯 JSON
- **渲染**：前端根据 JSON 生成 SVG

### 坐标系统

- 坐标系：CAD 标准（原点左下角，Y 轴向上）
- 单位：毫米 (mm)
- 前端转换：`y_screen = canvasHeight - y_model`

### 编码注意

- 新建 `.cs` 文件后必须在 `.csproj` 中添加引用
- Edit 工具可能导致中文乱码，批量替换前先存档
- 优先编辑现有文件，不创建新文件

### 调试代码规范

调试输出统一使用 `System.Diagnostics.Trace.WriteLine()`：

```csharp
System.Diagnostics.Trace.WriteLine($"[方法名] 调试信息: {变量}");
```

**规范要求**：
- 前缀格式：`[类名/方法名]`，便于过滤
- 调试完成后必须删除调试代码
- 不要使用 `Console.WriteLine` 或 `MessageBox`（会阻塞 UI）

---

## 常用命令

### 编译

```bash
# .NET Standard / .NET 6+ 项目（推荐）
dotnet restore BIMCanvas.Core
dotnet build BIMCanvas.Core --no-restore

# MSBuild 路径（备用）
"D:\JetBrains\JetBrains Rider 2025.1.4\tools\MSBuild\Current\Bin\MSBuild.exe"
```

### 运行

```bash
# .NET 6+ 项目
dotnet run --project BIMCanvas.MCP.Canvas

# .NET FW 控制台（必须直接执行 exe）
"bin/Debug/[项目名].exe"
```
