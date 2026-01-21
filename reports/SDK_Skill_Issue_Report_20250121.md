# BIMCanvas Agent SDK 问题汇报

**日期**: 2025-01-21
**版本**: SDK anthropic 0.75.0 → 0.76.0
**状态**: 未解决（需回滚到手动注入方案）

---

## 问题概述

尝试使用 Claude Agent SDK 的 `setting_sources` 参数自动加载 Skill 文件，导致系统卡住无法对话。

---

## 问题列表

### Bug #1: 初始对话不能工作（已解决）

| 项目 | 内容 |
|------|------|
| **发现时间** | 2025-01-21 早些时候 |
| **症状** | Agent 无法正常对话 |
| **解决方式** | 与能运行的分支 `rollback/log` 对比，恢复正常配置 |
| **状态** | ✅ 已解决 |

**备注**: 具体原因待补充记录。

---

### Bug #2: SDK Bug #406 - setting_sources 导致静默失败（未解决）

| 项目 | 内容 |
|------|------|
| **发现时间** | 2025-01-21 |
| **来源** | SDK 测试代码 `test_agents_and_settings.py` |
| **症状** | 使用 `setting_sources=["project"]` 或 `["user", "project"]` 时，迭代器在 init 消息后静默失败，永远不产生 `AssistantMessage` 或 `ResultMessage` |
| **表现** | 控制台在 "Using bundled Claude Code CLI" 后卡住，不打印用户输入 |
| **状态** | ❌ 未解决 |

#### SDK 测试代码中的记录

**文件**: `docs/agent_sdk/claude-agent-sdk-python/tests/e2e/test_agents_and_settings.py`

```python
"""Test that filesystem-based agents load via setting_sources and produce full response.

This is the core test for issue #406. It verifies that when using
setting_sources=["project"] with a .claude/agents/ directory containing
agent definitions, the SDK:
1. Loads the agents (they appear in init message)
2. Produces a full response with AssistantMessage
3. Completes with a ResultMessage

The bug in #406 causes the iterator to complete after only the
init SystemMessage, never yielding AssistantMessage or ResultMessage.
"""
```

#### 尝试的修复措施

| # | 措施 | 结果 |
|---|------|------|
| 1 | 使用 `setting_sources=["project"]` | ❌ 卡住 |
| 2 | 改为 `setting_sources=["user", "project"]` | ❌ 卡住 |
| 3 | 升级 SDK 0.75.0 → 0.76.0 | ❌ 仍然卡住 |

#### SDK 底层机制

`setting_sources` 参数在 `subprocess_cli.py` 中被转换为 CLI 参数：

```python
# subprocess_cli.py 第 287-292 行
sources_value = (
    ",".join(self._options.setting_sources)
    if self._options.setting_sources is not None
    else ""
)
cmd.extend(["--setting-sources", sources_value])
```

---

## 当前可行方案

### 方案: skill_loader 手动注入（已验证可行）

使用 `skill_loader.py` 手动读取 Skill 文件内容，注入到 `system_prompt` 中：

```python
# main_agent.py
from .skill_loader import get_skill_loader

# 在 _create_options() 中
skill_loader = get_skill_loader()
main_skills = skill_loader.get_main_agent_skills()
if main_skills:
    system_prompt = f"{system_prompt}\n\n{main_skills}"
```

**优点**:
- 已验证可正常工作
- 不依赖有 Bug 的 SDK 功能

**缺点**:
- 需要额外的 `skill_loader.py` 模块
- 不是 SDK 推荐的标准方式

---

## 后续计划

1. **短期**: 回滚到 `skill_loader` 手动注入方案，确保系统正常运行
2. **中期**: 监控 Claude Agent SDK 更新，关注 Bug #406 的修复进展
3. **长期**: Bug 修复后，切换到 `setting_sources` 标准方案

---

## 相关文件

| 文件 | 说明 |
|------|------|
| `BIMCanvas.Agent/src/agent/main_agent.py` | 主 Agent 实现 |
| `BIMCanvas.Agent/src/agent/skill_loader.py` | 手动 Skill 加载器 |
| `docs/agent_sdk/docs/Guides/Agent Skills in the SDK.md` | SDK 官方 Skill 文档 |
| `docs/agent_sdk/claude-agent-sdk-python/tests/e2e/test_agents_and_settings.py` | Bug #406 测试代码 |

---

## 参考链接

- Claude Agent SDK 官方文档
- Bug #406 相关测试代码（本地 `docs/agent_sdk` 目录）
