"""MainAgent - BIMCanvas coordinator using Agent SDK with SubAgent support."""

import asyncio
import logging
import re
from typing import AsyncIterator
from dataclasses import dataclass

from claude_agent_sdk import (
    ClaudeSDKClient,
    ClaudeAgentOptions,
    AssistantMessage,
    UserMessage,
    TextBlock,
    ThinkingBlock,
    ToolUseBlock,
    ToolResultBlock,
)

from ..config.settings import get_settings
from ..config.loader import get_config_loader
from .subagents import create_subagents
from .agent_logger import get_agent_logger
from .worktree_manager import WorktreeManager, WorktreeContext
from ..mcp import create_canvas_mcp, get_allowed_tools

logger = logging.getLogger(__name__)


@dataclass
class StreamChunk:
    """
    流式响应块 - 支持 SubAgent/ToolCall 事件

    事件类型：
    - thinking / thinking_complete: 思考内容
    - text / text_complete: 文本内容
    - subagent_start / subagent_complete: SubAgent 生命周期
    - tool_call_start / tool_call_output / tool_call_complete: 工具调用生命周期
    - task_output_polling: TaskOutput 工具轮询事件
    """
    type: str
    content: str = ""
    # SubAgent 事件字段
    subagent_id: str = None
    subagent_name: str = None
    subagent_type: str = None
    # ToolCall 事件字段
    tool_call_id: str = None
    tool_name: str = None
    tool_description: str = None
    tool_params: dict = None
    tool_output: str = None
    success: bool = None
    error: str = None
    # 错误分类字段
    error_type: str = None       # "recoverable" | "blocking" | None
    error_content: str = None    # 提取的错误内容（不含 XML 标签，blocking 类型）
    hidden_content: str = None   # 被过滤的内容（调试用，recoverable 类型）
    # TaskOutput 事件字段
    task_id: str = None          # 后台任务 ID
    timeout: int = None          # 轮询超时时间 (ms)


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

        # Configuration loader
        self._config_loader = get_config_loader()

        # SubAgent definitions (loaded from config)
        self._subagents = create_subagents()

        # Agent logger for console output (with window_seq for multi-window prefix)
        self._agent_logger = get_agent_logger("MainAgent", window_seq=self.window_seq)

        # ClaudeSDKClient instance management
        self._client: ClaudeSDKClient | None = None
        self._connected = False
        self._lock = asyncio.Lock()

        # State tracking for logging
        self._in_thinking = False
        self._in_response = False
        self._streamed_text = False  # 标记是否已通过流式事件输出文本，避免重复
        self._current_tool_name = None

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

        # 当前思考强度等级
        self._current_thinking_level: str | None = None

        # Worktree 管理器（用于并行布置）
        self._worktree_manager: WorktreeManager | None = None

    # ─────────────────────────────────────────────────────
    # Configuration
    # ─────────────────────────────────────────────────────

    def _create_options(self, thinking_level: str = None) -> ClaudeAgentOptions:
        """
        Create agent options with SubAgent support.

        Args:
            thinking_level: 思考强度等级，None 使用默认配置
        """
        settings = get_settings()

        # 从配置加载系统提示词和工具权限
        system_prompt = self._config_loader.load_system_prompt()

        # 追加工作目录到 system prompt，让 AI 知道自己的工作路径
        system_prompt = system_prompt + f"\n\n工作目录: {self.working_directory}"

        allowed_tools, disallowed_tools = self._config_loader.load_permissions()

        # 构建自定义环境变量（用于 Agent SDK 独立配置）
        custom_env = {}
        if settings.base_url:
            custom_env["ANTHROPIC_BASE_URL"] = settings.base_url
        if settings.anthropic_api_key:
            custom_env["ANTHROPIC_API_KEY"] = settings.anthropic_api_key

        # 获取思考强度 token 数量
        thinking_tokens = settings.thinking.get_tokens(thinking_level)

        # 创建 MCP 服务器
        canvas_mcp = None
        mcp_tools = []
        try:
            canvas_mcp = create_canvas_mcp()
            mcp_tools = get_allowed_tools()
            self._agent_logger._print(f"[MCP] MCP 服务器已创建，工具: {mcp_tools}")
        except ValueError as e:
            self._agent_logger.log_warning(f"MCP 服务器创建失败: {e}")
        except Exception as e:
            self._agent_logger.log_error(f"MCP 服务器创建异常: {e}")

        # 合并工具权限
        all_allowed = (allowed_tools or []) + mcp_tools

        return ClaudeAgentOptions(
            system_prompt=system_prompt,
            cwd=self.working_directory,
            max_turns=20,
            model=settings.model_name,
            tools=allowed_tools,                   # None 表示默认全开
            allowed_tools=all_allowed,             # 包含 MCP 工具
            disallowed_tools=disallowed_tools,     # 工具黑名单
            agents=self._subagents,
            permission_mode="acceptEdits",
            include_partial_messages=True,
            env=custom_env,                        # Agent SDK 独立环境变量
            max_thinking_tokens=thinking_tokens,   # 思考强度
            mcp_servers={"canvas": canvas_mcp} if canvas_mcp else {},  # MCP 服务器
        )

    # ─────────────────────────────────────────────────────
    # Error Filtering
    # ─────────────────────────────────────────────────────

    # 可恢复错误的模式匹配
    _RECOVERABLE_ERROR_PATTERNS = [
        r"cygpath.*fatal error",      # Git Bash cygpath 错误
        r"add_item.*failed.*errno",   # Git Bash 内部错误
        r"EBUSY.*resource busy",      # 文件锁定
    ]

    # 权限错误的模式匹配（短特征，能匹配分片传输的 text_delta）
    _PERMISSION_ERROR_PATTERNS = [
        r"haven't granted",           # "but you haven't granted it yet"
        r"requested permissions",     # "Claude requested permissions to"
        r"Permission denied",         # 通用权限拒绝
        r"requires approval",         # Bash 命令批准
        r"approval required",         # 变体
        r"needs approval",            # 变体
    ]

    def _detect_permission_error(self, text: str) -> tuple[bool, str | None]:
        """
        检测文本中是否包含权限错误
        返回: (是否为权限错误, 错误消息)
        """
        for pattern in self._PERMISSION_ERROR_PATTERNS:
            match = re.search(pattern, text, re.IGNORECASE)
            if match:
                return True, match.group(0)
        return False, None

    def _classify_tool_error(self, error_content: str) -> str:
        """分类工具调用错误：recoverable 或 blocking"""
        for pattern in self._RECOVERABLE_ERROR_PATTERNS:
            if re.search(pattern, error_content, re.IGNORECASE):
                return "recoverable"
        return "blocking"


    def _classify_tool_result_error(self, error_message: str) -> tuple[str, str | None]:
        """
        分类工具调用结果中的错误

        返回:
            (error_type, classified_message)
            - error_type: "permission_required" | "blocking" | "recoverable"
            - classified_message: 处理后的错误消息（可选）
        """
        # 1. 优先检测权限错误
        is_perm_error, perm_msg = self._detect_permission_error(error_message)
        if is_perm_error:
            return "permission_required", perm_msg

        # 2. 检测可恢复错误
        for pattern in self._RECOVERABLE_ERROR_PATTERNS:
            if re.search(pattern, error_message, re.IGNORECASE):
                return "recoverable", None

        # 3. 默认为阻塞错误
        return "blocking", None
    def _filter_recoverable_errors(self, text: str) -> tuple[str, str | None, str | None, str | None]:
        """
        过滤错误标签，返回 (清理后内容, 错误内容, 隐藏内容, 错误类型)
        - 所有 <tool_use_error> 标签都会被移除
        - recoverable 错误：内容放入 hidden_content（调试用）
        - blocking 错误：内容放入 error_content（前端可选显示）
        """
        pattern = r'<tool_use_error>([\s\S]*?)</tool_use_error>'

        hidden_parts = []      # recoverable 错误
        error_parts = []       # blocking 错误
        error_type = None

        def replace_match(m):
            nonlocal error_type
            err_content = m.group(1).strip()
            err_type = self._classify_tool_error(err_content)
            if err_type == "recoverable":
                hidden_parts.append(err_content)
                if not error_type:
                    error_type = "recoverable"
            else:
                error_parts.append(err_content)
                error_type = "blocking"  # blocking 优先级更高
            return ""  # 统一移除 XML 标签

        cleaned = re.sub(pattern, replace_match, text)
        hidden = "\n".join(hidden_parts) if hidden_parts else None
        error_content = "\n".join(error_parts) if error_parts else None

        return cleaned, error_content, hidden, error_type

    # ─────────────────────────────────────────────────────
    # Connection Management
    # ─────────────────────────────────────────────────────

    async def connect(self, thinking_level: str = None) -> None:
        """
        Establish persistent connection.

        Args:
            thinking_level: 初始思考强度等级，None 使用默认配置
        """
        async with self._lock:
            if self._connected:
                return
            options = self._create_options(thinking_level)

            # 保存初始思考强度
            settings = get_settings()
            self._current_thinking_level = thinking_level or settings.thinking.default_level

            # 调试日志：打印实际使用的配置（使用 _agent_logger 确保带窗口前缀）
            tools_display = options.tools if options.tools else "默认全开"
            deny_display = options.disallowed_tools if options.disallowed_tools else "无"
            base_url_display = options.env.get("ANTHROPIC_BASE_URL", "默认端点") if options.env else "默认端点"
            thinking_display = f"{options.max_thinking_tokens} tokens" if options.max_thinking_tokens else "禁用"
            self._agent_logger._print(f"[MainAgent] ========== 配置信息 ==========")
            self._agent_logger._print(f"[MainAgent] 模型: {options.model}")
            self._agent_logger._print(f"[MainAgent] Base URL: {base_url_display}")
            self._agent_logger._print(f"[MainAgent] 思考强度: {self._current_thinking_level} ({thinking_display})")
            self._agent_logger._print(f"[MainAgent] 允许工具: {tools_display}")
            self._agent_logger._print(f"[MainAgent] 禁止工具: {deny_display}")
            self._agent_logger._print(f"[MainAgent] 项目路径: {self.project_path}")
            self._agent_logger._print(f"[MainAgent] 工作目录: {self.working_directory}")
            self._agent_logger._print(f"[MainAgent] ================================")

            self._client = ClaudeSDKClient(options)
            await self._client.connect()
            self._connected = True
            if self.verbose:
                self._agent_logger.log_info(f"Connected to project: {self.project_path or 'default'}")

    async def disconnect(self) -> None:
        """Disconnect from the agent."""
        async with self._lock:
            if self._client and self._connected:
                await self._client.disconnect()
                self._connected = False
                self._client = None
                logger.info(f"MainAgent disconnected for project: {self.project_path}")

    async def set_thinking_level(self, level: str) -> bool:
        """
        动态调整思考强度（不断开连接）

        通过 SDK 内部控制协议发送 set_max_thinking_tokens 消息。
        注意：此方法依赖 SDK 内部 API，可能随版本更新变化。

        Args:
            level: 思考强度等级 ("off", "low", "medium", "high")

        Returns:
            是否成功调整
        """
        if not self._connected or not self._client:
            logger.warning("Cannot set thinking level: not connected")
            return False

        if level == self._current_thinking_level:
            return True  # 无需调整

        settings = get_settings()
        tokens = settings.thinking.get_tokens(level)

        try:
            # 通过底层 Query 发送控制消息
            # 参考 TypeScript SDK 的 setMaxThinkingTokens 实现
            await self._client._query._send_control_request({
                "subtype": "set_max_thinking_tokens",
                "max_thinking_tokens": tokens
            })
            self._current_thinking_level = level

            if self.verbose:
                tokens_display = f"{tokens} tokens" if tokens else "禁用"
                self._agent_logger.log_info(f"思考强度已调整: {level} ({tokens_display})")

            return True
        except Exception as e:
            logger.error(f"Failed to set thinking level: {e}")
            if self.verbose:
                self._agent_logger.log_warning(f"思考强度调整失败: {e}")
            return False

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
            self._response_model = getattr(message, 'model', None)

            for block in message.content:
                if isinstance(block, ThinkingBlock):
                    if self.verbose:
                        if not self._in_thinking:
                            self._agent_logger.log_thinking_start()
                            self._in_thinking = True
                        self._agent_logger.log_thinking(block.thinking)
                        self._agent_logger.log_thinking_end()
                        self._in_thinking = False

                elif isinstance(block, TextBlock):
                    text_content += block.text
                    if self.verbose:
                        if self._in_thinking:
                            self._agent_logger.log_thinking_end()
                            self._in_thinking = False
                        if not self._in_response:
                            self._agent_logger.log_response_start()
                            self._in_response = True
                        self._agent_logger.log_response(block.text)

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

        elif hasattr(message, 'event'):
            event = message.event
            event_type = event.get("type", "")
            self._process_streaming_event(event_type, event)

        elif isinstance(message, dict):
            self._process_dict_message(message)

        return text_content

    def _process_streaming_event(self, event_type: str, event: dict) -> None:
        """Process streaming events from the SDK."""
        if event_type == "content_block_start":
            block_type = event.get("content_block", {}).get("type", "")
            if block_type == "thinking" and self.verbose:
                if not self._in_thinking:
                    self._agent_logger.log_thinking_start()
                    self._in_thinking = True
            elif block_type == "text" and self.verbose:
                if not self._in_response:
                    self._agent_logger.log_response_start()
                    self._in_response = True
            elif block_type == "tool_use" and self.verbose:
                tool_name = event.get("content_block", {}).get("name", "")
                self._current_tool_name = tool_name

        elif event_type == "content_block_delta":
            delta = event.get("delta", {})
            delta_type = delta.get("type", "")
            if delta_type == "thinking_delta" and self.verbose:
                thinking = delta.get("thinking", "")
                if thinking:
                    self._agent_logger.log_thinking(thinking, is_delta=True)
            elif delta_type == "text_delta" and self.verbose:
                text = delta.get("text", "")
                if text:
                    self._agent_logger.log_response(text, is_delta=True)

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

    async def chat(self, user_message: str) -> str:
        """Unified chat interface."""
        if not self._connected:
            await self.connect()

        if self.verbose:
            self._agent_logger.log_user_message(user_message)

        self._in_thinking = False
        self._in_response = False
        self._current_tool_name = None

        await self._client.query(user_message)

        full_response = ""
        async for message in self._client.receive_response():
            text = self._process_message(message)
            full_response += text

        if self.verbose:
            if self._in_response:
                self._agent_logger.log_response_end()
            self._agent_logger.log_complete(model=self._response_model)

        return full_response

    async def chat_stream(
        self,
        user_message: str,
        images: list[str] = None,
        thinking_level: str = None
    ) -> AsyncIterator[StreamChunk]:
        """
        Streaming chat interface with thinking support.

        Args:
            user_message: 用户消息
            images: 图片附件列表（base64 编码，可带 data:image/png;base64, 前缀）
            thinking_level: 思考强度等级 ("off", "low", "medium", "high")
                           None 表示使用当前配置，不调整
        """
        if not self._connected:
            await self.connect(thinking_level)
        elif thinking_level is not None and thinking_level != self._current_thinking_level:
            # 已连接但需要调整思考强度
            await self.set_thinking_level(thinking_level)

        if self.verbose:
            self._agent_logger.log_user_message(user_message)

        self._in_thinking = False
        self._in_response = False
        self._streamed_text = False  # 重置流式文本标记
        self._current_tool_name = None
        # 重置 SubAgent/ToolCall 状态
        self._active_subagents.clear()
        self._tool_call_counter = 0
        self._pending_tool_calls.clear()
        self._tool_to_subagent.clear()

        # 构建消息内容（支持多模态）
        if images:
            content = []
            for img_base64 in images:
                # 移除 data:image/png;base64, 前缀
                pure_base64 = img_base64
                if "," in img_base64:
                    pure_base64 = img_base64.split(",", 1)[1]
                content.append({
                    "type": "image",
                    "source": {
                        "type": "base64",
                        "media_type": "image/png",
                        "data": pure_base64
                    }
                })
            content.append({"type": "text", "text": user_message})
            await self._client.query(content)
        else:
            await self._client.query(user_message)

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
                        text = delta.get("text", "")
                        if text:
                            # 1. 检测权限错误（纯文本形式）
                            is_perm_error, perm_msg = self._detect_permission_error(text)
                            if is_perm_error:
                                if self.verbose:
                                    self._agent_logger.log_permission_error(perm_msg)
                                # 发送权限错误事件
                                yield StreamChunk(
                                    type="text",
                                    content=text,
                                    error_type="permission_required",
                                    error_content=perm_msg
                                )
                                # 关闭当前工具调用（如果有）
                                if self._current_tool_name:
                                    yield StreamChunk(
                                        type="tool_call_complete",
                                        tool_call_id=f"tc-{self._tool_call_counter}",
                                        success=False,
                                        error=perm_msg
                                    )
                                self._streamed_text = True
                                continue  # 跳过后续处理

                            # 2. 过滤错误标签
                            cleaned, error_content, hidden, err_type = self._filter_recoverable_errors(text)

                            if hidden and self.verbose:
                                self._agent_logger.log_warning(f"过滤可恢复错误: {hidden[:200]}...")

                            if cleaned or error_content:  # 有内容或有错误时发送
                                self._streamed_text = True
                                yield StreamChunk(
                                    type="text",
                                    content=cleaned,
                                    error_type=err_type,
                                    error_content=error_content,
                                    hidden_content=hidden
                                )
                    elif delta_type == "thinking_delta":
                        thinking = delta.get("thinking", "")
                        if thinking:
                            yield StreamChunk(type="thinking", content=thinking)

                # 处理 tool_result 事件 - 工具执行完成
                elif event_type == "tool_result":
                    tool_name = event.get("tool_name") or self._current_tool_name or "unknown"
                    result = event.get("result", "")
                    is_error = event.get("is_error", False)
                    tool_use_id = event.get("tool_use_id")

                    # ✅ 对错误进行分类
                    error_type = None
                    error_message = None
                    hidden_message = None


                    # 🔍 调试日志：记录所有 tool_result 事件
                    if self.verbose:
                        self._agent_logger.log_info(f"[DEBUG] tool_result: tool_name={tool_name}, is_error={is_error}, result={str(result)[:100] if result else 'None'}")

                    if is_error and result:
                        err_type, classified_msg = self._classify_tool_result_error(str(result))

                        if err_type == "recoverable":
                            # 可恢复错误：隐藏，不影响用户
                            hidden_message = str(result)
                            is_error = False  # 标记为成功（前端不显示错误）
                            if self.verbose:
                                self._agent_logger.log_warning(f"工具调用可恢复错误: {str(result)[:200]}")
                        elif err_type == "permission_required":
                            # 权限错误：特殊标记
                            error_type = "permission_required"
                            error_message = classified_msg or str(result)
                            if self.verbose:
                                self._agent_logger.log_permission_error(str(result)[:200])
                        else:  # blocking
                            # 阻塞错误：传递给前端
                            error_type = "blocking"
                            error_message = str(result)

                            if self.verbose:
                                self._agent_logger.log_error(f"工具调用失败 ({tool_name}): {str(result)[:200]}")
                    # 判断是否是 Task（SubAgent）的结果
                    if tool_name == "Task" and tool_use_id and tool_use_id in self._active_subagents:
                        # SubAgent 完成 - 从映射中获取并清理
                        subagent_id = self._active_subagents.pop(tool_use_id)
                        if self.verbose:
                            self._agent_logger.exit_subagent(subagent_id=subagent_id)
                        yield StreamChunk(
                            type="subagent_complete",
                            subagent_id=subagent_id,
                            content=str(result)[:500] if result and not is_error else "",
                            success=not is_error,
                            error=error_message,
                            error_type=error_type,
                            hidden_content=hidden_message
                        )
                        # 重置标记，准备接收 MainAgent 后续输出（修复 SubAgent 完成后最终结论不显示的 bug）
                        self._streamed_text = False
                    else:
                        # 普通工具调用完成 - 查找关联的 SubAgent
                        tool_call_id = self._pending_tool_calls.get(tool_use_id) if tool_use_id else None
                        subagent_id = self._tool_to_subagent.get(tool_use_id) if tool_use_id else None
                        yield StreamChunk(
                            type="tool_call_complete",
                            subagent_id=subagent_id,
                            tool_call_id=tool_call_id or f"tc-{self._tool_call_counter}",
                            tool_output=str(result)[:1000] if result and not is_error else "",
                            success=not is_error,
                            error=error_message,
                            error_type=error_type,
                            hidden_content=hidden_message
                        )
                    self._current_tool_name = None

            elif isinstance(message, AssistantMessage):
                # 存储 API 响应的模型值，用于日志显示（不覆盖 _current_model）
                self._response_model = getattr(message, 'model', None)

                for block in message.content:
                    if isinstance(block, ThinkingBlock):
                        if self.verbose and not self._in_thinking:
                            self._agent_logger.log_thinking_start()
                            self._agent_logger.log_thinking(block.thinking)
                            self._agent_logger.log_thinking_end()
                        yield StreamChunk(type="thinking_complete", content=block.thinking)
                    elif isinstance(block, TextBlock):
                        if self.verbose and not self._in_response:
                            self._agent_logger.log_response_start()
                            self._agent_logger.log_response(block.text)
                            self._agent_logger.log_response_end()
                        # 如果已通过流式事件输出，跳过完整块输出（避免重复）
                        if not self._streamed_text:
                            yield StreamChunk(type="text_complete", content=block.text)
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
                            result_str = str(block.content)[:500] if block.content else ""
                            yield StreamChunk(
                                type="subagent_complete",
                                subagent_id=subagent_id,
                                content=result_str,
                                success=not is_error,
                                error=str(block.content) if is_error else None
                            )
                            # 重置标记，准备接收 MainAgent 后续输出（修复 SubAgent 完成后最终结论不显示的 bug）
                            self._streamed_text = False
                        else:
                            # 普通工具调用完成 - 查找关联信息
                            tool_call_id = self._pending_tool_calls.get(block_tool_use_id)
                            subagent_id = self._tool_to_subagent.get(block_tool_use_id)
                            output_str = str(block.content)[:1000] if block.content else ""
                            yield StreamChunk(
                                type="tool_call_complete",
                                subagent_id=subagent_id,
                                tool_call_id=tool_call_id or f"tc-{self._tool_call_counter}",
                                tool_output=output_str,
                                success=not is_error,
                                error=str(block.content) if is_error else None
                            )
                            # 清理映射
                            if block_tool_use_id:
                                self._pending_tool_calls.pop(block_tool_use_id, None)
                                self._tool_to_subagent.pop(block_tool_use_id, None)

                        self._current_tool_name = None

            # 处理 UserMessage 中的 ToolResultBlock（工具调用完成）
            elif isinstance(message, UserMessage):
                for block in message.content:
                    if isinstance(block, ToolResultBlock):
                        is_error = getattr(block, 'is_error', False)
                        block_tool_use_id = getattr(block, 'tool_use_id', None)

                        if block_tool_use_id and block_tool_use_id in self._active_subagents:
                            # SubAgent 完成 - 从映射中获取并清理
                            subagent_id = self._active_subagents.pop(block_tool_use_id)
                            if self.verbose:
                                self._agent_logger.exit_subagent(subagent_id=subagent_id)
                            result_str = str(block.content)[:500] if block.content else ""
                            yield StreamChunk(
                                type="subagent_complete",
                                subagent_id=subagent_id,
                                content=result_str,
                                success=not is_error,
                                error=str(block.content) if is_error else None
                            )
                            # 重置标记，准备接收 MainAgent 后续输出（修复 SubAgent 完成后最终结论不显示的 bug）
                            self._streamed_text = False
                        else:
                            # 普通工具调用完成 - 查找关联信息
                            tool_call_id = self._pending_tool_calls.get(block_tool_use_id)
                            subagent_id = self._tool_to_subagent.get(block_tool_use_id)
                            if tool_call_id:
                                output_str = str(block.content)[:1000] if block.content else ""
                                yield StreamChunk(
                                    type="tool_call_complete",
                                    subagent_id=subagent_id,
                                    tool_call_id=tool_call_id,
                                    tool_output=output_str,
                                    success=not is_error,
                                    error=str(block.content) if is_error else None
                                )
                                # 清理映射
                                self._pending_tool_calls.pop(block_tool_use_id, None)
                                self._tool_to_subagent.pop(block_tool_use_id, None)

        if self.verbose:
            self._agent_logger.log_complete(model=self._response_model)

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
                    response = await self.chat(layout_prompt)

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
