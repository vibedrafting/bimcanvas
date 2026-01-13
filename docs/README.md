# BIMCanvas 技术文档索引

> 本文档索引 `docs/` 根目录下的技术文档。
>
> **最后更新**: 2026-01-13
> **文档版本**: v2.0 (重构后)

---

## 文档总览

| 文档 | 分类 | 核心内容 |
|------|------|----------|
| [PRD.md](PRD.md) | 产品与需求 | 产品需求文档 |
| [Schema.md](Schema.md) | 数据模型 | JSON 数据模型规范 (v3.0) |
| [Architecture.md](Architecture.md) | 系统架构 | 系统架构总设计 |
| [Arch_MCP_Tools.md](Arch_MCP_Tools.md) | 系统架构 | MCP 工具接口规范 |
| [Arch_Converter.md](Arch_Converter.md) | 系统架构 | 转换器架构专题 |
| [Arch_DataFlow.md](Arch_DataFlow.md) | 系统架构 | 数据流场景分析专题 |
| [Flow_Workflows.md](Flow_Workflows.md) | 业务流程 | 端到端业务流程 |
| [Agent_Design.md](Agent_Design.md) | Agent 设计 | Agent 架构与提示词设计 |
| [Agent_SDK.md](Agent_SDK.md) | Agent 设计 | Agent SDK 技术指南 |
| [Agent_Spatial.md](Agent_Spatial.md) | Agent 设计 | AI 空间理解能力增强 |
| [Web_Frontend.md](Web_Frontend.md) | Web 前端 | Web 前端技术 |

---

## 文档分类

### 1. 产品与需求

| 文档 | 核心内容 |
|------|----------|
| [PRD.md](PRD.md) | 产品定位、功能需求、File-Driven Architecture、OBB 规划师约束 |

### 2. 系统架构

| 文档 | 核心内容 |
|------|----------|
| [Architecture.md](Architecture.md) | 整体架构、数据流、Git Worktree、组件职责 |
| [Arch_MCP_Tools.md](Arch_MCP_Tools.md) | Canvas-MCP 工具接口、Y-up 坐标系统 |
| [Arch_Converter.md](Arch_Converter.md) | 转换器分层架构、坐标转换公式、NTS 中间层、PlacementValidator 设计原则 |
| [Arch_DataFlow.md](Arch_DataFlow.md) | 五个典型场景调用链、REST/SignalR API 参考、脏数据追踪、批量更新模式 |

### 3. 数据模型

| 文档 | 核心内容 |
|------|----------|
| [Schema.md](Schema.md) | v3.0 JSON Schema、三层汉堡模型、OBB 约束 |

### 4. 业务流程

| 文档 | 核心内容 |
|------|----------|
| [Flow_Workflows.md](Flow_Workflows.md) | 六阶段流程、MVP/完整版工作流、ChangeSource 机制 |

### 5. Agent 设计

| 文档 | 核心内容 |
|------|----------|
| [Agent_Design.md](Agent_Design.md) | 主控 Agent + SubAgent 架构、提示词规范、行为边界 |
| [Agent_SDK.md](Agent_SDK.md) | ClaudeSDKClient 封装、并行设计模式、MCP 工具定位 |
| [Agent_Spatial.md](Agent_Spatial.md) | OBB 规划师哲学、视觉增强、设计场线、语义网格 |

### 6. Web 前端

| 文档 | 核心内容 |
|------|----------|
| [Web_Frontend.md](Web_Frontend.md) | 启动流程、SVG 渲染系统、坐标变换 |

---

## 核心思想速览

### 架构与数据

| 概念 | 说明 |
|------|------|
| **File-Driven Architecture** | 文件是唯一真理源，Server 是"文件播放器"而非内存数据库 |
| **三层汉堡模型** | baseline（只读）/ schemes（设计数据）/ computed（自动生成） |
| **Y-up 坐标系统** | CAD 标准，原点左下角，Web 渲染转换 `y_screen = height - y_model` |
| **OBB 规划师** | AI 只操作方向包围盒，不计算精确几何，Core 层负责转换 |

### Agent 设计

| 概念 | 说明 |
|------|------|
| **主控 Agent + SubAgent** | 分层架构，废弃单体 PlacementAgent |
| **query/execute 分类** | 查询任务只读，执行任务可写，防止越权 |
| **ClaudeSDKClient** | 主 Agent 推荐使用，支持 Hooks、Custom Tools、持久会话 |
| **MCP 工具定位** | MCP 是能力扩展（函数调用），非 SubAgent 实现方式 |
| **提示词限制** | SubAgent 提示词 < 3000 字符，过长会导致加载失败 |

### 协作流程

| 概念 | 说明 |
|------|------|
| **Server 角色** | 约束管理者，持有空间几何计算能力，不做布置决策 |
| **Agent 角色** | 智能决策者，不持有状态，依赖 Server 验证 |
| **ChangeSource 机制** | 区分变更来源，根据来源动态决策历史管理策略 |
| **tags 预计算** | 房间类型→功能标签映射由 Server 持有，Agent 只读取 |

---

## 文档关联关系

```
PRD (产品定位)
 │
 ├──→ Architecture (系统架构)
 │         │
 │         ├──→ Schema (数据模型)
 │         │
 │         ├──→ Arch_MCP_Tools (工具接口)
 │         │
 │         ├──→ Arch_Converter (转换器架构专题)
 │         │
 │         └──→ Arch_DataFlow (数据流场景分析专题)
 │
 ├──→ Flow_Workflows (业务流程)
 │
 ├──→ Agent 设计体系
 │         │
 │         ├── Agent_SDK (SDK 技术)
 │         │
 │         ├── Agent_Design (架构与提示词)
 │         │
 │         └── Agent_Spatial (空间理解)
 │
 └──→ Web_Frontend (前端技术)
```

---

## 已解决的冲突

> **重构日期**: 2026-01-13
>
> 以下冲突在文档重构过程中已统一处理。

| 冲突ID | 冲突主题 | 解决方案 |
|--------|----------|----------|
| B1 | 坐标系统 Y-down vs Y-up | **采用 Y-up**，已修正 Arch_MCP_Tools.md |
| A1 | Agent 架构：单体 vs SubAgent | **采用 SubAgent 架构**，已废弃单体模式 |
| A2 | SDK 使用：query() vs ClaudeSDKClient | **采用 ClaudeSDKClient**，已标记 query() 为废弃 |
| A3 | MCP 定位 | **MCP 是能力扩展**，非 SubAgent 实现 |
| S1 | 元数据文件名 | **采用 project.json** |
| S2 | baseline 结构 | **采用 architecture.json** |
| S4 | 防抖时间 | **采用 500ms** |
| S5 | Undo/Redo | **采用 ChangeSource 策略** |
| B2 | tags 生成时机 | **采用 Server 预计算** |

---

## 子目录说明

| 目录 | 内容 |
|------|------|
| `agent_sdk/` | Anthropic Agent SDK 官方文档与示例代码 |
| `archives/` | 旧版本文档归档（15个原始文档） |

### archives 目录内容

归档的原始文档供参考，新开发应使用 docs 根目录下的重构后文档：

| 原始文档 | 重构后位置 |
|----------|------------|
| Schema-JSON-v3.md | Schema.md |
| MCP-Tools-Spec.md | Arch_MCP_Tools.md |
| Architecture.md + FileDrivenArchitecture.md + Data_Flow_Guide.md | Architecture.md |
| PRD.md | PRD.md |
| Workflows.md + Server_Agent_Workflow.md | Flow_Workflows.md |
| Agent_Design_Spec.md + Agent_Prompt_Design_Guide.md | Agent_Design.md |
| Agent_SDK_Technical_Guide.md + AI_Parallel_Design_Patterns.md | Agent_SDK.md |
| AISpatialUnderstanding.md | Agent_Spatial.md |
| SVG_Rendering_System.md + Web_Loading_Sequence.md | Web_Frontend.md |

---

## 快速入门建议

### 新开发者
1. 阅读 [PRD.md](PRD.md) 了解产品定位
2. 阅读 [Architecture.md](Architecture.md) 了解系统架构
3. 阅读 [Schema.md](Schema.md) 了解数据模型

### Agent 开发
1. 阅读 [Agent_Design.md](Agent_Design.md) 了解架构与提示词规范
2. 阅读 [Agent_SDK.md](Agent_SDK.md) 了解 SDK 使用
3. 阅读 [Agent_Spatial.md](Agent_Spatial.md) 了解空间理解增强

### Web 前端开发
1. 阅读 [Web_Frontend.md](Web_Frontend.md) 了解前端技术栈

---

*文档重构完成于 2026-01-13，将 15 个原始文档整合为 11 个分类清晰的新文档（包含 2 个补全的专题文档）*
