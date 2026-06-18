"""HTTP Server for Web integration using aiohttp."""

from __future__ import annotations

import asyncio
import base64
import json
import logging
import time
import uuid
from datetime import datetime, timedelta
from pathlib import Path
from typing import Any

import aiohttp_cors
from aiohttp import web

from ..agent.errors import TurnPausedError
from ..agent.factory import create_agent
from ..agent.protocol import HostAgentProtocol
from ..attachments.chat_attachments import AttachmentResolutionError, resolve_attachment_image_blocks
from ..config.loader import resolve_bimcanvas_home
from ..config.settings import get_settings
from ..runtime import (
    DEFAULT_RUNTIME_PROVIDER,
    MainStreamMapper,
    OPENAI_RUNTIME_ID,
    PendingInteractionRuntimeBinding,
    RuntimeSessionRecord,
    RuntimeStateStore,
    StreamChunk,
    build_capability_matrix,
    get_runtime_descriptor,
    normalize_runtime_provider,
)

# Configure logging
logger = logging.getLogger(__name__)

# Global agent instances (cached by window ID for multi-window parallel support)
agents: dict[str, HostAgentProtocol] = {}  # windowId → Agent
_agents_lock = asyncio.Lock()
runtime_store = RuntimeStateStore()

# Plugin web action registry: (namespace, action_name) → async handler
# Populated by config_bundle after each plugin's register() call.
_plugin_action_registry: dict[tuple[str, str], Any] = {}

STREAM_DELTA_FLUSH_INTERVAL_SECONDS = 0.08
STREAM_DELTA_FLUSH_MAX_CHARS = 512
_COALESCIBLE_DELTA_CHUNK_TYPES = {"text", "thinking"}

# 窗口序号管理（日志前缀用）
_window_counter = 1  # 从 1 开始，primary 不占用（序号 0）
_window_seq_map: dict[str, int] = {}  # windowId → 序号


def _get_window_prefix(window_seq: int) -> str:
    """获取窗口日志前缀"""
    if window_seq == 0:
        return "[Agent]"
    return f"[Agent#{window_seq}]"


def _build_runtime_context(window_id: str, session_id: str, turn_id: str) -> dict[str, str]:
    return {
        "windowId": window_id,
        "sessionId": session_id,
        "turnId": turn_id,
    }


async def _background_task_pusher(record: dict[str, Any]) -> None:
    """Agent 注入回调：把后台任务（Workflow）完成事件经 runtime_store 带外推送给前端。

    复用 interaction SSE 通道（/api/interaction/events），事件名 background_task.completed。
    """
    await runtime_store.push_background_task(record=record)


async def _background_progress_pusher(record: dict[str, Any]) -> None:
    """Agent 注入回调：把后台 Workflow 进度经 runtime_store 带外实时推送给前端。

    复用 interaction SSE 通道，事件名 background_task.progress（只实时、不落 history）。
    """
    await runtime_store.push_background_progress(record=record)


def _build_session_ready_event(session_snapshot: dict[str, Any]) -> dict[str, Any]:
    return {
        "type": "session_ready",
        "sessionId": session_snapshot["sessionId"],
        "windowId": session_snapshot["windowId"],
        "runtimeId": session_snapshot["runtimeId"],
        "status": session_snapshot["status"],
    }


class _StreamDeltaCoalescer:
    """Coalesce high-frequency text/thinking deltas before SSE/history emission."""

    def __init__(
        self,
        *,
        flush_interval_seconds: float = STREAM_DELTA_FLUSH_INTERVAL_SECONDS,
        max_chars: int = STREAM_DELTA_FLUSH_MAX_CHARS,
    ) -> None:
        self.flush_interval_seconds = flush_interval_seconds
        self.max_chars = max_chars
        self._chunk_type: str | None = None
        self._content_parts: list[str] = []
        self._content_length = 0
        self._buffer_started_at = 0.0
        self._chunk_attrs: dict[str, Any] = {}
        self._flush_task: asyncio.Task[None] | None = None
        self._lock = asyncio.Lock()

    async def process(
        self,
        chunk: StreamChunk,
        emit_chunk: Any,
    ) -> None:
        async with self._lock:
            if not self._can_coalesce(chunk):
                await self._flush_locked(emit_chunk, cancel_timer=True)
                await emit_chunk(chunk)
                return

            chunk_key = self._coalesce_key(chunk)
            if self._content_parts and chunk_key != self._coalesce_key_from_buffer():
                await self._flush_locked(emit_chunk, cancel_timer=True)

            now = time.monotonic()
            if not self._content_parts:
                self._chunk_type = chunk.type
                self._buffer_started_at = now
                self._chunk_attrs = self._extract_chunk_attrs(chunk)
                self._schedule_flush(emit_chunk)

            self._content_parts.append(chunk.content)
            self._content_length += len(chunk.content)

            if (
                self._content_length >= self.max_chars
                or now - self._buffer_started_at >= self.flush_interval_seconds
            ):
                await self._flush_locked(emit_chunk, cancel_timer=True)

    async def flush(self, emit_chunk: Any) -> None:
        async with self._lock:
            await self._flush_locked(emit_chunk, cancel_timer=True)

    async def _flush_locked(self, emit_chunk: Any, *, cancel_timer: bool) -> None:
        if not self._content_parts or self._chunk_type is None:
            if cancel_timer:
                self._cancel_flush_task()
            return

        if cancel_timer:
            self._cancel_flush_task()

        content = "".join(self._content_parts)
        chunk = StreamChunk(
            type=self._chunk_type,
            content=content,
            **self._chunk_attrs,
        )
        self._clear()
        await emit_chunk(chunk)

    def _schedule_flush(self, emit_chunk: Any) -> None:
        self._cancel_flush_task()
        self._flush_task = asyncio.create_task(self._flush_later(emit_chunk))

    def _cancel_flush_task(self) -> None:
        task = self._flush_task
        if task is None:
            return
        self._flush_task = None
        if task is not asyncio.current_task() and not task.done():
            task.cancel()

    async def _flush_later(self, emit_chunk: Any) -> None:
        try:
            await asyncio.sleep(self.flush_interval_seconds)
            async with self._lock:
                self._flush_task = None
                await self._flush_locked(emit_chunk, cancel_timer=False)
        except asyncio.CancelledError:
            return

    def _clear(self) -> None:
        self._chunk_type = None
        self._content_parts = []
        self._content_length = 0
        self._buffer_started_at = 0.0
        self._chunk_attrs = {}

    def _can_coalesce(self, chunk: StreamChunk) -> bool:
        return (
            chunk.type in _COALESCIBLE_DELTA_CHUNK_TYPES
            and bool(chunk.content)
            and not chunk.error
            and not chunk.error_type
            and not chunk.error_content
            and not chunk.hidden_content
        )

    def _coalesce_key_from_buffer(self) -> tuple[Any, ...]:
        return (
            self._chunk_type,
            self._chunk_attrs.get("subagent_id"),
            self._chunk_attrs.get("subagent_name"),
            self._chunk_attrs.get("subagent_type"),
            self._chunk_attrs.get("parent_subtask_id"),
            self._chunk_attrs.get("origin"),
        )

    def _coalesce_key(self, chunk: StreamChunk) -> tuple[Any, ...]:
        return (
            chunk.type,
            chunk.subagent_id,
            chunk.subagent_name,
            chunk.subagent_type,
            chunk.parent_subtask_id,
            chunk.origin,
        )

    def _extract_chunk_attrs(self, chunk: StreamChunk) -> dict[str, Any]:
        return {
            "subagent_id": chunk.subagent_id,
            "subagent_name": chunk.subagent_name,
            "subagent_type": chunk.subagent_type,
            "parent_subtask_id": chunk.parent_subtask_id,
            "origin": chunk.origin,
        }


def _resolve_runtime_provider_from_settings(settings: Any) -> str:
    return normalize_runtime_provider(getattr(settings, "runtime_provider", DEFAULT_RUNTIME_PROVIDER))


def _agent_is_connected(agent: Any) -> bool:
    if hasattr(agent, "is_connected"):
        return bool(getattr(agent, "is_connected"))
    return bool(getattr(agent, "_connected", False))


def _get_requested_session_id(request: web.Request) -> str | None:
    session_id = request.headers.get("X-Session-Id")
    if session_id:
        session_id = session_id.strip()
    return session_id or None


def _session_matches_request(session: RuntimeSessionRecord, project_path: str, worktree_path: str | None) -> bool:
    working_dir = worktree_path or project_path
    session_working_dir = session.worktree_path or session.project_path
    return session.project_path == project_path and session_working_dir == working_dir


def _control_plane_error_response(
    *,
    error_type: str,
    message: str,
    session: RuntimeSessionRecord | None = None,
    session_status: str | None = None,
    status: int = 409,
) -> web.Response:
    payload: dict[str, Any] = {
        "error": message,
        "errorType": error_type,
    }
    headers: dict[str, str] = {}
    if session is not None:
        payload["sessionId"] = session.session_id
        headers["X-Session-Id"] = session.session_id
    if session_status:
        payload["sessionStatus"] = session_status
    return web.json_response(payload, status=status, headers=headers)


def _resolve_chat_directory(path_value: str | None) -> Path | None:
    normalized = str(path_value or "").strip()
    if not normalized:
        return None

    try:
        return Path(normalized).expanduser().resolve(strict=False)
    except Exception:
        try:
            return Path(normalized).expanduser().absolute()
        except Exception:
            return None


async def _teardown_matching_request_session(
    window_id: str,
    project_path: str,
    worktree_path: str | None,
    *,
    cancel_reason: str,
) -> None:
    async with _agents_lock:
        active_session = await runtime_store.get_active_session(window_id)
        if active_session is not None and _session_matches_request(active_session, project_path, worktree_path):
            await _teardown_window_locked(
                window_id,
                cancel_reason=cancel_reason,
                drop_window_seq=True,
                sleep_after_disconnect=False,
            )


async def _validate_chat_directories(
    *,
    window_id: str,
    project_path: str,
    worktree_path: str | None,
) -> web.Response | None:
    project_dir = _resolve_chat_directory(project_path)
    if project_dir is None:
        return _control_plane_error_response(
            error_type="PROJECT_PATH_REQUIRED",
            message="projectPath is required.",
            status=400,
        )

    if not project_dir.is_dir():
        await _teardown_matching_request_session(
            window_id,
            project_path,
            worktree_path,
            cancel_reason="project_path_missing",
        )
        return _control_plane_error_response(
            error_type="PROJECT_PATH_NOT_FOUND",
            message="Project directory no longer exists. Return to homepage and reopen a valid project.",
            status=404,
        )

    if worktree_path:
        worktree_dir = _resolve_chat_directory(worktree_path)
        if worktree_dir is None or not worktree_dir.is_dir():
            await _teardown_matching_request_session(
                window_id,
                project_path,
                worktree_path,
                cancel_reason="worktree_path_missing",
            )
            return _control_plane_error_response(
                error_type="WORKTREE_PATH_NOT_FOUND",
                message="Worktree directory no longer exists. Close this window or reopen the project.",
                status=404,
            )

    return None


async def _check_chat_request_control_plane(
    request: web.Request,
    *,
    window_id: str,
    project_path: str,
    worktree_path: str | None,
) -> web.Response | None:
    active_session = await runtime_store.get_active_session(window_id)
    if active_session is None or not _session_matches_request(active_session, project_path, worktree_path):
        return None

    requested_session_id = _get_requested_session_id(request)
    derived_status = await runtime_store.derive_session_status(active_session.session_id)

    if requested_session_id and requested_session_id != active_session.session_id:
        return _control_plane_error_response(
            error_type="SESSION_EXPIRED",
            message="Requested session is no longer active for this window.",
            session=active_session,
            session_status=derived_status,
        )

    if derived_status == "paused":
        return _control_plane_error_response(
            error_type="SESSION_PAUSED",
            message="The active session is paused by a pending blocking interaction.",
            session=active_session,
            session_status=derived_status,
        )

    if derived_status == "error":
        return _control_plane_error_response(
            error_type="SESSION_ERROR",
            message="The active session is in an unrecoverable error state and must be rebuilt.",
            session=active_session,
            session_status=derived_status,
        )

    return None


def _is_runtime_session_failure(terminal_event: dict[str, Any]) -> bool:
    return (
        terminal_event.get("eventType") == "turn.failed"
        and terminal_event.get("payload", {}).get("stopReason") == "runtime_error"
    )


async def _finalize_turn_state(
    session_id: str,
    turn_id: str,
    terminal_event: dict[str, Any] | None,
    *,
    paused: bool = False,
) -> None:
    if paused:
        return

    if terminal_event is None:
        await runtime_store.cancel_turn_interactions(
            session_id,
            turn_id,
            cancel_reason="turn_aborted",
        )
        await runtime_store.mark_session_idle(session_id, turn_id)
        return

    stop_reason = terminal_event.get("payload", {}).get("stopReason")
    if stop_reason == "completed":
        cancel_reason = "turn_completed"
    elif stop_reason == "interrupted":
        cancel_reason = "turn_interrupted"
    else:
        cancel_reason = "turn_failed"

    await runtime_store.cancel_turn_interactions(
        session_id,
        turn_id,
        cancel_reason=cancel_reason,
    )

    if _is_runtime_session_failure(terminal_event):
        await runtime_store.mark_session_error(session_id)
    else:
        await runtime_store.mark_session_idle(session_id, turn_id)


async def _try_write_sse_data(response: web.StreamResponse, data: dict[str, Any]) -> bool:
    try:
        await response.write(f"data: {json.dumps(data, ensure_ascii=False)}\n\n".encode("utf-8"))
        return True
    except (BrokenPipeError, ConnectionResetError, RuntimeError):
        return False


def _sanitize_history_attachments(raw_value: Any) -> list[dict[str, Any]]:
    if not isinstance(raw_value, list):
        return []

    allowed_keys = {
        "attachmentId",
        "clientMessageId",
        "sourceKind",
        "originalFileName",
        "mimeType",
        "sizeBytes",
        "width",
        "height",
        "status",
        "contentUrl",
    }

    sanitized: list[dict[str, Any]] = []
    for item in raw_value:
        if not isinstance(item, dict):
            continue

        record = {key: item.get(key) for key in allowed_keys if key in item}
        attachment_id = record.get("attachmentId")
        content_url = record.get("contentUrl")
        if not isinstance(attachment_id, str) or not attachment_id.strip():
            continue
        if not isinstance(content_url, str) or not content_url.strip():
            continue
        sanitized.append(record)

    return sanitized


def _build_context_with_chat_attachments(
    context: Any,
    *,
    project_path: str,
    client_message_id: str | None,
    attachments: list[dict[str, Any]],
    attachment_ids: list[Any],
) -> dict[str, Any]:
    base_context = dict(context) if isinstance(context, dict) else {}
    attachment_items: list[dict[str, Any]] = []
    seen_ids: set[str] = set()

    for item in attachments:
        attachment_id = str(item.get("attachmentId") or "").strip()
        if not attachment_id or attachment_id in seen_ids:
            continue
        seen_ids.add(attachment_id)
        attachment_items.append({
            key: item.get(key)
            for key in (
                "attachmentId",
                "clientMessageId",
                "sourceKind",
                "originalFileName",
                "mimeType",
                "sizeBytes",
                "width",
                "height",
                "status",
            )
            if item.get(key) is not None
        })

    for raw_id in attachment_ids:
        attachment_id = str(raw_id or "").strip()
        if not attachment_id or attachment_id in seen_ids:
            continue
        seen_ids.add(attachment_id)
        attachment_items.append({"attachmentId": attachment_id})

    if attachment_items:
        base_context["chatAttachments"] = {
            "projectPath": project_path,
            "clientMessageId": client_message_id,
            "items": attachment_items,
        }

    return base_context


async def _append_chunk_events(
    *,
    stream_mapper: MainStreamMapper,
    chunk: StreamChunk,
    session_id: str,
    turn_id: str,
    window_id: str,
    response: web.StreamResponse | None = None,
    client_stream_connected: bool = False,
) -> tuple[list[dict[str, Any]], bool]:
    emitted: list[dict[str, Any]] = []
    is_connected = client_stream_connected

    for event_data in stream_mapper.map_chunk(chunk):
        emitted.append(event_data)
        await runtime_store.append_event_history(
            session_id=session_id,
            turn_id=turn_id,
            window_id=window_id,
            event_payload=event_data,
        )
        if response is not None and is_connected:
            is_connected = await _try_write_sse_data(response, event_data)

    return emitted, is_connected


async def _disconnect_agent(agent: HostAgentProtocol) -> None:
    try:
        await agent.disconnect()
    except Exception as exc:
        logger.warning(f"Error disconnecting agent: {exc}")


async def _teardown_window_locked(
    window_id: str,
    *,
    cancel_reason: str,
    drop_window_seq: bool,
    sleep_after_disconnect: bool,
) -> RuntimeSessionRecord | None:
    session = await runtime_store.get_active_session(window_id)
    if session:
        await runtime_store.cancel_session_interactions(
            session.session_id,
            cancel_reason=cancel_reason,
        )
        await runtime_store.close_session(session.session_id, remove_window_binding=True)

    agent = agents.pop(window_id, None)
    if agent is not None:
        agent.clear_runtime_context()
        await _disconnect_agent(agent)
        if sleep_after_disconnect:
            await asyncio.sleep(1.5)

    if drop_window_seq:
        _window_seq_map.pop(window_id, None)

    return session


async def get_agent(
    window_id: str,
    project_path: str,
    worktree_path: str = None,
) -> tuple[HostAgentProtocol, RuntimeSessionRecord]:
    """
    获取或创建窗口专属的 Agent 实例和 session。

    Args:
        window_id: 窗口唯一标识
        project_path: 项目根目录
        worktree_path: 实际工作目录（虚拟窗口的 Worktree 路径）

    Returns:
        (HostAgentProtocol, RuntimeSessionRecord)
    """
    global _window_counter

    working_dir = worktree_path or project_path
    settings = get_settings()
    runtime_provider = _resolve_runtime_provider_from_settings(settings)
    runtime_descriptor = get_runtime_descriptor(runtime_provider)

    async with _agents_lock:
        agent = agents.get(window_id)
        session = await runtime_store.get_active_session(window_id)

        if agent and session:
            same_project = session.project_path == project_path
            same_worktree = (session.worktree_path or session.project_path) == working_dir
            same_runtime = session.runtime_id == runtime_descriptor.runtime_id
            if same_project and same_worktree and same_runtime and agent.working_directory == working_dir:
                await runtime_store.touch_session(session.session_id)
                return agent, session

            seq = _window_seq_map.get(window_id, 0)
            prefix = _get_window_prefix(seq)
            print(f"{prefix} [Server] ========== 项目切换 ==========")
            print(f"{prefix} [Server] 旧路径: {agent.working_directory}")
            print(f"{prefix} [Server] 新路径: {working_dir}")
            print(f"{prefix} [Server] ===================================")

            await _teardown_window_locked(
                window_id,
                cancel_reason="project_switched",
                drop_window_seq=False,
                sleep_after_disconnect=False,
            )
            logger.info(f"Project switched for window {window_id}, recreating agent")
            agent = None
            session = None
        elif agent and not session:
            await _teardown_window_locked(
                window_id,
                cancel_reason="session_rebuilt",
                drop_window_seq=False,
                sleep_after_disconnect=False,
            )
            agent = None

        if window_id == "primary":
            seq = 0
            _window_seq_map.setdefault(window_id, seq)
        else:
            seq = _window_seq_map.get(window_id)
            if seq is None:
                seq = _window_counter
                _window_counter += 1
                _window_seq_map[window_id] = seq

        if agent is None:
            agent = create_agent(
                runtime_provider,
                project_path=project_path,
                working_directory=working_dir,
                window_seq=seq,
            )
            agents[window_id] = agent

            # 注入后台任务完成推送回调（Claude 路径用；其余 runtime 无此方法即跳过）
            if hasattr(agent, "set_background_push"):
                agent.set_background_push(_background_task_pusher)
            # 注入后台 Workflow 进度推送回调（Task 页实时可视化）
            if hasattr(agent, "set_background_progress_push"):
                agent.set_background_progress_push(_background_progress_pusher)

            prefix = _get_window_prefix(seq)
            print(f"{prefix} [Server] ========== Agent 实例创建 ==========")
            print(f"{prefix} [Server] 窗口ID: {window_id}")
            print(f"{prefix} [Server] 窗口序号: {seq}")
            print(f"{prefix} [Server] 项目路径: {project_path}")
            print(f"{prefix} [Server] 工作目录: {working_dir}")
            print(f"{prefix} [Server] 当前实例数: {len(agents)}")
            print(f"{prefix} [Server] =====================================")
            logger.info(f"Created agent for window: {window_id} (seq={seq}), working_dir: {working_dir}")

        session = await runtime_store.create_session(
            window_id=window_id,
            project_path=project_path,
            worktree_path=worktree_path,
            runtime_id=agent.runtime_id,
            runtime_version=agent.runtime_version,
        )
        return agent, session


async def cleanup_agents() -> None:
    """清理所有 Agent 连接（shutdown 时调用）"""
    async with _agents_lock:
        if agents:
            print("[Server] ========== 清理所有 Agent ==========")
            print(f"[Server] 待清理实例数: {len(agents)}")

        for window_id in list(agents.keys()):
            try:
                await _teardown_window_locked(
                    window_id,
                    cancel_reason="shutdown",
                    drop_window_seq=False,
                    sleep_after_disconnect=False,
                )
                print(f"[Server] 已断开: {window_id}")
                logger.info(f"Disconnected agent: {window_id}")
            except Exception as exc:
                print(f"[Server] 断开失败: {window_id} - {exc}")
                logger.error(f"Error disconnecting agent {window_id}: {exc}")

        if agents:
            print("[Server] =====================================")

        agents.clear()
        _window_seq_map.clear()


async def health_handler(request: web.Request) -> web.Response:
    """Health check endpoint"""
    return web.json_response({
        "status": "ok",
        "service": "bimcanvas-agent",
        "version": "0.1.0",
    })


async def config_handler(request: web.Request) -> web.Response:
    """
    Get default configuration for Web client.

    Response:
        {
            "models": [
                {"id": "opus", "label": "Opus"},
                {"id": "sonnet", "label": "Sonnet"},
                {"id": "haiku", "label": "Haiku"}
            ],
            "defaultEffort": "medium",
            "defaultThinking": "off"
        }
    """
    settings = get_settings()

    models = []
    for alias, entry in settings.model_mapping.items():
        if isinstance(entry, dict):
            label = entry.get("label", alias.capitalize())
        else:
            label = alias.capitalize()
        models.append({"id": alias, "label": label})

    runtime_provider = _resolve_runtime_provider_from_settings(settings)
    runtime_descriptor = get_runtime_descriptor(runtime_provider)
    is_openai_runtime = runtime_descriptor.runtime_id == OPENAI_RUNTIME_ID

    return web.json_response({
        "runtime": runtime_descriptor.runtime_id,
        "runtimeVersion": runtime_descriptor.runtime_version,
        "models": models,
        "defaultModel": settings.default_model,
        "defaultEffort": None if is_openai_runtime else settings.default_effort,
        "defaultThinking": None if is_openai_runtime else settings.default_thinking,
        "capabilityMatrix": build_capability_matrix(runtime_provider),
    })


async def chat_handler(request: web.Request) -> web.Response:
    """
    Handle chat requests.

    Request body:
        {
            "projectPath": "path/to/project",
            "windowId": "window-1",
            "worktreePath": null,
            "message": "user message"
        }
    """
    try:
        data = await request.json()
    except json.JSONDecodeError:
        return web.json_response({"error": "Invalid JSON"}, status=400)

    project_path = data.get("projectPath", "")
    window_id = data.get("windowId", "primary") or "primary"
    worktree_path = data.get("worktreePath")
    message = data.get("message", "")
    model = data.get("model")

    if not message:
        return web.json_response({"error": "Message cannot be empty"}, status=400)

    if not model:
        return web.json_response({"error": "Model is required"}, status=400)

    directory_error = await _validate_chat_directories(
        window_id=window_id,
        project_path=project_path,
        worktree_path=worktree_path,
    )
    if directory_error is not None:
        return directory_error

    control_plane_error = await _check_chat_request_control_plane(
        request,
        window_id=window_id,
        project_path=project_path,
        worktree_path=worktree_path,
    )
    if control_plane_error is not None:
        return control_plane_error

    turn_id = str(uuid.uuid4())
    session: RuntimeSessionRecord | None = None
    agent: HostAgentProtocol | None = None

    try:
        agent, session = await get_agent(window_id, project_path, worktree_path)

        requested_session_id = _get_requested_session_id(request)
        if requested_session_id and requested_session_id != session.session_id:
            session_status = await runtime_store.derive_session_status(session.session_id)
            return _control_plane_error_response(
                error_type="SESSION_EXPIRED",
                message="Requested session is no longer active for this window.",
                session=session,
                session_status=session_status,
            )

        runtime_context = _build_runtime_context(window_id, session.session_id, turn_id)

        await runtime_store.mark_session_running(session.session_id, turn_id)
        reply = await agent.chat(message, model=model, runtime_context=runtime_context)
        await runtime_store.cancel_turn_interactions(
            session.session_id,
            turn_id,
            cancel_reason="turn_completed",
        )
        await runtime_store.mark_session_idle(session.session_id, turn_id)

        return web.json_response({
            "reply": reply,
            "projectPath": project_path,
        })

    except TurnPausedError:
        if session is not None:
            session_status = await runtime_store.derive_session_status(session.session_id)
            return _control_plane_error_response(
                error_type="SESSION_PAUSED",
                message="The active session is paused by a pending blocking interaction.",
                session=session,
                session_status=session_status,
            )
        return web.json_response({"error": "Session paused"}, status=409)
    except Exception as exc:
        logger.exception(f"Chat error: {exc}")
        if session is not None:
            await runtime_store.cancel_turn_interactions(
                session.session_id,
                turn_id,
                cancel_reason="turn_failed",
            )
            await runtime_store.mark_session_idle(session.session_id, turn_id)
        return web.json_response({"error": str(exc)}, status=500)
    finally:
        if agent is not None:
            agent.clear_runtime_context()


async def chat_stream_handler(request: web.Request) -> web.StreamResponse:
    """
    Handle streaming chat requests using Server-Sent Events.
    """
    try:
        data = await request.json()
    except web.HTTPRequestEntityTooLarge:
        return web.json_response(
            {
                "error": "request_too_large: request body exceeds server limit",
                "errorType": "request_too_large",
            },
            status=413,
        )
    except json.JSONDecodeError:
        return web.json_response({"error": "Invalid JSON"}, status=400)

    project_path = data.get("projectPath", "")
    window_id = data.get("windowId", "primary") or "primary"
    worktree_path = data.get("worktreePath")
    client_message_id = data.get("clientMessageId")
    message = data.get("message", "")
    images = data.get("images", [])
    attachment_ids = data.get("attachmentIds", [])
    attachments = _sanitize_history_attachments(data.get("attachments"))
    model = data.get("model")
    effort = data.get("effort")
    thinking = data.get("thinking")
    context = data.get("context")

    logger.info(
        f"[chat_stream] Received request: windowId={window_id}, "
        f"projectPath={project_path if project_path else 'None'}"
    )

    if not message and not images and not attachment_ids:
        return web.json_response({"error": "Message or attachments cannot be empty"}, status=400)

    if not model:
        return web.json_response({"error": "Model is required"}, status=400)

    if not isinstance(attachment_ids, list):
        return web.json_response(
            {"error": "attachmentIds must be a list", "errorType": "attachment_invalid"},
            status=400,
        )

    directory_error = await _validate_chat_directories(
        window_id=window_id,
        project_path=project_path,
        worktree_path=worktree_path,
    )
    if directory_error is not None:
        return directory_error

    try:
        attachment_image_blocks = resolve_attachment_image_blocks(project_path, attachment_ids)
    except AttachmentResolutionError as exc:
        return web.json_response(
            {"error": exc.message, "errorType": exc.error_type},
            status=exc.status,
        )

    control_plane_error = await _check_chat_request_control_plane(
        request,
        window_id=window_id,
        project_path=project_path,
        worktree_path=worktree_path,
    )
    if control_plane_error is not None:
        return control_plane_error

    turn_id = str(uuid.uuid4())
    requested_session_id = _get_requested_session_id(request)

    session: RuntimeSessionRecord | None = None

    try:
        agent, session = await get_agent(window_id, project_path, worktree_path)

        if requested_session_id and requested_session_id != session.session_id:
            session_status = await runtime_store.derive_session_status(session.session_id)
            return _control_plane_error_response(
                error_type="SESSION_EXPIRED",
                message="Requested session is no longer active for this window.",
                session=session,
                session_status=session_status,
            )

        if not _agent_is_connected(agent):
            await agent.connect(effort=effort, thinking=thinking, model=model)
        elif model and model != agent.get_current_model():
            await agent.set_model(model)
    except Exception as exc:
        logger.exception(f"Stream bootstrap error: {exc}")
        if session is not None:
            await runtime_store.mark_session_error(session.session_id)
        return web.json_response({"error": str(exc)}, status=500)

    response = web.StreamResponse(
        status=200,
        headers={
            "Content-Type": "text/event-stream",
            "Cache-Control": "no-cache",
            "Connection": "keep-alive",
            "X-Session-Id": session.session_id,
        },
    )
    await response.prepare(request)

    runtime_context = _build_runtime_context(window_id, session.session_id, turn_id)
    message_context = _build_context_with_chat_attachments(
        context,
        project_path=project_path,
        client_message_id=client_message_id,
        attachments=attachments,
        attachment_ids=attachment_ids,
    )
    stream_mapper = MainStreamMapper(session_id=session.session_id, turn_id=turn_id)
    terminal_event: dict[str, Any] | None = None
    client_stream_connected = True
    client_disconnect_logged = False
    turn_paused = False
    delta_coalescer = _StreamDeltaCoalescer()

    def _log_client_stream_disconnected_once() -> None:
        nonlocal client_disconnect_logged
        if client_disconnect_logged:
            return
        client_disconnect_logged = True
        logger.info(
            "Client stream disconnected; continue turn in transcript-only mode. "
            "windowId=%s sessionId=%s turnId=%s",
            window_id,
            session.session_id,
            turn_id,
        )

    async def _emit_stream_chunk(chunk: StreamChunk) -> None:
        nonlocal client_stream_connected
        _, client_stream_connected = await _append_chunk_events(
            stream_mapper=stream_mapper,
            chunk=chunk,
            session_id=session.session_id,
            turn_id=turn_id,
            window_id=window_id,
            response=response,
            client_stream_connected=client_stream_connected,
        )
        if not client_stream_connected:
            _log_client_stream_disconnected_once()

    try:
        if not session.ready_announced:
            session_snapshot = await runtime_store.get_session_snapshot(session.session_id)
            if session_snapshot is not None:
                client_stream_connected = await _try_write_sse_data(
                    response,
                    _build_session_ready_event(session_snapshot),
                )
                if not client_stream_connected:
                    _log_client_stream_disconnected_once()
            await runtime_store.mark_session_ready_announced(session.session_id)

        await runtime_store.mark_session_running(session.session_id, turn_id)
        await runtime_store.append_user_history(
            session_id=session.session_id,
            turn_id=turn_id,
            window_id=window_id,
            client_message_id=client_message_id,
            message=message,
            attachments=attachments,
        )

        async for chunk in agent.chat_stream(
            message,
            images=images,
            image_blocks=attachment_image_blocks,
            client_message_id=client_message_id,
            effort=effort,
            thinking=thinking,
            model=model,
            context=message_context,
            runtime_context=runtime_context,
        ):
            await delta_coalescer.process(chunk, _emit_stream_chunk)

        await delta_coalescer.flush(_emit_stream_chunk)
        terminal_event = stream_mapper.build_success_terminal_event()
        await runtime_store.append_event_history(
            session_id=session.session_id,
            turn_id=turn_id,
            window_id=window_id,
            event_payload=terminal_event,
        )
        # 捕获并持久化 SDK 原生 session_id（续聊 resume 用，写入对话 meta/index）
        try:
            _sdk_sid = agent.get_sdk_session_id() if hasattr(agent, "get_sdk_session_id") else None
            if _sdk_sid:
                await runtime_store.set_sdk_session_id(session.session_id, _sdk_sid)
        except Exception as _exc:  # noqa: BLE001 - 捕获失败不阻断回合
            logger.warning("[history] capture sdk session id failed: %s", _exc)
        if client_stream_connected:
            client_stream_connected = await _try_write_sse_data(response, terminal_event)

    except TurnPausedError as exc:
        await delta_coalescer.flush(_emit_stream_chunk)
        pending_interaction_id = exc.interaction_id
        while True:
            logger.info(
                "Stream paused by interaction. windowId=%s sessionId=%s turnId=%s interactionId=%s",
                window_id,
                session.session_id,
                turn_id,
                pending_interaction_id,
            )

            try:
                interaction_result = await runtime_store.wait_for_interaction(pending_interaction_id)
            except KeyError:
                turn_paused = True
                break

            if interaction_result.get("status") != "resolved":
                # cancelled / expired → 正常收尾为 turn 结束
                turn_paused = False
                break

            binding = await runtime_store.get_runtime_binding(pending_interaction_id)
            if binding is None or binding.runtime_id != OPENAI_RUNTIME_ID:
                # 非 OpenAI Runtime（Claude 不走此分支，其 pause 靠 can_use_tool 内 await）
                # 或 binding 已被清理（理论上不应发生）
                turn_paused = True
                break

            await runtime_store.clear_runtime_binding(pending_interaction_id)
            resolution_payload = interaction_result.get("resolutionPayload") or {}

            try:
                async for chunk in agent.resume_interaction_stream(
                    interaction_id=pending_interaction_id,
                    binding=binding,
                    resolution_payload=resolution_payload,
                    session=session,
                ):
                    await delta_coalescer.process(chunk, _emit_stream_chunk)
                await delta_coalescer.flush(_emit_stream_chunk)
            except TurnPausedError as new_exc:
                await delta_coalescer.flush(_emit_stream_chunk)
                # 连续 pause（Agent 又问了新问题），进入下一轮等待
                pending_interaction_id = new_exc.interaction_id
                continue
            except Exception as resume_exc:
                await delta_coalescer.flush(_emit_stream_chunk)
                logger.exception(f"Resume stream error: {resume_exc}")
                terminal_event = stream_mapper.build_exception_terminal_event(resume_exc)
                await runtime_store.append_event_history(
                    session_id=session.session_id,
                    turn_id=turn_id,
                    window_id=window_id,
                    event_payload=terminal_event,
                )
                if client_stream_connected:
                    client_stream_connected = await _try_write_sse_data(
                        response, {"error": str(resume_exc)}
                    )
                    if client_stream_connected:
                        client_stream_connected = await _try_write_sse_data(response, terminal_event)
                turn_paused = False
                break

            # Resume 顺利跑完（未抛 TurnPausedError）→ turn 正常完成
            await delta_coalescer.flush(_emit_stream_chunk)
            terminal_event = stream_mapper.build_success_terminal_event()
            await runtime_store.append_event_history(
                session_id=session.session_id,
                turn_id=turn_id,
                window_id=window_id,
                event_payload=terminal_event,
            )
            if client_stream_connected:
                client_stream_connected = await _try_write_sse_data(response, terminal_event)
            turn_paused = False
            break
    except Exception as exc:
        await delta_coalescer.flush(_emit_stream_chunk)
        logger.exception(f"Stream error: {exc}")
        terminal_event = stream_mapper.build_exception_terminal_event(exc)
        await runtime_store.append_event_history(
            session_id=session.session_id,
            turn_id=turn_id,
            window_id=window_id,
            event_payload=terminal_event,
        )
        if client_stream_connected:
            client_stream_connected = await _try_write_sse_data(response, {"error": str(exc)})
            if client_stream_connected:
                client_stream_connected = await _try_write_sse_data(response, terminal_event)
    finally:
        if client_stream_connected:
            try:
                await response.write(b"data: [DONE]\n\n")
            except (BrokenPipeError, ConnectionResetError, RuntimeError):
                pass
        agent.clear_runtime_context()
        await _finalize_turn_state(session.session_id, turn_id, terminal_event, paused=turn_paused)

    return response


async def clear_history_handler(request: web.Request) -> web.Response:
    """
    Clear conversation history for a window.
    """
    try:
        data = await request.json()
    except json.JSONDecodeError:
        return web.json_response({"error": "Invalid JSON"}, status=400)

    window_id = data.get("windowId")
    if not window_id:
        return web.json_response({"error": "windowId required"}, status=400)

    async with _agents_lock:
        if window_id in agents or await runtime_store.get_active_session(window_id):
            await _teardown_window_locked(
                window_id,
                cancel_reason="clear_history",
                drop_window_seq=False,
                sleep_after_disconnect=False,
            )
            logger.info(f"Cleared history for window: {window_id}")

    return web.json_response({"success": True})


async def get_history_handler(request: web.Request) -> web.Response:
    """
    Get conversation history for a window.
    """
    window_id = request.query.get("windowId", "primary")
    # 纯内存:刷新(Agent 存活)恢复实时会话。Agent 重启后内存为空 → 返回空,窗口空窗起步;
    # 过往对话经历史面板(/api/history/sessions + /session)浏览,不在此自动回放(避免重启误恢复 + 轮询死循环)。
    session, history, interactions = await runtime_store.get_history_for_window(window_id)
    return web.json_response({
        "history": history,
        "interactions": interactions,
        "windowId": window_id,
        "session": session,
        "sessionId": session["sessionId"] if session else None,
        "sessionStatus": session["status"] if session else None,
    })


async def list_history_sessions_handler(request: web.Request) -> web.Response:
    """列出项目历史会话(.history/index.json)。供历史面板渲染。"""
    project_path = request.query.get("projectPath") or None
    sessions = await runtime_store.list_history_sessions(project_path) if project_path else []
    return web.json_response({"sessions": sessions})


async def load_history_session_handler(request: web.Request) -> web.Response:
    """按 sessionId 加载某历史会话事件流(只读回放)。返回与 /api/history 同形。"""
    project_path = request.query.get("projectPath") or None
    session_id = request.query.get("sessionId") or None
    if not project_path or not session_id:
        return web.json_response({"error": "projectPath and sessionId required"}, status=400)
    session, history, interactions = await runtime_store.load_history_session(project_path, session_id)
    return web.json_response({
        "history": history,
        "interactions": interactions,
        "windowId": (session.get("windowId") if session else "") or "",
        "session": session,
        "sessionId": session_id,
        "sessionStatus": (session.get("status") if session else "closed") or "closed",
    })


def _sdk_transcript_exists(working_dir: str, sdk_session_id: str) -> bool:
    """探测 SDK 原生 transcript 是否存在(决定续聊 contextStatus)。
    路径 = {CLAUDE_CONFIG_DIR|~/.claude}/projects/{sanitize(realpath(NFC(cwd)))}/{sdkSessionId}.jsonl,
    与 SDK 0.2.87 sessions.py 的 _sanitize_path 规则一致(非字母数字→-;>200 字符的 hash 后缀 v1 暂不处理)。
    """
    if not sdk_session_id or not working_dir:
        return False
    import re
    import unicodedata
    try:
        resolved = os.path.realpath(working_dir)
    except OSError:
        resolved = working_dir
    sanitized = re.sub(r"[^a-zA-Z0-9]", "-", unicodedata.normalize("NFC", resolved))
    config_dir = os.environ.get("CLAUDE_CONFIG_DIR") or os.path.join(os.path.expanduser("~"), ".claude")
    return os.path.isfile(os.path.join(config_dir, "projects", sanitized, f"{sdk_session_id}.jsonl"))


async def activate_conversation_handler(request: web.Request) -> web.Response:
    """激活(切换/恢复)一段历史对话:拆窗口当前 agent → rehydrate host 会话 → connect(resume=sdkSessionId)
    恢复 SDK 上下文 → 返回显示历史。此后该窗口的 sendMessage 打进这段对话(隔离 + 记忆)。
    """
    global _window_counter
    try:
        data = await request.json()
    except json.JSONDecodeError:
        return web.json_response({"error": "Invalid JSON"}, status=400)

    window_id = data.get("windowId")
    conversation_id = data.get("conversationId")
    project_path = data.get("projectPath", "")
    model = data.get("model")
    effort = data.get("effort")
    thinking = data.get("thinking")
    if not window_id or not conversation_id or not project_path:
        return web.json_response({"error": "windowId, conversationId, projectPath required"}, status=400)
    if not model:
        return web.json_response({"error": "model required"}, status=400)

    sessions = await runtime_store.list_history_sessions(project_path)
    entry = next((s for s in sessions if s.get("sessionId") == conversation_id), None)
    if not entry:
        return web.json_response({"error": "conversation not found"}, status=404)

    sdk_session_id = entry.get("sdkSessionId")
    worktree_path = entry.get("worktreePath")
    working_dir = worktree_path or project_path
    context_status = "live" if (sdk_session_id and _sdk_transcript_exists(working_dir, sdk_session_id)) else "expired"

    settings = get_settings()
    runtime_provider = _resolve_runtime_provider_from_settings(settings)

    async with _agents_lock:
        if window_id in agents or await runtime_store.get_active_session(window_id):
            await _teardown_window_locked(
                window_id,
                cancel_reason="conversation_switch",
                drop_window_seq=False,
                sleep_after_disconnect=False,
            )

        events = await runtime_store.rehydrate_conversation(window_id, project_path, conversation_id, entry)

        if window_id == "primary":
            seq = 0
            _window_seq_map.setdefault(window_id, seq)
        else:
            seq = _window_seq_map.get(window_id)
            if seq is None:
                seq = _window_counter
                _window_counter += 1
                _window_seq_map[window_id] = seq

        agent = create_agent(
            runtime_provider,
            project_path=project_path,
            working_directory=working_dir,
            window_seq=seq,
        )
        agents[window_id] = agent
        if hasattr(agent, "set_background_push"):
            agent.set_background_push(_background_task_pusher)
        if hasattr(agent, "set_background_progress_push"):
            agent.set_background_progress_push(_background_progress_pusher)

        try:
            await agent.connect(
                effort=effort,
                thinking=thinking,
                model=model,
                resume_session_id=(sdk_session_id if context_status == "live" else None),
            )
        except Exception as exc:  # noqa: BLE001
            logger.error("[history] activate conversation connect failed: %s", exc)
            await _teardown_window_locked(
                window_id,
                cancel_reason="activate_failed",
                drop_window_seq=False,
                sleep_after_disconnect=False,
            )
            return web.json_response({"error": f"connect failed: {exc}"}, status=500)

        session = await runtime_store.get_session_snapshot(conversation_id)

    return web.json_response({
        "history": events,
        "interactions": [],
        "windowId": window_id,
        "session": session,
        "sessionId": conversation_id,
        "sessionStatus": (session.get("status") if session else "idle") or "idle",
        "contextStatus": context_status,
    })


async def new_conversation_handler(request: web.Request) -> web.Response:
    """开始一段新对话:拆掉窗口当前 agent/session(下一条消息会创建全新会话)。"""
    try:
        data = await request.json()
    except json.JSONDecodeError:
        return web.json_response({"error": "Invalid JSON"}, status=400)
    window_id = data.get("windowId")
    if not window_id:
        return web.json_response({"error": "windowId required"}, status=400)
    async with _agents_lock:
        if window_id in agents or await runtime_store.get_active_session(window_id):
            await _teardown_window_locked(
                window_id,
                cancel_reason="new_conversation",
                drop_window_seq=False,
                sleep_after_disconnect=False,
            )
    return web.json_response({"success": True})


async def interrupt_handler(request: web.Request) -> web.Response:
    """
    中断当前任务
    """
    try:
        data = await request.json()
    except json.JSONDecodeError:
        return web.json_response({"error": "Invalid JSON"}, status=400)

    window_id = data.get("windowId")
    if not window_id:
        return web.json_response({"error": "windowId required"}, status=400)

    async with _agents_lock:
        session = await runtime_store.get_active_session(window_id)
        agent = agents.get(window_id)

        if not agent and not session:
            return web.json_response({"error": "Agent not found"}, status=404)

        if session:
            await runtime_store.cancel_session_interactions(
                session.session_id,
                cancel_reason="interrupted",
            )
            await runtime_store.mark_session_idle(session.session_id, session.active_turn_id)

        if agent:
            agent.clear_runtime_context()
            await agent.interrupt()
        print(f"[Server] 任务中断: 窗口 {window_id}")
        logger.info(f"Interrupted task for window: {window_id}")
        return web.json_response({"success": True})


async def close_agent_handler(request: web.Request) -> web.Response:
    """
    关闭指定窗口的 Agent（窗口关闭时调用）
    """
    try:
        data = await request.json()
    except json.JSONDecodeError:
        return web.json_response({"error": "Invalid JSON"}, status=400)

    window_id = data.get("windowId")
    if not window_id:
        return web.json_response({"error": "windowId required"}, status=400)

    async with _agents_lock:
        if window_id in agents or await runtime_store.get_active_session(window_id):
            seq = _window_seq_map.get(window_id, 0)
            prefix = _get_window_prefix(seq)

            await _teardown_window_locked(
                window_id,
                cancel_reason="agent_closed",
                drop_window_seq=True,
                sleep_after_disconnect=True,
            )

            print(f"{prefix} [Server] ========== Agent 实例关闭 ==========")
            print(f"{prefix} [Server] 窗口ID: {window_id}")
            print(f"{prefix} [Server] 窗口序号: {seq}")
            print(f"{prefix} [Server] 剩余实例数: {len(agents)}")
            print(f"{prefix} [Server] =====================================")
            logger.info(f"Closed agent for window: {window_id} (seq={seq})")
            return web.json_response({"success": True})

    return web.json_response({"error": "Agent not found"}, status=404)


def _resolve_for_project_match(path_value: str | None) -> Path | None:
    if not path_value:
        return None

    try:
        return Path(path_value).expanduser().resolve(strict=False)
    except Exception:
        try:
            return Path(path_value).expanduser().absolute()
        except Exception:
            return None


def _is_same_or_child_path(candidate: Path | None, root: Path) -> bool:
    if candidate is None:
        return False

    try:
        candidate.relative_to(root)
        return True
    except ValueError:
        candidate_text = str(candidate).rstrip("\\/").casefold()
        root_text = str(root).rstrip("\\/").casefold()
        return candidate_text == root_text or candidate_text.startswith(root_text + "\\") or candidate_text.startswith(root_text + "/")


async def close_project_agents_handler(request: web.Request) -> web.Response:
    """
    关闭指定项目目录下的所有 Agent 实例。
    Server 删除/关闭项目时调用，用来释放项目根目录和 worktree 的 CWD 文件锁。
    """
    try:
        data = await request.json()
    except json.JSONDecodeError:
        return web.json_response({"error": "Invalid JSON"}, status=400)

    project_path = data.get("projectPath")
    project_root = _resolve_for_project_match(project_path)
    if project_root is None:
        return web.json_response({"error": "projectPath required"}, status=400)

    async with _agents_lock:
        matched_window_ids: set[str] = set()

        for window_id, agent in agents.items():
            agent_project = _resolve_for_project_match(getattr(agent, "project_path", None))
            agent_working_dir = _resolve_for_project_match(agent.working_directory)
            if _is_same_or_child_path(agent_project, project_root) or _is_same_or_child_path(agent_working_dir, project_root):
                matched_window_ids.add(window_id)

        for session in await runtime_store.list_sessions():
            if session.base_status in {"closed", "error"}:
                continue

            session_project = _resolve_for_project_match(session.project_path)
            session_worktree = _resolve_for_project_match(session.worktree_path)
            if _is_same_or_child_path(session_project, project_root) or _is_same_or_child_path(session_worktree, project_root):
                matched_window_ids.add(session.window_id)

        if not matched_window_ids:
            return web.json_response({"error": "Agent not found"}, status=404)

        closed_window_ids: list[str] = []
        for window_id in sorted(matched_window_ids):
            await _teardown_window_locked(
                window_id,
                cancel_reason="project_closed",
                drop_window_seq=True,
                sleep_after_disconnect=False,
            )
            closed_window_ids.append(window_id)

        await asyncio.sleep(0.5)

    print(f"[Server] 已关闭项目 Agent 实例: {project_root} ({len(closed_window_ids)} windows)")
    logger.info("Closed project agents: %s windows=%s", project_root, closed_window_ids)
    return web.json_response({"success": True, "closedWindowIds": closed_window_ids})


async def interaction_events_handler(request: web.Request) -> web.StreamResponse:
    """统一 InteractionChannel SSE 端点。"""
    response = web.StreamResponse(
        status=200,
        headers={
            "Content-Type": "text/event-stream",
            "Cache-Control": "no-cache",
            "Connection": "keep-alive",
        },
    )
    await response.prepare(request)

    queue = await runtime_store.subscribe_interactions()
    logger.info("Interaction SSE client connected")

    try:
        while True:
            payload = await queue.get()
            await response.write(
                f"event: {payload['event']}\ndata: {json.dumps(payload['record'], ensure_ascii=False)}\n\n".encode("utf-8")
            )
    except (asyncio.CancelledError, ConnectionResetError):
        pass
    finally:
        await runtime_store.unsubscribe_interactions(queue)
        logger.info("Interaction SSE client disconnected")

    return response


async def interaction_query_handler(request: web.Request) -> web.Response:
    """查询当前窗口活跃 session 下的 unresolved interactions。"""
    window_id = request.query.get("windowId", "primary")
    include_terminal = request.query.get("includeTerminal", "").strip().lower() == "true"
    if include_terminal:
        session_id, interactions = await runtime_store.get_interactions_for_window(
            window_id,
            include_terminal=True,
        )
    else:
        session_id, interactions = await runtime_store.get_pending_interactions_for_window(window_id)
    return web.json_response({
        "windowId": window_id,
        "sessionId": session_id,
        "includeTerminal": include_terminal,
        "interactions": interactions,
    })


async def interaction_submit_handler(request: web.Request) -> web.Response:
    """提交 interaction resolution。

    只做状态标记，不驱动 runtime resume。Runtime 侧（chat_stream_handler）通过
    runtime_store.wait_for_interaction 的 waiter 被唤醒后继续流式推送。
    """
    interaction_id = request.match_info["id"]

    try:
        data = await request.json()
    except json.JSONDecodeError:
        return web.json_response({"error": "Invalid JSON"}, status=400)

    resolution_payload = data.get("resolutionPayload")
    if resolution_payload is None:
        resolution_payload = {}
    if not isinstance(resolution_payload, dict):
        return web.json_response({"error": "resolutionPayload must be an object"}, status=400)

    interaction = await runtime_store.submit_interaction(interaction_id, resolution_payload)
    if interaction is None:
        return web.json_response({"error": "Unknown interaction ID"}, status=404)

    return web.json_response({
        "success": True,
        "interaction": interaction.to_public_dict(),
    })


async def interaction_cancel_handler(request: web.Request) -> web.Response:
    """取消 interaction。"""
    interaction_id = request.match_info["id"]

    try:
        data = await request.json()
    except json.JSONDecodeError:
        data = {}

    interaction = await runtime_store.cancel_interaction(
        interaction_id,
        cancel_reason=data.get("cancelReason"),
    )
    if interaction is None:
        return web.json_response({"error": "Unknown interaction ID"}, status=404)

    return web.json_response({
        "success": True,
        "interaction": interaction.to_public_dict(),
    })


# ============== Question API (AskUserQuestion facade) ==============

async def request_user_question(
    questions: list[dict],
    runtime_context: dict[str, str] | None = None,
) -> dict:
    """
    发送问题给 Web 端并等待用户回答（供 MainAgent._auto_approve_tool 调用）。
    """
    context = runtime_context or {}
    window_id = context.get("windowId")
    session_id = context.get("sessionId")
    turn_id = context.get("turnId")

    if not window_id or not session_id or not turn_id:
        raise RuntimeError("AskUserQuestion requires runtime context with windowId/sessionId/turnId")

    interaction = await runtime_store.create_interaction(
        session_id=session_id,
        turn_id=turn_id,
        window_id=window_id,
        kind="question",
        blocking=True,
        resume_token=f"resume:{uuid.uuid4()}",
        request_payload={"questions": questions},
    )

    logger.info(f"Question request sent: {interaction.interaction_id}, {len(questions)} questions")

    result = await runtime_store.wait_for_interaction(interaction.interaction_id)
    if result.get("status") == "resolved":
        resolution_payload = result.get("resolutionPayload") or {}
        return resolution_payload.get("answers", {})
    return {}


async def push_openai_question_interaction(
    *,
    questions: list[dict],
    runtime_context: dict[str, str] | None,
    runtime_binding: PendingInteractionRuntimeBinding,
):
    context = runtime_context or {}
    window_id = context.get("windowId")
    session_id = context.get("sessionId")
    turn_id = context.get("turnId")

    if not window_id or not session_id or not turn_id:
        raise RuntimeError("OpenAI question pause requires runtime context with windowId/sessionId/turnId")

    interaction = await runtime_store.create_interaction(
        session_id=session_id,
        turn_id=turn_id,
        window_id=window_id,
        kind="question",
        blocking=True,
        resume_token=f"resume:{uuid.uuid4()}",
        request_payload={"questions": questions},
        runtime_binding=runtime_binding,
    )
    logger.info(
        "OpenAI question interaction pushed: %s, %s questions",
        interaction.interaction_id,
        len(questions),
    )
    return interaction


async def question_events_handler(request: web.Request) -> web.StreamResponse:
    """兼容 SSE 端点：Web 端监听 Agent 用户问题请求（AskUserQuestion）"""
    response = web.StreamResponse(
        status=200,
        headers={
            "Content-Type": "text/event-stream",
            "Cache-Control": "no-cache",
            "Connection": "keep-alive",
        },
    )
    await response.prepare(request)

    queue = await runtime_store.subscribe_interactions()
    logger.info("Question SSE client connected")

    try:
        while True:
            payload = await queue.get()
            record = payload["record"]
            if payload["event"] != "interaction.pushed" or record.get("kind") != "question":
                continue

            event = {
                "requestId": record["interactionId"],
                "questions": record["requestPayload"].get("questions", []),
            }
            await response.write(
                f"event: question_request\ndata: {json.dumps(event, ensure_ascii=False)}\n\n".encode("utf-8")
            )
    except (asyncio.CancelledError, ConnectionResetError):
        pass
    finally:
        await runtime_store.unsubscribe_interactions(queue)
        logger.info("Question SSE client disconnected")

    return response


async def question_answer_handler(request: web.Request) -> web.Response:
    """
    Web 端提交用户答案（AskUserQuestion）
    """
    try:
        data = await request.json()
    except json.JSONDecodeError:
        return web.json_response({"error": "Invalid JSON"}, status=400)

    request_id = data.get("requestId")
    if not request_id:
        return web.json_response({"error": "requestId required"}, status=400)

    interaction = await runtime_store.get_interaction(request_id)
    if interaction is None or interaction.kind != "question":
        return web.json_response({"error": "Unknown request ID"}, status=404)

    if data.get("cancelled"):
        await runtime_store.cancel_interaction(request_id, cancel_reason="question_cancelled")
    else:
        await runtime_store.submit_interaction(
            request_id,
            {"answers": data.get("answers", {})},
        )

    logger.info(f"Question answer received: {request_id}")
    return web.json_response({"success": True})


# ============== Screenshot API facade ==============

async def screenshot_events_handler(request: web.Request) -> web.StreamResponse:
    """
    SSE 端点：Web 端监听 Agent 截图请求
    """
    response = web.StreamResponse(
        status=200,
        headers={
            "Content-Type": "text/event-stream",
            "Cache-Control": "no-cache",
            "Connection": "keep-alive",
        },
    )
    await response.prepare(request)

    queue = await runtime_store.subscribe_interactions()
    logger.info("Screenshot SSE client connected")

    try:
        while True:
            payload = await queue.get()
            record = payload["record"]
            if payload["event"] != "interaction.pushed" or record.get("kind") != "screenshot":
                continue

            event = {
                "requestId": record["interactionId"],
                "roomId": record["requestPayload"].get("roomId"),
            }
            await response.write(
                f"event: screenshot_request\ndata: {json.dumps(event, ensure_ascii=False)}\n\n".encode("utf-8")
            )
    except (asyncio.CancelledError, ConnectionResetError):
        pass
    finally:
        await runtime_store.unsubscribe_interactions(queue)
        logger.info("Screenshot SSE client disconnected")

    return response


async def screenshot_request_handler(request: web.Request) -> web.Response:
    """
    Agent 请求截图 → 通知 Web 端
    """
    try:
        data = await request.json()
    except json.JSONDecodeError:
        data = {}

    window_id = data.get("windowId", "primary") or "primary"
    room_id = data.get("roomId")

    session, turn_id = await runtime_store.get_active_turn_id(window_id)
    if session is None or not turn_id:
        return web.json_response(
            {"error": "No active turn available for screenshot request"},
            status=409,
        )

    project_path = data.get("projectPath") or session.project_path or "."

    interaction = await runtime_store.create_interaction(
        session_id=session.session_id,
        turn_id=turn_id,
        window_id=window_id,
        kind="screenshot",
        blocking=False,
        resume_token="resume:none",
        request_payload={"roomId": room_id},
        expires_at=datetime.utcnow() + timedelta(seconds=10),
    )

    logger.info(f"Screenshot request sent: {interaction.interaction_id}, roomId={room_id}")

    try:
        result = await asyncio.wait_for(
            runtime_store.wait_for_interaction(interaction.interaction_id),
            timeout=10.0,
        )
    except asyncio.TimeoutError:
        logger.warning(f"Screenshot request timeout: {interaction.interaction_id}")
        await runtime_store.cancel_interaction(
            interaction.interaction_id,
            cancel_reason="screenshot_timeout",
            final_status="expired",
        )
        return web.json_response({"error": "Screenshot request timeout"}, status=504)

    status = result.get("status")
    if status != "resolved":
        return web.json_response({"error": "Screenshot request cancelled"}, status=409)

    resolution_payload = result.get("resolutionPayload") or {}
    if resolution_payload.get("error"):
        return web.json_response({"error": resolution_payload["error"]}, status=400)

    image_data = resolution_payload.get("imageData")
    if not image_data:
        return web.json_response({"error": "Screenshot imageData is required"}, status=400)

    filepath, pure_base64 = _save_screenshot(image_data, project_path, room_id)

    logger.info(f"Screenshot saved: {filepath}")
    return web.json_response({
        "path": filepath,
        "base64": pure_base64,
    })


async def screenshot_result_handler(request: web.Request) -> web.Response:
    """
    Web 端返回截图结果
    """
    try:
        data = await request.json()
    except json.JSONDecodeError:
        return web.json_response({"error": "Invalid JSON"}, status=400)

    request_id = data.get("requestId")
    if not request_id:
        return web.json_response({"error": "requestId required"}, status=400)

    interaction = await runtime_store.get_interaction(request_id)
    if interaction is None or interaction.kind != "screenshot":
        return web.json_response({"error": "Unknown request ID"}, status=404)

    await runtime_store.submit_interaction(
        request_id,
        {
            "imageData": data.get("imageData"),
            "error": data.get("error"),
        },
    )

    logger.info(f"Screenshot result received: {request_id}")
    return web.json_response({"success": True})


def _save_screenshot(base64_data: str, project_path: str, room_id: str = None) -> tuple[str, str]:
    """
    保存 Base64 图片到文件
    """
    pure_base64 = base64_data
    if "," in base64_data:
        pure_base64 = base64_data.split(",", 1)[1]

    image_bytes = base64.b64decode(pure_base64)

    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    prefix = f"room_{room_id}" if room_id else "canvas"
    filename = f"{prefix}_{timestamp}.png"

    save_dir = Path(project_path) / "screenshots"
    save_dir.mkdir(parents=True, exist_ok=True)
    filepath = save_dir / filename

    filepath.write_bytes(image_bytes)
    return str(filepath), pure_base64


async def screenshot_save_handler(request: web.Request) -> web.Response:
    """
    保存截图到本地临时目录
    """
    try:
        data = await request.json()
    except json.JSONDecodeError:
        return web.json_response({"error": "Invalid JSON"}, status=400)

    image_data = data.get("imageData")
    if not image_data:
        return web.json_response({"error": "Missing imageData"}, status=400)

    project_path = data.get("projectPath")

    if project_path:
        docs_dir = Path(project_path) / "screenshots"
        logger.info(f"Using project screenshots dir: {docs_dir}")
    else:
        docs_dir = resolve_bimcanvas_home() / "Screenshots"
        logger.info(f"Using global screenshots dir: {docs_dir}")

    docs_dir.mkdir(parents=True, exist_ok=True)

    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    filename = data.get("filename") or f"screenshot_{timestamp}.png"
    filepath = docs_dir / filename

    pure_base64 = image_data
    if "," in image_data:
        pure_base64 = image_data.split(",", 1)[1]

    image_bytes = base64.b64decode(pure_base64)
    filepath.write_bytes(image_bytes)

    logger.info(f"Screenshot saved to: {filepath}")
    return web.json_response({"path": str(filepath)})


async def plugin_action_handler(request: web.Request) -> web.Response:
    """通用插件 Web Action 路由处理器。

    路由: POST /api/plugin-actions/{namespace}/{action}
    将请求分发到插件注册的 web_action handler。
    """
    namespace = request.match_info["namespace"]
    action = request.match_info["action"]

    handler = _plugin_action_registry.get((namespace, action))
    if handler is None:
        return web.json_response(
            {"error": f"action '{action}' not found for plugin '{namespace}'"},
            status=404,
        )

    try:
        data = await request.json()
    except Exception:
        data = {}

    try:
        result = await handler(data)
        return web.json_response(result)
    except Exception as exc:  # noqa: BLE001
        logger.exception("plugin action %s/%s 执行失败", namespace, action)
        return web.json_response({"error": str(exc)}, status=500)


async def _startup_register_web_actions(app: web.Application) -> None:
    """HTTP server 启动时扫描所有已安装插件，注册 web_actions 到 registry。

    不依赖 Agent 初始化，不依赖 active plugin，任何已安装插件的 web_action 均可用。
    """
    import importlib.util
    from pathlib import Path
    from ..config.loader import resolve_bimcanvas_home
    from bimcanvas_plugin_sdk import McpServerBuilder
    from bimcanvas_plugin_sdk.context import PluginContext

    try:
        home = Path(resolve_bimcanvas_home())
        plugins_dir = home / "plugins"
        print(f"[web_actions] home={home}, plugins_dir={plugins_dir}, exists={plugins_dir.is_dir()}", flush=True)
        if not plugins_dir.is_dir():
            logger.info("plugins_dir 不存在，跳过 web_actions 注册: %s", plugins_dir)
            return

        import os as _os
        server_url = _os.environ.get("BIMCANVAS_SERVER_URL", "http://localhost:5000").rstrip("/")

        for plugin_dir in sorted(plugins_dir.iterdir()):
            if not plugin_dir.is_dir() or plugin_dir.name == "core-base":
                continue

            mcp_tools_dir = plugin_dir / "mcp_tools"
            py_files = sorted(mcp_tools_dir.glob("*.py")) if mcp_tools_dir.is_dir() else []
            if not py_files:
                continue

            plugin_id = plugin_dir.name
            namespace = py_files[0].stem
            entry_path = py_files[0].resolve()

            try:
                plugin_ctx = PluginContext(
                    server_url=server_url,
                    project_path=None,
                    active_plugin_id=plugin_id,
                    logger=logging.getLogger(f"bimcanvas.plugin.{plugin_id}"),
                    session=None,  # web_action 用自己的 aiohttp session，不依赖长连接
                )
                builder = McpServerBuilder(namespace=namespace, context=plugin_ctx)

                module_name = f"_bimcanvas_webaction_{plugin_id.replace('-', '_')}"
                spec = importlib.util.spec_from_file_location(module_name, entry_path)
                if spec is None or spec.loader is None:
                    continue
                module = importlib.util.module_from_spec(spec)
                import sys as _sys
                _sys.modules[module_name] = module
                spec.loader.exec_module(module)

                register_fn = getattr(module, "register", None)
                if not callable(register_fn):
                    continue
                register_fn(builder)

                for wa in builder.web_actions:
                    _plugin_action_registry[(namespace, wa.name)] = wa.handler
                    logger.info("startup: web_action 已注册 %s/%s", namespace, wa.name)

            except Exception as exc:  # noqa: BLE001
                logger.warning("startup: plugin %s web_actions 注册失败: %s", plugin_id, exc, exc_info=True)

    except Exception as exc:  # noqa: BLE001
        logger.warning("startup: _startup_register_web_actions 失败: %s", exc, exc_info=True)


async def on_shutdown(app: web.Application) -> None:
    logger.info("Shutting down, cleaning up agents...")
    await cleanup_agents()


def create_app() -> web.Application:
    """
    Create and configure the aiohttp application.

    Returns:
        Configured web.Application
    """
    app = web.Application(client_max_size=12 * 1024**2)

    app.on_startup.append(_startup_register_web_actions)
    app.on_shutdown.append(on_shutdown)

    cors = aiohttp_cors.setup(app, defaults={
        "*": aiohttp_cors.ResourceOptions(
            allow_credentials=True,
            expose_headers="*",
            allow_headers="*",
            allow_methods=["GET", "POST", "OPTIONS"],
        )
    })

    routes = [
        web.get("/health", health_handler),
        web.get("/api/config", config_handler),
        web.post("/api/chat", chat_handler),
        web.post("/api/chat/stream", chat_stream_handler),
        web.post("/api/clear-history", clear_history_handler),
        web.post("/api/agent/close", close_agent_handler),
        web.post("/api/agent/close-project", close_project_agents_handler),
        web.get("/api/history", get_history_handler),
        web.get("/api/history/sessions", list_history_sessions_handler),
        web.get("/api/history/session", load_history_session_handler),
        web.post("/api/conversation/activate", activate_conversation_handler),
        web.post("/api/conversation/new", new_conversation_handler),
        web.post("/api/interrupt", interrupt_handler),
        web.get("/api/interaction/events", interaction_events_handler),
        web.get("/api/interaction", interaction_query_handler),
        web.post("/api/interaction/{id}/submit", interaction_submit_handler),
        web.post("/api/interaction/{id}/cancel", interaction_cancel_handler),
        web.get("/api/screenshot/events", screenshot_events_handler),
        web.post("/api/screenshot/request", screenshot_request_handler),
        web.post("/api/screenshot/result", screenshot_result_handler),
        web.post("/api/screenshot/save", screenshot_save_handler),
        web.get("/api/question/events", question_events_handler),
        web.post("/api/question/answer", question_answer_handler),
        web.post("/api/plugin-actions/{namespace}/{action}", plugin_action_handler),
    ]

    routes_by_path: dict[str, list] = {}
    for route in routes:
        if route.path not in routes_by_path:
            routes_by_path[route.path] = []
        routes_by_path[route.path].append(route)

    for path, path_routes in routes_by_path.items():
        resource = cors.add(app.router.add_resource(path))
        for route in path_routes:
            cors.add(resource.add_route(route.method, route.handler))

    return app


def run_server(host: str = None, port: int = None) -> None:
    """
    Start the HTTP server.

    Args:
        host: Host to bind to (default from settings)
        port: Port to bind to (default from settings)
    """
    settings = get_settings()

    host = host or settings.server_host
    port = port or settings.server_port

    import socket
    import time

    for attempt in range(3):
        try:
            with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
                sock.bind((host, port))
            break
        except OSError:
            if attempt < 2:
                logger.warning(f"端口 {port} 暂不可用，2秒后重试 ({attempt + 1}/3)...")
                time.sleep(2)
            else:
                logger.error(f"端口 {port} 持续被占用，Agent 服务无法启动")
                return

    app = create_app()

    logger.info(f"Agent 服务已启动: http://{host}:{port}")
    web.run_app(app, host=host, port=port, print=lambda s: logger.info(s))
