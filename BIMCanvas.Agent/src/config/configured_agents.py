"""Shared configured-agent requirement parsing."""

from __future__ import annotations

from dataclasses import dataclass

from .loader import AgentConfig


@dataclass(frozen=True)
class ParsedConfiguredAgentRequirements:
    """Runtime-neutral requirement categories derived from shared agent config."""

    config: AgentConfig
    local_tool_names: tuple[str, ...]
    mcp_tool_names: tuple[str, ...]
    requires_skill: bool
    special_tool_names: tuple[str, ...]
    unsupported_tool_names: tuple[str, ...]


def parse_configured_agent_requirements(
    config: AgentConfig,
    *,
    known_local_tool_names: set[str],
    reserved_tool_names: set[str],
) -> ParsedConfiguredAgentRequirements:
    local_tool_names: list[str] = []
    mcp_tool_names: list[str] = []
    special_tool_names: list[str] = []
    unsupported_tool_names: list[str] = []
    requires_skill = False

    for tool_name in config.tools:
        if tool_name == "Skill":
            requires_skill = True
            continue
        if tool_name.startswith("mcp__"):
            if tool_name not in mcp_tool_names:
                mcp_tool_names.append(tool_name)
            continue
        if tool_name in known_local_tool_names:
            if tool_name not in local_tool_names:
                local_tool_names.append(tool_name)
            continue
        if tool_name in {"Task", "AskUserQuestion"} or tool_name in reserved_tool_names:
            if tool_name not in special_tool_names:
                special_tool_names.append(tool_name)
            continue
        if tool_name not in unsupported_tool_names:
            unsupported_tool_names.append(tool_name)

    return ParsedConfiguredAgentRequirements(
        config=config,
        local_tool_names=tuple(local_tool_names),
        mcp_tool_names=tuple(mcp_tool_names),
        requires_skill=requires_skill,
        special_tool_names=tuple(special_tool_names),
        unsupported_tool_names=tuple(unsupported_tool_names),
    )
