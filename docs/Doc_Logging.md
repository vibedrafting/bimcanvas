# BIMCanvas 日志系统

> 面向 AI 与开发者:**怎么用日志定位程序 / 插件工作流的问题**。
> 本文是跨模块的排查视图,**不展开日志代码的改造细节**——要改 Server / Web 的日志实现,去对应模块 README §11(见 §6)。

BIMCanvas 有**三套日志**,分属三个运行时,各是**某一类真相的唯一所有者**。看懂一个问题往往要把三套对齐着看。

---

## 1. 三套日志:真理归属

| | Server | Web 前端 | Agent SDK transcript |
|---|---|---|---|
| 运行时 | BIMCanvas.Server(.NET 8) | 浏览器(Vue 3) | 托管的 Python Agent(Claude SDK) |
| **唯一记录** | 服务端**执行与状态**:REST / SignalR / SSE 到达、几何 / Git / 落盘、子进程编排 | 浏览器**意图与感知** + **SSE 流的实际内容** | AI **推理与工具轨迹** |
| **只此一层有** | 落盘 / 几何 / Git 的真实结果 | 流解析到的实际事件、用户操作;**Server 对 SSE 流是盲区** | thinking、工具入参全文、token / cache、各子代理耗时 |
| 实时 / 持久 | 控制台 + `{项目}/logs/session_*.log`(**持久**) | F12 + 面板(内存环形 buffer,**易失,刷新即丢**) | 无实时,落 `.jsonl`(**持久**) |
| 对齐键 | `windowId` + 时间戳 | `windowId` + 时间戳 + `clientMessageId` | `toolUseId` / `isSidechain` / `parentUuid` |

**核心**:流是 Server 的结构性盲区(`ProxyToAgentAsync` 透明转发、零日志),由 Web 补全;AI 的 thinking 与工具入参全文只在 SDK transcript。**走错层会一无所获**——这是 §5 排查路由的依据。

---

## 2. Server 日志

实时打**控制台**,并由一层 Console Tee 把同一字节流(去掉颜色码)镜像到**本地文件**。实现:`Logging/ServerConsoleFormatter.cs`(格式化)、`Logging/ConversationLogger.cs`(文件镜像)、`Program.cs`(安装点)。

### 2.1 控制台前缀(判断「这行谁说的」的唯一依据)

| 前缀 | 来源 | 颜色 |
|------|------|------|
| `[Server]` / `:WARN` / `:ERR` / `:DBG` / `:TRC` / `:CRIT` | Server 自身 `ILogger`(按 `LogLevel` 选前缀与颜色) | 白 / 黄 / 灰系 |
| `[Agent]` / `[Agent#n]` | Agent(Python)**stdout**——AI 执行轨迹本身,前缀由 Python 自打,Server 只转发 | 青 |
| `[Agent:ERR]` | Agent **stderr** | 暗青 |
| `[Web]` | 托管的 Vite 输出 | 绿 |
| `[CCR]` | CCR 网关 stdout(已过滤 `image_url` / base64 附件) | 品红 |

> `[Server]` 与 `[Agent]` 是两套日志:前者是 Server 居中调度时自己说的,后者是 AI 干活时说的。查 AI 决策别在 `[Server]` 找。

### 2.2 本地文件 `session_*.log`

- **落点**:`{项目路径}/logs/session_{yyyyMMdd_HHmmss}.log`,按「项目 × Server 会话」分文件;切 / 重开项目滚动到新文件。
- **内容**:与控制台一模一样(只去 ANSI 色码),含 Server / Agent / Web / CCR 全部来源,**不是只存 AI 对话**。
- **落盘**:每行追加后立即关句柄,从不持有文件——否则删 / 移项目时被占住目录报 `IOException`(已修)。
- **时序红线**:`ConversationLogger.Install()` 装 Tee 须早于日志框架初始化(`Program.cs:87` 装、`:119-120` 才注册格式化器),反了 Server 自身日志不进文件。
- **多线程交错**:控制台有多个并发写入者,高并发瞬间单行可能交错,文件如实镜像,未做行级串行化。

### 2.3 级别配置

`appsettings.json`(Production = 全 `Warning`)/ `appsettings.Development.json`(`Default=Information`,框架压到 `Warning`)。文件里有什么 = 控制台显示什么,Tee 不另设过滤。

---

## 3. Web 前端日志

全前端日志走唯一出口 `src/utils/logger.ts`,**禁止散用 `console.*`**。`logger.ts` 是 framework-agnostic 纯 TS,任何模块都能直接 `import`。

### 3.1 五域

| 域 | 色 | 记什么 |
|---|---|---|
| `USER` | 紫 | 用户操作:发送 / 中止、开关 / 删项目、移动 / 旋转 / 复制 / 镜像 / 放置 |
| `STREAM` | 蓝 | SSE 流收发 + turn:send 载荷、`turn.completed` / `turn.failed`、解析失败 |
| `RECV` | 绿 | SignalR 推送:`ReceiveUpdate` / `GitStatusChanged` / `AgentNotification` / `SceneArtifactUpdated` |
| `RENDER` | 粉 | Three / 渲染:模块库加载、场景重建、fitToScreen |
| `SYS` | 灰 | 系统 / 生命周期 / 项目加载 |

格式 `[时间] [Web:域] msg key=val` 与 Server `[时间] [Server] msg` 视觉对齐,F12 与终端可并排对照。

### 3.2 用法与开关

```ts
const log = createLogger('STREAM');
log.info('turn.completed', { win: 'main', dur: '12s', tokens: 4521 });
// → [23:45:12.341] [Web:STREAM] turn.completed win=main dur=12s tokens=4521
```

- `msg` 用英文短语,变量进 `fields` 对象(不拼字符串);关联键带 `win=<windowId>`、`clientMessageId`。
- 切级别:F12 `__bimlog.setLevel('debug')`(另含 `.level` / `.clear()` / `.buffer`)或 `localStorage.bimlog='debug'`(持久)。默认 `DEV→info` / `PROD→warn`,低于当前级别直接丢弃。
- 面板:`Ctrl+\`` 开 `DebugConsole.vue`(级别切换 / 域过滤 / 复制全部)。

### 3.3 易失性

Web 日志纯易失(内存环形 buffer 上限 500 条,**无持久化**)。要留证据,`__bimlog.buffer` 或面板「复制全部」**当场导出**,刷新即丢——这是与 Server `session_*.log` 最大的不对称。

---

## 4. Agent SDK transcript 层

三套里**唯一没有模块 README**、排查 AI 工作流信息量最大的一层。Claude SDK 把每次会话和每个子代理的完整对话逐行落成 `.jsonl`。

### 4.1 落点与目录命名

根:`C:\Users\{user}\.claude\projects\{转义路径}\`。转义规则:工作目录绝对路径 → 盘符冒号去掉、`\` 与非字母数字字符 → `-`、中文保留。
例:`...\Projects\金凤127` → `C--Users-huhaonan-Documents-BIMCanvas-Projects---127`。

### 4.2 文件布局

```
{转义路径}/
├── {sessionUuid}.jsonl                  # 主对话 transcript
└── {sessionUuid}/                       # 仅当起过子代理 / workflow 才有
    ├── subagents/
    │   ├── agent-{id}.jsonl             # 普通 Task 子代理 transcript
    │   ├── agent-{id}.meta.json         # {agentType, description, toolUseId}
    │   └── workflows/wf_{runId}/        # 一次 Workflow 扇出的全部子代理(每个一对文件)
    └── workflows/wf_{runId}.json        # Workflow 运行台账(见 4.4)
```

### 4.3 主 transcript 的关键字段

逐行 JSON,每行一个 `type`(`user` / `assistant` / `queue-operation` / …)。排查最常用:

- **turn 链**:`parentUuid`——某行 `uuid` 是下一行的 `parentUuid`。
- **工具调用**:发起在 `assistant.message.content[]` 的 `{type:"tool_use", id, name, input}`;结果在后续 `user.message.content[]` 的 `{type:"tool_result", tool_use_id, content, is_error}`。
- **工具失败**:`tool_result.is_error: true`(无专门 error 行)。
- **决策依据**:`assistant.message.content[]` 的 `{type:"thinking"}`(Server / Web 都看不到)。
- **token / model**:`assistant.message.usage` / `.model`。

### 4.4 关联键与 workflow 台账

- `agent-{id}.meta.json` 的 **`toolUseId`** = 该子代理在主 transcript 里那次 Task / Workflow 调用的 `id`。拿它在主 `.jsonl` grep,即可回溯「谁、用什么入参派了它」。子代理 jsonl 额外带 `isSidechain:true`、`agentId`、内部 `parentUuid`。
- `agentType` 取值:平台级 `general-purpose` / `Explore`;interior-layout 的 `placement-agent` / `review-agent` / `judge-agent` / `design-scribe` 等(**完整清单以 plugin 仓库 `agents/` 为准**)。
- `wf_{runId}.json` 台账:`script`(含 `meta.name` / `meta.phases`)+ **`workflowProgress`**(每个扇出 agent 的 `state` / `tokens` / `toolCalls` / `durationMs`)——排查 workflow「卡在哪、谁烧 token」先看它。

---

## 5. 排查:按真理归属定位

**判断原则**(替代背症状表):排查不靠枚举现象,靠一个问题——**「我缺的这条事实,属于哪类真相?」**——按 §1 的真理归属直接去那一层。因为流是 Server 盲区、thinking / 入参只在 SDK,**走错层一无所获**。

| 缺的事实属于 | 去 |
|---|---|
| 服务端执行 / 状态(谁收到请求、几何 / Git / 落盘结果) | Server `session_*.log`(按前缀过滤) |
| 浏览器意图 / 感知 + **SSE 流的实际内容** | Web F12(`STREAM` / `RECV` / `RENDER` 域) |
| AI 决策依据(thinking)、工具入参全文、workflow 卡点 | SDK transcript / `wf_*.json` 台账 |

跨层接力用对齐键(§1 末列):`windowId`+时间戳(Server↔Web)、`clientMessageId`(Web 内 send→turn)、`toolUseId`(主 transcript↔子代理)。

**范例 ·「对话发了没反应」**:缺的是「流有没有内容、断在哪」→ 属于**流** → 先看 Web `STREAM`(不是 Server,Server 对流是盲区):有 send 载荷无 `turn.completed` = 流中断;连 send 都没有 = UI 没发出。需确认 AI 侧是否卡住,再跳 SDK 主 transcript 末尾(看 `assistant` 停在哪 / `tool_result.is_error`)。

**范例 ·「workflow 某 agent 失败」**:缺的是「哪个 agent、什么入参、报什么」→ SDK `wf_*.json` 台账找 `state != done` 的 agent → 读其 `subagents/workflows/wf_*/agent-{id}.jsonl` 末尾 → 若 meta 有 `toolUseId`,回主 transcript 看派发入参。

> 症状不穷举是有意的:任何新现象都按「缺哪类真相」自行推导路由,不必等本表列出。环境无 `jq` / `python`,字段已给全,用 `grep` / `findstr` 自行提取即可。

---

## 6. 想改造日志系统?去哪改

本文只讲「用」。改**实现**进对应模块 README,那里有红线与标准动作:

| 改什么 | 去哪读 |
|--------|--------|
| Server 控制台格式 / 前缀 / 本地文件镜像 / Tee | `BIMCanvas.Server/README.md` §11(注意 `Install()` 须早于日志框架初始化) |
| Web logger / 域 / 门控 / 面板 | `BIMCanvas.Web/README.md` §11(注意**禁止在 computed/getter 内打日志**——触发响应式无限循环) |
| Agent SDK transcript | **不可改**——由 Claude SDK 写入,格式随版本演进,§4 仅描述当前实测结构 |

相关:`docs/Architecture.md`(三方分工)、`docs/Arch_Stream_Protocol.md`(SSE / chunk)、`docs/Arch_Workflow.md`(workflow 编排)。
