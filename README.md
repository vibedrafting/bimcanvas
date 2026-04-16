# BIMCanvas

BIMCanvas 是一款连接 AI 与 Revit 的室内设计辅助工具。它通过解析自然语言指令，自动生成符合空间逻辑的家具布局方案，并支持在 Web 端进行交互式调整，最终直接输出为可编辑的 Revit BIM 模型。

> **当前版本**: v3.1 | **数据架构**: File-Driven Architecture | **Agent 架构**: 主控 Agent + SubAgent

**核心竞争力**：实现从"自然语言创意"到"可编辑方案设计"的直接转化。

---

## 快速上手

### 环境要求

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js](https://nodejs.org/)（用于 Web 前端）
- [Git](https://git-scm.com/)（用于项目版本管理）
- [Python 3.10+](https://www.python.org/)（用于 Agent 服务，可选）
- [Docker Desktop / Docker Engine](https://www.docker.com/)（用于本地生产态烟测与后续服务器部署，可选）

### 启动模式

#### 1. Windows 开发态

推荐命令：

```bash
dotnet run --project BIMCanvas.Server
```

默认行为：

- 启动 Server API：`http://localhost:5000`
- 自动拉起 Web 开发服务器：`http://localhost:5173`
- 自动启动 Agent 服务
- 自动打开浏览器

首次启动会在 `%USERPROFILE%\Documents\BIMCanvas\` 下自动创建一组安全模板，并额外生成两个开发态私有补齐文件：

- `config.dev.local.json`
- `ccr_config.dev.local.json`

使用约定：

- 直连快测：把测试 `baseUrl`、`apiKey` 写入 `config.dev.local.json`
- CCR 快测：把测试 `Providers`、`Router` 写入 `ccr_config.dev.local.json`，并在设置 UI 或 `server_config.json` 中启用 `ccr.enabled=true`
- 这两份文件只在对应运行时配置文件首次创建时作为初始化种子读取一次
- 只要 `config.json` / `ccr_config.json` 已存在，后续启动一律以运行时文件本身为准
- 它们不进仓库，也不是设置 UI 的长期真源

#### 2. Windows 本机发布态

在项目根目录执行：

```bash
dotnet publish BIMCanvas.Server -c Release -o publish
```

然后运行：

双击 `publish/BIMCanvas.Server.exe` 即可一键拉起所有服务：

| 服务 | 地址 | 说明 |
|------|------|------|
| Server API | http://localhost:5000 | REST + SignalR 后端 |
| Web 前端 | http://localhost:5173 | 自动启动并打开浏览器 |
| Agent 服务 | 后台进程 | 自动启动（需 Python 环境） |

> 发布路径必须为项目根目录下的 `publish/` 文件夹（`-o publish`）。项目绝对路径因电脑而异，命令中无需写绝对路径，在项目根目录执行即可。

#### 3. Linux 服务器 Docker 部署

当前 Docker 基线是：

- `deploy/docker-compose.yml` + `deploy/docker-compose.server.yml` + `deploy/nginx.server.conf` 作为服务器编排入口
- `deploy/start.sh` 负责实例 bootstrap
- `instance.env` 只用于首次初始化与缺省值补齐
- 首页“实例设置”是实例内部应用配置的正式入口

---

## 解决的问题

| 问题 | 现状 | BIMCanvas 方案 |
|------|------|----------------|
| AI 理解门槛高 | Revit 格式复杂 | JSON 结构清晰，AI 可直接理解 |
| AI 设计是"空想" | 输出无法对应真实产品 | 族库提供真实家具 + Revit 模型 |
| 设计迭代慢 | 每次修改需打开 Revit | Web 画布实时协作 |

---

## 核心设计理念

### 文件驱动架构 (File-Driven Architecture)

> **核心理念：文件是唯一真理源，Server 是"文件播放器"而非"内存数据库"**

- **持久化优先**：所有业务数据以 JSON 文件形式存储在磁盘
- **Server 无状态**：Server 不"拥有"数据，只负责读取、聚合、分发文件内容
- **变更可追溯**：任何外部进程（Agent、脚本、手工编辑）修改文件后，系统自动感知并同步
- **Git 原生集成**：项目文件即 Git 仓库，分支/回滚/协作开箱即用

### 三层汉堡模型

| 层 | 目录 | 内容 | 权限 |
|---|---|---|---|
| 顶层 | `computed/` | room_zones, exclusions (禁区) | 自动生成 |
| 中层 | `schemes/` | strategy, zones, finishes, modules | AI/Server 可写 |
| 底层 | `baseline/` | walls, columns, openings, rooms, locationLines | 只读（Revit 导出） |

> **多策略隔离**：多个策略通过 **Git 分支** 隔离，而非 schemes/ 子目录。每个分支的 schemes/ 目录结构相同。

### 坐标系统

采用 **CAD 标准坐标系**（笛卡尔坐标系）：

| 属性 | BIMCanvas | Web 屏幕 |
|------|-----------|----------|
| 原点 | 左下角 | 左上角 |
| Y 轴 | **向上为正** | 向下为正 |
| 单位 | 毫米 (mm) | 像素 (px) |

---

## 技术架构

### 架构总览

```
┌─────────────────────────────────────────────────────────────────┐
│                      用户交互层                                   │
│         Web UI (Vue 3)  /  Claude Code (AI CLI)                 │
└──────────┬──────────────────────────────┬───────────────────────┘
           │ REST / SignalR               │ HTTP / SSE
           ▼                              ▼
┌───────────────────────┐      ┌───────────────────────┐
│  BIMCanvas.Server     │      │  BIMCanvas.Agent      │
│  (.NET 8.0)           │◄────►│  (Python 3.10+)       │
│  状态管理+通信中枢       │ HTTP │  MainAgent+SubAgent   │
│  Canvas-MCP 工具       │      │  AI 决策+工具调用       │
└───────────┬───────────┘      └───────────────────────┘
            │ 引用
┌───────────┴───────────┐
│  BIMCanvas.Core       │
│  (.NET Std 2.0)       │
│  数据模型+空间算法       │
└───────────────────────┘
            │ 引用
┌───────────┴───────────┐
│  BIMCanvas.Revit      │
│  (.NET FW 4.7.2)      │
│  Revit 导出+回写        │
└───────────────────────┘
```

### 组件角色定位

| 组件 | 比喻 | 核心职责 |
|------|------|----------|
| **BIMCanvas.Server** | 心脏 + 神经系统 | 状态管理、几何计算、通信中枢、事件分发 |
| **BIMCanvas.Agent** | 大脑 | 智能决策、理解意图、规划布置方案 |
| **BIMCanvas.Core** | 骨骼 | 数据结构、基础算法、类型定义 |
| **BIMCanvas.Web** | 皮肤 + 眼睛 | 渲染展示、用户交互 |
| **BIMCanvas.Revit** | 手臂 | 从 Revit 抓取数据、回写 Revit |

### Agent 架构

采用「主控 Agent + SubAgent」架构（基于 Anthropic Agent SDK）：

| 组件 | 角色 | 职责 |
|------|------|------|
| **主控 Agent** | 项目经理 | 任务协调、意图解析、结果整合 |
| **SubAgent** | 领域专家 | 专注单一领域任务执行 |
| **MCP 工具** | 工具箱 | 能力扩展、数据接口 |

**SubAgent 清单**：
- `layout-agent`：单房间设计专家（负责单房间 planning + placement，按是否携带定稿参考分析决定消费方式）

**Skill 工作流**：
- `query-workflow`：查询统计（查看布置状态、房间信息）
- `edit-workflow`：编辑操作（移动、删除、旋转家具）
- `generate-reference-analysis`：参考分析（`v1` 客观分析 → `v2` 差异分析 → `v3` 用户确认版）
- `generate-planning`：统一规划（`v0.1` 纯空间骨架 → `v0.2` 战略层方案 → `v0.3` 完整施工简报）
- `generate-placement`：按 `v0.3` 施工与验证
- `generate-zoning`：推导路径分区 helper

**关键设计原则**：
- Agent 只做决策，不做计算
- Agent 只发指令，不持状态
- Server 是通信中枢，负责状态管理和约束验证

### Server vs Agent 职责边界

| 维度 | Server（指挥中心） | Agent（设计师） |
|------|-------------------|-----------------|
| **状态管理** | ✅ 管理项目文件夹 | ❌ 无状态 |
| **几何计算** | ✅ Zone生成/禁区/innerBoundary | ❌ 不做几何计算 |
| **智能决策** | ❌ 不决定"放哪里" | ✅ 规划布置方案 |
| **约束验证** | ✅ 边界/碰撞检查 | ❌ 依赖 Server |
| **Git 操作** | ✅ Worktree 创建/合并 | ✅ 在 Worktree 中工作 |
| **通信中枢** | ✅ REST/WebSocket/SSE/MCP | ❌ 只通过 MCP/SSE |

---

## 技术栈

| 组件 | 技术 | 版本 | 选型理由 |
|------|------|------|----------|
| Core 类库 | .NET Standard | 2.0 | 同时兼容 .NET FW 4.7.2 和 .NET 6+ |
| Revit 插件 | .NET Framework | 4.7.2 | Revit API 限制 |
| Server 后端 | ASP.NET Core | 8.0 | REST + SignalR + SSE + Canvas-MCP |
| Agent 服务 | Python + Agent SDK | 3.10+ | 基于 Anthropic Agent SDK |
| Web 前端 | Vue 3 + TypeScript | 3.x | 响应式 + 类型安全 |
| 构建工具 | Vite | 5.x | 快速开发体验 |
| 状态管理 | Pinia | 2.x | Vue 3 官方推荐 |

---

## 项目结构

```
BIMCanvas/
├── BIMCanvas.Core/              核心类库 (.NET Standard 2.0)
│   ├── Models/                  数据模型 (Project, Zone, Module...)
│   └── Algorithms/              空间算法 (碰撞检测, 布置验证)
│
├── BIMCanvas.Server/            统一后端服务 (.NET 8.0)
│   ├── Controllers/             REST API (Project, Git, Validation...)
│   ├── Services/                项目管理、Git Worktree、方案数据、禁区计算
│   ├── McpTools/                Canvas-MCP 工具
│   ├── Hubs/                    SignalR Hub
│   └── Templates/               知识库 + 模块库 + 配置模板
│
├── BIMCanvas.Agent/             MainAgent 服务 (Python 3.10+)
│   ├── src/
│   │   ├── main.py              入口 (CLI + HTTP 服务)
│   │   ├── agent/               主控 Agent + SubAgent + Worktree 管理
│   │   ├── server/              HTTP 服务 (aiohttp + CORS)
│   │   ├── tools/               文件读写、布置、分区工具
│   │   ├── mcp/                 MCP 工具集成
│   │   └── config/              配置管理
│   └── templates/               系统提示词 + SubAgent 配置 + Skill 工作流
│
├── BIMCanvas.Revit/             Revit 插件 (.NET FW 4.7.2)
│   ├── Commands/                Ribbon 按钮命令
│   ├── Adapters/                Revit 元素适配器 (墙体/门窗/房间)
│   ├── Services/                导出服务、坐标转换、房间推断
│   └── Views/                   WPF 配置窗口
│
├── BIMCanvas.Web/               Web 前端 (Vue 3 + TypeScript)
│   └── src/
│       ├── components/          Canvas + UI 组件 (Ribbon, AI Command Center...)
│       ├── services/            Three.js 场景、交互工具、模块库
│       ├── composables/         组合式逻辑 (Chat, Screenshot, Selection...)
│       └── stores/              Pinia 状态管理
│
├── demos/                       示例 .bcp 项目文件
└── docs/                        架构文档、设计文档、工作流文档
```

---

## .bcp 项目格式

`.bcp` 是项目的标准交换格式，本质是包含以下结构的 ZIP 文件：

```
project.bcp (ZIP) → 解压为 Git 仓库
├── project.json              项目元数据
├── baseline/                 建筑基础数据（只读，Revit 导出）
│   ├── metadata.json         坐标转换参数
│   ├── architecture.json     墙体 + 柱子
│   ├── openings.json         门窗数据
│   ├── rooms.json            房间边界
│   ├── location_lines.json   完成面定位线
│   └── baseline.manifest     哈希校验
├── computed/                 计算派生数据（自动生成）
│   └── exclusions.json       禁区
├── schemes/{strategyId}/     方案设计数据（按策略分目录）
│   ├── strategy.json         策略元数据
│   ├── zones.json            设计区域划分
│   ├── finishes.json         完成面定义
│   └── modules.json          家具模块布置
├── context/                  上下文信息
│   └── requirements.md       用户需求描述
├── modules/                  模块素材库
│   ├── module_library.json   模块元数据
│   └── assets/               SVG 资源
└── .git/                     Git 仓库（v3.1 多策略通过分支隔离）
```

> 运行时设计规则不再放在项目目录 `knowledge/` 下，而是由 `<BIMCANVAS_HOME>/skills/*/references/` 中的 Skill 私有模板提供。

详细 Schema 见：[docs/Schema.md](./docs/Schema.md)

---

## 核心设计决策

| 决策点 | 选择 | 理由 |
|--------|------|------|
| 数据架构 | File-Driven + .bcp ZIP | 文件为真理源，Git 原生支持 |
| 多策略管理 | Git 分支隔离 | 每个策略一个分支，支持 diff 对比 |
| Agent 架构 | 主控 + SubAgent | 职责分离，支持并行执行 |
| 坐标系 | Y-Up (笛卡尔) | 符合 CAD/BIM/数学直觉 |
| 门扇区域 | 预计算为禁区 | KISS - AI 只需知道"这里不能放" |
| 布置单元 | modules（模块） | 支持单一家具或组合 |
| 模块朝向 | 语义化方向 | AI 友好，插件端转换为角度 |
| Core 运行时 | .NET Standard 2.0 | 同时兼容 .NET FW 4.7.2 和 .NET 6+ |

---

## 开发阶段

### Phase 1: 核心基础（MVP） ✅

**目标**：AI 可以在画布上设计，Web 可以显示

- ✅ 实现 Core 数据模型（Project, Zone, Module 等）
- ✅ 实现空间算法（CollisionDetector, PlacementValidator）
- ✅ 实现 Server 层项目加载（v3.1 文件驱动架构）
- ✅ 实现 Web 层项目数据加载
- ✅ 实现 Web 前端 3D 渲染（Three.js 引擎，双视图模式）

### Phase 2: Agent 集成 ✅

**目标**：智能布置助手自动化

- ✅ 实现 BIMCanvas.Agent 项目结构（Python + Anthropic Agent SDK）
- ✅ 实现 MainAgent + SubAgent 架构（layout-agent）
- ✅ 实现 HTTP 服务 + SSE 流式响应
- ✅ 实现 Skill 工作流系统（query / edit / generate）
- ✅ 实现 AI Command Center（Web 端对话 + 任务卡）

### Phase 3: 协作编辑 🔶

**目标**：AI 和用户可以实时协作

- ✅ 实现 Git Worktree 并行设计（Server 端分支管理）
- ✅ 实现元素拖拽/旋转交互（Move + Rotate + Ghost 预览）
- ✅ 实现模块库面板 + 放置工具（拖拽放置、连续放置）
- ✅ 实现 Web 端分支选择器 + 切换
- 🔶 实现 Visual Merge UI（分支合并向导已实现，冲突解决待完善）

### Phase 4: Revit 集成

**目标**：完整的 Revit 双向同步

- ✅ 实现 Revit → JSON 导出（6 阶段导出流程）
- ✅ 实现 .bcp 格式导出
- ⬜ 实现 JSON → Revit 同步（回写家具）
