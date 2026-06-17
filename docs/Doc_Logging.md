# BIMCanvas 日志系统

> 面向开发者:**怎么用日志定位程序 / 插件工作流的问题**。
> 本文是端到端的跨模块视图与排查手册,**不展开各模块日志代码的改造细节**——要改 Server / Web 的日志实现,请到对应模块 README 的「日志系统」小节(见 §7 指针)。

BIMCanvas 有**三套互不相同、互为补充**的日志,分属三个进程 / 运行时。看懂一个问题往往要把三套对齐着看:谁在「居中调度」(Server)、谁在「呈现与感知」(Web)、谁在「干活推理」(Agent SDK)。

---

## 1. 三套日志一张图

| | Server 日志 | Web 前端日志 | Agent SDK transcript |
|---|---|---|---|
| 进程 / 运行时 | BIMCanvas.Server(.NET 8) | 浏览器(Vue 3) | Claude Agent SDK(托管的 Python Agent) |
| 视角 | 服务端**执行与状态**:REST / SignalR / SSE、几何 / Git / 落盘、子进程编排 | 浏览器**意图与感知**:用户操作、SSE 流解析、渲染 | AI **推理与工具轨迹**:thinking、工具入参全文、token、子代理编排 |
| 实时出口 | 终端控制台 | F12 Console + 应用内面板 | 无(只落盘) |
| 持久化 | ✅ `{项目}/logs/session_*.log` | ❌ 仅浏览器内存环形 buffer(关窗即丢) | ✅ `~/.claude/projects/{转义路径}/*.jsonl` |
| SSE 流内容 | **盲区**(`ProxyToAgentAsync` 透明转发,零日志) | **主战场**(记录实际解析到的事件) | 源头(Agent 这边的原始事件) |
| 对齐键 | `windowId` + 时间戳 | `windowId` + 时间戳 + `clientMessageId` | `toolUseId` / `isSidechain` / `parentUuid` |

一句话:**Server 记执行与状态,Web 记意图与感知,SDK 记推理与工具轨迹;流是 Server 的结构性盲区,由 Web 补全;AI 内部的 thinking / 工具入参只在 SDK transcript 有。**

---

## 2. Server 日志

### 2.1 两个出口、同一份内容

Server 日志实时打**控制台**,并由一层 **Console Tee** 把同一字节流(去掉颜色码)镜像到**本地文件**。两者是同一份内容的「易失视图」与「持久档案」,不是冗余。

- 控制台格式化:`BIMCanvas.Server/Logging/ServerConsoleFormatter.cs`
- 本地文件镜像:`BIMCanvas.Server/Logging/ConversationLogger.cs`
- 输出辅助与安装点:`BIMCanvas.Server/Program.cs`

### 2.2 控制台前缀总表(靠前缀区分来源)

控制台是多来源汇流,**前缀是判断「这行谁说的」的唯一依据**:

| 前缀 | 来源 | 颜色 | 说明 |
|------|------|------|------|
| `[Server]` `[Server:WARN]` `[Server:ERR]` `[Server:DBG]` `[Server:TRC]` `[Server:CRIT]` | Server 自身 `ILogger` | 白 / 黄 / 灰系 | 经 `ServerConsoleFormatter` 格式化,按 `LogLevel` 选前缀与颜色 |
| `[Agent]` `[Agent#n]` | Agent(Python)进程 **stdout** | 青(Cyan) | 前缀由 Python 端自己打,Server 只补时间戳转发;这是 **AI 执行轨迹本身**,不是 Server 日志 |
| `[Agent:ERR]` | Agent 进程 **stderr** | 暗青 | Server 转发 |
| `[Web]` | 托管的 Vite dev server 输出 | 绿 | Server 转发(已过滤 Vite 冗余行) |
| `[CCR]` | CCR 网关 **stdout** | 品红(Magenta) | Server 转发,**已过滤 `image_url` / base64 附件**避免刷屏;CCR stderr 不过滤 |

`LogLevel → 前缀` 映射:`Trace→[Server:TRC]`、`Debug→[Server:DBG]`、`Information→[Server]`、`Warning→[Server:WARN]`、`Error→[Server:ERR]`、`Critical→[Server:CRIT]`。

> `[Server]` 与 `[Agent]` 是**两套日志**:前者是 Server 居中调度时自己说的话,后者是 AI 干活时说的话,Server 只是后者的管道。排查 AI 决策问题别在 `[Server]` 里找,要看 `[Agent]` 或直接看 SDK transcript(§4)。

### 2.3 本地文件 `session_*.log`

- **落点**:`{项目路径}/logs/session_{yyyyMMdd_HHmmss}.log`,按「项目 × Server 会话」分文件;切换 / 重开项目即滚动到新文件。
- **内容**:与控制台**一模一样**,只去掉 ANSI 颜色码——含 Server / Agent / Web / CCR 全部来源,**不是只存 AI 对话**。文件名前缀 `session_`(旧版 `chat_` 已废弃)。
- **落盘策略**:每行 `File.AppendAllText` 追加后**立即关闭句柄**,从不持有文件。这样删除 / 移动项目时不会被日志文件占住目录(曾因常开的 `StreamWriter` 报 `IOException: being used by another process`,已修)。
- **生命周期**(`ConversationLogger`):
  - `Install()` — 进程启动最早期装 Tee,**必须早于日志框架初始化**(`Program.cs:87` 装 Tee,`Program.cs:119-120` 才 `ClearProviders()` + `AddServerConsoleFormatter()`)。顺序反了 Server 自身日志不进文件。
  - `Initialize(projectPath)` — 打开 / 切换项目时调,在 `logs/` 下起新文件。
  - `Shutdown()` — 进程退出收尾。
- **静默退化**:项目未打开 / 目录被删时,写入静默忽略,不重建目录、不报错。

### 2.4 日志级别配置

| 环境 | 配置文件 | 默认级别 |
|------|----------|----------|
| Production | `BIMCanvas.Server/appsettings.json` | 全部 `Warning`(静默) |
| Development | `BIMCanvas.Server/appsettings.Development.json` | `Default=Information`,框架(`Microsoft.AspNetCore`)压到 `Warning` |

文件里看到什么 = 控制台显示什么,Tee 不另设过滤。要让某类日志进文件,调这两份配置的级别即可。

### 2.5 已知约束

- **多线程交错**:控制台有多个并发写入者(Server 日志线程 + Agent stdout 泵 + Web 泵 + CCR 泵)。高并发瞬间单行可能交错,文件如实镜像这一交错,未做行级串行化。
- **Agent stdout 物理行**:Server 按物理行(遇 `\n` 才返回)读 Agent stdout,只在行首补时间戳——所以 AI 流式输出的一行不会被内部 flush 拆成大量带重复前缀的日志。

---

## 3. Web 前端日志

### 3.1 单一出口

全前端日志走唯一出口 `BIMCanvas.Web/src/utils/logger.ts`,**禁止散用 `console.*`**(唯一例外是 logger 自身内部的 console sink)。`logger.ts` 是 framework-agnostic 纯 TS 模块,service 单例 / Three 服务 / Pinia 初始化前都能直接 `import`。

### 3.2 五域

| 域 | 颜色 | 记什么 |
|---|---|---|
| `USER` | 紫 | 用户关键操作:发送 / 中止、开关 / 删除项目、移动 / 旋转 / 复制 / 镜像 / 放置 |
| `STREAM` | 蓝 | SSE 流收发 + turn 状态:send 载荷、`turn.completed` / `turn.failed`、解析失败 |
| `RECV` | 绿 | SignalR 推送接收:`ReceiveUpdate` / `GitStatusChanged` / `AgentNotification` / `SceneArtifactUpdated` |
| `RENDER` | 粉 | Three / 渲染:模块库加载失败、场景重建、fitToScreen |
| `SYS` | 灰 | 系统 / 生命周期 / 项目加载 |

> Web 端的颜色是浏览器原生 CSS `%c`(非 ANSI),与 Server 终端的 ANSI 是两套机制,但 `[时间] [Web:域] msg key=val` 的**文本格式**与 Server `[时间] [Server] msg` 视觉对齐,F12 与终端可并排对照。

### 3.3 F12 用法

```ts
import { createLogger } from '@/utils/logger';
const log = createLogger('STREAM');
log.info('turn.completed', { win: 'main', dur: '12s', tokens: 4521 });
// → [23:45:12.341] [Web:STREAM] turn.completed win=main dur=12s tokens=4521
```

- `msg` 用英文短语,变量进 `fields` 对象(**不要字符串拼接**);`fields` 渲染成 `key=val`,带空格的值自动加引号,`Error` 取 `.message`,对象 `JSON.stringify` 且超 200 字截断。
- 关联键:涉及某窗口的日志带 `win=<windowId>`;一次对话用 `clientMessageId` 贯穿 send→turn。

### 3.4 切级别 + 面板

- **切级别**:F12 控制台 `__bimlog.setLevel('debug')`(另含 `__bimlog.level` / `.clear()` / `.buffer`),或 `localStorage.bimlog='debug'`(持久,刷新保留)。默认级别 `DEV→info`、`PROD→warn`;低于当前级别的日志直接丢弃(不进 buffer、不打 console)。
- **应用内面板**:`Ctrl+\`` 开关 `DebugConsole.vue`,支持级别切换、域过滤、复制全部。

### 3.5 易失性(重要)

Web 日志**纯易失**:浏览器内存环形 buffer(上限 500 条,newest-first),**无本地持久化**。要留证据(报 bug / 事后复盘),F12 里 `__bimlog.buffer` 或面板「复制全部」**当场导出**——刷新页面即丢。这是与 Server `session_*.log` 最大的不对称。

---

## 4. Agent SDK transcript 层

这是**三套里唯一没有模块 README 的**,也是排查 AI 工作流问题信息量最大的一层。Claude Agent SDK 把每次会话和每个子代理的完整对话逐行落成 `.jsonl`。

### 4.1 落点与目录命名

根:`C:\Users\{user}\.claude\projects\{转义后的工作目录}\`

转义规则:工作目录绝对路径 → 盘符冒号去掉、`\` 与非字母数字字符替换为 `-`、中文保留。例:

- `C:\Users\huhaonan\Documents\BIMCanvas\Projects\金凤127` → `C--Users-huhaonan-Documents-BIMCanvas-Projects---127`
- 本仓库 `E:\工作文档\...\MyCode\BIMCanvas` → `E-----------MyCode-BIMCanvas`

### 4.2 一次会话的文件布局

```
{转义路径}/
├── {sessionUuid}.jsonl                  # 主对话 transcript(逐行 JSON 事件)
└── {sessionUuid}/                       # 仅当本会话起过子代理 / workflow 才有
    ├── subagents/
    │   ├── agent-{agentId}.jsonl        # 普通 Task 子代理的完整 transcript
    │   ├── agent-{agentId}.meta.json    # {agentType, description, toolUseId}
    │   └── workflows/
    │       └── wf_{runId}/              # 一次 Workflow 编排扇出的全部子代理
    │           ├── agent-{id}.jsonl     # 每个 workflow agent 一份
    │           └── agent-{id}.meta.json # workflow agent 的 meta 可能精简为 {agentType}
    └── workflows/
        └── wf_{runId}.json              # Workflow 运行台账(见 §4.5)
```

### 4.3 主 transcript `{sessionUuid}.jsonl` 的事件

逐行 JSON,每行一个 `type`。常见取值与关键字段:

| `type` | 含义 | 关键字段 |
|--------|------|----------|
| `user` | 用户输入 / 工具结果回填 | `message.content`(可含 `tool_result`)、`uuid`、`parentUuid`、`promptId`、`cwd`、`gitBranch` |
| `assistant` | AI 响应 | `message.content`(可含 `text` / `thinking` / `tool_use`)、`message.model`、`message.usage`(token)、`stop_reason` |
| `queue-operation` / `ai-title` / `attachment` / `task_reminder` | 队列事件 / 会话标题 / 附加元数据 / 后台任务提醒 | 各自轻量 |

- **turn 边界**:靠 `parentUuid` 链——某行 `uuid` 是下一行的 `parentUuid`。
- **工具调用**:发起在 `assistant.message.content[]` 的 `{type:"tool_use", id, name, input}`;结果在后续 `user.message.content[]` 的 `{type:"tool_result", tool_use_id, content, is_error}`。
- **工具失败**:`tool_result.is_error: true`(没有专门的 error 行)。
- **token**:`assistant.message.usage`(`input_tokens` / `output_tokens` / `cache_read_input_tokens` 等)。
- **model**:`assistant.message.model`(可能因 CCR 路由是不同下游模型)。

### 4.4 子代理关联键

- `{sessionUuid}/subagents/agent-{id}.meta.json` 的 **`toolUseId`** = 该子代理在**主 transcript 里那次 Task / Workflow 工具调用**的 `id`。拿它在主 `{sessionUuid}.jsonl` grep,即可回溯「谁、用什么入参、派了这个子代理」。
- 子代理 jsonl 的 schema 与主 transcript 一致,额外有 `isSidechain: true`(标侧链)、`agentId`、`parentUuid`(子代理内部 turn 链)。
- `agentType` 取值:平台级如 `general-purpose` / `Explore`;interior-layout workflow 的如 `placement-agent` / `review-agent` / `judge-agent` / `design-scribe` 等。**完整清单以 plugin 仓库 `agents/` 为准**,本文不固化。

### 4.5 Workflow 运行台账 `{sessionUuid}/workflows/wf_{runId}.json`

一次 `Workflow` 工具调用的完整编排记录,排查工作流问题先看它:

- `runId` / `timestamp` / `taskId`
- `script` — 完整 workflow 脚本源码,含 `meta.name` / `meta.description` / `meta.phases`(阶段标题与说明)
- `workflowProgress` — 每个扇出 agent 的 `state` / `startedAt` / `lastProgressAt` / `tokens` / `toolCalls` / `durationMs`

例:interior-layout 场景① 的台账 `meta.name` 为 `interior-layout-scene1`,`phases` 为七步流(感知→规划推演→多方案→落地→评审→裁决→精修)。

### 4.6 只有这层才有的信息

完整 thinking、工具入参全文、token / cache 命中分布、各子代理 `durationMs` 与并行情况——Server / Web 日志都**没有**。AI「为什么这么决策」「卡在哪个 agent」只能在这层查。

---

## 5. 三套怎么对齐

| 想关联的两端 | 对齐键 |
|---|---|
| Server ↔ Web(同一时刻服务端做了什么 / 浏览器看到了什么) | `windowId` + 时间戳 |
| Web 内一次对话的 send→turn 全过程 | `clientMessageId` |
| 主 transcript ↔ 某子代理 / workflow | `toolUseId`(主 transcript 的 tool_use `id` = 子代理 meta 的 `toolUseId`) |

---

## 6. 排查速查表

> 环境无 `jq` / `python`,下列示例用 `grep`/`sed`(git-bash)或 Windows `findstr`。jsonl 行很长,提取字段而非整行打印。

| 现象 | 先看哪套 | 怎么定位 |
|------|----------|----------|
| 渲染丢失 / 画布空白 | Web(RENDER)→ Server(`[Server]`) | F12 看 `RENDER` 域报错;再到 `session_*.log` 看 Server 落盘 / 推送是否发出 |
| SSE 聊天断流 / 没回复 | Web(STREAM)→ SDK transcript | F12 看 `STREAM` 的 `turn.failed` / 解析失败(Server 对 SSE 是盲区);确认 AI 侧轨迹去 SDK `{sessionUuid}.jsonl` |
| 几何 / 碰撞 / 验证错 | Server(`[Server]`) | `session_*.log` grep `PlacementService` / `validation` / `E0` 错误码 |
| Git / Worktree / 分支锁异常 | Server(`[Server]`) | `session_*.log` grep `Worktree` / `BranchLock` / `git` |
| AI 决策不对 / 放错位置 | SDK transcript | 看 `assistant` 行的 `thinking` 与 `tool_use.input`,Server / Web 都看不到 |
| Workflow 某 agent 失败 / 卡住 | SDK workflow 台账 | 读 `wf_{runId}.json` 的 `workflowProgress` 找 `state != done` 的 agent,再看其 `subagents/workflows/wf_*/agent-{id}.jsonl` 末尾 |
| token 暴涨 / 跑得慢 | SDK | `wf_{runId}.json` 的 `workflowProgress[].tokens` / `durationMs`,或各 `assistant.message.usage` |
| 附件图片相关 | Server(`[CCR]`)+ SDK | `[CCR]` 的 base64 已被过滤;入参全文看 SDK 的 `tool_use.input` |

### 范例一:一条对话「发了没反应」

```bash
# 1) Web 侧:这次 turn 到底失败还是没发出?(F12 或导出的 buffer)
#    看 STREAM 域:有 send 载荷但无 turn.completed → 流中断;连 send 都没有 → UI 没发出

# 2) Server 侧:HTTP 是否到达、是否转发给 Agent
grep -n "agent" "{项目}/logs/session_20260617_xxxxxx.log" | grep -iE "proxy|/agent|error"

# 3) AI 侧:Agent 那边收到没、卡在哪
#    定位本次会话 jsonl,看最后几行是 assistant 卡住还是 tool_result is_error
tail -5 "C--...---127/{sessionUuid}.jsonl"
```

### 范例二:workflow 某子代理报错,定位是哪个、什么入参

```bash
B="C--...---127/{sessionUuid}"
# 1) 台账里找没跑完的 agent
grep -o '"label":"[^"]*","state":"[^"]*"' "$B/workflows/wf_xxx.json"
# 2) 该 agent 的 meta 拿 agentType,jsonl 末尾看错误
cat "$B/subagents/workflows/wf_xxx/agent-{id}.meta.json"
grep -o '"is_error":true[^}]*' "$B/subagents/workflows/wf_xxx/agent-{id}.jsonl"
# 3) 若 meta 有 toolUseId,回主 transcript 看派发它时的入参
grep -o '"toolUseId":"[^"]*"' "$B/subagents/.../agent-{id}.meta.json"   # 取 id
grep "<上一步的 id>" "C--...---127/{sessionUuid}.jsonl"
```

---

## 7. 想改造日志系统?去哪改

本文只讲「用」。要**改日志实现**,进对应模块 README 的「日志系统」小节,那里有红线与标准动作:

| 改什么 | 去哪读 + 关键文件 |
|--------|-------------------|
| Server 控制台格式 / 前缀 / 着色 / 异常过滤 | `BIMCanvas.Server/README.md` §11;`Logging/ServerConsoleFormatter.cs` |
| Server 本地文件镜像 / Tee / 落盘 | `BIMCanvas.Server/README.md` §11;`Logging/ConversationLogger.cs`(注意 `Install()` 须早于日志框架初始化的时序红线) |
| Web 前端 logger / 域 / 门控 / 面板 | `BIMCanvas.Web/README.md` §11;`src/utils/logger.ts`(注意**禁止在 computed/getter 内打日志**——会触发响应式无限循环) |
| Agent SDK transcript | **不可改**——由 Claude Agent SDK 写入,格式随 SDK 版本演进,本文 §4 仅描述当前实测结构 |

---

## 相关文档

| 文档 | 内容 |
|------|------|
| `BIMCanvas.Server/README.md` §11 | Server 日志实现细节(改造入口) |
| `BIMCanvas.Web/README.md` §11 | Web 日志实现细节(改造入口) |
| `docs/Architecture.md` | 整体架构、三方分工 |
| `docs/Arch_Stream_Protocol.md` | Agent↔Web 实时流 / SSE / chunk |
| `docs/Arch_Workflow.md` | workflow 五段流 / 编排 |
