"""MainAgent - BIMCanvas coordinator using Agent SDK with SubAgent support."""

import asyncio
import json
import logging
import os
import re
from dataclasses import dataclass, field
from typing import Any, AsyncIterator, Awaitable, Callable

import aiohttp

from claude_agent_sdk import (
    ClaudeSDKClient,
    ClaudeAgentOptions,
    AssistantMessage,
    UserMessage,
    ResultMessage,
    SystemMessage,
    TextBlock,
    ThinkingBlock,
    ToolUseBlock,
    ToolResultBlock,
    PermissionResultAllow,
    ToolPermissionContext,
    # SDK 0.2.87 新增类型（WP-1 主控消息层硬化）
    ServerToolUseBlock,
    ServerToolResultBlock,
    RateLimitEvent,
    TaskStartedMessage,
    TaskProgressMessage,
    TaskNotificationMessage,
    # WP-2 M2: SDK 连接异常基类(winerror=206 走 CLIConnectionError 路径)
    CLIConnectionError,
    CLINotFoundError,
)
from claude_agent_sdk.types import (
    ThinkingConfigAdaptive,
    ThinkingConfigDisabled,
    SystemPromptFile,  # WP-2 M2: 走 --system-prompt-file 绕 32767 上限
)

from ..config.settings import get_settings
from .subagents import create_subagents
from .agent_logger import get_agent_logger
from .worktree_manager import WorktreeManager, WorktreeContext
# 组3 改造: 不再硬编码 canvas_mcp; bundle.mcp_servers_spec 动态构造
from ..runtime import ConfigBundle, StreamChunk, build_config_bundle, materialize_system_prompt_file
from ..runtime.main_stream import MainStreamMapper
from ..runtime.launch_context import build_project_bound_context, resolve_launch_context
from .errors import CLICommandLineTooLongError, SystemPromptFileWriteError

logger = logging.getLogger(__name__)

# WP-2 CLAUDECODE: 进程级 flag,确保 WARNING 只打一次(__init__ 内 once-触发)
# SDK 0.1.51 PR #732 起,SDK 已自动剥离 CLAUDECODE env;此 flag 仅提醒"无需手动设"
_claudecode_warned: bool = False

_UNKNOWN_MODEL_VALUES = {"", "unknown"}


@dataclass
class _TurnChannel:
    """单个用户回合的消息通道。

    常驻 _drain_loop 把属于当前活跃回合的 SDK 消息投递到 queue；
    chat / chat_stream 从 queue 消费，直到取到 ResultMessage（或异常）为止。
    queue 中若取到 BaseException 实例，表示常驻 reader 异常退出，消费方应抛出它，
    避免 await queue.get() 永久挂起。
    """

    queue: asyncio.Queue = field(default_factory=asyncio.Queue)


class MainAgent:
    """
    BIMCanvas 主控 Agent（后台常驻）

    职责：
    1. 分析用户需求，理解设计意图
    2. 评估任务复杂度，制定执行计划
    3. 自主决定执行方式：简单问答直接回答，布置任务调用 layout-agent
    4. 整合执行结果，向用户汇报

    架构特点：
    - 使用 ClaudeSDKClient 维持持久连接
    - 通过 agents 参数注册 SubAgent（AgentDefinition）
    - 启用 Task 工具，AI 自主决定何时派发 SubAgent
    - 详细日志输出所有 Agent 活动
    """

    runtime_id = "claude"
    runtime_version = "0.1.0"

    def __init__(
        self,
        project_path: str = None,
        working_directory: str = None,
        window_seq: int = 0,
        verbose: bool = True
    ):
        """
        Initialize the MainAgent.

        Args:
            project_path: 项目根目录（用于定位配置等）
            working_directory: 实际工作目录（文件操作的基准目录）
                              为 None 时等于 project_path
            window_seq: 窗口序号（0=primary, 2+=虚拟窗口）用于日志前缀区分
            verbose: Enable detailed console logging
        """
        self.project_path = project_path
        self.working_directory = working_directory or project_path
        self.window_seq = window_seq
        self.verbose = verbose

        # Host-injected shared configuration bundle
        self._bundle: ConfigBundle | None = None
        self._subagents = {}

        # Agent logger for console output (with window_seq for multi-window prefix)
        self._agent_logger = get_agent_logger("MainAgent", window_seq=self.window_seq)

        # ClaudeSDKClient instance management
        self._client: ClaudeSDKClient | None = None
        self._connected = False
        self._lock = asyncio.Lock()

        # 组3: long-lived aiohttp.ClientSession,供 PluginContext / load_artifact 共享。
        # 在首次 _require_bundle (fallback 路径) 时 lazy 创建;disconnect 时关闭。
        # 注:host 外部通过 configure(bundle) 注入 bundle 时,session 生命周期由 host 自管。
        self._owned_session: aiohttp.ClientSession | None = None

        # State tracking for logging
        self._in_thinking = False
        self._in_response = False
        self._streamed_text = False  # 标记是否已通过流式事件输出文本，避免重复
        self._current_tool_name = None
        self._placeholder_text_suppressed_logged = False

        # SubAgent/ToolCall 状态跟踪（用于 SSE 事件）
        # 支持多个并行 SubAgent：task_tool_use_id → subagent_id
        self._active_subagents: dict[str, str] = {}
        self._tool_call_counter = 0
        self._pending_tool_calls: dict[str, str] = {}  # tool_use_id -> tool_call_id 映射
        # 跟踪每个工具调用所属的 SubAgent：tool_use_id → subagent_id
        self._tool_to_subagent: dict[str, str] = {}
        # Workflow 阶段预声明（Task 页运行态全阶段可视化）：拦截 Workflow tool_use 时解析脚本
        # meta.phases 暂存（key=tool_use_id=block.id），TaskStarted 拿到 task_id 后再推前端。
        # 不依赖闭源 CLI 写的 per-run 脚本副本/wf_*.json（运行态常缺失/runId 错位）。
        self._pending_workflow_meta: dict[str, dict[str, Any]] = {}
        # Workflow 身份标记：拦截 Workflow tool_use 记 tool_use_id；TaskStarted 命中后记 task_id。
        # 进度心跳据此带 isWorkflow 字段——前端统一后台活动灯需要区分"Workflow"与"普通后台 Task"
        # （二者心跳同走 kind=workflow_progress 通道，record 自身无别的判别字段）。
        self._workflow_tool_use_ids: set[str] = set()
        self._workflow_task_ids: set[str] = set()
        # agent 型后台任务 id（Task/Agent 工具发起）：其内部工具执行会被 CLI 登记成独立 task，
        # 心跳归类为 agent-internal 供前端折叠。跨回合常驻（detach 的后台 agent 跑过回合边界）。
        self._bg_agent_task_ids: set[str] = set()
        # 并行工具调用安全的工具名跟踪：tool_use_id → tool_name（解决 _current_tool_name 单值覆盖问题）
        self._tool_name_by_id: dict[str, str] = {}

        # 当前请求的模型（用于 set_model 检查，避免重复发送控制消息）
        self._current_model: str | None = None
        # API 响应的模型（用于日志显示，可能与请求模型名称不同）
        self._response_model: str | None = None

        # Worktree 管理器（用于并行布置）
        self._worktree_manager: WorktreeManager | None = None
        self._runtime_context: dict[str, str] | None = None
        # 跨回合保留的 runtime context，仅在 disconnect 时清空。
        # 后台任务（Workflow）完成的带外推送靠它定位目标窗口/会话。
        self._last_runtime_context: dict[str, str] | None = None

        # 常驻消息排空（SDK 0.2.87 后台 Workflow 适配）：
        # _drain_loop 独占 client.receive_messages()，按是否有活跃回合分发消息：
        #   有活跃回合 → 投递到 turn.queue（chat/chat_stream 消费）
        #   无活跃回合 → 走 _handle_background_message（后台任务完成等带外消息）
        # 由此 receive_response() 的"首个 ResultMessage 即停"语义被移出回合路径，
        # 后台续写回合的多余 ResultMessage 不再污染下一回合（修复消息错位/吞答 bug）。
        self._drain_task: asyncio.Task | None = None
        self._active_turn: _TurnChannel | None = None
        # 后台任务完成推送回调（host 注入；Claude 路径用，OpenAI/protocol 路径不设即 no-op）
        self._background_push: Callable[[dict[str, Any]], Awaitable[None]] | None = None
        # 后台 Workflow 进度推送回调（host 注入）：detach 后 workflow 进度走带外通道实时推前端
        self._background_progress_push: Callable[[dict[str, Any]], Awaitable[None]] | None = None
        # 后台 Workflow 完成：收集"主控原生总结回合"的状态。
        # CLI 收到 <task-notification> 会把它当 user 消息注入会话、让主控自动唤醒生成总结回合；
        # drain loop 据此把【原生总结文本】投递给前端，而非丢弃（修复 detach 后总结被吞）。
        self._bg_completion_pending: dict[str, Any] | None = None
        # 已向前端投递过终态的 task_id（防"嗅探注入通知"与 TaskNotificationMessage 双路双发）
        self._bg_completed_emitted: set[str] = set()
        self._bg_summary_parts: list[str] = []
        # 后台原生总结回合内是否出现过工具调用（仅作日志诊断）。
        # 注意：SDK 一个 response 只有一个 ResultMessage（response 内多次 tool-call 是
        # AssistantMessage↔UserMessage 往返、不产生额外 RM）。R4-3 起收口判据已由本标记改为
        # "_bg_summary_parts 非空"（见 _handle_background_message 的 ResultMessage 分支），
        # 本字段保留仅供诊断、不再参与收口决策。
        self._bg_round_had_tool: bool = False
        # T2：后台原生总结回合的完整 envelope 序列（thinking/tool/text），随 background_task.completed
        # 的 events 字段一次性投递前端，由 applyNormalizedEventToMessage 渲染成完整一条回合（不再只剩 text）。
        self._bg_turn_events: list[dict[str, Any]] = []
        self._bg_tool_names: dict[str, str] = {}   # tool_use_id → tool_name（跨 Assistant/User 消息配对工具完成）
        self._bg_stream_mapper: MainStreamMapper | None = None

        # WP-2 CLAUDECODE: 进程级一次性 WARNING(SDK 0.1.51 PR #732 已自动剥离 CLAUDECODE env)
        global _claudecode_warned
        if not _claudecode_warned and os.environ.get("CLAUDECODE") == "1":
            logger.warning(
                "检测到 CLAUDECODE=1 环境变量;SDK 0.1.51 PR #732 已自动剥离该 env,"
                "无需在 ClaudeAgentOptions(env=...) 里手动设 CLAUDECODE 为空字符串。"
            )
            _claudecode_warned = True

    @property
    def is_connected(self) -> bool:
        return self._connected

    def set_runtime_context(self, runtime_context: dict[str, str] | None) -> None:
        """Set host-provided runtime context for the current turn."""
        self._runtime_context = dict(runtime_context) if runtime_context else None
        # 同步刷新跨回合保留的副本，供后台任务带外推送定位窗口（回合结束 clear 后仍可用）
        if self._runtime_context:
            self._last_runtime_context = dict(self._runtime_context)

    def clear_runtime_context(self) -> None:
        """Clear host-provided runtime context after the current turn."""
        self._runtime_context = None

    def set_background_push(
        self, callback: Callable[[dict[str, Any]], Awaitable[None]] | None
    ) -> None:
        """注入后台任务完成的带外推送回调（host → runtime_store 发布）。"""
        self._background_push = callback

    def set_background_progress_push(
        self, callback: Callable[[dict[str, Any]], Awaitable[None]] | None
    ) -> None:
        """注入后台 Workflow 进度的带外推送回调（host → runtime_store 发布，只实时不落盘）。"""
        self._background_progress_push = callback

    @staticmethod
    def _normalize_response_model(model: Any) -> str | None:
        normalized = str(model or "").strip()
        if normalized.lower() in _UNKNOWN_MODEL_VALUES:
            return None
        return normalized

    def _capture_response_model(self, model: Any) -> None:
        normalized = self._normalize_response_model(model)
        if normalized:
            self._response_model = normalized

    def _capture_stream_event_model(self, event: dict) -> None:
        event_message = event.get("message")
        if isinstance(event_message, dict):
            self._capture_response_model(event_message.get("model"))

    def _completion_model_stamp(self) -> str | None:
        response_model = self._normalize_response_model(self._response_model)
        if response_model:
            return response_model

        current_model = self._normalize_response_model(self._current_model)
        if current_model:
            return f"requested:{current_model}"
        return None

    def configure(self, bundle: ConfigBundle) -> None:
        self._bundle = bundle
        self._subagents = create_subagents(
            bundle.shared_agents,
            main_allow=bundle.tools_allow,
            main_deny=bundle.tools_deny,
            project_path=self.project_path,
            working_directory=self.working_directory,
        )

    async def resume_interaction_stream(self, *args, **kwargs):
        raise NotImplementedError(
            "Claude runtime does not support host-driven interaction resume; "
            "pause/resume happens inside can_use_tool within the original chat_stream coroutine."
        )
        if False:  # pragma: no cover — keep function an async generator for typing
            yield

    def _require_bundle(self) -> ConfigBundle:
        if self._bundle is None:
            # 组3: lazy 创建 long-lived aiohttp session,供 load_artifact / plugin 工具共享
            if self._owned_session is None:
                # 识图/截图并发稳健化(同 factory.create_agent):扩连接上限+keep-alive+DNS 缓存,保并行。
                self._owned_session = aiohttp.ClientSession(
                    connector=aiohttp.TCPConnector(
                        limit=64, limit_per_host=32, ttl_dns_cache=300, enable_cleanup_closed=True
                    )
                )
            # 接线总开关:此懒加载路径(未经 factory.create_agent 预 configure)同样用
            # self.project_path 构造 ProjectBound,杜绝无参 build 得 projectless。
            lc = (
                build_project_bound_context(self.project_path)
                if self.project_path
                else resolve_launch_context()
            )
            self.configure(build_config_bundle(launch_context=lc, session=self._owned_session))
        assert self._bundle is not None
        return self._bundle

    # ─────────────────────────────────────────────────────
    # Configuration
    # ─────────────────────────────────────────────────────

    def _create_options(self, effort: str = None, thinking: str = None, model: str = None,
                        resume_session_id: str = None) -> ClaudeAgentOptions:
        """
        Create agent options with SubAgent support.

        Args:
            effort: 推理深度 ("low"/"medium"/"high"/"max")，None 使用默认配置
            thinking: 扩展思考开关 ("off"/"adaptive")，None 使用默认配置
            model: 模型名称
            resume_session_id: 续聊时传 SDK 原生 session_id，SDK 读 transcript 重建上下文
                （fork_session=False 续同一会话）；None=全新会话。
        """
        if not model:
            raise ValueError("Model is required")

        settings = get_settings()
        bundle = self._require_bundle()

        # 从配置加载系统提示词和工具权限
        system_prompt = bundle.system_prompt

        # 追加项目路径和工作目录到 system prompt，让 AI 知道 MCP 参数和文件操作基准。
        project_path = self.project_path or self.working_directory or "（unknown）"
        working_directory = self.working_directory or self.project_path or "（unknown）"
        system_prompt = system_prompt + f"\n\n项目路径: {project_path}\n工作目录: {working_directory}"

        # 追加 active domain plugin 绝对根，供主控构造 Workflow scriptPath 绝对路径。
        # SDK 把相对 scriptPath 按 cwd=项目目录解析（在项目目录下找不到插件 workflows/，报
        # "Workflow script file not found"），故 plugin BIMCANVAS.md 要求
        # scriptPath = {此处注入的插件根}/workflows/*.workflow.js。统一正斜杠，避免 Windows
        # 反斜杠在主控拼出的 JSON scriptPath 里成为非法转义。None（无 domain plugin）时不注入。
        if bundle.active_plugin_root is not None:
            plugin_root_posix = str(bundle.active_plugin_root).replace("\\", "/")
            system_prompt = system_prompt + f"\n插件根: {plugin_root_posix}"

        # WP-2 M2.1: 落盘到 BIMCANVAS_HOME/.runtime/system-prompt/system_prompt.window_{seq}.runtime.md,
        # 走 SDK --system-prompt-file(0.1.51+)绕过 Windows CreateProcess 32767 字符上限。
        system_prompt_file = materialize_system_prompt_file(system_prompt, self.window_seq)

        # 工具权限重设计 v3.2 §7.1 / §7.2:
        # - bundle.tools_allow 原样传给 SDK (空 list = SDK 全开)
        # - bundle.tools_deny 原样传给 SDK (deny 优先于 allow,跟随 SDK 语义)
        # - 不再自动合入 mcp_tool_names / Skill 等隐式工具,plugin MCP 工具
        #   需在 config.json 显式列出
        allowed_tools = bundle.tools_allow
        disallowed_tools = bundle.tools_deny

        # 构建自定义环境变量（用于 Agent SDK 独立配置）
        # 顺序:先填用户在 config.json claude.env 中声明的自定义变量(如
        # CLAUDE_CODE_WORKFLOWS / DISABLE_GROWTHBOOK 等 Claude CLI 特性开关),
        # 再用 baseUrl / apiKey 派生的 ANTHROPIC_* 覆盖,避免用户在 env 里误塞同名 key
        # 与专门字段冲突。SDK 内部最终合并顺序为:os.environ → custom_env → SDK 内置版本号。
        custom_env: dict[str, str] = {}
        if settings.extra_env:
            custom_env.update(settings.extra_env)
        if settings.base_url:
            custom_env["ANTHROPIC_BASE_URL"] = settings.base_url
        if settings.anthropic_api_key:
            custom_env["ANTHROPIC_API_KEY"] = settings.anthropic_api_key
        # ANTHROPIC_DEFAULT_*_MODEL 由 _apply_model_mapping() 设置到 os.environ，
        # Agent SDK 的 env 参数是合并模式（{**os.environ, **custom_env}），自动继承。

        # effort: "off"→None, 其他直传
        sdk_effort = None if effort == "off" else (effort or settings.default_effort)
        # thinking: "off"→ThinkingConfigDisabled（显式告知 CLI 关闭扩展思考）
        #           "adaptive"→ThinkingConfigAdaptive
        thinking_val = thinking or settings.default_thinking
        if thinking_val == "adaptive":
            sdk_thinking = ThinkingConfigAdaptive(type="adaptive")
        else:
            sdk_thinking = ThinkingConfigDisabled(type="disabled")

        # === MCP 服务器配置 (组3 改造: 动态从 bundle 拿) ===
        mcp_servers_spec = dict(bundle.mcp_servers_spec)
        self._agent_logger._print(
            f"[MCP] MCP servers registered: {list(mcp_servers_spec.keys())}, "
            f"tools={len(bundle.mcp_tool_names)}"
        )
        if bundle.diagnostics:
            for diag in bundle.diagnostics:
                self._agent_logger._print(f"[Bundle] {diag}")

        # === Plugin 机制加载 Skills (组3 改造: 遍历 active_plugin_paths) ===
        # BIMCANVAS_HOME 本身就是 Plugin 目录(core-base / 旧布局 base);active plugin root
        # 是 domain plugin 目录。两者都通过 SDK plugins 数组注册,SDK 自动扫 skills 注入 reminder。
        # 独立于 setting_sources,彻底避免 CLAUDE.md 污染 (README 开发难点 #4)
        plugins = []
        for plugin_path in bundle.active_plugin_paths:
            if (plugin_path / ".claude-plugin").exists():
                plugins.append({"type": "local", "path": str(plugin_path)})
                self._agent_logger._print(f"[Plugin] 已注册: {plugin_path.name} ({plugin_path})")
            else:
                self._agent_logger.log_warning(
                    f"[Plugin] 跳过 (缺 .claude-plugin/): {plugin_path}"
                )

        return ClaudeAgentOptions(
            system_prompt=system_prompt_file,      # WP-2 M2: SystemPromptFile dict,走 --system-prompt-file 绕 32767 上限
            cwd=self.working_directory,
            resume=resume_session_id,              # 续聊：续指定 SDK session;None=新会话。fork_session 默认 False=续同一 transcript
            max_turns=30,
            model=model,
            allowed_tools=allowed_tools,           # 工具权限 v3.2: bundle.tools_allow 原样;空 list = SDK 全开
            disallowed_tools=disallowed_tools,     # 工具权限 v3.2: bundle.tools_deny 原样;deny 优先
            agents=self._subagents,
            permission_mode="acceptEdits",
            include_partial_messages=True,
            env=custom_env,                        # Agent SDK 独立环境变量
            effort=sdk_effort,                     # SDK 原生（0.1.36+）
            thinking=sdk_thinking,                 # SDK 原生（0.1.36+）
            max_thinking_tokens=settings.max_thinking_tokens,  # thinking 预算上限（None=不限制）
            mcp_servers=mcp_servers_spec,          # 组3: bundle.mcp_servers_spec 动态构造 (canvas + active plugin)
            strict_mcp_config=True,                # WP-2 O1: 只用代码传入的 mcp_servers,不被外部 settings 污染
            # WP-2 S2 双写过渡期:plugin manifest 的 "Skill" literal 暂保留,
            # 待 SDK issue #977 close 后清理
            skills="all",
            # 必须 [] 而非 None: SDK 0.1.53 修了"误传空串"bug 后, None=不传 --setting-sources flag
            # → CLI 默认加载 user+project (CLAUDE.md/Skills/MCP/agents 全注入污染); [] 才是
            # CHANGELOG 0.1.60 #822 钦定的"显式禁用全部 filesystem discovery"信号。
            # Plugin/Skill 通过 plugins=[...] 走 --plugin-dir, 与 setting_sources 完全正交。
            # 详见 README §"开发难点 #4 — CLAUDE.md 污染"。
            setting_sources=[],
            plugins=plugins,                       # ✅ 通过 Plugin 机制加载 Skills
            max_buffer_size=10 * 1024 * 1024,      # 10MB — 截图 ImageContent 需要足够缓冲区（默认仅 1MB）
            can_use_tool=self._auto_approve_tool,  # Agent 后端无人值守，自动批准所有工具调用
        )

    async def _auto_approve_tool(
        self, tool_name: str, tool_input: dict, context: ToolPermissionContext
    ) -> PermissionResultAllow:
        """Agent 后端模式：自动批准所有工具调用，AskUserQuestion 走侧信道等待用户回答。"""

        if tool_name == "AskUserQuestion":
            from ..server.http_server import request_user_question
            questions = tool_input.get("questions", [])
            if self.verbose:
                self._agent_logger.log_info(
                    f"[Permission] AskUserQuestion: {len(questions)} questions, forwarding to Web"
                )
            answers = await request_user_question(
                questions,
                runtime_context=dict(self._runtime_context or {}),
            )
            return PermissionResultAllow(updated_input={
                **tool_input,
                "answers": answers
            })

        if self.verbose:
            self._agent_logger.log_info(f"[Permission] 自动批准工具: {tool_name}")
        return PermissionResultAllow()

    # ─────────────────────────────────────────────────────
    # Error Filtering
    # ─────────────────────────────────────────────────────

    # 可恢复错误的模式匹配（环境特有噪音，SDK 无法结构化识别）
    _RECOVERABLE_ERROR_PATTERNS = [
        r"cygpath.*fatal error",      # Git Bash cygpath 错误
        r"add_item.*failed.*errno",   # Git Bash 内部错误
        r"EBUSY.*resource busy",      # 文件锁定
    ]
    _PLACEHOLDER_ASSISTANT_TEXTS = {
        "(no content)",
        "[no content]",
    }

    def _classify_tool_error(self, error_message: str) -> str:
        """分类工具错误：recoverable（已知可忽略）或 blocking（需通知前端）。"""
        for pattern in self._RECOVERABLE_ERROR_PATTERNS:
            if re.search(pattern, error_message, re.IGNORECASE):
                return "recoverable"
        return "blocking"

    def _strip_tool_error_tags(self, text: str) -> str:
        """剥离 <tool_use_error> XML 标签，保留干净文本。

        错误分类由 tool_result 事件的 is_error 字段处理。
        """
        return re.sub(r'<tool_use_error>[\s\S]*?</tool_use_error>', '', text)

    @classmethod
    def _is_placeholder_assistant_text(cls, text: str | None) -> bool:
        """判断 assistant 文本是否为应抑制的占位内容。"""
        if text is None:
            return True

        trimmed = text.strip()
        if not trimmed:
            return True

        return trimmed.lower() in cls._PLACEHOLDER_ASSISTANT_TEXTS

    @classmethod
    def _normalize_visible_content(
        cls,
        text: str | None,
        *,
        preserve_blank: bool = False,
    ) -> str | None:
        """归一化 assistant 可见内容，过滤占位内容。

        流式 delta 中的单独空格或换行是 Markdown 语义的一部分，不能当作空内容丢弃。
        """
        if text is None:
            return None
        if text == "":
            return None
        trimmed = text.strip()
        if not trimmed:
            return text if preserve_blank else None
        if trimmed.lower() in cls._PLACEHOLDER_ASSISTANT_TEXTS:
            return None
        return text

    @classmethod
    def _normalize_assistant_text(
        cls,
        text: str,
        *,
        preserve_blank: bool = False,
    ) -> str | None:
        """归一化 assistant 文本，过滤占位内容，保留真实正文。剥离 tool_use_error 标签后判断。"""
        cleaned = re.sub(r'<tool_use_error>[\s\S]*?</tool_use_error>', '', text)
        if cleaned != text and not cleaned.strip():
            return None
        return cls._normalize_visible_content(cleaned, preserve_blank=preserve_blank)

    def _filter_assistant_text(self, text: str, *, preserve_blank: bool = False) -> str | None:
        """实例级过滤，附带一次性兼容日志。"""
        normalized = self._normalize_assistant_text(text, preserve_blank=preserve_blank)
        if normalized is None:
            cleaned = self._strip_tool_error_tags(text).strip()
            if cleaned and not self._placeholder_text_suppressed_logged and self.verbose:
                self._agent_logger.log_info("兼容层已抑制占位 assistant 文本")
                self._placeholder_text_suppressed_logged = True
        return normalized

    def _pop_tool_tracking(self, tool_use_id: str | None) -> tuple[str | None, str | None]:
        """Pop tool tracking state and return (toolCallId, subAgentId)."""
        if not tool_use_id:
            return None, None
        tool_call_id = self._pending_tool_calls.pop(tool_use_id, None)
        subagent_id = self._tool_to_subagent.pop(tool_use_id, None)
        return tool_call_id, subagent_id

    def _resolve_tool_result_state(
        self,
        *,
        tool_name: str,
        result: Any,
        is_error: bool,
        output_limit: int,
    ) -> tuple[bool, str, str | None, str | None, str | None]:
        """Normalize tool/subagent result payloads across all completion paths."""
        result_text = str(result) if result is not None else ""
        output_text = result_text[:output_limit] if result_text and not is_error else ""
        error_message = None
        error_type = None
        hidden_message = None

        if is_error:
            classified = self._classify_tool_error(result_text) if result_text else "blocking"
            error_type = classified
            if classified == "recoverable":
                hidden_message = result_text or "Tool execution failed."
                output_text = ""
                is_error = False
                if self.verbose:
                    self._agent_logger.log_warning(f"工具调用可恢复错误: {hidden_message[:200]}")
            else:
                error_message = result_text or "Tool execution failed."
                output_text = ""
                if self.verbose:
                    self._agent_logger.log_error(f"工具调用失败 ({tool_name}): {error_message[:200]}")

        return (not is_error), output_text, error_message, error_type, hidden_message

    def _build_tool_completion_chunk(
        self,
        *,
        tool_use_id: str | None,
        tool_name: str,
        result: Any,
        is_error: bool,
    ) -> StreamChunk:
        success, output_text, error_message, error_type, hidden_message = self._resolve_tool_result_state(
            tool_name=tool_name,
            result=result,
            is_error=is_error,
            output_limit=1000,
        )
        tool_call_id, subagent_id = self._pop_tool_tracking(tool_use_id)
        return StreamChunk(
            type="tool_call_complete",
            subagent_id=subagent_id,
            tool_call_id=tool_call_id or f"tc-{self._tool_call_counter}",
            tool_output=output_text,
            success=success,
            error=error_message,
            error_type=error_type,
            hidden_content=hidden_message,
        )

    def _build_subagent_completion_chunk(
        self,
        *,
        subagent_id: str,
        tool_name: str,
        result: Any,
        is_error: bool,
    ) -> StreamChunk:
        success, summary_text, error_message, error_type, hidden_message = self._resolve_tool_result_state(
            tool_name=tool_name,
            result=result,
            is_error=is_error,
            output_limit=500,
        )
        return StreamChunk(
            type="subagent_complete",
            subagent_id=subagent_id,
            content=summary_text,
            success=success,
            error=error_message,
            error_type=error_type,
            hidden_content=hidden_message,
        )

    # ─────────────────────────────────────────────────────
    # Connection Management
    # ─────────────────────────────────────────────────────

    async def connect(self, effort: str = None, thinking: str = None, model: str = None,
                      resume_session_id: str = None) -> None:
        """
        Establish persistent connection.

        Args:
            effort: 推理深度 ("low"/"medium"/"high"/"max")，None 使用默认配置
            thinking: 扩展思考开关 ("off"/"adaptive")，None 使用默认配置
            model: 模型名称；首次连接必须提供，后续可复用当前模型
            resume_session_id: 续聊时传 SDK 原生 session_id（切换/恢复历史对话用）；None=新会话
        """
        async with self._lock:
            if self._connected:
                return
            resolved_model = model or self._current_model
            if not resolved_model:
                raise ValueError("Model is required before establishing the first connection")

            options = self._create_options(effort, thinking, resolved_model, resume_session_id)
            # resume 时先把已知的 SDK session id 记下;否则等首个 ResultMessage 捕获。
            self._sdk_session_id = resume_session_id

            # 调试日志：打印实际使用的配置（使用 _agent_logger 确保带窗口前缀）
            tools_display = options.allowed_tools if options.allowed_tools else "默认全开"
            deny_display = options.disallowed_tools if options.disallowed_tools else "无"
            base_url_display = options.env.get("ANTHROPIC_BASE_URL", "默认端点") if options.env else "默认端点"
            effort_display = options.effort or "未设置"
            thinking_display = options.thinking.get("type", "unknown") if options.thinking else "disabled"
            self._agent_logger._print(f"[MainAgent] ========== 配置信息 ==========")
            self._agent_logger._print(f"[MainAgent] 模型: {options.model}")
            self._agent_logger._print(f"[MainAgent] Base URL: {base_url_display}")
            self._agent_logger._print(f"[MainAgent] effort: {effort_display}")
            self._agent_logger._print(f"[MainAgent] thinking: {thinking_display}")
            thinking_tokens_display = options.max_thinking_tokens if options.max_thinking_tokens else "无限制"
            self._agent_logger._print(f"[MainAgent] thinking token 预算: {thinking_tokens_display}")
            self._agent_logger._print(f"[MainAgent] 允许工具: {tools_display}")
            self._agent_logger._print(f"[MainAgent] 禁止工具: {deny_display}")
            self._agent_logger._print(f"[MainAgent] 项目路径: {self.project_path}")
            self._agent_logger._print(f"[MainAgent] 工作目录: {self.working_directory}")
            self._agent_logger._print(f"[MainAgent] ================================")

            # 调试日志：输出 SubAgent 加载信息
            if self._subagents:
                self._agent_logger._print(f"[MainAgent] SubAgents loaded: {list(self._subagents.keys())}")
                for name, agent_def in self._subagents.items():
                    self._agent_logger._print(f"  - {name}: {len(agent_def.prompt)} chars")
            else:
                self._agent_logger._print(f"[MainAgent] 警告: 未加载任何 SubAgent")

            self._client = ClaudeSDKClient(options)
            try:
                await self._client.connect()
            except CLIConnectionError as e:
                # WP-2 M2.3: winerror=206 (Windows ERROR_FILENAME_EXCED_RANGE) 走 CLIConnectionError 父类路径
                # —— SDK 把 FileNotFoundError 包成 CLINotFoundError,其他 Exception(含 winerror=206)
                # 包成 CLIConnectionError(父类)。catch 父类同时覆盖两条;判定 __cause__ 后区分。
                cause = e.__cause__
                if isinstance(cause, OSError) and getattr(cause, "winerror", None) == 206:
                    raise CLICommandLineTooLongError() from e
                raise
            self._connected = True
            self._current_model = resolved_model
            # 启动常驻消息排空任务（必须在 client 同一 async 上下文内创建，见 SDK caveat）
            self._active_turn = None
            self._drain_task = asyncio.create_task(self._drain_loop())
            if self.verbose:
                self._agent_logger.log_info(f"Connected to project: {self.project_path or 'default'}")

    def get_sdk_session_id(self) -> str | None:
        """当前 SDK 原生 session_id（首个 ResultMessage 后可用 / resume 连接即可用）。续聊持久化用。"""
        return getattr(self, "_sdk_session_id", None)

    async def disconnect(self) -> None:
        """Disconnect from the agent with force-kill fallback."""
        async with self._lock:
            # 先停常驻排空任务，避免它在 client 关闭时撞上 receive_messages 异常
            if self._drain_task is not None:
                self._drain_task.cancel()
                try:
                    await self._drain_task
                except asyncio.CancelledError:
                    pass
                except Exception as e:
                    logger.warning(f"Drain task await error during disconnect: {e}")
                finally:
                    self._drain_task = None
            # drain 已停，若仍有回合在 await queue.get()，投终止异常唤醒它避免挂死
            self._fail_active_turn(CLIConnectionError("Agent disconnected"))
            self._active_turn = None
            # N1 兜底：断开前 flush 残留后台 pending（趁 _last_runtime_context 未清，仍能定位窗口）
            await self._flush_pending_background()
            self._last_runtime_context = None

            if self._client and self._connected:
                try:
                    await self._client.disconnect()
                except Exception as e:
                    logger.warning(f"SDK disconnect error: {e}")
                    # disconnect 失败，强制杀掉 claude.exe 子进程（释放 CWD 文件锁）
                    await self._force_kill_subprocess()
                finally:
                    self._connected = False
                    self._client = None
                    logger.info(f"MainAgent disconnected for project: {self.project_path}")

            # 组3: 关闭 long-lived aiohttp session (R4 缓解,防 plugin 工具用的 session 泄漏)
            if self._owned_session is not None and not self._owned_session.closed:
                try:
                    await self._owned_session.close()
                    logger.info("MainAgent owned aiohttp.ClientSession closed")
                except Exception as e:
                    logger.warning(f"aiohttp session close error: {e}")
                finally:
                    self._owned_session = None

    async def _force_kill_subprocess(self) -> None:
        """强制杀掉 claude.exe 子进程（disconnect 失败时的 fallback）"""
        try:
            transport = getattr(self._client, '_transport', None)
            process = getattr(transport, '_process', None) if transport else None
            if process and process.returncode is None:
                process.kill()
                try:
                    await process.wait()
                except Exception:
                    pass
                logger.info("Force-killed claude.exe subprocess")
        except Exception as e:
            logger.error(f"Force-kill subprocess failed: {e}")

    # ─────────────────────────────────────────────────────
    # 常驻消息排空（demux）：回合消息 vs 后台带外消息
    # ─────────────────────────────────────────────────────

    async def _drain_loop(self) -> None:
        """独占 client.receive_messages()，把消息分发到活跃回合或后台处理器。

        这是修复"后台 Workflow 完成消息错位/吞答"的核心：回合不再各自调用
        receive_response()（其语义是"首个 ResultMessage 即停"），而由本任务统一读流。
        - 有活跃回合：消息进 turn.queue；ResultMessage 标记回合结束并清空 _active_turn。
        - 无活跃回合：交给 _handle_background_message（后台任务通知 + 续写 ResultMessage 丢弃）。
        """
        try:
            async for message in self._client.receive_messages():
                turn = self._active_turn
                if turn is not None:
                    turn.queue.put_nowait(message)
                    if isinstance(message, ResultMessage):
                        self._active_turn = None
                else:
                    try:
                        await self._handle_background_message(message)
                    except Exception as e:
                        logger.warning(f"Background message handling error: {e}")
        except asyncio.CancelledError:
            raise
        except Exception as e:
            # reader 异常退出：唤醒等待中的回合，避免 chat_stream 永久挂起
            logger.warning(f"Drain loop terminated with error: {e}")
            self._fail_active_turn(e)
        else:
            # receive_messages() 正常结束（stream 关闭）：同样唤醒等待中的回合
            self._fail_active_turn(
                CLIConnectionError("SDK message stream closed unexpectedly")
            )

    def _fail_active_turn(self, exc: BaseException) -> None:
        """把异常投递给当前活跃回合的队列，让其消费方抛出（防止挂死）。"""
        turn = self._active_turn
        if turn is not None:
            self._active_turn = None
            try:
                turn.queue.put_nowait(exc)
            except Exception:
                pass

    async def _iter_turn_messages(self, turn: _TurnChannel) -> AsyncIterator[Any]:
        """从回合队列消费消息，直到 ResultMessage（含）为止；遇异常对象则抛出。"""
        while True:
            message = await turn.queue.get()
            if isinstance(message, BaseException):
                raise message
            yield message
            if isinstance(message, ResultMessage):
                return

    async def _handle_background_message(self, message: Any) -> None:
        """处理无活跃回合时到达的带外消息（后台 Workflow 完成 + 主控原生总结回合）。

        关键：CLI 收到 <task-notification> 会注入会话并让主控**原生自动唤醒生成一条总结回合**
        （THINK + AssistantMessage 文本 + ResultMessage）。本方法的职责是把这条**原生总结**收集起来、
        在其收尾时经带外通道投递给前端——而不是把它当噪音丢弃（那正是 detach 后"总结被吞"的根因）。
        """
        if isinstance(message, TaskNotificationMessage):
            if self.verbose:
                self._agent_logger.log_info(
                    f"[TaskNotification] task_id={message.task_id}, status={message.status}, "
                    f"output_file={message.output_file}, "
                    f"summary_len={len(message.summary or '')} (background)"
                )
            if self._bg_completion_pending is not None:
                # N1 防覆写：总结槽归首任务，不覆写身份；但并行完成的任务终态不能丢——
                # 立即裸投递 completed（无富总结），否则前端卡片永久 running（2026-06-12 实测）。
                if self.verbose:
                    self._agent_logger.log_info(
                        f"[Background] pending 占用中，并行完成通知裸投递 "
                        f"(keep task_id={self._bg_completion_pending.get('taskId')}, "
                        f"bare task_id={message.task_id}, status={message.status})"
                    )
                await self._emit_bare_background_completion(
                    message.task_id, str(message.status),
                    summary=message.summary or "",
                    output_file=message.output_file or None,
                    sdk_session_id=message.session_id,
                )
            else:
                # 记录待汇报状态，开始收集随后到达的【原生总结回合】文本；
                # 不在此处推送（TaskNotification.summary 只是标题，真正内容由原生总结回合给出）。
                self._bg_completion_pending = {
                    "taskId": message.task_id,
                    "status": str(message.status),
                    "outputFile": message.output_file or None,
                    "sdkSessionId": message.session_id,
                    "fallback": message.summary or "",
                }
                self._bg_summary_parts = []
                self._bg_round_had_tool = False
                # T2：复位本次后台回合的 envelope 缓冲 + 工具名映射 + per-turn mapper
                self._bg_turn_events = []
                self._bg_tool_names = {}
                ctx = self._last_runtime_context or {}
                self._bg_stream_mapper = MainStreamMapper(
                    session_id=ctx.get("sessionId") or "",
                    turn_id=f"bgtask:{message.task_id}",
                )
                # 复位日志状态位，让随后的原生总结回合经 _process_message 干净地打印到 Server 日志
                self._in_thinking = False
                self._in_response = False
                if self.verbose:
                    self._agent_logger.log_info("[Background] ↓ 主控原生完成总结回合（自动唤醒）")
        elif isinstance(message, AssistantMessage) and self._bg_completion_pending is not None:
            # 原生总结/绕行回合：可能多轮 agentic（每 tool-call 一个 ResultMessage）。
            # 记录本轮是否含工具调用（方案①据此判定收口）；复用正常日志路径（_process_message
            # 打印 THINK/AI/工具调用），同时返回文本块内容用于投递（工具/思考块不计入文本）。
            if any(isinstance(b, ToolUseBlock) for b in message.content):
                self._bg_round_had_tool = True
            text = self._process_message(message)
            if text:
                self._bg_summary_parts.append(text)
            self._collect_bg_turn_events(message)   # T2：收 thinking/tool_use/text 的 envelope 序列
        elif isinstance(message, ResultMessage):
            if self._bg_completion_pending is None:
                # 启动回合自身的尾随 ResultMessage（detach 已提前结束回合）等 —— 丢弃即可
                if self.verbose:
                    self._agent_logger.log_info("[Background] discarded out-of-turn ResultMessage")
            else:
                # R4-3 收口判据：had_tool → content 非空。
                # SDK 一个 response 只有一个 ResultMessage（response 内多次 tool-call 是
                # AssistantMessage↔UserMessage 往返、不产生额外 RM；旧注释"每 tool-call 一个 RM"是错的）。
                # ResultMessage = 该 response 完全结束 = 此前所有 AssistantMessage（含汇报文本）已到达收齐，
                # 故此刻合并 emit 的文本必然完整不截断。
                content = "\n".join(self._bg_summary_parts).strip()
                if not content:
                    # content 为空 = detach 启动回合的尾随 RM / 主控纯工具无文本响应：汇报文本尚未到达。
                    # 不收口、保留 pending 继续收集（替代原 had_tool 中间轮分支的"防误清丢汇报"职责），
                    # 由兜底 flush（下次前台回合 / disconnect）兜底。
                    self._bg_round_had_tool = False
                    if self.verbose:
                        self._agent_logger.log_info(
                            "[Background] 无汇报文本的 ResultMessage → 保持收集，不收口"
                        )
                else:
                    # 已收集到汇报文本（含"汇报+截图同轮"，had_tool=True 也照样收口）→ 把跨多轮收集的
                    # 全部文本合并后经带外通道投递前端（落 history + 实时 SSE）。
                    if self.verbose and self._in_response:
                        self._agent_logger.log_response_end()
                        self._in_response = False
                    pending = self._bg_completion_pending
                    self._bg_completion_pending = None
                    self._bg_summary_parts = []
                    self._bg_round_had_tool = False
                    if pending.get("taskId"):
                        self._bg_completed_emitted.add(pending["taskId"])  # 嗅探路径去重
                    if self.verbose:
                        self._agent_logger.log_info(
                            f"[Background] 原生总结回合收口（content 非空，含工具轮亦收口）→ 投递完成汇报 "
                            f"(task_id={pending.get('taskId')}, chars={len(content)})"
                        )
                    await self._emit_background_completion(pending, content)
        elif isinstance(message, TaskProgressMessage):
            # 高频进度心跳：只经带外通道推前端（Task 页实时可视化），不打 Server 控制台。
            # 单条仅 task_id、无 usage/last_tool，逐 tick 刷屏且无 console 价值；实时进度看 Task 页。
            await self._push_background_progress(message)
        elif isinstance(message, TaskStartedMessage):
            # 子任务启动：低频，留一行 console 便于观测；同样推前端。
            if self.verbose:
                self._agent_logger.log_info(
                    f"[Background] TaskStarted task_id={getattr(message, 'task_id', None)}"
                )
            # Task 页运行态全阶段预声明（detach 后 workflow 任务的 TaskStarted 走此路径时也兜底）
            await self._maybe_emit_workflow_phases(message)
            # 启动即推心跳（在 phases 之后——先记 workflow task_id，isWorkflow 标记才正确）
            await self._push_background_progress(message)
        elif isinstance(message, UserMessage) and self._bg_completion_pending is not None:
            # T2：后台回合的工具结果(ToolResultBlock)→ 收 tool.completed envelope（之前在下方 generic 分支被静默丢弃）
            await self._sniff_injected_task_notifications(message)
            self._collect_bg_turn_events(message)
        elif isinstance(message, UserMessage):
            # 注入式 task-notification 嗅探（终态投递第三路径），其余内容仍静默
            await self._sniff_injected_task_notifications(message)
        elif hasattr(message, 'event') or isinstance(message, (AssistantMessage, UserMessage, SystemMessage)):
            # 逐 token 流式增量 / 工具结果 / 系统事件 / 非汇报态的整段回复：
            # 与回合内路径的聚合纪律一致——静默丢弃、不逐条 log，避免刷屏。
            pass
        else:
            # 仅对真正未知的新消息类型留一行（与顶层 [UnknownMessage] 同源的"勿静默吞未知"纪律）
            if self.verbose:
                self._agent_logger.log_info(
                    f"[Background] ignored out-of-turn {type(message).__name__}"
                )

    async def _flush_pending_background(self) -> None:
        """N1 方案①兜底 flush：若后台原生总结回合的 pending 仍未收口（主信号"无 tool_use 轮"
        未命中——罕见，如主控以带工具调用的轮收尾），在前台新回合开始 / disconnect 前合并 emit
        已收集文本并清空，保证可观测性不丢消息（N1 底线）。pending 为空时为 no-op。

        必须在 set_runtime_context 覆写 _last_runtime_context **之前**调用——这样 emit 用的是
        launching 回合的窗口/会话定位，而非新回合的。
        """
        if self._bg_completion_pending is None:
            return
        pending = self._bg_completion_pending
        content = "\n".join(self._bg_summary_parts).strip()
        self._bg_completion_pending = None
        self._bg_summary_parts = []
        self._bg_round_had_tool = False
        if self.verbose:
            self._agent_logger.log_info(
                f"[Background] 兜底 flush 残留 pending → 投递完成汇报 "
                f"(task_id={pending.get('taskId')}, chars={len(content)})"
            )
        await self._emit_background_completion(pending, content)

    @staticmethod
    def _compose_background_text(status: str, summary: str) -> str:
        """组装后台任务完成的展示文本（实时气泡与 history 重建共用同一份，保证渲染收敛）。"""
        body = (summary or "").strip()
        if status == "completed":
            return body or "后台任务已完成"
        status_text = "已停止" if status == "stopped" else "执行失败"
        prefix = f"后台任务{status_text}"
        return f"{prefix}\n\n{body}" if body else prefix

    def _collect_bg_turn_events(self, message: Any) -> None:
        """T2：把后台原生总结回合一条 SDK 消息(Assistant/User)的 block 序列，镜像前台 block→chunk
        构造，经 per-turn MainStreamMapper 映射成 envelope，按序追加进 self._bg_turn_events。
        复用前台同一 mapper → envelope 形状零漂移；工具 started/completed 用 SDK tool_use_id 配对。
        失败只 log、不破坏收口流程。"""
        mapper = self._bg_stream_mapper
        if mapper is None:
            return
        chunks: list[StreamChunk] = []
        for block in (getattr(message, "content", None) or []):
            if isinstance(block, ThinkingBlock):
                t = self._normalize_visible_content(block.thinking)
                if t:
                    chunks.append(StreamChunk(type="thinking_complete", content=t))
            elif isinstance(block, TextBlock):
                t = self._filter_assistant_text(block.text)
                if t:
                    chunks.append(StreamChunk(type="text_complete", content=t))
            elif isinstance(block, ToolUseBlock):
                # 总结回合一般只用普通工具(Read/Glob/load_artifact…)；Task/Workflow/TaskOutput 特例
                # 在此降级为普通工具气泡（总结回合不派发它们）。
                inp = block.input if isinstance(block.input, dict) else {}
                self._bg_tool_names[block.id] = block.name
                chunks.append(StreamChunk(
                    type="tool_call_start",
                    tool_call_id=block.id,
                    tool_name=block.name,
                    tool_description=inp.get("description", ""),
                    tool_params=inp or None,
                ))
            elif isinstance(block, ToolResultBlock):
                tool_use_id = getattr(block, "tool_use_id", None)
                tool_name = self._bg_tool_names.get(tool_use_id or "", "unknown")
                is_error = getattr(block, "is_error", False)
                success, output_text, error_message, error_type, hidden_message = self._resolve_tool_result_state(
                    tool_name=tool_name, result=block.content, is_error=is_error, output_limit=1000,
                )
                chunks.append(StreamChunk(
                    type="tool_call_complete",
                    tool_call_id=tool_use_id,
                    tool_output=output_text,
                    success=success,
                    error=error_message,
                    error_type=error_type,
                    hidden_content=hidden_message,
                ))
        for chunk in chunks:
            try:
                self._bg_turn_events.extend(mapper.map_chunk(chunk))
            except Exception as e:
                logger.warning(f"[Background] map bg turn chunk failed: {e}")

    async def _emit_bare_background_completion(
        self,
        task_id: str,
        status: str,
        summary: str = "",
        output_file: str | None = None,
        sdk_session_id: str | None = None,
    ) -> None:
        """裸投递后台任务终态：只为前端 Task 面板收口，无富总结、不消费回合 events 缓冲。

        两个调用场景（2026-06-12 实测教训：丢失终态会让卡片永久 running——回合结束后
        SDK 心跳通道关闭，前端的静默推断收口无从触发，流死寂时不推断是有意设计）：
        ① N1 单槽被占期间到达的并行 TaskNotificationMessage（总结槽归首任务）；
        ② CLI 把 <task-notification> 注入主控 prompt 流（回合内消费，宿主收不到
           TaskNotificationMessage）——经 _sniff_injected_task_notifications 嗅探。
        """
        if self._background_push is None or not task_id:
            return
        if task_id in self._bg_completed_emitted:
            return
        self._bg_completed_emitted.add(task_id)
        ctx = self._last_runtime_context or {}
        record = {
            "kind": "background_task",
            "taskId": task_id,
            "status": status,
            "hasSummary": False,   # 前端只收口面板，不渲染气泡、不落 history
            "content": self._compose_background_text(status, summary),
            "events": [],
            "summary": summary,
            "outputFile": output_file,
            "windowId": ctx.get("windowId"),
            "sessionId": ctx.get("sessionId"),
            "sdkSessionId": sdk_session_id,
            "turnId": ctx.get("turnId"),
        }
        try:
            await self._background_push(record)
        except Exception as e:
            logger.warning(f"background_push (bare) callback failed: {e}")

    _TASK_NOTIFICATION_RE = re.compile(
        r"<task-notification>(.*?)</task-notification>", re.DOTALL
    )

    async def _sniff_injected_task_notifications(self, message: Any) -> None:
        """嗅探 CLI 注入主控 prompt 流的 <task-notification> 块并裸投递终态。

        实测（2026-06-12 18:22 金凤127）：同回合多任务完成时，CLI 可能只给宿主发一条
        TaskNotificationMessage，其余通知直接作为 user 消息注入主控 prompt（主控因此
        "知道"完成，宿主却收不到）——这是终态投递的第三条路径，必须在消息流里嗅探。
        与 TaskNotificationMessage 双到达时由 _bg_completed_emitted 去重。
        """
        content = getattr(message, "content", None)
        texts: list[str] = []
        if isinstance(content, str):
            texts.append(content)
        elif isinstance(content, list):
            for b in content:
                t = getattr(b, "text", None)
                if isinstance(t, str):
                    texts.append(t)
                elif isinstance(b, dict) and isinstance(b.get("text"), str):
                    texts.append(b["text"])
        if not texts:
            return
        for blob in self._TASK_NOTIFICATION_RE.findall("\n".join(texts)):
            task_id = self._extract_tag(blob, "task-id")
            if not task_id or task_id in self._bg_completed_emitted:
                continue
            status = self._extract_tag(blob, "status") or "completed"
            if self.verbose:
                self._agent_logger.log_info(
                    f"[Background] 嗅探到注入式 task-notification → 裸投递 "
                    f"(task_id={task_id}, status={status})"
                )
            await self._emit_bare_background_completion(
                task_id,
                status,
                summary=self._extract_tag(blob, "summary") or "",
                output_file=self._extract_tag(blob, "output-file"),
                sdk_session_id=getattr(message, "session_id", None),
            )

    @staticmethod
    def _extract_tag(blob: str, tag: str) -> str | None:
        m = re.search(rf"<{tag}>(.*?)</{tag}>", blob, re.DOTALL)
        return m.group(1).strip() if m else None

    async def _emit_background_completion(self, pending: dict[str, Any], content: str) -> None:
        """把后台 Workflow 完成汇报（优先用主控原生总结文本）经 host 回调带外推送给前端。

        content 为空（极少数无原生总结回合的情形）时回退到 TaskNotification.summary（标题级）。
        sessionId 用 runtime context 的 store session id（与 _window_sessions 同源），SDK 子进程的
        session_id 另存 sdkSessionId 仅作诊断。实时推送与 host 落 history 复用这同一份 content。
        """
        if self._background_push is None:
            return
        ctx = self._last_runtime_context or {}
        status = pending.get("status", "completed")
        # has_summary：主控是否产出了原生总结文本。True → Chat 渲染气泡 + 落 history；
        # False → 仅 generic 占位（'Workflow ... completed'），前端只收口 Task 面板、不渲染气泡、不落盘。
        has_summary = bool(content and content.strip())
        body = content.strip() if has_summary else \
            self._compose_background_text(status, pending.get("fallback", ""))
        # T2：本次后台回合的完整 envelope 序列（thinking/tool/text）。非空时前端据此渲染完整一条回合，
        # 落 history 也用它（逐 envelope），不再单独落 content（避免重载双文本）。content 仅作无 events 时的兜底。
        turn_events = self._bg_turn_events
        record = {
            "kind": "background_task",  # 前端通道判别字段（与 interaction record 区分）
            "taskId": pending.get("taskId"),
            "status": status,
            "hasSummary": has_summary,  # 前端/落盘据此区分富总结 vs generic 占位
            "content": body,
            "events": turn_events,      # T2：完整回合 envelope 序列（可能为空）
            "summary": pending.get("fallback", ""),
            "outputFile": pending.get("outputFile"),
            "windowId": ctx.get("windowId"),
            "sessionId": ctx.get("sessionId"),
            "sdkSessionId": pending.get("sdkSessionId"),
            "turnId": ctx.get("turnId"),
        }
        # 复位本次后台回合缓冲（收口后不再复用）
        self._bg_turn_events = []
        self._bg_tool_names = {}
        self._bg_stream_mapper = None
        try:
            await self._background_push(record)
        except Exception as e:
            logger.warning(f"background_push callback failed: {e}")

    async def _push_background_progress(self, message: Any) -> None:
        """把后台 Workflow 进度（TaskStarted/TaskProgress）组装成 record 带外推送给前端。

        只实时推送、不落 history（瞬时心跳；完成态由 _push_background_task 持久化）。
        SDK 实时只给 task 级聚合：usage(total_tokens/tool_uses/duration_ms) + last_tool_name + description，
        无 per-agent 模型/prompt（完成后读 transcript 补，见 Task 页 tier C）。
        """
        if self._background_progress_push is None:
            return
        ctx = self._last_runtime_context or {}
        usage = getattr(message, "usage", None)
        task_id = getattr(message, "task_id", None)
        tool_use_id = getattr(message, "tool_use_id", None)
        # 任务形态：agent=子代理型（Task/Agent 工具发起）| command=单次工具/Shell 型。
        # 经发起工具名反查（_tool_name_by_id 回合内常驻）；未命中（如 workflow 内派生）退 SDK task_type，再退 None。
        launch_tool = self._tool_name_by_id.get(tool_use_id) if tool_use_id else None
        if launch_tool in ("Task", "Agent"):
            task_kind = "agent"
        elif launch_tool:
            task_kind = "command"
        else:
            task_kind = getattr(message, "task_type", None)
        if task_kind == "agent" and task_id:
            self._bg_agent_task_ids.add(task_id)
        owner_kind, owner_id = self._classify_bg_task_owner(task_id, tool_use_id)
        record = {
            "kind": "workflow_progress",  # 前端通道判别字段（与 background_task / interaction 区分）
            "taskId": task_id,
            "isWorkflow": bool(task_id and task_id in self._workflow_task_ids),  # 区分 Workflow / 普通后台 Task（统一活动灯用）
            "status": "running",
            "usage": dict(usage) if usage else None,
            "lastToolName": getattr(message, "last_tool_name", None),
            "description": getattr(message, "description", None),
            # 归属链（Task 页后台任务卡分组 + 详情端点定位用）
            "toolUseId": tool_use_id,
            "ownerKind": owner_kind,   # main | subagent | workflow（best-effort，见 _classify_bg_task_owner）
            "ownerId": owner_id,
            "taskKind": task_kind,     # agent | command | None（面板按形态分区）
            "windowId": ctx.get("windowId"),
            "sessionId": ctx.get("sessionId"),
            "sdkSessionId": getattr(message, "session_id", None),
        }
        try:
            await self._background_progress_push(record)
        except Exception as e:
            logger.warning(f"background_progress_push callback failed: {e}")

    def _classify_bg_task_owner(
        self, task_id: str | None, tool_use_id: str | None
    ) -> tuple[str, str | None]:
        """后台任务归属判定（best-effort，供 Task 页分组展示）。

        - subagent：发起工具调用经主控流、且归属某回合内 Task 子代理（_tool_to_subagent 命中非空）。
        - main：发起工具调用经主控流、主控自身（命中但值为 None）。
        - workflow：tool_use 未经主控流（Workflow 编排内 agent 的工具转后台不进主控消息循环），
          且本会话存在 workflow 任务——组级归类，精确到哪个 agent 由详情端点按 toolUseId 反查。
        注意 _tool_to_subagent 在工具结果到达时 pop、回合开始时 clear——自动转后台的 Bash
        其结果（"running in background"）可能先于 TaskStarted 到达，命中率非 100%，未命中时
        按会话有无 workflow 退化归类。
        """
        if tool_use_id and tool_use_id in self._tool_to_subagent:
            owner = self._tool_to_subagent.get(tool_use_id)
            return ("subagent", owner) if owner else ("main", None)
        if self._workflow_task_ids and not (task_id and task_id in self._workflow_task_ids):
            return "workflow", None
        # 后台 agent 的内部工具执行（CLI 登记为独立 task）：tool_use 不经主控流、
        # 且存在活跃的 agent 型后台任务（自身除外）→ 折叠展示用的 agent-internal
        if (
            self._bg_agent_task_ids
            and not (task_id and task_id in self._bg_agent_task_ids)
            and not (tool_use_id and tool_use_id in self._tool_name_by_id)
        ):
            return "agent-internal", None
        return "main", None

    @staticmethod
    def _parse_workflow_meta(script: str) -> dict[str, Any]:
        """从 workflow 脚本源码解析 meta.name + meta.phases（Task 页运行态全阶段预声明）。

        镜像 .NET WorkflowTranscriptService.ParseScriptPhases 的正则：phases 块内逐个
        `{ title:'...', detail:'...' }` 抽取。detail 含 ] / } 会截断（与 .NET 同限，真实脚本不触发）。
        失败一律返回空，绝不破坏回合。
        """
        name: str | None = None
        phases: list[dict[str, Any]] = []
        try:
            nm = re.search(r"name:\s*['\"]([^'\"]+)['\"]", script)
            if nm:
                name = nm.group(1)
            block = re.search(r"phases:\s*\[(.*?)\]", script, re.DOTALL)
            if block:
                idx = 1
                for obj in re.finditer(r"\{[^}]*\}", block.group(1)):
                    seg = obj.group(0)
                    tm = re.search(r"title:\s*['\"]([^'\"]+)['\"]", seg)
                    if not tm:
                        continue
                    dm = re.search(r"detail:\s*['\"]([^'\"]+)['\"]", seg)
                    phases.append({"index": idx, "title": tm.group(1),
                                   "detail": dm.group(1) if dm else None})
                    idx += 1
        except Exception as e:
            logger.warning(f"_parse_workflow_meta failed: {e}")
        return {"workflowName": name, "phases": phases}

    def _stash_workflow_meta(self, block: Any) -> None:
        """拦截 Workflow tool_use：读 scriptPath 指向的插件源脚本（稳定常在）或 inline script，
        解析 meta 暂存（key=block.id=tool_use_id），待 TaskStarted 拿到 task_id 再推前端。
        """
        # 无论 meta 解析成败都记下"这个 tool_use 是 Workflow"——isWorkflow 标记不依赖脚本可读
        if getattr(block, "id", None):
            self._workflow_tool_use_ids.add(block.id)
        try:
            inp = getattr(block, "input", None) or {}
            script_path = inp.get("scriptPath")
            script = inp.get("script")
            if script_path and not script:
                with open(script_path, encoding="utf-8") as f:
                    script = f.read()
            if not script:
                return
            meta = self._parse_workflow_meta(script)
            if meta["phases"]:
                self._pending_workflow_meta[block.id] = meta
        except Exception as e:
            logger.warning(f"_stash_workflow_meta failed: {e}")

    async def _push_workflow_phases(self, task_id: str | None, session_id: str | None,
                                    workflow_name: str | None, phases: list[dict[str, Any]]) -> None:
        """把预声明的全阶段经现有 SSE 带外通道推前端（kind=workflow_phases），只实时不落盘。"""
        if self._background_progress_push is None:
            return
        ctx = self._last_runtime_context or {}
        record = {
            "kind": "workflow_phases",  # 前端通道判别字段（与 workflow_progress / background_task 区分）
            "taskId": task_id,
            "sdkSessionId": session_id,
            "workflowName": workflow_name,
            "phases": phases,
            "windowId": ctx.get("windowId"),
            "sessionId": ctx.get("sessionId"),
        }
        try:
            await self._background_progress_push(record)
        except Exception as e:
            logger.warning(f"workflow_phases push failed: {e}")

    async def _maybe_emit_workflow_phases(self, message: Any) -> None:
        """TaskStarted 命中暂存的 Workflow meta（按 tool_use_id）→ 以 task_id 为 key 推前端。"""
        tool_use_id = getattr(message, "tool_use_id", None)
        if not tool_use_id:
            return
        # Workflow 工具发起的 task → 记 task_id，后续进度心跳带 isWorkflow=true
        if tool_use_id in self._workflow_tool_use_ids:
            task_id = getattr(message, "task_id", None)
            if task_id:
                self._workflow_task_ids.add(task_id)
        meta = self._pending_workflow_meta.pop(tool_use_id, None)
        if not meta:
            return
        await self._push_workflow_phases(
            getattr(message, "task_id", None),
            getattr(message, "session_id", None),
            meta.get("workflowName"),
            meta.get("phases", []),
        )

    async def set_model(self, model: str) -> bool:
        """
        动态切换模型（不断开连接）

        通过 SDK 的 set_model() 方法发送控制消息。
        仅当模型实际变化时才发送控制消息，避免触发会话状态问题。

        Args:
            model: 模型名称（如 "claude-sonnet-4-20250514"）

        Returns:
            是否成功切换
        """
        if not self._connected or not self._client:
            logger.warning("Cannot set model: not connected")
            return False

        # 检查模型是否相同，相同则跳过，避免不必要的控制消息
        if model == self._current_model:
            if self.verbose:
                self._agent_logger.log_info(f"模型未变化，跳过: {model}")
            return True

        try:
            await self._client.set_model(model)
            self._current_model = model

            if self.verbose:
                self._agent_logger.log_info(f"模型已切换: {model}")

            return True
        except Exception as e:
            logger.error(f"Failed to set model: {e}")
            if self.verbose:
                self._agent_logger.log_warning(f"模型切换失败: {e}")
            return False

    # ─────────────────────────────────────────────────────
    # Message Processing with Logging
    # ─────────────────────────────────────────────────────

    def _process_message(self, message) -> str:
        """Process a message from the SDK and log it."""
        text_content = ""

        if isinstance(message, AssistantMessage):
            # 存储 API 响应的模型值，用于日志显示（不覆盖 _current_model）
            self._capture_response_model(getattr(message, 'model', None))

            for block in message.content:
                if isinstance(block, ThinkingBlock):
                    # WP-2 O3 阶段 1 临时观测(指挥部 2026-05-29 拍板跨 WP-1 §2 OUT 边界 1 行);
                    # 阶段 2 决策后(独立 PR)删除本行或升级为 display="summary" 长期配置
                    if self.verbose:
                        self._agent_logger.log_info(f"[O3-obs] thinking_block has_content={bool(block.thinking)}")
                    normalized_thinking = self._normalize_visible_content(block.thinking)
                    if normalized_thinking and self.verbose:
                        if not self._in_thinking:
                            self._agent_logger.log_thinking_start()
                            self._in_thinking = True
                        self._agent_logger.log_thinking(normalized_thinking)
                        self._agent_logger.log_thinking_end()
                        self._in_thinking = False

                elif isinstance(block, TextBlock):
                    normalized_text = self._filter_assistant_text(block.text)
                    if normalized_text:
                        text_content += normalized_text
                        if self.verbose:
                            if self._in_thinking:
                                self._agent_logger.log_thinking_end()
                                self._in_thinking = False
                            if not self._in_response:
                                self._agent_logger.log_response_start()
                                self._in_response = True
                            self._agent_logger.log_response(normalized_text)

                elif isinstance(block, ToolUseBlock):
                    if self.verbose:
                        if self._in_response:
                            self._agent_logger.log_response_end()
                            self._in_response = False
                        self._current_tool_name = block.name
                        if block.id:
                            self._tool_name_by_id[block.id] = block.name
                        if block.name == "Task":
                            # Task 工具：enter_subagent 已包含 DISPATCH 输出
                            subagent_type = block.input.get("subagent_type", "unknown")
                            description = block.input.get("description", "")
                            self._agent_logger.enter_subagent(
                                subagent_type=subagent_type,
                                description=description
                            )
                        else:
                            self._agent_logger.log_tool_use(block.name, block.input)

                elif isinstance(block, ToolResultBlock):
                    if self.verbose:
                        is_error = getattr(block, 'is_error', False)
                        block_id = getattr(block, 'tool_use_id', None)
                        tool_name = (self._tool_name_by_id.pop(block_id, None) if block_id else None) or self._current_tool_name or "unknown"
                        self._agent_logger.log_tool_result(tool_name, block.content, is_error)
                        if tool_name == "Task":
                            result_summary = str(block.content)[:200] if block.content else None
                            self._agent_logger.exit_subagent("SubAgent", result_summary)
                        self._current_tool_name = None

                elif isinstance(block, (ServerToolUseBlock, ServerToolResultBlock)):
                    # M1: SDK 0.1.65+ 新增的 server-side 工具块（advisor / web_search / web_fetch / code_execution）
                    if self.verbose:
                        ident = getattr(block, 'name', None) or getattr(block, 'tool_use_id', '')
                        self._agent_logger.log_info(
                            f"[ServerTool] {type(block).__name__}: {ident}"
                        )

                else:
                    # M1: 未知 content block 类型兜底（避免 SDK 未来扩展时静默丢失）
                    if self.verbose:
                        self._agent_logger.log_warning(
                            f"[UnknownBlock] Unhandled content block: {type(block).__name__}"
                        )

        elif hasattr(message, 'event'):
            event = message.event
            event_type = event.get("type", "")
            self._process_streaming_event(event_type, event)

        elif isinstance(message, dict):
            self._process_dict_message(message)

        return text_content

    def _process_streaming_event(self, event_type: str, event: dict) -> None:
        """Process streaming events from the SDK."""
        self._capture_stream_event_model(event)

        if event_type == "content_block_start":
            block_type = event.get("content_block", {}).get("type", "")
            if block_type == "thinking" and self.verbose:
                if not self._in_thinking:
                    self._agent_logger.log_thinking_start()
                    self._in_thinking = True
            elif block_type == "tool_use" and self.verbose:
                tool_name = event.get("content_block", {}).get("name", "")
                self._current_tool_name = tool_name

        elif event_type == "content_block_delta":
            delta = event.get("delta", {})
            delta_type = delta.get("type", "")
            if delta_type == "thinking_delta" and self.verbose:
                thinking = self._normalize_visible_content(delta.get("thinking", ""), preserve_blank=True)
                if thinking:
                    self._agent_logger.log_thinking(thinking, is_delta=True)
            elif delta_type == "text_delta" and self.verbose:
                normalized_text = self._filter_assistant_text(delta.get("text", ""), preserve_blank=True)
                if normalized_text:
                    if not self._in_response:
                        self._agent_logger.log_response_start()
                        self._in_response = True
                    self._agent_logger.log_response(normalized_text, is_delta=True)

        elif event_type == "content_block_stop":
            if self._in_thinking and self.verbose:
                self._agent_logger.log_thinking_end()
                self._in_thinking = False
            if self._in_response and self.verbose:
                self._agent_logger.log_response_end()
                self._in_response = False

        elif event_type == "message_start":
            if self.verbose:
                self._agent_logger.log_info("Processing...")

        elif event_type == "subagent_start":
            subagent_name = event.get("subagent_name", "SubAgent")
            if self.verbose:
                self._agent_logger.enter_subagent(subagent_name)

        elif event_type == "subagent_stop":
            subagent_name = event.get("subagent_name", "SubAgent")
            result = event.get("result", "")
            if self.verbose:
                self._agent_logger.exit_subagent(subagent_name, result)

        elif event_type == "tool_use":
            tool_name = event.get("name", "")
            tool_input = event.get("input", {})
            tool_use_id = event.get("id", "") or event.get("tool_use_id", "")
            if self.verbose:
                self._agent_logger.log_tool_use(tool_name, tool_input)
                self._current_tool_name = tool_name
                if tool_use_id:
                    self._tool_name_by_id[tool_use_id] = tool_name
                if tool_name == "Task":
                    subagent_type = tool_input.get("subagent_type", "unknown")
                    self._agent_logger.enter_subagent(subagent_type)

        elif event_type == "tool_result":
            ev_tool_use_id = event.get("tool_use_id", "")
            tool_name = (self._tool_name_by_id.pop(ev_tool_use_id, None) if ev_tool_use_id else None) or event.get("tool_name") or self._current_tool_name or "unknown"
            result = event.get("result", "")
            is_error = event.get("is_error", False)
            if self.verbose:
                self._agent_logger.log_tool_result(tool_name, result, is_error)
                if tool_name == "Task":
                    self._agent_logger.exit_subagent("SubAgent", str(result)[:200])
                self._current_tool_name = None

    def _process_dict_message(self, message: dict) -> None:
        """Process raw dict messages."""
        msg_type = message.get("type", "")
        if self.verbose:
            self._agent_logger.log_event(msg_type, message)

    # ─────────────────────────────────────────────────────
    # Chat Methods (Unified Entry Point)
    # ─────────────────────────────────────────────────────

    async def chat(
        self,
        user_message: str,
        model: str | None = None,
        runtime_context: dict[str, str] | None = None,
    ) -> str:
        """Unified chat interface."""
        # N1 兜底：前台新回合前 flush 残留后台 pending（趁旧 _last_runtime_context 定位 launching 回合窗口）
        await self._flush_pending_background()
        self.set_runtime_context(runtime_context)
        turn: _TurnChannel | None = None
        try:
            if not self._connected:
                await self.connect(model=model)
            elif model and model != self._current_model:
                await self.set_model(model)

            if self.verbose:
                self._agent_logger.log_user_message(user_message)

            self._in_thinking = False
            self._in_response = False
            self._current_tool_name = None
            self._placeholder_text_suppressed_logged = False
            self._response_model = None
            self._tool_name_by_id.clear()

            # 注册回合通道后再 query：常驻 _drain_loop 据 _active_turn 把消息投递到本回合队列。
            # 不变量：注册新回合前，上一回合的 ResultMessage 必须已被 drain 读出（正常回合结束时
            # drain 投递 ResultMessage 的同步操作会清空 _active_turn）。sleep(0) 让出事件循环，
            # 给 drain 机会排空滞留的回合外消息，缩小"陈旧 ResultMessage 串入新回合"的竞态窗口。
            await asyncio.sleep(0)
            turn = _TurnChannel()
            self._active_turn = turn
            await self._client.query(user_message)

            full_response = ""
            async for message in self._iter_turn_messages(turn):
                text = self._process_message(message)
                full_response += text

            if self.verbose:
                if self._in_response:
                    self._agent_logger.log_response_end()
                self._agent_logger.log_complete(model=self._completion_model_stamp())

            return full_response
        finally:
            # 早退/异常时复位回合槽（is 身份比较，避免误清后续回合）
            if turn is not None and self._active_turn is turn:
                self._active_turn = None
            self.clear_runtime_context()

    @staticmethod
    def _build_context_block(context: dict) -> str | None:
        """构建画布上下文 content block（独立于用户消息）。

        模块按所属区域分组显示，其他类型按类型平铺显示。
        直接选中的区域标签作为"分区"类别独立展示。
        """
        if not context:
            return None
        parts = []

        # ── 模块（按区域分组） ──
        if context.get("modules"):
            from collections import OrderedDict
            zone_groups: dict[str | None, list] = OrderedDict()
            for m in context["modules"]:
                zid = m.get("zoneId")
                zone_groups.setdefault(zid, []).append(m)

            group_strs = []
            for zid, mods in zone_groups.items():
                names = "、".join(
                    f'{m.get("name", "?")}(id:{m.get("id", "?")})'
                    for m in mods
                )
                if zid:
                    zname = mods[0].get("zoneName") or zid
                    group_strs.append(
                        f"{names}，所在区域：{zname}(id:{zid})"
                    )
                else:
                    group_strs.append(names)
            parts.append(f"模块：{'；'.join(group_strs)}")

        # ── 墙体 ──
        if context.get("walls"):
            wall_list = "、".join(
                f'墙体(id:{w.get("id", "?")})' for w in context["walls"]
            )
            parts.append(f"墙体：{wall_list}")

        # ── 柱子 ──
        if context.get("columns"):
            col_list = "、".join(
                f'柱(id:{c.get("id", "?")}, 结构柱:{"是" if c.get("isStructural") else "否"})'
                for c in context["columns"]
            )
            parts.append(f"柱：{col_list}")

        # ── 门 ──
        if context.get("doors"):
            door_list = "、".join(
                f'门(id:{d.get("id", "?")})' for d in context["doors"]
            )
            parts.append(f"门：{door_list}")

        # ── 窗 ──
        if context.get("windows"):
            win_list = "、".join(
                f'窗(id:{w.get("id", "?")})' for w in context["windows"]
            )
            parts.append(f"窗：{win_list}")

        # ── 禁区 ──
        if context.get("exclusions"):
            exc_list = "、".join(
                f'{e.get("name") or "禁区"}(id:{e.get("id", "?")})'
                for e in context["exclusions"]
            )
            parts.append(f"禁区：{exc_list}")

        # ── 分区（直接选中的区域标签） ──
        if context.get("zones"):
            zone_list = "、".join(
                f'{z.get("name", "?")}(id:{z.get("id", "?")})'
                for z in context["zones"]
            )
            parts.append(f"分区：{zone_list}")

        # ── 用户标注区域（完成后的临时意图批次） ──
        spatial_marks = context.get("spatialMarks")
        if isinstance(spatial_marks, list) and spatial_marks:
            mark_lines = ["用户标注区域（网格选区表示用户意图的大致范围；具体落位、贴墙、避让和边界需结合项目几何重新计算）："]
            for mark in spatial_marks:
                if not isinstance(mark, dict):
                    continue

                mark_id = mark.get("id", "?")
                zone_id = mark.get("zoneId", "?")
                label = mark.get("label", "")
                description = mark.get("description", "")
                geometry = mark.get("geometry", [])
                geometry_text = json.dumps(
                    geometry,
                    ensure_ascii=False,
                    separators=(",", ":")
                )
                mark_lines.append(f"- id={mark_id} zoneId={zone_id} label={label}")
                mark_lines.append(f"  description={description}")
                mark_lines.append(f"  geometry={geometry_text}")

            if len(mark_lines) > 1:
                parts.append("\n".join(mark_lines))

        # ── 本轮聊天附件（由 _chat_attachments.json 持久化索引） ──
        chat_attachments = context.get("chatAttachments")
        if isinstance(chat_attachments, dict):
            attachment_items = chat_attachments.get("items")
            if isinstance(attachment_items, list) and attachment_items:
                project_path = chat_attachments.get("projectPath") or "?"
                client_message_id = chat_attachments.get("clientMessageId") or "?"
                item_strs = []
                for item in attachment_items:
                    if not isinstance(item, dict):
                        continue
                    attachment_id = item.get("attachmentId") or "?"
                    file_name = item.get("originalFileName") or "?"
                    mime_type = item.get("mimeType") or "?"
                    width = item.get("width") or "?"
                    height = item.get("height") or "?"
                    item_strs.append(
                        f"attachmentId={attachment_id}, originalFileName={file_name}, "
                        f"mimeType={mime_type}, size={width}x{height}"
                    )
                if item_strs:
                    parts.append(
                        "本轮聊天附件："
                        f"projectPath={project_path}；clientMessageId={client_message_id}；"
                        f"{'；'.join(item_strs)}。"
                        "如需分析参考图，直接使用上述 attachmentId 调用 "
                        "mcp__canvas__canvas_vision（传 prompt + attachmentId），"
                        "不要再通过 Glob/Read 搜索 _chat_attachments.json。"
                    )

        if not parts:
            return None
        detail = "\n".join(parts)
        return (
            f"<canvas_context>用户在设计画布上选中了以下对象：\n"
            f"{detail}\n\n"
            f"以上上下文可能与当前请求相关，也可能无关。</canvas_context>"
        )

    async def chat_stream(
        self,
        user_message: str,
        images: list[str] = None,
        image_blocks: list[dict] = None,
        client_message_id: str | None = None,
        effort: str = None,
        thinking: str = None,
        model: str = None,
        context: dict = None,
        runtime_context: dict[str, str] | None = None,
    ) -> AsyncIterator[StreamChunk]:
        """流式对话外壳：保证回合槽 / runtime context 在任何退出路径（正常/异常/生成器提前关闭）都被复位。"""
        try:
            async for chunk in self._chat_stream_impl(
                user_message,
                images=images,
                image_blocks=image_blocks,
                client_message_id=client_message_id,
                effort=effort,
                thinking=thinking,
                model=model,
                context=context,
                runtime_context=runtime_context,
            ):
                yield chunk
        finally:
            # 单窗口串行：本生成器结束后才会有下一回合，置空回合槽安全；
            # drain 正常路径已在 ResultMessage 时清空，这里兜底早退/异常/GeneratorExit。
            self._active_turn = None
            self.clear_runtime_context()

    async def _chat_stream_impl(
        self,
        user_message: str,
        images: list[str] = None,
        image_blocks: list[dict] = None,
        client_message_id: str | None = None,
        effort: str = None,
        thinking: str = None,
        model: str = None,
        context: dict = None,
        runtime_context: dict[str, str] | None = None,
    ) -> AsyncIterator[StreamChunk]:
        """
        Streaming chat interface with thinking support.

        Args:
            user_message: 用户消息
            images: 图片附件列表（base64 编码，可带 data:image/png;base64, 前缀）
            image_blocks: 资源化附件转换后的 image block 列表
            client_message_id: 前端草稿消息 ID（用于日志与恢复）
            effort: 推理深度 ("low"/"medium"/"high"/"max")，None 使用默认配置
            thinking: 扩展思考开关 ("off"/"adaptive")，None 使用默认配置
            model: 模型名称，None 使用默认配置
            context: 画布上下文（选中模块/区域），由前端 buildContextPayload() 构建
        """
        # N1 兜底：前台新回合前 flush 残留后台 pending（趁旧 _last_runtime_context 定位 launching 回合窗口）
        await self._flush_pending_background()
        self.set_runtime_context(runtime_context)

        if not self._connected:
            await self.connect(effort=effort, thinking=thinking, model=model)
        # 注意：effort/thinking 仅在 connect() 时配置，不支持动态调整
        # 如需不同配置，需要断开后重新 connect()

        if self.verbose:
            self._agent_logger.log_user_message(user_message)
            if client_message_id:
                self._agent_logger.log_info(f"[Attachment] clientMessageId={client_message_id}")

        self._in_thinking = False
        self._in_response = False
        self._streamed_text = False  # 重置流式文本标记
        self._current_tool_name = None
        self._placeholder_text_suppressed_logged = False
        self._response_model = None
        # 重置 SubAgent/ToolCall 状态
        self._active_subagents.clear()
        self._tool_call_counter = 0
        self._pending_tool_calls.clear()
        self._tool_name_by_id.clear()
        self._tool_to_subagent.clear()
        # A 修复:本回合是否启动了后台 Workflow（用于"真后台脱离"——见 AssistantMessage 分支末尾）
        self._turn_launched_workflow = False

        # 构建画布上下文 content block（独立于用户消息，对齐 Claude Code 的 <ide_selection> 模式）
        context_block = self._build_context_block(context)

        inline_image_blocks = list(image_blocks or [])
        if images:
            for img_base64 in images:
                pure_base64 = img_base64
                if "," in img_base64:
                    pure_base64 = img_base64.split(",", 1)[1]
                inline_image_blocks.append({
                    "type": "image",
                    "source": {
                        "type": "base64",
                        "media_type": "image/png",
                        "data": pure_base64
                    }
                })

        # 构建消息内容（images / image_blocks / context 存在时走多 content block 路径）
        if inline_image_blocks or context_block or user_message:
            content = []
            # 1. 图片附件（如有）
            if inline_image_blocks:
                content.extend(inline_image_blocks)
            # 2. 画布上下文（独立 block）
            if context_block:
                content.append({"type": "text", "text": context_block})
            # 3. 用户消息（独立 block）
            if user_message:
                content.append({"type": "text", "text": user_message})

            # 构建完整消息并以异步迭代器形式发送
            # query() 接受 str 或 AsyncIterable，不接受 list
            async def message_stream():
                yield {
                    "type": "user",
                    "message": {"role": "user", "content": content},
                    "parent_tool_use_id": None,
                    "session_id": "default",
                }

            # 注册回合通道后再 query：常驻 _drain_loop 据 _active_turn 把消息投递到本回合队列。
            # 不变量：注册新回合前，上一回合 ResultMessage 应已被 drain 读出（正常结束时同步清空 _active_turn）。
            # sleep(0) 让出事件循环给 drain 排空滞留的回合外消息，缩小"陈旧 ResultMessage 串入新回合"的竞态窗口。
            await asyncio.sleep(0)
            turn = _TurnChannel()
            self._active_turn = turn
            await self._client.query(message_stream())
        else:
            raise ValueError("Message or attachments cannot be empty")

        async for message in self._iter_turn_messages(turn):
            # 获取当前消息的 parent_tool_use_id（用于关联工具调用到 SubAgent）
            current_parent_id = getattr(message, 'parent_tool_use_id', None)

            if hasattr(message, 'event'):
                event = message.event
                event_type = event.get("type", "")
                self._process_streaming_event(event_type, event)

                if event_type == "content_block_delta":
                    delta = event.get("delta", {})
                    delta_type = delta.get("type", "")
                    if delta_type == "text_delta":
                        normalized_text = self._filter_assistant_text(delta.get("text", ""), preserve_blank=True)
                        if normalized_text:
                            self._streamed_text = True
                            yield StreamChunk(type="text", content=normalized_text)
                    elif delta_type == "thinking_delta":
                        thinking = self._normalize_visible_content(delta.get("thinking", ""), preserve_blank=True)
                        if thinking:
                            yield StreamChunk(type="thinking", content=thinking)

                # 处理 tool_result 事件 - 工具执行完成
                elif event_type == "tool_result":
                    tool_name = event.get("tool_name") or self._current_tool_name or "unknown"
                    result = event.get("result", "")
                    is_error = event.get("is_error", False)
                    tool_use_id = event.get("tool_use_id")

                    if self.verbose:
                        self._agent_logger.log_tool_result(tool_name, result, is_error)
                    # 判断是否是 Task（SubAgent）的结果
                    if tool_name == "Task" and tool_use_id and tool_use_id in self._active_subagents:
                        # SubAgent 完成 - 从映射中获取并清理
                        subagent_id = self._active_subagents.pop(tool_use_id)
                        if self.verbose:
                            self._agent_logger.exit_subagent(subagent_id=subagent_id)
                        yield self._build_subagent_completion_chunk(
                            subagent_id=subagent_id,
                            tool_name=tool_name,
                            result=result,
                            is_error=is_error,
                        )
                        # 重置标记，准备接收 MainAgent 后续输出（修复 SubAgent 完成后最终结论不显示的 bug）
                        self._streamed_text = False
                    else:
                        yield self._build_tool_completion_chunk(
                            tool_use_id=tool_use_id,
                            tool_name=tool_name,
                            result=result,
                            is_error=is_error,
                        )
                    self._current_tool_name = None

            elif isinstance(message, AssistantMessage):
                # 存储 API 响应的模型值，用于日志显示（不覆盖 _current_model）
                self._capture_response_model(getattr(message, 'model', None))
                # A 修复:本条 AssistantMessage 是否含工具调用（无工具=主控的收尾文本）
                _had_tool_use = any(isinstance(b, ToolUseBlock) for b in message.content)

                # 检查 API 级错误（0.1.28 修复了 error 字段填充 bug）
                api_error = getattr(message, 'error', None)
                if api_error:
                    error_display = {
                        "authentication_failed": "API 认证失败，请检查 API Key",
                        "billing_error": "计费错误，请检查账户余额",
                        "rate_limit": "请求频率超限，请稍后重试",
                        "invalid_request": "请求格式错误",
                        "server_error": "Anthropic 服务器错误",
                    }.get(api_error, f"未知 API 错误: {api_error}")
                    if self.verbose:
                        self._agent_logger.log_error(f"API 错误: {error_display}")
                    yield StreamChunk(
                        type="text",
                        content=f"\n[API 错误] {error_display}\n",
                        error_type="api_error",
                        error_content=api_error
                    )

                for block in message.content:
                    if isinstance(block, ThinkingBlock):
                        normalized_thinking = self._normalize_visible_content(block.thinking)
                        if normalized_thinking:
                            if self.verbose and not self._in_thinking:
                                self._agent_logger.log_thinking_start()
                                self._agent_logger.log_thinking(normalized_thinking)
                                self._agent_logger.log_thinking_end()
                            yield StreamChunk(type="thinking_complete", content=normalized_thinking)
                    elif isinstance(block, TextBlock):
                        normalized_text = self._filter_assistant_text(block.text)
                        if normalized_text:
                            if self.verbose and not self._in_response:
                                self._agent_logger.log_response_start()
                                self._agent_logger.log_response(normalized_text)
                                self._agent_logger.log_response_end()
                            # 如果已通过流式事件输出，跳过完整块输出（避免重复）
                            if not self._streamed_text:
                                yield StreamChunk(type="text_complete", content=normalized_text)
                        self._streamed_text = False  # 重置标记，准备下一轮
                    elif isinstance(block, ToolUseBlock):
                        self._current_tool_name = block.name
                        if block.id:
                            self._tool_name_by_id[block.id] = block.name
                        if block.name == "Workflow":
                            # A 修复:标记本回合启动了后台 Workflow，供稍后"真后台脱离"判定
                            self._turn_launched_workflow = True
                            # Task 页运行态全阶段预声明:读脚本 meta.phases 暂存,待 TaskStarted 推前端
                            self._stash_workflow_meta(block)
                        if block.name == "Task":
                            # SubAgent 开始 - 添加到活跃映射（支持多个并行）
                            subagent_type = block.input.get("subagent_type", "general-purpose")
                            subagent_name = block.input.get("description", "SubAgent")
                            subagent_id = f"sa-{block.id}"
                            self._active_subagents[block.id] = subagent_id  # 添加映射
                            if self.verbose:
                                # enter_subagent 已包含 DISPATCH 输出，无需单独调用 log_tool_use
                                self._agent_logger.enter_subagent(
                                    subagent_type=subagent_type,
                                    subagent_id=subagent_id,
                                    description=subagent_name
                                )
                            yield StreamChunk(
                                type="subagent_start",
                                subagent_id=subagent_id,
                                subagent_name=subagent_name,
                                subagent_type=subagent_type
                            )
                        elif block.name == "TaskOutput":
                            # TaskOutput 工具 - 发送特殊事件（用于前端识别后台任务轮询）
                            task_id = block.input.get("task_id", "")
                            timeout = block.input.get("timeout", 30000)
                            if self.verbose:
                                self._agent_logger.log_tool_use(block.name, block.input)
                            yield StreamChunk(
                                type="task_output_polling",
                                task_id=task_id,
                                timeout=timeout
                            )
                        else:
                            # 普通工具调用 - 关联到所属的 SubAgent
                            self._tool_call_counter += 1
                            tool_call_id = f"tc-{self._tool_call_counter}"
                            # 保存映射
                            self._pending_tool_calls[block.id] = tool_call_id
                            # 根据 parent_tool_use_id 确定所属的 SubAgent
                            subagent_id = self._active_subagents.get(current_parent_id) if current_parent_id else None
                            self._tool_to_subagent[block.id] = subagent_id  # 记录工具到 SubAgent 的映射
                            if self.verbose:
                                self._agent_logger.log_tool_use(block.name, block.input, subagent_id=subagent_id)
                            yield StreamChunk(
                                type="tool_call_start",
                                subagent_id=subagent_id,
                                tool_call_id=tool_call_id,
                                tool_name=block.name,
                                tool_description=block.input.get("description", ""),
                                tool_params=block.input
                            )

                    elif isinstance(block, ToolResultBlock):
                        block_tool_use_id = getattr(block, 'tool_use_id', None)
                        tool_name = (self._tool_name_by_id.pop(block_tool_use_id, None) if block_tool_use_id else None) or self._current_tool_name or "unknown"
                        is_error = getattr(block, 'is_error', False)
                        if self.verbose:
                            self._agent_logger.log_tool_result(tool_name, block.content, is_error)

                        if block_tool_use_id and block_tool_use_id in self._active_subagents:
                            # SubAgent 完成 - 从映射中获取并清理
                            subagent_id = self._active_subagents.pop(block_tool_use_id)
                            if self.verbose:
                                self._agent_logger.exit_subagent(subagent_id=subagent_id)
                            yield self._build_subagent_completion_chunk(
                                subagent_id=subagent_id,
                                tool_name=tool_name,
                                result=block.content,
                                is_error=is_error,
                            )
                            # 重置标记，准备接收 MainAgent 后续输出（修复 SubAgent 完成后最终结论不显示的 bug）
                            self._streamed_text = False
                        else:
                            yield self._build_tool_completion_chunk(
                                tool_use_id=block_tool_use_id,
                                tool_name=tool_name,
                                result=block.content,
                                is_error=is_error,
                            )

                        self._current_tool_name = None

                    elif isinstance(block, (ServerToolUseBlock, ServerToolResultBlock)):
                        # M1: SDK 0.1.65+ 新增的 server-side 工具块（advisor / web_search / web_fetch / code_execution）
                        if self.verbose:
                            ident = getattr(block, 'name', None) or getattr(block, 'tool_use_id', '')
                            self._agent_logger.log_info(
                                f"[ServerTool] {type(block).__name__}: {ident}"
                            )

                    else:
                        # M1: 未知 content block 类型兜底（避免 SDK 未来扩展时静默丢失）
                        if self.verbose:
                            self._agent_logger.log_warning(
                                f"[UnknownBlock] Unhandled content block: {type(block).__name__}"
                            )

                # A 修复:Workflow 真后台脱离。本回合已启动后台 Workflow，且主控刚输出一条"无工具调用"的
                # 收尾文本（即启动后的总结）→ 主动结束回合，不再死等被后台任务推迟到工作流跑完才发的 ResultMessage。
                # 清空 _active_turn 后，后续 TaskProgress/TaskNotification 由常驻 _drain_loop 走带外通道
                # （_handle_background_message：进度静默丢弃、完成时 _push_background_task 推前端），输入框立即解锁。
                # 这样 Workflow 行为对齐"真后台 Task"：发起轮立即收尾，工作流成败都不再霸占对话。
                if self._turn_launched_workflow and not _had_tool_use:
                    if self.verbose:
                        self._agent_logger.log_info(
                            "[Workflow] 后台任务已启动且收尾文本已输出，回合脱离收尾（真后台，不锁输入）"
                        )
                    # 显式告知前端：本回合已把 workflow 脱离到后台，完成会经 background_task.completed
                    # 旁路到达。前端据此置 isPollingBackground，跳过"前台回合结束即内联收口"——否则回合一
                    # 结束就把仍在后台跑的 workflow 误标 completed（与旧 TaskOutput 轮询的 task_output_polling
                    # 同为"后台脱离"信号，但此处是 push 式真后台、非轮询，故用独立语义名）。
                    yield StreamChunk(type="workflow_detached")
                    self._turn_launched_workflow = False
                    self._active_turn = None  # 后续消息 → drain 带外通道
                    break

            # 处理 UserMessage 中的 ToolResultBlock（工具调用完成）
            elif isinstance(message, UserMessage):
                # 注入式 task-notification 嗅探（CLI 回合内消费的完成通知，宿主唯一可见处）
                await self._sniff_injected_task_notifications(message)
                for block in message.content:
                    if isinstance(block, ToolResultBlock):
                        block_tool_use_id = getattr(block, 'tool_use_id', None)
                        tool_name = (self._tool_name_by_id.pop(block_tool_use_id, None) if block_tool_use_id else None) or self._current_tool_name or "unknown"
                        is_error = getattr(block, 'is_error', False)
                        # 日志：输出工具结果
                        if self.verbose:
                            self._agent_logger.log_tool_result(tool_name, block.content, is_error)

                        if block_tool_use_id and block_tool_use_id in self._active_subagents:
                            # SubAgent 完成 - 从映射中获取并清理
                            subagent_id = self._active_subagents.pop(block_tool_use_id)
                            if self.verbose:
                                self._agent_logger.exit_subagent(subagent_id=subagent_id)
                            yield self._build_subagent_completion_chunk(
                                subagent_id=subagent_id,
                                tool_name=tool_name,
                                result=block.content,
                                is_error=is_error,
                            )
                            # 重置标记，准备接收 MainAgent 后续输出（修复 SubAgent 完成后最终结论不显示的 bug）
                            self._streamed_text = False
                        else:
                            yield self._build_tool_completion_chunk(
                                tool_use_id=block_tool_use_id,
                                tool_name=tool_name,
                                result=block.content,
                                is_error=is_error,
                            )
                        self._current_tool_name = None

            elif isinstance(message, ResultMessage):
                # 捕获 SDK 原生 session_id（续聊 resume 用，跨回合稳定）
                _sid = getattr(message, "session_id", None)
                if _sid:
                    self._sdk_session_id = _sid
                # SDK 级结果消息（超轮、超预算、执行错误、正常完成）
                # S3: 软读 SDK 0.2.87 新字段 api_error_status（0.1.76 引入）/ errors（0.1.51 引入）
                api_error_status = getattr(message, "api_error_status", None)
                errors_list = getattr(message, "errors", None)

                if message.is_error:
                    error_display = {
                        "error_during_execution": "执行过程中发生错误",
                        "error_max_turns": f"已达最大轮数限制 ({message.num_turns} 轮)",
                        "error_max_budget_usd": f"已达预算上限 (${message.total_cost_usd:.2f})",
                        "error_max_structured_output_retries": "结构化输出重试失败",
                    }.get(message.subtype, f"未知 SDK 错误: {message.subtype}")

                    error_extra: dict[str, Any] | None = None
                    if api_error_status is not None or errors_list:
                        error_extra = {}
                        if api_error_status is not None:
                            error_extra["httpStatus"] = api_error_status
                        if errors_list:
                            error_extra["errors"] = list(errors_list)

                    yield StreamChunk(
                        type="text",
                        content=f"\n[SDK 错误] {error_display}\n",
                        error_type="sdk_error",
                        error_content=message.subtype,
                        error_extra=error_extra,
                    )
                if self.verbose:
                    suffix = f", httpStatus={api_error_status}" if api_error_status is not None else ""
                    self._agent_logger.log_info(
                        f"[Result] subtype={message.subtype}, cost=${message.total_cost_usd or 0:.4f}, "
                        f"turns={message.num_turns}, duration={message.duration_ms}ms{suffix}"
                    )
                    # W3 v3: SDK ResultMessage usage / model_usage 原样透传（诊断 #974）
                    # 不做计算/比例/累加 —— 字段名和值原封不动 dump，由读日志的人解读
                    # （历史：v1/v2 用 ratio=read/(read+creation)，稳态会话下数学恒 100%，无诊断价值）
                    if message.usage:
                        self._agent_logger.log_info(
                            f"[Usage] {json.dumps(message.usage, ensure_ascii=False, default=str)}"
                        )
                    if message.model_usage:
                        self._agent_logger.log_info(
                            f"[ModelUsage] {json.dumps(message.model_usage, ensure_ascii=False, default=str)}"
                        )

            # S4: RateLimitEvent 分支（SDK 0.1.49+）
            # 注意：RateLimitEvent 是独立 dataclass（types.py:1213-1224），非 SystemMessage 子类，
            # 但前置仍属防御性最佳实践；与下方 Task* 三类前置策略保持一致。
            elif isinstance(message, RateLimitEvent):
                info = message.rate_limit_info
                if self.verbose:
                    self._agent_logger.log_info(
                        f"[RateLimit] status={info.status}, type={info.rate_limit_type}, "
                        f"utilization={info.utilization}, resets_at={info.resets_at}"
                    )
                yield StreamChunk(
                    type="rate_limit",
                    content=info.status,
                    extra={
                        "status": info.status,
                        "rateLimitType": info.rate_limit_type,
                        "utilization": info.utilization,
                        "resetsAt": info.resets_at,
                    },
                )

            # S4: TaskStartedMessage 分支（SDK 0.1.46+）
            # 硬约束：TaskStartedMessage / TaskProgressMessage / TaskNotificationMessage 都是 SystemMessage 子类
            # （SDK types.py:1059-1110），必须前置于 SystemMessage 分支，否则被父类 isinstance 吞掉。
            # 决策 B：KISS 只 verbose log，不动 _active_subagents 结构、不再 yield subagent_start
            # （现有 ToolUseBlock(name="Task") 已 yield 过，避免双触发）。
            elif isinstance(message, TaskStartedMessage):
                if self.verbose:
                    self._agent_logger.log_info(
                        f"[TaskStarted] task_id={message.task_id}, desc={message.description}, "
                        f"tool_use_id={message.tool_use_id}"
                    )
                # Task 页运行态全阶段预声明:workflow 任务启动即推完整 phases（命中暂存才推）
                await self._maybe_emit_workflow_phases(message)
                # 回合内也推 SSE 心跳（双通道：chat 流 subagent_* 照旧）：活动灯/后台任务卡片的
                # 数据源只接 SSE——此前回合内静默，导致 todo 面板与后台任务灯永远无法同屏。
                # 须在 phases 之后（先记 workflow task_id，isWorkflow 标记才正确）。
                await self._push_background_progress(message)

            # S4: TaskProgressMessage 分支（SDK 0.1.46+）
            # 通过 tool_use_id 反查 subagent_id（_active_subagents 当前结构 dict[tool_use_id, subagent_id]）。
            # usage 是 TaskUsage TypedDict，运行时本质 dict，可直接 .get / dict() 转换。
            elif isinstance(message, TaskProgressMessage):
                subagent_id = self._active_subagents.get(message.tool_use_id) if message.tool_use_id else None
                # 降噪（7.3）：对齐后台路径（:829-832 只推前端、不打 console）——逐 tick log 无 console 价值、
                # 内联 workflow 下刷屏。实时进度仍经下方 subagent_progress 投递 Task 页。
                yield StreamChunk(
                    type="subagent_progress",
                    subagent_id=subagent_id,
                    task_id=message.task_id,
                    content=message.description,
                    tool_name=message.last_tool_name,
                    usage=dict(message.usage) if message.usage else None,
                )
                # 回合内也推 SSE 心跳（双通道）：理由同 TaskStartedMessage 分支——
                # 活动灯/后台任务卡片只接 SSE，回合内静默会让它们在回合期间不可见。
                await self._push_background_progress(message)

            # S4: TaskNotificationMessage 分支（SDK 0.1.46+）
            # 注意：实测（2026-06-11 金凤127 chat_20260611_153658.log）CLI 在回合内不向宿主
            # 投递 task_notification——通知作为 queued_command 注入主控 prompt 流后从队列
            # remove，本分支在"回合内完成"场景不触发；该场景的前端收口由 Web 端
            # reapStaleBackgroundTasks（回合结束+心跳静默）兜底。本分支保留作 CLI 未来
            # 行为变化的防御：若真触发，登记 pending 后由回合末 _flush_pending_background 收口。
            elif isinstance(message, TaskNotificationMessage):
                if self.verbose:
                    self._agent_logger.log_info(
                        f"[TaskNotification] task_id={message.task_id}, status={message.status}, "
                        f"output_file={message.output_file}, summary_len={len(message.summary or '')}"
                    )
                if self._bg_completion_pending is not None:
                    # N1 防覆写：总结槽归首任务；并行完成通知裸投递终态，不丢弃（同 background 分支）。
                    if self.verbose:
                        self._agent_logger.log_info(
                            f"[TaskNotification] pending 占用中，并行完成通知裸投递 "
                            f"(keep task_id={self._bg_completion_pending.get('taskId')}, "
                            f"bare task_id={message.task_id})"
                        )
                    await self._emit_bare_background_completion(
                        message.task_id, str(message.status),
                        summary=message.summary or "",
                        output_file=message.output_file or None,
                        sdk_session_id=message.session_id,
                    )
                else:
                    self._bg_completion_pending = {
                        "taskId": message.task_id,
                        "status": str(message.status),
                        "outputFile": message.output_file or None,
                        "sdkSessionId": message.session_id,
                        "fallback": message.summary or "",
                    }
                    self._bg_summary_parts = []
                    self._bg_round_had_tool = False

            elif isinstance(message, SystemMessage):
                # SDK 级系统消息（会话初始化、上下文压缩等）
                # 注意：此分支必须降到 Task* 三类之后，否则会先吞掉子类消息。
                if self.verbose:
                    self._agent_logger.log_info(f"[System] subtype={message.subtype}")

            else:
                # S4: 未知顶层消息类型兜底（SDK 未来扩展时不静默丢失）
                if self.verbose:
                    self._agent_logger.log_warning(
                        f"[UnknownMessage] Unhandled top-level message: {type(message).__name__}"
                    )

        # 回合末兜底 flush：实测 CLI 在回合内不向宿主投递 task_notification（pending 在
        # "回合内完成"场景从不登记，此调用通常 no-op）——保留以防御 CLI 未来行为变化 /
        # 极端时序下回合内登记了 pending 的情形（content="" → 前端仅收口面板、不注气泡）。
        # "回合内完成"场景的前端收口由 Web 端 reapStaleBackgroundTasks 负责。
        await self._flush_pending_background()

        if self.verbose:
            self._agent_logger.log_complete(model=self._completion_model_stamp())
        # 注：_active_turn 复位与 clear_runtime_context 由外壳 chat_stream 的 finally 统一处理

    # ─────────────────────────────────────────────────────
    # Control Methods
    # ─────────────────────────────────────────────────────

    async def interrupt(self) -> None:
        """Interrupt the current task."""
        if self._client and self._connected:
            await self._client.interrupt()
            logger.info("MainAgent task interrupted")
            if self.verbose:
                self._agent_logger.log_warning("Task interrupted by user")

    def clear_history(self) -> None:
        """Clear conversation history (reconnect)."""
        asyncio.create_task(self._reset_session())
        if self.verbose:
            self._agent_logger.log_info("Conversation history cleared")

    async def _reset_session(self) -> None:
        """Reset the conversation session."""
        await self.disconnect()

    def get_history(self) -> list[dict]:
        """Get conversation history."""
        return []

    def get_current_model(self) -> str | None:
        """获取当前模型名称"""
        return self._current_model

    def set_project_path(self, project_path: str) -> None:
        """Set project path (triggers reconnect)."""
        if self.project_path != project_path:
            self.project_path = project_path
            self.working_directory = project_path
            asyncio.create_task(self.disconnect())
            if self.verbose:
                self._agent_logger.log_info(f"Project path changed to: {project_path}")

    def set_verbose(self, verbose: bool) -> None:
        """Enable or disable verbose logging."""
        self.verbose = verbose

    # ─────────────────────────────────────────────────────
    # Parallel Layout Methods
    # ─────────────────────────────────────────────────────

    def _get_worktree_manager(self) -> WorktreeManager:
        """获取或创建 WorktreeManager 实例"""
        if self._worktree_manager is None:
            if not self.project_path:
                raise ValueError("Project path not set, cannot create WorktreeManager")
            self._worktree_manager = WorktreeManager(self.project_path)
        return self._worktree_manager

    async def parallel_layout(
        self,
        zone_ids: list[str],
        max_parallel: int = 3
    ) -> dict[str, bool]:
        """
        并行布置多个分区

        为每个分区创建独立的 Worktree，在隔离环境中执行布置任务，
        完成后合并结果到主分支。

        Args:
            zone_ids: 要布置的分区 ID 列表
            max_parallel: 最大并行数（默认 3）

        Returns:
            字典 {zone_id: success}，表示每个分区的布置结果
        """
        if not self._connected:
            await self.connect()

        manager = self._get_worktree_manager()
        results: dict[str, bool] = {}

        if self.verbose:
            self._agent_logger.log_info(f"开始并行布置 {len(zone_ids)} 个分区（最大并行: {max_parallel}）")

        # 使用 Semaphore 限制并行数
        semaphore = asyncio.Semaphore(max_parallel)

        async def layout_zone(zone_id: str) -> tuple[str, bool]:
            """布置单个分区"""
            async with semaphore:
                try:
                    # 1. 创建 Worktree
                    context = await manager.create_for_subagent(f"zone_{zone_id}")
                    if not context:
                        logger.error(f"Failed to create worktree for zone {zone_id}")
                        return zone_id, False

                    if self.verbose:
                        self._agent_logger.log_info(f"分区 {zone_id} Worktree 已创建: {context.path}")

                    # 2. 执行布置任务（调用 SubAgent）
                    # 这里简化处理，实际应该调用专门的布置 SubAgent
                    layout_prompt = f"""
                    请在分区 {zone_id} 中执行家具布置任务。
                    工作目录: {context.path}
                    分支: {context.branch_name}

                    请完成以下步骤：
                    1. 读取分区的空间数据
                    2. 根据分区类型选择合适的布置策略
                    3. 生成模块布置方案
                    4. 写入 modules.json
                    """

                    # 注意：这里应该使用专门的 SubAgent，但为了简化先用 chat
                    # 实际实现应该派发到 layout-agent SubAgent
                    response = await self.chat(layout_prompt, model=self._current_model)

                    if self.verbose:
                        self._agent_logger.log_info(f"分区 {zone_id} 布置完成")

                    # 3. 提交并合并
                    merge_success = await manager.commit_and_merge(
                        f"zone_{zone_id}",
                        f"Auto layout zone {zone_id}"
                    )

                    # 4. 清理 Worktree
                    await manager.remove(f"zone_{zone_id}")

                    return zone_id, merge_success

                except Exception as e:
                    logger.error(f"Failed to layout zone {zone_id}: {e}")
                    # 尝试清理
                    await manager.remove(f"zone_{zone_id}")
                    return zone_id, False

        # 并行执行所有分区布置
        tasks = [layout_zone(zone_id) for zone_id in zone_ids]
        completed = await asyncio.gather(*tasks, return_exceptions=True)

        # 收集结果
        for item in completed:
            if isinstance(item, Exception):
                logger.error(f"Layout task exception: {item}")
            elif isinstance(item, tuple):
                zone_id, success = item
                results[zone_id] = success

        # 统计结果
        success_count = sum(1 for v in results.values() if v)
        if self.verbose:
            self._agent_logger.log_info(
                f"并行布置完成: {success_count}/{len(zone_ids)} 成功"
            )

        return results

    async def cleanup_parallel_worktrees(self) -> int:
        """
        清理所有并行任务的 Worktree

        Returns:
            清理的 Worktree 数量
        """
        if self._worktree_manager is None:
            return 0

        count = await self._worktree_manager.cleanup_all()
        if self.verbose:
            self._agent_logger.log_info(f"已清理 {count} 个并行 Worktree")
        return count
