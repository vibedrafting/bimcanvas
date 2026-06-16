# Claude Agent SDK 参数配置说明（BIMCanvas Agent）

> **文档定位**:BIMCanvas Agent 端关于 `claude-agent-sdk` 配置的**权威、长期维护**参考。一份文档讲清:① 当前 SDK 版本;② 每个参数「可配置 / 硬编码 / manifest 驱动 / 未消费」的归属;③ `instance.config.json` 怎么改;④ SDK 全量参数与 Agent 消费明细。
> **怎么读这份文档**:只想知道「能改什么、怎么改」→ 看 §1 + §2;想知道「某参数为什么写死」→ §1.3;想查 SDK 全量字段 → §3;想知道某字段 Agent 怎么消费 → §4。
> **维护规范**:见 §6。**每次升级 SDK 或改 `instance.config.json` schema，必须按 §6 流程更新本文并轮换 🆕 标记。**
> **配套深度分析**（一次性快照，非维护态）:`.dev/plans/SDK_0.2.87升级适配/SDK_0.2.87_可配置项与Agent消费对照表.md`。

---

## 0. 当前 SDK 版本

| 项 | 值 |
|----|----|
| Python 包 | `claude-agent-sdk == 0.2.87` |
| 声明位置 | `BIMCanvas.Agent/pyproject.toml` |
| bundled CLI | `claude-cli/2.1.150`（SDK 内置，随包升级） |
| `mcp` 依赖下限 | `>= 1.23.0`（0.2.82 引入） |
| SDK 源码 | `<python>/Lib/site-packages/claude_agent_sdk/`（`types.py` 为类型权威） |
| 唯一活跃运行时 | **Claude Agent SDK 路径**。OpenAI 兼容路径已废弃维护（项目 CLAUDE.md §12） |

**归属图例**（参数「从哪来、能不能改」）:

| 标记 | 含义 |
|------|------|
| ⚙️ | **可配置** — `instance.config.json` 可改（见 §2） |
| 📦 | **manifest/bundle 驱动** — 值来自 plugin manifest / ConfigBundle，不在 `instance.config.json`，也不是常量 |
| 🔧 | **硬编码** — 写死在 `main_agent.py:_create_options` 等处，**无任何配置入口** |
| 🧭 | **运行时派生** — 由请求上下文/构造参数决定（如 cwd、运行时 model 覆盖） |
| ⬜ | **未消费** — SDK 支持但 Agent 未传（见 §4.4 缺口表） |

**变动图例**:

| 标记 | 含义 |
|------|------|
| 🆕`0.2.87` | **本版（0.2.87 升级系列）新增或变动**的参数/配置。下次升级时按 §6 规范沉淀为普通样式 |

---

## 1. 配置落地真相（先看这个）

> 把传给 SDK 的每个参数按「从哪来」归类。这是判断「能改什么 / 为什么改不了」的唯一权威表。Agent 在 `main_agent.py:_create_options()` 构造 `ClaudeAgentOptions`。
> ⚠️ 本文出现的 `:NNN` 行号锚点会随代码漂移，**以符号名为准**（`_create_options` / `_auto_approve_tool` / `materialize_system_prompt_file` / `subagents.py:create_subagents`），行号仅作当时参考。

### 1.1 ⚙️ 可通过 `instance.config.json` 修改

> 除末行 `imageRecognition` 外全部在 `agent.claude` 段。改完重启 Agent 生效。用法详见 §2。

| 配置键 | 对应 SDK 参数 | 功能（简短） | 取值 / 默认 | 变动 |
|--------|--------------|-------------|------------|------|
| `runtimeProvider` | （选 runtime，非 SDK 参数） | 选 claude / openai 运行时 | `claude`(默认) / `openai` | — |
| `claude.defaultModel` | `model` | 默认模型 alias（运行时可被请求覆盖 🧭） | `opus`/`sonnet`/`haiku`，默认 `opus` | — |
| `claude.modelMapping` | `env: ANTHROPIC_DEFAULT_{OPUS,SONNET,HAIKU}_MODEL` | alias→真实 model id+label | key 限三别名；value `{id,label}` | — |
| `claude.defaultEffort` | `effort` | 默认推理深度（运行时可覆盖 🧭） | `low/medium/high/max/`🆕`xhigh`，默认 `low` | 🆕`0.2.87` 新增 `xhigh` 取值（O2） |
| `claude.defaultThinking` | `thinking` | 扩展思考开关（运行时可覆盖 🧭） | `off`/`adaptive`，默认 `adaptive` | — |
| `claude.maxThinkingTokens` | `max_thinking_tokens` | 思考 token 预算 | int / 空=8000 / -1=不限；env `MAX_THINKING_TOKENS` 覆盖 | — |
| `claude.baseUrl` | `env: ANTHROPIC_BASE_URL` | 直连 API endpoint | 字符串；CCR 托管时由网关 env 接管 | — |
| `claude.apiKey` | `env: ANTHROPIC_API_KEY` | 直连 API key | 字符串；推荐改用环境变量 | — |
| `claude.env` | `env`（`ClaudeAgentOptions.env`） | 透传给 Claude CLI 子进程的环境变量 | `dict[str,str]` | 🆕`0.2.87` **新增字段**（透传 SDK 0.2.87 env 入口） |
| `imageRecognition.*` | （非 SDK 参数，`canvas_vision` 工具消费） | 识图后端多 provider 配置 | 见 §2.5 | 🆕`2026-06-08` R4-4 新增 apiyi provider |

### 1.2 📦 plugin manifest / ConfigBundle 驱动（不在 instance.config.json）

> 这些「可改」，但入口不在 `instance.config.json`，而在 plugin（manifest / prompt / agents / mcp_tools）。

| SDK 参数 | 功能（简短） | 真源 | 位置 |
|---------|-------------|------|------|
| `system_prompt`（内容） | 系统提示词内容 | plugin `BIMCANVAS.md`（core-base + domain 两层叠加）+ 运行时追加三项:项目路径 / 工作目录 / **插件根**（🆕`2026-06` Workflow 机制需要:主控用它拼 `scriptPath=<插件根>/workflows/*.workflow.js` 绝对路径,统一正斜杠;无 domain plugin 不注入） | `_create_options` 开头（落盘机制见 §1.3） |
| `allowed_tools` | 工具白名单（空=全开） | plugin manifest `tools.allow` | `:253,310` |
| `disallowed_tools` | 工具黑名单（deny 优先） | plugin manifest `tools.deny` | `:254,311` |
| `mcp_servers` | in-process MCP 服务器 | ConfigBundle（canvas + plugin `mcp_tools/*.py`） | `:282,319` |
| `plugins` | 加载的 plugin 目录 | `ConfigBundle.active_plugin_paths`（core-base + active domain） | `:295-303,330` |
| `agents` | SubAgent 定义集 | base + plugin `agents/*.md`（见 §4.2） | `:312` |

### 1.3 🔧 硬编码常量（无任何配置入口）

> 写死在代码里，是平台/安全不变量，**刻意不开放配置**。

| 硬编码值 | 对应 SDK 参数 | 功能（简短） | 为什么写死 | 位置 / 变动 |
|---------|--------------|-------------|-----------|------------|
| `setting_sources=[]` | `setting_sources` | 禁用全部 filesystem 设置层 | 防 `~/.claude/CLAUDE.md`/用户 Skills/MCP 注入污染；`None` 会触发默认拉 user+project，**勿改回 None** | `:329` 🆕`0.2.87`（`None`→`[]`） |
| `system_prompt=SystemPromptFile{file}` | `system_prompt`（形态） | 提示词走文件而非命令行，绕 Windows 32767 上限 | 内容是 📦 plugin 驱动；**落盘机制/路径**写死 `BIMCANVAS_HOME/.runtime/system-prompt/` | `:246,306` 🆕`0.2.87`（M2，原为 str） |
| `skills="all"` | `skills` | 启用全部 Skills | 0.1.62+ 官方推荐入口，平台行为无 per-instance 取舍 | `:323` 🆕`0.2.87`（S2，待 SDK #977 清理 manifest `"Skill"` literal） |
| `strict_mcp_config=True` | `strict_mcp_config` | 只用代码传入的 mcp_servers | 与 `setting_sources=[]` 形成防污染闭环，安全不变量 | `:320` 🆕`0.2.87`（O1） |
| `max_turns=30` | `max_turns` | 单会话最大轮数 | 限流（W3 cache miss 严重时想降的对象，目前写死） | `:308` |
| `permission_mode="acceptEdits"` | `permission_mode` | 自动接受文件操作 | 后端无人值守必需 | `:313` |
| `include_partial_messages=True` | `include_partial_messages` | 输出流式 partial 事件 | 流式 UI 必需 | `:314` |
| `max_buffer_size=10*1024*1024` | `max_buffer_size` | stdout 读取缓冲 10MB | 截图 ImageContent 默认 1MB 不够 | `:331` |
| `can_use_tool=_auto_approve_tool` | `can_use_tool` | 自动批准工具；AskUserQuestion 走侧信道 | 后端无人值守必需 | `:332,335-358` |
| `ToolAnnotations(maxResultSizeChars=500_000)` | `@tool` 的 `annotations` | 把 `load_scene_artifact` 结果上限提到 500K | 工具级实现细节，单点扩容（其余 8 工具不动） | `canvas.py:1171` 🆕`0.2.87`（O5） |
| `AgentDefinition.disallowedTools`（三态分支逻辑） | `AgentDefinition.disallowedTools` | 恢复 per-SubAgent deny 能力 | 消费**逻辑**写死；deny **值**来自 manifest `tools.deny`（📦） | `subagents.py:107-114,120` 🆕`0.2.87`（S1） |

### 1.4 🧭 运行时派生

| SDK 参数 | 来源 |
|---------|------|
| `cwd` | `self.working_directory`（MainAgent 构造/请求上下文，`:307`） |
| `model` / `effort` / `thinking` | 默认值见 §1.1，但 `chat_stream(model/effort/thinking=)` 运行时入参**优先级更高** |

### 1.5 ⬜ 未消费

SDK 支持但 Agent 未传的 25 个 `ClaudeAgentOptions` 字段 + 8 个 `AgentDefinition` 字段，见 **§4.4 缺口表**。

---

## 2. `instance.config.json` 使用详解

### 2.1 文件位置与结构

- **运行时路径**（不在仓库，首启动自动生成）:`%USERPROFILE%\Documents\BIMCanvas\instance.config.json`
- **模板**:`BIMCanvas.Server/Templates/global-config/instance.config.json`
- **编码**:UTF-8（支持 BOM，`utf-8-sig` 读取）
- **顶层结构**:`{ server, web, agent, ccr }`。**Agent 只读 `agent` 段**（`runtimeProvider / claude / openai / chatgptBackend`）。
- **旧布局回退**:无 `instance.config.json` 但有独立 `config.json` 时，整份当作 agent 段（过渡兼容）。
- **生效**:改完**重启 Agent**（`settings` 经 `@lru_cache` 缓存）。

### 2.2 `agent.claude` 段示例（当前默认 runtime）

```jsonc
"agent": {
  "runtimeProvider": "claude",          // "claude"(默认) | "openai"；env AGENT_RUNTIME_PROVIDER 可覆盖
  "claude": {
    "baseUrl": "",                       // 直连 ANTHROPIC_BASE_URL；CCR 托管留空(网关注入)
    "apiKey": "",                        // 直连 ANTHROPIC_API_KEY；推荐用环境变量
    "defaultModel": "opus",              // opus|sonnet|haiku，且须存在于 modelMapping
    "defaultEffort": "low",              // low|medium|high|max|xhigh(🆕0.2.87)；非法值启动报错
    "defaultThinking": "adaptive",       // off|adaptive
    "maxThinkingTokens": 8000,           // 整数=预算；-1 或空=不限制；env MAX_THINKING_TOKENS 覆盖
    "modelMapping": {                    // alias→真实 id+label；key 只能 opus/sonnet/haiku
      "opus":   { "id": "claude-opus-4-7[1m]", "label": "Opus" },
      "sonnet": { "id": "claude-sonnet-4-6",   "label": "Sonnet" },
      "haiku":  { "id": "claude-haiku-4-5",    "label": "Haiku" }
    },
    "env": {                             // 🆕0.2.87 透传给 Claude CLI 子进程(ClaudeAgentOptions.env)
      "CLAUDE_CODE_WORKFLOWS": "1",      // key/value 必须都是字符串(1 要写 "1")；
      "DISABLE_GROWTHBOOK": "1"          // 同名会被派生的 ANTHROPIC_BASE_URL/API_KEY 覆盖
    }
  }
}
```

### 2.3 校验与解析点（出错时定位）

| 配置键 | 校验/解析函数 | 失败行为 |
|--------|--------------|---------|
| `claude.defaultModel` | `settings._resolve_claude_default_model` | 非 opus/sonnet/haiku 或不在 modelMapping → 启动报错 |
| `claude.modelMapping` | `_sanitize_claude_model_mapping` + `_apply_model_mapping` | key 超出三别名 → 报错 |
| `claude.defaultEffort` | `_resolve_claude_effort`（白名单 `_ALLOWED_EFFORTS`） | 非白名单 → 报错 |
| `claude.defaultThinking` | `_resolve_claude_thinking` | 非 off/adaptive → 报错 |
| `claude.maxThinkingTokens` | `_resolve_optional_int` | 非整数/空/-1 → 报错 |
| `claude.baseUrl` / `claude.apiKey` | `_load_claude_settings`（CCR 托管检测 `AGENT_SDK_*`） | CCR 模式缺网关 env → 报错 |
| `claude.env` | `loader.ensure_agent_config_schema` + `_load_claude_settings` | 非 dict、空 key、非字符串 value → 报错 |

**优先级**:运行时 `chat_stream(model/effort/thinking=)` > `instance.config.json` 默认。CCR 托管模式下 `baseUrl/apiKey` 由 `AGENT_SDK_BASE_URL/API_KEY` 接管，config 内同名被忽略。

### 2.4 `agent.openai` / `agent.chatgptBackend` — ⚠️ 已废弃维护

`runtimeProvider="openai"` 路径已大幅偏离当前架构、不维护（项目 CLAUDE.md §12）。日常仅维护 `claude` 段。

### 2.5 `agent.imageRecognition` 段（canvas_vision 识图后端，🆕`2026-06-08`）

> 非 SDK 参数，由 `mcp__canvas__canvas_vision` 工具消费。加载逻辑:`BIMCanvas.Agent/src/image_recognition/config.py:load_recognition_config`。

```jsonc
"agent": {
  "imageRecognition": {
    "provider": "apiyi",                 // apiyi(默认,OpenAI Chat Completions 格式) | aoment(multipart);env IMAGE_RECOGNITION_PROVIDER 覆盖
    "providers": {
      "apiyi":  { "apiKey": "", "endpoint": "https://api.apiyi.com/v1/chat/completions", "model": "gemini-3.5-flash", "timeoutSeconds": 90 },
      "aoment": { "apiKey": "", "endpoint": "https://www.aoment.com/api/aoment/v1/image/recognitions", "model": "image-recognition-g2", "timeoutSeconds": 90 }
    }
  }
}
```

- 优先级:provider 专属环境变量（`APIYI_API_KEY` / `AOMENT_API_KEY`）> `providers.<provider>` 字段 > 代码默认值。
- `apiKey` 缺失 → 工具调用时抛 `RecognitionConfigError`（含注册引导链接），不影响 Agent 启动。
- `timeoutSeconds` 钳制到 [30, 600]，非法值回落 90。

### 2.6 不在 `instance.config.json` 的相关配置

| 配置 | 真源 |
|------|------|
| 端口（server/web/agent） | `instance.config.json` 的 `server` 段 / `server_config.json` |
| 工具权限 `tools.allow/deny` | plugin manifest `bimcanvas-plugin.json`（v3.3 起从 config 迁出，config 内再写报错） |
| SubAgent / Skill / MCP 工具 / 系统提示词 | plugin（core-base 在 `BIMCanvas.Agent/plugins/core-base/`；domain 在独立仓库） |
| §1.3 硬编码项 | `main_agent.py:_create_options`，无入口 |

---

## 3. SDK 全量参数参考

> 纯 SDK 表面，以 `types.py` 为准。「归属」列指向 §1 的分类（Agent 侧如何对待）。

### 3.1 `ClaudeAgentOptions`（共 45 字段）

| SDK 字段 | 类型 | 默认 | 含义 | 归属 |
|---------|------|------|------|------|
| `system_prompt` | `str \| SystemPromptPreset \| SystemPromptFile \| None` | `None` | 系统提示词 | 📦内容/🔧机制 |
| `tools` | `list[str] \| ToolsPreset \| None` | `None` | 基础工具集 | ⬜ |
| `allowed_tools` | `list[str]` | `[]` | 免提示工具名（`"Skill"` 弃用） | 📦 |
| `disallowed_tools` | `list[str]` | `[]` | 禁用工具名 | 📦 |
| `mcp_servers` | `dict[str, McpServerConfig] \| str \| Path` | `{}` | MCP 服务器配置 | 📦 |
| `strict_mcp_config` | `bool` | `False` | 只用代码传入的 MCP | 🔧 |
| `permission_mode` | `PermissionMode \| None` | `None` | 权限模式 | 🔧 |
| `can_use_tool` | `CanUseTool \| None` | `None` | 自定义权限回调 | 🔧 |
| `permission_prompt_tool_name` | `str \| None` | `None` | 用 MCP 工具处理权限（与 can_use_tool 互斥） | ⬜ |
| `hooks` | `dict[HookEvent, list[HookMatcher]] \| None` | `None` | 生命周期 hook | ⬜ |
| `model` | `str \| None` | `None` | 模型 id | ⚙️🧭 |
| `fallback_model` | `str \| None` | `None` | 降级模型 | ⬜ |
| `effort` | `EffortLevel \| None` | `None` | 推理深度 | ⚙️🧭 |
| `thinking` | `ThinkingConfig \| None` | `None` | 扩展思考配置 | ⚙️🧭 |
| `max_thinking_tokens` | `int \| None` | `None` | 思考预算（SDK 标注 deprecated） | ⚙️ |
| `betas` | `list[SdkBeta]` | `[]` | beta 特性 | ⬜ |
| `max_turns` | `int \| None` | `None` | 最大轮数 | 🔧 |
| `max_budget_usd` | `float \| None` | `None` | USD 预算上限 | ⬜ |
| `task_budget` | `TaskBudget \| None` | `None` | API 侧 token 预算 | ⬜ |
| `agents` | `dict[str, AgentDefinition] \| None` | `None` | 程序化 SubAgent | 📦 |
| `skills` | `list[str] \| Literal["all"] \| None` | `None` | 启用的 Skills | 🔧 |
| `plugins` | `list[SdkPluginConfig]` | `[]` | 加载的 plugin | 📦 |
| `setting_sources` | `list[SettingSource] \| None` | `None` | 加载哪些 filesystem 设置层 | 🔧 |
| `settings` | `str \| None` | `None` | 额外 settings JSON 路径 | ⬜ |
| `cwd` | `str \| Path \| None` | `None` | 工作目录 | 🧭 |
| `add_dirs` | `list[str \| Path]` | `[]` | cwd 外可访问目录 | ⬜ |
| `cli_path` | `str \| Path \| None` | `None` | 自定义 CLI 路径 | ⬜ |
| `env` | `dict[str, str]` | `{}` | 子进程环境变量 | ⚙️ |
| `extra_args` | `dict[str, str \| None]` | `{}` | 透传额外 CLI 参数 | ⬜ |
| `max_buffer_size` | `int \| None` | `None`(≈1MB) | stdout 缓冲上限 | 🔧 |
| `stderr` | `Callable[[str], None] \| None` | `None` | stderr 回调 | ⬜ |
| `debug_stderr` | `Any` | `sys.stderr` | 已 deprecated | ⬜ |
| `user` | `str \| None` | `None` | 用户标识 | ⬜ |
| `include_partial_messages` | `bool` | `False` | 流式 partial 事件 | 🔧 |
| `include_hook_events` | `bool` | `False` | hook 生命周期事件 | ⬜ |
| `output_format` | `dict[str, Any] \| None` | `None` | 结构化输出 | ⬜ |
| `sandbox` | `SandboxSettings \| None` | `None` | 命令沙箱隔离 | ⬜ |
| `enable_file_checkpointing` | `bool` | `False` | 文件检查点（`rewind_files()`） | ⬜ |
| `continue_conversation` | `bool` | `False` | 续接最近会话 | ⬜ |
| `resume` | `str \| None` | `None` | 恢复指定 session | ⬜ |
| `session_id` | `str \| None` | `None` | 固定 session UUID | ⬜ |
| `fork_session` | `bool` | `False` | resume 时分叉新 session | ⬜ |
| `session_store` | `SessionStore \| None` | `None` | 外部会话存储 | ⬜ |
| `session_store_flush` | `SessionStoreFlushMode` | `"batched"` | 刷盘策略 | ⬜ |
| `load_timeout_ms` | `int` | `60000` | session_store.load 超时 | ⬜ |

### 3.2 `AgentDefinition`（SubAgent，共 13 字段）

| 字段 | 类型 | 默认 | 含义 | 归属 |
|------|------|------|------|------|
| `description` | `str` | required | SubAgent 描述 | 📦 |
| `prompt` | `str` | required | SubAgent system prompt | 📦 |
| `tools` | `list[str] \| None` | `None` | 工具白名单 | 📦 |
| `disallowedTools` | `list[str] \| None` | `None` | 工具黑名单（0.1.51 #759 恢复） | 📦🔧 |
| `model` | `str \| None` | `None` | 模型 alias/id（`inherit`） | 📦 |
| `skills` | `list[str] \| None` | `None` | 该 SubAgent 可用 Skills | ⬜ |
| `memory` | `Literal['user','project','local'] \| None` | `None` | 内存作用域 | ⬜ |
| `mcpServers` | `list[str \| dict] \| None` | `None` | per-SubAgent MCP 隔离 | ⬜ |
| `initialPrompt` | `str \| None` | `None` | 启动首条提示 | ⬜ |
| `maxTurns` | `int \| None` | `None` | 该 SubAgent 轮数上限 | ⬜ |
| `background` | `bool \| None` | `None` | 后台运行 | ⬜ |
| `effort` | `EffortLevel \| int \| None` | `None` | per-SubAgent 推理深度 | ⬜ |
| `permissionMode` | `PermissionMode \| None` | `None` | per-SubAgent 权限模式 | ⬜ |

### 3.3 工具注册 API（in-process MCP）

| 入口 | 参数 | 含义 |
|------|------|------|
| `@tool` | `name` / `description` / `input_schema` / `annotations` | 定义单个 MCP 工具（`annotations` 透传 `_meta`） |
| `create_sdk_mcp_server` | `name` / `version="1.0.0"` / `tools` | 打包成 in-process MCP server |
| `ToolAnnotations` | `maxResultSizeChars` / `readOnly` / `destructive` / `openWorld` | 结果上限 / 只读 / 破坏性 / 开放世界 |

### 3.4 关键类型别名与嵌套类型

| 类型 | 取值 / 字段 |
|------|------------|
| `EffortLevel` | `low` / `medium` / `high` / `xhigh`(Opus 4.7+) / `max` |
| `PermissionMode` | `default` / `acceptEdits` / `plan` / `bypassPermissions` / `dontAsk` / `auto` |
| `SettingSource` | `user` / `project` / `local` |
| `SystemPromptPreset` | `{type:"preset", preset:"claude_code", append?, exclude_dynamic_sections?}` |
| `SystemPromptFile` | `{type:"file", path}` |
| `ThinkingConfigAdaptive` | `{type:"adaptive", display?}` |
| `ThinkingConfigEnabled` | `{type:"enabled", budget_tokens, display?}` |
| `ThinkingConfigDisabled` | `{type:"disabled"}` |
| `ThinkingDisplay` | `summarized` / `omitted` |
| `McpServerConfig` | `McpStdioServerConfig` / `McpSSEServerConfig` / `McpHttpServerConfig` / `McpSdkServerConfig`（BIMCanvas 仅用 sdk in-process） |

### 3.5 会话 / Session 管理 API（与 Options 无关，BIMCanvas 全不用）

`list_sessions` / `get_session_messages` / `fork_session` / `delete_session` / `rename_session` / `tag_session` 及 `*_from_store` 变体、`SessionStore` 协议；`ClaudeSDKClient` 的 `set_permission_mode` / `set_model` / `rewind_files` / `reconnect_mcp_server` / `toggle_mcp_server` / `stop_task`。
→ **全部不用**:持久化/编排由 `.bcp/schemes/{zoneId}/*.json` + git worktree + 自有 `X-Session-Id` 承担。`ClaudeSDKClient` 只用 `connect/query/receive_*/interrupt/disconnect`。

---

## 4. Agent 消费明细

### 4.1 `ClaudeAgentOptions`（传入 20 / 45）

> 「来源」与「归属」对应 §1 分类，此处补充「怎么消费 / 位置」。

| SDK 字段 | 归属 | 怎么消费 / 位置 | 变动 |
|---------|------|----------------|------|
| `system_prompt` | 📦🔧 | `ConfigBundle.system_prompt`（两层 BIMCANVAS.md）+ 追加项目/工作目录 → `materialize_system_prompt_file()` 落盘 → `SystemPromptFile` dict。`:237-246,306` | 🆕`0.2.87` M2 |
| `cwd` | 🧭 | `self.working_directory` 直传。`:307` | — |
| `max_turns` | 🔧 | `30`。`:308` | — |
| `model` | ⚙️🧭 | 运行时入参 或 `claude.defaultModel`→modelMapping。`:309` | — |
| `allowed_tools` | 📦 | `bundle.tools_allow` 原样（空=全开）。`:253,310` | — |
| `disallowed_tools` | 📦 | `bundle.tools_deny` 原样（deny 优先）。`:254,311` | — |
| `agents` | 📦 | `create_subagents()`。`:312` | — |
| `permission_mode` | 🔧 | `"acceptEdits"`。`:313` | — |
| `include_partial_messages` | 🔧 | `True`。`:314` | — |
| `env` | ⚙️ | `claude.env` + 派生 `ANTHROPIC_BASE_URL/API_KEY` + `ANTHROPIC_DEFAULT_*_MODEL`。`:261-269,315` | 🆕`0.2.87` 新增 `claude.env` 入口 |
| `effort` | ⚙️🧭 | 运行时入参 或 `claude.defaultEffort`；`"off"→None`。`:272,316` | 🆕`0.2.87` 接受 `xhigh` |
| `thinking` | ⚙️🧭 | `adaptive→Adaptive`，否则 `Disabled`。`:275-279,317` | — |
| `max_thinking_tokens` | ⚙️ | `claude.maxThinkingTokens`（env 覆盖）。`:318` | — |
| `mcp_servers` | 📦 | `bundle.mcp_servers_spec`（canvas + plugin）。`:282,319` | — |
| `strict_mcp_config` | 🔧 | `True`。`:320` | 🆕`0.2.87` O1 |
| `skills` | 🔧 | `"all"`。`:323` | 🆕`0.2.87` S2 |
| `setting_sources` | 🔧 | `[]`（**勿改回 None**）。`:329` | 🆕`0.2.87`（None→[]） |
| `plugins` | 📦 | `bundle.active_plugin_paths` → `{type:"local",path}`。`:295-303,330` | — |
| `max_buffer_size` | 🔧 | `10*1024*1024`。`:331` | — |
| `can_use_tool` | 🔧 | `_auto_approve_tool`。`:332,335-358` | — |

### 4.2 `AgentDefinition`（消费 5 / 13）

> `subagents.py:create_subagents()` 把 `loader.py` 解析的 `AgentConfig`（frontmatter）转 `AgentDefinition`，构造在 `subagents.py:116-122`。frontmatter 仅解析 `name/description/tools/model`。

| 字段 | 状态 | 取值 / 逻辑 | 变动 |
|------|------|-----------|------|
| `description` | ✅ | frontmatter `description`（必填） | — |
| `prompt` | ✅ | `.md` 正文 + `_append_runtime_context()`（仅 layout-agent/module-relocation-agent） | — |
| `tools` | ✅ | 三态:`None`→继承 `tools_allow`（空则 `None`=inherit-all）；list→用 `.md` 列表 | — |
| `disallowedTools` | 🟡 | 仅「继承分支」从 `tools_deny` 深拷贝；「显式自主分支」为 `None`（主控 deny 全局兜底） | 🆕`0.2.87` S1 回填 |
| `model` | ✅ | frontmatter `model`，缺省 `"inherit"` | — |
| `skills/memory/mcpServers/initialPrompt/maxTurns/background/effort/permissionMode` | ⬜ | 未传（见 §4.4） | — |

### 4.3 工具注册 + 输出侧消费

- **`@builder.tool`**:消费 `name/description/input_schema/annotations`。`load_scene_artifact` 设 `maxResultSizeChars=500_000`（🆕`0.2.87` O5），其余工具默认 50K；`readOnly/destructive/openWorld` ⬜ 未标注。
- **输出侧（SDK→Agent）**，消息循环 `main_agent.py` 约 `:1180-1480`:
  - `AssistantMessage.content[]`（全 block + `ServerToolUseBlock/AdvisorToolResultBlock` 兜底 🆕`0.2.87` M1）、`AssistantMessage.error/model`
  - `ResultMessage.is_error/subtype/num_turns/total_cost_usd/duration_ms`、`api_error_status`/`errors`（🆕`0.2.87` S3）、`usage`/`model_usage`（🆕`0.2.87` W3 cache 埋点+fallback）
  - `RateLimitEvent`、`TaskStarted/TaskProgress/TaskNotification`（🆕`0.2.87` S4；TaskNotification 仅观察期 log）

### 4.4 缺口表（SDK 支持但未消费，按价值）

| SDK 项 | 价值 | 现状替代 / 备注 |
|--------|------|----------------|
| `AgentDefinition.mcpServers=[]` | `layout-agent` 物理隔离 canvas MCP | 软约定（O4 计划，未落地） |
| `AgentDefinition.permissionMode/effort` | per-SubAgent 权限/推理深度定制 | 全继承主控 |
| `ThinkingConfig.display="summarized"` | layout-agent 推理可视化调试 | O3 阶段1 观测中 |
| `fallback_model` | 生产高可用降级 | 无降级 |
| `max_budget_usd` / `task_budget` | 成本/ token 硬上限 | 仅 `max_turns=30` 限流 |
| `user` | Web 多用户会话隔离 | 未注入 |
| `ToolAnnotations.destructive/readOnly` | 删除类工具显式标注 | 隐含处理 |
| `betas=["context-1m-2025-08-07"]` | 超大户型上下文 | 未启用 |
| session API / `session_store` / `resume` / `enable_file_checkpointing` | SDK 自带会话持久化/回滚 | **刻意不用**:`.bcp` + git worktree + 自有 session；回滚靠 git |
| `settings` / `setting_sources!=[]` / `hooks` / `sandbox` | 外部设置/hook/沙箱 | **刻意不用**:`setting_sources=[]` 防污染，隔离靠 worktree+权限 |

---

## 5. 变更记录

| 日期 | SDK 版本 | 本版新增/变动（🆕 项汇总） |
|------|---------|--------------------------|
| 2026-06-12 | 0.2.87 | 文档对账修订:补 `agent.imageRecognition` 段（§1.1/§2.5,2026-06-08 R4-4 canvas_vision 多 provider）;补 system_prompt 运行时追加「插件根」（Workflow scriptPath 用）;声明行号锚点以符号名为准;§6 触发清单纳入 `image_recognition/config.py`。 |
| 2026-05-29 | 0.2.87 | **可配置**:新增 `claude.env`、`defaultEffort` 接受 `xhigh`。**硬编码**:`setting_sources`(None→[])、`system_prompt`(SystemPromptFile)、`skills="all"`、`strict_mcp_config=True`、`maxResultSizeChars=500K`、`AgentDefinition.disallowedTools` 回填。**输出侧**:M1 块兜底、S3 错误协议、S4 类型化消息、W3 cache 埋点。计划未落地:O3 阶段2、O4。 |

---

## 6. 维护规范（必读）

**触发**:升级 SDK 版本，或改动 `main_agent.py:_create_options` / `subagents.py` / `settings.py` / `image_recognition/config.py` / **任何新增 `instance.config.json` `agent` 段配置键的代码**（不限于上述文件——2026-06-12 对账发现 imageRecognition 段因加在清单外文件而漏记，引以为戒）。

**🆕 标记口径**:`🆕vX.Y.Z` 只标「相对**上一份本文记录的版本**新增或变动」的参数/配置——只标 delta，不标存量。

**更新流程（按顺序）**:

1. **沉淀上一版**:全文搜索上一版本号的 `🆕` 标记（如 `🆕\`0.2.87\``），**全部删除**，让这些参数回归普通样式（变成存量）。
2. **标注本版**:给本次新增/变动的项加 `🆕\`新版本号\``（§1 表格「变动」列、§2 示例注释、§4 「变动」列）。
3. **更新 §0**:当前 SDK 版本、CLI 版本、依赖下限。
4. **核对全量表（§3）**:对照 `types.py` 的 `ClaudeAgentOptions` / `AgentDefinition` 字段增删，增改字段并更新「归属」列。
5. **核对落地真相（§1）/消费明细（§4）**:对照 `main_agent.py:_create_options` 与 `subagents.py` 实际传参，调整 ⚙️/📦/🔧/🧭/⬜ 归属。
6. **同步 §2**:若改了 `instance.config.json` schema，更新示例、校验点表、§2.5。
7. **追加 §5 变更记录**:一行，汇总本版所有 🆕 项。

**自检**:`python -m compileall` 通过即可；本文不写测试，端到端由用户手动验证。
