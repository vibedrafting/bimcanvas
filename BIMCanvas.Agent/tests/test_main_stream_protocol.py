from __future__ import annotations

import asyncio
import json
import sys
from pathlib import Path
from types import SimpleNamespace

import pytest
from aiohttp.test_utils import TestClient, TestServer


AGENT_ROOT = Path(__file__).resolve().parents[1]
if str(AGENT_ROOT) not in sys.path:
    sys.path.insert(0, str(AGENT_ROOT))

from src.agent.main_agent import MainAgent
from src.runtime import MainStreamMapper, RuntimeStateStore, StreamChunk
from src.server import http_server


def _parse_sse_payloads(raw_text: str) -> list[dict | str]:
    payloads: list[dict | str] = []
    for line in raw_text.splitlines():
        if not line.startswith("data: "):
            continue
        data = line[6:]
        if data == "[DONE]":
            payloads.append(data)
        else:
            payloads.append(json.loads(data))
    return payloads


class _FakeStreamAgent:
    def __init__(self, *, chunks: list[StreamChunk], error: Exception | None = None) -> None:
        self._connected = True
        self._chunks = chunks
        self._error = error
        self.runtime_contexts: list[dict[str, str] | None] = []

    def get_current_model(self) -> str:
        return "sonnet"

    async def set_model(self, model: str) -> None:
        return None

    def clear_runtime_context(self) -> None:
        return None

    async def chat_stream(self, *args, **kwargs):
        self.runtime_contexts.append(kwargs.get("runtime_context"))
        for chunk in self._chunks:
            yield chunk
        if self._error is not None:
            raise self._error


async def _run_stream_request(
    monkeypatch: pytest.MonkeyPatch,
    fake_agent: _FakeStreamAgent,
) -> tuple[dict[str, str], list[dict | str], str]:
    runtime_store = RuntimeStateStore()
    session = await runtime_store.create_session(
        window_id="primary",
        project_path="C:/demo",
        worktree_path=None,
    )

    async def _fake_get_agent(window_id: str, project_path: str, worktree_path: str | None = None):
        return fake_agent, session

    monkeypatch.setattr(http_server, "runtime_store", runtime_store)
    monkeypatch.setattr(http_server, "get_agent", _fake_get_agent)
    monkeypatch.setattr(http_server, "resolve_attachment_image_blocks", lambda *args, **kwargs: [])

    app = http_server.create_app()
    server = TestServer(app)
    client = TestClient(server)
    await client.start_server()

    try:
        response = await client.post(
            "/api/chat/stream",
            json={
                "projectPath": "C:/demo",
                "windowId": "primary",
                "message": "hello",
                "model": "sonnet",
            },
        )
        headers = dict(response.headers)
        raw_text = await response.text()
        return headers, _parse_sse_payloads(raw_text), session.session_id
    finally:
        await client.close()


def test_main_stream_mapper_maps_legacy_and_envelope_events() -> None:
    mapper = MainStreamMapper(session_id="session-1", turn_id="turn-1")

    payloads: list[dict[str, object]] = []
    for chunk in [
        StreamChunk(type="thinking", content="thinking..."),
        StreamChunk(type="text_complete", content="done"),
        StreamChunk(
            type="subagent_start",
            subagent_id="sa-1",
            subagent_name="Explore",
            subagent_type="explorer",
        ),
        StreamChunk(
            type="tool_call_start",
            tool_call_id="tc-1",
            tool_name="Read",
            tool_description="Read file",
            tool_params={"path": "a.txt"},
        ),
        StreamChunk(
            type="tool_call_complete",
            tool_call_id="tc-1",
            success=True,
            tool_output="file body",
        ),
        StreamChunk(type="task_output_polling", task_id="task-1", timeout=3000),
    ]:
        payloads.extend(item for item in mapper.map_chunk(chunk) if isinstance(item, dict))

    envelope_events = [item for item in payloads if item.get("eventType")]
    assert len({item["eventId"] for item in envelope_events}) == len(envelope_events)
    assert all(item["sessionId"] == "session-1" for item in envelope_events)
    assert all(item["turnId"] == "turn-1" for item in envelope_events)

    assert envelope_events[0]["eventType"] == "thinking.delta"
    assert envelope_events[1]["eventType"] == "text.completed"

    subtask_started = next(item for item in envelope_events if item["eventType"] == "subtask.started")
    assert subtask_started["subtaskId"] == "sa-1"
    assert subtask_started["payload"]["parentSubtaskId"] == "st-root-turn-1"
    assert subtask_started["payload"]["origin"] == "tool"

    tool_started = next(item for item in envelope_events if item["eventType"] == "tool.started")
    tool_output = next(item for item in envelope_events if item["eventType"] == "tool.output")
    tool_completed = next(item for item in envelope_events if item["eventType"] == "tool.completed")

    assert tool_started["toolCallId"] == "tc-1"
    assert tool_output["toolCallId"] == "tc-1"
    assert tool_completed["toolCallId"] == "tc-1"
    assert tool_started["subtaskId"] == "st-root-turn-1"
    assert tool_output["type"] == "tool_call_output"
    assert tool_completed["type"] == "tool_call_complete"
    assert "toolOutput" not in tool_completed

    task_polling = next(item for item in payloads if item.get("type") == "task_output_polling")
    assert "eventType" not in task_polling


def test_main_stream_mapper_builds_max_turns_terminal_failure() -> None:
    mapper = MainStreamMapper(session_id="session-1", turn_id="turn-2")
    mapper.map_chunk(
        StreamChunk(
            type="text",
            content="[SDK 错误] 已达最大轮数限制 (30 轮)",
            error_type="sdk_error",
            error_content="error_max_turns",
        )
    )

    terminal = mapper.build_success_terminal_event()
    assert terminal["eventType"] == "turn.failed"
    assert terminal["payload"]["stopReason"] == "max_turns"
    assert terminal["payload"]["error"]["code"] == "MAX_TURNS_EXCEEDED"


def test_main_stream_mapper_builds_tool_error_terminal_failure_on_exception() -> None:
    mapper = MainStreamMapper(session_id="session-1", turn_id="turn-3")
    mapper.map_chunk(
        StreamChunk(
            type="tool_call_complete",
            tool_call_id="tc-9",
            success=False,
            error="tool boom",
            error_type="blocking",
        )
    )

    terminal = mapper.build_exception_terminal_event(RuntimeError("stream lost"))
    assert terminal["eventType"] == "turn.failed"
    assert terminal["payload"]["stopReason"] == "tool_error"
    assert terminal["payload"]["error"]["code"] == "TOOL_EXECUTION_FAILED"
    assert terminal["payload"]["error"]["relatedToolCallId"] == "tc-9"


def test_chat_stream_handler_keeps_session_ready_and_done(monkeypatch: pytest.MonkeyPatch) -> None:
    async def _test() -> None:
        fake_agent = _FakeStreamAgent(
            chunks=[
                StreamChunk(type="text", content="hello"),
                StreamChunk(type="text_complete", content="hello"),
            ]
        )

        headers, payloads, session_id = await _run_stream_request(monkeypatch, fake_agent)

        assert headers["X-Session-Id"] == session_id
        assert payloads[0]["type"] == "session_ready"
        assert "eventType" not in payloads[0]

        text_event = next(item for item in payloads if isinstance(item, dict) and item.get("type") == "text")
        assert text_event["eventType"] == "text.delta"
        assert text_event["sessionId"] == session_id
        assert text_event["turnId"] == fake_agent.runtime_contexts[0]["turnId"]

        terminal = next(item for item in payloads if isinstance(item, dict) and item.get("eventType") == "turn.completed")
        assert terminal["payload"]["stopReason"] == "completed"
        assert payloads[-1] == "[DONE]"

    asyncio.run(_test())


def test_chat_stream_handler_emits_tool_error_turn_failed_on_exception(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    async def _test() -> None:
        fake_agent = _FakeStreamAgent(
            chunks=[
                StreamChunk(
                    type="tool_call_start",
                    tool_call_id="tc-5",
                    tool_name="Read",
                    tool_params={"path": "a.txt"},
                ),
                StreamChunk(
                    type="tool_call_complete",
                    tool_call_id="tc-5",
                    success=False,
                    error="tool boom",
                    error_type="blocking",
                ),
            ],
            error=RuntimeError("stream lost"),
        )

        _, payloads, _ = await _run_stream_request(monkeypatch, fake_agent)

        flat_error = next(item for item in payloads if isinstance(item, dict) and item.get("error") == "stream lost")
        assert flat_error == {"error": "stream lost"}

        terminal = next(item for item in payloads if isinstance(item, dict) and item.get("eventType") == "turn.failed")
        assert terminal["payload"]["stopReason"] == "tool_error"
        assert terminal["payload"]["error"]["relatedToolCallId"] == "tc-5"
        assert payloads[-1] == "[DONE]"

    asyncio.run(_test())


def test_main_agent_tool_result_clears_pending_tool_tracking() -> None:
    async def _test() -> None:
        agent = MainAgent(
            project_path=str(AGENT_ROOT),
            working_directory=str(AGENT_ROOT),
            verbose=False,
        )
        agent._connected = True
        agent._process_streaming_event = lambda *args, **kwargs: None

        class _FakeClient:
            async def query(self, _message_stream) -> None:
                agent._pending_tool_calls["tool-1"] = "tc-1"
                agent._tool_to_subagent["tool-1"] = "sa-1"
                agent._tool_call_counter = 1

            async def receive_response(self):
                yield SimpleNamespace(
                    event={
                        "type": "tool_result",
                        "tool_name": "Read",
                        "result": "done",
                        "is_error": False,
                        "tool_use_id": "tool-1",
                    }
                )

        agent._client = _FakeClient()

        chunks = [chunk async for chunk in agent.chat_stream("hello", model="sonnet")]

        assert len(chunks) == 1
        assert chunks[0].type == "tool_call_complete"
        assert chunks[0].tool_call_id == "tc-1"
        assert chunks[0].subagent_id == "sa-1"
        assert agent._pending_tool_calls == {}
        assert agent._tool_to_subagent == {}

    asyncio.run(_test())
