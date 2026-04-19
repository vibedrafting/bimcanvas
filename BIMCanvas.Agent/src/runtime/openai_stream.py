"""OpenAI Agents stream-event translation into runtime-neutral chunks."""

from __future__ import annotations

import json
from typing import Any

from .chunks import StreamChunk

SUBTASK_ERROR_MARKER = "__bimcanvas_subtask_error__:"

_AGENT_TOOL_SUBTASK_TYPE_MAP = {
    "delegate_query_task": "query-worker",
    "delegate_edit_task": "edit-worker",
}


def _get_attr(value: Any, name: str, default: Any = None) -> Any:
    if isinstance(value, dict):
        return value.get(name, default)
    return getattr(value, name, default)


def _extract_text_parts(content: Any) -> list[str]:
    if content is None:
        return []
    if isinstance(content, str):
        return [content]
    if isinstance(content, list):
        parts: list[str] = []
        for item in content:
            item_type = _get_attr(item, "type")
            if item_type in {"output_text", "summary_text", "reasoning_text", "text"}:
                text = _get_attr(item, "text") or _get_attr(item, "content")
                if isinstance(text, str) and text:
                    parts.append(text)
            else:
                parts.extend(_extract_text_parts(_get_attr(item, "content")))
        return parts
    return []


def _extract_message_text(item: Any) -> str:
    raw_item = _get_attr(item, "raw_item", item)
    return "".join(_extract_text_parts(_get_attr(raw_item, "content"))).strip()


def _extract_reasoning_text(item: Any) -> str:
    raw_item = _get_attr(item, "raw_item", item)
    summary = _get_attr(raw_item, "summary")
    if isinstance(summary, list):
        texts: list[str] = []
        for part in summary:
            text = _get_attr(part, "text")
            if isinstance(text, str) and text:
                texts.append(text)
        return "\n".join(texts).strip()
    return "".join(_extract_text_parts(_get_attr(raw_item, "content"))).strip()


def _serialize_tool_output(output: Any) -> str:
    if output is None:
        return ""
    if isinstance(output, str):
        return output
    try:
        return json.dumps(output, ensure_ascii=False)
    except (TypeError, ValueError):
        return str(output)


def _extract_tool_output(item: Any) -> Any:
    output = _get_attr(item, "output")
    if output not in (None, ""):
        return output

    raw_item = _get_attr(item, "raw_item", item)
    raw_output = _get_attr(raw_item, "output")
    if raw_output not in (None, ""):
        return raw_output

    return output


def _resolve_call_id(item: Any) -> str | None:
    raw_item = _get_attr(item, "raw_item", item)
    return _get_attr(raw_item, "call_id") or _get_attr(raw_item, "id")


def _resolve_tool_name(item: Any) -> str | None:
    return _get_attr(item, "tool_name") or _get_attr(_get_attr(item, "raw_item", item), "name")


def _resolve_tool_arguments(item: Any) -> dict[str, Any]:
    raw_item = _get_attr(item, "raw_item", item)
    arguments = _get_attr(raw_item, "arguments")
    if isinstance(arguments, str):
        try:
            parsed = json.loads(arguments)
            return parsed if isinstance(parsed, dict) else {"arguments": parsed}
        except json.JSONDecodeError:
            return {"arguments": arguments}
    if isinstance(arguments, dict):
        return arguments
    return {}


def _resolve_tool_origin_type(item: Any) -> str | None:
    origin = _get_attr(item, "tool_origin")
    if origin is None:
        return None
    origin_type = _get_attr(origin, "type")
    if isinstance(origin_type, str):
        return origin_type
    value = _get_attr(origin_type, "value")
    return value if isinstance(value, str) else None


def _resolve_agent_as_tool_metadata(item: Any) -> tuple[str | None, str | None]:
    tool_name = _resolve_tool_name(item)
    arguments = _resolve_tool_arguments(item)
    task_title = arguments.get("task_title")
    if not isinstance(task_title, str) or not task_title.strip():
        task_title = tool_name or "SubAgent"
    return task_title.strip(), _AGENT_TOOL_SUBTASK_TYPE_MAP.get(tool_name or "", "agent_as_tool")


def _parse_agent_as_tool_output(output_text: str) -> tuple[bool, str | None, str | None]:
    normalized = (output_text or "").strip()
    if not normalized.startswith(SUBTASK_ERROR_MARKER):
        return True, normalized or None, None

    payload = normalized[len(SUBTASK_ERROR_MARKER):].strip()
    try:
        parsed = json.loads(payload) if payload else {}
    except json.JSONDecodeError:
        parsed = {}

    error = parsed.get("error") if isinstance(parsed, dict) else None
    if not isinstance(error, str) or not error.strip():
        error = normalized
    return False, None, error.strip()


class OpenAIStreamTranslator:
    """Translate OpenAI stream events into the shared StreamChunk shape."""

    def __init__(
        self,
        *,
        turn_id: str,
        projection_state: dict[str, Any] | None = None,
        current_subtask_id: str | None = None,
    ) -> None:
        self.turn_id = turn_id
        snapshot = projection_state or {}
        self._tool_calls_by_provider: dict[str, str] = dict(snapshot.get("toolCallsByProvider", {}))
        self._subtasks_by_provider: dict[str, str] = dict(snapshot.get("subtasksByProvider", {}))
        self._started_subtasks_by_provider: set[str] = set(snapshot.get("startedSubtasksByProvider", []))
        self._subtask_stack: list[str] = list(snapshot.get("subtaskStack", []))
        self._subtask_messages_by_id: dict[str, str] = dict(snapshot.get("subtaskMessagesById", {}))
        self._active_tool_calls_by_subtask: dict[str, list[str]] = {
            subtask_id: [
                tool_call_id
                for tool_call_id in tool_call_ids
                if isinstance(tool_call_id, str) and tool_call_id
            ]
            for subtask_id, tool_call_ids in dict(snapshot.get("activeToolCallsBySubtask", {})).items()
            if isinstance(subtask_id, str) and isinstance(tool_call_ids, list)
        }
        self._tool_counter: int = int(snapshot.get("toolCounter", 0))
        self._current_subtask_id = current_subtask_id

    def snapshot(self) -> dict[str, Any]:
        return {
            "toolCallsByProvider": dict(self._tool_calls_by_provider),
            "subtasksByProvider": dict(self._subtasks_by_provider),
            "startedSubtasksByProvider": sorted(self._started_subtasks_by_provider),
            "subtaskStack": list(self._subtask_stack),
            "subtaskMessagesById": dict(self._subtask_messages_by_id),
            "activeToolCallsBySubtask": {
                subtask_id: list(tool_call_ids)
                for subtask_id, tool_call_ids in self._active_tool_calls_by_subtask.items()
            },
            "toolCounter": self._tool_counter,
        }

    def translate(self, event: Any, *, forced_subtask_id: str | None = None) -> list[StreamChunk]:
        event_type = _get_attr(event, "type")
        if event_type == "raw_response_event":
            return self._translate_raw_event(_get_attr(event, "data"), forced_subtask_id=forced_subtask_id)
        if event_type == "run_item_stream_event":
            return self._translate_run_item_event(
                _get_attr(event, "name"),
                _get_attr(event, "item"),
                forced_subtask_id=forced_subtask_id,
            )
        return []

    def translate_result_item(
        self,
        item: Any,
        *,
        forced_subtask_id: str | None = None,
    ) -> list[StreamChunk]:
        item_type = _get_attr(item, "type")

        if item_type == "message_output_item":
            content = _extract_message_text(item)
            if content:
                self._remember_subtask_message(forced_subtask_id, content)
                return [StreamChunk(type="text_complete", content=content, subagent_id=forced_subtask_id)]
            return []

        if item_type == "reasoning_item":
            content = _extract_reasoning_text(item)
            if content:
                return [StreamChunk(type="thinking_complete", content=content, subagent_id=forced_subtask_id)]
            return []

        if item_type == "tool_call_item":
            return self._translate_tool_called(item, forced_subtask_id=forced_subtask_id)

        if item_type == "tool_call_output_item":
            return self._translate_tool_output(item, forced_subtask_id=forced_subtask_id)

        return []

    def _translate_raw_event(self, raw_event: Any, *, forced_subtask_id: str | None = None) -> list[StreamChunk]:
        raw_type = _get_attr(raw_event, "type")
        if raw_type == "response.output_text.delta":
            delta = _get_attr(raw_event, "delta")
            if isinstance(delta, str) and delta:
                return [StreamChunk(type="text", content=delta, subagent_id=forced_subtask_id)]
        if raw_type in {"response.reasoning_text.delta", "response.reasoning_summary_text.delta"}:
            delta = _get_attr(raw_event, "delta")
            if isinstance(delta, str) and delta:
                return [StreamChunk(type="thinking", content=delta, subagent_id=forced_subtask_id)]
        return []

    def _translate_run_item_event(
        self,
        name: str | None,
        item: Any,
        *,
        forced_subtask_id: str | None = None,
    ) -> list[StreamChunk]:
        if name == "message_output_created":
            content = _extract_message_text(item)
            if content:
                self._remember_subtask_message(forced_subtask_id, content)
                return [StreamChunk(type="text_complete", content=content, subagent_id=forced_subtask_id)]
            return []

        if name == "reasoning_item_created":
            content = _extract_reasoning_text(item)
            if content:
                return [StreamChunk(type="thinking_complete", content=content, subagent_id=forced_subtask_id)]
            return []

        if name == "tool_called":
            return self._translate_tool_called(item, forced_subtask_id=forced_subtask_id)

        if name == "tool_output":
            return self._translate_tool_output(item, forced_subtask_id=forced_subtask_id)

        return []

    def _translate_tool_called(self, item: Any, *, forced_subtask_id: str | None = None) -> list[StreamChunk]:
        provider_call_id = _resolve_call_id(item)
        if not provider_call_id:
            return []

        public_tool_call_id = self._ensure_public_tool_call_id(provider_call_id)
        tool_origin_type = _resolve_tool_origin_type(item)
        resolved_subtask_id = forced_subtask_id or self._active_subtask_id()

        if tool_origin_type == "agent_as_tool":
            return self._ensure_agent_as_tool_started(item, provider_call_id=provider_call_id)

        self._track_active_tool_call(resolved_subtask_id, public_tool_call_id)
        return [
            StreamChunk(
                type="tool_call_start",
                subagent_id=resolved_subtask_id,
                tool_call_id=public_tool_call_id,
                tool_name=_resolve_tool_name(item),
                tool_params=_resolve_tool_arguments(item),
                origin="tool" if resolved_subtask_id else "root",
            )
        ]

    def _translate_tool_output(self, item: Any, *, forced_subtask_id: str | None = None) -> list[StreamChunk]:
        provider_call_id = _resolve_call_id(item)
        if not provider_call_id:
            return []

        public_tool_call_id = self._ensure_public_tool_call_id(provider_call_id)
        tool_origin_type = _resolve_tool_origin_type(item)
        output_text = _serialize_tool_output(_extract_tool_output(item))
        resolved_subtask_id = forced_subtask_id or self._active_subtask_id()

        if tool_origin_type == "agent_as_tool":
            subtask_id = self._ensure_subtask_id(provider_call_id, public_tool_call_id)
            if self._subtask_stack and self._subtask_stack[-1] == subtask_id:
                self._subtask_stack.pop()
            success, summary, error = _parse_agent_as_tool_output(output_text)
            summary = summary or self._consume_subtask_message(subtask_id)
            completion_chunks = self._build_synthetic_tool_completions(subtask_id)
            completion_chunks.append(
                StreamChunk(
                    type="subagent_complete",
                    subagent_id=subtask_id,
                    content=summary or "",
                    success=success,
                    error=error,
                    parent_subtask_id=forced_subtask_id or self._active_subtask_id(),
                    origin="tool",
                )
            )
            return completion_chunks

        self._complete_active_tool_call(resolved_subtask_id, public_tool_call_id)

        return [
            StreamChunk(
                type="tool_call_complete",
                subagent_id=resolved_subtask_id,
                tool_call_id=public_tool_call_id,
                tool_output=output_text or None,
                success=True,
            )
        ]

    def _ensure_public_tool_call_id(self, provider_call_id: str) -> str:
        existing = self._tool_calls_by_provider.get(provider_call_id)
        if existing is not None:
            return existing
        self._tool_counter += 1
        tool_call_id = f"tc-{self._tool_counter}"
        self._tool_calls_by_provider[provider_call_id] = tool_call_id
        return tool_call_id

    def _ensure_subtask_id(self, provider_call_id: str, public_tool_call_id: str) -> str:
        existing = self._subtasks_by_provider.get(provider_call_id)
        if existing is not None:
            return existing
        subtask_id = f"st-{public_tool_call_id}"
        self._subtasks_by_provider[provider_call_id] = subtask_id
        return subtask_id

    def get_public_tool_call_id(self, provider_call_id: str | None) -> str | None:
        if not provider_call_id:
            return None
        return self._tool_calls_by_provider.get(provider_call_id)

    def ensure_public_tool_call_id(self, provider_call_id: str | None) -> str | None:
        if not provider_call_id:
            return None
        return self._ensure_public_tool_call_id(provider_call_id)

    def ensure_subtask_id_for_provider_call(self, provider_call_id: str | None) -> str | None:
        if not provider_call_id:
            return None
        public_tool_call_id = self._ensure_public_tool_call_id(provider_call_id)
        return self._ensure_subtask_id(provider_call_id, public_tool_call_id)

    def ensure_subtask_started_for_tool_call(self, tool_call: Any) -> tuple[list[StreamChunk], str | None]:
        provider_call_id = _resolve_call_id(tool_call)
        if not provider_call_id:
            return [], None
        chunks = self._ensure_agent_as_tool_started(tool_call, provider_call_id=provider_call_id)
        return chunks, self._subtasks_by_provider.get(provider_call_id)

    def _ensure_agent_as_tool_started(
        self,
        item: Any,
        *,
        provider_call_id: str,
    ) -> list[StreamChunk]:
        public_tool_call_id = self._ensure_public_tool_call_id(provider_call_id)
        subtask_id = self._ensure_subtask_id(provider_call_id, public_tool_call_id)
        if provider_call_id in self._started_subtasks_by_provider:
            return []

        self._started_subtasks_by_provider.add(provider_call_id)
        if subtask_id not in self._subtask_stack:
            self._subtask_stack.append(subtask_id)

        subtask_name, subtask_type = _resolve_agent_as_tool_metadata(item)
        return [
            StreamChunk(
                type="subagent_start",
                subagent_id=subtask_id,
                subagent_name=subtask_name,
                subagent_type=subtask_type,
                parent_subtask_id=self._active_parent_subtask_id(subtask_id),
                origin="tool",
            )
        ]

    def _active_subtask_id(self) -> str | None:
        if self._current_subtask_id:
            return self._current_subtask_id
        if self._subtask_stack:
            return self._subtask_stack[-1]
        return None

    def _active_parent_subtask_id(self, subtask_id: str) -> str | None:
        if self._current_subtask_id and self._current_subtask_id != subtask_id:
            return self._current_subtask_id
        if len(self._subtask_stack) >= 2 and self._subtask_stack[-1] == subtask_id:
            return self._subtask_stack[-2]
        if self._subtask_stack and self._subtask_stack[-1] != subtask_id:
            return self._subtask_stack[-1]
        return None

    def _remember_subtask_message(self, subtask_id: str | None, content: str) -> None:
        normalized_subtask_id = (subtask_id or "").strip()
        if not normalized_subtask_id:
            return
        normalized_content = content.strip()
        if normalized_content:
            self._subtask_messages_by_id[normalized_subtask_id] = normalized_content

    def _consume_subtask_message(self, subtask_id: str | None) -> str | None:
        normalized_subtask_id = (subtask_id or "").strip()
        if not normalized_subtask_id:
            return None
        message = self._subtask_messages_by_id.pop(normalized_subtask_id, None)
        if isinstance(message, str):
            message = message.strip()
        return message or None

    def _track_active_tool_call(self, subtask_id: str | None, tool_call_id: str) -> None:
        normalized_subtask_id = (subtask_id or "").strip()
        if not normalized_subtask_id:
            return
        active_tool_calls = self._active_tool_calls_by_subtask.setdefault(normalized_subtask_id, [])
        if tool_call_id not in active_tool_calls:
            active_tool_calls.append(tool_call_id)

    def _complete_active_tool_call(self, subtask_id: str | None, tool_call_id: str | None) -> None:
        normalized_subtask_id = (subtask_id or "").strip()
        normalized_tool_call_id = (tool_call_id or "").strip()
        if not normalized_subtask_id or not normalized_tool_call_id:
            return
        active_tool_calls = self._active_tool_calls_by_subtask.get(normalized_subtask_id)
        if not active_tool_calls:
            return
        try:
            active_tool_calls.remove(normalized_tool_call_id)
        except ValueError:
            return
        if not active_tool_calls:
            self._active_tool_calls_by_subtask.pop(normalized_subtask_id, None)

    def _build_synthetic_tool_completions(self, subtask_id: str | None) -> list[StreamChunk]:
        normalized_subtask_id = (subtask_id or "").strip()
        if not normalized_subtask_id:
            return []
        active_tool_calls = self._active_tool_calls_by_subtask.pop(normalized_subtask_id, [])
        return [
            StreamChunk(
                type="tool_call_complete",
                subagent_id=normalized_subtask_id,
                tool_call_id=tool_call_id,
                success=True,
            )
            for tool_call_id in active_tool_calls
        ]
