"""SubAgent definitions for BIMCanvas - loaded from configuration files."""

from claude_agent_sdk import AgentDefinition

from ..config.loader import get_config_loader
from .skill_loader import get_skill_loader


def create_subagents() -> dict[str, AgentDefinition]:
    """
    从配置文件加载 SubAgent 定义，并注入相应的 Skill 内容

    配置文件位置: ~/.bimcanvas/agents/*.md
    Skill 文件位置: ~/.bimcanvas/skills/*/SKILL.md

    SubAgents are defined using AgentDefinition and dispatched via Task tool.
    Note: SubAgent tools should NOT include "Task" (cannot dispatch further SubAgents).

    Returns:
        Dictionary mapping agent names to their definitions
    """
    loader = get_config_loader()
    skill_loader = get_skill_loader()
    agents_config = loader.load_agents()

    result = {}
    for name, cfg in agents_config.items():
        # 获取该 SubAgent 的 Skill 内容
        skill_content = skill_loader.get_subagent_skills(name)

        # 合并 prompt 和 skill 内容
        full_prompt = cfg.prompt
        if skill_content:
            full_prompt = f"{cfg.prompt}\n\n{skill_content}"

        result[name] = AgentDefinition(
            description=cfg.description,
            prompt=full_prompt,
            tools=cfg.tools if cfg.tools else None,
            model=cfg.model,
        )

    return result
