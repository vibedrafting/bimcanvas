"""SubAgent definitions for BIMCanvas - loaded from configuration files."""

import logging
from claude_agent_sdk import AgentDefinition

from ..config.loader import AgentConfig

logger = logging.getLogger(__name__)


def _append_runtime_context(
    *,
    prompt: str,
    name: str,
    project_path: str | None,
    working_directory: str | None,
) -> str:
    """Append Claude runtime context needed by configured agents."""
    if name != "layout-agent":
        return prompt

    resolved_project_path = project_path or working_directory or "（unknown）"
    resolved_working_directory = working_directory or project_path or "（unknown）"
    return (
        f"{prompt}\n\n"
        "## Claude Runtime Adapter Appendix\n"
        f"- 当前项目路径：{resolved_project_path}\n"
        f"- 当前工作目录：{resolved_working_directory}"
    )


def create_subagents(
    agents_config: dict[str, AgentConfig],
    *,
    project_path: str | None = None,
    working_directory: str | None = None,
) -> dict[str, AgentDefinition]:
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
        prompt = _append_runtime_context(
            prompt=cfg.prompt,
            name=name,
            project_path=project_path,
            working_directory=working_directory,
        )
        result[name] = AgentDefinition(
            description=cfg.description,
            prompt=prompt,
            tools=cfg.tools if cfg.tools else None,
            model=cfg.model,
        )

    # 调试日志：输出每个 SubAgent 的注册信息和 prompt 长度
    for name, agent_def in result.items():
        prompt_length = len(agent_def.prompt)
        logger.info(f"SubAgent registered: {name} (prompt: {prompt_length} chars)")

    return result
