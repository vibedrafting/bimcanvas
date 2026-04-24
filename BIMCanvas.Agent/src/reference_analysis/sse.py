"""SSE 基础设施：从 ChatGPT 后端 codex/responses 端点读取流式事件。

从 test/openai_stream_image_saver.py 平移，去掉硬编码 URL/headers，改为参数。
"""

from __future__ import annotations

import json
from typing import Any, Iterable, Iterator
from urllib import error, request


TERMINAL_RESPONSE_EVENTS = {"response.completed", "response.done"}


class StreamClientError(RuntimeError):
    """SSE 流请求失败。"""


class StreamOutputCollector:
    """收集 SSE 中的输出项，必要时回填到最终 response.output。"""

    def __init__(self) -> None:
        self._output_items: dict[str, dict[str, Any]] = {}
        self._output_order: list[str] = []
        self._text_segments: dict[str, str] = {}
        self._text_order: list[str] = []
        self._fallback_item_id: str | None = None

    def consume(self, event_name: str, payload: dict[str, Any]) -> None:
        if event_name == "response.output_item.added":
            item = payload.get("item")
            if isinstance(item, dict):
                self._remember_output_item(payload, item)
            return

        if event_name == "response.content_part.added":
            part = payload.get("part")
            if isinstance(part, dict):
                self._remember_content_part(payload, part)
            return

        if event_name == "response.output_item.done":
            item = payload.get("item")
            if isinstance(item, dict):
                self._remember_output_item(payload, item)
            return

        if event_name == "response.output_text.delta":
            key = self._build_text_key(payload)
            delta = payload.get("delta")
            if isinstance(delta, str) and delta:
                self._append_part_text(payload, delta)
                self._append_text(key, delta)
            return

        if event_name == "response.output_text.done":
            key = self._build_text_key(payload)
            text = payload.get("text")
            if isinstance(text, str):
                self._set_part_text(payload, text)
                self._set_text(key, text)
            return

        if event_name == "response.content_part.done":
            part = payload.get("part")
            if not isinstance(part, dict):
                return
            self._remember_content_part(payload, part)
            text = part.get("text")
            if part.get("type") in {"output_text", "text"} and isinstance(text, str):
                key = self._build_text_key(payload)
                self._set_text(key, text)

    def build_output(self) -> list[dict[str, Any]]:
        output = [self._output_items[key] for key in self._output_order if key in self._output_items]

        fallback_message = self._build_fallback_message()
        if fallback_message is not None and not self._has_message_item(output):
            output.append(fallback_message)

        return output

    def _remember_output_item(self, payload: dict[str, Any], item: dict[str, Any]) -> None:
        key = self._build_output_key(payload, item)
        if key not in self._output_items:
            self._output_order.append(key)
        existing = self._output_items.get(key)
        if isinstance(existing, dict) and existing.get("content") and not item.get("content"):
            merged_item = dict(item)
            merged_item["content"] = existing["content"]
            self._output_items[key] = merged_item
        else:
            self._output_items[key] = item

        item_id = item.get("id")
        if isinstance(item_id, str) and item_id and self._fallback_item_id is None:
            self._fallback_item_id = item_id

    def _remember_content_part(self, payload: dict[str, Any], part: dict[str, Any]) -> None:
        item = self._ensure_message_item(payload)
        content = item.setdefault("content", [])
        if not isinstance(content, list):
            content = []
            item["content"] = content

        content_index = payload.get("content_index")
        if not isinstance(content_index, int) or content_index < 0:
            content_index = len(content)

        while len(content) <= content_index:
            content.append(
                {
                    "type": "output_text",
                    "annotations": [],
                    "logprobs": [],
                    "text": "",
                }
            )

        content[content_index] = part

    def _append_part_text(self, payload: dict[str, Any], delta: str) -> None:
        part = self._ensure_output_text_part(payload)
        current = part.get("text")
        part["text"] = (current if isinstance(current, str) else "") + delta

    def _set_part_text(self, payload: dict[str, Any], text: str) -> None:
        part = self._ensure_output_text_part(payload)
        part["text"] = text

    def _append_text(self, key: str, delta: str) -> None:
        self._touch_text_key(key)
        self._text_segments[key] = self._text_segments.get(key, "") + delta

    def _set_text(self, key: str, text: str) -> None:
        self._touch_text_key(key)
        self._text_segments[key] = text

    def _touch_text_key(self, key: str) -> None:
        if key not in self._text_segments:
            self._text_order.append(key)
            self._text_segments[key] = ""

        if self._fallback_item_id is None:
            item_id = key.split(":", 1)[0]
            if item_id and item_id != "message":
                self._fallback_item_id = item_id

    def _build_output_key(self, payload: dict[str, Any], item: dict[str, Any]) -> str:
        item_id = item.get("id")
        if isinstance(item_id, str) and item_id:
            return item_id

        output_index = payload.get("output_index")
        if output_index is not None:
            return f"output:{output_index}"

        return f"output:{len(self._output_order)}"

    def _build_text_key(self, payload: dict[str, Any]) -> str:
        item_id = str(payload.get("item_id") or "message")
        output_index = payload.get("output_index")
        content_index = payload.get("content_index")

        parts = [item_id]
        if output_index is not None:
            parts.append(str(output_index))
        if content_index is not None:
            parts.append(str(content_index))
        return ":".join(parts)

    def _build_fallback_message(self) -> dict[str, Any] | None:
        text = "".join(
            self._text_segments[key]
            for key in self._text_order
            if self._text_segments.get(key)
        )
        if not text:
            return None

        message_item: dict[str, Any] = {
            "type": "message",
            "role": "assistant",
            "status": "completed",
            "content": [
                {
                    "type": "output_text",
                    "text": text,
                }
            ],
        }
        if self._fallback_item_id:
            message_item["id"] = self._fallback_item_id
        return message_item

    def _has_message_item(self, output: list[dict[str, Any]]) -> bool:
        for item in output:
            if not isinstance(item, dict):
                continue
            if item.get("type") != "message":
                continue
            content = item.get("content")
            if isinstance(content, list) and content:
                return True
        return False

    def _ensure_message_item(self, payload: dict[str, Any]) -> dict[str, Any]:
        item_id = str(payload.get("item_id") or f"message:{payload.get('output_index', 0)}")
        item = self._output_items.get(item_id)
        if isinstance(item, dict):
            return item

        item = {
            "id": item_id,
            "type": "message",
            "status": "in_progress",
            "content": [],
            "role": "assistant",
        }
        self._remember_output_item(payload, item)
        return item

    def _ensure_output_text_part(self, payload: dict[str, Any]) -> dict[str, Any]:
        item = self._ensure_message_item(payload)
        content = item.setdefault("content", [])
        if not isinstance(content, list):
            content = []
            item["content"] = content

        content_index = payload.get("content_index")
        if not isinstance(content_index, int) or content_index < 0:
            content_index = 0

        while len(content) <= content_index:
            content.append(
                {
                    "type": "output_text",
                    "annotations": [],
                    "logprobs": [],
                    "text": "",
                }
            )

        part = content[content_index]
        if not isinstance(part, dict):
            part = {
                "type": "output_text",
                "annotations": [],
                "logprobs": [],
                "text": "",
            }
            content[content_index] = part

        part.setdefault("type", "output_text")
        part.setdefault("annotations", [])
        part.setdefault("logprobs", [])
        part.setdefault("text", "")
        return part


def iter_sse_messages(lines: Iterable[str]) -> Iterator[tuple[str, str]]:
    event_name: str | None = None
    data_lines: list[str] = []

    for raw_line in lines:
        line = raw_line.rstrip("\r\n")
        if not line:
            if event_name is not None or data_lines:
                yield event_name or "message", "\n".join(data_lines)
            event_name = None
            data_lines = []
            continue

        if line.startswith(":"):
            continue

        if line.startswith("event:"):
            event_name = line[len("event:"):].strip()
            continue

        if line.startswith("data:"):
            data_lines.append(line[len("data:"):].lstrip())

    if event_name is not None or data_lines:
        yield event_name or "message", "\n".join(data_lines)


def iter_sse_from_http(
    payload: dict[str, Any],
    *,
    url: str,
    headers: dict[str, str],
    timeout_seconds: int,
) -> Iterator[tuple[str, str]]:
    body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
    req = request.Request(
        url=url,
        data=body,
        headers=headers,
        method="POST",
    )

    try:
        with request.urlopen(req, timeout=timeout_seconds) as response:
            encoding = response.headers.get_content_charset() or "utf-8"
            decoded_lines = (
                raw_line.decode(encoding, errors="replace")
                for raw_line in response
            )
            yield from iter_sse_messages(decoded_lines)
    except error.HTTPError as exc:
        error_body = exc.read().decode("utf-8", errors="replace")
        raise StreamClientError(
            f"HTTP {exc.code}: {error_body}",
        ) from exc
    except error.URLError as exc:
        raise StreamClientError(f"请求失败: {exc}") from exc


def looks_like_response_object(node: Any) -> bool:
    return (
        isinstance(node, dict)
        and isinstance(node.get("id"), str)
        and bool(node.get("id"))
        and any(key in node for key in ("status", "output", "error", "object"))
    )


def is_complete_response_object(node: Any) -> bool:
    if not looks_like_response_object(node):
        return False

    status = node.get("status")
    if isinstance(status, str) and status.strip().lower() == "in_progress":
        return False

    return any(key in node for key in ("output", "error", "incomplete_details", "usage", "status"))


def extract_response_candidate(payload: dict[str, Any]) -> dict[str, Any] | None:
    candidates: list[dict[str, Any]] = []

    response_node = payload.get("response")
    if isinstance(response_node, dict):
        candidates.append(response_node)

    data_node = payload.get("data")
    if isinstance(data_node, dict):
        candidates.append(data_node)

    if looks_like_response_object(payload):
        candidates.append(payload)

    for candidate in candidates:
        if is_complete_response_object(candidate):
            return candidate

    return None


def is_terminal_response_event(event_name: str, payload: dict[str, Any]) -> bool:
    payload_type = payload.get("type")
    if isinstance(payload_type, str) and payload_type in TERMINAL_RESPONSE_EVENTS:
        return True
    return event_name in TERMINAL_RESPONSE_EVENTS


def hydrate_response_output(
    response: dict[str, Any],
    output_collector: StreamOutputCollector,
) -> dict[str, Any]:
    output = response.get("output")
    if isinstance(output, list) and output:
        return response

    hydrated_output = output_collector.build_output()
    if not hydrated_output:
        return response

    response["output"] = hydrated_output
    return response


def collect_final_response(stream: Iterable[tuple[str, str]]) -> dict[str, Any]:
    completed_response: dict[str, Any] | None = None
    latest_complete_response: dict[str, Any] | None = None
    output_collector = StreamOutputCollector()

    for event_name, data_text in stream:
        if data_text == "[DONE]":
            break

        if not data_text:
            continue

        try:
            payload = json.loads(data_text)
        except json.JSONDecodeError:
            continue

        if not isinstance(payload, dict):
            continue

        output_collector.consume(event_name, payload)

        candidate = extract_response_candidate(payload)
        if candidate is None:
            continue

        latest_complete_response = candidate
        if is_terminal_response_event(event_name, payload):
            completed_response = candidate

    if completed_response is not None:
        return hydrate_response_output(completed_response, output_collector)
    if latest_complete_response is not None:
        return hydrate_response_output(latest_complete_response, output_collector)

    raise StreamClientError("SSE 结束后未找到完整 response JSON")
