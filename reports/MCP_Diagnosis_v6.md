# MCP 工具调用问题诊断报告 v6

## 测试结果（2026-01-20）

### Phase 1：第三方 MCP 对比测试结果

#### 使用 `query()` 函数测试（✅ 成功）

**配置**：
```python
options = ClaudeAgentOptions(
    mcp_servers={
        "filesystem": {
            "type": "stdio",
            "command": "npx",
            "args": ["-y", "@modelcontextprotocol/server-filesystem", "C:\\tmp\\test"]
        }
    },
    allowed_tools=["mcp__filesystem__read_file", ...]
)

async for message in query(prompt="...", options=options):
    ...
```

**测试日志**：
```
[MCP] ===== MCP 服务器状态 =====
[MCP] {'name': 'filesystem', 'status': 'connected'}
[MCP] =============================

[TOOL_USE] mcp__filesystem__read_file: {'path': 'C:/tmp/test/hello.txt'}
```

**结论**：
- ✅ Filesystem stdio MCP 成功连接
- ✅ MCP 工具正确注册到 tools 列表
- ✅ 模型正确调用了 `mcp__filesystem__read_file`
- ✅ 工具调用流程完全正常

#### 使用 `ClaudeSDKClient` 测试（❌ 失败）

**配置**：
```python
options = ClaudeAgentOptions(
    mcp_servers={
        "canvas": canvas_mcp,  # SDK MCP
        "filesystem": {...}   # stdio MCP
    },
    allowed_tools=[...]
)

client = ClaudeSDKClient(options)
await client.connect()
await client.query(prompt)
async for message in client.receive_response():
    ...
```

**测试日志**：
```
[MCP] ===== tools/list 请求收到 =====
[MCP] 返回工具列表: ['ai_job_create', 'ai_job_complete']
# filesystem 的 tools/list 从未收到

[AI] <mcp__filesystem__read_file>{"path":"..."}</mcp__filesystem__read_file>
# 模型输出 XML 文本，而非 tool_use
```

**结论**：
- ❌ Filesystem stdio MCP 未被查询 tools/list
- ❌ Canvas SDK MCP 的 tools/list 正常，但 tools/call 从未触发
- ❌ 模型输出 XML 文本而非 tool_use content block

---

## 问题定位

### 根本原因

| 接口 | MCP 工作状态 | 分析 |
|------|-------------|------|
| `query()` 函数 | ✅ 正常 | 每次查询启动新 CLI 进程，完整初始化 |
| `ClaudeSDKClient` | ❌ 失败 | 持久连接模式，MCP 初始化可能不完整 |

**核心差异**：
- `query()` - 无状态，每次调用启动新进程，MCP 完整初始化
- `ClaudeSDKClient` - 有状态，维持持久连接，MCP 可能在后续调用中未正确加载

### 问题链

```
ClaudeSDKClient.connect()
    ↓ MCP 服务器配置传入
    ↓ CLI 启动并建立持久连接
    ↓ [问题] MCP 服务器初始化不完整或未持久化
    ↓
ClaudeSDKClient.query(prompt)
    ↓ [问题] CLI 未将 MCP 工具传递给模型
    ↓ 模型不知道 MCP 工具是真实可调用的
    ↓ 模型输出 XML 格式的"工具调用意图"
    ↓ CLI 不识别为 tool_use，不触发 tools/call
```

---

## 解决方案

### 方案 A：改用 `query()` 函数（推荐）

将 `MainAgent` 从 `ClaudeSDKClient` 改为使用 `query()` 函数。

**优点**：
- MCP 工具调用完全正常
- 每次查询独立，状态清晰

**缺点**：
- 失去持久会话能力
- 每次查询需要完整初始化（可能更慢）

**实现**：
```python
class MainAgent:
    async def chat(self, user_message: str) -> str:
        async for message in query(
            prompt=user_message,
            options=self._create_options()
        ):
            # 处理消息
            pass
```

### 方案 B：排查 ClaudeSDKClient 的 MCP 初始化

深入 Agent SDK 源码，找出为什么 `ClaudeSDKClient` 的 MCP 初始化不完整。

**需要检查**：
1. `ClaudeSDKClient.connect()` 中 MCP 服务器的初始化流程
2. 持久连接中 MCP 状态是否丢失
3. 是否需要在每次 query 前重新初始化 MCP

### 方案 C：混合模式

- 普通对话使用 `ClaudeSDKClient`（快速响应）
- 需要 MCP 工具时使用 `query()`（可靠调用）

---

## 遗留问题

### 1. Canvas SDK MCP 的 tools/call 未触发

即使 `tools/list` 正常返回工具列表，`tools/call` 也从未被调用。

**可能原因**：
- CLI 没有将 SDK MCP 的工具定义正确传递给模型
- 模型认为这些只是"允许调用"的工具名，而非实际可用的工具

### 2. 允许目录配置问题

Filesystem MCP 的允许目录配置可能未正确传递：
```
配置: args: ["...", "C:\\tmp\\test"]
错误: 当前允许访问的目录是: E:\...\BIMCanvas.Agent
```

**需要验证**：调用 `list_allowed_directories` 确认 MCP 服务器收到的配置

---

## 下一步行动

1. **短期**：将 `MainAgent` 改为使用 `query()` 函数，验证 Canvas SDK MCP 是否能正常工作
2. **中期**：排查 `ClaudeSDKClient` 的 MCP 初始化问题
3. **长期**：考虑是否需要维持持久会话，或接受 `query()` 的无状态模式

---

## 测试文件清单

| 文件 | 用途 |
|------|------|
| `test_mcp_filesystem.py` | 使用 ClaudeSDKClient 测试（失败） |
| `test_mcp_query.py` | 使用 query() 函数测试（成功） |

---

## 附录：关键日志

### query() 函数成功日志

```
[DEBUG] SystemMessage.data: {
    'tools': [..., 'mcp__filesystem__read_file', ...],
    'mcp_servers': [{'name': 'filesystem', 'status': 'connected'}],
    ...
}

[TOOL_USE] mcp__filesystem__read_file: {'path': 'C:/tmp/test/hello.txt'}
```

### ClaudeSDKClient 失败日志

```
[MCP] ===== tools/list 请求收到 =====
[MCP] 返回工具列表: ['ai_job_create', 'ai_job_complete']
# 只有 Canvas SDK MCP 收到 tools/list，filesystem 未收到

[AI TEXT] <mcp__filesystem__read_file>{"path":"..."}</mcp__filesystem__read_file>
# 模型输出 XML 文本，工具未被真正调用
```
