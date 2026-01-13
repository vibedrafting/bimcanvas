# BIMCanvas 技术文档索引

> 本文档索引 `docs/` 根目录下的技术文档，不含 `agent_sdk/` 和 `archives/` 子目录。
>
> **最后更新**: 2026-01-13

---

## 文档分类

### 1. 产品与需求

| 文档 | 核心内容 | 最后修改 |
|------|----------|----------|
| [PRD.md](PRD.md) | 产品需求文档 | 2025-12-04 |

### 2. 系统架构

| 文档 | 核心内容 | 最后修改 |
|------|----------|----------|
| [Architecture.md](Architecture.md) | 系统架构总设计 | 2025-12-29 |
| [FileDrivenArchitecture.md](FileDrivenArchitecture.md) | 文件驱动架构详解 | 2025-12-29 |
| [Data_Flow_Guide.md](Data_Flow_Guide.md) | 数据流与通信机制 | 2026-01-11 |

### 3. 数据模型

| 文档 | 核心内容 | 最后修改 |
|------|----------|----------|
| [Schema-JSON-v3.md](Schema-JSON-v3.md) | v3.0 JSON 数据模型规范 | 2025-12-30 |

### 4. 业务流程

| 文档 | 核心内容 | 最后修改 |
|------|----------|----------|
| [Workflows.md](Workflows.md) | 端到端执行流程 | 2025-12-29 |
| [Server_Agent_Workflow.md](Server_Agent_Workflow.md) | Server-Agent 协作工作流 | 2026-01-10 |

### 5. Agent 设计

| 文档 | 核心内容 | 最后修改 |
|------|----------|----------|
| [Agent_Design_Spec.md](Agent_Design_Spec.md) | PlacementAgent 架构设计 | 2026-01-09 |
| [Agent_Prompt_Design_Guide.md](Agent_Prompt_Design_Guide.md) | 提示词工程与 SubAgent 框架 | 2026-01-13 |
| [Agent_SDK_Technical_Guide.md](Agent_SDK_Technical_Guide.md) | Agent SDK 技术实现指南 | 2026-01-06 |
| [AI_Parallel_Design_Patterns.md](AI_Parallel_Design_Patterns.md) | AI 并行设计模式 | 2025-12-30 |

### 6. AI 能力

| 文档 | 核心内容 | 最后修改 |
|------|----------|----------|
| [AISpatialUnderstanding.md](AISpatialUnderstanding.md) | AI 空间理解与视觉增强 | 2025-12-22 |

### 7. 工具接口

| 文档 | 核心内容 | 最后修改 |
|------|----------|----------|
| [MCP-Tools-Spec.md](MCP-Tools-Spec.md) | Canvas-MCP 工具接口规范 | 2025-12-02 |

### 8. Web 前端

| 文档 | 核心内容 | 最后修改 |
|------|----------|----------|
| [SVG_Rendering_System.md](SVG_Rendering_System.md) | Three.js SVG 渲染系统 | 2026-01-11 |
| [Web_Loading_Sequence.md](Web_Loading_Sequence.md) | Web 启动流程与动画编排 | 2026-01-11 |

---

## 核心思想速览

### 产品与需求

| 文档 | 核心思想 |
|------|----------|
| **PRD** | 产品定位为"基于 AI CLI 的室内装修平面方案设计助手"，通过 Revit 插件启动，支持 AI 直出方案、Web 实时交互编辑、最后回写 Revit。整体架构分为 AI CLI → MCP Server 集群 → 核心库/Web/数据的三层体系。 |

### 系统架构

| 文档 | 核心思想 |
|------|----------|
| **Architecture** | 采用 File-Driven Architecture，文件是唯一真理源，Server 充当"文件播放器"而非内存数据库。使用三层汉堡模型（baseline/schemes/computed）分离建筑基础数据（只读）、方案设计数据（可写）、计算派生数据（自动生成）。 |
| **FileDrivenArchitecture** | 文件系统是连接 AI、Web、Server 和用户的通用总线。采用"磁盘即时同步 + Git 周期存档"的双层持久化策略，Git Worktree 实现 AI 提议方案的物理隔离与并发读写。 |
| **Data_Flow_Guide** | 阐述本地文件、Server 端、Web 端之间的三条核心数据流。引入 ChangeSource 枚举区分变更来源，根据来源动态决策历史管理策略，实现批量更新模式解决高频操作性能问题。 |

### 数据模型

| 文档 | 核心思想 |
|------|----------|
| **Schema-JSON-v3** | v3.0 从单一 JSON 升级为多文件夹结构，每个策略是独立 Git 仓库支持并行开发。核心设计约束是"AI = OBB 规划师"，AI 只操作矩形包围盒，Core 层负责转换为精确几何。 |

### 业务流程

| 文档 | 核心思想 |
|------|----------|
| **Workflows** | 完整设计流程分为 6 个阶段：数据准备 → 数据处理 → 区域确认 → 方案生成 → 交互修改 → 回写 Revit。支持三种 Agent 触发方式（AI 对话、Web 按钮、自动修正）和完整的错误恢复机制。 |
| **Server_Agent_Workflow** | 定义 MVP 版本（文件驱动 + 事后验证）和完整版（MCP 工具驱动 + 实时验证）两种协作模式。强调 Server 作为"约束管理者"不做布置决策，Agent 作为"智能决策者"不持有状态。 |

### Agent 设计

| 文档 | 核心思想 |
|------|----------|
| **Agent_Design_Spec** | PlacementAgent 作为"设计师"角色，负责智能决策但不持有状态。通过 Git Worktree 实现物理隔离的并行架构，支持策略分叉、布局求解器、主编式合并三大工作场景。 |
| **Agent_Prompt_Design_Guide** | 建立主控 Agent + SubAgent 分层架构，核心原则是任务类型分类（query 只读 vs execute 可写）、最小权限原则、行为边界约束。提示词需精简（<3000 字符）、结构化、包含防御性检查。 |
| **Agent_SDK_Technical_Guide** | Agent SDK 是 Claude Code CLI 的 Python 封装，核心理念是"Claude Code 底座 + 领域 MCP 工具"。`query()` 用于独立任务，`ClaudeSDKClient` 用于持久会话（支持 Hooks 和 Custom Tools）。 |
| **AI_Parallel_Design_Patterns** | 将 AI 从"聊天机器人"升级为"拥有无限分身的并发设计团队"，依靠文件驱动、异步协作、并行生成三大支柱。Git Worktree 是实现真正物理隔离和并发读写的最优方案。 |

### AI 能力

| 文档 | 核心思想 |
|------|----------|
| **AISpatialUnderstanding** | 定义 AI 作为"OBB 规划师"的核心隐喻，通过数据抽象、视觉增强和规则显性化消除人类视觉与 AI 逻辑的鸿沟。提出四层递进式增强方案：物理层 → 约束层 → 意图层 → 索引层。 |

### 工具接口

| 文档 | 核心思想 |
|------|----------|
| **MCP-Tools-Spec** | 详细定义 Canvas-MCP 的完整 MCP 工具接口，供 AI Agent 调用进行画布操作。采用"乐观锁 + 意图声明 + 版本感知"设计原则，工具集涵盖画布管理、元素操作、版本控制、查询分析和区域管理共 5 大类 17 个工具。 |

### Web 前端

| 文档 | 核心思想 |
|------|----------|
| **SVG_Rendering_System** | Three.js 中 SVG 模块轮廓在 3D 场景中的实时渲染机制。通过"父子 Group 方案"解决 Euler 旋转陷阱，使 SVG 轮廓正确压平到水平面；通过缓存、按需加载、depthTest 关闭等手段优化性能。 |
| **Web_Loading_Sequence** | 设计"电影式体验"的四阶段启动流程：蓝图构建 → UI 展开 → 场景搭建 → 就绪。通过 App.vue 作为总导演协调各阶段，实现从"无序 → 有序"、"蓝图 → 实体"的视觉隐喻。 |

---

## 文档演进时间线

```
2025-12
├── 12-02  MCP-Tools-Spec.md        工具接口定稿
├── 12-04  PRD.md                   产品需求文档
├── 12-22  AISpatialUnderstanding.md AI 空间理解
├── 12-29  Architecture.md          系统架构总设计
├── 12-29  FileDrivenArchitecture.md 文件驱动架构
├── 12-29  Workflows.md             业务流程
└── 12-30  Schema-JSON-v3.md        数据模型 v3.0
         AI_Parallel_Design_Patterns.md 并行设计模式

2026-01
├── 01-06  Agent_SDK_Technical_Guide.md SDK 技术指南
├── 01-09  Agent_Design_Spec.md     Agent 设计规范
├── 01-10  Server_Agent_Workflow.md Server-Agent 协作
├── 01-11  Data_Flow_Guide.md       数据流指南
│          SVG_Rendering_System.md  SVG 渲染系统
│          Web_Loading_Sequence.md  Web 启动流程
└── 01-13  Agent_Prompt_Design_Guide.md 提示词设计指南 (最新)
```

---

## 文档关联关系

```
PRD (产品定位)
 │
 ├──→ Architecture (系统架构) ←── FileDrivenArchitecture (架构模式)
 │         │
 │         ├──→ Schema-JSON-v3 (数据模型)
 │         │
 │         └──→ Data_Flow_Guide (数据流) ←── 最新补充
 │
 ├──→ Workflows (业务流程)
 │         │
 │         └──→ Server_Agent_Workflow (协作流程)
 │
 ├──→ Agent 设计体系
 │         │
 │         ├── AI_Parallel_Design_Patterns (并行架构愿景)
 │         │         ↓
 │         ├── Agent_SDK_Technical_Guide (SDK 技术验证)
 │         │         ↓
 │         ├── Agent_Design_Spec (Agent 规范细化)
 │         │         ↓
 │         └── Agent_Prompt_Design_Guide (提示词最佳实践) ← 最新
 │
 ├──→ MCP-Tools-Spec (工具接口)
 │
 ├──→ AISpatialUnderstanding (AI 能力)
 │
 └──→ Web 前端
           ├── SVG_Rendering_System (渲染引擎)
           └── Web_Loading_Sequence (启动体验)
```

---

## 观点冲突清单

> **原则**：新文档观点优先，旧文档观点标记为废弃。
>
> **检查日期**: 2026-01-13

### P0 - 架构级冲突 (需立即修正)

| ID | 冲突主题 | 旧观点 | 新观点 | 建议 |
|----|----------|--------|--------|------|
| A1 | **PlacementAgent 架构** | Agent_Design_Spec (01-09): 单体 PlacementAgent | Agent_Prompt_Design_Guide (01-13): 主控 Agent + SubAgent 集群 | ⚠️ 以新文档为准，单体架构已废弃 |
| A2 | **SDK 使用方式** | SDK_Technical_Guide v1.0-1.4: 推荐 `query()` | SDK_Technical_Guide v1.5: 主 Agent 应用 `ClaudeSDKClient` | ⚠️ `query()` 推荐已废弃，改用 `ClaudeSDKClient` |
| A3 | **MCP 工具定位** | SDK_Technical_Guide: MCP 可作为 SubAgent 替代 | Agent_Prompt_Design_Guide: MCP 是「能力扩展」，非 SubAgent 实现 | ⚠️ 以 Prompt Guide 为准 |
| S1 | **元数据文件名** | Architecture: `manifest.json` | Schema-JSON-v3: `project.json` | ⚠️ 以 Schema 为准 |
| S2 | **baseline 文件结构** | Architecture: `walls.json` + `columns.json` 分离 | Schema-JSON-v3: 合并为 `architecture.json` | ⚠️ 以 Schema 为准 |
| S3 | **schemes 独立 Git** | Architecture: 普通文件夹 | Schema-JSON-v3: 每个策略是独立 Git 仓库 | ⚠️ 以 Schema 为准 |
| B1 | **坐标系统** | Workflows: Y-up (CAD 标准) | MCP-Tools-Spec: Y-down (屏幕坐标) | ❓ 需统一，待核实代码实现 |

### P1 - 实现级冲突 (需计划更新)

| ID | 冲突主题 | 旧观点 | 新观点 | 建议 |
|----|----------|--------|--------|------|
| A4 | **任务类型分类** | SDK_Technical_Guide: 未区分任务类型 | Agent_Prompt_Design_Guide: 强制区分 `query`(只读) vs `execute`(可写) | 📝 新增强制规范 |
| A5 | **提示词长度限制** | 未提及 | Agent_Prompt_Design_Guide: SubAgent 提示词 < 3000 字符 | 📝 新增技术约束 |
| S4 | **防抖机制** | FileDrivenArchitecture: 防抖禁用 | Data_Flow_Guide: 防抖 500ms | ⚠️ 以 Data_Flow 为准 |
| S5 | **Undo/Redo 行为** | FileDrivenArchitecture: 外部修改清空 Undo 栈 | Data_Flow_Guide: 基于 ChangeSource 的策略表 | ⚠️ 以 Data_Flow 为准 |
| B2 | **tags 生成时机** | Workflows: AI 推断 tags | Server_Agent_Workflow: Server 预计算 tags | ⚠️ 以新文档为准 |
| B3 | **MVP vs 完整版** | 各文档标准不一 | 无统一定义 | 📝 需创建版本定义章节 |

### P2 - 待澄清冲突

| ID | 冲突主题 | 状态 | 建议 |
|----|----------|------|------|
| S6 | **Visual Merge UI** | 仅 FileDrivenArchitecture 提及，其他文档未涉及 | ❓ 确认是否保留或废弃 |
| S7 | **Opening 类型定义** | Architecture 用字符串 (`"door"`), Schema 用数字 (`0`) | ⚠️ 以 Schema 为准 |
| B4 | **MCP 工具集命名** | Canvas-MCP (画布操作) vs Agent 工具 (文件+Git) 混淆 | 📝 需统一命名 |

### 冲突详解

#### A1: PlacementAgent 架构演进

```
旧架构 (Agent_Design_Spec, 01-09):
  PlacementAgent (单体)
    ├── 理解意图
    ├── 选择模块
    └── 决定布置

新架构 (Agent_Prompt_Design_Guide, 01-13):
  主控 Agent (协调者)
    ├── zone-agent (分区专家)
    ├── layout-agent (布置专家)
    └── ... (其他 SubAgent)
```

**结论**: 单体 PlacementAgent 概念已演进为主控+SubAgent 分层架构。

#### S1-S3: 项目文件结构演进

```
旧结构 (Architecture, 12-29):
  project.bcp/
  ├── manifest.json           ← 已废弃
  ├── baseline/
  │   ├── walls.json          ← 已废弃
  │   ├── columns.json        ← 已废弃
  │   └── ...
  └── schemes/                ← 普通文件夹

新结构 (Schema-JSON-v3, 12-30):
  project/
  ├── project.json            ← 新命名
  ├── baseline/
  │   ├── architecture.json   ← 合并
  │   └── ...
  └── schemes/{s}/
      └── .git/               ← 独立 Git 仓库
```

**结论**: Schema-JSON-v3 定义了最新的项目结构规范。

---

## 深度冲突分析 (三轮交叉验证)

> **验证日期**: 2026-01-13
> **验证方法**: 6个并行Agent进行三轮交叉验证

### 紧急问题: 坐标系统严重矛盾

| 文档 | 坐标定义 | 状态 |
|------|----------|------|
| Architecture.md | Y-up (笛卡尔), 原点左下角 | ✅ 正确 |
| Schema-JSON-v3.md | Y-up (CAD标准), 原点左下角 | ✅ 正确 |
| Data_Flow_Guide.md | Y-up, Web 端转换 `y_screen = height - y_model` | ✅ 正确 |
| **MCP-Tools-Spec.md §1.3** | **Y-down (屏幕坐标), 原点左上角** | ❌ **错误** |

**结论**: MCP-Tools-Spec.md §1.3 坐标定义与其他所有文档矛盾，需紧急修正为 Y-up。

### 数据结构混淆: Zone 的两套定义

| 文件位置 | 用途 | Type 格式 | 说明 |
|----------|------|-----------|------|
| `computed/room_zones.json` | 自动生成的房间区域 | 数字 `0/1` | 0=禁区, 1=可设计区 |
| `schemes/{s}/zones.json` | 手动设计的可设计区域 | 字符串 | exclusion/circulation/designable |

**问题**: 两个 Zone 概念容易混淆，建议:
- 方案A: 统一为单一 Zone 模型，用 `schemeId` 区分来源
- 方案B: 明确重命名为 `RoomZone` 和 `DesignZone`

### 架构演进: 需废弃的早期设计

| 早期设计 | 出处 | 废弃原因 | 替代方案 |
|----------|------|----------|----------|
| `element_*` MCP 工具 | MCP-Tools-Spec (12-02) | 工具过多，无权限隔离 | SubAgent + 基础工具 (Read/Write/Edit) |
| `expectedVersion` 乐观锁 | MCP-Tools-Spec (12-02) | 无法追踪操作来源 | Git 分支管理 + ChangeSource |
| SSE 单向推送 | PRD (12-04) | 不支持双向通信 | SignalR WebSocket |
| 单一 Agent 模式 | MCP-Tools-Spec (12-02) | 容易越权、过度主动 | 主控 + SubAgent 分层 |
| SVG 核心存储格式 | PRD §6 (12-04) | 不利于 AI 直接编辑 | JSON 核心存储 (.bcp) |
| `pendingCommits` 确认流程 | MCP-Tools-Spec (12-02) | 手动确认冗余 | SignalR 实时推送 |

### 关键概念缺失检查

| 核心概念 | PRD | Workflows | MCP-Tools-Spec | 建议 |
|----------|-----|-----------|----------------|------|
| OBB 规划师 | ❌ | ❌ | - | 补充到 PRD §4 |
| File-Driven Architecture | ❌ | ❌ | ❌ | 补充到 PRD §4, Workflows §0 |
| Server-Agent 职责划分 | ❌ | ⚠️ 模糊 | - | 补充到 PRD §4, Workflows §7 |
| 三层汉堡模型 | - | ❌ | - | 补充到 Workflows §2 前 |
| ChangeSource 机制 | - | ❌ | - | 补充到 Workflows §5 |
| query/execute 分类 | - | - | ❌ | 补充到 MCP-Tools-Spec §1 |

### PRD 与实现文档一致性 (72/100)

| PRD 描述 | 实现现状 | 差异 | 建议 |
|----------|----------|------|------|
| SVG 是数据桥梁 (§6) | JSON 是核心，SVG 仅渲染 | ❌ 严重过时 | PRD §6 需重写为 JSON Schema |
| 快速布置/开启对话 (§3.3) | Web按钮/AI对话/自动修正 | ⚠️ 命名不对应 | 添加映射表 |
| Canvas-MCP export (§5.2) | 应为 JSON 导出 | ⚠️ 格式过时 | 更新工具定义 |

### 版本演进时间线

```
12月初 (早期设计)                      1月中 (最新设计)
─────────────────                      ─────────────────
Agent 定位:                            Agent 定位:
  单一工具调用者                    →    主控 Agent + SubAgent 集群

数据流驱动:                            数据流驱动:
  MCP 工具驱动                      →    文件驱动 + MCP 辅助

状态管理:                              状态管理:
  Server 持有状态                   →    文件是唯一真理源

通信机制:                              通信机制:
  SSE 单向推送                      →    SignalR WebSocket 双向

版本控制:                              版本控制:
  expectedVersion 乐观锁            →    Git 分支 + ChangeSource

工具设计:                              工具设计:
  element_* (17个工具)              →    query/execute 分层权限
```

### 新观点溯源

| 观点 | 首次出现 | 性质 | 说明 |
|------|----------|------|------|
| query/execute 任务分类 | Agent_Prompt_Design_Guide (01-13) | **教训驱动** | 源于 layout-agent 在查询任务中错误写入数据 |
| ChangeSource 机制 | Data_Flow_Guide (01-11) | **需求驱动** | 为支持"不同场景的历史管理策略"而设计 |
| SubAgent 架构 | Agent_Prompt_Design_Guide (01-13) | **演进** | 从单体 PlacementAgent 分层为主控+专家 |
| 提示词 <3000 字符 | Agent_Prompt_Design_Guide (01-13) | **技术约束** | 实践发现过长会导致 SubAgent 加载失败 |

---

## 子目录说明

| 目录 | 内容 |
|------|------|
| `agent_sdk/` | Anthropic Agent SDK 官方文档与示例代码 |
| `archives/` | 已归档的旧版本文档 |
