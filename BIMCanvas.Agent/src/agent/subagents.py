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
    main_allow: list[str],
    main_deny: list[str],
    project_path: str | None = None,
    working_directory: str | None = None,
) -> dict[str, AgentDefinition]:
    """
    把 AgentConfig 字典转为 SDK AgentDefinition 字典 (主真理源 v1.1 §3.6)。

    输入 agents_config 已经由 ConfigLoader.load_agents 完成 base + active plugin 合并;
    v3.7 silent override 改造后,同名 agent 由 plugin 那一份默认覆盖 base 同名(loader.py 内
    logger.info 记录覆盖决定,不再抛错),本函数不区分 agent 来源,只做格式转换 + runtime
    appendix 注入。

    配置文件位置:
    - core-base / 旧布局 base: <BIMCANVAS_HOME>/agents/*.md
    - active plugin: <active_plugin_root>/agents/*.md

    SubAgents 通过 Task 工具派发。SubAgent 自身的 tools 不应包含 "Task"
    (避免递归派发)。

    工具权限重设计 v3.2 §5.2 + §7.1 继承装配规则:
    - cfg.tools is None (`.md` 未声明 / 空值): 继承主控
        * main_allow == [] (主控全开)  → AgentDefinition.tools = None (SDK inherit-all)
        * main_allow == [X, Y, Z]      → AgentDefinition.tools = [X, Y, Z] 深拷贝
        * main_deny  == []             → AgentDefinition.disallowedTools = None
        * main_deny  == [X]            → AgentDefinition.disallowedTools = [X] 深拷贝
    - cfg.tools is list (`.md` 显式列出): 直接用,不再继承
        * AgentDefinition.tools = cfg.tools 拷贝
        * AgentDefinition.disallowedTools = None (SubAgent 没有自己的 deny)

    关键: SDK AgentDefinition.tools 字段 None vs [] 语义不同。
    None = "省略 = inherit all";[] = "明确空 = 仅可调列出工具 = 零工具"。

    Args:
        agents_config: 已经过 agents.allow/deny 过滤后的 SubAgent 配置
        main_allow: 主控 tools.allow (来自 bundle.tools_allow)
        main_deny:  主控 tools.deny  (来自 bundle.tools_deny)

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

        if cfg.tools is None:
            # 继承: 空 list 时传 None 给 SDK (走 inherit-all);非空时深拷贝
            agent_def_tools = list(main_allow) if main_allow else None
            agent_def_disallowed = list(main_deny) if main_deny else None
        else:
            # 显式自主: 直接用 .md 列出的工具列表,SubAgent 没有自己的 deny
            agent_def_tools = list(cfg.tools)
            agent_def_disallowed = None

        result[name] = AgentDefinition(
            description=cfg.description,
            prompt=prompt,
            tools=agent_def_tools,
            disallowedTools=agent_def_disallowed,
            model=cfg.model,
        )

    # 调试日志：输出每个 SubAgent 的注册信息和 prompt 长度
    for name, agent_def in result.items():
        prompt_length = len(agent_def.prompt)
        logger.info(f"SubAgent registered: {name} (prompt: {prompt_length} chars)")

    return result
