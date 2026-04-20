# OpenAI Agents SDK 适配说明

> 更新时间：2026-04-19  
> 适用范围：BIMCanvas `openai-agents` Runtime  
> 对应实现：`BIMCanvas.Agent/src/agent/openai_agent.py`、`BIMCanvas.Agent/src/runtime/openai_stream.py`

---

## 0. v0.1 收口声明（2026-04-20 生效）

**本文档的方向已收口。** 后续章节中关于"as_tool 语义站稳 / 继续深化 nested child / 把 responses 作为默认主路"等叙述是 v0.1 之前的阶段性设计，**不再作为当前实施方向**。如果上下文相互冲突，以本节为准。

**收口后的 OpenAI Runtime**（面向第三方 OpenAI-compatible provider 友好的轻量 Runtime）：

- **主路径**：`chat_completions` + streaming + root agent + 普通本地 tools（这是 `config.json > openaiApi` 的新默认值）。
- **不承诺**：`layout-agent` 与任何 configured subagent 的稳定性。chat_completions 主路下 `layout-agent` 不注册（`_resolve_configured_agent_tool_specs` 入口直接将其加入 `blocked_specs`），显式请求时走现有"blocked + honest unavailable reason"路径，不会用 helper worker 冒充。
- **降级声明**：`providers.py` 中 OpenAI 的 `subtask_causality` 由 `optional` 降为 `unsupported`，前端按 `hide-subtask-activity-panel` 降级。
- **实验性 opt-in**：`responses` 模式保留，但只允许与 **官方 OpenAI endpoint**（`api.openai.com/v1`）搭配；第三方 endpoint + `responses` 在 `Settings.load()` 阶段直接 `ValueError`，不再静默降级为 `Runner.run()` 非流式缓冲。
- **不做的事**：不改 Host 主契约（`docs/Agent_API_Contract.md` 的 `MainStreamEvent` / `subtask.*` / `/api/interaction` shape 冻结）；不拆共享配置资产（`agents/*.md`、`skills/*/SKILL.md`、MCP 定义保留单一来源）；不新增"Host 接管 child/subtask 执行"机制。

**为什么这样收口**：`reports/OpenAI_Runtime_ThirdParty_LayoutAgent_Compatibility_Report.md` §4.1-4.4 与上游 issues（#864、#601、#1179、#1575、#2257）都指向——`responses + nested child + on_stream + summary 提取`在第三方 provider 下是高风险组合。本轮不压在"让 layout-agent 在 OpenAI Runtime 下稳定跑通"这个目标上，而是把 OpenAI Runtime 收口为一套边界清楚、能诚实兑现的 Runtime。详见 `plans/MultiAgentRuntime_Requirements.md §5.2 / §6.4 / §10` 与 `C:\Users\huhaonan\.claude\plans\bimcanvas-openai-lexical-dream.md`。

---

## 1. 文档目的

这份文档不再是“是否要做 OpenAI Runtime”的前期设计稿，而是改成一份**OpenAI Agents SDK 介入 BIMCanvas Runtime 的实践说明**。

它回答四类问题：

- OpenAI Agents SDK 到底提供了什么原生能力
- BIMCanvas 为了接入它，具体做了哪些适配
- 研究官方可运行示例后，我们得出的结论是什么
- 现阶段已经跑通了什么，接下来还要继续做什么

这份文档的定位，是给后续继续维护 OpenAI Runtime 的工程师看。重点不是“抽象概念大全”，而是**这套 SDK 在 BIMCanvas 里真正怎么落地、哪里顺手、哪里别硬拧、哪里要绕开坑**。

---

## 2. 先说结论

### 2.1 当前总判断

OpenAI Agents SDK 是一套**以 `Agent + Runner + Tool + Session + RunState` 为核心**的运行时框架。

对 BIMCanvas 来说，它最适合拿来做：

- 文本对话
- 图片输入
- 本地 function tools
- 人工审批 / AskUserQuestion 暂停恢复
- 本地或应用侧持久多轮对话

它**不适合被当成 Claude Agent SDK 的“API 平替”直接硬翻译**。  
原因很简单：两边的原生语义不一样。

- Claude 侧很多能力是围绕 `query / can_use_tool / Task` 组织的
- OpenAI 侧很多能力是围绕 `Runner.run(...) / session / RunState / interruptions` 组织的

如果强行追求“代码结构长得一样”，最后会把 OpenAI 侧写得很别扭；  
正确做法是：**公共 Host 契约尽量保持一致，但运行时边界尊重 OpenAI SDK 自己的机制**。

### 2.2 当前阶段的实际结论

截至目前，BIMCanvas 的 OpenAI Runtime 已经走到“阶段一可用”：

- 可文本对话
- 可连续对话
- 可读图片
- `AskUserQuestion` 可暂停 / 提交 / 恢复
- 本地 tools 已接入：`Read / Write / Edit / Glob / Grep / Bash / AskUserQuestion`
- 支持 `responses`
- 支持 `chat_completions`
- 支持自定义第三方 OpenAI 兼容网关

但仍然是**阶段一 Runtime**，不是 Claude Runtime 的完全等价版本。

当前明确还没有做：

- Claude 风格 `Task` 兼容壳
- `Skill / Plugin`
- `mcp__canvas__*`
- Claude / OpenAI 两套工作流完全同构

### 2.3 后续适配的三条设计原则

后续 OpenAI Runtime 的 Agent / Skill / MCP / 权限暂停恢复等扩展，统一遵循下面三条原则：

1. **保留各自 SDK 的原生语义**

- Claude Runtime 保留 `ClaudeSDKClient / Task / agents / can_use_tool / mcp_servers`
- OpenAI Runtime 保留 `Agent / Runner / Session / RunState / Agent.as_tool() / interruption`
- 目标是能力对齐，不是内部实现同构

2. **只在外层做 BIMCanvas Host 适配**

- 统一适配层负责把 provider 原生能力投影到 BIMCanvas Host 契约
- 对外保持 `/api/chat/stream`、`/api/interaction*`、`subtask.*`、`tool.*` 稳定
- 不为追求“看起来像 Claude”去给 OpenAI 硬造 `Task`、`TaskOutput`、`.claude-plugin` 兼容壳

3. **以浏览器真实链路作为唯一验收标准**

- Shell 脚本、isolated unit tests、协议推演都只是辅助信号
- 最终是否成立，以浏览器中的真实 UI、SSE、interaction、tool/subtask 呈现为准
- 如果脚本结论与浏览器链路冲突，以浏览器真实结果为唯一真理

这三条原则适用于后续全部阶段，而不只适用于 SubAgent：

- Agent / SubAgent 语义接入
- Skill 装配
- MCP 工具注入
- 权限中断、暂停、恢复
- 端到端工作流联调与能力收口

---

## 3. OpenAI Agents SDK 的几个关键概念

### 3.1 `Runner.run(...)`

这是 SDK 的主执行入口。  
一次 `Runner.run(...)` 代表一次完整的 agent turn。

它内部可能发生很多事：

- 模型先输出文本
- 调用一个或多个工具
- 暂停等待审批
- 恢复后继续执行
- 最后得到一个最终输出

所以不要把它理解成“发一次模型请求”。  
更准确地说，它是**一轮 agent 工作流执行器**。

### 3.2 `session`

这是 OpenAI Agents SDK 提供的**应用侧 / 本地侧多轮记忆**机制。

最常见的是：

- `SQLiteSession`

它的工作方式很直接：

1. 每次 run 前，从 session 里取历史
2. 把历史和当前输入拼起来喂给模型
3. run 完后，把本轮新产生的 user / assistant / tool items 写回 session

对 BIMCanvas 这种本地桌面应用来说，这个能力非常合适。  
因为我们想要的是：

- 记忆掌握在应用自己手里
- 不依赖 OpenAI 服务端托管会话
- 能和本地 Host `sessionId` 对齐

### 3.3 `RunState`

这是 OpenAI Agents SDK 里非常关键的能力。  
它表示“一次被暂停的运行状态快照”。

典型场景：

1. agent 运行到一个需要审批的工具
2. 本轮执行暂停
3. 你把 `result.to_state()` 存下来
4. 用户批准或补充回答后，再 `Runner.run(agent, state, ...)` 继续

对 BIMCanvas 来说，这一点特别重要。  
因为我们的 `AskUserQuestion` 并不是“当场阻塞等用户输入”，而是：

- Host 先把问题挂到 `/api/interaction`
- 前端稍后提交答案
- Host 再恢复原来的 turn

OpenAI SDK 的 `RunState` 天然适合这个流程。

### 3.4 `responses` 和 `chat_completions`

OpenAI Agents SDK 默认更贴近 Responses 路线，但它也支持基于 `OpenAIChatCompletionsModel` 的 chat-completions 路线。

对 BIMCanvas 来说，两条路都能跑，但适用场景不同：

- `responses`
  更原生，和 OpenAI 官方能力最一致，适合官方 API
- `chat_completions`
  更容易兼容第三方 OpenAI 兼容网关，适合某些只做 `/v1/chat/completions` 的服务

### 3.5 tracing

SDK 默认有 tracing 能力，会尝试把 trace 往 OpenAI tracing 端点送。  
这对官方 OpenAI 环境很自然，但对第三方网关不一定成立。

实践结论是：

- 用官方 OpenAI：可以开 tracing
- 用第三方网关：通常应默认关 tracing

否则常见现象是：

- tracing 握手超时
- tracing 401
- 主业务能跑，但日志里持续出现 tracing 非致命错误

---

## 4. BIMCanvas 为接入 OpenAI Agents SDK 做了哪些适配

## 4.1 新建独立的 `OpenAIAgent`

我们没有把 Claude 的 `MainAgent` 直接改成“双 provider if/else”。

而是新增了独立的 `OpenAIAgent`，让它专门负责：

- OpenAI SDK client 初始化
- model / base_url / tracing 规则
- OpenAI stream event 到 BIMCanvas `StreamChunk` 的翻译
- `AskUserQuestion` 暂停与恢复
- OpenAI 本地 session 绑定

这样做的好处是：

- Claude 路径不被污染
- OpenAI 路径可以按自己的原生机制组织
- Host 对外还是统一契约

### 4.2 OpenAI 阶段一能力面收口

OpenAI Runtime 没有照搬 Claude 的全部能力，而是先收口成一个稳定基础 Runtime。

当前只注册本地 tools：

- `Read`
- `Write`
- `Edit`
- `Glob`
- `Grep`
- `Bash`
- `AskUserQuestion`

当前明确忽略：

- `Task`
- `Skill`
- `.claude-plugin`
- `mcp__canvas__*`

当前已支持：

- 两个 helper sub-agents：`delegate_query_task` / `delegate_edit_task`
- `<BIMCANVAS_HOME>/agents/*.md` 中“纯 prompt + 本地工具”的配置型 agents，经裁剪后投影为原生 `Agent.as_tool()`

当前仍明确不支持：

- 依赖 `Skill`
- 依赖 `mcp__canvas__*`
- 依赖 `AskUserQuestion`
- 依赖二级 agent delegation / Claude `Task`

同时保留配置裁剪逻辑：

- 先读现有 `permissions.allow/deny`
- 再和 OpenAI 阶段一支持工具集取交集
- 对不支持的工具打清晰日志

这保证了 OpenAI Runtime 不是“误打误撞跑起来”，而是**显式 capability profile**。

### 4.3 OpenAI 事件翻译层

我们保留了 BIMCanvas 统一的 `StreamChunk` 协议，但没有硬复用 Claude 的 provider 事件。

当前做法是：

- OpenAI 原始事件先进入 `OpenAIStreamTranslator`
- 再翻译成 Host 可消费的 `StreamChunk`

目前重点处理的事件包括：

- 文本增量
- reasoning 完成
- tool start / tool complete
- `agent_as_tool` 对应的 subtask start / complete

这层翻译的价值是：

- 前端不需要知道 OpenAI SDK 原始事件结构
- Host 可以继续沿用统一 SSE 封装
- 后续 Claude / OpenAI 可以共享同一套控制面协议

### 4.4 `AskUserQuestion` 的暂停 / 恢复

这一块是 OpenAI 适配最核心的部分。

我们没有模拟 Claude 的“协程里挂个 future 等用户回答”，而是改成 OpenAI 原生语义：

- `AskUserQuestion` 注册成 `FunctionTool(needs_approval=True)`
- SDK 在运行到这个工具时产生 interruption
- Host 把 `RunState` 和投影状态存到 `PendingInteractionRuntimeBinding`
- Web 提交答案后，Host 用 `Runner.run(..., state, ...)` 恢复

这带来的好处是：

- 暂停点是 SDK 原生概念
- 恢复逻辑清晰
- 页面刷新后还能通过 Host 的 interaction/history 找回状态

### 4.5 本地 / 应用侧多轮记忆：绑定 SDK `session`

这是这轮适配里最重要的增强之一。

一开始 OpenAI Runtime 只是：

- Host 存 history 给 UI 看
- 但没有把历史对话真正回放给模型

这意味着前端看上去像连续对话，模型实际上不是。

后来改成了：

- Host 的 `sessionId` 绑定 OpenAI SDK `SQLiteSession`
- 每次 `Runner.run(...)` / `Runner.run_streamed(...)` 都传同一个 `session`
- `AskUserQuestion` 恢复续跑时也继续传同一个 `session`

当前落地方式：

- session 存储位置：`<BIMCANVAS_HOME>/.runtime/openai_agent_sessions.sqlite3`
- 生命周期跟随 BIMCanvas Host session
- 清历史 / 项目切换 / 窗口关闭时，OpenAI SDK session 一起结束

这一步之后，OpenAI Runtime 才真正变成“本地持久多轮对话”。

### 4.6 自定义网关兼容：`responses` 与 `chat_completions`

为了兼容第三方 OpenAI 兼容网关，我们做了两层适配。

第一层是配置规则：

- `openaiApi = responses | chat_completions`
- `openaiDisableTracing` 可显式控制
- 自定义 `baseUrl` 默认更保守

第二层是运行时兼容：

- 如果是 `responses + 官方 OpenAI`，走正常 streaming 路径
- 如果是 `responses + 第三方网关`，启用 fallback：
  用 `Runner.run()` 拿完整结果，再投影成 BIMCanvas 事件
- 如果第三方网关更适合 `/v1/chat/completions`，则走 `chat_completions`

这样做不是最优雅，但非常务实。  
因为现实里很多所谓 OpenAI 兼容网关：

- `/v1/responses` 能回 200，但行为不完整
- tracing 端点不可用
- continuation 语义并不完全等价

所以 BIMCanvas 不能只按“官方 happy path”设计。

---

## 5. 研究官方可运行示例后，我们得到的结论

这里的结论主要来自本地参考仓库：

- `references/src/openai-agents-python/docs/quickstart.md`
- `references/src/openai-agents-python/docs/running_agents.md`
- `references/src/openai-agents-python/docs/sessions/index.md`
- `references/src/openai-agents-python/examples/memory/*`
- `references/src/openai-agents-python/examples/model_providers/*`

### 5.1 官方明确推荐三种多轮记忆方案

官方文档把多轮记忆分成三类：

1. `result.to_input_list()`
2. `session`
3. `conversation_id` / `previous_response_id`

我们的结论是：

- BIMCanvas 应优先用 `session`

原因：

- 我们要的是应用自己持有会话
- 我们有自己的 Host session / history / interaction 体系
- 我们不想把连续对话的主真相源交给 OpenAI 服务端

所以后来把多轮记忆改成 `SQLiteSession`，这是和官方推荐完全一致的方向。

### 5.2 官方对 HITL 的推荐方式，正是 `RunState + 同一个 session`

官方示例对 human-in-the-loop 的推荐路径很明确：

1. `Runner.run(...)`
2. 如果有 interruption，`result.to_state()`
3. 审批 / 回答后 `Runner.run(agent, state, session=session)`

这个结论非常重要。  
因为它直接说明了一点：

> 恢复续跑时，不应该重新自己拼历史，也不应该换一套会话载体；  
> 正确做法就是继续使用同一个 `state` + 同一个 `session`。

BIMCanvas 的 `AskUserQuestion` 现在正是这么做的。

### 5.3 官方示例明确表明：第三方 provider 走 `chat_completions` 更现实

官方 `custom_example_agent.py` 和 `hello_world_gpt_oss.py` 给出了一个很实际的信号：

- 如果你接的是非官方 provider / 本地模型 / OpenAI 兼容服务
- 最稳的办法往往是显式构造 `OpenAIChatCompletionsModel`
- 同时关闭 tracing

这和 BIMCanvas 这轮踩坑结果完全一致。

实际经验是：

- 官方 OpenAI：优先 `responses`
- 第三方兼容网关：很多时候 `chat_completions` 更稳

### 5.4 官方文档强调：`session` 不能和服务端会话链路混用

官方文档反复强调：

- `session`
- `conversation_id`
- `previous_response_id`

这些不是叠加关系，而是**择一使用**。

这对 BIMCanvas 很关键。  
因为我们已经决定走“本地 / 应用侧持久聊天”，那就不应该再混进：

- `previous_response_id`
- `conversation_id`

否则责任边界会混乱：

- 一部分历史在本地
- 一部分历史在 OpenAI 侧
- 出错时很难判断谁是真相源

### 5.5 官方示例的启发：不要把 SDK 当 HTTP wrapper 看

OpenAI Agents SDK 不只是“帮你调接口”的薄封装。

它真正的价值是这些运行时能力：

- session memory
- run lifecycle
- interruptions / approvals
- stream events
- agent-as-tool / handoff

如果只把它当成“统一调 `/responses` 的客户端”，就浪费了 SDK 的核心价值。  
BIMCanvas 这轮真正吃到红利的地方，也恰恰是 `RunState + session`。

---

## 6. 这轮接入中，我们踩过的坑

### 6.1 不能把 Claude 的思路直接照搬

最开始很容易有一种冲动：

- Claude 有 `Task`
- OpenAI 也找个最像的东西去平替

但这条路很危险。  
因为 OpenAI 的原生结构压根不是这么组织的。

当前最正确的经验是：

- 先接受“不完全同构”
- 先把阶段一能力面做扎实
- 不要一上来追求“两个 runtime 看起来一模一样”

### 6.2 第三方网关的“OpenAI 兼容”常常只兼容一半

这是这次适配里最现实的坑。

看起来很多网关都说自己支持 OpenAI API，但实际可能出现：

- `/v1/responses` 返回 200，但 streaming 或 continuation 语义不完整
- tracing 直接失败
- API key 对聊天接口有效，对 tracing 接口无效

所以工程上不能只看“能否回 200”，要看：

- 连续对话是否成立
- 暂停恢复是否成立
- 图片输入是否成立
- tracing 是否干扰主业务

### 6.3 Host history 和模型 memory 不是一回事

这是很容易误判的点。

Host 里的 `/api/history` 能看到多轮消息，只说明：

- UI 有历史

但不说明：

- 模型真的记住了历史

这次把 OpenAI Runtime 改成 SDK `session` 之后，这个问题才真正被解决。

### 6.4 不要用固定 heuristics 去硬拦工具

我们尝试过用固定的“小聊 / 图片识别关键词”去禁工具，想强行阻止 `Read README.md`。

后来回退了。

原因很明确：

- 这种做法太脆
- 词表很快失控
- 只是在补 prompt / 策略问题，不是从根上解决

这个坑值得记录，因为后面很容易再次走回去。

---

## 7. 当前代码状态

### 7.1 已经落地的部分

- `OpenAIAgent` 独立适配器
- OpenAI phase 1 capability profile
- 本地 function tools
- 图片输入链路
- `AskUserQuestion -> interruption -> RunState resume`
- SDK `SQLiteSession` 本地持久多轮记忆
- `responses` / `chat_completions` 双模式
- 第三方 `responses` fallback 投影
- 模型与配置校验
- OpenAI phase 1 测试集

### 7.2 当前实现取舍

当前 Runtime 的取舍是：

- 优先让基础 Runtime 稳定可用
- 不追求和 Claude Runtime 全量等价
- 遇到 provider 特性差异，优先尊重 OpenAI SDK 的原生语义

这是一种刻意的“先收口，再扩能力”的策略。

---

## 8. 下一步计划

### 8.1 先解决 `Read README.md` 过度调用

这是当前最明显的体验问题。

它不是 session 问题，也不是 SDK 本身的问题，而是：

- 共享 prompt 里写着“执行前读取 README.md”
- OpenAI 模型当前把这条规则执行得很机械

下一步正确方向不是固定 heuristic，而是更工程化地处理：

- 重新审视共享主控 prompt 中“必须先读 README.md”这条规则
- 判断这条规则是否要改成更细粒度的条件式描述
- 评估是否把“项目初始化上下文”从工具调用迁移为 Host 侧注入摘要
- 评估是否引入“工具预算 / 工具目的约束”，但不要走关键词黑名单

### 8.2 继续收紧 OpenAI Runtime 的 prompt / tool economy

重点不是“能不能调用工具”，而是“什么时候该调、什么时候不该调”。

后续应优先考虑：

- 哪些上下文应该由 Host 直接注入
- 哪些上下文应该保留给模型按需读取
- 哪些 prompt 规则对 Claude 合理、对 OpenAI 会过拟合成机械动作

### 8.3 阶段一补完：原生 SubAgent 语义已落地

当前已经先补上 OpenAI 的原生 SubAgent 语义底座：

- Root Agent 挂载两个 helper sub-agents：`delegate_query_task` / `delegate_edit_task`
- 纯 prompt + 本地工具的配置型 agents 也可通过 `Agent.as_tool()` 暴露给 root agent
- 通过 `Agent.as_tool()` 投影 `subtask.started / subtask.completed`
- 子代理内部 `tool.*` 生命周期继续复用 Host 既有 MainStream 契约

当前刻意**没有**做：

- Claude `Task` 兼容壳
- Claude `handoff` 兼容壳

原因很明确：这一步的目标是先把 OpenAI 原生 `Agent.as_tool()` 语义站稳，  
而不是为了追 Claude 同构，提前把所有工作流能力揉成一团。

### 8.4 阶段二进展：`layout-agent` 已定向启用

在保持三条设计原则不变的前提下，阶段二已经先落一个浏览器可验收的最小闭环：

- 仅对 `layout-agent` 开放 Skill / MCP 适配
- `layout-agent` 继续使用原生 `Agent.as_tool()`，不引入 Claude `Task`
- `layout-agent` 现在受共享 `permissions.allow/deny` 真实约束，不再为了浏览器 happy path 绕过 `allow`
- `generate-planning` / `generate-placement` 不再伪装成 `Skill` 工具，而是在 OpenAI child agent 构建时按原文装配进运行时 instructions
- `mcp__canvas__*` 不走 Claude `mcp_servers` 兼容壳，而是改为 OpenAI 原生 function tools wrapper
- 当前只开放单区 generate happy path 需要的最小 MCP 子集：
  - `mcp__canvas__request_background_screenshot`
  - `mcp__canvas__get_zone_boundaries`
  - `mcp__canvas__save_semantic_plan`
  - `mcp__canvas__load_semantic_plan`
  - `mcp__canvas__validate_layout`
  - `mcp__canvas__load_reference_analysis`
- 浏览器主验收入口固定为：用户在消息中显式要求主控调用 `layout-agent`，先验证单区 generate，而不把自然路由一起塞进同一轮
- 若用户显式点名 `layout-agent`，但共享权限或当前阶段能力不足，root 会诚实返回不可用原因，不再退回 helper worker 冒充
- OpenAI 子任务投影已改为“诚实完成”：空摘要、未闭合 tool call、或 `layout-agent` 缺少落地动作/校验时，都按失败处理
- 现有用户机器上的 `<BIMCANVAS_HOME>/config.json` 不自动迁移；浏览器要验收 `layout-agent`，必须先手动同步共享权限基线

这里的关键不是“OpenAI 也有了 Skill / MCP”，而是：

- 保留 OpenAI SDK 原生 agent-tool delegation 语义
- Skill 退回为 Host 侧运行时装配层
- MCP 退回为 Host 侧原生工具注入层
- 浏览器子任务气泡、工具链事件、项目侧输出文件，继续复用 BIMCanvas Host 既有外部契约

### 8.5 后续仍未完成的部分

阶段二这次只解决“浏览器里能看到并验证 `layout-agent` 单区闭环”这一件事。  
后面还没做、也不应该在这轮混做的内容包括：

- 其它依赖 `Skill / MCP` 的配置型 agents 接入
- `.claude-plugin` 等 Claude 专属装配机制在 OpenAI 下的等价 Host 适配
- `permission_pause_resume`
- 更完整的 MCP 权限模型
- 多区 generate / 更自然的 root 路由

这些后续能力都应继续遵循第 2.3 节的三条原则：

- 保留 OpenAI SDK 原生能力与语义
- 只在 BIMCanvas Host 外层做适配
- 始终以浏览器真实链路作为唯一验收标准

### 8.6 长对话治理

后面如果 OpenAI Runtime 长对话越来越多，还需要补这一层：

- session 历史裁剪
- session compaction
- 更精细的 context merge 策略

官方 SDK 已经给了方向：

- `SessionSettings(limit=...)`
- `session_input_callback`
- `OpenAIResponsesCompactionSession`

但 BIMCanvas 当前还没进入必须做这一步的阶段。

---

## 9. 我们对 OpenAI Agents SDK 的整体评价

### 9.1 它的优点

- `session` 很适合本地应用
- `RunState` 很适合暂停 / 恢复
- `Runner` 的单 turn 语义很清晰
- 官方文档和 runnable examples 比较完整
- 支持官方 OpenAI，也给了第三方 provider 的接法

### 9.2 它的局限

- 不应该指望它和 Claude Agent SDK 一一对应
- 第三方兼容网关的现实质量参差不齐
- 如果直接照搬共享 prompt，模型行为风格差异会暴露得很明显
- 某些高级工作流能力并不是“天然现成”，仍然需要 Host 层自己消化

### 9.3 对 BIMCanvas 的最终意义

OpenAI Agents SDK 适合成为 BIMCanvas 的第二套 Runtime 基座，但前提是我们接受下面这件事：

> 我们要的是“统一外部契约”，不是“内部 SDK 长得一样”。

只要这件事想清楚，OpenAI Runtime 就不该被当成 Claude Runtime 的拷贝，而应该被当成：

- 共享 Host 契约
- 独立 provider 语义
- 分阶段扩展能力

这样做，后面的路才走得稳。

---

## 10. 一句话总结

这轮接入最大的收获不是“OpenAI Runtime 跑起来了”，而是我们已经摸清楚：

- OpenAI Agents SDK 真正值得用的能力是什么
- BIMCanvas 应该在哪一层尊重它的原生机制
- 哪些地方不能硬抄 Claude
- 当前应该先把什么做稳，后面再扩什么

当前最重要的原则可以浓缩成一句话：

> 阶段一先把 OpenAI Agents SDK 当成一个稳定的基础 Runtime，而不是提前把它逼成 Claude Runtime 的镜像。

如果把后续所有阶段再压缩成一句话，那就是：

> 保留各自 SDK 的原生语义，只在外层做 BIMCanvas Host 适配，并且始终以浏览器真实链路作为唯一验收标准。
