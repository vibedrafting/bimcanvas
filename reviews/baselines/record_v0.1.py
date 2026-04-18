"""Record v0.1 baseline traces through real aiohttp HTTP/SSE endpoints."""

from __future__ import annotations

import asyncio
import json
import sys
from datetime import datetime, timezone
from itertools import count
from pathlib import Path
from typing import Any, Awaitable, Callable

from aiohttp import ClientResponse
from aiohttp.test_utils import TestClient, TestServer


REPO_ROOT = Path(__file__).resolve().parents[2]
AGENT_ROOT = REPO_ROOT / "BIMCanvas.Agent"
if str(AGENT_ROOT) not in sys.path:
    sys.path.insert(0, str(AGENT_ROOT))

from src.agent.main_agent import StreamChunk
from src.runtime import RuntimeStateStore
from src.server import http_server


WINDOW_ID = "primary"
PROJECT_PATH = "C:/demo"
MODEL_ID = "sonnet"


class _FakeAgent:
    def __init__(
        self,
        *,
        stream_factory: Callable[[dict[str, str]], Awaitable[list[StreamChunk]] | Any],
    ) -> None:
        self._connected = True
        self._stream_factory = stream_factory
        self.runtime_contexts: list[dict[str, str] | None] = []

    def get_current_model(self) -> str:
        return MODEL_ID

    async def connect(self, **_: Any) -> None:
        self._connected = True

    async def set_model(self, model: str) -> None:
        self._connected = True

    def clear_runtime_context(self) -> None:
        return None

    async def chat_stream(self, *args: Any, **kwargs: Any):
        runtime_context = kwargs.get("runtime_context")
        self.runtime_contexts.append(runtime_context)
        async for chunk in self._stream_factory(runtime_context):
            yield chunk


class SSERecorder:
    def __init__(
        self,
        *,
        stream_name: str,
        response: ClientResponse,
        timeline: list[dict[str, Any]],
        sequence_counter: count,
    ) -> None:
        self.stream_name = stream_name
        self.response = response
        self.timeline = timeline
        self.sequence_counter = sequence_counter
        self.items: list[dict[str, Any]] = []
        self._queue: asyncio.Queue[dict[str, Any]] = asyncio.Queue()
        self._task = asyncio.create_task(self._consume())

    async def _consume(self) -> None:
        event_name: str | None = None
        data_lines: list[str] = []
        try:
            while True:
                raw_line = await self.response.content.readline()
                if raw_line == b"":
                    break

                line = raw_line.decode("utf-8").rstrip("\r\n")
                if line.startswith("event: "):
                    event_name = line[7:]
                    continue
                if line.startswith("data: "):
                    data_lines.append(line[6:])
                    continue
                if line != "":
                    continue
                if not data_lines:
                    event_name = None
                    continue

                item = self._build_item(event_name, "\n".join(data_lines))
                self.items.append(item)
                self._queue.put_nowait(item)
                self._append_timeline(item)
                event_name = None
                data_lines = []
        except asyncio.CancelledError:
            raise

    def _build_item(self, event_name: str | None, data_text: str) -> dict[str, Any]:
        if data_text == "[DONE]":
            parsed_data: dict[str, Any] | str = "[DONE]"
        else:
            parsed_data = json.loads(data_text)
        return {
            "stream": self.stream_name,
            "event": event_name,
            "data": parsed_data,
        }

    def _append_timeline(self, item: dict[str, Any]) -> None:
        sequence = next(self.sequence_counter)
        data = item["data"]
        if self.stream_name == "chat":
            if data == "[DONE]":
                self.timeline.append({
                    "sequence": sequence,
                    "stream": "chat",
                    "done": True,
                })
                return
            if isinstance(data, dict) and data.get("eventType"):
                payload = data.get("payload") if isinstance(data.get("payload"), dict) else {}
                self.timeline.append({
                    "sequence": sequence,
                    "stream": "chat",
                    "eventType": data.get("eventType"),
                    "turnId": data.get("turnId"),
                    "toolCallId": data.get("toolCallId"),
                    "subtaskId": data.get("subtaskId"),
                    "toolName": (
                        payload.get("toolName")
                        if isinstance(payload, dict) and payload.get("toolName") is not None
                        else data.get("toolName")
                    ),
                    "stopReason": payload.get("stopReason") if isinstance(payload, dict) else None,
                })
            return

        if self.stream_name == "interaction" and isinstance(data, dict):
            self.timeline.append({
                "sequence": sequence,
                "stream": "interaction",
                "event": item.get("event"),
                "interactionId": data.get("interactionId"),
                "turnId": data.get("turnId"),
                "kind": data.get("kind"),
                "blocking": data.get("blocking"),
                "status": data.get("status"),
            })

    async def wait_for(
        self,
        predicate: Callable[[dict[str, Any]], bool],
        *,
        timeout: float = 10.0,
    ) -> dict[str, Any]:
        for item in self.items:
            if predicate(item):
                return item

        while True:
            item = await asyncio.wait_for(self._queue.get(), timeout=timeout)
            if predicate(item):
                return item

    async def wait_closed(self, *, timeout: float = 10.0) -> None:
        await asyncio.wait_for(self._task, timeout=timeout)

    async def stop(self) -> None:
        if self._task.done():
            try:
                await self._task
            except asyncio.CancelledError:
                pass
        else:
            self._task.cancel()
            try:
                await self._task
            except asyncio.CancelledError:
                pass
        self.response.close()


def _reset_http_state(runtime_store: RuntimeStateStore) -> tuple[Any, Any, Any, Any, Any]:
    original_runtime_store = http_server.runtime_store
    original_agents = http_server.agents
    original_seq_map = http_server._window_seq_map
    original_counter = http_server._window_counter
    original_resolver = http_server.resolve_attachment_image_blocks
    http_server.runtime_store = runtime_store
    http_server.agents = {}
    http_server._window_seq_map = {}
    http_server._window_counter = 1
    http_server.resolve_attachment_image_blocks = lambda *args, **kwargs: []
    return (
        original_runtime_store,
        original_agents,
        original_seq_map,
        original_counter,
        original_resolver,
    )


def _restore_http_state(originals: tuple[Any, Any, Any, Any, Any]) -> None:
    (
        http_server.runtime_store,
        http_server.agents,
        http_server._window_seq_map,
        http_server._window_counter,
        http_server.resolve_attachment_image_blocks,
    ) = originals


async def _build_client(
    runtime_store: RuntimeStateStore,
    *,
    fake_agent: _FakeAgent,
    session: Any,
) -> TestClient:
    async def _fake_get_agent(window_id: str, project_path: str, worktree_path: str | None = None):
        return fake_agent, session

    http_server.get_agent = _fake_get_agent  # type: ignore[assignment]

    app = http_server.create_app()
    server = TestServer(app)
    client = TestClient(server)
    await client.start_server()
    return client


async def _open_recorder(
    client: TestClient,
    *,
    stream_name: str,
    path: str,
    timeline: list[dict[str, Any]],
    sequence_counter: count,
) -> SSERecorder:
    response = await client.get(path)
    return SSERecorder(
        stream_name=stream_name,
        response=response,
        timeline=timeline,
        sequence_counter=sequence_counter,
    )


def _extract_main_events(recorder: SSERecorder) -> tuple[list[dict[str, Any]], bool]:
    events: list[dict[str, Any]] = []
    done_received = False
    for item in recorder.items:
        data = item["data"]
        if data == "[DONE]":
            done_received = True
            continue
        if isinstance(data, dict) and data.get("eventType"):
            events.append(data)
    return events, done_received


def _extract_interaction_events(recorder: SSERecorder) -> list[dict[str, Any]]:
    extracted: list[dict[str, Any]] = []
    for item in recorder.items:
        data = item["data"]
        if isinstance(data, dict):
            extracted.append({
                "event": item.get("event"),
                "record": data,
            })
    return extracted


def _build_chat_timeline_entry(sequence: int, data: dict[str, Any] | str) -> dict[str, Any] | None:
    if data == "[DONE]":
        return {
            "sequence": sequence,
            "stream": "chat",
            "done": True,
        }

    if not isinstance(data, dict) or not data.get("eventType"):
        return None

    payload = data.get("payload") if isinstance(data.get("payload"), dict) else {}
    return {
        "sequence": sequence,
        "stream": "chat",
        "eventType": data.get("eventType"),
        "turnId": data.get("turnId"),
        "toolCallId": data.get("toolCallId"),
        "subtaskId": data.get("subtaskId"),
        "toolName": (
            payload.get("toolName")
            if isinstance(payload, dict) and payload.get("toolName") is not None
            else data.get("toolName")
        ),
        "stopReason": payload.get("stopReason") if isinstance(payload, dict) else None,
    }


def _build_interaction_timeline_entry(sequence: int, item: dict[str, Any]) -> dict[str, Any] | None:
    data = item.get("data")
    if not isinstance(data, dict):
        return None

    return {
        "sequence": sequence,
        "stream": "interaction",
        "event": item.get("event"),
        "interactionId": data.get("interactionId"),
        "turnId": data.get("turnId"),
        "kind": data.get("kind"),
        "blocking": data.get("blocking"),
        "status": data.get("status"),
    }


async def _record_standard_chat_turn(output_dir: Path) -> None:
    async def _stream(runtime_context: dict[str, str]):
        yield StreamChunk(type="text", content="Hello from baseline")
        yield StreamChunk(type="text_complete", content="Hello from baseline")

    await _record_simple_chat_scenario(
        output_dir=output_dir,
        scenario="standard_chat_turn",
        stream_factory=_stream,
    )


async def _record_single_tool_call(output_dir: Path) -> None:
    async def _stream(runtime_context: dict[str, str]):
        yield StreamChunk(
            type="tool_call_start",
            tool_call_id="tool-read-1",
            tool_name="Read",
            tool_description="Read file",
            tool_params={"path": "README.md"},
        )
        yield StreamChunk(
            type="tool_call_complete",
            tool_call_id="tool-read-1",
            tool_name="Read",
            success=True,
            tool_output="file contents",
        )

    await _record_simple_chat_scenario(
        output_dir=output_dir,
        scenario="single_tool_call",
        stream_factory=_stream,
    )


async def _record_simple_chat_scenario(
    *,
    output_dir: Path,
    scenario: str,
    stream_factory: Callable[[dict[str, str]], Any],
) -> None:
    runtime_store = RuntimeStateStore()
    session = await runtime_store.create_session(
        window_id=WINDOW_ID,
        project_path=PROJECT_PATH,
        worktree_path=None,
    )
    originals = _reset_http_state(runtime_store)
    original_get_agent = http_server.get_agent
    client: TestClient | None = None
    interaction_recorder: SSERecorder | None = None
    chat_recorder: SSERecorder | None = None
    timeline: list[dict[str, Any]] = []
    sequence_counter = count(1)

    try:
        fake_agent = _FakeAgent(stream_factory=stream_factory)
        client = await _build_client(runtime_store, fake_agent=fake_agent, session=session)
        interaction_recorder = await _open_recorder(
            client,
            stream_name="interaction",
            path="/api/interaction/events",
            timeline=timeline,
            sequence_counter=sequence_counter,
        )

        chat_response = await client.post(
            "/api/chat/stream",
            json={
                "projectPath": PROJECT_PATH,
                "windowId": WINDOW_ID,
                "message": "hello",
                "model": MODEL_ID,
            },
        )
        chat_recorder = SSERecorder(
            stream_name="chat",
            response=chat_response,
            timeline=timeline,
            sequence_counter=sequence_counter,
        )
        await chat_recorder.wait_closed()

        events, done_received = _extract_main_events(chat_recorder)
        normalized_timeline: list[dict[str, Any]] = []
        for sequence, item in enumerate(chat_recorder.items, start=1):
            entry = _build_chat_timeline_entry(sequence, item["data"])
            if entry is not None:
                normalized_timeline.append(entry)
        trace = {
            "scenario": scenario,
            "recordedAt": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
            "windowId": WINDOW_ID,
            "sessionId": chat_response.headers.get("X-Session-Id"),
            "doneReceived": done_received,
            "events": events,
            "interactionEvents": _extract_interaction_events(interaction_recorder),
            "interactionQueries": [],
            "timeline": normalized_timeline,
        }

        output_path = output_dir / f"{scenario}.normalized.json"
        output_path.write_text(json.dumps(trace, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print(f"WROTE {output_path}")
    finally:
        if chat_recorder is not None:
            await chat_recorder.stop()
        if interaction_recorder is not None:
            await interaction_recorder.stop()
        if client is not None:
            await client.close()
        http_server.get_agent = original_get_agent  # type: ignore[assignment]
        _restore_http_state(originals)


async def _record_blocking_interaction_question(output_dir: Path) -> None:
    questions = [
        {
            "header": "Color",
            "question": "Pick one color",
            "options": [
                {"label": "Blue", "description": "Use blue"},
                {"label": "Green", "description": "Use green"},
            ],
            "multiSelect": False,
        }
    ]

    async def _stream(runtime_context: dict[str, str]):
        yield StreamChunk(
            type="tool_call_start",
            tool_call_id="tool-ask-1",
            tool_name="AskUserQuestion",
            tool_description="Ask the user a blocking question",
            tool_params={"questions": questions},
        )
        await http_server.request_user_question(questions, runtime_context=runtime_context)
        yield StreamChunk(
            type="tool_call_complete",
            tool_call_id="tool-ask-1",
            tool_name="AskUserQuestion",
            success=True,
        )

    runtime_store = RuntimeStateStore()
    session = await runtime_store.create_session(
        window_id=WINDOW_ID,
        project_path=PROJECT_PATH,
        worktree_path=None,
    )
    originals = _reset_http_state(runtime_store)
    original_get_agent = http_server.get_agent
    client: TestClient | None = None
    interaction_recorder: SSERecorder | None = None
    chat_recorder: SSERecorder | None = None
    timeline: list[dict[str, Any]] = []
    sequence_counter = count(1)

    try:
        fake_agent = _FakeAgent(stream_factory=_stream)
        client = await _build_client(runtime_store, fake_agent=fake_agent, session=session)
        interaction_recorder = await _open_recorder(
            client,
            stream_name="interaction",
            path="/api/interaction/events",
            timeline=timeline,
            sequence_counter=sequence_counter,
        )

        chat_response = await client.post(
            "/api/chat/stream",
            json={
                "projectPath": PROJECT_PATH,
                "windowId": WINDOW_ID,
                "message": "hello",
                "model": MODEL_ID,
            },
        )
        chat_recorder = SSERecorder(
            stream_name="chat",
            response=chat_response,
            timeline=timeline,
            sequence_counter=sequence_counter,
        )

        tool_started_item = await chat_recorder.wait_for(
            lambda item: isinstance(item.get("data"), dict)
            and item["data"].get("eventType") == "tool.started"
            and item["data"].get("toolName") == "AskUserQuestion",
        )

        pushed = await interaction_recorder.wait_for(
            lambda item: item.get("event") == "interaction.pushed"
            and isinstance(item.get("data"), dict)
            and item["data"].get("kind") == "question",
        )
        interaction_record = pushed["data"]
        interaction_id = interaction_record["interactionId"]

        query_response = await client.get(f"/api/interaction?windowId={WINDOW_ID}")
        query_payload = await query_response.json()

        submit_response = await client.post(
            f"/api/interaction/{interaction_id}/submit",
            json={"resolutionPayload": {"answers": {"Pick one color": "Blue"}}},
        )
        await submit_response.json()

        resolved_item = await interaction_recorder.wait_for(
            lambda item: item.get("event") == "interaction.resolved"
            and isinstance(item.get("data"), dict)
            and item["data"].get("interactionId") == interaction_id,
        )
        await chat_recorder.wait_closed()

        events, done_received = _extract_main_events(chat_recorder)
        tool_completed_event = next(
            event
            for event in events
            if event.get("eventType") == "tool.completed" and event.get("toolName") == "AskUserQuestion"
        )
        turn_completed_event = next(event for event in events if event.get("eventType") == "turn.completed")

        normalized_timeline: list[dict[str, Any]] = []
        entry = _build_chat_timeline_entry(1, tool_started_item["data"])
        if entry is not None:
            normalized_timeline.append(entry)
        entry = _build_interaction_timeline_entry(2, pushed)
        if entry is not None:
            normalized_timeline.append(entry)
        entry = _build_interaction_timeline_entry(3, resolved_item)
        if entry is not None:
            normalized_timeline.append(entry)
        entry = _build_chat_timeline_entry(4, tool_completed_event)
        if entry is not None:
            normalized_timeline.append(entry)
        entry = _build_chat_timeline_entry(5, turn_completed_event)
        if entry is not None:
            normalized_timeline.append(entry)
        if done_received:
            entry = _build_chat_timeline_entry(6, "[DONE]")
            if entry is not None:
                normalized_timeline.append(entry)

        trace = {
            "scenario": "blocking_interaction_question",
            "recordedAt": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
            "windowId": WINDOW_ID,
            "sessionId": chat_response.headers.get("X-Session-Id"),
            "doneReceived": done_received,
            "events": events,
            "interactionEvents": _extract_interaction_events(interaction_recorder),
            "interactionQueries": [query_payload],
            "timeline": normalized_timeline,
        }

        output_path = output_dir / "blocking_interaction_question.normalized.json"
        output_path.write_text(json.dumps(trace, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print(f"WROTE {output_path}")
    finally:
        if chat_recorder is not None:
            await chat_recorder.stop()
        if interaction_recorder is not None:
            await interaction_recorder.stop()
        if client is not None:
            await client.close()
        http_server.get_agent = original_get_agent  # type: ignore[assignment]
        _restore_http_state(originals)


async def _record_all(output_dir: Path) -> None:
    output_dir.mkdir(parents=True, exist_ok=True)
    await _record_standard_chat_turn(output_dir)
    await _record_single_tool_call(output_dir)
    await _record_blocking_interaction_question(output_dir)


def main(argv: list[str]) -> int:
    output_dir = Path(argv[1]) if len(argv) > 1 else Path(__file__).resolve().parent / "fixtures"
    asyncio.run(_record_all(output_dir))
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
