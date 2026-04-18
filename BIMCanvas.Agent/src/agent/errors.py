"""Host-facing agent exceptions."""

from __future__ import annotations


class TurnPausedError(RuntimeError):
    """Raised when a streaming turn pauses on a pending blocking interaction."""

    def __init__(self, interaction_id: str) -> None:
        super().__init__(f"Turn paused by interaction {interaction_id}")
        self.interaction_id = interaction_id
