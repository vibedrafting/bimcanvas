"""Factory helpers for creating host-facing agent adapters."""

from __future__ import annotations

from .main_agent import MainAgent
from .openai_agent import OpenAIAgent
from .protocol import HostAgentProtocol
from ..runtime import (
    CLAUDE_RUNTIME_ID,
    OPENAI_RUNTIME_ID,
    build_config_bundle,
    normalize_runtime_provider,
)


def create_agent(
    runtime_provider: str,
    *,
    project_path: str,
    working_directory: str | None,
    window_seq: int,
) -> HostAgentProtocol:
    normalized = normalize_runtime_provider(runtime_provider)
    bundle = build_config_bundle()
    if normalized == OPENAI_RUNTIME_ID:
        agent = OpenAIAgent(
            project_path=project_path,
            working_directory=working_directory,
            window_seq=window_seq,
        )
    elif normalized == CLAUDE_RUNTIME_ID:
        agent = MainAgent(
            project_path=project_path,
            working_directory=working_directory,
            window_seq=window_seq,
        )
    else:
        raise ValueError(f"Unsupported runtime provider: {runtime_provider}")
    agent.configure(bundle)
    return agent
