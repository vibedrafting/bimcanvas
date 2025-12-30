"""PlacementAgent - AI-powered interior layout assistant using Agent SDK"""

import asyncio
from typing import AsyncIterator

from anthropic import Anthropic

from ..config.settings import get_settings

# Agent system prompt
SYSTEM_PROMPT = """你是 BIMCanvas 的 PlacementAgent，一个专业的室内布置助手。

你的职责：
1. 理解用户的布置需求
2. 分析房间功能和空间特点
3. 为用户提供专业的布置建议
4. 执行家具布置任务

当前阶段（MVP）你可以：
- 与用户对话，理解需求
- 解答室内设计相关问题
- 提供布置方案建议

设计原则：
- 大型家具尽量靠墙放置（床、衣柜、沙发）
- 电视柜居中于电视墙
- 沙发正对电视，保持合理观看距离
- 床头不靠窗，避免对流
- 家具不阻挡门的开启范围
- 保持主要动线畅通（至少800mm通道宽度）

请用简洁专业的中文回答。"""


class PlacementAgent:
    """基于 Agent SDK 的布置助手"""

    def __init__(self, project_path: str = None):
        """
        Initialize the PlacementAgent.

        Args:
            project_path: Path to the current project (optional)
        """
        self.project_path = project_path
        self.conversation_history: list[dict] = []
        self._client: Anthropic | None = None

    @property
    def client(self) -> Anthropic:
        """Lazy initialization of Anthropic client"""
        if self._client is None:
            settings = get_settings()
            self._client = Anthropic(api_key=settings.anthropic_api_key)
        return self._client

    async def chat(self, user_message: str) -> str:
        """
        Process user message and return AI response.

        Args:
            user_message: The user's input message

        Returns:
            AI assistant's response text
        """
        settings = get_settings()

        # Add user message to history
        self.conversation_history.append({
            "role": "user",
            "content": user_message
        })

        # Call Claude API using Agent SDK pattern
        response = await asyncio.get_event_loop().run_in_executor(
            None,
            lambda: self.client.messages.create(
                model=settings.model_name,
                max_tokens=settings.max_tokens,
                system=SYSTEM_PROMPT,
                messages=self.conversation_history
            )
        )

        # Extract response content
        assistant_message = response.content[0].text

        # Add assistant response to history
        self.conversation_history.append({
            "role": "assistant",
            "content": assistant_message
        })

        return assistant_message

    async def chat_stream(self, user_message: str) -> AsyncIterator[str]:
        """
        Process user message and stream AI response.

        Args:
            user_message: The user's input message

        Yields:
            Chunks of the AI response as they arrive
        """
        settings = get_settings()

        # Add user message to history
        self.conversation_history.append({
            "role": "user",
            "content": user_message
        })

        # Use streaming for real-time response
        full_response = ""

        def create_stream():
            return self.client.messages.stream(
                model=settings.model_name,
                max_tokens=settings.max_tokens,
                system=SYSTEM_PROMPT,
                messages=self.conversation_history
            )

        # Run the stream in executor to make it async
        stream = await asyncio.get_event_loop().run_in_executor(
            None, create_stream
        )

        with stream as s:
            for text in s.text_stream:
                full_response += text
                yield text

        # Add complete response to history
        self.conversation_history.append({
            "role": "assistant",
            "content": full_response
        })

    def clear_history(self) -> None:
        """Clear conversation history"""
        self.conversation_history = []

    def get_history(self) -> list[dict]:
        """
        Get the current conversation history.

        Returns:
            List of message dictionaries
        """
        return self.conversation_history.copy()

    def set_project_path(self, project_path: str) -> None:
        """
        Set the current project path.

        Args:
            project_path: Path to the project
        """
        self.project_path = project_path
