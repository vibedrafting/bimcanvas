"""Factory helpers for creating host-facing agent adapters."""

from __future__ import annotations

from .main_agent import MainAgent
from .openai_agent import OpenAIAgent
from .protocol import HostAgentProtocol
from ..runtime import normalize_runtime_provider


def create_agent(
    runtime_provider: str,
    *,
    project_path: str,
    working_directory: str | None,
    window_seq: int,
) -> HostAgentProtocol:
    normalized = normalize_runtime_provider(runtime_provider)
    if normalized == OpenAIAgent.runtime_id:
        return OpenAIAgent(
            project_path=project_path,
            working_directory=working_directory,
            window_seq=window_seq,
        )
    return MainAgent(
        project_path=project_path,
        working_directory=working_directory,
        window_seq=window_seq,
    )
