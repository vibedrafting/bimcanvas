"""MainAgent - BIMCanvas coordinator using Agent SDK with SubAgent support."""

import asyncio
import json
import logging
import os
import re
from typing import Any, AsyncIterator

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
from .errors import CLICommandLineTooLongError, SystemPromptFileWriteError

logger = logging.getLogger(__name__)

# WP-2 CLAUDECODE: 进程级 flag,确保 WARNING 只打一次(__init__ 内 once-触发)
# SDK 0.1.51 PR #732 起,SDK 已自动剥离 CLAUDECODE env;此 flag 仅提醒"无需手动设"
_claudecode_warned: bool = False

_UNKNOWN_MODEL_VALUES = {"", "unknown"}


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

        # 组3: long-lived aiohttp.ClientSession,供 PluginContext / load_scene_artifact 共享。
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

        # 当前请求的模型（用于 set_model 检查，避免重复发送控制消息）
        self._current_model: str | None = None
        # API 响应的模型（用于日志显示，可能与请求模型名称不同）
        self._response_model: str | None = None

        # Worktree 管理器（用于并行布置）
        self._worktree_manager: WorktreeManager | None = None
        self._runtime_context: dict[str, str] | None = None

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

    def clear_runtime_context(self) -> None:
        """Clear host-provided runtime context after the current turn."""
        self._runtime_context = None

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
            # 组3: lazy 创建 long-lived aiohttp session,供 load_scene_artifact / plugin 工具共享
            if self._owned_session is None:
                self._owned_session = aiohttp.ClientSession()
            self.configure(build_config_bundle(session=self._owned_session))
        assert self._bundle is not None
        return self._bundle

    # ─────────────────────────────────────────────────────
    # Configuration
    # ─────────────────────────────────────────────────────

    def _create_options(self, effort: str = None, thinking: str = None, model: str = None) -> ClaudeAgentOptions:
        """
        Create agent options with SubAgent support.

        Args:
            effort: 推理深度 ("low"/"medium"/"high"/"max")，None 使用默认配置
            thinking: 扩展思考开关 ("off"/"adaptive")，None 使用默认配置
            model: 模型名称
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

        # WP-2 M2.1: 落盘到 BIMCANVAS_HOME/cache/system_prompt.window_{seq}.runtime.md,
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

    async def connect(self, effort: str = None, thinking: str = None, model: str = None) -> None:
        """
        Establish persistent connection.

        Args:
            effort: 推理深度 ("low"/"medium"/"high"/"max")，None 使用默认配置
            thinking: 扩展思考开关 ("off"/"adaptive")，None 使用默认配置
            model: 模型名称；首次连接必须提供，后续可复用当前模型
        """
        async with self._lock:
            if self._connected:
                return
            resolved_model = model or self._current_model
            if not resolved_model:
                raise ValueError("Model is required before establishing the first connection")

            options = self._create_options(effort, thinking, resolved_model)

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
            if self.verbose:
                self._agent_logger.log_info(f"Connected to project: {self.project_path or 'default'}")

    async def disconnect(self) -> None:
        """Disconnect from the agent with force-kill fallback."""
        async with self._lock:
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
                        tool_name = self._current_tool_name or "unknown"
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
            if self.verbose:
                self._agent_logger.log_tool_use(tool_name, tool_input)
                self._current_tool_name = tool_name
                if tool_name == "Task":
                    subagent_type = tool_input.get("subagent_type", "unknown")
                    self._agent_logger.enter_subagent(subagent_type)

        elif event_type == "tool_result":
            tool_name = self._current_tool_name or event.get("tool_name", "unknown")
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
        self.set_runtime_context(runtime_context)
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

            await self._client.query(user_message)

            full_response = ""
            async for message in self._client.receive_response():
                text = self._process_message(message)
                full_response += text

            if self.verbose:
                if self._in_response:
                    self._agent_logger.log_response_end()
                self._agent_logger.log_complete(model=self._completion_model_stamp())

            return full_response
        finally:
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
                        "mcp__canvas__analyze_image，"
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
        self._tool_to_subagent.clear()

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

            await self._client.query(message_stream())
        else:
            raise ValueError("Message or attachments cannot be empty")

        async for message in self._client.receive_response():
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
                        tool_name = self._current_tool_name or "unknown"
                        is_error = getattr(block, 'is_error', False)
                        if self.verbose:
                            self._agent_logger.log_tool_result(tool_name, block.content, is_error)

                        # 使用 tool_use_id 精确匹配
                        block_tool_use_id = getattr(block, 'tool_use_id', None)

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

            # 处理 UserMessage 中的 ToolResultBlock（工具调用完成）
            elif isinstance(message, UserMessage):
                for block in message.content:
                    if isinstance(block, ToolResultBlock):
                        tool_name = self._current_tool_name or "unknown"
                        is_error = getattr(block, 'is_error', False)
                        block_tool_use_id = getattr(block, 'tool_use_id', None)
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
                    # W3: cache 命中率埋点（诊断 SDK #974 bundled CLI 多轮 cache miss）
                    usage = getattr(message, "usage", None) or {}
                    cache_read = usage.get("cache_read_input_tokens") or 0
                    cache_creation = usage.get("cache_creation_input_tokens") or 0
                    # W3 fallback（2026-05-29 实测预言成真）：多 model 路由场景下（如 deepseek 代理
                    # 网关）cache 计数下沉到 model_usage[<model>]，顶层 usage 为 None。SDK 是 raw
                    # dict 透传（claude_agent_sdk/_internal/message_parser.py:258,261）。
                    if cache_read == 0 and cache_creation == 0:
                        model_usage = getattr(message, "model_usage", None) or {}
                        for mu in model_usage.values():
                            if isinstance(mu, dict):
                                cache_read += mu.get("cache_read_input_tokens") or 0
                                cache_creation += mu.get("cache_creation_input_tokens") or 0
                    total = cache_read + cache_creation
                    if total > 0:
                        ratio = cache_read / total * 100
                        self._agent_logger.log_info(
                            f"[Cache] read={cache_read}, creation={cache_creation}, "
                            f"ratio={ratio:.1f}%"
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

            # S4: TaskProgressMessage 分支（SDK 0.1.46+）
            # 通过 tool_use_id 反查 subagent_id（_active_subagents 当前结构 dict[tool_use_id, subagent_id]）。
            # usage 是 TaskUsage TypedDict，运行时本质 dict，可直接 .get / dict() 转换。
            elif isinstance(message, TaskProgressMessage):
                subagent_id = self._active_subagents.get(message.tool_use_id) if message.tool_use_id else None
                if self.verbose:
                    usage = message.usage or {}
                    self._agent_logger.log_info(
                        f"[TaskProgress] task_id={message.task_id}, "
                        f"tokens={usage.get('total_tokens')}, "
                        f"tool_uses={usage.get('tool_uses')}, "
                        f"last_tool={message.last_tool_name}"
                    )
                yield StreamChunk(
                    type="subagent_progress",
                    subagent_id=subagent_id,
                    task_id=message.task_id,
                    content=message.description,
                    tool_name=message.last_tool_name,
                    usage=dict(message.usage) if message.usage else None,
                )

            # S4: TaskNotificationMessage 分支（SDK 0.1.46+）
            # 并存观察策略：不切现有 ToolResultBlock 完成路径（行 ~1214），避免双触发；
            # 仅 verbose log，待后续 WP 视前端契约需求决定是否切主路径。
            elif isinstance(message, TaskNotificationMessage):
                if self.verbose:
                    self._agent_logger.log_info(
                        f"[TaskNotification] task_id={message.task_id}, status={message.status}, "
                        f"output_file={message.output_file}, summary_len={len(message.summary or '')}"
                    )

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

        if self.verbose:
            self._agent_logger.log_complete(model=self._completion_model_stamp())
        self.clear_runtime_context()

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
