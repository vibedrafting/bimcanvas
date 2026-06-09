# Agent ↔ Web 流协议契约 Registry（骨架 · 持续维护）

> 本文是 Agent(Python)↔ Web(Vue/TS) 一切实时通信的**单一真源登记表**。
> 目的：杜绝「迁移 producer 信号却漏改 consumer」「靠信号缺席推断状态」「终态默认不安全」这类静默回归
> （2026-06-09 两起 workflow Task 卡片过早 Completed 事故的同根问题）。
>
> **铁律**：新增 / 修改 / 删除任何信号（chunk type、SSE event、状态转移），**必须同步更新本表**，
> 并 grep 全量 consumer 确认契约不破。本表与代码不一致即视为 bug。
>
> 状态：🚧 骨架。已核实项标注 file:line（行号会漂移，以符号名为准）；未核实项标 `TODO/⚠`，由维护者补全校验。

---

## 0. 名词与边界

- **chunk**：Agent 端 `StreamChunk`（`BIMCanvas.Agent/src/runtime/chunks.py`），运行时中立的流式单元，`type` 是裸字符串。
- **envelope event**：`main_stream.py` 把 chunk 映射成的 `MainStreamEvent`（现代点号命名，如 `text.delta`）。
- **legacy event**：未进 `EVENT_TYPE_MAP` 的 chunk 原样透传（`build_legacy_chunk_event_data`，仅 `{type, ...}`）。
- **SSE**：两条独立通道（见 §1）。

---

## 1. 两条通信通道（务必分清，两起事故都源于混淆）

### 通道 A：前台回合流（turn stream）
用户发消息 → 一次回合的实时增量。

```
Agent _chat_stream_impl (main_agent.py) ── yield StreamChunk
  → main_stream.py MainStreamMapper.map_chunk → envelope/legacy event
  → http_server.py chat stream 端点（text/event-stream，_try_write_sse_data）
  → 前端 useChatStream.ts normalizeStreamEvent → switch(normalizedEvent.eventType)
  → 同时 runtime_store.append_event_history 落 history（重建真源）
```
承载：主控/SubAgent 的 thinking / text / 工具调用 / 回合终态。

### 通道 B：后台带外（interaction SSE，detach 后唯一实时来源）
workflow detach 到后台后，回合已结束，进度/完成走带外。

```
Agent _emit_background_completion / _push_background_progress
  → host 回调 _background_task_pusher / _background_progress_pusher (http_server.py)
  → runtime_store.push_background_task / push_background_progress (store.py)
      ① push_background_task 落 history（turnId="bgtask:<taskId>" 的 text.completed）
      ② _publish 到 interaction SSE（/api/interaction/events）
  → 前端 InteractionChannelService → BackgroundTaskService
  → useBackgroundTask（注入 Chat 气泡）+ useWorkflowProgress（Task 页状态）
```
承载：`background_task.completed`、`background_task.progress`（含 `workflow_progress` / `workflow_phases` 两种 kind）。
复用 interaction SSE 通道（与 Question / Screenshot 同源）。

---

## 2. Registry：通道 A（前台回合 chunk / event）

| chunk.type (Agent) | envelope eventType | producer（main_agent.py 约） | consumer（useChatStream.ts 约） | 关键字段 | 语义 | 带状态转移? |
|---|---|---|---|---|---|---|
| `thinking` | `thinking.delta` | _process_streaming_event / _chat_stream_impl | applyEventToCurrentMessage | content | 思考增量 | 否 |
| `thinking_complete` | `thinking.completed` | TODO⚠ | 同上 | content | 思考块结束 | 否 |
| `text` | `text.delta` | _chat_stream_impl text_delta | enqueueDeltaEvent | content | 正文增量 | 否 |
| `text_complete` | `text.completed` | _chat_stream_impl(:1719) | 同上 | content | 正文整块（未流式时） | 否 |
| `tool_call_start` | `tool.started` | ToolUseBlock 普通工具(:1770) | tool.started case | tool_call_id/tool_name/tool_params | 工具调用开始 | 否 |
| `tool_call_complete` | `tool.output` + `tool.completed` | _build_tool_completion_chunk | _map_tool_completion → tool.completed | tool_call_id/success/error/output | 工具完成（map_chunk 拆两事件） | 否 |
| `subagent_start` | `subtask.started` | Task 工具(:1742) | subtask.started | subagent_id/name/type | SubAgent 开始 | 否 |
| `subagent_complete` | `subtask.completed` | _build_subagent_completion_chunk | subtask.completed | subagent_id/result | SubAgent 完成 | 否 |
| `subagent_progress` | `subtask.progress` | SubAgent 进度 | subtask.progress | taskId/usage/lastToolName | SubAgent 进度 | 否 |
| `rate_limit` | `runtime.rate_limit` | RateLimitEvent 分支 | runtime.rate_limit | extra | 限流提示 | 否 |
| `task_output_polling` | （legacy 透传） | **TaskOutput 工具(:1748)**（旧轮询式后台，现已基本弃用） | task_output_polling case → `isPollingBackground=true` | task_id/timeout | 旧式后台脱离信号 | ⚠ **是**（置 isPollingBackground） |
| `workflow_detached` | （legacy 透传，LEGACY_EVENT_TYPE_MAP 映射自身） | **真后台脱离(:1831)**，2026-06-09 新增 | workflow_detached case → `isPollingBackground=true` | （无） | 新式 push 后台脱离信号 | ⚠ **是**（置 isPollingBackground，跳过内联收口） |
| （终态） | `turn.completed` / `turn.failed` | main_stream build_*_terminal_event + host | turn.completed/failed case | error.code/httpStatus | 回合终态 | ⚠ 是（回合级；workflow 内联收口挂在 sendMessage finally） |
| `session_ready` | （legacy 特判） | host | session_ready case | sessionId/windowId | 会话就绪 | 否 |

> 映射维护点：Agent `main_stream.py:EVENT_TYPE_MAP`；前端 `useChatStream.ts:LEGACY_EVENT_TYPE_MAP`。**两份手工同步，是漂移高发区。**
> 未进 EVENT_TYPE_MAP 的 chunk → 自动 legacy 透传；前端 `normalizeStreamEvent` 对非 session_ready/task_output_polling 的 legacy type，**必须**在 LEGACY_EVENT_TYPE_MAP 有映射否则被丢弃（return null）。

---

## 3. Registry：通道 B（后台 interaction SSE）

| SSE event | record.kind | producer（store.py / agent） | consumer（前端） | 关键字段 | 带状态转移? |
|---|---|---|---|---|---|
| `background_task.completed` | `background_task` | push_background_task ← _emit_background_completion | BackgroundTaskService.onCompleted → useBackgroundTask（注入气泡 + onWorkflowCompleted） | taskId/status/content/windowId/sessionId | ⚠ **是**（workflow→completed/failed） |
| `background_task.progress` | `workflow_progress` | push_background_progress ← _push_background_progress | onProgress → onWorkflowProgress | taskId/sdkSessionId/usage/lastToolName | 是（首次绑 taskId/sdkSessionId、保持 running） |
| `background_task.progress` | `workflow_phases` | _maybe_emit_workflow_phases | onPhases → onWorkflowPhases | taskId⚠/sdkSessionId/phases | ⚠ TODO 核实是否带 taskId（影响 transcript 无 taskId 窗口） |

---

## 4. 状态转移权威表：workflow 生命周期（两起事故的核心区）

workflow 卡片状态 `running / completed / failed`（`useWorkflowProgress.ts`）目前由**多个来源**驱动，是 bug 高发点。
**目标权威序（应收敛到 SDK Task* 生命周期为唯一权威）**：

| 转移 | 当前触发源 | 文件 | 风险 |
|---|---|---|---|
| → running | `startWorkflow`（前台 Workflow tool.started） | useChatStream.ts:834（**不传 sdkSessionId**⚠） | gap：sdkSessionId 未绑期间 loadTranscript 不跑 |
| running（绑 id） | onWorkflowProgress / onWorkflowPhases | useBackgroundTask | sdkSessionId/taskId 绑定时机决定 transcript 查询能否带 taskId |
| → completed/failed（权威） | `background_task.completed` → onWorkflowCompleted | useBackgroundTask:51 | 正路。SSE fire-and-forget 断连即丢 → 才有下面两个 fallback |
| → completed/failed（fallback 1：前台内联收口） | sendMessage finally `!isPollingBackground` | useChatStream.ts:1435 | **事故①**：detach 没发 isPollingBackground 信号 → 误收口（已修：workflow_detached） |
| → completed/failed（fallback 2：transcript 轮询回填） | loadTranscript `!data.live && status==completed` | useWorkflowProgress.ts:390 | **事故②/latent**：无 taskId 读到别的 run 完成文件 → 误收口（已修：PickRunJson 无 taskId 不认完成态） |

> `onWorkflowCompleted` 自带 `status!=='running'` 幂等守卫 → 一旦误置 completed 即**粘滞不可恢复**（`onWorkflowProgress:250` 已完成不复活）。这放大了任一 fallback 误触发的后果。
>
> **设计债**：3 个来源抢着 flip 同一 status，2 个 fallback 靠"推断"。规范方向（见 §6 ①）= 收敛到单一权威 SDK Task* 生命周期，fallback 降级为身份门控、绝不在缺身份时翻终态。

---

## 5. 迁移纪律（Checklist：改任何信号前过一遍）

1. **登记**：先在本表增/改/删该信号行（type、producer、consumer、字段、语义、是否带状态转移）。
2. **清点 consumer**：grep 两端全量引用（chunk type 字符串、EVENT_TYPE_MAP / LEGACY_EVENT_TYPE_MAP、SSE event 名、record.kind）。
3. **producer 迁移时**：旧信号的每个 consumer，要么新路径继续产出等价信号，要么同步迁移 consumer。**不得让旧 consumer 悬空依赖一个新路径不再产生的信号**（事故①根因）。
4. **状态转移信号**：确认「缺该信号时，consumer 落入的默认分支是否安全」。终态（completed/failed）默认必须落在**可恢复**一侧（running/unknown）。
5. **跨语言映射**：Agent `EVENT_TYPE_MAP` 与前端 `LEGACY_EVENT_TYPE_MAP` 两份手工同步，改一处必查另一处。
6. **身份作用域**：凡按 run/task/session 取数据，必须按显式身份（taskId/sdkSessionId/runId）作用域，**禁止按 mtime / "最新" 猜**（latent bug 根因）。

---

## 6. 已知不变量与改进方向

**不变量（勿破）**：
- 通道 A 与通道 B 严格区分；workflow 完成汇报走 B（background_task.completed），前台增量走 A。
- `_emit_background_completion` 落 history + SSE 一体（push_background_task），故「Task 置 completed 却 Chat 无气泡」⇒ 一定不是 B 路径，往 A 的内联收口 / transcript 轮询查（事故排查捷径）。
- SDK 一个 response 只有一个 ResultMessage（参 R4-3）。

**改进方向（按性价比，详见事故复盘）**：
1. ⭐ 收敛 workflow status 到单一权威（SDK Task* 生命周期）；2 个 fallback 降级为身份门控、永不缺身份翻终态。
2. 前端事件 type 做成 TS union + `default: assertNever()` 穷尽校验 → 少 case 直接 vue-tsc 失败（抓「consumer 漏 case」，抓不到「producer 停发」，需配本表 §5）。
3. 终态 flip 打日志带触发源（B / 内联收口 / transcript 轮询），过早完成一看即定位。

---

## 7. 关联

- 事故复盘记忆：`~/.claude/projects/.../memory/agent-web-stream-contract-drift.md`
- 相关代码：`main_agent.py`（producer）、`main_stream.py`（映射）、`store.py`（通道 B）、`http_server.py`（host/SSE）、`useChatStream.ts` / `useWorkflowProgress.ts` / `useBackgroundTask.ts` / `BackgroundTaskService.ts`（consumer）。
- 架构总览：`docs/Architecture.md`、`docs/Arch_MCP_Tools.md`。
