# 控制台日志系统

## 概述

BIMCanvas Server 采用统一的控制台日志系统，将三个服务（Server、Agent、Web）的输出整合到一个控制台窗口，通过**分层前缀系统**让开发者快速定位日志来源。

## 系统特点

### 1. 分层前缀架构

日志采用三层前缀结构：

```
[时间戳] [一级前缀] [二级前缀] [三级标记] 消息内容
```

示例：
```
[20:59:16] [Agent] [MainAgent] [THINK] 用户只是打了个招呼...
[20:59:18] [Agent] [MainAgent] [AI] 您好，我是 BIMCanvas 的室内布置协调助手。
```

### 2. 统一时间戳格式

- **C# 端**: `HH:mm:ss` (精确到秒)
- **Python 端**: `HH:MM:SS.mmm` (精确到毫秒)

### 3. 子进程输出转发

Server 作为主进程，通过读取 Agent 和 Web 子进程的 stdout/stderr，添加前缀后统一输出到控制台。

---

## 前缀层级设计

### 一级前缀 - 服务来源

区分日志来自哪个服务：

| 前缀 | 来源 | 颜色 | 说明 |
|-----|------|------|------|
| `[Server]` | C# 主服务 | 白色 | 程序自身消息 |
| `[Server:ERR]` | C# 主服务错误 | 红色 | 错误信息 |
| `[Agent]` | Python Agent 服务 | 青色 | Agent stdout 转发 |
| `[Agent:ERR]` | Python Agent 错误 | 暗青色 | Agent stderr 转发 |
| `[Web]` | Vite 前端服务 | 绿色 | Vite stdout 转发 |
| `[Web:ERR]` | Vite 前端错误 | 暗绿色 | Vite stderr 转发 |

### 二级前缀 - Agent 内部模块

Agent 服务内部根据模块细分：

| 二级前缀 | 含义 | 使用场景 |
|---------|------|---------|
| `[MainAgent]` | 主 Agent 逻辑 | 核心对话处理 |
| `[#1 任务名]` | 并行 SubAgent | 如 `[#1 客厅家具]` |
| `[Server]` | Agent 服务器层 | HTTP 请求处理、实例管理 |
| `[chat_stream]` | 聊天流处理 | SSE 流式请求 |

### 三级标记 - 会话生命周期

标记 Agent 对话过程中的不同阶段：

| 标记 | 含义 |
|-----|------|
| `[START]` | 会话开始 |
| `[COMPLETE]` | 会话结束 |
| `[USER]` | 用户输入 |
| `[AI]` | AI 回复 |
| `[THINK]` | 思考过程 |
| `[TOOL]` | 工具调用 |

---

## 实现代码位置

### C# 端 (Server)

#### 1. 核心前缀输出函数

**文件**: `BIMCanvas.Server/Program.cs` (36-50 行)

```csharp
static void WriteWithColoredPrefix(string prefix, string message, ConsoleColor prefixColor)
{
    var originalColor = Console.ForegroundColor;
    // 时间戳（灰色）
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.Write($"[{DateTime.Now:HH:mm:ss}] ");
    // 前缀（指定颜色）
    Console.ForegroundColor = prefixColor;
    Console.Write(prefix);
    // 消息（恢复默认）
    Console.ForegroundColor = originalColor;
    Console.WriteLine($" {message}");
}
```

#### 2. 子进程输出转发

**文件**: `BIMCanvas.Server/Program.cs` (264-329 行)

Agent 进程输出转发：
```csharp
_ = Task.Run(async () =>
{
    while (!agentProcess.HasExited)
    {
        var line = await agentProcess.StandardOutput.ReadLineAsync();
        if (!string.IsNullOrEmpty(line))
            WriteWithColoredPrefix("[Agent]", line, ConsoleColor.Cyan);
    }
});
```

Web 进程输出转发：
```csharp
_ = Task.Run(async () =>
{
    while (!webProcess.HasExited)
    {
        var line = await webProcess.StandardOutput.ReadLineAsync();
        if (!string.IsNullOrEmpty(line))
            WriteWithColoredPrefix("[Web]", line, ConsoleColor.Green);
    }
});
```

#### 3. 日志格式化器

**文件**: `BIMCanvas.Server/Logging/ServerConsoleFormatter.cs`

用于 .NET 日志框架集成，根据日志级别动态生成前缀：

```csharp
private static string GetPrefix(LogLevel logLevel)
{
    return logLevel switch
    {
        LogLevel.Trace => "[Server:TRC]",
        LogLevel.Debug => "[Server:DBG]",
        LogLevel.Information => "[Server]",
        LogLevel.Warning => "[Server:WARN]",
        LogLevel.Error => "[Server:ERR]",
        LogLevel.Critical => "[Server:CRIT]",
        _ => "[Server]"
    };
}
```

---

### Python 端 (Agent)

#### 1. AgentLogger 类

**文件**: `BIMCanvas.Agent/src/agent/agent_logger.py`

核心日志类，负责二级前缀和三级标记的生成。

颜色定义（低调配色设计）：
```python
class Colors:
    # 亮度层级（信息重要性）
    PRIMARY = "\033[97m"      # 主要信息（亮白）- AI 回应
    SECONDARY = "\033[2m"     # 次要信息（暗淡）- 思考过程
    TERTIARY = "\033[90m"     # 背景信息（暗灰）- 时间戳、分隔线

    # 角色颜色
    USER = "\033[92m"         # 用户输入（亮绿）
    AI = "\033[97m"           # AI 输出（亮白）
    TOOL = "\033[33m"         # 工具调用（暗黄）
    SUBAGENT = "\033[36m"     # SubAgent（暗青）
```

时间戳格式：
```python
def _timestamp(self) -> str:
    return datetime.now().strftime("%H:%M:%S.%f")[:-3]
    # 输出: "14:23:45.678"
```

二级前缀生成：
```python
def _get_subagent_label(self, subagent_id: str = None) -> str:
    if subagent_id and subagent_id in self._active_subagents:
        info = self._active_subagents[subagent_id]
        seq = info.get('seq', '?')
        short_name = info.get('short_name', '')
        return f"[#{seq} {short_name}]"
    elif self._in_subagent:
        return f"[{self._current_subagent}]"
    else:
        return "[MainAgent]"
```

#### 2. 日志方法调用

**文件**: `BIMCanvas.Agent/src/agent/main_agent.py` (314-328 行, 482-551 行)

```python
# 会话生命周期日志
self._agent_logger.log_session_start()           # ═══ [START] ═══
self._agent_logger.log_user_message(message)     # [USER] 消息内容
self._agent_logger.log_thinking_start()          # [MainAgent] [THINK] ...
self._agent_logger.log_response_start()          # [MainAgent] [AI] ...
self._agent_logger.log_tool_use(name, params)    # [MainAgent] [TOOL] tool_name
self._agent_logger.log_session_complete()        # ═══ [COMPLETE] ═══
```

---

## 文件清单

| 功能 | 文件路径 |
|-----|---------|
| 一级前缀系统 | `BIMCanvas.Server/Program.cs` |
| .NET 日志格式化器 | `BIMCanvas.Server/Logging/ServerConsoleFormatter.cs` |
| Agent 二级前缀 | `BIMCanvas.Agent/src/agent/agent_logger.py` |
| MainAgent 日志调用 | `BIMCanvas.Agent/src/agent/main_agent.py` |

---

## 设计亮点

1. **统一入口**: Server 作为主控台，整合所有服务输出
2. **分层前缀**: 三层结构，快速定位日志来源和上下文
3. **低调配色**: 仅关键信息用颜色标记，避免视觉污染
4. **并行 SubAgent 支持**: 使用序号 (#1, #2, ...) 区分并行任务
5. **流式输出**: 支持分片输出，避免完整块重复
6. **跨平台兼容**: 自动检测终端编码，支持 ANSI 颜色
