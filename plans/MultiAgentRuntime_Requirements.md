# BIMCanvas 多 Agent 框架适配需求文档

> BIMCanvas 的目标不是分别做一套 Claude 版 Agent 和一套 OpenAI 版 Agent，而是把 `BIMCanvas.Agent` 建成一个可插拔 Runtime 的 Agent Host，Claude 与 OpenAI 只是挂接在这个 Host 上的两套 Runtime Adapter。

## 1. 文档目的与背景

本文是一份面向 BIMCanvas Agent 端后续改造的需求与方向文档，用来统一“支持多 Agent 框架”的目标态、边界、设计原则与验收方式。

之所以需要这份文档，是因为后续随着 OpenAI Runtime 持续推进，如果没有一份上位约束文档，Claude 与 OpenAI 两条线很容易在实现过程中逐步分叉，最终演化为两套平行的提示词体系、配置体系和工具接入体系。那样虽然短期可以分别跑通，但长期会带来维护成本失控、行为基线漂移、Host 外部契约失稳等问题。

本文与现有文档的关系如下：

- [docs/Agent_API_Contract.md](/E:/工作文档/开发类/MyCode/BIMCanvas/docs/Agent_API_Contract.md) 负责定义 BIMCanvas Host 的对外契约。
- [reviews/ClaudeRuntimeRefactor_Plan.md](/E:/工作文档/开发类/MyCode/BIMCanvas/reviews/ClaudeRuntimeRefactor_Plan.md) 与 [plans/OpenAIRuntimeAdapter_Plan.md](/E:/工作文档/开发类/MyCode/BIMCanvas/plans/OpenAIRuntimeAdapter_Plan.md) 负责描述分 Runtime 的实施路径。
- 本文负责定义多 Runtime 改造的总方向、总原则、总边界，作为后续各 Runtime 实施方案的共同上位约束。

本文吸收以下上游结论作为前提：

- [reviews/AgentFrameworkPluggability_Review.md](/E:/工作文档/开发类/MyCode/BIMCanvas/reviews/AgentFrameworkPluggability_Review.md) 第 4 章共识总结。
- [reviews/BIMCanvasRuntimeContract_v0.1_Review.md](/E:/工作文档/开发类/MyCode/BIMCanvas/reviews/BIMCanvasRuntimeContract_v0.1_Review.md) 第 4 章共识总结。
- [reviews/ClaudeRuntimeRefactor_Plan.md](/E:/工作文档/开发类/MyCode/BIMCanvas/reviews/ClaudeRuntimeRefactor_Plan.md)。
- [docs/Agent_API_Contract.md](/E:/工作文档/开发类/MyCode/BIMCanvas/docs/Agent_API_Contract.md)。
- [plans/OpenAIRuntimeAdapter_Plan.md](/E:/工作文档/开发类/MyCode/BIMCanvas/plans/OpenAIRuntimeAdapter_Plan.md) 中“2.3 后续适配的三条设计原则”。

## 2. 三方概述

### 2.1 BIMCanvas Host

这里的 BIMCanvas Host 指 `BIMCanvas.Agent` 这一层对外暴露的 Host 能力，而不是某一个具体 provider SDK。本层是协议拥有者，也是多 Runtime 的统一接入面，负责：

- 持有 Host 对外契约，包括 `MainStream`、`InteractionChannel`、`ControlPlane`。
- 持有 session 生命周期、turn 生命周期与 `PendingInteraction` 真相源。
- 持有共享配置资产体系，包括主控提示词、SubAgent 配置、Skill、MCP、工具与权限配置。
- 将底层 Runtime 的原生事件、工具调用、人机中断投影为 BIMCanvas Host 契约要求的稳定外部表现。

### 2.2 Claude Agent SDK

Claude Agent SDK 是当前已经深度使用的基线 Runtime。它在 BIMCanvas 中保留自己的原生语义，包括但不限于：

- `ClaudeSDKClient`
- `Task`
- `agents`
- `can_use_tool`
- `mcp_servers`

Claude Runtime 的职责不是成为“唯一真理”，而是作为已存在的第一套 Runtime Adapter，为 BIMCanvas Host 提供一条已验证的运行时基线。

### 2.3 OpenAI Agents SDK

OpenAI Agents SDK 是第二套接入中的 Runtime。它也应保留自己的原生语义，包括但不限于：

- `Agent`
- `Runner`
- `Session`
- `RunState`
- `Agent.as_tool()`
- `interruption`

OpenAI Runtime 的目标不是复制 Claude Runtime 的内部形态，而是在保持 OpenAI 原生工作方式的前提下，满足 BIMCanvas Host 的外部契约和浏览器真实链路要求。

## 3. 三者关系与分层

多 Runtime 架构下，必须清楚区分以下四个角色：

- `Protocol Owner = BIMCanvas Agent Host`
- `Runtime Adapter = Claude / OpenAI 适配层`
- `Network Gateway = BIMCanvas.Server`
- `Project Resource Owner = BIMCanvas 宿主体系`

它们之间的关系应当保持为：

- BIMCanvas Host 负责协议语义、状态归属与外部稳定性。
- Claude/OpenAI Runtime Adapter 负责解释并驱动各自 SDK 的原生能力，再把结果投影为 Host 契约要求的统一外部行为。
- `BIMCanvas.Server` 负责网络暴露、路由转发和项目配套支撑，但不拥有 Runtime 内部状态，不拥有 `PendingInteraction` 真相源。
- BIMCanvas 宿主体系继续负责 `windowId`、`projectPath`、`worktreePath`、附件范围与项目资源绑定。

这意味着：Host 对外契约必须稳定，但 Runtime 内部机制可以不同。只要 Claude 与 OpenAI 最终都被 Host 正确投影到同一外部契约上，它们内部如何实现 session、subagent、tool lifecycle、pause/resume，并不要求相互同构。

## 4. 适配目标

本次多 Agent 框架适配的目标，不是让 Claude 与 OpenAI 在内部实现上完全一样，而是让它们在 BIMCanvas Host 外部契约下实现稳定、可验证、可维护的能力对齐。

核心目标如下：

- `BIMCANVAS.md`
- `agents/*.md`
- `skills/*/SKILL.md`
- `config.json`
- MCP / Tool / Permission 相关配置

以上资产应继续只有一份，并继续放在同一套目录结构里。Claude Runtime 和 OpenAI Runtime 都去读取、解释、消化同样的一套文件，而不是各自维护一套平行配置树。

换句话说：

- BIMCanvas 的配置文件系统是 Host 级资产，不属于某一个 Runtime 私有。
- Runtime 之间允许解释方式不同，但不允许把同一类配置拆成两套独立来源。
- 任何新增 Runtime 适配都默认先复用现有共享配置体系，而不是从一开始复制出一套 `openai/`、`claude/` 并行目录。

与此同时，BIMCanvas Host 的对外契约必须保持稳定，至少包括：

- `/api/chat/stream`
- `/api/interaction*`
- `/api/config`
- `subtask.* / tool.* / text.*`
- `capability matrix`

后续每个 Runtime 的适配成败，不能只看“能不能调用模型”或“能不能触发某个工具”，而要看：在接入后，Web、Server、MCP、提示词资产和交互链路是否仍然稳定。

## 5. 设计原则

后续所有多 Runtime 适配工作，统一遵循以下四条设计原则。

### 5.1 共享单一配置文件系统

配置资产只有一份，Runtime 只负责解释，不负责复制配置。

这意味着：

- 主控提示词、SubAgent 配置、Skill、MCP、Tool、Permission 配置都应继续共用同一套目录结构。
- Claude 和 OpenAI 都应读取同一套文件，而不是各自维护一套“长得差不多”的平行配置。
- 若某个 Runtime 暂时无法完整消化某类配置，应通过适配层做限制、降级或阶段性禁用，而不是复制配置源来回避问题。

### 5.2 保留各自 SDK 的原生语义

目标是能力对齐，不是把 OpenAI 硬拧成 Claude，也不是把 Claude 改写成 OpenAI。

具体要求：

- Claude Runtime 保留 `Task / agents / can_use_tool / mcp_servers` 等原生机制。
- OpenAI Runtime 保留 `Agent / Runner / Session / RunState / Agent.as_tool() / interruption` 等原生机制。
- 不为了“看起来一致”去给某一方硬造兼容壳，尤其不允许为了追求表面同构而牺牲该 SDK 的稳定性与可验证性。

### 5.3 只在外层做 BIMCanvas Host 适配

统一适配层负责把 Runtime 原生能力投影到 BIMCanvas Host 契约，不造伪兼容壳。

这意味着：

- Host 层负责对外保持 `/api/chat/stream`、`/api/interaction*`、`subtask.*`、`tool.*` 等稳定。
- Runtime Adapter 只负责桥接本 SDK 与 Host，不负责重新定义一套新的 Host 外部协议。
- 不为追求“像另一套 SDK”而硬造 `Task`、`.claude-plugin`、伪 `mcp_servers`、伪 `handoff` 等表面兼容结构。

### 5.4 以浏览器真实链路作为唯一验收标准

Shell、单测、脚本、协议推演都只是辅助信号。最终是否成立，以浏览器中的真实 UI、SSE、interaction、tool/subtask 呈现为准。

这意味着：

- 如果自动化测试与浏览器真实链路冲突，以浏览器结果为唯一真理。
- 每一轮 Runtime 适配都必须回到浏览器实际场景中验证，而不是只在 isolated script 中自证成功。
- 验收重点不只是“事件发出来了”，还包括前端能否正确显示、交互能否恢复、工具链路是否真实闭环。

除上述四条原则外，还应坚持一条底线：不假设不同 Runtime 天然等价，必须通过 `RuntimeContract + capability matrix` 显式声明差异、降级策略和当前支持边界。

## 6. 共享内容与 Runtime 差异内容

多 Runtime 方案下，必须同时成立两件事：共享资产要单一，Runtime 差异要可承认。

| 分类 | 内容 |
|------|------|
| 共享内容 | 提示词资产、agent 配置、skill 文件、MCP/工具权限配置、Host API 契约、能力矩阵 key、行为基线 |
| Runtime 专属内容 | session 绑定方式、subagent 实现机制、skill 装配方式、MCP 注入方式、pause/resume checkpoint 绑定、事件映射细节、provider fallback |

这里有两条必须明确的边界：

- “共享”不等于“内部实现一样”。Claude 与 OpenAI 可以用不同方式装配 Skill、注入 MCP、处理 session 和记忆。
- “差异”不等于“允许复制一套独立配置树”。只要某项内容本质上属于 Host 级资产，就应保持单一来源。

在具体适配上，可以允许：

- Claude 通过 Claude 原生机制读取 agent / skill / mcp 配置。
- OpenAI 通过 OpenAI 原生机制对同一份 agent / skill / mcp 配置做运行时装配或包装。

但不允许：

- 为 Claude 维护一份 `agents/`，再为 OpenAI 维护另一份不同来源的 `agents/`
- 为某一 Runtime 单独复制一套 Skill 文本或 Tool 权限目录
- 用两套不同配置源解释同一个 Host 行为

## 7. 注意事项与非目标

以下内容属于本方向文档明确排除的非目标：

- 不追求 Claude / OpenAI 内部语义完全同构。
- 不为某一 Runtime 新造一套专属 prompt / skill / agent 配置目录。
- 首版不支持同一 session 内热切换 Runtime。
- 首版不要求同实例混跑多个 Runtime。

以下内容是必须长期保持关注的注意事项：

- `subtask causality` 是跨 Runtime 对齐中的关键关注点。任何子任务、工具调用、嵌套执行，都必须能被稳定追溯到统一的子任务上下文。
- `permission / pause / resume` 是另一条高优先级主线。不同 SDK 的暂停恢复形态可能完全不同，但 Host 外部的交互闭环必须稳定。
- 不允许把 `.NET Server` 与 `Agent Host` 的职责混淆。`.NET Server` 可以暴露路由与承接网络流量，但 Runtime 会话状态和 `PendingInteraction` 真相源仍归 Agent Host。
- 不允许因为某一 Runtime 当前能力不足，就反向破坏 Host 契约或共享配置源。应优先通过 capability matrix、阶段性禁用、适配层限流来处理。

## 8. 验收与推进方式

后续多 Runtime 改造，统一按以下顺序推进和验收：

1. 先保证 Host 契约稳定。
2. 保持 Claude 作为基线 Runtime 持续成立。
3. 再按阶段推进 OpenAI 等其他 Runtime 接入。
4. 每个阶段都必须回到浏览器真实链路验收。

每轮适配都应同时检查以下四件事：

- 配置是否仍来自同一套目录和同一份资产体系。
- `capability matrix` 是否如实声明当前 Runtime 的支持边界。
- 浏览器中的真实链路是否可见、可理解、可恢复。
- Host 对外契约是否保持稳定，没有因为 Runtime 差异而漂移。

验收标准应当是：

- 新增 Runtime 后，BIMCanvas Host 的对外行为仍然稳定。
- Web / Server / MCP / 提示词资产没有因为新增 Runtime 而分叉。
- Claude 与 OpenAI 虽然内部实现不同，但都能在 Host 契约下形成稳定、可验证的浏览器真实链路。

本文不新增任何 API 或字段。它的职责只有三点：

- 确认 [docs/Agent_API_Contract.md](/E:/工作文档/开发类/MyCode/BIMCanvas/docs/Agent_API_Contract.md) 是 Host 外部唯一契约来源。
- 确认 `capability matrix`、`subtask causality`、`interaction / pause / resume` 是跨 Runtime 必须显式对齐的核心关注点。
- 确认“共享配置源 + Host 外层适配 + 浏览器验收”是后续所有 Runtime 实施的共同上位原则。
