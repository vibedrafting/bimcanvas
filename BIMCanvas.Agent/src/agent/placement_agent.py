"""PlacementAgent - AI-powered interior layout assistant using Agent SDK"""

from typing import AsyncIterator
from dataclasses import dataclass

from claude_agent_sdk import (
    query,
    ClaudeAgentOptions,
    AssistantMessage,
    TextBlock,
    ThinkingBlock,
)

from ..config.settings import get_settings


@dataclass
class StreamChunk:
    """流式响应块"""
    type: str  # "thinking" | "text" | "delta"
    content: str


# Agent system prompt (对话模式)
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

请用简洁专业的中文回答，不要使用Emoji。"""


# Layout task system prompt (布置任务模式)
LAYOUT_SYSTEM_PROMPT = """你是 BIMCanvas 的 PlacementAgent，一个专业的室内布置助手。

## 职责
1. 理解用户的布置需求
2. 分析房间功能和空间特点
3. 执行家具布置任务，输出布置结果

## 当前项目文件结构
工作目录已设置为项目根目录，你可以直接访问以下文件：

**输入数据**（只读）：
- computed/room_zones.json - 房间分区数据，包含每个房间的边界、类型、禁区
- baseline/openings.json - 门窗数据，包含位置、方向、开启方式
- modules/ - 家具素材目录，包含可用的家具模块

**输出数据**（可写）：
- schemes/{schemeId}/modules.json - 布置结果

## 布置规则
- 大型家具尽量靠墙放置（床、衣柜、沙发）
- 电视柜居中于电视墙
- 沙发正对电视，保持合理观看距离
- 床头不靠窗，避免对流
- 家具不阻挡门的开启范围（检查 openings.json 中的 swingArc）
- 保持主要动线畅通（至少800mm通道宽度）
- 家具不能与 exclusionAreas 重叠

## 布置优先级
1. 锚点家具：确定设计区的核心家具（客厅-电视柜，卧室-床，餐厅-餐桌）
2. 主要家具：围绕锚点布置（沙发正对电视柜，床头柜在床两侧）
3. 辅助家具：填充剩余空间（茶几、边几、装饰柜）

## modules.json 输出格式
```json
{
  "modules": [
    {
      "id": "mod_1",
      "templateId": "sofa_3seat",
      "bounds": {
        "center": [x, y],
        "size": [width, height],
        "rotation": 0
      },
      "facing": "north",
      "zoneId": "rz_1"
    }
  ]
}
```

请用简洁专业的中文回答，不要使用Emoji。"""


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
        settings = get_settings()
        options = ClaudeAgentOptions(
            system_prompt=SYSTEM_PROMPT,
            cwd=self.project_path,  # 设置工作目录
            max_turns=1,  # P1.5: 单轮对话
            model=settings.model_name,  # 使用配置的模型
            resume=self.session_id,  # 会话恢复(None时为新会话)
            # P2 阶段将启用工具：
            # allowed_tools=["Read", "Write", "Glob"],
            # permission_mode="acceptEdits"
        )

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

    async def chat_stream(self, user_message: str) -> AsyncIterator[StreamChunk]:
        """
        Process user message and stream AI response with thinking process.

        Args:
            user_message: The user's input message

        Yields:
            StreamChunk objects containing type and content
        """
        settings = get_settings()
        options = ClaudeAgentOptions(
            system_prompt=SYSTEM_PROMPT,
            cwd=self.project_path,
            max_turns=1,
            model=settings.model_name,
            resume=self.session_id,
            include_partial_messages=True,  # 启用增量消息流
        )

        async for message in query(prompt=user_message, options=options):
            # 捕获会话 ID
            if hasattr(message, 'subtype') and message.subtype == 'init':
                self.session_id = message.data.get('session_id')

            # 处理流式增量事件 (使用 duck typing 检查)
            if hasattr(message, 'event'):
                event = message.event
                event_type = event.get("type", "")

                # 处理内容块增量
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

            # 处理完整消息（作为备用）
            elif isinstance(message, AssistantMessage):
                for block in message.content:
                    if isinstance(block, ThinkingBlock):
                        yield StreamChunk(type="thinking_complete", content=block.thinking)
                    elif isinstance(block, TextBlock):
                        yield StreamChunk(type="text_complete", content=block.text)

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

    async def run_layout(self, task_prompt: str, scheme_id: str = "default") -> str:
        """
        Execute a layout task with file tools enabled.

        This method enables Agent SDK built-in tools (Read, Write, Glob)
        for reading project data and writing layout results.

        Args:
            task_prompt: The layout task description from user
            scheme_id: The scheme ID for output path (default: "default")

        Returns:
            Task execution summary
        """
        # Build the full task prompt with scheme context
        full_prompt = f"""
用户请求：{task_prompt}

请执行家具布置任务：
1. 读取 computed/room_zones.json 获取房间分区数据
2. 读取 baseline/openings.json 获取门窗数据
3. 查看 modules/ 目录了解可用家具
4. 根据布置规则为每个房间布置家具
5. 将布置结果写入 schemes/{scheme_id}/modules.json

注意：输出的 modules.json 必须符合规定的格式。
"""

        settings = get_settings()
        options = ClaudeAgentOptions(
            system_prompt=LAYOUT_SYSTEM_PROMPT,
            cwd=self.project_path,
            max_turns=10,  # 允许多轮工具调用
            model=settings.model_name,
            # P2 阶段启用内置工具
            allowed_tools=["Read", "Write", "Glob", "Edit"],
            permission_mode="acceptEdits",  # 自动接受文件编辑
        )

        # 布置任务不使用会话恢复，每次独立执行
        full_response = ""
        async for message in query(prompt=full_prompt, options=options):
            if isinstance(message, AssistantMessage):
                for block in message.content:
                    if isinstance(block, TextBlock):
                        full_response += block.text

        return full_response

    async def run_layout_stream(
        self, task_prompt: str, scheme_id: str = "default"
    ) -> AsyncIterator[StreamChunk]:
        """
        Execute a layout task with streaming output.

        Args:
            task_prompt: The layout task description
            scheme_id: The scheme ID for output path

        Yields:
            StreamChunk objects containing thinking and text content
        """
        full_prompt = f"""
用户请求：{task_prompt}

请执行家具布置任务：
1. 读取 computed/room_zones.json 获取房间分区数据
2. 读取 baseline/openings.json 获取门窗数据
3. 查看 modules/ 目录了解可用家具
4. 根据布置规则为每个房间布置家具
5. 将布置结果写入 schemes/{scheme_id}/modules.json

注意：输出的 modules.json 必须符合规定的格式。
"""

        settings = get_settings()
        options = ClaudeAgentOptions(
            system_prompt=LAYOUT_SYSTEM_PROMPT,
            cwd=self.project_path,
            max_turns=10,
            model=settings.model_name,
            allowed_tools=["Read", "Write", "Glob", "Edit"],
            permission_mode="acceptEdits",
            include_partial_messages=True,
        )

        async for message in query(prompt=full_prompt, options=options):
            # 处理流式增量事件 (使用 duck typing 检查)
            if hasattr(message, 'event'):
                event = message.event
                event_type = event.get("type", "")

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

            # 处理完整消息
            elif isinstance(message, AssistantMessage):
                for block in message.content:
                    if isinstance(block, ThinkingBlock):
                        yield StreamChunk(type="thinking_complete", content=block.thinking)
                    elif isinstance(block, TextBlock):
                        yield StreamChunk(type="text_complete", content=block.text)
