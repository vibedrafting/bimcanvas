"""PlacementAgent - AI-powered interior layout assistant using Agent SDK"""

from typing import AsyncIterator

from claude_agent_sdk import query, ClaudeAgentOptions, AssistantMessage, TextBlock


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
        self.session_id: str | None = None  # Agent SDK 会话管理

    async def chat(self, user_message: str) -> str:
        """
        Process user message and return AI response.

        Args:
            user_message: The user's input message

        Returns:
            AI assistant's response text
        """
        options = ClaudeAgentOptions(
            system_prompt=SYSTEM_PROMPT,
            cwd=self.project_path,  # 设置工作目录
            max_turns=1,  # P1.5: 单轮对话
            # P2 阶段将启用工具：
            # allowed_tools=["Read", "Write", "Glob"],
            # permission_mode="acceptEdits"
        )

        # 如果有会话，恢复上下文
        if self.session_id:
            options.resume = self.session_id

        full_response = ""
        async for message in query(prompt=user_message, options=options):
            # 捕获会话 ID
            if hasattr(message, 'subtype') and message.subtype == 'init':
                self.session_id = message.data.get('session_id')

            # 提取文本响应
            if isinstance(message, AssistantMessage):
                for block in message.content:
                    if isinstance(block, TextBlock):
                        full_response += block.text

        return full_response

    async def chat_stream(self, user_message: str) -> AsyncIterator[str]:
        """
        Process user message and stream AI response.

        Args:
            user_message: The user's input message

        Yields:
            Chunks of the AI response as they arrive
        """
        options = ClaudeAgentOptions(
            system_prompt=SYSTEM_PROMPT,
            cwd=self.project_path,
            max_turns=1
        )

        if self.session_id:
            options.resume = self.session_id

        async for message in query(prompt=user_message, options=options):
            # 捕获会话 ID
            if hasattr(message, 'subtype') and message.subtype == 'init':
                self.session_id = message.data.get('session_id')

            # 提取并流式返回文本
            if isinstance(message, AssistantMessage):
                for block in message.content:
                    if isinstance(block, TextBlock):
                        yield block.text

    def clear_history(self) -> None:
        """Clear conversation history (reset session)"""
        self.session_id = None

    def get_history(self) -> list[dict]:
        """
        Get the current conversation history.

        Note: Agent SDK manages history internally via session_id.
        This method returns an empty list as history is not directly accessible.

        Returns:
            Empty list (history managed by Agent SDK)
        """
        return []

    def set_project_path(self, project_path: str) -> None:
        """
        Set the current project path.

        Args:
            project_path: Path to the project
        """
        self.project_path = project_path
