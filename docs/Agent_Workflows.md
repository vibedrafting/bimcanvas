# BIMCanvas Agent 运行时工作流

> **版本**：v1.1 | **更新日期**：2026-03-01
> **定位**：Agent 运行手册 — 从请求进入到结果输出的完整链路
>
> **相关文档**：
> - [Flow_Workflows.md](Flow_Workflows.md) — 端到端 6 阶段业务流程（宏观视角）
> - [Agent_Design.md](Agent_Design.md) — Agent 架构设计决策
> - [Agent_Skills_Design.md](Agent_Skills_Design.md) — Skills 工作流封装机制
> - [Arch_MCP_Tools.md](Arch_MCP_Tools.md) — MCP 工具接口规范

---

## 1. 概述

BIMCanvas Agent 是基于 Anthropic Claude Agent SDK 的 AI 室内布置助手，在建筑平面内为用户规划和布置家具。

```
用户请求
  │
  ▼
HTTP Server (aiohttp)           ← /api/chat/stream (SSE)
  │
  ▼
MainAgent (Claude Agent SDK)    ← 意图分析、任务分类、直接执行
  │
  ├──→ [预留] SubAgent          ← 已设计未启用（enabled: false）
  │
  └──→ Canvas MCP Tools         ← validate_layout / screenshot
  └──→ Workflow Skills           ← query-workflow / edit-workflow / generate-workflow
         │
         ▼
  .NET Server (BIMCanvas.Server) ← 几何验证、截图渲染、数据持久化
```

> **当前状态**：SubAgent 机制已设计但未启用，MainAgent 通过 `self.chat()` 直接处理所有任务类型（query / edit / generate）。详见 [§8 SubAgent 设计预留](#8-subagent-设计预留)。

### 运行模式

| 模式 | 命令 | 用途 |
|------|------|------|
| HTTP 服务 | `python -m src.main --serve` | 生产环境，Web 前端通过 SSE 调用 |
| 交互式 CLI | `python -m src.main` | 开发调试 |

---

## 2. 系统启动流程

### 2.1 配置加载

Agent 启动时从 `~/.bimcanvas/` 加载全部配置（首次运行自动从 `templates/` 初始化）：

```
~/.bimcanvas/
├── config.json              ← 模型、Token、权限、服务器配置
├── BIMCANVAS.md             ← 主控 Agent 系统提示词
├── .claude-plugin/
│   └── plugin.json          ← Plugin 清单（旁路加载 Skills）
├── agents/
│   └── layout-agent.md      ← SubAgent 配置（frontmatter + 提示词）
├── skills/                  ← Skills 按需加载
└── knowledge/
    └── placement_guide.md   ← 布置规则知识库
```

**加载优先级**：环境变量 > config.json > 默认值

**config.json 关键字段**：model、maxTokens、defaultEffort、defaultThinking、permissions、server。详见 `templates/config.json`。

### 2.2 Agent SDK 初始化

MainAgent 通过 `ClaudeAgentOptions` 配置 SDK：

| 参数 | 说明 |
|------|------|
| `model` | 模型名称（默认值见 config.json） |
| `system_prompt` | BIMCANVAS.md 内容 |
| `thinking` | ThinkingConfigAdaptive 或 ThinkingConfigDisabled |
| `thinking_effort` | "low" / "medium" / "high" / "max" |
| `include_partial_messages` | `True`（启用流式文本输出） |
| `tools` | 内置工具列表 |
| `mcp_servers` | `{"canvas": canvas_mcp}` |
| `agents` | `{}`（SubAgent 未启用，`create_subagents()` 返回空字典） |
| `plugins` | `[{"type": "local", "path": "~/.bimcanvas"}]` |

### 2.3 MCP 工具注册

Canvas MCP Server 在进程内直接注册（无 IPC 开销）。

**工具调用名规则**：`mcp__{mcp_servers字典key}__{@tool装饰器名}`

| 工具 | 调用名 |
|------|--------|
| validate_layout | `mcp__canvas__validate_layout` |
| request_background_screenshot | `mcp__canvas__request_background_screenshot` |

> **注**：`get_workflow_guide` 已迁移到 Skills 机制（query-workflow / edit-workflow / generate-workflow），不再作为 MCP 工具注册。

### 2.4 SubAgent 加载（当前未启用）

`create_subagents()` 从 `~/.bimcanvas/agents/*.md` 解析 YAML frontmatter，但当前**返回空字典**：

- `init_manifest.json` 中 `layout-agent.md` 被设为 `enabled: false`
- `~/.bimcanvas/agents/` 目录未初始化（首次运行不会创建）
- 因此 MainAgent 的 `agents` 参数为 `{}`，不会派发任何 SubAgent

**预留配置格式**（启用后将使用）：

```yaml
---
name: layout-agent
description: 家具布置专家。用于空间规划、家具摆放、布局优化任务。
tools: Read, Write, Glob
model: inherit
---
```

### 2.5 HTTP 服务启动

- 多窗口支持：每个 `windowId` 对应独立的 Agent 实例（缓存在内存中）
- CORS 启用：支持 Web 前端跨域调用

---

## 3. 请求处理总流程

### 3.1 HTTP 入口

**端点**：`POST /api/chat/stream`

**请求体**：
```json
{
    "projectPath": "path/to/project",
    "windowId": "primary",
    "message": "帮我布置客厅",
    "model": "<model-id>",
    "effort": "high",
    "thinking": "adaptive"
}
```

### 3.2 意图分析与任务分类

MainAgent 收到用户消息后，根据关键词判断任务类型：

| 类型 | 触发关键词 | 性质 |
|------|-----------|------|
| **query** | 有多少、统计、查看、列出、当前状态 | 只读 |
| **edit** | 移动、删除、旋转、调整 | 单一修改 |
| **generate** | 布置、设计、创建、生成、规划 | 完整布置 |

### 3.3 工作流 Skill 自动加载

分类后，Claude 根据任务类型自动触发对应的工作流 Skill：

| 任务类型 | Skill | 触发关键词 |
|----------|-------|-----------|
| query | `query-workflow` | 统计、查看、列出、有多少 |
| edit | `edit-workflow` | 移动、删除、旋转、调整 |
| generate | `generate-workflow` | 布置、设计、创建、生成、规划 |

Skill 内容以系统指令形式注入上下文，MainAgent 严格按照 Skill 中定义的步骤执行。

### 3.4 SSE 事件流

响应通过 Server-Sent Events 实时推送：

```
data: {"type": "thinking", "content": "分析用户意图..."}
data: {"type": "text", "content": "好的，我来帮你布置客厅"}
data: {"type": "tool_call_start", "toolCallId": "tc-1", "toolName": "Read", "toolParams": {...}}
data: {"type": "tool_call_output", "toolCallId": "tc-1", "toolOutput": "..."}
data: {"type": "tool_call_complete", "toolCallId": "tc-1", "success": true}
data: [DONE]
```

> **注**：SSE 协议中预留了 `subagent_start` / `subagent_complete` 事件类型，但当前 SubAgent 未启用，不会产生这些事件。

---

## 4. Query 工作流（只读查询）

### 触发条件

关键词："统计"、"查看"、"列出"、"有多少"、"当前状态"

### 执行流程

```
用户请求（如"统计当前卧室有多少家具"）
  │
  ▼
1. 自动加载 query-workflow Skill
  │
  ▼
2. [可选] 调用 request_background_screenshot 查看空间截图
  │
  ▼
3. Read 目标数据文件（如 schemes/{zoneId}/modules.json）
  │
  ▼
4. 空数据检查
  ├── 空 → 报告"数量为 0"
  └── 非空 → 分析/统计
  │
  ▼
5. 验证报告内容与文件实际内容一致
  │
  ▼
6. 返回结果
```

### 工具权限

| 允许 | 禁止 |
|------|------|
| Read, Glob, Grep | Write, Edit |

### 约束

- 禁止根据房间信息推断/编造不存在的模块
- 空数据时报告"数量为 0"，禁止自动创建示例数据
- 所有统计结果必须基于实际读取的文件内容

### 示例

| 用户输入 | 执行动作 |
|----------|----------|
| "统计当前卧室有多少家具" | Read modules.json → 统计 zoneId 为卧室的模块数量 |
| "查看客厅布置状态" | Read modules.json → 筛选客厅区域的模块并展示 |

---

## 5. Edit 工作流（单一修改）

### 触发条件

关键词："移动"、"删除"、"旋转"、"调整"

### 执行流程

```
用户请求（如"移动沙发到靠窗位置"）
  │
  ▼
1. 自动加载 edit-workflow Skill
  │
  ▼
2. [可选] 调用 request_background_screenshot 查看修改前状态
  │
  ▼
3. Read schemes/{zoneId}/modules.json
  │
  ▼
4. 定位目标模块
  │
  ▼
5. 执行修改（修改 bounds/facing）
  │
  ▼
6. 预检约束（门前净空、通道宽度）
  │
  ▼
7. Write 保存结果
  │
  ▼
8. 调用 validate_layout() 编译检查
  │
  ├── 通过（0 个错误）→ 修改完成
  │
  └── 失败 → 根据错误报告修正 → Write → 再次 validate_layout
  │
  ▼
9. [可选] 调用截图工具验证视觉效果
```

validate_layout 错误代码及返回格式详见 [§7.2](#72-validate_layout)。

### 修正策略

按优先级尝试：平移 → 旋转 → 缩小 → 替换 → 移除

### 示例

| 用户输入 | 执行动作 |
|----------|----------|
| "移动沙发到靠窗位置" | Read → 修改 bounds → Write → validate_layout |
| "删除茶几" | Read → 移除对应项 → Write → validate_layout |
| "旋转床 90 度" | Read → 修改 facing 和 bounds → Write → validate_layout |

---

## 6. Generate 工作流（完整布置）

这是最复杂的工作流，分两个阶段执行，每个阶段包含"编译检查 + 截图审查 + 修正循环"。

### 触发条件

关键词："布置"、"设计"、"创建"、"生成"、"规划"

### 流程总览

```
前置准备（步骤 1-5，只读）
  │
  ▼
阶段 A：放置锚点 + 主要家具
  │  Write → validate_layout → [失败则修正] → 截图审查
  │  [设计违规] → 修正循环 A（最多 1 次）
  │
  ▼
阶段 B：补充辅助家具
  │  Read A 结果 → Write（合并全部）→ validate_layout → 截图审查
  │  [设计违规] → 修正循环 B（最多 1 次）
  │
  ▼
报告结果（仅在自审通过后）
```

**核心原则**：不要一次性放置全部家具。分两阶段放置，每阶段"编译检查 + 截图审查"，自主修正后再继续。

---

### 6.1 执行前强制检查清单

在执行任何 Write 操作前，以下步骤必须全部完成：

| # | 必读文件 | 用途 | 并行规则 |
|---|----------|------|----------|
| 1 | 前置截图（`request_background_screenshot`） | 理解空间形态、门窗位置 | **单独调用** |
| 2 | `knowledge/placement_guide.md` | 布置规则（尺寸标准、房间要点） | 2-7 可并行 |
| 3 | `modules/README.md` | 模块库架构（双层：契约层+意图层） | 2-7 可并行 |
| 4 | `modules/module_library.json` | 家具尺寸（禁止编造） | 2-7 可并行 |
| 5 | `computed/room_zones.json` | 设计区域边界 | 2-7 可并行 |
| 6 | `computed/exclusions.json` | 禁区数据 | 2-7 可并行 |
| 7 | `baseline/openings.json` | 门窗位置 | 2-7 可并行 |

**任何步骤缺失，禁止执行 Write 操作。**

---

### 6.2 前置准备

分两步执行：先单独调用截图工具查看空间，再并行读取设计规范、模块库、空间数据（步骤 2-5）。详见 `generate-workflow` Skill。

---

### 6.3 阶段 A：布置锚点 + 主要家具

#### 家具分层定义

| 层级 | 定义 | 示例 |
|------|------|------|
| **锚点家具** | 决定房间布局核心定位 | 床、电视柜、餐桌、书桌 |
| **主要家具** | 围绕锚点的功能家具 | 衣柜、沙发、床头柜、餐椅 |
| **辅助家具** | 填充和装饰性家具 | 茶几、边几、梳妆台、落地灯 |

**成套依赖必须同阶段放置**：床+床头柜、书桌+椅子、餐桌+餐椅。

#### 6A. 放置前预检

对每件要放置的家具，在确定坐标前预检门前净空和通道宽度要求。

> bounds 范围、重叠、禁区冲突等几何检查将在写入后由 `validate_layout` 自动完成，无需心算。

如果检查不通过，**在放置前**调整位置或朝向，不要先写入再修正。

#### 7A. 写入阶段 A 结果

```
Write schemes/{zoneId}/modules.json
```

**路径规范**：
- 正确：`schemes/rz_1/modules.json`（分区 1）
- 正确：`schemes/rz_2/modules.json`（分区 2）
- 错误：`schemes/modules.json`（已废弃）

**查找分区**：先读取 `schemes/zones.json` 获取所有分区 ID。

**数据格式**（id 由 Server 在 validate_layout 时自动生成，禁止手动填写）：

```json
[
  {
    "moduleId": "mod_bed_001",
    "bounds": [[x1,y1], [x2,y2], [x3,y3], [x4,y4]],
    "facing": "north",
    "items": []
  }
]
```

#### 8A. 阶段 A 验证

**8A.1 布局编译检查（必须）**

```
mcp__canvas__validate_layout()
```

自动检测几何错误：越界(E001)、墙体重叠(E002)、柱子重叠(E003)、禁区重叠(E004)、模块间重叠(E005)。

- 验证通过（0 个错误）→ 进入 8A.2 截图设计审查
- 验证失败 → 进入修正循环 A

**8A.2 截图设计审查**

```
mcp__canvas__request_background_screenshot(
  projectPath="{当前工作目录}",
  viewport={"mode": "full"}
)
```

**对照 `generate-workflow` Skill 中的设计检查清单执行截图审查**（硬性约束 H4-H5 + 设计规则 S1-Sn）。

- 检查全部通过 → 跳到阶段 B
- 有违反 → 进入修正循环 A

#### 修正循环 A（最多 1 次）

按优先级修正违规项：平移 → 旋转 → 缩小 → 替换 → 移除。修正后重新 validate_layout 确认。

详细步骤见 `generate-workflow` Skill。

**修正原则**：最小化变动，只改违规家具，不动已通过验证的家具。

---

### 6.4 阶段 B：补充辅助家具

**前提**：阶段 A 家具已验证通过。

#### 6B. 规划辅助家具

1. Read 当前 `schemes/{zoneId}/modules.json`（获取阶段 A 已放置的家具）
2. 在已有布局基础上，规划辅助家具位置
3. 对每件辅助家具执行同样的放置前预检

#### 7B. 写入完整结果

```
Write schemes/{zoneId}/modules.json
```

**必须包含阶段 A 已有的全部家具 + 阶段 B 新增的辅助家具**，不能只写新增部分。

#### 8B. 最终验证

**8B.1 布局编译检查（必须）**

```
mcp__canvas__validate_layout()
```

检测全部家具（阶段 A + B）的几何错误。

**8B.2 截图设计审查**

对照 `generate-workflow` Skill 中的完整设计检查清单执行审查。

#### 修正循环 B（最多 1 次）

步骤同修正循环 A。

**如果修正后仍有违规**：移除违规的辅助家具，保留核心布局（锚点+主要家具），再次调用 `validate_layout` 确认，在报告中说明被移除的家具及原因。

---

### 6.5 报告结果

仅在自审检查通过后，向用户汇报：

1. 已放置的家具清单（名称、位置概要、朝向）
2. 自审检查结果（逐项列出设计检查清单的通过状态）
3. 如有修正，说明修正了什么
4. 如有被放弃的家具，说明原因
5. 整体布局评价（动线是否通畅、功能是否完整）

**禁止在自审未通过时报告"布置完成"。**

---

## 7. MCP 工具参考

### 7.1 工作流 Skills（替代原 get_workflow_guide）

原 `get_workflow_guide` MCP 工具已迁移为三个独立的 Skills，通过 Plugin 机制按需加载：

| Skill | 触发关键词 | 内容 |
|-------|-----------|------|
| `query-workflow` | 统计、查看、列出、有多少 | 只读查询流程 |
| `edit-workflow` | 移动、删除、旋转、调整 | 单一修改流程 |
| `generate-workflow` | 布置、设计、创建、生成、规划 | 完整布置流程（含阶段 A/B、验证修正循环） |

**加载方式**：Claude 根据用户请求自动匹配 Skill 的 `description` 字段触发，Skill 内容作为系统指令注入上下文。

**Skill 文件位置**：`~/.bimcanvas/skills/{skill-name}/SKILL.md`

---

### 7.2 validate_layout

**调用名**：`mcp__canvas__validate_layout`

**功能**：验证当前方案的布局合法性（布局编译器）。检查三类错误：越界、与建筑元素重叠、模块间重叠。

**参数**：无（自动验证当前项目）。

**返回**：格式化的验证报告文本。

**Server 端实现**：

1. 读取 baseline 数据（walls, columns）
2. 读取 computed 数据（designZones, exclusionZones）
3. 读取所有模块（支持 `schemes/{zoneId}/modules.json` 分区格式）
4. 调用 Core 层 `SchemeValidator.Validate()`
5. 自动持久化模块的自动生成 ID

**错误代码表**：

| 代码 | 严重度 | 含义 | 修正指引 |
|------|--------|------|----------|
| E001 | error | 模块超出设计区域 | 向区域内方向移动 |
| E002 | error | 与墙体重叠 | 反方向移动穿透深度 |
| E003 | error | 与柱子重叠 | 反方向移动穿透深度 |
| E004 | error | 与禁区重叠 | 反方向移动穿透深度 |
| E005 | error | 模块间互相重叠 | 沿穿透方向反向移动 |

**返回格式示例**：

```
=== 布局验证通过 ===
共 8 个模块，0 个错误 (32ms)
```

验证失败时返回错误明细（错误代码 + 模块名 + 修正建议）。

**调用时机**：每次 Write modules.json 后必须调用。

---

### 7.3 request_background_screenshot

**调用名**：`mcp__canvas__request_background_screenshot`

**功能**：后台截图，返回 base64 图片 + 保存到 `screenshots/` 目录。

**参数**：

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| projectPath | string | 是 | 项目根目录 |
| viewport | object | 否 | 视口配置 |
| shots | array | 否 | 批量截图列表 |

**viewport 模式**：

| 模式 | 附加参数 | 说明 |
|------|----------|------|
| full | 无 | 全局视图 |
| room | roomId | 指定房间 |
| zone | zoneId | 指定分区 |
| bounds | minX/minY/maxX/maxY | 自定义区域 |

**调用示例**：

```
mcp__canvas__request_background_screenshot({
  "projectPath": "C:\\Projects\\demo_1",
  "viewport": {"mode": "full"}
})
```

**调用时机**：
- Generate 流程前置准备（必须，单独调用）
- 每个阶段编译通过后的设计审查（必须，单独调用）
- Query/Edit 流程中可选使用

---

## 8. SubAgent 设计预留

> **当前状态**：SubAgent 机制已完成架构设计，但**未启用**。`init_manifest.json` 中 `layout-agent.md` 被设为 `enabled: false`，`create_subagents()` 返回空字典。所有任务（query / edit / generate）由 MainAgent 通过 `self.chat()` 直接执行。

### 8.1 当前实际运行方式

MainAgent 收到用户消息后，**不经过 SubAgent 派发**，直接通过 `self.chat()` 完成全部工作：

```
用户消息 → MainAgent.chat() → 意图分析 → 调用 MCP 工具 → 返回结果
```

- 所有工作流（§4 Query、§5 Edit、§6 Generate）均由 MainAgent 自身执行
- 工具调用权限由 MainAgent 的 `permissions` 配置统一控制
- 不会产生 `subagent_start` / `subagent_complete` SSE 事件

### 8.2 预留设计：layout-agent

以下为架构设计中规划的 SubAgent 配置，**启用后**将用于家具布置任务的专项处理：

| 配置项 | 值 |
|--------|-----|
| 名称 | layout-agent |
| 描述 | 家具布置专家 |
| 工具权限 | Read, Write, Glob |
| 模型 | inherit（继承主控 Agent 模型） |
| 配置来源 | `~/.bimcanvas/agents/layout-agent.md` |
| 启用状态 | `enabled: false`（init_manifest.json） |

**设计职责**（启用后）：

1. 读取房间分区数据，理解空间特点
2. 分析门窗位置，规划动线
3. 根据布置规则为房间布置家具
4. 输出符合规范的布置结果

**设计的派发流程**（启用后）：

1. MainAgent 分析意图后，SDK 自动选择匹配的 SubAgent
2. SubAgent 在独立上下文中执行（使用自己的工具权限和提示词）
3. 执行过程通过 SSE 事件实时推送（`subagent_start` → `tool_call_*` → `subagent_complete`）
4. SubAgent 完成后，结果返回给 MainAgent 整合

### 8.3 启用条件

SubAgent 启用需满足：

1. 将 `init_manifest.json` 中 `layout-agent.md` 的 `enabled` 改为 `true`
2. 确保 `~/.bimcanvas/agents/layout-agent.md` 文件存在（首次运行时从 templates/ 初始化）
3. MainAgent 的 `create_subagents()` 将返回非空字典，SDK 自动接管派发

---

## 9. 数据路径规范

### 9.1 .bcp 项目三层结构

```
project/
├── baseline/               【只读】建筑基础数据（Revit 导出）
│   ├── walls.json
│   ├── columns.json
│   ├── openings.json
│   └── rooms.json
├── computed/               【只读】计算派生数据（Server 自动生成）
│   ├── room_zones.json
│   └── exclusions.json
├── schemes/                【可读写】方案设计数据
│   ├── zones.json
│   └── {zoneId}/
│       └── modules.json    ← Agent 写入的布置结果
└── modules/
    ├── README.md           ← 模块库架构说明
    └── module_library.json ← 家具素材库
```

### 9.2 modules.json 数据格式

```json
[
  {
    "id": "auto_generated_by_server",
    "moduleId": "mod_bed_001",
    "bounds": [[x1,y1], [x2,y2], [x3,y3], [x4,y4]],
    "facing": "north",
    "items": []
  }
]
```

| 字段 | 说明 |
|------|------|
| id | Server 在 validate_layout 时自动生成，**禁止手动填写** |
| moduleId | 家具模块 ID，必须在 module_library.json 中存在 |
| bounds | OBB 四角坐标 [[x1,y1], [x2,y2], [x3,y3], [x4,y4]] |
| facing | 语义朝向（"north"/"south"/"east"/"west" 等）或 Vec2D |
| items | 子物件列表（通常为空数组） |

### 9.3 Facing 朝向对照

| 朝向 | 角度 | 朝向 | 角度 |
|------|------|------|------|
| north | 0° | south | 180° |
| east | 90° | west | 270° |
| northeast | 45° | southwest | 225° |
| southeast | 135° | northwest | 315° |

---

## 附录：常见错误

| 错误 | 正确做法 |
|------|----------|
| 写入 `schemes/modules.json` | 写入 `schemes/{zoneId}/modules.json` |
| 凭空编造家具尺寸 | 从 `module_library.json` 选择 |
| 一次性放置全部家具再验证 | 分阶段 A/B 放置，每阶段编译+截图审查 |
| 跳过 `validate_layout` | 每次 Write 后必须调用 |
| 跳过截图设计审查 | 编译通过后仍须对照设计检查清单审查 |
| 跳过 `placement_guide.md` | 必须读取并遵守规范 |
| 跳过 `modules/README.md` | 必须读取以理解 agent_config 使用方式 |
| 阶段 B 只写新增家具 | 必须合并已有+新增全部写入 |
| 修正循环超过 1 次仍失败 | 移除违规家具，保留核心布局 |
| 自审未通过就报告完成 | 禁止，必须通过后才报告 |
