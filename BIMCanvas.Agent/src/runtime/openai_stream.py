"""OpenAI Agents stream-event translation into runtime-neutral chunks."""

from __future__ import annotations

import json
from typing import Any

from .chunks import StreamChunk


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
        self._subtask_stack: list[str] = list(snapshot.get("subtaskStack", []))
        self._tool_counter: int = int(snapshot.get("toolCounter", 0))
        self._current_subtask_id = current_subtask_id

    def snapshot(self) -> dict[str, Any]:
        return {
            "toolCallsByProvider": dict(self._tool_calls_by_provider),
            "subtasksByProvider": dict(self._subtasks_by_provider),
            "subtaskStack": list(self._subtask_stack),
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
        tool_name = _resolve_tool_name(item)
        tool_origin_type = _resolve_tool_origin_type(item)

        if tool_origin_type == "agent_as_tool":
            parent_subtask_id = forced_subtask_id or self._active_subtask_id()
            subtask_id = self._ensure_subtask_id(provider_call_id, public_tool_call_id)
            self._subtask_stack.append(subtask_id)
            return [
                StreamChunk(
                    type="subagent_start",
                    subagent_id=subtask_id,
                    subagent_name=tool_name or "SubAgent",
                    subagent_type="agent_as_tool",
                    parent_subtask_id=parent_subtask_id,
                    origin="tool",
                )
            ]

        return [
            StreamChunk(
                type="tool_call_start",
                subagent_id=forced_subtask_id or self._active_subtask_id(),
                tool_call_id=public_tool_call_id,
                tool_name=tool_name,
                tool_params=_resolve_tool_arguments(item),
                origin="tool" if (forced_subtask_id or self._active_subtask_id()) else "root",
            )
        ]

    def _translate_tool_output(self, item: Any, *, forced_subtask_id: str | None = None) -> list[StreamChunk]:
        provider_call_id = _resolve_call_id(item)
        if not provider_call_id:
            return []

        public_tool_call_id = self._ensure_public_tool_call_id(provider_call_id)
        tool_origin_type = _resolve_tool_origin_type(item)
        output_text = _serialize_tool_output(_get_attr(item, "output"))

        if tool_origin_type == "agent_as_tool":
            subtask_id = self._ensure_subtask_id(provider_call_id, public_tool_call_id)
            if self._subtask_stack and self._subtask_stack[-1] == subtask_id:
                self._subtask_stack.pop()
            return [
                StreamChunk(
                    type="subagent_complete",
                    subagent_id=subtask_id,
                    content=output_text,
                    success=True,
                    parent_subtask_id=forced_subtask_id or self._active_subtask_id(),
                    origin="tool",
                )
            ]

        return [
            StreamChunk(
                type="tool_call_complete",
                subagent_id=forced_subtask_id or self._active_subtask_id(),
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

    def _active_subtask_id(self) -> str | None:
        if self._current_subtask_id:
            return self._current_subtask_id
        if self._subtask_stack:
            return self._subtask_stack[-1]
        return None
