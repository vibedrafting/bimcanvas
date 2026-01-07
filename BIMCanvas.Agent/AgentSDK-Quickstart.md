# Claude Agent SDK 快速入门指南

> 基于官方文档整理：https://platform.claude.com/docs/en/agent-sdk/overview

---

## 1. 是什么

Claude Agent SDK 让你把 **Claude Code** 作为库来使用，构建能自主执行任务的 AI Agent。

**核心区别**：

| 方式 | 工作模式 | 适用场景 |
|------|----------|----------|
| Anthropic Client SDK | 你实现工具循环 | 精细控制每一步 |
| **Agent SDK** | Claude 自主执行工具 | 自动化任务、Agent 应用 |

---

## 2. 环境准备

### 2.1 系统要求

- Python 3.10+ 或 Node.js 18+
- ANTHROPIC_API_KEY（从 [Console](https://console.anthropic.com/) 获取）

### 2.2 安装

**步骤 1：安装 Claude Code CLI**（SDK 运行时依赖）

macOS/Linux/WSL：
```bash
curl -fsSL https://claude.ai/install.sh | bash
```

npm（跨平台）：
```bash
npm install -g @anthropic-ai/claude-code
```

Homebrew：
```bash
brew install --cask claude-code
```

安装后运行 `claude` 完成认证。

**步骤 2：安装 SDK**

Python：
```bash
pip install claude-agent-sdk
```

TypeScript：
```bash
npm install @anthropic-ai/claude-agent-sdk
```

### 2.3 设置 API Key

**Windows PowerShell**：
```powershell
$env:ANTHROPIC_API_KEY = "sk-ant-xxx"
```

**Windows CMD**：
```cmd
set ANTHROPIC_API_KEY=sk-ant-xxx
```

**Linux/macOS**：
```bash
export ANTHROPIC_API_KEY=sk-ant-xxx
```

---

## 3. 两种使用方式

SDK 提供两种 API 来与 Claude Code 交互：

| 特性 | `query()` | `ClaudeSDKClient` |
|------|-----------|-------------------|
| 会话 | 每次新建 | 可复用/继续 |
| 对话上下文 | 单轮 | 多轮连续 |
| Hooks | ❌ 不支持 | ✅ 支持 |
| 自定义工具 | ❌ 不支持 | ✅ 支持 |
| 中断控制 | ❌ 不支持 | ✅ 支持 |
| 适用场景 | 一次性任务 | 交互式对话 |

---

## 4. 最简示例（query 方式）

### 4.1 基础对话

```python
import asyncio
from claude_agent_sdk import query

async def main():
    async for message in query(prompt="你好，请介绍一下你自己"):
        print(message)

asyncio.run(main())
```

### 4.2 提取文本内容

```python
import asyncio
from claude_agent_sdk import query, AssistantMessage, TextBlock

async def main():
    async for message in query(prompt="Hello"):
        if isinstance(message, AssistantMessage):
            for block in message.content:
                if isinstance(block, TextBlock):
                    print(block.text)

asyncio.run(main())
```

### 4.3 使用配置选项

```python
from claude_agent_sdk import query, ClaudeAgentOptions

options = ClaudeAgentOptions(
    system_prompt="你是一个友好的助手",
    max_turns=1
)

async for message in query(prompt="讲个笑话", options=options):
    print(message)
```

---

## 5. ClaudeSDKClient（多轮对话）

对于需要多轮交互、使用 Hooks 或自定义工具的场景，使用 `ClaudeSDKClient`：

```python
import asyncio
from claude_agent_sdk import ClaudeSDKClient, ClaudeAgentOptions, AssistantMessage, TextBlock

async def main():
    options = ClaudeAgentOptions(
        allowed_tools=["Read", "Glob"],
        permission_mode="bypassPermissions"
    )

    async with ClaudeSDKClient(options=options) as client:
        # 第一轮对话
        await client.query("读取 auth.py 文件")
        async for message in client.receive_response():
            if isinstance(message, AssistantMessage):
                for block in message.content:
                    if isinstance(block, TextBlock):
                        print(block.text)

        # 第二轮对话（Claude 记得上下文）
        await client.query("找出所有调用它的地方")
        async for message in client.receive_response():
            if isinstance(message, AssistantMessage):
                for block in message.content:
                    if isinstance(block, TextBlock):
                        print(block.text)

asyncio.run(main())
```

---

## 6. 内置工具

SDK 提供开箱即用的工具，无需自己实现：

| 工具 | 功能 | 示例场景 |
|------|------|----------|
| `Read` | 读取文件 | 分析代码、读取配置 |
| `Write` | 创建文件 | 生成代码、保存结果 |
| `Edit` | 编辑文件 | 修复 Bug、重构代码 |
| `Bash` | 执行命令 | 运行脚本、Git 操作 |
| `Glob` | 文件匹配 | 查找 `**/*.py` |
| `Grep` | 内容搜索 | 搜索 TODO 注释 |
| `WebSearch` | 网络搜索 | 查询最新信息 |
| `WebFetch` | 获取网页 | 抓取文档内容 |
| `Task` | 子代理 | 委派复杂任务 |

### 6.1 使用工具示例

```python
from claude_agent_sdk import query, ClaudeAgentOptions

async def find_todos():
    options = ClaudeAgentOptions(
        allowed_tools=["Read", "Glob", "Grep"],
        permission_mode="bypassPermissions"  # 只读操作可跳过权限
    )

    async for message in query(
        prompt="找出所有 TODO 注释并总结",
        options=options
    ):
        print(message)
```

### 6.2 文件编辑示例

```python
options = ClaudeAgentOptions(
    allowed_tools=["Read", "Edit", "Bash"],
    permission_mode="acceptEdits"  # 自动接受文件编辑
)

async for message in query(
    prompt="修复 auth.py 中的 Bug",
    options=options
):
    print(message)
```

---

## 7. 核心配置项

```python
ClaudeAgentOptions(
    # 基础配置
    system_prompt="系统提示词",
    max_turns=10,                    # 最大对话轮数
    cwd="/path/to/project",          # 工作目录

    # 工具配置
    allowed_tools=["Read", "Bash"],  # 允许的工具列表
    permission_mode="default",       # 权限模式

    # 会话管理
    resume="session-id",             # 恢复之前的会话

    # MCP 服务器
    mcp_servers={
        "playwright": {
            "command": "npx",
            "args": ["@playwright/mcp@latest"]
        }
    }
)
```

### 7.1 权限模式

| 模式 | 说明 |
|------|------|
| `default` | 默认，敏感操作需确认 |
| `acceptEdits` | 自动接受文件编辑 |
| `bypassPermissions` | 跳过所有权限检查（只读场景） |
| `plan` | 规划模式，不执行实际操作 |

---

## 8. 会话管理

可以保存会话 ID，之后恢复上下文继续对话：

```python
session_id = None

# 第一次查询：获取 session_id
async for message in query(prompt="读取认证模块"):
    if hasattr(message, 'subtype') and message.subtype == 'init':
        session_id = message.session_id  # 直接属性访问

# 恢复会话，继续对话
async for message in query(
    prompt="找出所有调用它的地方",
    options=ClaudeAgentOptions(resume=session_id)
):
    print(message)
```

---

## 9. MCP 集成

可以接入 MCP Server 扩展能力：

```python
options = ClaudeAgentOptions(
    mcp_servers={
        "playwright": {
            "command": "npx",
            "args": ["@playwright/mcp@latest"]
        }
    }
)

async for message in query(
    prompt="打开 example.com 并描述页面内容",
    options=options
):
    print(message)
```

---

## 10. 子代理（Subagents）

启用 `Task` 工具，Claude 会自动判断何时需要委派子任务：

```python
options = ClaudeAgentOptions(
    allowed_tools=["Read", "Glob", "Grep", "Task"]
)

async for message in query(
    prompt="分析这个代码库的安全漏洞",
    options=options
):
    print(message)
```

---

## 11. Hooks 系统

> **重要**：Hooks 仅在 `ClaudeSDKClient` 中支持，`query()` 函数不支持 hooks。

在 Agent 生命周期的关键点执行自定义代码：

```python
import asyncio
from claude_agent_sdk import ClaudeSDKClient, ClaudeAgentOptions, HookMatcher, HookContext
from typing import Any

async def log_file_change(
    input_data: dict[str, Any],
    tool_use_id: str | None,
    context: HookContext
) -> dict[str, Any]:
    """文件修改后记录日志"""
    file_path = input_data.get('tool_input', {}).get('file_path', 'unknown')
    with open('./audit.log', 'a') as f:
        f.write(f"文件已修改: {file_path}\n")
    return {}

async def main():
    options = ClaudeAgentOptions(
        permission_mode="acceptEdits",
        hooks={
            "PostToolUse": [HookMatcher(matcher="Edit|Write", hooks=[log_file_change])]
        }
    )

    async with ClaudeSDKClient(options=options) as client:
        await client.query("重构 utils.py 提高可读性")
        async for message in client.receive_response():
            print(message)

asyncio.run(main())
```

**可用 Hooks**：
- `PreToolUse` - 工具执行前
- `PostToolUse` - 工具执行后
- `UserPromptSubmit` - 用户提交提示时
- `Stop` - Agent 停止时
- `SubagentStop` - 子代理停止时
- `PreCompact` - 消息压缩前

> 注意：Python SDK 不支持 `SessionStart`、`SessionEnd` 和 `Notification` hooks。

---

## 12. 参考资源

- [官方文档](https://platform.claude.com/docs/en/agent-sdk/overview)
- [Python SDK GitHub](https://github.com/anthropics/claude-agent-sdk-python)
- [TypeScript SDK GitHub](https://github.com/anthropics/claude-agent-sdk-typescript)
- [示例项目](https://github.com/anthropics/claude-agent-sdk-demos)

---

## 附：TypeScript 对照

```typescript
import { query } from "@anthropic-ai/claude-agent-sdk";

for await (const message of query({
  prompt: "修复 auth.py 中的 Bug",
  options: {
    allowedTools: ["Read", "Edit", "Bash"],
    permissionMode: "acceptEdits"
  }
})) {
  console.log(message);
}
```
