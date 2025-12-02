# BIMCanvas 项目指令

> 在用户提供的建筑平面内，布置符合设计逻辑的家具组合。

---

## 快速导航

### 文档索引

| 文档 | 路径 | 内容 |
|------|------|------|
| 架构文档 | `docs/Architecture.md` | 系统架构、数据流、模块设计 |
| JSON Schema | `docs/Schema-JSON.md` | 数据模型定义 |
| PRD | `docs/PRD.md` | 产品需求、工作流程 |
| 评审记录 | `docs/Architecture_Design_Review.md` | 设计决策讨论 |

### 模块速查

| 项目 | 运行时 | 职责 | 入口 |
|------|--------|------|------|
| BIMCanvas.Core | .NET Standard 2.0 | 数据模型 + 空间算法 | `Models/`, `Algorithms/` |
| BIMCanvas.Revit | .NET FW 4.7.2 | Revit 插件 | `Commands/` |
| BIMCanvas.MCP.Canvas | .NET 6+ | 画布 MCP Server | `Program.cs` |
| BIMCanvas.MCP.Library | .NET 6+ | 族库 MCP Server | `Program.cs` |
| BIMCanvas.Web.Server | .NET 6+ | Web 后端 | `Program.cs` |
| BIMCanvas.Web | Vue 3 + TS | Web 前端 | `src/main.ts` |

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
# MSBuild 路径
"D:\JetBrains\JetBrains Rider 2025.1.4\tools\MSBuild\Current\Bin\MSBuild.exe"

# 编译项目
"[MSBuild]" "BIMCanvas.Core/BIMCanvas.Core.csproj" -nologo -clp:ErrorsOnly
```

### 运行

```bash
# .NET 6+ 项目
dotnet run --project BIMCanvas.MCP.Canvas

# .NET FW 控制台（必须直接执行 exe）
"bin/Debug/[项目名].exe"
```
