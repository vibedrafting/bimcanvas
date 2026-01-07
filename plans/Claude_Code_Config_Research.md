# Claude Code CLI 配置机制深度研究报告

> **研究目的**：深入理解 Claude Code CLI 的文件驱动配置架构，为 BIMCanvas.Agent 配置系统改造提供参考
> **研究日期**：2026-01-07
> **数据来源**：用户本地 Claude Code 配置 (`C:\Users\huhaonan\.claude\`)

---

## 一、Claude Code 配置目录结构

### 1.1 完整目录树

```
C:\Users\huhaonan\.claude/
├── 核心配置文件
│   ├── config.json                    # API 密钥配置
│   ├── settings.json                  # 全局设置（权限、模型、钩子）
│   ├── settings.local.json            # 项目级权限覆盖
│   ├── CLAUDE.md                      # 全局系统提示词（186行）
│   └── REVIT_KNOWLEDGE_BASE.md        # 领域知识库
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
│   ├── history.jsonl                  # 对话历史（2572行）
│   ├── projects/                      # 项目元数据
│   ├── todos/                         # 待办清单
│   ├── plans/                         # 计划文件
│   ├── debug/                         # 调试日志
│   └── file-history/                  # 文件变化追踪
│
├── 扩展系统
│   ├── plugins/                       # 插件管理
│   ├── scripts/                       # 自定义脚本
│   │   ├── mission_accomplished.py    # 完成通知
│   │   └── need_confirmation.py       # 确认通知
│   └── agents/                        # Agent 配置（空）
│
└── 系统文件
    ├── config/notification_states.json
    ├── stats-cache.json
    └── .update.lock
```

### 1.2 配置文件职责划分

| 文件 | 职责 | 格式 | 优先级 |
|------|------|------|--------|
| config.json | API 密钥 | JSON | 全局 |
| settings.json | 权限、模型、钩子 | JSON | 全局 |
| settings.local.json | 项目级权限覆盖 | JSON | 项目 |
| CLAUDE.md | 系统提示词 | Markdown | 全局/项目 |
| commands/*.md | 命令定义 | Markdown | 全局 |
| output-styles/*.md | 输出样式 | Markdown | 可选 |

---

## 二、核心配置文件详解

### 2.1 config.json - API 配置

```json
{
  "primaryApiKey": "crs"
}
```

**特点**：极简设计，仅存储 API 密钥引用

### 2.2 settings.json - 全局设置

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
    "command": "npx -y ccstatusline@latest"
  },
  "enabledPlugins": {
    "ralph-wiggum@claude-plugins-official": false
  }
}
```

**关键字段**：
- `$schema`：JSON Schema 验证
- `permissions.allow`：工具白名单（7个内置工具）
- `permissions.deny`：工具黑名单
- `model`：使用的模型（opus = Claude Opus 4.5）
- `hooks`：事件钩子（Stop、Notification）
- `statusLine`：状态行显示
- `enabledPlugins`：插件启用状态

### 2.3 settings.local.json - 项目级覆盖

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

## 三、提示词注入机制

### 3.1 CLAUDE.md 格式规范

**全局 CLAUDE.md（186行）结构**：
```markdown
# 开发指导原则
## 语言规范
## 核心目标
## 协作方式

# Git 存档系统
## 存档操作定义
  - 快速存档（quick_archive）
  - 手动存档（manual_archive）
  - 隔离存档（isolation_archive）
  - 回档（rollback）

# 构建编译
# 说明文档管理
# 特别注意
```

**项目级 CLAUDE.md（273行）结构**：
```markdown
# BIMCanvas 项目指令
## 快速导航
## 核心约束
## PlacementAgent 架构速查
## v3.0 数据模型速查
## 开发规范
## 常用命令
```

**关键发现**：
- **无 YAML Front Matter**：纯 Markdown 格式
- **编码差异**：全局 UTF-8 无 BOM，项目级 UTF-8 带 BOM
- **内容差异**：全局定义通用规范，项目级定义特定知识

### 3.2 提示词加载优先级

```
1. 项目级 CLAUDE.md            [最高优先级]
   ↓
2. 全局 CLAUDE.md              [默认行为]
   ↓
3. 命令文件 commands/*.md      [按需注入]
   ↓
4. 知识库 *_KNOWLEDGE_BASE.md  [上下文增强]
```

### 3.3 提示词注入时序

```
用户启动 Claude Code
    ↓
检测项目目录 → 加载项目级 CLAUDE.md
    ↓
合并全局 CLAUDE.md（如果项目级不存在则使用全局）
    ↓
加载权限配置（settings.json + settings.local.json）
    ↓
初始化命令系统（扫描 commands/ 目录）
    ↓
恢复会话内存（history.jsonl + projects/*/session-memory/）
    ↓
系统提示词组装完成
```

---

## 四、命令系统（Skills）

### 4.1 命令文件格式

**路径**：`~/.claude/commands/{category}/{command}.md`

**示例**：`commands/git/commit.md`
```markdown
根据当前工作区状态和开发上下文，生成合适的中文git commit信息并提交，提交主要目的是 $ARGUMENTS 。
```

**变量替换**：
- `$ARGUMENTS`：用户输入的参数
- 执行时动态替换，无需转义

### 4.2 命令调用方式

```
/commit                    → git:commit 技能
/doc:read                  → doc:read 技能
Skill(skill: "git:commit", args: "修复bug")
```

### 4.3 已定义命令清单

| 命令 | 文件 | 功能 |
|------|------|------|
| git:commit | commit.md | 生成中文提交信息 |
| git:worktree | worktree.md | 创建并行开发环境 |
| git:merge-branch | merge-branch.md | 安全分支合并 |
| git:merge-worktree | merge-worktree.md | 工作树清理 |
| doc:read | read.md | 知识查阅 |
| doc:save | save.md | 知识积累判断 |
| doc:update | update.md | 文档同步更新 |

---

## 五、权限系统

### 5.1 三层权限模型

```
allow  [白名单] → 明确允许的工具
deny   [黑名单] → 明确禁止的工具（优先于 allow）
ask    [询问]   → 需要用户确认
```

### 5.2 权限验证流程

```
AI 请求调用工具
    ↓
获取工具完整名（Tool.Name）
    ↓
黑名单检查（deny） → 在列表中 → REJECT
    ↓ 不在
白名单检查（allow） → 在列表中 → PERMIT
    ↓ 不在
询问列表检查（ask） → 在列表中 → PROMPT_USER
    ↓ 不在
系统默认 → REJECT
```

### 5.3 MCP 工具命名规范

```
格式：mcp__<server-name>__<tool-name>

示例：
mcp__revit-mcp__get_revit_status
├── mcp__            (命名空间前缀)
├── revit-mcp        (服务器名，kebab-case)
└── get_revit_status (工具名，snake_case)
```

### 5.4 权限合并规则

```
全局 settings.json
    ↓ 合并
项目 settings.local.json（项目级扩展或限制）
    ↓
最终权限集合
```

**优先级**：项目级 deny > 项目级 allow > 全局 deny > 全局 allow

---

## 六、Hooks 事件系统

### 6.1 支持的事件类型

| 事件 | 触发时机 | 用途 |
|------|----------|------|
| Stop | AI 推理停止 | 任务完成通知 |
| Notification | 需要用户确认 | 提示用户交互 |

### 6.2 Hook 配置格式

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

**特点**：
- 支持 Windows 环境变量（%USERPROFILE%）
- 可执行任意命令
- 异步执行，不阻塞主流程

### 6.3 Hook 脚本示例

```python
# mission_accomplished.py
from win10toast import ToastNotifier
toaster = ToastNotifier()
toaster.show_toast("Claude Code", "Mission Accomplished!", duration=10, threaded=True)
```

---

## 七、知识库系统

### 7.1 REVIT_KNOWLEDGE_BASE.md 结构

```markdown
# Revit API 知识库

## 共享参数管理
### [Revit 2019共享参数系统] - 文件编码与参数控制
  - 核心问题描述
  - 解决方案
  - 代码示例
  - 适用范围

## NetTopologySuite 兼容性管理
### [NetTopologySuite 2.6.0] - 依赖版本冲突
  - 关键症状
  - 解决方案

## 更新记录
```

### 7.2 知识积累决策流程

```
用户输入 → 判断通用性
  ├─ 全局通用 → REVIT_KNOWLEDGE_BASE.md
  └─ 项目特定 → 项目 README.md
```

---

## 八、配置继承关系图

```
┌─────────────────────────────────────────────────────┐
│                   Claude Code CLI                    │
└────────────────────────┬────────────────────────────┘
                         │
         ┌───────────────┼───────────────┐
         ▼               ▼               ▼
┌─────────────┐  ┌─────────────┐  ┌─────────────┐
│ config.json │  │settings.json│  │ CLAUDE.md   │
│ (API密钥)   │  │ (全局设置)  │  │ (全局提示词) │
└──────┬──────┘  └──────┬──────┘  └──────┬──────┘
       │                │                │
       │         ┌──────┴──────┐         │
       │         ▼             ▼         │
       │  ┌─────────────┐ ┌────────┐     │
       │  │settings.    │ │commands│     │
       │  │local.json   │ │/*.md   │     │
       │  │(项目权限)   │ │(命令)  │     │
       │  └──────┬──────┘ └────┬───┘     │
       │         │             │         │
       │         └──────┬──────┘         │
       │                │                │
       │    ┌───────────┼────────────┐   │
       │    ▼           ▼            ▼   ▼
       │  项目级     项目级      项目级
       │  权限       命令        CLAUDE.md
       └────────────────┬────────────────┘
                        │
                        ▼
              ┌─────────────────┐
              │ 最终系统提示词   │
              │ + 权限配置      │
              └─────────────────┘
```

---

## 九、关键设计模式总结

### 9.1 文件驱动配置的优势

| 特性 | 说明 |
|------|------|
| **即时生效** | 修改配置文件后立即生效，无需重启 |
| **版本控制友好** | 所有配置都是文本文件，可 Git 管理 |
| **分层继承** | 全局 + 项目级，灵活覆盖 |
| **可读性强** | Markdown 格式的提示词，易于编写和维护 |
| **可扩展** | 命令系统、插件系统预留扩展空间 |

### 9.2 Claude Code 的设计选择

| 设计点 | Claude Code 的选择 | 原因 |
|--------|-------------------|------|
| 提示词格式 | 纯 Markdown（无 YAML） | 简单直接 |
| 配置格式 | JSON（带 Schema） | 标准、可验证 |
| 命令定义 | Markdown + $ARGUMENTS | 易于编写 |
| 权限模型 | allow/deny/ask | 灵活细粒度 |
| MCP 工具命名 | 完全限定名 | 避免冲突 |
| 知识库 | 独立 .md 文件 | 可跨项目复用 |

---

## 十、对 BIMCanvas.Agent 改造的启示

### 10.1 可借鉴的设计

1. **配置目录结构**：分离 config/settings/prompts/agents
2. **提示词格式**：纯 Markdown，无需 YAML front matter
3. **权限模型**：allow/deny 白名单黑名单模式
4. **命令系统**：$ARGUMENTS 变量替换
5. **分层继承**：全局 → 项目级覆盖

### 10.2 需要适配的差异

| Claude Code | BIMCanvas.Agent | 适配方案 |
|-------------|-----------------|----------|
| ~/.claude/ | Documents/BIMCanvas/ | 自定义配置目录 |
| 单一主代理 | 主代理 + 子代理 | 增加 agents/ 目录 |
| 内置工具 | Agent SDK 工具 | 映射工具名称 |
| MCP 服务器 | Canvas-MCP | 配置 mcp/servers.json |

### 10.3 建议的目标目录结构

```
C:\Users\{用户名}\Documents\BIMCanvas\
├── config.json                          # API Key、模型
├── settings.json                        # 权限、启用的代理
│
├── prompts/                             # 提示词（纯 Markdown）
│   ├── main.md                          # 主代理提示词
│   └── agents/
│       └── layout-agent.md              # 子代理提示词
│
├── agents/                              # 子代理配置
│   └── layout-agent.json                # 描述、工具、模型
│
├── commands/                            # 自定义命令
│   └── layout/
│       └── auto-place.md                # 自动布置命令
│
├── mcp/
│   └── servers.json                     # MCP 服务器配置
│
└── knowledge/
    └── BIMCANVAS.md                     # 领域知识库
```

---

## 十一、下一步行动

1. **确认配置格式**：是否采用 Claude Code 的纯 Markdown 提示词格式
2. **设计 agents/*.json**：子代理配置 Schema
3. **实现配置加载器**：Python 版本的 ConfigLoader
4. **迁移现有配置**：将 Python 代码中的硬编码配置迁移到文件
