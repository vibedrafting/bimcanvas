"""Host-facing agent protocol shared by Claude/OpenAI adapters."""

from __future__ import annotations

from collections.abc import AsyncIterator
from typing import Any, Protocol

from ..runtime import ConfigBundle, PendingInteractionRuntimeBinding, RuntimeSessionRecord, StreamChunk


class HostAgentProtocol(Protocol):
    runtime_id: str
    runtime_version: str
    working_directory: str | None

    @property
    def is_connected(self) -> bool:
        ...

    async def connect(
        self,
        effort: str | None = None,
        thinking: str | None = None,
        model: str | None = None,
    ) -> None:
        ...

    async def disconnect(self) -> None:
        ...

    async def set_model(self, model: str) -> bool | None:
        ...

    async def interrupt(self) -> None:
        ...

    def get_current_model(self) -> str | None:
        ...

    def clear_runtime_context(self) -> None:
        ...

    def configure(self, bundle: ConfigBundle) -> None:
        ...

    async def chat(
        self,
        user_message: str,
        model: str | None = None,
        runtime_context: dict[str, str] | None = None,
    ) -> str:
        ...

    async def chat_stream(
        self,
        user_message: str,
        **kwargs: Any,
    ) -> AsyncIterator[StreamChunk]:
        ...

    async def resume_interaction(
        self,
        *,
        interaction_id: str,
        binding: PendingInteractionRuntimeBinding,
        resolution_payload: dict[str, Any],
        session: RuntimeSessionRecord,
        append_event: Any,
    ) -> list[dict[str, Any]]:
        ...
