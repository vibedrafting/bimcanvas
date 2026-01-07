"""MainAgent - BIMCanvas coordinator using Agent SDK with SubAgent support."""

import asyncio
import logging
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
from .prompts import MAIN_AGENT_PROMPT
from .subagents import create_subagents
from .agent_logger import get_agent_logger

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

    def __init__(self, project_path: str = None, verbose: bool = True):
        """
        Initialize the MainAgent.

        Args:
            project_path: Path to the current project
            verbose: Enable detailed console logging
        """
        self.project_path = project_path
        self.verbose = verbose

        # SubAgent definitions
        self._subagents = create_subagents()

        # Agent logger for console output
        self._agent_logger = get_agent_logger("MainAgent")

        # ClaudeSDKClient instance management
        self._client: ClaudeSDKClient | None = None
        self._connected = False
        self._lock = asyncio.Lock()

        # State tracking for logging
        self._in_thinking = False
        self._in_response = False
        self._current_tool_name = None

        # SubAgent/ToolCall 状态跟踪（用于 SSE 事件）
        self._current_subagent_id: str | None = None
        self._tool_call_counter = 0
        self._task_tool_use_id: str | None = None  # Task 的 tool_use_id，用于匹配 ToolResultBlock
        self._prev_parent_tool_use_id: str | None = None  # 上一条消息的 parent_tool_use_id
        self._pending_tool_calls: dict[str, str] = {}  # tool_use_id -> tool_call_id 映射

    # ─────────────────────────────────────────────────────
    # Configuration
    # ─────────────────────────────────────────────────────

    def _create_options(self) -> ClaudeAgentOptions:
        """Create agent options with SubAgent support."""
        settings = get_settings()
        return ClaudeAgentOptions(
            system_prompt=MAIN_AGENT_PROMPT,
            cwd=self.project_path,
            max_turns=20,
            model=settings.model_name,
            allowed_tools=["Read", "Glob", "Grep", "Task"],
            agents=self._subagents,
            permission_mode="acceptEdits",
        )

    # ─────────────────────────────────────────────────────
    # Connection Management
    # ─────────────────────────────────────────────────────

    async def connect(self) -> None:
        """Establish persistent connection."""
        async with self._lock:
            if self._connected:
                return
            options = self._create_options()
            self._client = ClaudeSDKClient(options)
            await self._client.connect()
            self._connected = True
            logger.info(f"MainAgent connected for project: {self.project_path}")
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

    # ─────────────────────────────────────────────────────
    # Message Processing with Logging
    # ─────────────────────────────────────────────────────

    def _process_message(self, message) -> str:
        """Process a message from the SDK and log it."""
        text_content = ""

        if isinstance(message, AssistantMessage):
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
                        self._agent_logger.log_tool_use(block.name, block.input)
                        self._current_tool_name = block.name
                        if block.name == "Task":
                            subagent_type = block.input.get("subagent_type", "unknown")
                            self._agent_logger.enter_subagent(subagent_type)

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
            self._agent_logger.log_complete()

        return full_response

    async def chat_stream(self, user_message: str) -> AsyncIterator[StreamChunk]:
        """Streaming chat interface with thinking support."""
        if not self._connected:
            await self.connect()

        if self.verbose:
            self._agent_logger.log_user_message(user_message)

        self._in_thinking = False
        self._in_response = False
        self._current_tool_name = None
        # 重置 SubAgent/ToolCall 状态
        self._current_subagent_id = None
        self._tool_call_counter = 0
        self._task_tool_use_id = None
        self._prev_parent_tool_use_id = None

        await self._client.query(user_message)

        async for message in self._client.receive_response():
            # 检测 parent_tool_use_id 变化（SubAgent 完成检测）
            current_parent_id = getattr(message, 'parent_tool_use_id', None)

            # 如果从 SubAgent 内部退出（parent_tool_use_id 从有值变为 None）
            if (self._prev_parent_tool_use_id is not None and
                current_parent_id is None and
                self._current_subagent_id is not None):
                # SubAgent 完成
                yield StreamChunk(
                    type="subagent_complete",
                    subagent_id=self._current_subagent_id,
                    content="",  # 结果在后续 TextBlock 中
                    success=True
                )
                self._current_subagent_id = None
                self._task_tool_use_id = None

            self._prev_parent_tool_use_id = current_parent_id

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
                            yield StreamChunk(type="text", content=text)
                    elif delta_type == "thinking_delta":
                        thinking = delta.get("thinking", "")
                        if thinking:
                            yield StreamChunk(type="thinking", content=thinking)

                # 处理 tool_result 事件 - 工具执行完成
                elif event_type == "tool_result":
                    tool_name = event.get("tool_name") or self._current_tool_name or "unknown"
                    result = event.get("result", "")
                    is_error = event.get("is_error", False)

                    # 判断是否是 Task（SubAgent）的结果
                    if tool_name == "Task" or self._task_tool_use_id:
                        # SubAgent 完成
                        yield StreamChunk(
                            type="subagent_complete",
                            subagent_id=self._current_subagent_id,
                            content=str(result)[:500] if result else "",
                            success=not is_error,
                            error=str(result) if is_error else None
                        )
                        self._current_subagent_id = None
                        self._task_tool_use_id = None
                    else:
                        # 普通工具调用完成
                        yield StreamChunk(
                            type="tool_call_complete",
                            tool_call_id=f"tc-{self._tool_call_counter}",
                            tool_output=str(result)[:1000] if result else "",
                            success=not is_error,
                            error=str(result) if is_error else None
                        )
                    self._current_tool_name = None

            elif isinstance(message, AssistantMessage):
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
                        yield StreamChunk(type="text_complete", content=block.text)
                    elif isinstance(block, ToolUseBlock):
                        self._current_tool_name = block.name
                        if self.verbose:
                            self._agent_logger.log_tool_use(block.name, block.input)

                        if block.name == "Task":
                            # SubAgent 开始
                            subagent_type = block.input.get("subagent_type", "general-purpose")
                            subagent_name = block.input.get("description", "SubAgent")
                            self._current_subagent_id = f"sa-{block.id}"
                            self._task_tool_use_id = block.id  # 保存 tool_use_id 用于后续匹配
                            if self.verbose:
                                self._agent_logger.enter_subagent(subagent_type)
                            yield StreamChunk(
                                type="subagent_start",
                                subagent_id=self._current_subagent_id,
                                subagent_name=subagent_name,
                                subagent_type=subagent_type
                            )
                        else:
                            # 普通工具调用（可能在 SubAgent 上下文中）
                            self._tool_call_counter += 1
                            tool_call_id = f"tc-{self._tool_call_counter}"
                            # 保存 tool_use_id -> tool_call_id 映射，用于 UserMessage 中的 ToolResultBlock 匹配
                            self._pending_tool_calls[block.id] = tool_call_id
                            yield StreamChunk(
                                type="tool_call_start",
                                subagent_id=self._current_subagent_id,
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

                        # 使用 tool_use_id 匹配来判断是否是 Task 的结果
                        block_tool_use_id = getattr(block, 'tool_use_id', None)
                        is_task_result = (
                            self._task_tool_use_id is not None and
                            block_tool_use_id == self._task_tool_use_id
                        )

                        if is_task_result:
                            # SubAgent 完成
                            if self.verbose:
                                self._agent_logger.exit_subagent("SubAgent")
                            result_str = str(block.content)[:500] if block.content else ""
                            yield StreamChunk(
                                type="subagent_complete",
                                subagent_id=self._current_subagent_id,
                                content=result_str,
                                success=not is_error,
                                error=str(block.content) if is_error else None
                            )
                            self._current_subagent_id = None
                            self._task_tool_use_id = None
                        else:
                            # 普通工具调用完成
                            # 使用计数器生成 tool_call_id（与 start 保持一致）
                            tool_call_id = f"tc-{self._tool_call_counter}"
                            output_str = str(block.content)[:1000] if block.content else ""
                            yield StreamChunk(
                                type="tool_call_complete",
                                tool_call_id=tool_call_id,
                                tool_output=output_str,
                                success=not is_error,
                                error=str(block.content) if is_error else None
                            )

                        self._current_tool_name = None

            # 处理 UserMessage 中的 ToolResultBlock（工具调用完成）
            elif isinstance(message, UserMessage):
                for block in message.content:
                    if isinstance(block, ToolResultBlock):
                        is_error = getattr(block, 'is_error', False)
                        block_tool_use_id = getattr(block, 'tool_use_id', None)

                        # 检查是否是 Task（SubAgent）的结果
                        is_task_result = (
                            self._task_tool_use_id is not None and
                            block_tool_use_id == self._task_tool_use_id
                        )

                        if is_task_result:
                            # SubAgent 完成 - 已通过 parent_tool_use_id 变化检测处理
                            # 这里不重复发送 subagent_complete
                            pass
                        else:
                            # 普通工具调用完成 - 使用 _pending_tool_calls 查找 tool_call_id
                            tool_call_id = self._pending_tool_calls.get(block_tool_use_id)
                            if tool_call_id:
                                output_str = str(block.content)[:1000] if block.content else ""
                                yield StreamChunk(
                                    type="tool_call_complete",
                                    subagent_id=self._current_subagent_id,
                                    tool_call_id=tool_call_id,
                                    tool_output=output_str,
                                    success=not is_error,
                                    error=str(block.content) if is_error else None
                                )
                                # 清理已处理的映射
                                del self._pending_tool_calls[block_tool_use_id]

        if self.verbose:
            self._agent_logger.log_complete()

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
