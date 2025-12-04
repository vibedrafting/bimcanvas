# BIMCanvas 项目指令

> 在用户提供的建筑平面内，布置符合设计逻辑的家具组合。

**数据模型版本**: v2.3 落实讨论结论 (outline + zones + modules)

---

## 快速导航

### 文档索引

| 文档 | 路径 | 内容 |
|------|------|------|
| 架构文档 | `docs/Architecture.md` | 系统架构、数据流、执行流程 |
| JSON Schema | `docs/Schema-JSON.md` | v2.3 数据模型定义 |
| PRD | `docs/PRD.md` | 产品需求、工作流程 |

### 模块速查

| 项目 | 运行时 | 职责 | 状态 |
|------|--------|------|------|
| BIMCanvas.Core | .NET Standard 2.0 | 数据模型 + 空间算法 | ⬜ 待开发 |
| BIMCanvas.Revit | .NET FW 4.7.2 | Revit 插件 | ⬜ 待开发 |
| BIMCanvas.MCP.Canvas | .NET 6+ | 画布 MCP Server | ⬜ 待开发 |
| BIMCanvas.MCP.Library | .NET 6+ | 族库 MCP Server | ⬜ 待开发 |
| BIMCanvas.Web.Server | .NET 6+ | Web 后端 | ⬜ 待开发 |
| BIMCanvas.Web | Vue 3 + TS | Web 前端 | ⬜ 待开发 |

> **当前阶段**：文档设计。数据模型定义见 `docs/Schema-JSON.md`

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

## v2.3 数据模型速查

### 核心设计原则

> **AI = OBB 规划师**：AI 只操作矩形包围盒，不计算精确几何。Core 层负责转换。

### JSON 顶级结构

```
CanvasDocument
├── outline          墙体轮廓 + 门窗线段 (仅视觉)
│   ├── walls[]      封闭多边形 Polygon2D
│   └── openings[]   线段 Line2D + type (door/window)
├── zones[]          设计区域 (AI 核心工作区)
│   ├── innerBoundary    可用空间轮廓 Polygon2D
│   ├── exclusionAreas[] 禁区 boundary: Polygon2D (4顶点矩形)
│   └── openings[]       关联门窗 ID
└── modules[]        布置模块 (最小布置单元)
    ├── bounds           Polygon2D [[x,y], ...] (4顶点矩形)
    ├── facing           Facing (语义字符串 | Vec2D)
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
