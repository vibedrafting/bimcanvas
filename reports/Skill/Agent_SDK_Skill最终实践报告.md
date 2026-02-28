# Agent SDK Skill 最终实践报告

**报告日期**: 2026-02-28
**SDK 版本**: claude-agent-sdk 0.1.41 / Claude Code CLI 2.1.52
**项目**: BIMCanvas.Agent
**报告状态**: 已验证通过

---

## 执行摘要

经过 3 轮方案迭代和 4 份研究报告的积累，我们找到了在 Agent SDK 中加载 Skill 且完全避免配置污染的最终方案：**Plugin 旁路策略**。

核心配置组合：

```python
ClaudeAgentOptions(
    setting_sources=None,                                    # 不加载任何文件系统配置
    plugins=[{"type": "local", "path": ".bimcanvas-plugin"}] # 通过 Plugin 独立加载 Skills
)
```

**验证结果**（HTTP 抓包确认）：
- Skill 正常加载并可通过 `Skill` 工具调用
- 请求中完全不包含 `~/.claude/CLAUDE.md` 内容
- System Prompt 仅包含开发者自定义内容

---

## 一、问题背景

### 1.1 为什么需要 Skill

BIMCanvas.Agent 作为一个独立的 Python 后端进程，通过 Agent SDK 调用 Claude CLI 执行家具布置任务。在这个架构中，Skill 的价值在于：

| 需求 | Skill 的作用 |
|------|-------------|
| **领域知识注入** | 将设计规范、布置流程等知识封装为 SKILL.md，AI 按需加载 |
| **语义触发** | 用户说"布置卧室"时，AI 自动关联对应的设计规范 Skill |
| **标准化分发** | Skill 是纯文件格式，可随项目模板一起分发给最终用户 |

### 1.2 核心矛盾

Agent SDK 加载 Skill 的官方方式是设置 `setting_sources=["project"]`。但这个参数是一个**粗粒度开关**——开启后，SDK 不仅扫描 Skills，还会加载 `CLAUDE.md`、`settings.json` 等所有文件系统配置。

对于 BIMCanvas 项目，开发者的 `~/.claude/CLAUDE.md` 包含 Git 自动存档规则、MSBuild 路径等**与 Agent 业务完全无关**的全局配置。这些内容一旦注入，会导致 Agent 在修改 JSON 文件时自动触发 Git commit、加载错误的 API 路径，甚至工具集被完全替换。

---

## 二、踩坑记录：3 轮方案迭代

### 2.1 第一轮：全量加载（严重污染）

**配置**：
```python
setting_sources=["user", "project"]
```

**问题**：
- `~/.claude/CLAUDE.md`（1172 字符）完整注入到 System Prompt
- Git 自动存档规则激活 → Agent 修改 JSON 文件后自动执行 `git commit`
- API 请求分流到两个不同服务器（Agent 服务器 vs Claude Code 服务器）
- Canvas MCP 工具集全部缺失，被 Claude Code 工具集替换

**HTTP 抓包证据**：
```json
{
  "content": [{
    "text": "Contents of C:\\Users\\huhaonan\\.claude\\CLAUDE.md (user's private global instructions):\n\n# Git存档系统\n### 自动触发（AI主动执行quick_archive）..."
  }]
}
```

**教训**：`"user"` source 会加载用户全局配置，**绝对不能在 Agent SDK 中使用**。

### 2.2 第二轮：仅项目级（仍然污染）

**配置**：
```python
setting_sources=["project"]
```

**预期**：仅加载项目目录下的 `.claude/skills/`，不触及用户全局配置。

**实际结果**：Skill 成功加载，但 **CLI 仍然加载了 `~/.claude/CLAUDE.md`**。即使 `cwd` 指向一个干净的项目目录（不包含 CLAUDE.md），`"project"` source 依然触发了全局 CLAUDE.md 的扫描。

**HTTP 抓包证据**：请求中仍然出现 `~/.claude/CLAUDE.md` 的完整内容。

**教训**：`setting_sources` 的实际行为与文档描述不符。无论配置为 `"user"` 还是 `"project"`，CLI 都会加载 `~/.claude/CLAUDE.md`。**此路不通**。

### 2.3 第三轮：完全禁用（安全但无 Skill）

**配置**：
```python
setting_sources=None
```

**结果**：
- 零配置污染 ✅
- Canvas MCP 工具正常 ✅
- API 请求走正确路径 ✅
- **Skill 功能完全不可用** ❌

**教训**：`setting_sources=None` 是最安全的配置，但也意味着放弃了 CLI 的 Skill 发现机制。需要找到一种**独立于 setting_sources 的 Skill 加载方式**。

---

## 三、最终方案：Plugin 旁路策略

### 3.1 方案依据

Agent SDK 官方文档（Plugins in the SDK）明确指出：
- Plugin 通过 `--plugin-dir` 加载，**独立于** `--setting-sources`
- Plugin 可以包含 `skills/` 目录，其中的 Skills 会被 CLI 发现
- `setting_sources=None` + `plugins=[...]` 可以实现**只加载 Skills、不加载任何配置**

### 3.2 Plugin 目录结构

`~/.bimcanvas/` 目录本身就是 Plugin 目录，同时扮演 Agent 配置目录和 Plugin 目录：

```
~/.bimcanvas/                      ← 整个目录既是 Agent 配置又是 Plugin
├── .claude-plugin/
│   └── plugin.json               ← Plugin 清单文件（必需）
├── skills/                        ← Skills 集中管理
│   └── test-echo/
│       └── SKILL.md
├── config.json                    ← Agent 配置（已有）
├── BIMCANVAS.md                   ← System Prompt（已有）
└── agents/                        ← SubAgent 定义（已有）
```

**plugin.json 最小格式**：
```json
{
  "name": "bimcanvas",
  "description": "BIMCanvas Skills Plugin - 家具布置 Agent 技能集",
  "version": "1.0.0"
}
```

### 3.3 代码实现

```python
from claude_agent_sdk import ClaudeAgentOptions

def _create_options(self) -> ClaudeAgentOptions:
    # === Plugin 机制加载 Skills ===
    # ~/.bimcanvas/ 本身就是 Plugin 目录
    plugins = []
    plugin_path = self._config_loader.config_dir  # ~/.bimcanvas/
    if (plugin_path / ".claude-plugin").exists():
        plugins.append({"type": "local", "path": str(plugin_path)})

    return ClaudeAgentOptions(
        system_prompt=system_prompt,
        cwd=self.working_directory,
        allowed_tools=[..., "Skill"],           # 必须包含 "Skill" 工具
        setting_sources=None,                   # 不加载任何文件系统配置
        plugins=plugins,                        # 通过 Plugin 机制加载 Skills
        mcp_servers={"canvas": canvas_mcp},     # 业务工具
    )
```

### 3.4 HTTP 抓包验证

**测试日期**：2026-02-28
**SDK 版本**：agent-sdk/0.1.41, CLI/2.1.52
**测试消息**："你有哪些skill能用"

**验证结果 1 — Skill 正常加载** ✅

在请求的 messages 中，Skill 通过 `system-reminder` 注入：

```json
{
  "text": "<system-reminder>\nThe following skills are available for use with the Skill tool:\n\n- .bimcanvas-plugin:test-echo: 测试 Skill 加载机制。当用户说\"测试skill\"或\"skill测试\"时触发。\n</system-reminder>"
}
```

注意 Skill 名称前缀为 `.bimcanvas-plugin:`，表明 CLI 通过 Plugin 机制发现了该 Skill。

**验证结果 2 — 零配置污染** ✅

请求的 system 数组仅包含 3 项：

```json
{
  "system": [
    {"text": "x-anthropic-billing-header: ..."},
    {"text": "You are a Claude agent, built on Anthropic's Claude Agent SDK."},
    {"text": "# 主控 Agent：BIMCanvas 室内布置助手\n...（开发者自定义 system_prompt）"}
  ]
}
```

没有任何 `~/.claude/CLAUDE.md` 内容。没有 Git 存档规则。没有 MSBuild 路径。

**验证结果 3 — 工具集正确** ✅

工具列表包含 Canvas MCP 工具 + Skill 工具，不包含 `mcp__context7__*` 等 Claude Code 污染工具。

---

## 四、最佳实践

### 4.1 配置原则

| 原则 | 说明 |
|------|------|
| **setting_sources 永远为 None** | Agent SDK 中绝不依赖文件系统配置加载，这是配置安全的基础 |
| **Skill 通过 Plugin 加载** | 使用 `plugins=[{"type": "local", "path": "..."}]` 独立加载 |
| **System Prompt 完全代码控制** | 所有注入 AI 的指令必须通过代码显式设置，不依赖隐式文件扫描 |
| **环境变量显式传递** | 通过 `env={}` 参数传递 API Key 和 Base URL，不依赖 settings.json |

### 4.2 Plugin 模板分发

Plugin 文件由 Agent 的模板系统管理，初始化时自动复制到 `~/.bimcanvas/`：

```
BIMCanvas.Agent/templates/
├── .claude-plugin/plugin.json     ← 源模板
├── skills/test-echo/SKILL.md      ← 源模板
├── config.json                    ← 已有
└── BIMCANVAS.md                   ← 已有

    ↓ Agent 初始化时复制到 ↓

~/.bimcanvas/                      ← 部署位置（全局唯一）
├── .claude-plugin/plugin.json
├── skills/test-echo/SKILL.md
├── config.json
└── BIMCANVAS.md
```

所有项目共享同一个 `~/.bimcanvas/skills/` 目录，统一管理 Skills。

### 4.3 SKILL.md 编写规范

```markdown
---
name: skill-identifier
description: |
  详细描述触发条件（AI 根据此字段判断何时调用）：
  - 关键词：列出触发词（中英文均可）
  - 场景：说明适用场景
---

# Skill 标题

## 工作流程
1. 步骤一
2. 步骤二

## 关键约束
- 约束一
- 约束二
```

**编写技巧**：
- `description` 字段是 AI 判断何时触发的唯一依据，必须语义丰富
- Skill 内容在被调用时才加载（渐进式披露），可以包含较详细的指令
- 避免在 Skill 中重复 System Prompt 已有的内容

### 4.4 不要使用的配置

```python
# ❌ 绝对不要
setting_sources=["user", "project"]  # 严重污染：加载全局 CLAUDE.md + settings.json

# ❌ 不要
setting_sources=["project"]          # 仍然污染：实测证明无法阻止全局 CLAUDE.md 加载

# ❌ 不要
setting_sources=["user"]             # 最严重：直接加载所有用户级配置
```

---

## 五、3 种方案对比总结

| 方案 | 原理 | Skill 可用 | 污染风险 | 推荐度 |
|------|------|-----------|----------|--------|
| `setting_sources=["project"]` | CLI 扫描项目目录 | ✅ | 🔴 实测仍加载 CLAUDE.md | ❌ 不推荐 |
| `setting_sources=None` | 完全禁用文件扫描 | ❌ | ✅ 零污染 | ⚠️ 安全但无 Skill |
| **Plugin 旁路**（最终方案）| `None` + `plugins=[...]` | ✅ | ✅ 零污染 | ✅ **推荐** |
| 手动拼接到 system_prompt | 代码读取 SKILL.md 拼接 | ⚠️ 无 Skill 工具 | ✅ 零污染 | ⚠️ 备选（降级方案） |
| In-Process MCP 工具替代 | 代码定义 @tool 函数 | ❌ 非 Skill | ✅ 零污染 | ⚠️ 长期演进方向 |

---

## 六、已知限制

### 6.1 CLI 基础工具残留

即使 `setting_sources=None`，CLI 仍会注入以下基础工具：
- `TodoWrite`、`EnterPlanMode`、`ExitPlanMode` — CLI 专用工具
- `TaskCreate`、`TaskUpdate`、`TaskList` 等

这些工具不影响核心功能，可通过 `disallowed_tools` 显式禁用：

```python
disallowed_tools=["TodoWrite", "EnterPlanMode", "ExitPlanMode"]
```

### 6.2 Skill 名称前缀

通过 Plugin 加载的 Skill，名称会带有 Plugin 目录名前缀（取决于 Plugin 目录的名称）。AI 通过 `Skill` 工具调用时需要使用完整名称。

### 6.3 SubAgent 不继承 Plugin Skills

Plugin 机制仅影响顶层 CLI 会话（MainAgent）。SubAgent 作为独立的 `AgentDefinition`，其 prompt 需要通过代码显式注入 Skill 内容，不会自动继承 Plugin 中的 Skills。

---

## 七、后续计划

### Phase 1：编写生产级 Skills（近期）

为 BIMCanvas.Agent 开发实际业务 Skills：

```
~/.bimcanvas/skills/
├── test-echo/SKILL.md              ← 已完成（测试验证用）
├── layout-guide/SKILL.md           ← 待开发（布置设计规范）
├── git-workflow/SKILL.md           ← 待开发（Git 工作流指导）
└── furniture-catalog/SKILL.md      ← 待开发（家具规格知识库）
```

### Phase 2：关注 SDK 演进（中期）

**GitHub Issues 跟踪**：

| Issue | 仓库 | 状态 | 说明 |
|-------|------|------|------|
| #456 | claude-agent-sdk-python | Open | 请求独立的 Skill 路径配置（`skills_path` 参数） |
| #36 | claude-agent-sdk-typescript | Open | Skills 发现机制问题 |
| #268 | claude-agent-sdk-python | Open | Linux 路径兼容性问题 |

如果 SDK 未来支持 `skills_path` 参数，可以迁移到更直接的配置方式。但 Plugin 方案已经足够稳定，不急于迁移。

### Phase 3：In-Process MCP 工具化（长期）

对于高频、性能敏感的 Skill，考虑迁移为 In-Process MCP 工具：

```python
@tool("layout_validator", "验证家具布置方案的合法性", {...})
async def layout_validator(args: dict) -> dict:
    # Python 原生实现，无需文件系统、无 Bash 依赖
    ...
```

这是"黄金标准"方案：零文件依赖、强类型、可调试。但当前阶段 Skill 足够使用。

---

## 八、关键文件索引

| 文件 | 说明 |
|------|------|
| `BIMCanvas.Agent/src/agent/main_agent.py` | Agent 入口，`_create_options()` 方法包含 Plugin 配置 |
| `BIMCanvas.Server/Templates/bimcanvas-plugin/` | Plugin 模板源目录 |
| `BIMCanvas.Server/Templates/init_manifest.json` | 模板分发清单 |
| `reports/Skill/Agent_SDK_Skill配置使用研究报告.md` | 初始研究：Skill 核心概念 |
| `reports/Skill/Agent_SDK_Skill配置隔离问题研究报告_by_Claude.md` | 社区调研：GitHub Issues + 隔离方案 |
| `reports/Skill/Agent_SDK配置污染问题报告.md` | 实证分析：配置污染的完整诊断 |
| `reports/Skill/Claude Agent SDK 配置深度解析_by_Gemini.md` | 架构深度分析：3 套解决方案 |

---

## 九、版本历程

| 日期 | 事件 | 结果 |
|------|------|------|
| 2025-01-25 | Skill 初始研究 | 明确 Skill 是文件系统工件，需要 `setting_sources` |
| 2026-01-26 | 配置污染发现 | `setting_sources=["user","project"]` 导致 100% 严重污染 |
| 2026-01-26 | 紧急回滚 | `setting_sources=None` → 零污染，但 Skill 不可用 |
| 2026-01-26 | 隔离问题研究 | 确认 `setting_sources` 无法细粒度控制，GitHub Issues 佐证 |
| 2026-01-26 | 社区方案调研 | 发现手动加载、符号链接等临时方案 |
| 2026-02-28 | 测试 `["project"]` | 失败：CLI 仍加载 `~/.claude/CLAUDE.md` |
| 2026-02-28 | **Plugin 旁路方案** | **成功**：`None` + `plugins=[...]` 实现零污染 Skill 加载 |
| 2026-02-28 | 清理旧 skill_loader | 删除无效的手动加载机制，仅保留 Plugin 新方案 |
| 2026-02-28 | Plugin 迁移到 ~/.bimcanvas/ | 全局统一管理 Skills，项目目录不再存放 Plugin |

---

**报告编制**: BIMCanvas 开发团队
**文档版本**: v1.0
**最后更新**: 2026-02-28
