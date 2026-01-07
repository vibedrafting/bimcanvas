# Claude Code CLI 配置机制深度研究报告

> **研究目的**：深入理解 Claude Code CLI 的文件驱动配置架构，为 BIMCanvas.Agent 配置系统改造提供参考
> **研究日期**：2026-01-08（更新）
> **数据来源**：用户本地 Claude Code 配置 (`C:\Users\huhaonan\.claude\`)

---

## 一、Claude Code 配置目录结构

### 1.1 完整目录树

```
C:\Users\huhaonan\.claude/
├── 核心配置文件
│   ├── settings.json                  # 全局设置（权限、模型、钩子）
│   ├── settings.local.json            # 项目级权限覆盖
│   ├── CLAUDE.md                      # 全局系统提示词
│   └── REVIT_KNOWLEDGE_BASE.md        # 领域知识库
│
├── Agent 系统 (agents/)
│   └── code-review-expert.md          # 自定义 Agent（YAML frontmatter + 提示词）
│
├── 命令系统 (commands/)
│   ├── doc/                           # 文档命令
│   │   ├── read.md                    # 知识查阅
│   │   ├── save.md                    # 知识积累
│   │   └── update.md                  # 文档更新
│   └── git/                           # Git 命令
│       ├── commit.md                  # 提交
│       ├── worktree.md                # 工作树
│       ├── merge-branch.md            # 分支合并
│       └── merge-worktree.md          # 工作树合并
│
├── 输出样式 (output-styles/)
│   ├── iterative-debugger.md
│   └── revit2019-master.md            # Revit 专家模式
│
├── 运行时数据
│   ├── history.jsonl                  # 对话历史
│   ├── projects/                      # 项目元数据
│   ├── todos/                         # 待办清单
│   ├── plans/                         # 计划文件
│   ├── debug/                         # 调试日志
│   └── file-history/                  # 文件变化追踪
│
├── 扩展系统
│   ├── plugins/                       # 插件管理
│   └── scripts/                       # 自定义脚本
│       ├── mission_accomplished.py    # 完成通知
│       └── need_confirmation.py       # 确认通知
│
└── 全局运行时配置
    └── ../.claude.json                # MCP 服务器 + 项目状态
```

### 1.2 配置文件职责划分

| 文件 | 职责 | 格式 | 优先级 |
|------|------|------|--------|
| `.claude.json` | MCP 服务器、项目状态 | JSON | 全局 |
| `settings.json` | 权限、模型、钩子 | JSON | 全局 |
| `settings.local.json` | 项目级权限覆盖 | JSON | 项目 |
| `CLAUDE.md` | 系统提示词 | Markdown | 全局/项目 |
| `agents/*.md` | 自定义 Agent | Markdown + YAML | 全局 |
| `commands/*.md` | 命令定义 | Markdown | 全局 |

---

## 二、核心配置文件详解

### 2.1 settings.json - 全局设置

```json
{
  "$schema": "https://json.schemastore.org/claude-code-settings.json",
  "env": {},
  "permissions": {
    "allow": ["Bash", "Read", "WebSearch", "WebFetch", "Glob", "Grep", "LS"],
    "deny": []
  },
  "model": "opus",
  "hooks": {
    "Stop": [{ "hooks": [{ "type": "command", "command": "python ..." }] }],
    "Notification": [{ "hooks": [{ "type": "command", "command": "python ..." }] }]
  },
  "statusLine": {
    "type": "command",
    "command": "npx -y ccstatusline@latest",
    "padding": 0
  },
  "enabledPlugins": {
    "ralph-wiggum@claude-plugins-official": false
  }
}
```

**关键字段**：

| 字段 | 类型 | 说明 |
|------|------|------|
| `$schema` | string | JSON Schema 验证 |
| `permissions.allow` | string[] | 工具白名单 |
| `permissions.deny` | string[] | 工具黑名单 |
| `model` | string | 模型选择（opus/sonnet/haiku） |
| `hooks` | object | 事件钩子（Stop/Notification/SessionEnd 等） |
| `statusLine` | object | 状态行配置 |
| `enabledPlugins` | object | 插件启用状态 |

### 2.2 settings.local.json - 项目级覆盖

```json
{
  "permissions": {
    "allow": [
      "mcp__revit-mcp__get_revit_status",
      "mcp__revit-mcp__ai_element_filter",
      "mcp__revit-mcp__operate_element_visual"
    ],
    "deny": [],
    "ask": []
  }
}
```

**特点**：
- 仅覆盖 permissions 字段
- MCP 工具使用完全限定名：`mcp__<server>__<tool>`
- 支持 `ask` 列表（需用户确认）

---

## 三、MCP 服务器配置（重点研究）

### 3.1 配置位置

MCP 服务器配置位于 `~/.claude.json` 文件的 `mcpServers` 字段：

```json
{
  "mcpServers": {
    "context7": {
      "type": "stdio",
      "command": "cmd",
      "args": ["/c", "npx", "@upstash/context7-mcp"],
      "env": {}
    },
    "revit-mcp": {
      "command": "node",
      "args": ["E:\\工作文档\\开发类\\MyCode\\Revit-MCP\\revit-mcp\\build\\index.js"]
    }
  }
}
```

### 3.2 MCP 服务器配置字段

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `type` | string | 否 | 传输类型：`stdio`（默认）或 `sse` |
| `command` | string | 是 | 启动命令 |
| `args` | string[] | 否 | 命令参数 |
| `env` | object | 否 | 环境变量 |
| `headersHelper` | string | 否 | 动态 header 生成命令（SSE 类型） |

### 3.3 MCP 服务器类型

| 类型 | 说明 | 配置示例 |
|------|------|----------|
| **stdio** | 标准 I/O 通信 | `{ "command": "node", "args": ["server.js"] }` |
| **sse** | Server-Sent Events | `{ "type": "sse", "url": "http://..." }` |
| **http** | Streamable HTTP | 支持 OAuth 认证 |

### 3.4 项目级 MCP 配置

每个项目在 `.claude.json` 的 `projects` 字段中有独立的 MCP 设置：

```json
{
  "projects": {
    "E:\\工作文档\\开发类\\MyCode\\BIMCanvas": {
      "mcpServers": {},
      "enabledMcpjsonServers": [],
      "disabledMcpjsonServers": [],
      "disabledMcpServers": ["revit-mcp", "context7"],
      "mcpContextUris": []
    }
  }
}
```

| 字段 | 说明 |
|------|------|
| `mcpServers` | 项目级 MCP 服务器定义 |
| `enabledMcpjsonServers` | 启用的 .mcp.json 服务器 |
| `disabledMcpjsonServers` | 禁用的 .mcp.json 服务器 |
| `disabledMcpServers` | 禁用的全局服务器（按名称） |
| `mcpContextUris` | MCP 上下文 URI |

### 3.5 .mcp.json 项目配置文件

可在项目根目录创建 `.mcp.json` 文件定义项目级 MCP 服务器：

```json
{
  "mcpServers": {
    "project-mcp": {
      "command": "node",
      "args": ["./mcp-server/index.js"]
    }
  }
}
```

### 3.6 MCP 工具命名规范

```
格式：mcp__<server-name>__<tool-name>

示例：
mcp__revit-mcp__get_revit_status
├── mcp__            (命名空间前缀)
├── revit-mcp        (服务器名，kebab-case)
└── get_revit_status (工具名，snake_case)
```

**通配符支持**：
- `mcp__server__*` - 允许/禁止服务器的所有工具

---

## 四、自定义 Agent 配置（重点研究）

### 4.1 Agent 文件位置

```
~/.claude/agents/{agent-name}.md
```

### 4.2 Agent 文件格式

**实际示例**：`code-review-expert.md`

```markdown
---
name: code-review-expert
description: Use this agent when you need to review recently written or modified code for quality, correctness, and adherence to best practices...
tools: Glob, Grep, Read, WebFetch, TodoWrite, WebSearch, mcp__ide__getDiagnostics, mcp__ide__executeCode
model: opus
---

你是一位资深代码审核专家，拥有 15 年以上的软件开发和代码审查经验...

## 核心职责
...

## 审查范围
### 1. 正确性检查
...

## 输出格式
...
```

### 4.3 Agent YAML Frontmatter 字段

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `name` | string | 是 | Agent 名称（与文件名一致） |
| `description` | string | 是 | Agent 描述，包含触发示例 |
| `tools` | string | 否 | 可用工具，逗号分隔 |
| `model` | string | 否 | 模型选择（opus/sonnet/haiku/inherit） |
| `permissionMode` | string | 否 | 权限模式 |
| `disallowedTools` | string | 否 | 禁用工具列表 |
| `skills` | string | 否 | 自动加载的技能 |

### 4.4 Tools 字段详解

tools 字段支持以下工具类型：

| 类型 | 示例 | 说明 |
|------|------|------|
| 内置工具 | `Read`, `Glob`, `Grep` | Claude Code 内置工具 |
| MCP 工具 | `mcp__ide__getDiagnostics` | 来自 MCP 服务器的工具 |
| 混合使用 | `Read, Glob, mcp__ide__*` | 内置 + MCP 工具 |

### 4.5 Description 字段最佳实践

description 应包含详细的使用场景说明：

```yaml
description: |
  Use this agent when you need to review recently written or modified code...

  Examples:

  <example>
  Context: User has just written a new function and wants it reviewed.
  user: "Please write a function to calculate the area of a polygon"
  assistant: "Let me use the code-review-expert agent to review this code..."
  </example>
```

### 4.6 Agent 调用方式

```
@agent-name              # @-mention 调用
/agents                  # 查看所有 Agent
Skill(skill: "agent")    # 程序化调用
```

---

## 五、提示词系统

### 5.1 CLAUDE.md 格式规范

**全局 CLAUDE.md 结构**：
```markdown
# 开发指导原则
## 语言规范
## 核心目标
## 协作方式

# Git 存档系统
## 存档操作定义

# 构建编译
# 说明文档管理
# 特别注意
```

**项目级 CLAUDE.md 结构**：
```markdown
# 项目名称 项目指令
## 快速导航
## 核心约束
## 架构速查
## 开发规范
## 常用命令
```

**关键发现**：
- **无 YAML Front Matter**：纯 Markdown 格式
- **编码**：UTF-8（无 BOM 或带 BOM 均可）
- **内容差异**：全局定义通用规范，项目级定义特定知识
- **文件导入**：支持 `@path/to/file.md` 语法导入其他文件

### 5.2 提示词加载优先级

```
1. 项目级 CLAUDE.md            [最高优先级]
   ↓
2. 全局 CLAUDE.md              [默认行为]
   ↓
3. 命令文件 commands/*.md      [按需注入]
   ↓
4. 知识库 *_KNOWLEDGE_BASE.md  [上下文增强]
```

---

## 六、命令系统（Skills）

### 6.1 命令文件格式

**路径**：`~/.claude/commands/{category}/{command}.md`

**示例**：`commands/git/commit.md`
```markdown
根据当前工作区状态和开发上下文，生成合适的中文git commit信息并提交，提交主要目的是 $ARGUMENTS 。
```

**变量替换**：
- `$ARGUMENTS`：用户输入的参数

### 6.2 命令调用方式

```
/commit                    → git:commit 技能
/doc:read                  → doc:read 技能
Skill(skill: "git:commit", args: "修复bug")
```

---

## 七、权限系统

### 7.1 三层权限模型

```
allow  [白名单] → 明确允许的工具
deny   [黑名单] → 明确禁止的工具（优先于 allow）
ask    [询问]   → 需要用户确认
```

### 7.2 权限合并规则

```
全局 settings.json
    ↓ 合并
项目 settings.local.json（项目级扩展或限制）
    ↓
最终权限集合
```

**优先级**：项目级 deny > 项目级 allow > 全局 deny > 全局 allow

---

## 八、Hooks 事件系统

### 8.1 支持的事件类型

| 事件 | 触发时机 | 用途 |
|------|----------|------|
| Stop | AI 推理停止 | 任务完成通知 |
| Notification | 需要用户确认 | 提示用户交互 |
| SessionStart | 会话开始 | 初始化操作 |
| SessionEnd | 会话结束 | 清理操作 |
| PreToolUse | 工具调用前 | 修改工具输入 |
| PostToolUse | 工具调用后 | 处理工具输出 |
| PermissionRequest | 权限请求 | 自动批准/拒绝 |
| SubagentStart | 子代理启动 | 监控子任务 |
| SubagentStop | 子代理停止 | 处理子任务结果 |
| UserPromptSubmit | 用户提交提示 | 预处理用户输入 |
| PreCompact | 压缩前 | 自定义压缩逻辑 |

### 8.2 Hook 配置格式

```json
{
  "hooks": {
    "Stop": [{
      "hooks": [{
        "type": "command",
        "command": "python %USERPROFILE%/.claude/scripts/mission_accomplished.py"
      }]
    }]
  }
}
```

---

## 九、配置继承关系图

```
┌─────────────────────────────────────────────────────────────────┐
│                      Claude Code CLI                             │
└─────────────────────────────┬───────────────────────────────────┘
                              │
        ┌─────────────────────┼─────────────────────┐
        ▼                     ▼                     ▼
┌───────────────┐     ┌───────────────┐     ┌───────────────┐
│ .claude.json  │     │ settings.json │     │  CLAUDE.md    │
│ (MCP服务器)   │     │ (全局设置)    │     │ (全局提示词)  │
└───────┬───────┘     └───────┬───────┘     └───────┬───────┘
        │                     │                     │
        │              ┌──────┴──────┐              │
        │              ▼             ▼              │
        │       ┌───────────┐ ┌──────────┐         │
        │       │settings.  │ │agents/   │         │
        │       │local.json │ │*.md      │         │
        │       │(项目权限) │ │(自定义   │         │
        │       │           │ │ Agent)   │         │
        │       └─────┬─────┘ └────┬─────┘         │
        │             │            │               │
        │             └──────┬─────┘               │
        │                    │                     │
        │        ┌───────────┼───────────┐         │
        │        ▼           ▼           ▼         ▼
        │      项目级      项目级     项目级
        │      权限        Agent      CLAUDE.md
        └────────────────────┬─────────────────────┘
                             │
                             ▼
                   ┌─────────────────┐
                   │ 最终系统提示词  │
                   │ + 权限配置      │
                   │ + MCP 工具      │
                   └─────────────────┘
```

---

## 十、关键设计模式总结

### 10.1 文件驱动配置的优势

| 特性 | 说明 |
|------|------|
| **即时生效** | 修改配置文件后立即生效，无需重启 |
| **版本控制友好** | 所有配置都是文本文件，可 Git 管理 |
| **分层继承** | 全局 + 项目级，灵活覆盖 |
| **可读性强** | Markdown 格式的提示词，易于编写和维护 |
| **可扩展** | Agent 系统、插件系统预留扩展空间 |

### 10.2 Claude Code 的设计选择

| 设计点 | Claude Code 的选择 | 原因 |
|--------|-------------------|------|
| 提示词格式 | 纯 Markdown（无 YAML） | 简单直接 |
| 配置格式 | JSON（带 Schema） | 标准、可验证 |
| Agent 定义 | Markdown + YAML frontmatter | 提示词和配置合一 |
| 命令定义 | Markdown + $ARGUMENTS | 易于编写 |
| 权限模型 | allow/deny/ask | 灵活细粒度 |
| MCP 工具命名 | 完全限定名 | 避免冲突 |
| 知识库 | 独立 .md 文件 | 可跨项目复用 |

---

## 十一、对 BIMCanvas.Agent 改造的启示

### 11.1 可借鉴的设计

1. **Agent 配置格式**：YAML frontmatter + Markdown 提示词
2. **配置目录结构**：分离 config/agents/prompts
3. **提示词格式**：纯 Markdown，无需 YAML front matter
4. **权限模型**：allow/deny 白名单黑名单模式
5. **分层继承**：全局 → 项目级覆盖
6. **MCP 配置**：独立的 mcpServers 字段

### 11.2 BIMCanvas.Agent 目标配置结构

```
C:\Users\{用户名}\Documents\BIMCanvas\
├── BIMCANVAS.md              ← 系统提示词（类似 CLAUDE.md）
├── config.json               ← API/模型/工具/服务器配置
│
└── agents/                   ← 子 Agent 目录
    └── layout-agent.md       ← 子 Agent 配置（YAML frontmatter + 提示词）
```

### 11.3 关键差异与适配

| Claude Code | BIMCanvas.Agent | 适配方案 |
|-------------|-----------------|----------|
| `~/.claude/` | `Documents/BIMCanvas/` | 自定义配置目录 |
| 单一主代理 | 主代理 + 子代理 | 增加 agents/ 目录 |
| 内置工具 | Agent SDK 工具 | 映射工具名称 |
| MCP 服务器（可选） | 暂不使用 MCP | 预留 mcpServers 字段 |
| 多个配置文件 | 简化为 3 个文件 | config.json + BIMCANVAS.md + agents/*.md |

---

## 十二、附录：配置文件完整示例

### A. settings.json 完整结构

```json
{
  "$schema": "https://json.schemastore.org/claude-code-settings.json",
  "env": {},
  "permissions": {
    "allow": ["Bash", "Read", "WebSearch", "WebFetch", "Glob", "Grep", "LS"],
    "deny": []
  },
  "model": "opus",
  "hooks": {
    "Stop": [{ "hooks": [{ "type": "command", "command": "..." }] }],
    "Notification": [{ "hooks": [{ "type": "command", "command": "..." }] }]
  },
  "statusLine": {
    "type": "command",
    "command": "npx -y ccstatusline@latest",
    "padding": 0
  },
  "enabledPlugins": {}
}
```

### B. Agent 文件完整结构

```markdown
---
name: agent-name
description: |
  Agent 描述，包含触发场景说明和示例...
tools: Tool1, Tool2, mcp__server__tool
model: opus
---

Agent 系统提示词内容...

## 核心职责
...

## 工作流程
...

## 输出格式
...
```

### C. MCP 服务器配置完整结构

```json
{
  "mcpServers": {
    "stdio-server": {
      "type": "stdio",
      "command": "node",
      "args": ["server.js"],
      "env": { "DEBUG": "true" }
    },
    "sse-server": {
      "type": "sse",
      "url": "http://localhost:3000/sse",
      "headersHelper": "node get-headers.js"
    }
  }
}
```
