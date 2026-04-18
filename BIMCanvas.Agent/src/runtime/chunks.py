"""Runtime-neutral streaming chunk definitions."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any


@dataclass
class StreamChunk:
    """Runtime-neutral chunk consumed by the shared MainStream mapper."""

    type: str
    content: str = ""

    # Subtask lifecycle fields
    subagent_id: str | None = None
    subagent_name: str | None = None
    subagent_type: str | None = None
    parent_subtask_id: str | None = None

    # Tool lifecycle fields
    tool_call_id: str | None = None
    tool_name: str | None = None
    tool_description: str | None = None
    tool_params: dict[str, Any] | None = None
    tool_output: str | None = None

    # Completion/error fields
    success: bool | None = None
    error: str | None = None
    error_type: str | None = None
    error_content: str | None = None
    hidden_content: str | None = None

    # Misc protocol fields
    origin: str | None = None
    task_id: str | None = None
    timeout: int | None = None
    suppress_public_tool_output: bool = False
