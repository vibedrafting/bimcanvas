# BIMCanvas 系统架构

> **本文用途**：BIMCanvas 的系统架构总览——是什么、由哪些组件构成、数据怎么流动、各组件职责边界。是阅读其他架构专题文档的入口。
>
> **读者**：想整体理解 BIMCanvas 的工程师。各专题（平台/插件、Workflow 执行、设计交付物、数据格式、SDK 配置）见文末文档地图。
>
> **状态**：2026-06 当前态。

---

## 1. BIMCanvas 是什么

BIMCanvas 是连接 AI 与 BIM 的设计辅助工具：**自然语言 → AI 设计方案 → BIM 模型**。用户在 Web 画布上与 AI 协作，AI 在建筑平面内产出符合设计逻辑的方案，结果可同步回 Revit。

架构上，BIMCanvas 是 **通用 BIM-AI 平台基座 + 可插拔域插件（plugin）**：

- **平台基座**一次写、所有领域共享：几何/碰撞计算、文件驱动的项目存储、Web 画布、Agent 运行底座、Git 版本隔离、`.bcp` 项目格式。平台**绝不内置任何具体领域知识**。
- **域插件**封装某一垂直领域的全部业务（系统提示词、SubAgents、Skills、MCP 工具、设计规则、模块库）。首个也是当前唯一的域插件是**室内家具布置（interior-layout）**；未来的 MEP、精装点位、施工序列等都是独立插件。

平台与插件的边界、生命周期与安全模型见 [Arch_Plugin.md](./Arch_Plugin.md)。

## 2. 核心设计决策

| 决策 | 选择 | 理由 |
|------|------|------|
| **数据架构** | File-Driven Architecture | 文件是唯一真理源，Server 是"文件播放器"而非内存数据库 |
| **数据分层** | 三层模型 baseline / schemes / computed | 读写权限分明：只读 / 可写 / 自动生成 |
| **项目格式** | `.bcp`（ZIP 包） | 多文件夹结构，天然支持 Git 版本控制与传输 |
| **坐标系** | Y-Up 笛卡尔，单位 mm | 符合 CAD/BIM/数学直觉，仅前端渲染时转换 |
| **AI 抽象** | OBB 规划师 | AI 只操作有向包围盒（center + size + facing），精确几何由 Core 计算 |
| **平台/插件** | 平台基座 + 域插件 | 业务领域可插拔，平台零领域知识 |
| **多方案** | 指针式平级 + 采纳=翻指针 | 候选方案平级共存，切换生效只改一行指针，零复制零删除 |
| **AI 编排** | 确定性 JS workflow + 粗粒度 Agent | 流程骨架代码写死，领域判断交给 LLM |
| **Core 运行时** | .NET Standard 2.0 | 同时兼容 .NET FW 4.7.2（Revit）和 .NET 8（Server） |

## 3. 组件构成

| 组件 | 比喻 | 运行时 | 核心职责 |
|------|------|--------|----------|
| **BIMCanvas.Core** | 骨骼 | .NET Standard 2.0 | 数据模型、几何算法（碰撞/对齐/转换） |
| **BIMCanvas.Server** | 心脏 + 神经 | .NET 8.0 | 状态管理、几何计算、约束验证、通信中枢、Git Worktree |
| **BIMCanvas.Agent** | 大脑 | Python 3.10+ | 智能决策、意图解析、规划方案（基于 Claude Agent SDK） |
| **BIMCanvas.Web** | 皮肤 + 眼睛 | Vue 3 + Vite | 画布渲染、用户交互 |
| **BIMCanvas.Revit** | 手臂 | .NET FW 4.7.2 | 从 Revit 导出建筑数据、回写 Revit 模型 |

```
┌──────────────────────────────────────────────────────────────┐
│                    BIMCanvas.Web (Vue 3)                       │
│              画布渲染 · 拖拽编辑 · 与 AI 对话                    │
└───────────────┬───────────────────────────┬──────────────────┘
                │ REST / SignalR / SSE       │ /agent 代理
                ▼                            ▼
┌──────────────────────────────┐   ┌──────────────────────────┐
│     BIMCanvas.Server (.NET8)  │   │  BIMCanvas.Agent (Python) │
│  状态 · 几何 · 约束验证 · Git │◄──│  主控 Agent + SubAgent     │
│  Canvas-MCP (canvas 命名空间) │MCP│  + Workflow 编排           │
└───────────────┬──────────────┘   │  + 域插件 MCP 工具         │
                │ 读写 .bcp 文件     └──────────────────────────┘
                ▼
┌──────────────────────────────┐   ┌──────────────────────────┐
│   .bcp 项目（文件即真理源）   │   │   BIMCanvas.Revit (FW472) │
│   baseline/ schemes/ computed/│◄──│   导出建筑 / 回写模型       │
└──────────────────────────────┘   └──────────────────────────┘
        │
        └─ 引用 BIMCanvas.Core（.NET Standard 2.0，几何与数据模型）
```

**AI 入口**：用户在 Web 与 AI 对话，请求统一经 Server `/agent` 代理路由到 Agent 进程（开发态不再固定直连 Agent 端口）。Agent 基于 Claude Agent SDK 运行，不依赖 Claude Code CLI 作为对外入口。

## 4. 文件驱动架构

### 4.1 理念

> **文件是唯一真理源，Server 是"文件播放器"而非"内存数据库"。**

- **持久化优先**：所有业务数据以 JSON / Markdown 文件存储在 `.bcp` 项目目录。
- **Server 无状态**：Server 不"拥有"数据，只读取、聚合、分发、验证；任何外部进程（Agent、脚本、手工编辑、Git）改文件后系统自动感知同步（`ProjectWatcherService`，500ms 防抖）。
- **天然 Git**：项目即 Git 仓库，分支/回滚/多方案隔离开箱即用。

### 4.2 三层数据模型

| 层 | 目录 | 权限 | 内容 |
|----|------|------|------|
| **基准层** | `baseline/` | 只读（Revit 导出） | `metadata` 坐标参数 / `architecture` 墙柱 / `openings` 门窗 / `rooms` 房间 / `location_lines` 完成面定位线 |
| **方案层** | `schemes/` | AI / 用户 / Server 可写 | 设计方案：分区、家具布置、设计意图 |
| **计算层** | `computed/` | Server 自动生成 | `room_zones` 可设计区（从 rooms 派生）/ `exclusions` 禁区（门扇扫过区等） |

写入 gate 由 `ProjectContext.CheckWriteAllowed` 强制：路径落在 `baseline/` 或 `computed/` 返回 `readonly_zone` 拒绝。

### 4.3 .bcp 项目结构

```
project.bcp (ZIP)
├── project.json              项目元数据 + scene 绑定
├── baseline/                 建筑基础数据（只读，Revit 导出）
├── computed/                 计算派生数据（自动生成）
├── schemes/                  方案设计层（见下）
├── modules/                  模块素材库      ┐ 由 active 域插件的 projectMount
└── references/               设计规则（*.md）┘ 在打开项目时按需物化到项目全局
```

**方案层（schemes/）采用指针式平级模型**：每个设计区 `{zoneId}/` 下，`DESIGN.md` 用 frontmatter `adopted: {slug}` 指向当前生效方案，多个候选方案 `{slug}/` 平级共存，几何落在叶子 `modules.json`。**采纳 = 翻指针**，零复制零删除完全可逆。完整模型见 [Arch_Design_Delivery.md](./Arch_Design_Delivery.md)，字段级格式见 [Schema.md](./Schema.md)。

## 5. AI 设计能力

AI 设计任务不再是一份巨型提示词，而是 **确定性 JS workflow 编排 + 粗粒度 Agent 子任务 + 分级知识注入** 的混合架构：流程骨架（何时做、做几次、并行扇出、失败重试、副作用核验）由代码保证，每步的领域判断交给 Agent，领域规则独立成层按需加载。执行架构与实测教训见 [Arch_Workflow.md](./Arch_Workflow.md)。

主控 Agent 路由意图、派发 SubAgent / Skill / Workflow；领域能力全部由 active 域插件注入（五层投影：系统提示词 / SubAgents / Skills / MCP 工具 / 工具权限）。SDK 参数配置见 [Doc_SDK_Config.md](./Doc_SDK_Config.md)。

## 6. Server 与 Agent 职责边界（铁律）

| 维度 | Server | Agent |
|------|--------|-------|
| 状态管理 | ✅ 管理项目文件 | ❌ 无状态 |
| 几何计算 | ✅ 区域生成 / 禁区 / 碰撞 / 边界 | ❌ 不做几何计算 |
| 约束验证 | ✅ 边界 / 碰撞检查 | ❌ 依赖 Server |
| Git 操作 | ✅ Worktree 创建 / 隔离 | ✅ 在 Worktree 内工作 |
| 通信中枢 | ✅ REST / SignalR / SSE / MCP | ❌ 只经 MCP / 代理通信 |
| **智能决策** | ❌ 不决定"放哪里" | ✅ 规划布置方案 |
| 意图解析 | ❌ | ✅ 自然语言 → 设计意图 |

两条不可越线：**Server 不做决策**（不决定"沙发放哪里"，只验证和计算）；**Agent 不持状态、不做几何计算**（只发设计意图，几何与状态由 Server / 文件承担）。

## 7. 通信与 MCP 工具

- **REST / SignalR / SSE**：Web ↔ Server 的状态同步、文件变更广播（`SceneArtifactUpdated` 等通用事件）、交互带外通道。
- **Canvas-MCP**（命名空间 `canvas`，平台一次写、所有插件共享，5 个工具）：`canvas_vision`（截图/识图）、`create_job` / `complete_job`（并行工作环境）、`load_artifact`（读 scene 数据）、`validate_layout`（几何/碰撞校验）。
- **插件 MCP**：域工具走插件自己的命名空间（如 `mcp__interior-layout__*`），以 Python `register(builder)` 范式在 Agent 进程内注册。

工具不再是独立的 MCP server 集群，也不再是 Server 端 C# 实现——全部是 Agent 进程内的 in-process MCP。契约见 [Arch_Plugin.md](./Arch_Plugin.md) §4。

## 8. 坐标系统

BIMCanvas 采用 **CAD 标准笛卡尔坐标系**：

| 属性 | BIMCanvas | Web 屏幕 |
|------|-----------|----------|
| 原点 | 左下角 | 左上角 |
| Y 轴 | 向上为正 | 向下为正 |
| 单位 | 毫米 (mm) | 像素 (px) |

各层职责：Revit 导出 Y-up/mm → Core 纯笛卡尔运算、不转换 → Web 渲染时 `y_screen = height - y_model`、事件反向转换（禁用 CSS `scaleY(-1)`）。项目中存在数据模型角（CCW+）/ 交互角（CW+）/ Three.js 角三套角度系统，混用会导致方向相反的 bug，转换规范见 `BIMCanvas.Web/README.md`。

## 9. Git 与多方案

- **存储层**：单仓库多分支，所有数据在一个 `.git` 历史。
- **执行层**：并行设计任务用 Git Worktree 物理隔离（`create_job` / `complete_job` 创建与收口工作环境），互不干扰。
- **多方案采纳**：候选方案平级共存于 `schemes/{zoneId}/{slug}/`，生效由 `DESIGN.md` 的 `adopted` 指针决定，**采纳即翻指针**——不再依赖分支合并 / 复制式收口。落选方案完全可回溯。

## 10. 项目结构

```
BIMCanvas/
├── BIMCanvas.Core/       核心类库（数据模型 + 几何算法）.NET Standard 2.0
├── BIMCanvas.Server/     统一后端（REST/SignalR/SSE/Canvas-MCP）.NET 8.0
├── BIMCanvas.Agent/      AI Agent（主控 + SubAgent + Workflow）Python
│   └── plugins/core-base/  平台基座插件（提示词 + canvas MCP 工具）
├── BIMCanvas.Revit/      Revit 插件（导出 / 回写）.NET FW 4.7.2
├── BIMCanvas.Web/        Web 前端（画布渲染 + 交互）Vue 3 + Vite
├── BIMCanvas.sln         解决方案（含全部 .NET 项目）
└── docs/                 对外技术文档（本目录）
```

域插件（如 interior-layout）是**独立 GitHub 仓库**，通过 install 流程下载到运行时目录，不在主仓库内。

## 11. 文档地图

| 主题 | 文档 |
|------|------|
| 平台 / 插件体系（边界、生命周期、安全模型、manifest、MCP 契约） | [Arch_Plugin.md](./Arch_Plugin.md) |
| Workflow 执行架构（五层 / 五段流 / 确定性控制流 / 实测教训） | [Arch_Workflow.md](./Arch_Workflow.md) |
| 前端架构（渲染分层 / 状态通信 / 两种运行时 / 插件 Web 扩展） | [Arch_Web.md](./Arch_Web.md) |
| 设计经验（让 AI 胜任室内设计的有效灵感 / 设计哲学） | [Design_Insights.md](./Design_Insights.md) |
| 设计交付物数据模型（指针式平级 / Zone 递归嵌套 / 采纳=翻指针） | [Arch_Design_Delivery.md](./Arch_Design_Delivery.md) |
| .bcp 数据格式字段级规范 | [Schema.md](./Schema.md) |
| Agent ↔ Web 实时流协议契约 | [Arch_Stream_Protocol.md](./Arch_Stream_Protocol.md) |
| Claude Agent SDK 参数配置 | [Doc_SDK_Config.md](./Doc_SDK_Config.md) |
