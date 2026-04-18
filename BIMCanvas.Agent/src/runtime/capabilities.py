"""Backward-compatible capability exports."""

from .providers import (
    CLAUDE_RUNTIME_ID,
    OPENAI_RUNTIME_ID,
    RUNTIME_VERSION,
    build_capability_matrix,
    get_runtime_descriptor,
)

RUNTIME_ID = CLAUDE_RUNTIME_ID

__all__ = [
    "RUNTIME_ID",
    "RUNTIME_VERSION",
    "CLAUDE_RUNTIME_ID",
    "OPENAI_RUNTIME_ID",
    "build_capability_matrix",
    "get_runtime_descriptor",
]
