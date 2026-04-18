"""Host-side MainStreamEvent mapper with legacy compatibility."""

from __future__ import annotations

from datetime import datetime, timezone
from typing import Any
import uuid

from .chunks import StreamChunk


EVENT_TYPE_MAP: dict[str, str] = {
    "thinking": "thinking.delta",
    "thinking_complete": "thinking.completed",
    "text": "text.delta",
    "text_complete": "text.completed",
    "subagent_start": "subtask.started",
    "subagent_complete": "subtask.completed",
    "tool_call_start": "tool.started",
}


def build_root_subtask_id(turn_id: str) -> str:
    return f"st-root-{turn_id}"


def build_legacy_chunk_event_data(
    chunk: StreamChunk,
    *,
    include_tool_output: bool = True,
) -> dict[str, Any]:
    event_data = {"type": chunk.type}

    if chunk.content:
        event_data["content"] = chunk.content

    if chunk.subagent_id:
        event_data["subAgentId"] = chunk.subagent_id
    if chunk.subagent_name:
        event_data["subAgentName"] = chunk.subagent_name
    if chunk.subagent_type:
        event_data["subAgentType"] = chunk.subagent_type

    if chunk.tool_call_id:
        event_data["toolCallId"] = chunk.tool_call_id
    if chunk.tool_name:
        event_data["toolName"] = chunk.tool_name
    if chunk.tool_description:
        event_data["toolDescription"] = chunk.tool_description
    if chunk.tool_params:
        event_data["toolParams"] = chunk.tool_params
    if chunk.tool_output and include_tool_output:
        event_data["toolOutput"] = chunk.tool_output

    if chunk.success is not None:
        event_data["success"] = chunk.success
    if chunk.error:
        event_data["error"] = chunk.error

    if chunk.error_type:
        event_data["errorType"] = chunk.error_type
    if chunk.error_content:
        event_data["errorContent"] = chunk.error_content
    if chunk.hidden_content:
        event_data["hiddenContent"] = chunk.hidden_content

    if chunk.task_id:
        event_data["taskId"] = chunk.task_id
    if chunk.timeout is not None:
        event_data["timeout"] = chunk.timeout

    return event_data


class MainStreamMapper:
    """Map legacy StreamChunk events to MainStreamEvent envelopes."""

    def __init__(self, *, session_id: str, turn_id: str) -> None:
        self.session_id = session_id
        self.turn_id = turn_id
        self.root_subtask_id = build_root_subtask_id(turn_id)
        self._blocking_tool_failure: dict[str, Any] | None = None
        self._sdk_error_subtype: str | None = None
        self._sdk_error_message: str | None = None
        self._api_error_code: str | None = None
        self._api_error_message: str | None = None

    def map_chunk(self, chunk: StreamChunk) -> list[dict[str, Any]]:
        self._record_chunk_state(chunk)

        if chunk.type == "task_output_polling":
            return [build_legacy_chunk_event_data(chunk)]

        if chunk.type == "tool_call_complete":
            return self._map_tool_completion(chunk)

        event_type = EVENT_TYPE_MAP.get(chunk.type)
        legacy = build_legacy_chunk_event_data(chunk)
        if not event_type:
            return [legacy]

        payload = self._build_chunk_payload(chunk, event_type)
        return [
            self._build_event(
                event_type,
                payload,
                legacy=legacy,
                subtask_id=self._resolve_subtask_id(chunk),
                tool_call_id=chunk.tool_call_id,
            )
        ]

    def build_success_terminal_event(self) -> dict[str, Any]:
        if self._sdk_error_subtype == "error_max_turns":
            return self._build_turn_failed(
                stop_reason="max_turns",
                error=self._build_error(
                    code="MAX_TURNS_EXCEEDED",
                    layer="turn",
                    message=self._sdk_error_message or "Reached runtime max turns.",
                    retryable=False,
                ),
            )

        runtime_failure = self._build_recorded_runtime_failure()
        if runtime_failure is not None:
            return runtime_failure

        return self._build_turn_completed()

    def build_exception_terminal_event(self, exc: Exception) -> dict[str, Any]:
        if self._sdk_error_subtype == "error_max_turns":
            return self.build_success_terminal_event()

        if self._blocking_tool_failure is not None:
            return self._build_turn_failed(
                stop_reason="tool_error",
                error=self._build_error(
                    code="TOOL_EXECUTION_FAILED",
                    layer="tool",
                    message=self._blocking_tool_failure["message"],
                    retryable=True,
                    related_tool_call_id=self._blocking_tool_failure["toolCallId"],
                ),
            )

        runtime_failure = self._build_recorded_runtime_failure()
        if runtime_failure is not None:
            return runtime_failure

        if self._is_interrupted_exception(exc):
            return self._build_turn_failed(
                stop_reason="interrupted",
                error=self._build_error(
                    code="INTERRUPTED",
                    layer="turn",
                    message=str(exc) or "Turn interrupted.",
                    retryable=True,
                ),
            )

        return self._build_turn_failed(
            stop_reason="runtime_error",
            error=self._build_error(
                code="SDK_CONNECTION_ERROR",
                layer="session",
                message=str(exc) or "Claude runtime stream failed unexpectedly.",
                retryable=False,
            ),
        )

    def _map_tool_completion(self, chunk: StreamChunk) -> list[dict[str, Any]]:
        events: list[dict[str, Any]] = []
        subtask_id = self._resolve_subtask_id(chunk)
        output_value = self._resolve_tool_output(chunk)

        if output_value and not chunk.suppress_public_tool_output:
            legacy_output = None
            if chunk.tool_output and not chunk.suppress_public_tool_output:
                legacy_output = {
                    "type": "tool_call_output",
                    "toolCallId": chunk.tool_call_id,
                    "toolOutput": chunk.tool_output,
                }
            events.append(
                self._build_event(
                    "tool.output",
                    {"output": output_value},
                    legacy=legacy_output,
                    subtask_id=subtask_id,
                    tool_call_id=chunk.tool_call_id,
                )
            )

        events.append(
            self._build_event(
                "tool.completed",
                self._compact(
                    {
                        "output": output_value,
                        "success": chunk.success,
                        "errorType": chunk.error_type,
                        "error": chunk.error,
                    }
                ),
                legacy=build_legacy_chunk_event_data(
                    chunk,
                    include_tool_output=not bool(chunk.tool_output) or chunk.suppress_public_tool_output,
                ),
                subtask_id=subtask_id,
                tool_call_id=chunk.tool_call_id,
            )
        )
        return events

    def _build_chunk_payload(self, chunk: StreamChunk, event_type: str) -> dict[str, Any]:
        if event_type in {"thinking.delta", "thinking.completed", "text.delta", "text.completed"}:
            return {"content": chunk.content}

        if event_type == "subtask.started":
            return self._compact(
                {
                    "name": chunk.subagent_name,
                    "type": chunk.subagent_type,
                    "parentSubtaskId": chunk.parent_subtask_id or self.root_subtask_id,
                    "origin": chunk.origin or "tool",
                }
            )

        if event_type == "subtask.completed":
            return self._compact(
                {
                    "success": chunk.success,
                    "error": chunk.error,
                    "summary": chunk.content,
                    "parentSubtaskId": chunk.parent_subtask_id or self.root_subtask_id,
                    "origin": chunk.origin or "tool",
                }
            )

        if event_type == "tool.started":
            return self._compact(
                {
                    "toolName": chunk.tool_name,
                    "toolDescription": chunk.tool_description,
                    "params": chunk.tool_params or {},
                    "origin": chunk.origin or ("tool" if chunk.subagent_id else "root"),
                }
            )

        return {}

    def _build_event(
        self,
        event_type: str,
        payload: dict[str, Any],
        *,
        legacy: dict[str, Any] | None = None,
        subtask_id: str | None = None,
        tool_call_id: str | None = None,
    ) -> dict[str, Any]:
        event = {
            "eventId": str(uuid.uuid4()),
            "sessionId": self.session_id,
            "turnId": self.turn_id,
            "eventType": event_type,
            "timestamp": self._utcnow_iso(),
            "payload": payload,
        }
        if subtask_id:
            event["subtaskId"] = subtask_id
        if tool_call_id:
            event["toolCallId"] = tool_call_id
        if legacy:
            event.update(legacy)
        return event

    def _build_turn_completed(self) -> dict[str, Any]:
        return self._build_event("turn.completed", {"stopReason": "completed"})

    def _build_turn_failed(self, *, stop_reason: str, error: dict[str, Any] | None = None) -> dict[str, Any]:
        payload = {"stopReason": stop_reason}
        if error is not None:
            payload["error"] = error
        return self._build_event("turn.failed", payload)

    def _build_recorded_runtime_failure(self) -> dict[str, Any] | None:
        if self._api_error_code == "authentication_failed":
            return self._build_turn_failed(
                stop_reason="runtime_error",
                error=self._build_error(
                    code="AUTH_EXPIRED",
                    layer="session",
                    message=self._api_error_message or "Authentication expired.",
                    retryable=False,
                ),
            )

        if self._api_error_code:
            return self._build_turn_failed(
                stop_reason="runtime_error",
                error=self._build_error(
                    code="PROVIDER_API_ERROR",
                    layer="turn",
                    message=self._api_error_message or "Provider API error.",
                    retryable=False,
                ),
            )

        if self._sdk_error_subtype:
            return self._build_turn_failed(
                stop_reason="runtime_error",
                error=self._build_error(
                    code="PROVIDER_SDK_ERROR",
                    layer="turn",
                    message=self._sdk_error_message or "Provider SDK error.",
                    retryable=False,
                ),
            )

        return None

    def _record_chunk_state(self, chunk: StreamChunk) -> None:
        if chunk.type == "tool_call_complete" and chunk.error_type == "blocking":
            self._blocking_tool_failure = {
                "toolCallId": chunk.tool_call_id,
                "message": chunk.error or "Tool execution failed.",
            }

        if chunk.error_type == "sdk_error":
            self._sdk_error_subtype = chunk.error_content
            self._sdk_error_message = (chunk.content or "").strip() or "Provider SDK error."

        if chunk.error_type == "api_error":
            self._api_error_code = chunk.error_content
            self._api_error_message = (chunk.content or "").strip() or "Provider API error."

    def _resolve_subtask_id(self, chunk: StreamChunk) -> str | None:
        if chunk.type.startswith("tool_call_"):
            return chunk.subagent_id or self.root_subtask_id
        if chunk.type.startswith("subagent_"):
            return chunk.subagent_id
        return None

    @staticmethod
    def _resolve_tool_output(chunk: StreamChunk) -> str | None:
        if chunk.tool_output:
            return chunk.tool_output
        if chunk.success is False and chunk.error:
            return chunk.error
        return None

    @staticmethod
    def _build_error(
        *,
        code: str,
        layer: str,
        message: str,
        retryable: bool,
        related_tool_call_id: str | None = None,
        related_interaction_id: str | None = None,
    ) -> dict[str, Any]:
        error = {
            "code": code,
            "layer": layer,
            "message": message,
            "retryable": retryable,
        }
        if related_tool_call_id:
            error["relatedToolCallId"] = related_tool_call_id
        if related_interaction_id:
            error["relatedInteractionId"] = related_interaction_id
        return error

    @staticmethod
    def _compact(value: dict[str, Any]) -> dict[str, Any]:
        return {key: item for key, item in value.items() if item is not None}

    @staticmethod
    def _is_interrupted_exception(exc: Exception) -> bool:
        name = exc.__class__.__name__.lower()
        message = str(exc).lower()
        return name in {"interruptederror"} or "interrupted" in message

    @staticmethod
    def _utcnow_iso() -> str:
        return datetime.now(timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z")
