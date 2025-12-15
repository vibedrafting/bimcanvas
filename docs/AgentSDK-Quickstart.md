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

**Python**：
```bash
pip install claude-agent-sdk
```

**TypeScript**：
```bash
npm install @anthropic-ai/claude-agent-sdk
```

> 注意：SDK 会自动捆绑 Claude Code CLI，无需单独安装。

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

## 3. 最简示例

### 3.1 基础对话

```python
import asyncio
from claude_agent_sdk import query

async def main():
    async for message in query(prompt="你好，请介绍一下你自己"):
        print(message)

asyncio.run(main())
```

### 3.2 提取文本内容

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

### 3.3 使用配置选项

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

## 4. 内置工具

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

### 4.1 使用工具示例

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

### 4.2 文件编辑示例

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

## 5. 核心配置项

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

### 5.1 权限模式

| 模式 | 说明 |
|------|------|
| `default` | 默认，敏感操作需确认 |
| `bypassPermissions` | 跳过所有权限检查（只读场景） |
| `acceptEdits` | 自动接受文件编辑 |

---

## 6. 会话管理

可以保存会话 ID，之后恢复上下文继续对话：

```python
session_id = None

# 第一次查询：获取 session_id
async for message in query(prompt="读取认证模块"):
    if hasattr(message, 'subtype') and message.subtype == 'init':
        session_id = message.data.get('session_id')

# 恢复会话，继续对话
async for message in query(
    prompt="找出所有调用它的地方",
    options=ClaudeAgentOptions(resume=session_id)
):
    print(message)
```

---

## 7. MCP 集成

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

## 8. 子代理（Subagents）

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

## 9. Hooks 系统

在 Agent 生命周期的关键点执行自定义代码：

```python
options = ClaudeAgentOptions(
    permission_mode="acceptEdits",
    hooks={
        "PostToolUse": [{
            "matcher": "Edit|Write",
            "hooks": [{
                "type": "command",
                "command": "echo \"文件已修改\" >> ./audit.log"
            }]
        }]
    }
)
```

**可用 Hooks**：
- `PreToolUse` - 工具执行前
- `PostToolUse` - 工具执行后
- `Stop` - Agent 停止时
- `SessionStart` / `SessionEnd` - 会话开始/结束

---

## 10. 参考资源

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
