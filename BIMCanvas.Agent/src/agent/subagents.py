"""SubAgent definitions for BIMCanvas - loaded from configuration files."""

import logging
from claude_agent_sdk import AgentDefinition

from ..config.loader import AgentConfig

logger = logging.getLogger(__name__)


def create_subagents(agents_config: dict[str, AgentConfig]) -> dict[str, AgentDefinition]:
    """
    从配置文件加载 SubAgent 定义

    配置文件位置: <BIMCANVAS_HOME>/agents/*.md

    SubAgents are defined using AgentDefinition and dispatched via Task tool.
    Note: SubAgent tools should NOT include "Task" (cannot dispatch further SubAgents).

    Returns:
        Dictionary mapping agent names to their definitions
    """
    if not agents_config:
        logger.warning("无可用的 SubAgent 配置，跳过 SubAgent 创建")
        return {}

    result = {}
    for name, cfg in agents_config.items():
        result[name] = AgentDefinition(
            description=cfg.description,
            prompt=cfg.prompt,
            tools=cfg.tools if cfg.tools else None,
            model=cfg.model,
        )

    # 调试日志：输出每个 SubAgent 的注册信息和 prompt 长度
    for name, agent_def in result.items():
        prompt_length = len(agent_def.prompt)
        logger.info(f"SubAgent registered: {name} (prompt: {prompt_length} chars)")

    return result
