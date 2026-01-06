"""MainAgent - BIMCanvas coordinator using Agent SDK with SubAgent support."""

import asyncio
import logging
from typing import AsyncIterator
from dataclasses import dataclass

from claude_agent_sdk import (
    ClaudeSDKClient,
    ClaudeAgentOptions,
    AssistantMessage,
    TextBlock,
    ThinkingBlock,
)

from ..config.settings import get_settings
from .prompts import MAIN_AGENT_PROMPT
from .subagents import create_subagents

logger = logging.getLogger(__name__)


@dataclass
class StreamChunk:
    """流式响应块"""
    type: str  # "thinking" | "text" | "delta" | "thinking_complete" | "text_complete"
    content: str


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
    """

    def __init__(self, project_path: str = None):
        """
        Initialize the MainAgent.

        Args:
            project_path: Path to the current project
        """
        self.project_path = project_path

        # SubAgent definitions
        self._subagents = create_subagents()

        # ClaudeSDKClient instance management
        self._client: ClaudeSDKClient | None = None
        self._connected = False
        self._lock = asyncio.Lock()

    # ─────────────────────────────────────────────────────
    # Configuration
    # ─────────────────────────────────────────────────────

    def _create_options(self) -> ClaudeAgentOptions:
        """Create agent options with SubAgent support."""
        settings = get_settings()
        return ClaudeAgentOptions(
            system_prompt=MAIN_AGENT_PROMPT,
            cwd=self.project_path,
            max_turns=20,  # Allow multiple turns for SubAgent dispatch
            model=settings.model_name,
            # Enable Task tool for SubAgent dispatch + file tools for context
            allowed_tools=["Read", "Glob", "Grep", "Task"],
            # Register SubAgents
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

    async def disconnect(self) -> None:
        """Disconnect from the agent."""
        async with self._lock:
            if self._client and self._connected:
                await self._client.disconnect()
                self._connected = False
                self._client = None
                logger.info(f"MainAgent disconnected for project: {self.project_path}")

    # ─────────────────────────────────────────────────────
    # Chat Methods (Unified Entry Point)
    # ─────────────────────────────────────────────────────

    async def chat(self, user_message: str) -> str:
        """
        Unified chat interface.

        The AI autonomously decides whether to:
        - Answer directly (simple questions)
        - Dispatch to layout-agent (furniture placement tasks)

        Args:
            user_message: The user's input message

        Returns:
            AI assistant's response text
        """
        if not self._connected:
            await self.connect()

        await self._client.query(user_message)

        full_response = ""
        async for message in self._client.receive_response():
            if isinstance(message, AssistantMessage):
                for block in message.content:
                    if isinstance(block, TextBlock):
                        full_response += block.text

        return full_response

    async def chat_stream(self, user_message: str) -> AsyncIterator[StreamChunk]:
        """
        Streaming chat interface with thinking support.

        Args:
            user_message: The user's input message

        Yields:
            StreamChunk objects containing type and content
        """
        if not self._connected:
            await self.connect()

        await self._client.query(user_message)

        async for message in self._client.receive_response():
            # Handle streaming delta events (duck typing check)
            if hasattr(message, 'event'):
                event = message.event
                event_type = event.get("type", "")

                # Handle content block delta
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

            # Handle complete messages (as fallback)
            elif isinstance(message, AssistantMessage):
                for block in message.content:
                    if isinstance(block, ThinkingBlock):
                        yield StreamChunk(type="thinking_complete", content=block.thinking)
                    elif isinstance(block, TextBlock):
                        yield StreamChunk(type="text_complete", content=block.text)

    # ─────────────────────────────────────────────────────
    # Control Methods
    # ─────────────────────────────────────────────────────

    async def interrupt(self) -> None:
        """Interrupt the current task."""
        if self._client and self._connected:
            await self._client.interrupt()
            logger.info("MainAgent task interrupted")

    def clear_history(self) -> None:
        """Clear conversation history (reconnect)."""
        asyncio.create_task(self._reset_session())

    async def _reset_session(self) -> None:
        """Reset the conversation session."""
        await self.disconnect()
        # Next chat() call will auto-reconnect

    def get_history(self) -> list[dict]:
        """
        Get conversation history.

        Note: ClaudeSDKClient manages history internally.

        Returns:
            Empty list (history managed by SDK internally)
        """
        return []

    def set_project_path(self, project_path: str) -> None:
        """
        Set project path (triggers reconnect).

        Args:
            project_path: Path to the project
        """
        if self.project_path != project_path:
            self.project_path = project_path
            # Path change requires reconnection
            asyncio.create_task(self.disconnect())
