# BIMCanvas 多 Agent 框架适配需求文档

> BIMCanvas 的目标不是分别做一套 Claude 版 Agent 和一套 OpenAI 版 Agent，而是把 `BIMCanvas.Agent` 建成一个可插拔 Runtime 的 Agent Host，Claude 与 OpenAI 只是挂接在这个 Host 上的两套 Runtime Adapter。

## 1. 文档目的与背景

本文是一份面向 BIMCanvas Agent 端后续改造的**方向与约束文档**，用来统一"支持多 Agent 框架"的目标态、边界、设计原则与验收方式。

之所以需要这份文档，是因为后续随着 OpenAI Runtime 持续推进，如果没有一份上位约束文档，Claude 与 OpenAI 两条线很容易在实现过程中逐步分叉，最终演化为两套平行的提示词体系、配置体系和工具接入体系。那样虽然短期可以分别跑通，但长期会带来维护成本失控、行为基线漂移、Host 外部契约失稳等问题。

本文与现有文档的关系如下：

- [docs/Agent_API_Contract.md](/E:/工作文档/开发类/MyCode/BIMCanvas/docs/Agent_API_Contract.md) 负责定义 BIMCanvas Host 的对外契约（字段、端点、事件枚举）。
- [reviews/BIMCanvasRuntimeContract_v0.1_Review.md](/E:/工作文档/开发类/MyCode/BIMCanvas/reviews/BIMCanvasRuntimeContract_v0.1_Review.md) 负责定义 Runtime 契约的字段级规格与行为基线。
- [reviews/ClaudeRuntimeRefactor_Plan.md](/E:/工作文档/开发类/MyCode/BIMCanvas/reviews/ClaudeRuntimeRefactor_Plan.md) 与 [plans/OpenAIRuntimeAdapter_Plan.md](/E:/工作文档/开发类/MyCode/BIMCanvas/plans/OpenAIRuntimeAdapter_Plan.md) 负责描述分 Runtime 的实施路径。
- **本文**负责定义多 Runtime 改造的总方向、总原则、总边界，作为后续各 Runtime 实施方案的共同上位约束。

**本文不负责：**

- 定义字段级 API shape 或事件序列门禁
- 替代 `Agent_API_Contract.md` 的契约定义职责
- 替代 Claude / OpenAI 各自实施计划的实施细节
- 作为阶段性 gate 或 blocker 的执行依据

本文吸收以下上游结论作为前提：

- [reviews/AgentFrameworkPluggability_Review.md](/E:/工作文档/开发类/MyCode/BIMCanvas/reviews/AgentFrameworkPluggability_Review.md) 第 4 章共识总结。
- [reviews/BIMCanvasRuntimeContract_v0.1_Review.md](/E:/工作文档/开发类/MyCode/BIMCanvas/reviews/BIMCanvasRuntimeContract_v0.1_Review.md) 第 4 章共识总结。
- [plans/OpenAIRuntimeAdapter_Plan.md](/E:/工作文档/开发类/MyCode/BIMCanvas/plans/OpenAIRuntimeAdapter_Plan.md) 中"2.3 后续适配的三条设计原则"。

---

## 2. 目标态

### 2.1 核心定位

本次改造**不是**"在 Claude 旁边再接一个 OpenAI"，而是把 `BIMCanvas.Agent` 从"某个 SDK 的应用层代码"提升为"BIMCanvas 自有的 Agent Runtime 容器"。

Claude 与 OpenAI 是挂接在这个容器上的两套 Runtime Adapter，地位对等，不存在"参考实现 vs 兼容实现"的不对称。

### 2.2 成功标准

评判改造是否成功，不能只看"某个模型的事件能不能跑起来"，而要看：

> **新增第 N 个 Runtime 的边际成本是否显著下降。**

如果新增一个 Runtime 主要只需要实现适配器、补齐 capability matrix 声明、提供 provider 专属配置，而不需要为它额外维护一套专属协议分支、专属工具体系或专属配置树，那说明改造真正达到了目标。

### 2.3 目标形态

- BIMCanvas Host 向外暴露稳定的三平面 API（MainStream / InteractionChannel / ControlPlane），不因 Runtime 差异而漂移。
- 提示词体系、SubAgent 配置、Skills、MCP 工具、权限配置只有一份来源，两个 Runtime 都读同一套文件，装配方式可以不同，但源文件不分叉。
- 前端、Server、MCP 工具不感知 Runtime 切换；关键链路与降级行为应稳定、可预期。

---

## 3. 三方概述

### 3.1 BIMCanvas Host

这里的 BIMCanvas Host 指 `BIMCanvas.Agent` 这一层对外暴露的 Host 能力，而不是某一个具体 provider SDK。本层是协议拥有者，也是多 Runtime 的统一接入面，负责：

- 持有 Host 对外契约，包括 `MainStream`、`InteractionChannel`、`ControlPlane`。
- 持有 session 生命周期、turn 生命周期与 `PendingInteraction` 真相源。
- 持有共享配置资产体系（见第 5 节），包括主控提示词、SubAgent 配置、Skill、MCP 工具定义及宿主配置、工具与权限配置。
- 将底层 Runtime 的原生事件、工具调用、人机中断投影为 BIMCanvas Host 契约要求的稳定外部表现。

### 3.2 Claude Agent SDK

Claude Agent SDK 是当前已经深度使用的基线 Runtime。它在 BIMCanvas 中保留自己的原生语义，包括但不限于 `ClaudeSDKClient`、`Task`、`agents`、`can_use_tool`、`mcp_servers`。

Claude Runtime 的职责不是成为"唯一真理"，而是作为已存在的第一套 Runtime Adapter，为 BIMCanvas Host 提供一条已验证的运行时基线。

### 3.3 OpenAI Agents SDK

OpenAI Agents SDK 是第二套接入中的 Runtime。它也应保留自己的原生语义，包括但不限于 `Agent`、`Runner`、`Session`、`RunState`、`Agent.as_tool()`、`interruption`。

OpenAI Runtime 的目标不是复制 Claude Runtime 的内部形态，而是在保持 OpenAI 原生工作方式的前提下，满足 BIMCanvas Host 的外部契约和浏览器真实链路要求。

---

## 4. 四层角色划分

多 Runtime 架构下，必须清楚区分以下四个角色：

| 角色 | 归属 | 职责 |
|------|------|------|
| **Protocol Owner** | BIMCanvas Agent Host | 持有三平面协议语义与状态真相源（session、PendingInteraction） |
| **Runtime Adapter** | Claude / OpenAI 适配层 | 驱动各自 SDK 原生能力，投影为 Host 契约要求的统一外部行为 |
| **Network Gateway** | BIMCanvas.Server（.NET 进程） | 暴露路由、承接流量、项目配套支撑，不拥有 Runtime 内部状态 |
| **Project Resource Owner** | BIMCanvas 宿主体系 | windowId / projectPath / worktreePath / 附件范围与项目资源绑定 |

`.NET Server` 是网络入口，不是协议拥有者。`/agent` 路由由 `.NET Server` 暴露，并不意味着 `.NET Server` 拥有 RuntimeContract 状态；Runtime 会话状态与 `PendingInteraction` 真相源仍归 Agent Host。

---

## 5. 共享配置资产体系

**这是多 Runtime 架构中最核心的约束，也最容易被违反。**

### 5.1 共享资产（单一来源）

以下资产属于 BIMCanvas Host 级资产，不属于任何 Runtime 私有：

| 资产类型 | 说明 |
|---------|------|
| **主控提示词**（`BIMCANVAS.md`） | 全局规则层，所有 Runtime 共享 |
| **SubAgent 配置**（`agents/*.md`） | 业务角色定义，由 Host / 配置加载层读取，所有 Runtime 共享 |
| **Skill 文件**（`skills/*/SKILL.md`） | 工作流知识片段，所有 Runtime 共享 |
| **BIMCanvas MCP 工具定义及宿主配置** | 业务工具能力声明，所有 Runtime 共享 |
| **工具与权限配置**（`config.json`） | permissions.allow/deny，所有 Runtime 共享，适配层取交集 |

### 5.2 允许的差异化

"共享"不等于"内部实现一样"。允许：

- Claude Runtime 通过 Claude 原生插件/加载机制吸收 `skills/*`；`agents/*.md` 由 Host / 配置加载层读取后交由 Claude Runtime 消化。
- OpenAI Runtime 通过 Host 侧运行时装配层对同一份资产做解析与包装，再注入 Agent instructions。
- 某个 Runtime 暂时无法完整消化某类配置时，通过适配层做限制、降级或阶段性禁用。

### 5.3 明确禁止

- 为 Claude 维护一份 `agents/`，再为 OpenAI 维护另一份不同来源的 `agents/`
- 为某一 Runtime 单独复制一套 Skill 文本
- 为某一 Runtime 维护独立的工具权限配置，绕过共享的 `config.json`
- 用两套不同配置源解释同一个 Host 行为

违反以上规则的任何改动，无论短期是否能跑通，都属于配置分叉，必须在合并前修正。

---

## 6. 设计原则

后续所有多 Runtime 适配工作，统一遵循以下四条设计原则。

### 6.1 保留各自 SDK 的原生语义

目标是能力对齐，不是内部实现同构。

- Claude Runtime 保留 `ClaudeSDKClient / Task / agents / can_use_tool / mcp_servers` 等原生机制
- OpenAI Runtime 保留 `Agent / Runner / Session / RunState / Agent.as_tool() / interruption` 等原生机制
- 不为追求"看起来一样"去给某一方硬造兼容壳；不允许为了追求表面同构而牺牲该 SDK 的稳定性与可验证性

### 6.2 只在外层做 BIMCanvas Host 适配

统一适配层负责把 Runtime 原生能力投影到 BIMCanvas Host 契约，不在适配层内重新定义 Host 外部协议。

- Runtime Adapter 只负责桥接本 SDK 与 Host，不允许在适配器内修改 Host 端点签名或事件格式
- Provider 私有实现细节不暴露给协议消费者（Web、Server、前端组件）

### 6.3 以浏览器真实链路作为唯一验收标准

Shell 脚本、单元测试、协议推演都只是辅助信号。最终是否成立，以浏览器中的真实 UI、SSE、interaction、tool/subtask 呈现为准。

- 脚本结论与浏览器链路冲突时，浏览器真实结果是唯一真理
- 验收重点不只是"事件发出来了"，还包括前端能否正确显示、交互能否恢复、工具链路是否真实闭环

### 6.4 用 Capability Matrix 诚实声明差异，而不是假装等价

不同 Runtime 在 thinking 粒度、subtask 可视化、trace 导出等能力上天然不对等。正确做法是通过 `capability matrix` 显式声明每个 Runtime 支持什么、不支持什么、不支持时前端如何降级，而不是用硬编码补丁掩盖差异。

---

## 7. 跨 Runtime 关键关注点

以下三个方面是跨 Runtime 对齐中最容易出现分叉的横切关注点，每轮适配都必须重点检查。

### 7.1 Pause/Resume 语义

"暂停 - 等待外部输入 - 恢复"这个三段式是整个 InteractionChannel 的核心。Claude 与 OpenAI 的实现路径完全不同（前者通过 `can_use_tool` 回调内同步续跑，后者通过 `RunState` 中断-恢复），但对外的 `PendingInteraction` 闭环行为必须等价。

任何 Runtime 的 AskUserQuestion、工具审批、截图恢复流，都必须投影到统一的 InteractionChannel 语义，不允许绕过 Host 的 interaction 状态机另起一套私有暂停机制。

### 7.2 SubTask Causality

任何子任务、工具调用、嵌套执行，都必须能被稳定追溯到统一的子任务上下文。前端依赖这套因果树渲染任务气泡；如果换了 Runtime 后因果链断裂，用户会直接感知为体验退化。

不同 Runtime 的子代理实现机制可以不同（Claude 的 `Task`、OpenAI 的 `Agent.as_tool()`），但对外投影出的 `subtaskId / parentSubtaskId / origin` 因果结构必须稳定。

### 7.3 状态归属

| 状态类型 | 归属方 |
|---------|-------|
| Session 状态（sessionId、连接句柄） | Runtime Session，对外只暴露 provider-neutral 会话句柄 |
| 未决交互状态（PendingInteraction） | Agent Host，不归 Web、不归 Provider Runtime 内部 |
| 项目/worktree/attachment/window 绑定 | BIMCanvas 现有宿主体系，不下沉到 Runtime |

---

## 8. 共享内容与 Runtime 差异内容

| 分类 | 内容 |
|------|------|
| **共享（单一来源）** | 主控提示词、SubAgent 配置、Skill 文件、MCP 工具定义及宿主配置、工具权限配置、Host API 契约、capability matrix key 集合 |
| **Runtime 专属（各自实现）** | session 绑定方式、subagent 驱动机制、skill 装配方式、MCP 注入方式、pause/resume checkpoint 绑定、事件映射细节、provider fallback 策略 |

---

## 9. 验收方式

字段级 API shape、事件序列门禁、capability matrix 合规明细，以 `docs/Agent_API_Contract.md` 和 `reviews/BIMCanvasRuntimeContract_v0.1_Review.md` 为权威来源。

本文层面的验收，每轮适配检查以下四件事：

1. **配置来源**：提示词、SubAgent 配置、Skill、MCP、工具权限配置是否仍来自同一套目录和同一份资产体系
2. **能力声明**：`capability matrix` 是否如实声明当前 Runtime 的支持边界，差异是否通过降级策略显式处理
3. **浏览器链路**：真实链路是否可见、可理解、可恢复，关键链路与降级行为是否稳定、可预期
4. **Host 契约稳定性**：Host 对外契约是否保持稳定，没有因为 Runtime 差异而漂移

---

## 10. 非目标

- 不追求 Claude / OpenAI 内部语义完全同构
- 不为某一 Runtime 新建一套专属 prompt / skill / agent 配置目录
- 首版不支持同一 session 内热切换 Runtime
- 首版不要求同实例混跑多个 Runtime
- 不允许因为某一 Runtime 当前能力不足，就反向破坏 Host 契约或共享配置源；应优先通过 capability matrix、阶段性禁用、适配层限流处理

---

## 11. 一句话总结

> 先把 BIMCanvas 自己的运行时语义定义清楚，保持共享配置资产单一来源，再让 Claude/OpenAI 作为对等的适配实现去填充它。成功的标志不是"OpenAI 跑起来了"，而是"新增第三个 Runtime 只需要填空，不需要动手术"。
