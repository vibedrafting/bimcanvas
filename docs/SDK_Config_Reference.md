# Claude Agent SDK 参数配置说明（BIMCanvas Agent）

> **文档定位**:本文是 BIMCanvas Agent 端关于 `claude-agent-sdk` 配置的**权威、长期维护**参考。记录:① 当前 SDK 版本;② SDK 支持的全部参数;③ Agent 当前实际消费的参数;④ 通过运行时配置文件 `instance.config.json` 可修改的配置项及用法。
> **维护约定**:每次升级 SDK 或改动 `main_agent.py:_create_options` / `subagents.py` / `settings.py` / `instance.config.json` schema 时，**必须同步更新本文**（更新「当前 SDK 版本」「变更记录」并核对相关表格）。
> **配套深度分析**（非维护态，仅快照）:`.dev/plans/SDK_0.2.87升级适配/SDK_0.2.87_可配置项与Agent消费对照表.md`。

---

## 0. 当前 SDK 版本

| 项 | 值 |
|----|----|
| Python 包 | `claude-agent-sdk == 0.2.87` |
| 声明位置 | `BIMCanvas.Agent/pyproject.toml` |
| bundled CLI | `claude-cli/2.1.150`（SDK 内置，自动随包升级） |
| `mcp` 依赖下限 | `>= 1.23.0`（0.2.82 引入） |
| SDK 源码 | `<python>/Lib/site-packages/claude_agent_sdk/`（`types.py` 为类型权威） |
| 唯一活跃运行时 | **Claude Agent SDK 路径**（`config_bundle.py` + plugin `register(builder)` + manifest 权限）。OpenAI 兼容路径已废弃维护，见项目 CLAUDE.md §12 |

**状态图例**:✅ 已消费(传给 SDK) · 🔧 硬编码常量 · ⚙️ 配置文件可调 · 🟡 部分消费 · ⬜ 未消费(SDK 支持但 Agent 未传) · 🕓 观测/计划中

---

## 1. SDK 支持的参数配置（全量）

### 1.1 `ClaudeAgentOptions`（SDK 唯一启动配置对象，共 45 字段）

> Agent 在 `main_agent.py:_create_options()` 构造（`return ClaudeAgentOptions(...)` 约 `:305-333`）。「消费」列见 §2。

| SDK 字段 | 类型 | 默认 | 含义 |
|---------|------|------|------|
| `system_prompt` | `str \| SystemPromptPreset \| SystemPromptFile \| None` | `None` | 系统提示词:字符串 / preset dict / 文件 dict |
| `tools` | `list[str] \| ToolsPreset \| None` | `None` | 基础可用工具集（名单 / 空 list 全关 / preset） |
| `allowed_tools` | `list[str]` | `[]` | 免提示自动执行的工具名（`"Skill"` 已弃用，改用 `skills`） |
| `disallowed_tools` | `list[str]` | `[]` | 禁用工具名（从模型上下文移除） |
| `mcp_servers` | `dict[str, McpServerConfig] \| str \| Path` | `{}` | MCP 服务器配置或配置文件路径 |
| `strict_mcp_config` | `bool` | `False` | True=只用代码传入的 mcp_servers，忽略 CLI/项目/用户级 MCP |
| `permission_mode` | `PermissionMode \| None` | `None` | 权限模式（见 §1.4） |
| `can_use_tool` | `CanUseTool \| None` | `None` | 自定义权限回调（CLI 判定为 ask 时调用） |
| `permission_prompt_tool_name` | `str \| None` | `None` | 用 MCP 工具处理权限请求（与 `can_use_tool` 互斥） |
| `hooks` | `dict[HookEvent, list[HookMatcher]] \| None` | `None` | 生命周期 hook 回调 |
| `model` | `str \| None` | `None` | 模型 id（默认走 CLI 默认） |
| `fallback_model` | `str \| None` | `None` | 主模型失败时的降级模型 |
| `effort` | `EffortLevel \| None` | `None` | 推理深度 low/medium/high/xhigh/max |
| `thinking` | `ThinkingConfig \| None` | `None` | 扩展思考配置（见 §1.4） |
| `max_thinking_tokens` | `int \| None` | `None` | 思考 token 预算（SDK 标注 deprecated，建议用 `thinking`） |
| `betas` | `list[SdkBeta]` | `[]` | beta 特性（如 `context-1m-2025-08-07`） |
| `max_turns` | `int \| None` | `None` | 最大对话轮数 |
| `max_budget_usd` | `float \| None` | `None` | USD 预算上限，超出即停 |
| `task_budget` | `TaskBudget \| None` | `None` | API 侧 token 预算（需 task-budgets beta） |
| `agents` | `dict[str, AgentDefinition] \| None` | `None` | 程序化定义的 SubAgent（见 §1.2） |
| `skills` | `list[str] \| Literal["all"] \| None` | `None` | 启用的 Skills（None=不自动配 / "all" / 名单） |
| `plugins` | `list[SdkPluginConfig]` | `[]` | 加载的 plugin（命令/agents/skills/hooks） |
| `setting_sources` | `list[SettingSource] \| None` | `None` | 加载哪些 filesystem 设置层（None=全部 / `[]`=全禁） |
| `settings` | `str \| None` | `None` | 额外 settings JSON 路径（最高优先层） |
| `cwd` | `str \| Path \| None` | `None` | 工作目录 |
| `add_dirs` | `list[str \| Path]` | `[]` | cwd 之外可访问目录 |
| `cli_path` | `str \| Path \| None` | `None` | 自定义 CLI 可执行路径（默认用 bundled） |
| `env` | `dict[str, str]` | `{}` | 传给 CLI 子进程的环境变量 |
| `extra_args` | `dict[str, str \| None]` | `{}` | 透传额外 CLI 参数（None=布尔 flag） |
| `max_buffer_size` | `int \| None` | `None`(≈1MB) | 读取子进程 stdout 的最大缓冲字节 |
| `stderr` | `Callable[[str], None] \| None` | `None` | stderr 回调 |
| `debug_stderr` | `Any` | `sys.stderr` | 已 deprecated，transport 不再读 |
| `user` | `str \| None` | `None` | 关联的用户标识 |
| `include_partial_messages` | `bool` | `False` | 输出流式 partial 消息事件 |
| `include_hook_events` | `bool` | `False` | 输出 hook 生命周期事件 |
| `output_format` | `dict[str, Any] \| None` | `None` | 结构化输出（如 json_schema） |
| `sandbox` | `SandboxSettings \| None` | `None` | 命令执行沙箱隔离 |
| `enable_file_checkpointing` | `bool` | `False` | 文件检查点（支持 `rewind_files()`） |
| `continue_conversation` | `bool` | `False` | 续接 cwd 最近会话（与 resume 互斥） |
| `resume` | `str \| None` | `None` | 恢复指定 session_id 的历史 |
| `session_id` | `str \| None` | `None` | 指定固定 session UUID |
| `fork_session` | `bool` | `False` | resume 时分叉为新 session |
| `session_store` | `SessionStore \| None` | `None` | 外部会话存储 |
| `session_store_flush` | `SessionStoreFlushMode` | `"batched"` | 刷盘策略 batched/eager |
| `load_timeout_ms` | `int` | `60000` | session_store.load 超时 |

### 1.2 `AgentDefinition`（SubAgent 定义，共 13 字段）

| 字段 | 类型 | 默认 | 含义 |
|------|------|------|------|
| `description` | `str` | required | SubAgent 描述 |
| `prompt` | `str` | required | SubAgent system prompt |
| `tools` | `list[str] \| None` | `None` | 工具白名单（None=继承全部；SDK 标注倾向 skills） |
| `disallowedTools` | `list[str] \| None` | `None` | 工具黑名单（0.1.51 #759 恢复） |
| `model` | `str \| None` | `None` | 模型 alias/id（`inherit` 继承父） |
| `skills` | `list[str] \| None` | `None` | 该 SubAgent 可用 Skills |
| `memory` | `Literal['user','project','local'] \| None` | `None` | 内存作用域 |
| `mcpServers` | `list[str \| dict] \| None` | `None` | per-SubAgent MCP 隔离 |
| `initialPrompt` | `str \| None` | `None` | 启动时首条提示 |
| `maxTurns` | `int \| None` | `None` | 该 SubAgent 轮数上限 |
| `background` | `bool \| None` | `None` | 后台运行 |
| `effort` | `EffortLevel \| int \| None` | `None` | per-SubAgent 推理深度 |
| `permissionMode` | `PermissionMode \| None` | `None` | per-SubAgent 权限模式 |

### 1.3 工具注册 API（in-process MCP）

| 入口 | 参数 | 含义 |
|------|------|------|
| `@tool` | `name` / `description` / `input_schema` / `annotations` | 定义单个 MCP 工具 |
| `create_sdk_mcp_server` | `name` / `version="1.0.0"` / `tools` | 把工具打包成 in-process MCP server |
| `ToolAnnotations` | `maxResultSizeChars` / `readOnly` / `destructive` / `openWorld` | 工具注解（结果上限 / 只读 / 破坏性 / 开放世界） |

### 1.4 关键类型别名与嵌套类型

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
| `McpServerConfig` | `McpStdioServerConfig` / `McpSSEServerConfig` / `McpHttpServerConfig` / `McpSdkServerConfig` |

### 1.5 会话 / Session 管理 API（顶层函数，与 Options 无关）

SDK 暴露大量 session API:`list_sessions` / `get_session_info` / `get_session_messages` / `list_subagents` / `get_subagent_messages` / `delete_session` / `fork_session` / `rename_session` / `tag_session`，及 `*_from_store` / `*_via_store` 变体与 `SessionStore` 协议（`append/load/list_sessions/delete/list_subkeys`）。`ClaudeSDKClient` 另有 `set_permission_mode` / `set_model` / `rewind_files` / `reconnect_mcp_server` / `toggle_mcp_server` / `stop_task`。

> **BIMCanvas 全部不用**——持久化与编排由 `.bcp/schemes/{zoneId}/*.json` + git worktree + 自有 `X-Session-Id` 承担（见 §3 缺口表）。

---

## 2. Agent 当前已消费的参数配置

### 2.1 `ClaudeAgentOptions` 消费情况（传入 20 / 45）

| SDK 字段 | 状态 | 取值来源 | 怎么消费 / 位置 |
|---------|------|---------|----------------|
| `system_prompt` | ✅⚙️ | `ConfigBundle.system_prompt`（core-base + domain plugin 两层 `BIMCANVAS.md`）+ 追加项目/工作目录 | `materialize_system_prompt_file()` 落盘 `BIMCANVAS_HOME/cache/system_prompt.window_{seq}.runtime.md`，回传 `SystemPromptFile` dict（绕 Windows 32767 上限）。`main_agent.py:237-246,306` |
| `cwd` | ✅ | `self.working_directory` | 直传。`:307` |
| `max_turns` | 🔧 | `30` | `:308` |
| `model` | ✅⚙️ | 运行时入参 或 `claude.defaultModel`→`modelMapping` 解析 | `:309` |
| `allowed_tools` | ✅⚙️ | `ConfigBundle.tools_allow`（plugin manifest `tools.allow`） | 原样传，空=全开。`:253,310` |
| `disallowed_tools` | ✅⚙️ | `ConfigBundle.tools_deny`（plugin manifest `tools.deny`） | deny 优先。`:254,311` |
| `agents` | ✅⚙️ | `create_subagents()`（base+plugin `agents/*.md`） | `:312`，见 §2.2 |
| `permission_mode` | 🔧 | `"acceptEdits"` | 后端无人值守。`:313` |
| `include_partial_messages` | 🔧 | `True` | 流式必需。`:314` |
| `env` | ✅⚙️ | `claude.env` + 派生 `ANTHROPIC_BASE_URL/API_KEY` + `ANTHROPIC_DEFAULT_*_MODEL` | `:261-269,315` |
| `effort` | ✅⚙️ | 运行时入参 或 `claude.defaultEffort` | `"off"→None`，余直传。`:272,316` |
| `thinking` | ✅⚙️ | 运行时入参 或 `claude.defaultThinking` | `adaptive→Adaptive`，否则 `Disabled`。`:275-279,317` |
| `max_thinking_tokens` | ✅⚙️ | `claude.maxThinkingTokens`（env `MAX_THINKING_TOKENS` 覆盖） | `:318` |
| `mcp_servers` | ✅⚙️ | `ConfigBundle.mcp_servers_spec`（canvas + plugin in-process server） | `:282,319` |
| `strict_mcp_config` | 🔧 | `True` | 防外部 MCP 污染。`:320` |
| `skills` | 🔧 | `"all"` | S2 双写过渡（待 SDK #977）。`:323` |
| `setting_sources` | 🔧 | `[]` | **防 CLAUDE.md 污染关键，勿改回 None**。`:329` |
| `plugins` | ✅⚙️ | `ConfigBundle.active_plugin_paths` | 检查 `.claude-plugin/` → `{type:"local",path}`。`:295-303,330` |
| `max_buffer_size` | 🔧 | `10*1024*1024`(10MB) | 截图缓冲。`:331` |
| `can_use_tool` | 🔧 | `self._auto_approve_tool` | 自动批准；AskUserQuestion 走侧信道。`:332,335-358` |

**未传入的 25 个**:`tools / continue_conversation / resume / session_id / max_budget_usd / fallback_model / betas / permission_prompt_tool_name / cli_path / settings / add_dirs / extra_args / debug_stderr / stderr / hooks / user / include_hook_events / fork_session / sandbox / output_format / enable_file_checkpointing / session_store / session_store_flush / load_timeout_ms / task_budget`（说明见 §3）。

### 2.2 `AgentDefinition` 消费情况（消费 5 / 13）

> `subagents.py:create_subagents()` 把 `loader.py` 解析的 `AgentConfig`（frontmatter）转为 `AgentDefinition`，构造在 `subagents.py:116-122`。

| 字段 | 状态 | 取值 / 逻辑 |
|------|------|-----------|
| `description` | ✅ | frontmatter `description`（必填） |
| `prompt` | ✅ | `.md` 正文，经 `_append_runtime_context()` 注入路径附录（仅 `layout-agent`/`module-relocation-agent`） |
| `tools` | ✅ | 三态:`None`→继承主控 `tools_allow`（空则 `None`=inherit-all）;list→用 `.md` 列表 |
| `disallowedTools` | 🟡 | 仅「继承分支」从 `tools_deny` 深拷贝；「显式自主分支」为 `None`（主控 deny 全局兜底） |
| `model` | ✅ | frontmatter `model`，缺省 `"inherit"` |
| `skills` / `memory` / `mcpServers` / `initialPrompt` / `maxTurns` / `background` / `effort` / `permissionMode` | ⬜ | 未传（见 §3） |

> frontmatter 仅解析 `name / description / tools / model`（`loader._parse_simple_yaml`），其余键忽略。

### 2.3 工具注册与输出侧消费

- **`@builder.tool`**:消费 `name / description / input_schema / annotations`（O5 透传 `ToolAnnotations`）。`load_scene_artifact` 设 `maxResultSizeChars=500_000`（`canvas.py:1171`），其余工具用默认 50K。`readOnly/destructive/openWorld` ⬜ 未显式标注。
- **输出侧（SDK→Agent）**:消息消费循环（`main_agent.py` 约 `:1180-1480`）已消费 `AssistantMessage.content[]`（全 block 类型 + Server* 兜底）、`AssistantMessage.error/model`、`ResultMessage.is_error/subtype/num_turns/total_cost_usd/duration_ms`、`ResultMessage.api_error_status/errors`（S3）、`ResultMessage.usage/model_usage`（W3 cache 埋点 + fallback）、`RateLimitEvent`、`TaskStarted/TaskProgress/TaskNotification`（S4，TaskNotification 仅观察期 log）。

---

## 3. 缺口表（SDK 支持但 Agent 未消费，按价值）

| SDK 项 | 价值 | 现状替代 / 备注 |
|--------|------|----------------|
| `AgentDefinition.mcpServers=[]` | `layout-agent` 物理隔离 canvas MCP | 现靠 .md prompt 软约定（O4 计划，未落地） |
| `AgentDefinition.permissionMode/effort` | per-SubAgent 权限/推理深度定制 | 全继承主控 |
| `ThinkingConfig.display="summarized"` | layout-agent 推理可视化调试 | O3 阶段1 观测中 |
| `fallback_model` | 生产高可用降级 | 无降级 |
| `max_budget_usd` / `task_budget` | 成本/ token 硬上限 | 仅 `max_turns=30` 限流 |
| `user` | Web 多用户会话隔离 | 未注入 |
| `ToolAnnotations.destructive/readOnly` | 删除类工具显式标注 | 隐含处理 |
| `betas=["context-1m-2025-08-07"]` | 超大户型上下文 | 未启用 |
| session API / `session_store` / `resume` / `enable_file_checkpointing` | SDK 自带会话持久化与回滚 | **刻意不用**:`.bcp` + git worktree + 自有 session 已覆盖；回滚靠 git |
| `settings` / `setting_sources!=[]` / `hooks` / `sandbox` | 外部设置/hook/沙箱 | **刻意不用**:`setting_sources=[]` 防污染，隔离靠 worktree+权限 |

---

## 4. 通过 `instance.config.json` 可修改的配置项及用法

### 4.1 文件位置与结构

- **运行时路径**（不在仓库，首次启动自动生成）:`%USERPROFILE%\Documents\BIMCanvas\instance.config.json`
- **模板**:`BIMCanvas.Server/Templates/global-config/instance.config.json`
- **编码**:UTF-8（支持 BOM，`utf-8-sig` 读取）
- **顶层结构**:`{ server, web, agent, ccr }`。**Agent 只读 `agent` 段**（`runtimeProvider / claude / openai / chatgptBackend`）。
- **旧布局回退**:若无 `instance.config.json` 但存在独立 `config.json`，则整份当作 agent 段（过渡兼容）。

> 改完需重启 Agent 生效（`settings` 经 `@lru_cache` 缓存）。`appsettings.json` 不是真源，端口由 `server_config.json`/`instance.config.json server` 段管。

### 4.2 `agent.claude` 段 — Claude runtime 可调项（当前默认 runtime）

```jsonc
"agent": {
  "runtimeProvider": "claude",          // "claude"(默认) | "openai"；可被 env AGENT_RUNTIME_PROVIDER 覆盖
  "claude": {
    "baseUrl": "",                       // 直连模式 ANTHROPIC_BASE_URL；CCR 托管时由网关 env 注入，留空
    "apiKey": "",                        // 直连模式 ANTHROPIC_API_KEY；推荐用 env 而非写此处
    "defaultModel": "opus",              // 只能 opus|sonnet|haiku，且必须存在于 modelMapping
    "defaultEffort": "low",              // low|medium|high|max|xhigh(Opus4.7+)；非法值启动报错
    "defaultThinking": "adaptive",       // off|adaptive；adaptive→扩展思考，off→关闭
    "maxThinkingTokens": 8000,           // 整数=预算；-1 或空=不限制；env MAX_THINKING_TOKENS 覆盖
    "modelMapping": {                    // alias→真实 model id+label；key 只能 opus/sonnet/haiku
      "opus":   { "id": "claude-opus-4-7[1m]", "label": "Opus" },
      "sonnet": { "id": "claude-sonnet-4-6",   "label": "Sonnet" },
      "haiku":  { "id": "claude-haiku-4-5",    "label": "Haiku" }
    },
    "env": {                             // 透传给 Claude CLI 子进程的环境变量（ClaudeAgentOptions.env）
      "CLAUDE_CODE_WORKFLOWS": "1",      // key/value 必须都是字符串；同名会被派生 ANTHROPIC_* 覆盖
      "DISABLE_GROWTHBOOK": "1"
    }
  }
}
```

| 键 | 映射到 SDK | 取值规则 | 校验/解析点 |
|----|-----------|---------|------------|
| `claude.defaultModel` | `ClaudeAgentOptions.model` | `opus`/`sonnet`/`haiku`，须在 `modelMapping` 中 | `settings._resolve_claude_default_model` |
| `claude.modelMapping` | `env ANTHROPIC_DEFAULT_{OPUS,SONNET,HAIKU}_MODEL`（直连模式） | key 限 opus/sonnet/haiku；value 为 `{id,label}` 或字符串 | `_sanitize_claude_model_mapping` + `_apply_model_mapping` |
| `claude.defaultEffort` | `ClaudeAgentOptions.effort` | `low/medium/high/max/xhigh` | `_resolve_claude_effort`（白名单 `_ALLOWED_EFFORTS`） |
| `claude.defaultThinking` | `ThinkingConfig*` | `off`/`adaptive` | `_resolve_claude_thinking` |
| `claude.maxThinkingTokens` | `ClaudeAgentOptions.max_thinking_tokens` | int / 空(=8000) / -1(=不限) | `_resolve_optional_int` |
| `claude.baseUrl` | `env ANTHROPIC_BASE_URL` | 直连模式生效；CCR 托管由 `AGENT_SDK_BASE_URL` 覆盖 | `_load_claude_settings` |
| `claude.apiKey` | `env ANTHROPIC_API_KEY` | 同上（推荐改用环境变量） | `_load_claude_settings` |
| `claude.env` | `ClaudeAgentOptions.env` | dict[str,str]；空 key 报错 | `loader.ensure_agent_config_schema` + `_load_claude_settings` |

**运行时覆盖优先级**:`chat_stream(model/effort/thinking=)` 运行时入参 > `instance.config.json` 默认值。CCR 托管模式（检测到 `AGENT_SDK_API_KEY`/`AGENT_SDK_BASE_URL`）下 `baseUrl/apiKey` 由网关 env 接管，`claude.baseUrl/apiKey` 被忽略。

### 4.3 `agent.openai` / `agent.chatgptBackend` 段 — ⚠️ 已废弃维护

`runtimeProvider="openai"` 路径（含 `openai.*` 与 `chatgptBackend.*`）是为第三方模型另写的并行实现，**已大幅偏离当前架构、不维护**（详见项目 CLAUDE.md §12）。日常仅维护 `claude` 段。

### 4.4 不在 `instance.config.json` 的相关配置

| 配置 | 真源 |
|------|------|
| 端口（server/web/agent） | `instance.config.json` 的 `server` 段 / `server_config.json` |
| 工具权限 `tools.allow/deny` | **plugin manifest** `bimcanvas-plugin.json`（v3.3 起从 config.json 迁出，config 内再写会报错） |
| SubAgent / Skill / MCP 工具 | plugin（core-base 在 `BIMCanvas.Agent/plugins/core-base/`；domain 在独立仓库） |
| 系统提示词 | plugin 的 `BIMCANVAS.md`（core-base + domain 两层叠加） |
| `max_turns / permission_mode / strict_mcp_config / skills / setting_sources / max_buffer_size` | **硬编码**于 `main_agent.py:_create_options`，无配置入口 |

---

## 5. 变更记录

| 日期 | SDK 版本 | 变更 |
|------|---------|------|
| 2026-05-29 | 0.2.87 | 初版。记录 0.1.41→0.2.87 升级后状态:M1/M2/S1/S2/S3/S4/O1/O2/O5 已落地；O3 阶段1观测中；O4 未落地；O6/会话 API 不用。 |

> 维护提示:升级 SDK 后，对照 `types.py` 的 `ClaudeAgentOptions`/`AgentDefinition` 字段增删，更新 §0 版本、§1 全量表、§2 消费表、§5 变更记录；改 `instance.config.json` schema 时同步 §4。
