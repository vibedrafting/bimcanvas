# NanoClaw 项目研究：Agent SDK 实战经验精选

> **筛选标准**：只保留经过实际踩坑验证的、BIMCanvas.Agent 能直接受益的内容。
> 排除 NanoClaw 特有场景（WhatsApp 多群组、Docker 容器隔离、IPC 文件通信等）的设计。
>
> **研究日期**：2026-02-26
> **项目位置**：`E:\工作文档\开发类\MyCode\NanoClaw`

---

## 真正有价值的发现（3 个）

### 1. isSingleUserTurn 陷阱——Agent Teams 的致命问题

**这是 NanoClaw 开发者通过实际踩坑发现的 SDK 隐藏行为，官方文档未充分说明。**

SDK 内部有一个变量 `isSingleUserTurn`：
- 传 **string prompt** → `isSingleUserTurn = true`
- 传 **AsyncIterable** → `isSingleUserTurn = false`

当 `isSingleUserTurn = true` 且第一个 `result` 消息到达时：

```
SDK 自动关闭 CLI stdin
  ↓
CLI 检测到 stdin 关闭
  ↓
如果有活跃的 Agent Teams 成员，注入 shutdown prompt：
  "You MUST shut down your team before preparing your final response"
  ↓
Leader Agent 被迫杀死仍在工作的 SubAgent ❌
```

**实际场景**：
```
时间 0s:     Leader 启动 SubAgent 去做 5 分钟的研究
时间 2s:     Leader 先完成自己的部分，返回 result
时间 2.1s:   SDK 关闭 stdin（因为 isSingleUserTurn=true）
时间 2.2s:   SubAgent 被强制关闭，5 分钟的工作白费
```

**解决方案**：用 AsyncIterable 代替 string prompt。NanoClaw 的 MessageStream 实现：

```typescript
class MessageStream {
  private queue: SDKUserMessage[] = [];
  private waiting: (() => void) | null = null;
  private done = false;

  push(text: string): void {
    this.queue.push({
      type: 'user',
      message: { role: 'user', content: text },
      parent_tool_use_id: null, session_id: '',
    });
    this.waiting?.();
  }
  end(): void { this.done = true; this.waiting?.(); }

  async *[Symbol.asyncIterator](): AsyncGenerator<SDKUserMessage> {
    while (true) {
      while (this.queue.length > 0) yield this.queue.shift()!;
      if (this.done) return;
      await new Promise<void>(r => { this.waiting = r; });
      this.waiting = null;
    }
  }
}

const stream = new MessageStream();
stream.push(prompt);
query({ prompt: stream, ... });  // AsyncIterable → isSingleUserTurn = false ✓
```

**对 BIMCanvas 的意义**：如果 MainAgent 使用 Agent Teams（SubAgent 做区域分析、模块推荐等），**必须**用 AsyncIterable 模式，否则 SubAgent 会被默默杀死——而且表面上看不出问题，因为 Leader 的结果已经返回了。

**来源**：`container/agent-runner/src/index.ts:64-96` + `docs/SDK_DEEP_DIVE.md`

---

### 2. query() 返回的消息流比文档说的复杂得多

**官方文档只提到 7 种消息类型，NanoClaw 开发者逆向工程发现实际有 16 种。**

对 BIMCanvas 重要的几个：

| 消息类型 | 官方文档提到 | 实际行为 |
|----------|------------|---------|
| `result` (success) | ✅ | 一个 query **可能产生多个** result 消息（Agent Teams 场景） |
| `result` (error_*) | 部分 | 有 4 种错误子类型：`error_during_execution`、`error_max_turns`、`error_max_budget_usd`、`error_max_structured_output_retries` |
| `system/task_notification` | ❌ | 后台 Task/SubAgent 完成时发出，含 `task_id`、`status`、`summary` |
| `system/init` | ✅ | 含 `session_id`，需要捕获 |
| `assistant` | ✅ | 含 `uuid`，可用于会话恢复点 |

**关键发现：一个 query 可能产生多个 result**

NanoClaw 的 SDK_DEEP_DIVE.md 指出：
> "你会收到初始的 result，但 AsyncGenerator 可能继续产生更多消息，当 Agent Teams 成员处理响应并重新进入循环时。Generator 只在所有 teammates 关闭时才真正完成。"

NanoClaw 用 `resultCount` 计数器追踪：

```typescript
let resultCount = 0;
for await (const message of query({...})) {
  if (message.type === 'result') {
    resultCount++;
    const textResult = 'result' in message ? message.result : null;
    log(`Result #${resultCount}: subtype=${message.subtype}`);
    // 每个 result 都立即处理，不要只取第一个
  }
  if (message.type === 'system' && message.subtype === 'task_notification') {
    // 后台任务完成通知——官方文档没充分说明这个
    const tn = message as { task_id: string; status: string; summary: string };
    log(`Task ${tn.task_id}: ${tn.status}`);
  }
}
```

**对 BIMCanvas 的意义**：不能假设 `for await` 循环只返回一个 result。如果用了 Agent Teams，需要处理多个 result 和 task_notification。

**来源**：`container/agent-runner/src/index.ts:417-487` + `docs/SDK_DEEP_DIVE.md:326-327`

---

### 3. 超时判断的竞态条件——来自真实 Bug 修复

**这是从 Git 历史中发现的，经过两次 Bug 修复才稳定的逻辑。**

#### Bug #1：硬超时与空闲超时的竞态（commit `8eb80d4`）

问题：Agent 完成输出后进入空闲等待，硬超时和空闲超时同时到达。硬超时先触发 → 返回 error → 触发消息游标回滚 → 但消息已经发给用户了 → **无限重复发送同样的消息**。

解决：
```typescript
// 1. 硬超时必须比空闲超时晚
const timeoutMs = Math.max(configTimeout, IDLE_TIMEOUT + 30_000);

// 2. 超时后根据"是否曾经有过输出"来判断
if (timedOut && hadStreamingOutput) {
  // 有过输出的超时 = 正常的空闲清理，不是错误
  resolve({ status: 'success' });  // ← 不是 error！
} else if (timedOut && !hadStreamingOutput) {
  // 从未有过输出 = 真正的超时错误
  resolve({ status: 'error', error: 'Container timed out' });
}
```

#### Bug #2：idle 通知在流式模式下永远不触发（commit `c6b69e8`）

原始代码：
```typescript
// ❌ 错误：只在 result 为空时通知空闲
if (!result.result && result.status === 'success') {
  queue.notifyIdle(chatJid);
}
```

问题：Agent Teams 模式下每个 result 都有文本内容，`!result.result` 永远为 false，idle timer 永远不启动，容器 30 分钟后被强制杀死。

修复：
```typescript
// ✅ 正确：所有成功的 result 都通知空闲
if (result.status === 'success') {
  queue.notifyIdle(chatJid);
}
```

**对 BIMCanvas 的意义**：如果设计了任何超时/重试机制，必须考虑：
1. "有输出后超时"和"无输出超时"是完全不同的场景
2. 已经发送给用户的结果不能因为后续错误而"回滚重试"
3. Agent Teams 的流式结果与单次调用的行为不同

**来源**：`src/container-runner.ts:375-447` + Git commits `8eb80d4`, `c6b69e8`

---

## 值得参考但非必须的（2 个）

### 4. PreToolUse Hook 清理环境变量（安全纵深防御）

NanoClaw 在每个 Bash 命令前注入 `unset`，防止 Agent 通过 `echo $ANTHROPIC_API_KEY` 泄露密钥：

```typescript
function createSanitizeBashHook(): HookCallback {
  return async (input) => {
    const command = input.tool_input?.command;
    if (!command) return {};
    return {
      hookSpecificOutput: {
        hookEventName: 'PreToolUse',
        updatedInput: {
          ...input.tool_input,
          command: `unset ANTHROPIC_API_KEY CLAUDE_CODE_OAUTH_TOKEN 2>/dev/null; ${command}`,
        },
      },
    };
  };
}
```

**价值判断**：这是纵深防御，不是"必须"。如果 BIMCanvas.Agent 运行在受控环境中且不暴露 Bash 工具给外部，风险较低。但实现成本也很低（10 行代码），值得加上。

### 5. PreCompact Hook 归档对话

在 SDK 压缩上下文前，将完整对话归档为 Markdown 文件，存储在 `conversations/` 目录。

**价值判断**：仅在需要长期运行会话时有用。BIMCanvas.Agent 如果是单次 query 模式，上下文不会触发压缩，这个 Hook 不会被调用。**延后考虑**。

---

## 明确不需要学的（排除项）

| NanoClaw 模式 | 为什么不适用 BIMCanvas |
|--------------|---------------------|
| Docker 容器隔离 | 多租户需求，BIMCanvas 是单项目单用户 |
| IPC 文件通信 | 容器间通信方案，BIMCanvas 用 SSE/MCP |
| GroupQueue 并发控制 | 多群组并发，BIMCanvas 无此需求 |
| 消息格式化为 XML | WhatsApp 消息渲染，BIMCanvas 用 JSON |
| 会话恢复 (resume) | BIMCanvas 每次提供完整 JSON 状态，不依赖会话历史 |
| 触发词过滤 | WhatsApp 群组的 @mention 机制 |
| 定时任务调度 | BIMCanvas 事件驱动，不需要 cron |
| `<internal>` 标签过滤 | 聊天输出过滤，BIMCanvas 输出是 JSON 数据 |

---

## 附录：关键文件索引

| 文件 | 与上述发现的关联 |
|------|----------------|
| `container/agent-runner/src/index.ts` | 发现 1（MessageStream）、发现 2（消息处理循环） |
| `src/container-runner.ts` | 发现 3（超时竞态 Bug 修复） |
| `docs/SDK_DEEP_DIVE.md` | 发现 1、2 的原始分析文档 |
| `container/agent-runner/src/ipc-mcp-stdio.ts` | 参考 4（Hook 实现） |
