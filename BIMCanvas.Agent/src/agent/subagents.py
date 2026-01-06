"""SubAgent definitions for BIMCanvas using AgentDefinition."""

from claude_agent_sdk import AgentDefinition

from .prompts import LAYOUT_AGENT_PROMPT


def create_subagents() -> dict[str, AgentDefinition]:
    """
    Create SubAgent definitions for the main agent.

    SubAgents are defined using AgentDefinition and dispatched via Task tool.
    Note: SubAgent tools should NOT include "Task" (cannot dispatch further SubAgents).

    Returns:
        Dictionary mapping agent names to their definitions
    """
    return {
        "layout-agent": AgentDefinition(
            # description: Tells Claude when to use this SubAgent
            description="家具布置专家。用于空间规划、家具摆放、布局优化任务。当用户请求布置家具、设计布局、调整摆放位置时使用。",
            # prompt: SubAgent's System Prompt
            prompt=LAYOUT_AGENT_PROMPT,
            # tools: Restricted tool set (no Task - cannot dispatch SubAgents)
            tools=["Read", "Write", "Glob"],
            # model: Inherit from parent agent
            model="inherit",
        ),
    }
