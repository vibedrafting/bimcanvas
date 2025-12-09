# BIMCanvas 项目指令

> 在用户提供的建筑平面内，布置符合设计逻辑的家具组合。

**数据模型版本**: v2.7 (新增 BIMCanvas.Agent + Agent SDK 架构)

---

## 快速导航

### 文档索引

| 文档 | 路径 | 内容 |
|------|------|------|
| 架构文档 | `docs/Architecture.md` | 系统架构、数据流 |
| 执行流程 | `docs/Workflows.md` | 端到端执行流程、触发机制 |
| JSON Schema | `docs/Schema-JSON.md` | v2.5 数据模型定义 |
| PRD | `docs/PRD.md` | 产品需求、工作流程 |
| Core 实现计划 | `plans/Core_Implementation_Plan.md` | Core 层代码生成计划 |
| PlacementAgent 评审 | `reviews/PlacementAgent_Review.md` | Agent SDK 架构决策讨论 |

### 模块速查

| 项目 | 运行时 | 职责 | 状态 |
|------|--------|------|------|
| BIMCanvas.Core | .NET Standard 2.0 | 数据模型 + 空间算法 | ✅ 已完成 |
| BIMCanvas.Agent | Python 3.10+ | PlacementAgent（Agent SDK） | ⬜ 待开发 |
| BIMCanvas.Server | .NET 6+ | 统一后端（MCP + REST + SignalR + SSE） | ⬜ 待开发 |
| BIMCanvas.Revit | .NET FW 4.7.2 | Revit 插件 | ⬜ 待开发 |
| BIMCanvas.Web | Vue 3 + TS | Web 前端 | ⬜ 待开发 |

> **当前阶段**：Core 层已完成，准备开发 Server 和 Agent 层

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

## v2.5 数据模型速查

### 核心设计原则

> **AI = OBB 规划师**：AI 只操作矩形包围盒，不计算精确几何。Core 层负责转换。

### JSON 顶级结构

```
CanvasDocument
├── outline              边界轮廓 + 门窗线段 (仅视觉)
│   ├── boundaries[]     封闭多边形 Polygon2D (墙体 + 柱子)
│   └── openings[]       线段 Line2D + type (door/window)
├── rooms[]              物理房间 (v2.5 新增)
│   ├── id, name, type   RoomType 枚举
│   └── boundary         Polygon2D
├── zones[]              设计区域 (AI 核心工作区)
│   ├── roomId           所属房间 ID (v2.5 新增)
│   ├── tags[]           ZoneTag 枚举列表 (v2.5 替代 function)
│   ├── rawBoundary      原始边界 (v2.5 新增)
│   ├── innerBoundary    可用空间轮廓 Polygon2D
│   ├── exclusionAreas[] 禁区 boundary: Polygon2D (4顶点矩形)
│   └── openings[]       关联门窗 ID
├── wallFinishes[]       墙面完成面 (v2.5 新增)
│   ├── locationLine     定位线 Line2D
│   ├── thickness        厚度 (mm)
│   └── exclusionBoundary 禁区轮廓 Polygon2D
└── modules[]            布置模块 (最小布置单元)
    ├── bounds           Polygon2D [[x,y], ...] (4顶点矩形)
    ├── facing           Facing (FacingDirection 枚举 | Vec2D)
    └── items[]          内部家具清单 (回写 Revit 用)
```

### AI 布置约束

```
对于每个要放置的模块：
1. bounds 必须完全在 zone.innerBoundary 内
2. bounds 不能与任何 zone.exclusionAreas 重叠
3. bounds 不能与其他已放置模块重叠
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

- **存储/传输**：JSON（CanvasDocument）
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
