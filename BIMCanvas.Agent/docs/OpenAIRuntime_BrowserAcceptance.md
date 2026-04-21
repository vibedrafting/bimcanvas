# OpenAI Runtime Browser Acceptance

> 适用范围：任何修改 OpenAI Runtime `ControlPlane / MainStream / InteractionChannel` 的 PR。
>
> 验收原则：浏览器真实链路是唯一验收，不能只看单元测试或日志。

## 记录要求

每条验收项都要补齐以下信息：

- 前置配置
- 操作步骤
- 期望事件序列
- 期望前端 UI
- 实际结果
- 结果截图/录屏路径

建议记录格式：

```markdown
## X. 标题

- 前置配置：
- 操作步骤：
- 期望事件序列：
- 期望前端 UI：
- 实际结果：通过 / 失败
- 截图/录屏：
- 备注：
```

---

## 1. Session 建立

- 前置配置：`runtimeProvider="openai-agents"`，可正常连接的 OpenAI endpoint，浏览器打开 AI Command Center。
- 操作步骤：新开窗口，发送首条普通消息。
- 期望事件序列：`session_ready` → `text.delta` → `text.completed` → `turn.completed`
- 期望前端 UI：窗口进入可用状态；AI 文本逐步出现；本轮正常结束。
- 实际结果：
- 截图/录屏：

## 2. AskUserQuestion pause/resume

- 前置配置：准备一条会触发 `AskUserQuestion` 的消息。
- 操作步骤：发送消息；等待问题气泡出现；点击一个选项提交。
- 期望事件序列：`tool.started(AskUserQuestion)` → question interaction 出现 → 用户 submit → SSE 续跑 → `turn.completed`
- 期望前端 UI：问题气泡出现；提交后原轮继续输出并正常结束。
- 实际结果：
- 截图/录屏：

## 3. 取消 pause

- 前置配置：同上。
- 操作步骤：触发 `AskUserQuestion` 后，不提交答案，直接取消。
- 期望事件序列：interaction `cancelled`；session 状态从 `paused` 回到 `idle`
- 期望前端 UI：问题气泡进入取消态；随后允许发送新消息。
- 实际结果：
- 截图/录屏：

## 4. 工具调用可视化

- 前置配置：准备会触发 `Read / Write / Glob / Bash` 之一的消息。
- 操作步骤：发送消息并观察气泡。
- 期望事件序列：`tool.started` → `tool.output` → `tool.completed(success=true)`
- 期望前端 UI：工具卡片出现；输出可见；完成态正确。
- 实际结果：
- 截图/录屏：

## 5. 工具失败可视化

- 前置配置：准备一个必定失败的 `Bash` 命令。
- 操作步骤：发送消息，让 Agent 调用失败命令。
- 期望事件序列：`tool.completed(success=false,error=...)` + `turn.failed(stopReason=tool_error)`
- 期望前端 UI：失败工具卡片可见；轮次结束为失败态。
- 实际结果：
- 截图/录屏：

## 6. 子任务降级

- 前置配置：`/api/config` 中 `subtask_causality.frontendFallback = "hide-subtask-activity-panel"`。
- 操作步骤：触发 `delegate_query_task`、`delegate_edit_task` 或 `layout-agent`。
- 期望事件序列：允许后台真实运行；不要求前端完整展示 subtask 因果链。
- 期望前端 UI：`TaskSummaryWidget` 不出现。
- 实际结果：
- 截图/录屏：

## 7. 项目切换

- 前置配置：旧项目中先制造一个 unresolved interaction 或活跃 session。
- 操作步骤：切换到另一个项目，再发送新消息。
- 期望事件序列：旧 session 下未决 interaction 进入 `cancelled` 或 `expired`；新 session 重新 `session_ready`
- 期望前端 UI：旧窗口不再继续接收旧 session SSE；新项目会话正常工作。
- 实际结果：
- 截图/录屏：

## 8. 第三方 provider 错误冒泡

- 前置配置：配置一个非官方 OpenAI endpoint，并准备一个高概率失败的嵌套子任务。
- 操作步骤：触发 helper sub-agent 或 `layout-agent`。
- 期望事件序列：错误最终表现为 `turn.failed`，或至少以 `subtask.completed(error)` / 可见错误文本出现在前端
- 期望前端 UI：用户能看到失败，而不是静默结束或无响应。
- 实际结果：
- 截图/录屏：

---

## 合并门槛

- 上述 8 条未全部完成前，OpenAI Runtime 相关 PR 不得合并。
- 如果某条因环境问题无法执行，必须记录阻塞原因、环境信息和替代验证证据，不能留空。
