"""Runtime session and interaction records for the Agent host."""

from __future__ import annotations

import asyncio
from dataclasses import dataclass, field
from datetime import datetime, timezone
from typing import Any


def _utcnow() -> datetime:
    return datetime.now(timezone.utc)


def _serialize_datetime(value: datetime | None) -> str | None:
    if value is None:
        return None
    return value.isoformat().replace("+00:00", "Z")


@dataclass(slots=True)
class RuntimeSessionRecord:
    """Host-side durable session record."""

    session_id: str
    window_id: str
    project_path: str
    worktree_path: str | None
    runtime_id: str = "claude-sdk"
    runtime_version: str = "0.1.0"
    created_at: datetime = field(default_factory=_utcnow)
    last_active_at: datetime = field(default_factory=_utcnow)
    closed_at: datetime | None = None
    base_status: str = "idle"
    ready_announced: bool = False
    active_turn_id: str | None = None

    def touch(self) -> None:
        self.last_active_at = _utcnow()

    def to_public_dict(self) -> dict[str, Any]:
        return {
            "sessionId": self.session_id,
            "runtimeId": self.runtime_id,
            "runtimeVersion": self.runtime_version,
            "windowId": self.window_id,
            "projectPath": self.project_path,
            "worktreePath": self.worktree_path,
            "status": self.base_status,
            "createdAt": _serialize_datetime(self.created_at),
            "lastActiveAt": _serialize_datetime(self.last_active_at),
            "closedAt": _serialize_datetime(self.closed_at),
        }


@dataclass(slots=True)
class PendingInteractionRecord:
    """Host-side durable interaction record."""

    interaction_id: str
    session_id: str
    turn_id: str
    window_id: str
    kind: str
    blocking: bool
    resume_token: str
    request_payload: dict[str, Any]
    status: str = "pending"
    resolution_payload: dict[str, Any] | None = None
    created_at: datetime = field(default_factory=_utcnow)
    updated_at: datetime = field(default_factory=_utcnow)
    expires_at: datetime | None = None
    cancel_reason: str | None = None
    _waiter: asyncio.Future[Any] | None = field(default=None, repr=False, compare=False)

    def touch(self) -> None:
        self.updated_at = _utcnow()

    def to_public_dict(self) -> dict[str, Any]:
        return {
            "interactionId": self.interaction_id,
            "sessionId": self.session_id,
            "turnId": self.turn_id,
            "windowId": self.window_id,
            "kind": self.kind,
            "blocking": self.blocking,
            "status": self.status,
            "resumeToken": self.resume_token,
            "requestPayload": self.request_payload,
            "resolutionPayload": self.resolution_payload,
            "createdAt": _serialize_datetime(self.created_at),
            "updatedAt": _serialize_datetime(self.updated_at),
            "expiresAt": _serialize_datetime(self.expires_at),
            "cancelReason": self.cancel_reason,
        }
