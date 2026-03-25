# BIMCanvas 项目指令

> 在用户提供的建筑平面内，布置符合设计逻辑的家具组合。

**架构版本**: v3.4 (File-Driven Architecture + .bcp 项目格式)

---

## 快速导航

### 文档索引

| 文档 | 路径 | 内容 |
|------|------|------|
| 系统架构 | `docs/Architecture.md` | 整体架构、组件关系 |
| 数据流 | `docs/Arch_DataFlow.md` | 三层职责、数据流场景 |
| Agent 运行时工作流 | `docs/Agent_Workflows.md` | query/edit/generate 完整链路 |
| Agent 架构设计 | `docs/Agent_Design.md` | SubAgent 架构、策略体系 |
| 提示词设计哲学 | `docs/Agent_Prompt_Design_Philosophy.md` | 五个底层机制、三层论、留白、实操指南（通用） |
| Agent 层 | `BIMCanvas.Agent/README.md` | MainAgent + SubAgent + Skills + MCP 工具 |
| Server 层 | `BIMCanvas.Server/README.md` | 统一后端、状态管理、Git Worktree |
| Web 层 | `BIMCanvas.Web/README.md` | 前端渲染、交互工具、AI 指挥中心 |
| Core 层 | `BIMCanvas.Core/README.md` | 数据模型 + 空间算法 |
| Revit 层 | `BIMCanvas.Revit/README.md` | Revit 导出/回写 |

### 模块速查

| 项目 | 运行时 | 职责 | 状态 |
|------|--------|------|------|
| BIMCanvas.Core | .NET Standard 2.0 | 数据模型 + 空间算法 | ✅ 已完成 |
| BIMCanvas.Revit | .NET FW 4.7.2 | Revit 插件（导出 + 回写） | 🔶 导出完成，回写待开发 |
| BIMCanvas.Agent | Python 3.10+ | MainAgent + SubAgent + Skills | 🔶 P2 工具调用阶段 |
| BIMCanvas.Server | .NET 8.0 | 统一后端（REST + SignalR + Git Worktree） | ✅ v3.4 核心就绪 |
| BIMCanvas.Web | Vue 3 + TS | Web 前端（渲染 + 交互 + AI 指挥中心） | ✅ 核心就绪 |

> **当前阶段**：全栈核心功能就绪，Agent 端到端测试 + Web 集成收尾中

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

## 设计理念

### 架构三原则
- **File-Driven**：文件是唯一真理源，Server 是"文件播放器"而非"内存数据库"
- **AI = OBB 规划师**：AI 只操作矩形包围盒 (bounds + facing)，不计算精确几何
- **Agent 只做决策，不做计算**：几何验证、禁区计算、Zone 生成均由 Server 负责

### 数据流三条线

| 数据流 | 方向 | 触发 |
|--------|------|------|
| 用户编辑流 | Web → Server → 文件 | 用户拖动模块 |
| 文件同步流 | 文件 → Server → Web | Agent/外部编辑 |
| 项目加载流 | 文件 → Server → Web | 上传/切换项目 |

### Agent 提示词哲学

> 详见 `docs/Agent_Prompt_Design_Philosophy.md`

- **注意力零和**：每条规则都在竞争 AI 注意力，精准 > 数量
- **激活而非注入**：提示词唤醒 AI 已有知识，无法注入新知识
- **WHY 决定泛化能力**：有理由的规则能被灵活应用，没理由的只能机械执行
- **示例是最强锚定**：一个好示例的信息密度远超十条文字规则
- **位置效应**：头尾内容获得更多注意力，中间容易被遗忘
- **三级约束**：硬约束（必须/禁止）→ 软指导（应/建议）→ 自由区域（AI 自主决策）
- **留白是设计选择**：自由区域不是遗漏，是有意识地让 AI 施展判断力

### Agent 工作流模式

- **三种任务类型**：query（只读）→ edit（单一修改）→ generate（完整布置，分阶段 A/B）
- **Skills 驱动**：工作流通过 Plugin 旁路加载，不污染全局配置
- **验证闭环**：Write → validate_layout（编译检查）→ 截图审查 → 修正循环

### 并行设计架构

> 详见 `docs/Flow_Agent_Parallel_Workflows.md`

- **Git Worktree 隔离**：多 Agent 实例在物理隔离的 Worktree 中并行工作
- **策略/变体/Worktree 三层**：Strategy（长期分支）→ Variant（局部尝试）→ Worktree（临时环境）
- **JSON 数据层合并**：交付阶段是数据写入，不是 git merge

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
- **Server 层**：使用 .NET 8.0

### AI 布置约束

```
对于每个要放置的模块：
1. bounds 必须完全在 computed.roomZones[].innerBoundary 内
2. bounds 不能与任何 computed.roomZones[].exclusionAreas 重叠
3. bounds 不能与其他已放置 modules[] 重叠
```

### 禁止事项

- Core 层引用 Revit API
- 直接让 AI 操作 SVG 代码（应操作 JSON）
- 使用 CSS `scaleY(-1)` 做坐标翻转

---

## 模块速查

### Agent

> 基于 Anthropic Agent SDK，采用 MainAgent + SubAgent 架构，通过 Skills（query/edit/generate-workflow）驱动三种工作流。详见 `BIMCanvas.Agent/README.md`

### Revit

> Phase 1（导出）完成，Phase 2（回写）待开发。6 阶段导出流程 + feet↔mm 坐标转换。详见 `BIMCanvas.Revit/README.md`

---

## 开发规范

### 数据格式

- **存储/传输**：JSON
- **AI 交互**：纯 JSON
- **渲染**：前端根据 JSON 渲染

### 坐标系统

- 坐标系：CAD 标准（原点左下角，Y 轴向上）
- 单位：毫米 (mm)
- 前端转换：`y_screen = canvasHeight - y_model`

### 编码注意

- 新建 `.cs` 文件后必须在 `.csproj` 中添加引用
- Edit 工具可能导致中文乱码，批量替换前先存档
- 优先编辑现有文件，不创建新文件
- **Agent 提示词/工作流修改必须改模板文件**（`BIMCanvas.Server/Templates/global-config/agent/`），不要修改用户目录下初始化出来的文件（`~/.bimcanvas/`）——后者由 Server 启动时从模板生成，直接改会被覆盖

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
# .NET Standard / .NET 8.0 项目（推荐）
dotnet restore BIMCanvas.Core
dotnet build BIMCanvas.Core --no-restore

# MSBuild 路径（备用）
"D:\Microsoft Visual Studio\2026\MSBuild\Current\Bin\MSBuild.exe"
```

### 运行

```bash
# .NET 8.0 项目
dotnet run --project BIMCanvas.Server

# .NET FW 控制台（必须直接执行 exe）
"bin/Debug/[项目名].exe"
```
