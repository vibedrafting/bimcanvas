# Agent SDK Skill 配置隔离问题研究报告

**研究日期**: 2026-01-26  
**研究背景**: 在 Agent SDK 中配置 Skill 时，如何避免加载 Claude Code 相关配置导致项目污染  
**研究目标**: 调研社区是否有类似问题反馈，以及可行的解决方案

---

## 执行摘要

### 核心发现

1. **这是一个已知的 SDK 设计限制**，已有多个开发者在 GitHub 提出相关 Issue
2. **目前 SDK 不支持细粒度配置控制**（如"只加载 Skills，不加载 settings.json"）
3. **官方尚未提供解决方案**，Issue #456 提出了功能请求但仍处于 Open 状态
4. **社区采用的临时方案**：手动加载 Skills 或使用符号链接

### 问题本质

| 配置选项 | Skills 加载 | 其他配置加载 | 风险评估 |
|---------|------------|-------------|---------|
| `setting_sources=None` | ❌ 不加载 | ❌ 不加载 | ✅ 安全隔离 |
| `setting_sources=["project"]` | ✅ 加载 | ⚠️ 可能加载 CLAUDE.md、settings.json | ⚠️ 配置污染 |

---

## 相关 GitHub Issues

### Issue #456: 请求添加独立的 Skill 路径配置

**仓库**: anthropics/claude-agent-sdk-python  
**链接**: https://github.com/anthropics/claude-agent-sdk-python/issues/456  
**状态**: Open  
**创建时间**: 2026-01-05  
**相关性**: ⭐⭐⭐⭐⭐ 高度相关

#### 问题描述

Issue 提出者指出 `.claude/` 目录存在双重用途的问题：

> **Problem**
> 
> The `.claude/` directory serves dual purposes:
> 1. **Claude Code CLI** reads it for settings, permissions, and MCP server configuration
> 2. **Agent SDK** reads it for skills/commands discovery (`.claude/commands/*.md`, `.claude/agents/*.md`)
>
> When building agents that need custom skills while running from directories where Claude Code CLI is also used, this creates confusion. Skills must be discoverable from the working directory's `.claude/` path, often requiring symlinks.
>
> The SDK correctly isolates settings by default (`setting_sources=None` loads no settings), which is good.

#### 提出的解决方案

```python
ClaudeAgentOptions(
    # Explicit paths for agent resources (independent of setting_sources)
    commands_path="/path/to/my/commands",  # or list of paths
    agents_path="/path/to/my/agents",

    # Existing setting_sources continues to control CLI settings
    setting_sources=None,  # Still isolated from CLI settings
)
```

#### 临时解决方案（符号链接）

```python
claude_symlink = session_path / ".claude"
project_claude = project_root / ".claude"
if project_claude.exists() and not claude_symlink.exists():
    claude_symlink.symlink_to(project_claude)
```

---

### Issue #36: Skills 无法被发现（TypeScript SDK）

**仓库**: anthropics/claude-agent-sdk-typescript  
**链接**: https://github.com/anthropics/claude-agent-sdk-typescript/issues/36  
**状态**: Open  
**创建时间**: 2025-10-19  
**相关性**: ⭐⭐⭐⭐ 相关

#### 问题描述

即使正确配置了 `settingSources: ['project']`，Skills 仍然无法被自动发现：

> **Bug Report**
>
> Skills are not being auto-discovered from `.claude/skills/` directory despite correct configuration.
>
> When asked "List all available Skills", agent responds: "The `<available_skills>` section in my tool definitions is empty"
>
> **关键发现**: Standalone CLI works - When using Claude Code CLI 2.0.22 directly (not via SDK), skills ARE discovered correctly

#### 复现配置

```javascript
const config = {
    cwd: '/path/to/project/root',
    settingSources: ['project'],
    systemPrompt: '...',
    maxTurns: 20,
    model: 'claude-sonnet-4-5-20250929',
    permissionMode: 'bypassPermissions',
};
```

---

### Issue #268: Linux 系统上 Skills 不工作

**仓库**: anthropics/claude-agent-sdk-python  
**链接**: https://github.com/anthropics/claude-agent-sdk-python/issues/268  
**状态**: Open  
**创建时间**: 2025-10-18  
**相关性**: ⭐⭐⭐ 部分相关

#### 问题描述

SDK 在 Linux 系统上使用了硬编码的 macOS 路径来查找 Skills：

> Agent Skills are not auto-discovered by the Claude Agent SDK on Linux systems. Investigation reveals the SDK is looking for skills using hardcoded macOS paths (`/Users/...`) instead of using the environment's actual home directory.
>
> When explicitly asked to check for skills, the agent searches incorrect paths:
> - `/Users/chriscohoat/.claude/skills/` (appears to be Anthropic dev's machine)
> - Never checks: `/home/slzatz/.claude/skills/` (correct Linux path)

---

### Issue #186: setting_sources 标志兼容性问题

**仓库**: anthropics/claude-agent-sdk-python  
**链接**: https://github.com/anthropics/claude-agent-sdk-python/issues/186  
**状态**: Open  
**创建时间**: 2025-09-27  
**相关性**: ⭐⭐ 参考价值

#### 问题描述

SDK 与新版本 CLI 的 `--setting-sources` 标志存在兼容性问题：

> The claude-code-sdk in main branch has a compatibility issue with newer versions of the Claude Code CLI. The SDK always adds the `--setting-sources` flag to the CLI command even when the value is `None` or an empty list, but newer CLI versions don't recognize this flag.

---

### Issue #102: 后台 Agent 无法访问 Skill 工具

**仓库**: anthropics/claude-agent-sdk-typescript  
**链接**: https://github.com/anthropics/claude-agent-sdk-typescript/issues/102  
**状态**: Open  
**创建时间**: 2025-12-12  
**相关性**: ⭐⭐ 参考价值

#### 问题描述

异步后台 Agent 无法访问 Skill 工具：

> When using the Claude Agent SDK to spawn agents programmatically, there is an inconsistency in tool availability between synchronous and asynchronous agents:
> - Synchronous agents: Have access to the Skill tool as expected
> - Asynchronous agents (background): Do NOT have access to the Skill tool

---

## 官方文档说明

### Skills 加载机制

**来源**: https://platform.claude.com/docs/en/agent-sdk/skills

官方文档明确指出：

> **Loaded from filesystem**: Skills are loaded from configured filesystem locations. You must specify `settingSources` (TypeScript) or `setting_sources` (Python) to load Skills from the filesystem.
>
> **Default behavior**: By default, the SDK does not load any filesystem settings. To use Skills, you must explicitly configure `settingSources: ['user', 'project']` (TypeScript) or `setting_sources=["user", "project"]` (Python) in your options.
>
> Unlike subagents (which can be defined programmatically), **Skills must be created as filesystem artifacts. The SDK does not provide a programmatic API for registering Skills.**

### setting_sources 行为变更

**来源**: https://platform.claude.com/docs/en/agent-sdk/migration-guide

从 v0.1.0 开始的重大变更：

> **What changed**: The SDK no longer reads from filesystem settings (CLAUDE.md, settings.json, slash commands, etc.) by default.
>
> **Why this changed**: Ensures SDK applications have predictable behavior independent of local filesystem configurations. This is especially important for:
> - CI/CD environments - Consistent behavior without local customizations
> - Deployed applications - No dependency on filesystem settings

```javascript
// BEFORE (v0.0.x) - Loaded all settings automatically
const result = query({ prompt: "Hello" });
// Would read from:
// - ~/.claude/settings.json (user)
// - .claude/settings.json (project)
// - .claude/settings.local.json (local)
// - CLAUDE.md files
// - Custom slash commands

// AFTER (v0.1.0) - No settings loaded by default
const result = query({
    prompt: "Hello",
    options: {
        settingSources: ["user", "project", "local"]
    }
});
```

### Skills 目录结构

**来源**: https://docs.claude.com/en/docs/agent-sdk/skills

Skills 从以下位置加载：

| 类型 | 路径 | 加载条件 |
|-----|------|---------|
| Project Skills | `.claude/skills/` | `setting_sources` 包含 `"project"` |
| User Skills | `~/.claude/skills/` | `setting_sources` 包含 `"user"` |
| Plugin Skills | 随插件安装 | 自动加载 |

---

## 技术深度分析

### Skills 工作原理

**来源**: https://leehanchung.github.io/blogs/2025/10/26/claude-skills-deep-dive/

根据技术分析文章：

> **Key Insight**: Skill = Prompt Template + Conversation Context Injection + Execution Context Modification + Optional data files and Python Scripts
>
> Skills operate fundamentally differently from normal tools. Instead of executing discrete actions and returning results, skills inject comprehensive instruction sets that modify how Claude reasons about and approaches the task.
>
> When Claude invokes a skill, the system follows a simple workflow: it loads a markdown file (SKILL.md), expands it into detailed instructions, injects those instructions as new user messages into the conversation context, modifies the execution context (allowed tools, model selection), and continues the conversation with this enriched environment.

### setting_sources 加载内容

根据文档和测试，`setting_sources=["project"]` 可能加载以下内容：

```
.claude/
├── settings.json          ← ⚠️ 可能污染环境变量配置
├── settings.local.json    ← ⚠️ 可能污染本地配置
├── CLAUDE.md              ← ⚠️ 可能加载项目级指令
├── commands/              ← 自定义斜杠命令
├── agents/                ← 自定义 subagent 定义
└── skills/                ← ✅ 我们需要的 Skills
    ├── skill-1/
    │   └── SKILL.md
    └── skill-2/
        └── SKILL.md
```

**核心问题**：无法单独加载 `skills/` 目录而不加载其他配置文件。

---

## 解决方案对比

### 方案 A: 使用 `setting_sources=["project"]`

**优点**:
- SDK 原生支持
- 代码简洁（1 行配置）
- 符合官方设计

**缺点**:
- 可能加载 CLAUDE.md、settings.json 等不需要的配置
- 行为不完全可控
- 需要测试验证

**示例代码**:
```python
options = ClaudeAgentOptions(
    cwd="/path/to/project",
    setting_sources=["project"],
    allowed_tools=["Skill", "Read", "Write", "Bash"]
)
```

---

### 方案 B: 手动加载 Skills（推荐）

**优点**:
- 完全可控，明确知道加载了什么
- 安全隔离，不会引入配置污染
- 代码逻辑清晰

**缺点**:
- 需要手动实现加载逻辑
- 失去 SDK 动态管理功能

**示例代码**:
```python
from pathlib import Path

def _create_options(self) -> ClaudeAgentOptions:
    system_prompt = self._config_loader.load_system_prompt()
    
    # 手动加载 Skills
    project_skills_dir = Path(self.working_directory) / ".claude" / "skills"
    if project_skills_dir.exists():
        for skill_dir in sorted(project_skills_dir.iterdir()):
            if not skill_dir.is_dir():
                continue
            skill_file = skill_dir / "SKILL.md"
            if skill_file.exists():
                skill_name = skill_dir.name
                skill_content = skill_file.read_text(encoding="utf-8")
                system_prompt += f"\n\n# Skill: {skill_name}\n{skill_content}"
    
    return ClaudeAgentOptions(
        system_prompt=system_prompt,
        setting_sources=None,  # 保持隔离
        allowed_tools=[..., "Skill"],
    )
```

---

### 方案 C: 符号链接（Issue #456 临时方案）

**优点**:
- 可以与 SDK 原生机制配合
- 灵活控制加载哪些内容

**缺点**:
- 增加部署复杂度
- 跨平台兼容性问题（Windows 符号链接需要管理员权限）

**示例代码**:
```python
from pathlib import Path

# 创建符号链接
session_path = Path("/path/to/session")
project_claude = Path("/path/to/project/.claude")

claude_symlink = session_path / ".claude"
if project_claude.exists() and not claude_symlink.exists():
    claude_symlink.symlink_to(project_claude)

# 然后使用 setting_sources=["project"]
options = ClaudeAgentOptions(
    cwd=str(session_path),
    setting_sources=["project"],
)
```

---

## 推荐方案

### 短期策略（立即实施）

**采用方案 B（手动加载 Skills）**

理由：
1. ✅ 零配置污染风险
2. ✅ 完全可控，行为确定
3. ✅ 代码逻辑清晰，易于调试
4. ✅ 不依赖 SDK 的未文档化行为

### 中期策略（社区参与）

1. **在 Issue #456 下留言/点赞**
   - 分享你的具体用例
   - 帮助 Anthropic 团队理解需求优先级

2. **关注相关 Issue 的进展**
   - Issue #456: 独立 skill 路径配置
   - Issue #36: Skills 发现问题

### 长期策略（等待官方支持）

期待 Anthropic 添加细粒度配置选项，如：

```python
# 理想的 API 设计
ClaudeAgentOptions(
    skills_path="/path/to/skills",      # 独立的 Skills 路径
    commands_path="/path/to/commands",  # 独立的 Commands 路径
    setting_sources=None,               # 保持配置隔离
)
```

---

## 参考链接汇总

### GitHub Issues

| Issue | 仓库 | 标题 | 链接 |
|-------|-----|------|------|
| #456 | claude-agent-sdk-python | Feature: Add explicit skill/command path configuration | https://github.com/anthropics/claude-agent-sdk-python/issues/456 |
| #36 | claude-agent-sdk-typescript | Skills not being discovered despite correct configuration | https://github.com/anthropics/claude-agent-sdk-typescript/issues/36 |
| #268 | claude-agent-sdk-python | Skills do not appear to be working on Linux | https://github.com/anthropics/claude-agent-sdk-python/issues/268 |
| #186 | claude-agent-sdk-python | setting-sources Flag Compatibility Issue | https://github.com/anthropics/claude-agent-sdk-python/issues/186 |
| #102 | claude-agent-sdk-typescript | Background Agents Cannot Access Skill Tool | https://github.com/anthropics/claude-agent-sdk-typescript/issues/102 |

### 官方文档

| 文档 | 链接 |
|-----|------|
| Agent Skills in the SDK | https://platform.claude.com/docs/en/agent-sdk/skills |
| Agent SDK Overview | https://platform.claude.com/docs/en/agent-sdk/overview |
| Migration Guide | https://platform.claude.com/docs/en/agent-sdk/migration-guide |
| Agent Skills Overview | https://platform.claude.com/docs/en/agents-and-tools/agent-skills/overview |
| Claude Code Skills | https://code.claude.com/docs/en/skills |

### 技术文章

| 文章 | 链接 |
|-----|------|
| Claude Agent Skills: A First Principles Deep Dive | https://leehanchung.github.io/blogs/2025/10/26/claude-skills-deep-dive/ |
| Promptfoo - Claude Agent SDK | https://www.promptfoo.dev/docs/providers/claude-agent-sdk/ |

### 代码仓库

| 仓库 | 链接 |
|-----|------|
| claude-agent-sdk-python | https://github.com/anthropics/claude-agent-sdk-python |
| claude-agent-sdk-typescript | https://github.com/anthropics/claude-agent-sdk-typescript |

---

## 结论

**核心发现**：在 Agent SDK 中单独加载 Skills 而不污染其他配置，目前是一个**已知的设计限制**，尚无官方解决方案。

**推荐做法**：采用手动加载 Skills 的方式（方案 B），这是目前最安全、最可控的方法，与社区开发者采用的策略一致。

**后续行动**：
1. 实施方案 B
2. 在 GitHub Issue #456 下分享用例和点赞
3. 关注 SDK 更新

---

**文档版本**: v1.0  
**最后更新**: 2026-01-26  
**作者**: BIMCanvas 开发团队
