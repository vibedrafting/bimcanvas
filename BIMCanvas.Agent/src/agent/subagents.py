"""SubAgent definitions for BIMCanvas - loaded from configuration files."""

from claude_agent_sdk import AgentDefinition

from ..config.loader import get_config_loader


def create_subagents() -> dict[str, AgentDefinition]:
    """
    从配置文件加载 SubAgent 定义

    配置文件位置: ~/Documents/BIMCanvas/agents/*.md

    SubAgents are defined using AgentDefinition and dispatched via Task tool.
    Note: SubAgent tools should NOT include "Task" (cannot dispatch further SubAgents).

    Returns:
        Dictionary mapping agent names to their definitions
    """
    loader = get_config_loader()
    agents_config = loader.load_agents()

    return {
        name: AgentDefinition(
            description=cfg.description,
            prompt=cfg.prompt,
            tools=cfg.tools if cfg.tools else None,
            model=cfg.model,
        )
        for name, cfg in agents_config.items()
    }
