"""SubAgent definitions for BIMCanvas - loaded from configuration files."""

import logging
from claude_agent_sdk import AgentDefinition

from ..config.loader import AgentConfig

logger = logging.getLogger(__name__)


# SubAgent that need the project-path / working-directory appendix injected at runtime.
# Other helper sub-agents will not receive the appendix to avoid bloating their prompts.
_AGENTS_NEEDING_RUNTIME_APPENDIX = frozenset({
    "layout-agent",
    "module-relocation-agent",
})


def _append_runtime_context(
    *,
    prompt: str,
    name: str,
    project_path: str | None,
    working_directory: str | None,
) -> str:
    """Append Claude runtime context needed by configured agents."""
    if name not in _AGENTS_NEEDING_RUNTIME_APPENDIX:
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
    把 AgentConfig 字典转为 SDK AgentDefinition 字典 (主真理源 v1.1 §3.6)。

    输入 agents_config 已经由 ConfigLoader.load_agents 完成 base + active plugin 合并,
    且对同名冲突在 loader 层做了 overrides 强制声明检查 (抛 OverrideNotDeclaredError),
    本函数不再区分 agent 来源,只做格式转换 + runtime appendix 注入。

    配置文件位置:
    - core-base / 旧布局 base: <BIMCANVAS_HOME>/agents/*.md
    - active plugin: <active_plugin_root>/agents/*.md

    SubAgents 通过 Task 工具派发。SubAgent 自身的 tools 不应包含 "Task"
    (避免递归派发)。

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
