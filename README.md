# BIMCanvas

BIMCanvas 是一款连接 AI 与 Revit 的室内设计辅助工具。它通过解析自然语言指令，自动生成符合空间逻辑的家具布局方案，并支持在 Web 端进行交互式调整，最终直接输出为可编辑的 Revit BIM 模型。

> **当前版本**: v3.0 | **数据架构**: File-Driven Architecture + .bcp 项目格式 | **Agent 架构**: 主控 Agent + SubAgent

**核心竞争力**：实现从"自然语言创意"到"可编辑 BIM 模型"的直接转化。

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

> **文件是唯一真理源，Server 是"文件播放器"而非"内存数据库"**

- **持久化优先**：所有业务数据以 JSON 文件形式存储在磁盘
- **Server 无状态**：Server 不"拥有"数据，只负责读取、聚合、分发文件内容
- **Git 原生集成**：项目文件即 Git 仓库，分支/回滚/协作开箱即用

### 三层汉堡模型

| 层 | 目录 | 内容 | 权限 |
|---|---|---|---|
| 顶层 | `computed/` | room_zones, exclusions (禁区) | 自动生成 |
| 中层 | `schemes/` | strategy, zones, finishes, modules | AI/Server 可写 |
| 底层 | `baseline/` | walls, columns, openings, rooms, locationLines | 只读 |

> **多策略隔离**：多个策略通过 **Git 分支** 隔离，而非 schemes/ 子目录。每个分支的 schemes/ 目录结构相同。

### JSON 为骨，SVG 为皮

| 层面 | 格式 | 职责 |
|------|------|------|
| 数据层（骨） | JSON | 存储、传输、AI 交互、业务逻辑 |
| 视图层（皮） | SVG | 渲染、显示、视觉反馈 |

**数据流**：AI 修改 JSON → WebSocket 推送 → 前端生成 SVG → 用户看到画布

### 坐标系统

采用 **CAD 标准坐标系**（非 Web 屏幕坐标系）：

| 属性 | BIMCanvas | Web 屏幕 |
|------|-----------|----------|
| 原点 | 左下角 | 左上角 |
| Y 轴 | 向上为正 | 向下为正 |
| 单位 | 毫米 (mm) | 像素 (px) |

---

## 技术架构

### 架构总览

```
┌─────────────────────────────────────────────────────────────────┐
│                      Claude Code (AI CLI)                        │
│                    用户与 AI 的对话交互入口                        │
└──────────────────────────────┬──────────────────────────────────┘
                               │ MCP Protocol
┌──────────────────────────────┼──────────────────────────────────┐
│                         MCP Server 集群                          │
├──────────────────────────────┴──────────────────────────────────┤
│   Revit-MCP (.NET FW 4.7.2)   提取建筑结构、创建 Revit 元素       │
└─────────────────────────────────────────────────────────────────┘
                               │ 引用
              ┌────────────────┼─────────────────────────┐
              ▼                ▼                         ▼
┌──────────────────┐  ┌───────────────────────┐  ┌────────────────────┐
│  BIMCanvas.Core  │  │  BIMCanvas.Server     │  │  BIMCanvas.Web     │
│  (.NET Std 2.0)  │  │  (.NET 6+)            │  │  (Vue 3 + TS)      │
│  数据模型+算法    │  │  MCP + REST + SignalR │  │  JSON → SVG 渲染   │
└──────────────────┘  └───────────┬───────────┘  └────────────────────┘
                                  │ SSE 事件流
                                  ▼
                      ┌───────────────────────┐
                      │  BIMCanvas.Agent      │
                      │  (Python 3.10+)       │
                      │  MainAgent            │
                      │  (主控 + SubAgent)    │
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
- `layout-agent`：家具布置专家
- `zone-agent`：空间分区专家
- 更多规划中...

**关键设计原则**：
- Agent 只做决策，不做计算
- Agent 只发指令，不持状态
- Server 是通信中枢，负责状态管理和约束验证

### 数据流向

```
【Revit → 画布】
Revit 模型 → BIMCanvas.Revit 提取 → Core 转换 JSON → Server 处理 → Web 渲染

【AI 布置方案】
AI 理解需求 → Library-MCP 搜索家具 → Canvas-MCP 修改 JSON → WebSocket 推送 → Web 渲染

【用户交互修改】
Web 拖拽 → 修改本地 JSON → REST API → Server 写入文件 → AI 可感知变化

【同步回 Revit】
导出 JSON → Core 解析 → Revit-MCP 创建元素
```

---

## 技术栈

| 组件 | 技术 | 版本 | 选型理由 |
|------|------|------|----------|
| Core 类库 | .NET Standard | 2.0 | 同时兼容 .NET FW 4.7.2 和 .NET 6+ |
| Revit 插件 | .NET Framework | 4.7.2 | Revit API 限制 |
| Server 后端 | ASP.NET Core | 6+ | MCP + REST + SignalR + SSE |
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
├── BIMCanvas.Server/            统一后端服务 (.NET 6+)
│   ├── McpTools/                Canvas-MCP + Library-MCP 工具
│   ├── Controllers/             REST API + SSE 事件端点
│   ├── Hubs/                    SignalR Hub
│   └── Services/                EventBus、状态管理、业务服务
│
├── BIMCanvas.Agent/             MainAgent 服务 (Python 3.10+)
│   ├── main_agent.py            主控 Agent
│   ├── subagents/               SubAgent 实现
│   │   ├── layout_agent.py      家具布置专家
│   │   └── zone_agent.py        空间分区专家
│   └── events/                  SSE 事件监听器
│
├── BIMCanvas.Revit/             Revit 插件 (.NET FW 4.7.2)
│   ├── Commands/                Ribbon 按钮命令
│   ├── Views/                   WPF 配置窗口
│   └── Adapters/                Revit 元素适配器
│
├── BIMCanvas.Web/               Web 前端 (Vue 3)
│   └── src/
│       ├── components/Canvas/   SVG 画布组件
│       ├── stores/              Pinia 状态
│       └── services/            SignalR 客户端、渲染器
│
├── docs/                        文档
└── external/Revit-MCP/          已有 Revit-MCP 项目
```

---

## .bcp 项目格式

`.bcp` 是项目的标准交换格式，本质是包含以下结构的 ZIP 文件：

```
project.bcp (ZIP)
├── project.json            项目元数据 + 方案列表
├── baseline/               建筑基础数据（只读）
│   ├── metadata.json       坐标转换参数
│   ├── architecture.json   墙体 + 柱子
│   ├── openings.json       门窗数据
│   ├── rooms.json          房间边界
│   └── location_lines.json 完成面定位线
├── computed/               计算派生数据（自动生成）
│   ├── room_zones.json     房间区域
│   └── exclusions.json     禁区
├── schemes/                方案设计数据（无子目录）
│   ├── strategy.json       策略元数据
│   ├── zones.json          设计区域划分
│   ├── finishes.json       完成面定义
│   └── modules.json        家具模块布置
├── context/                上下文信息
│   └── requirements.md     用户需求描述
├── knowledge/              知识库
│   └── placement_guide.md  布置规则指南
└── modules/                模块素材库
    ├── module_library.json 模块元数据
    └── assets/             SVG 资源目录
```

详细 Schema 见：[docs/Schema-JSON-v3.md](./docs/Schema-JSON-v3.md)

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

---

## 开发阶段

### Phase 1: 核心基础（MVP）

**目标**：AI 可以在画布上设计，Web 可以显示

- ✅ 实现 Core 数据模型（Project, Zone, Module 等）
- ✅ 实现空间算法（CollisionDetector, PlacementValidator）
- ✅ 实现 Server 层项目加载
- ✅ 实现 Web 层项目数据加载
- ⬜ 实现 Web 前端 JSON → SVG 渲染

### Phase 2: Agent 集成

**目标**：智能布置助手自动化

- ⬜ 实现 BIMCanvas.Agent 项目结构
- ⬜ 实现 MainAgent + SubAgent 架构
- ⬜ 实现 EventBus + SSE 事件机制
- ⬜ 实现三种触发方式（AI 对话、Web 按钮、自动修正）

### Phase 3: 协作编辑

**目标**：AI 和用户可以实时协作

- ⬜ 实现 Git Worktree 并行设计
- ⬜ 实现元素拖拽/旋转交互
- ⬜ 实现 Visual Merge UI（可视化合并）

### Phase 4: Revit 集成

**目标**：完整的 Revit 双向同步

- ✅ 实现 Revit → JSON 导出
- ✅ 实现 .bcp 格式导出
- ⬜ 实现 JSON → Revit 同步（回写家具）

---

## 相关文档

| 文档 | 说明 |
|------|------|
| [Architecture.md](./docs/Architecture.md) | 系统架构设计 |
| [Schema-JSON-v3.md](./docs/Schema-JSON-v3.md) | JSON 数据模型规范 |
| [Agent_Design.md](./docs/Agent_Design.md) | Agent 架构与提示词设计 |
| [PRD.md](./docs/PRD.md) | 产品需求文档 |
| [Flow_Workflows.md](./docs/Flow_Workflows.md) | 端到端业务流程 |
