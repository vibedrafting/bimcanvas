# BIMCanvas 技术文档改造计划

> **创建日期**: 2026-01-13
> **完成日期**: 2026-01-13
> **状态**: ✅ 已完成
> **目标**: 将 archives 中的 15 个旧文档重构为分类清晰、观点统一的新文档体系

---

## 一、改造原则

1. **复制优先**: 通过复制 archives 中老文档 + 重命名 + 局部修改的方式生成新文档
2. **分阶段执行**: 分 4 个批次，每批次 2-3 个文档
3. **保留源数据**: archives 中的老文档保持不变
4. **新旧优先**: 冲突时以新文档观点为准，标记旧观点为废弃

---

## 二、新文档命名规范

采用 `分类前缀_文档名.md` 格式，通过语义化前缀体现分类：

| 前缀 | 分类 | 说明 | 示例 |
|------|------|------|------|
| `PRD` | 产品与需求 | 产品定位、功能需求 | PRD.md |
| `Arch_` | 系统架构 | 整体架构、数据流、MCP 工具 | Architecture.md |
| `Schema` | 数据模型 | JSON Schema、数据结构 | Schema.md |
| `Flow_` | 业务流程 | 端到端流程、协作规范 | Flow_Workflows.md |
| `Agent_` | Agent 设计 | Agent 架构、提示词、SDK | Agent_Design.md |
| `Web_` | Web 前端 | 渲染系统、交互设计 | Web_Frontend.md |

> 注：PRD 和 Schema 作为独立文档，无需额外后缀

---

## 三、新旧文档映射表

### 3.1 保留核心文档（重命名 + 局部修正）

| 新文档名 | 源文档 | 修改范围 |
|----------|--------|----------|
| `PRD.md` | PRD.md (12-04) | 补充 File-Driven、OBB 规划师、Server-Agent 职责划分概念 |
| `Schema.md` | Schema-JSON-v3.md (12-30) | 保持不变（作为数据模型权威来源） |
| `Arch_MCP_Tools.md` | MCP-Tools-Spec.md (12-02) | **紧急修正**: §1.3 坐标系统改为 Y-up |

### 3.2 合并重组文档

| 新文档名 | 源文档 | 合并策略 |
|----------|--------|----------|
| `Architecture.md` | Architecture.md (12-29) + FileDrivenArchitecture.md (12-29) + Data_Flow_Guide.md (01-11) | 以 Data_Flow_Guide 为主，合并架构概述 |
| `Flow_Workflows.md` | Workflows.md (12-29) + Server_Agent_Workflow.md (01-10) | 以 Server_Agent_Workflow 为主，保留六阶段流程 |
| `Agent_Design.md` | Agent_Design_Spec.md (01-09) + Agent_Prompt_Design_Guide.md (01-13) | 以 Prompt Guide 为主，SubAgent 架构优先 |
| `Agent_SDK.md` | Agent_SDK_Technical_Guide.md (01-06) + AI_Parallel_Design_Patterns.md (12-30) | 以 SDK Guide 为主，保留并行设计模式 |
| `Web_Frontend.md` | SVG_Rendering_System.md (01-11) + Web_Loading_Sequence.md (01-11) | 合并为完整前端技术文档 |

### 3.3 独立保留文档

| 新文档名 | 源文档 | 说明 |
|----------|--------|------|
| `Agent_Spatial.md` | AISpatialUnderstanding.md (12-22) | 独立保留，OBB 规划师核心概念 |

---

## 四、分批次执行计划

### 批次 1: 核心架构文档 (3个)

**目标**: 建立系统架构基础

| 步骤 | 操作 | 源文件 | 目标文件 |
|------|------|--------|----------|
| 1.1 | 复制+重命名 | archives/Schema-JSON-v3.md | docs/Schema.md |
| 1.2 | 复制+修正 | archives/MCP-Tools-Spec.md | docs/Arch_MCP_Tools.md |
| 1.3 | 合并生成 | archives/Architecture.md + FileDrivenArchitecture.md + Data_Flow_Guide.md | docs/Architecture.md |

**1.2 修正内容** (MCP-Tools-Spec):
```markdown
§1.3 坐标与单位：
- 旧: 原点左上角，Y轴向下为正
- 新: 原点左下角，Y轴向上为正 (CAD标准)，Web 端渲染时转换 y_screen = height - y_model
```

**1.3 合并策略** (Architecture):
- 保留 Architecture.md 的系统概述和组件定义
- 用 Data_Flow_Guide.md 替换数据流章节
- 用 FileDrivenArchitecture.md 补充 Git Worktree 细节
- 修正项目文件结构为 Schema-JSON-v3 的定义（project.json、architecture.json）

---

### 批次 2: 产品与流程文档 (2个)

**目标**: 完善产品需求和业务流程

| 步骤 | 操作 | 源文件 | 目标文件 |
|------|------|--------|----------|
| 2.1 | 复制+补充 | archives/PRD.md | docs/PRD.md |
| 2.2 | 合并生成 | archives/Workflows.md + Server_Agent_Workflow.md | docs/Flow_Workflows.md |

**2.1 补充内容** (PRD):
- §4 架构章节补充:
  - File-Driven Architecture 概念说明
  - OBB 规划师设计约束
  - Server-Agent 职责划分表
- §6 SVG Schema 章节标记为过时，引用 Schema.md

**2.2 合并策略** (Workflows):
- 保留 Workflows.md 的六阶段流程框架
- 用 Server_Agent_Workflow.md 的 MVP/完整版定义补充 Phase 4
- 补充三层汉堡模型说明（引用 Schema.md）
- 补充 ChangeSource 机制说明（来自 Data_Flow_Guide）
- 修正 tags 生成时机为 Server 预计算

---

### 批次 3: Agent 设计文档 (3个)

**目标**: 统一 Agent 架构和 SDK 使用规范

| 步骤 | 操作 | 源文件 | 目标文件 |
|------|------|--------|----------|
| 3.1 | 合并生成 | archives/Agent_Design_Spec.md + Agent_Prompt_Design_Guide.md | docs/Agent_Design.md |
| 3.2 | 合并生成 | archives/Agent_SDK_Technical_Guide.md + AI_Parallel_Design_Patterns.md | docs/Agent_SDK.md |
| 3.3 | 复制+重命名 | archives/AISpatialUnderstanding.md | docs/Agent_Spatial.md |

**3.1 合并策略** (Agent_Design):
- 以 Agent_Prompt_Design_Guide.md 为主框架（最新）
- 标记单体 PlacementAgent 为废弃，采用主控+SubAgent 架构
- 保留 Agent_Design_Spec.md 的 Git Worktree 和三大工作场景
- 强调 query/execute 任务分类
- 保留提示词 <3000 字符限制

**3.2 合并策略** (Agent_SDK):
- 以 Agent_SDK_Technical_Guide.md 为主框架
- 标记 query() 推荐为废弃，主 Agent 用 ClaudeSDKClient
- 保留 AI_Parallel_Design_Patterns.md 的并行设计三大支柱
- 明确 MCP 是能力扩展，非 SubAgent 实现

---

### 批次 4: Web 前端文档 (1个)

**目标**: 合并前端技术文档

| 步骤 | 操作 | 源文件 | 目标文件 |
|------|------|--------|----------|
| 4.1 | 合并生成 | archives/SVG_Rendering_System.md + Web_Loading_Sequence.md | docs/Web_Frontend.md |

**4.1 合并策略**:
- SVG_Rendering_System.md 作为渲染引擎章节
- Web_Loading_Sequence.md 作为启动流程章节
- 统一命名空间和引用

---

## 五、冲突解决规则

### 5.1 文档新旧优先级

| 优先级 | 文档 | 日期 | 说明 |
|--------|------|------|------|
| 1 (最高) | Agent_Prompt_Design_Guide.md | 01-13 | Agent 架构权威 |
| 2 | Data_Flow_Guide.md | 01-11 | 数据流权威 |
| 3 | Server_Agent_Workflow.md | 01-10 | 协作流程权威 |
| 4 | Agent_Design_Spec.md | 01-09 | Git Worktree 参考 |
| 5 | Agent_SDK_Technical_Guide.md | 01-06 | SDK 技术参考 |
| 6 | Schema-JSON-v3.md | 12-30 | 数据模型权威 |
| 7 (最低) | MCP-Tools-Spec.md, PRD.md | 12-02/04 | 需要更新 |

### 5.2 具体冲突处理

| 冲突ID | 处理方式 |
|--------|----------|
| A1 (Agent架构) | 采用 SubAgent 架构，标记单体为废弃 |
| A2 (SDK使用) | 采用 ClaudeSDKClient，标记 query() 为废弃 |
| A3 (MCP定位) | MCP 是能力扩展，非 SubAgent |
| S1 (元数据文件名) | 采用 project.json |
| S2 (baseline结构) | 采用 architecture.json |
| S3 (schemes Git) | 采用独立 Git 仓库 |
| B1 (坐标系统) | 采用 Y-up，修正 MCP-Tools-Spec |
| S4 (防抖) | 采用 500ms |
| S5 (Undo/Redo) | 采用 ChangeSource 策略 |
| B2 (tags) | 采用 Server 预计算 |

---

## 六、最终文档清单

```
docs/
├── README.md                    # 文档索引（已存在，需更新）
│
├── PRD.md                       # 产品需求文档
├── Schema.md                    # JSON 数据模型规范
│
├── Architecture.md              # 系统架构总设计
├── Arch_MCP_Tools.md            # MCP 工具接口规范
│
├── Flow_Workflows.md            # 端到端业务流程
│
├── Agent_Design.md              # Agent 架构与提示词设计
├── Agent_SDK.md                 # Agent SDK 技术指南
├── Agent_Spatial.md             # AI 空间理解
│
├── Web_Frontend.md              # Web 前端技术
│
├── agent_sdk/                   # Agent SDK 官方文档
└── archives/                    # 旧版本文档归档
    ├── Agent_Design_Spec.md
    ├── Agent_Prompt_Design_Guide.md
    ├── Agent_SDK_Technical_Guide.md
    ├── AI_Parallel_Design_Patterns.md
    ├── AISpatialUnderstanding.md
    ├── Architecture.md
    ├── Data_Flow_Guide.md
    ├── FileDrivenArchitecture.md
    ├── MCP-Tools-Spec.md
    ├── PRD.md
    ├── Schema-JSON-v3.md
    ├── Server_Agent_Workflow.md
    ├── SVG_Rendering_System.md
    ├── Web_Loading_Sequence.md
    └── Workflows.md
```

---

## 七、验证清单

### 批次完成后验证

- [x] 新文档能正确链接到 README.md
- [x] 新文档内部引用正确（无死链）
- [x] 冲突观点已按规则处理
- [x] archives 中原文档保持不变

### 全部完成后验证

- [x] 所有 9 个新文档已生成
- [x] README.md 已更新为新文档索引
- [x] 文档间交叉引用已更新
- [x] 无遗漏的核心概念（补充了角度语义规范）

---

## 八、执行时间估算

| 批次 | 文档数 | 主要工作 |
|------|--------|----------|
| 批次 1 | 3 | 架构文档合并，MCP 坐标修正 |
| 批次 2 | 2 | PRD 补充，Workflows 合并 |
| 批次 3 | 3 | Agent 文档合并（最复杂） |
| 批次 4 | 1 | Web 前端合并 |

**总计**: 9 个新文档，4 个批次

---

## 九、执行记录

### 2026-01-13 执行完成

**Commits**:
- `89eac96` - 文档：重构技术文档体系（9 个新文档创建）
- `99212d3` - 文档：重命名 Arch_Overview.md 为 Architecture.md

**最终产出**:
| 文档 | 状态 | 说明 |
|------|------|------|
| PRD.md | ✅ | 补充 File-Driven、OBB 规划师 |
| Schema.md | ✅ | 复制自 Schema-JSON-v3.md |
| Architecture.md | ✅ | 合并 3 个文档 + 角度语义规范 |
| Arch_MCP_Tools.md | ✅ | 修正坐标系统为 Y-up |
| Flow_Workflows.md | ✅ | 合并 2 个文档 |
| Agent_Design.md | ✅ | 合并 2 个文档，采用 SubAgent 架构 |
| Agent_SDK.md | ✅ | 合并 2 个文档，推荐 ClaudeSDKClient |
| Agent_Spatial.md | ✅ | 复制自 AISpatialUnderstanding.md |
| Web_Frontend.md | ✅ | 合并 2 个文档 |
| README.md | ✅ | 更新为新文档索引 v2.0 |

**补充内容**:
- Architecture.md 新增 §7.x 角度语义规范（三套角度系统及转换规则）
