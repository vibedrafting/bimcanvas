"""In-memory runtime store for host-owned session and interaction state."""

from __future__ import annotations

import asyncio
import uuid
from datetime import datetime, timezone
from typing import Any

from .records import PendingInteractionRecord, RuntimeSessionRecord


class RuntimeStateStore:
    """Host-owned truth source for sessions and pending interactions."""

    TERMINAL_RETENTION_LIMIT = 64

    def __init__(self, terminal_retention_limit: int | None = None) -> None:
        self._lock = asyncio.Lock()
        self._window_sessions: dict[str, str] = {}
        self._sessions: dict[str, RuntimeSessionRecord] = {}
        self._session_pending: dict[str, set[str]] = {}
        self._session_terminal: dict[str, list[str]] = {}
        self._interactions: dict[str, PendingInteractionRecord] = {}
        self._interaction_subscribers: list[asyncio.Queue] = []
        self._terminal_retention_limit = terminal_retention_limit or self.TERMINAL_RETENTION_LIMIT

    async def subscribe_interactions(self) -> asyncio.Queue:
        queue: asyncio.Queue = asyncio.Queue()
        async with self._lock:
            self._interaction_subscribers.append(queue)
        return queue

    async def unsubscribe_interactions(self, queue: asyncio.Queue) -> None:
        async with self._lock:
            if queue in self._interaction_subscribers:
                self._interaction_subscribers.remove(queue)

    async def create_session(
        self,
        *,
        window_id: str,
        project_path: str,
        worktree_path: str | None,
    ) -> RuntimeSessionRecord:
        session = RuntimeSessionRecord(
            session_id=str(uuid.uuid4()),
            window_id=window_id,
            project_path=project_path,
            worktree_path=worktree_path,
        )
        async with self._lock:
            self._sessions[session.session_id] = session
            self._window_sessions[window_id] = session.session_id
            self._session_pending.setdefault(session.session_id, set())
            self._session_terminal.setdefault(session.session_id, [])
        return session

    async def get_active_session(self, window_id: str) -> RuntimeSessionRecord | None:
        async with self._lock:
            session_id = self._window_sessions.get(window_id)
            if not session_id:
                return None
            return self._sessions.get(session_id)

    async def get_session(self, session_id: str) -> RuntimeSessionRecord | None:
        async with self._lock:
            return self._sessions.get(session_id)

    async def get_session_snapshot(self, session_id: str) -> dict[str, Any] | None:
        async with self._lock:
            session = self._sessions.get(session_id)
            if not session:
                return None
            return self._serialize_session_locked(session)

    async def touch_session(self, session_id: str) -> RuntimeSessionRecord | None:
        async with self._lock:
            session = self._sessions.get(session_id)
            if session:
                session.touch()
            return session

    async def mark_session_running(self, session_id: str, turn_id: str) -> RuntimeSessionRecord | None:
        async with self._lock:
            session = self._sessions.get(session_id)
            if session and session.base_status not in {"closed", "error"}:
                session.base_status = "running"
                session.active_turn_id = turn_id
                session.touch()
            return session

    async def mark_session_idle(self, session_id: str, turn_id: str | None = None) -> RuntimeSessionRecord | None:
        async with self._lock:
            session = self._sessions.get(session_id)
            if session and session.base_status not in {"closed", "error"}:
                session.base_status = "idle"
                if turn_id is None or session.active_turn_id == turn_id:
                    session.active_turn_id = None
                session.touch()
            return session

    async def mark_session_error(self, session_id: str, *, clear_turn: bool = True) -> RuntimeSessionRecord | None:
        async with self._lock:
            session = self._sessions.get(session_id)
            if session:
                session.base_status = "error"
                if clear_turn:
                    session.active_turn_id = None
                session.touch()
            return session

    async def mark_session_ready_announced(self, session_id: str) -> RuntimeSessionRecord | None:
        async with self._lock:
            session = self._sessions.get(session_id)
            if session:
                session.ready_announced = True
                session.touch()
            return session

    async def close_session(self, session_id: str, *, remove_window_binding: bool = True) -> RuntimeSessionRecord | None:
        waiters_to_resolve: list[asyncio.Future] = []
        async with self._lock:
            session = self._sessions.get(session_id)
            if not session:
                return None
            session.base_status = "closed"
            session.active_turn_id = None
            session.closed_at = datetime.now(timezone.utc)
            session.touch()
            if remove_window_binding and self._window_sessions.get(session.window_id) == session_id:
                self._window_sessions.pop(session.window_id, None)
            pending_ids = list(self._session_pending.pop(session_id, set()))
            terminal_ids = list(self._session_terminal.pop(session_id, []))

            purge_ids = list(dict.fromkeys([*pending_ids, *terminal_ids]))
            for interaction_id in pending_ids:
                interaction = self._interactions.get(interaction_id)
                if interaction and interaction.status == "pending":
                    interaction.status = "cancelled"
                    interaction.cancel_reason = "session_closed"
                    interaction.touch()
                    if interaction._waiter and not interaction._waiter.done():
                        waiters_to_resolve.append(interaction._waiter)

            for interaction_id in purge_ids:
                self._interactions.pop(interaction_id, None)

        for waiter in waiters_to_resolve:
            waiter.set_result(
                {
                    "status": "cancelled",
                    "resolutionPayload": None,
                    "cancelReason": "session_closed",
                }
            )
        return session

    async def derive_session_status(self, session_id: str) -> str | None:
        async with self._lock:
            session = self._sessions.get(session_id)
            if not session:
                return None
            return self._derive_session_status_locked(session)

    async def get_active_turn_id(self, window_id: str) -> tuple[RuntimeSessionRecord | None, str | None]:
        async with self._lock:
            session_id = self._window_sessions.get(window_id)
            if not session_id:
                return None, None
            session = self._sessions.get(session_id)
            if not session:
                return None, None
            return session, session.active_turn_id

    async def create_interaction(
        self,
        *,
        session_id: str,
        turn_id: str,
        window_id: str,
        kind: str,
        blocking: bool,
        resume_token: str,
        request_payload: dict[str, Any],
        expires_at: datetime | None = None,
    ) -> PendingInteractionRecord:
        loop = asyncio.get_running_loop()
        interaction = PendingInteractionRecord(
            interaction_id=str(uuid.uuid4()),
            session_id=session_id,
            turn_id=turn_id,
            window_id=window_id,
            kind=kind,
            blocking=blocking,
            resume_token=resume_token,
            request_payload=request_payload,
            expires_at=expires_at,
            _waiter=loop.create_future(),
        )
        subscribers: list[asyncio.Queue]
        async with self._lock:
            self._interactions[interaction.interaction_id] = interaction
            self._session_pending.setdefault(session_id, set()).add(interaction.interaction_id)
            session = self._sessions.get(session_id)
            if session:
                session.touch()
            subscribers = list(self._interaction_subscribers)
        self._publish(subscribers, "interaction.pushed", interaction.to_public_dict())
        return interaction

    async def get_interaction(self, interaction_id: str) -> PendingInteractionRecord | None:
        async with self._lock:
            return self._interactions.get(interaction_id)

    async def get_pending_interactions_for_window(self, window_id: str) -> tuple[str | None, list[dict[str, Any]]]:
        async with self._lock:
            session_id = self._window_sessions.get(window_id)
            if not session_id:
                return None, []
            pending_ids = self._session_pending.get(session_id, set())
            interactions = [
                self._interactions[interaction_id].to_public_dict()
                for interaction_id in pending_ids
                if interaction_id in self._interactions and self._interactions[interaction_id].status == "pending"
            ]
            return session_id, interactions

    async def wait_for_interaction(self, interaction_id: str) -> dict[str, Any]:
        async with self._lock:
            interaction = self._interactions.get(interaction_id)
            if not interaction:
                raise KeyError(interaction_id)
            waiter = interaction._waiter
            if interaction.status != "pending":
                return {
                    "status": interaction.status,
                    "resolutionPayload": interaction.resolution_payload,
                    "cancelReason": interaction.cancel_reason,
                }
        if waiter is None:
            return {
                "status": interaction.status,
                "resolutionPayload": interaction.resolution_payload,
                "cancelReason": interaction.cancel_reason,
            }
        return await waiter

    async def submit_interaction(
        self,
        interaction_id: str,
        resolution_payload: dict[str, Any] | None,
    ) -> PendingInteractionRecord | None:
        return await self._finalize_interaction(
            interaction_id,
            final_status="resolved",
            resolution_payload=resolution_payload or {},
            cancel_reason=None,
        )

    async def cancel_interaction(
        self,
        interaction_id: str,
        *,
        cancel_reason: str | None = None,
        final_status: str = "cancelled",
    ) -> PendingInteractionRecord | None:
        return await self._finalize_interaction(
            interaction_id,
            final_status=final_status,
            resolution_payload=None,
            cancel_reason=cancel_reason,
        )

    async def cancel_session_interactions(
        self,
        session_id: str,
        *,
        cancel_reason: str,
        final_status: str = "cancelled",
    ) -> list[PendingInteractionRecord]:
        async with self._lock:
            pending_ids = list(self._session_pending.get(session_id, set()))
        finalized: list[PendingInteractionRecord] = []
        for interaction_id in pending_ids:
            interaction = await self._finalize_interaction(
                interaction_id,
                final_status=final_status,
                resolution_payload=None,
                cancel_reason=cancel_reason,
            )
            if interaction is not None:
                finalized.append(interaction)
        return finalized

    async def cancel_turn_interactions(
        self,
        session_id: str,
        turn_id: str,
        *,
        cancel_reason: str,
        final_status: str = "cancelled",
    ) -> list[PendingInteractionRecord]:
        async with self._lock:
            pending_ids = [
                interaction_id
                for interaction_id in self._session_pending.get(session_id, set())
                if (
                    interaction_id in self._interactions
                    and self._interactions[interaction_id].status == "pending"
                    and self._interactions[interaction_id].turn_id == turn_id
                )
            ]
        finalized: list[PendingInteractionRecord] = []
        for interaction_id in pending_ids:
            interaction = await self._finalize_interaction(
                interaction_id,
                final_status=final_status,
                resolution_payload=None,
                cancel_reason=cancel_reason,
            )
            if interaction is not None:
                finalized.append(interaction)
        return finalized

    async def _finalize_interaction(
        self,
        interaction_id: str,
        *,
        final_status: str,
        resolution_payload: dict[str, Any] | None,
        cancel_reason: str | None,
    ) -> PendingInteractionRecord | None:
        public_payload: dict[str, Any] | None = None
        event_name: str | None = None
        waiter: asyncio.Future | None = None
        subscribers: list[asyncio.Queue] = []

        async with self._lock:
            interaction = self._interactions.get(interaction_id)
            if not interaction:
                return None

            if interaction.status != "pending":
                return interaction

            interaction.status = final_status
            interaction.resolution_payload = resolution_payload
            interaction.cancel_reason = cancel_reason
            interaction.touch()
            self._session_pending.get(interaction.session_id, set()).discard(interaction_id)
            terminal_ids = self._session_terminal.setdefault(interaction.session_id, [])
            terminal_ids.append(interaction_id)

            session = self._sessions.get(interaction.session_id)
            if session:
                session.touch()

            if interaction._waiter and not interaction._waiter.done():
                waiter = interaction._waiter

            self._trim_terminal_interactions_locked(interaction.session_id)
            public_payload = interaction.to_public_dict()
            subscribers = list(self._interaction_subscribers)

            if final_status == "resolved":
                event_name = "interaction.resolved"
            elif final_status == "expired":
                event_name = "interaction.expired"
            else:
                event_name = "interaction.cancelled"

        if waiter:
            waiter.set_result(
                {
                    "status": final_status,
                    "resolutionPayload": resolution_payload,
                    "cancelReason": cancel_reason,
                }
            )

        if public_payload and event_name:
            self._publish(subscribers, event_name, public_payload)

        return interaction

    def _derive_session_status_locked(self, session: RuntimeSessionRecord) -> str:
        if session.base_status not in {"closed", "error"}:
            pending_ids = self._session_pending.get(session.session_id, set())
            for interaction_id in pending_ids:
                interaction = self._interactions.get(interaction_id)
                if interaction and interaction.status == "pending" and interaction.blocking:
                    return "paused"
        return session.base_status

    def _serialize_session_locked(self, session: RuntimeSessionRecord) -> dict[str, Any]:
        snapshot = session.to_public_dict()
        snapshot["status"] = self._derive_session_status_locked(session)
        return snapshot

    def _trim_terminal_interactions_locked(self, session_id: str) -> None:
        terminal_ids = self._session_terminal.setdefault(session_id, [])
        while len(terminal_ids) > self._terminal_retention_limit:
            interaction_id = terminal_ids.pop(0)
            self._interactions.pop(interaction_id, None)

    @staticmethod
    def _publish(subscribers: list[asyncio.Queue], event_name: str, record: dict[str, Any]) -> None:
        payload = {"event": event_name, "record": record}
        for queue in subscribers:
            queue.put_nowait(payload)
