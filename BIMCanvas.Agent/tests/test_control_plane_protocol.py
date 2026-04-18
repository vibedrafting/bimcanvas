from __future__ import annotations

import asyncio
import json
import sys
from pathlib import Path
from types import SimpleNamespace

from aiohttp.test_utils import TestClient, TestServer
import pytest


AGENT_ROOT = Path(__file__).resolve().parents[1]
if str(AGENT_ROOT) not in sys.path:
    sys.path.insert(0, str(AGENT_ROOT))

from src.runtime import PendingInteractionRuntimeBinding, RuntimeStateStore, StreamChunk
from src.server import http_server


def _reset_http_state(monkeypatch: pytest.MonkeyPatch, runtime_store: RuntimeStateStore) -> None:
    monkeypatch.setattr(http_server, "runtime_store", runtime_store)
    monkeypatch.setattr(http_server, "agents", {})
    monkeypatch.setattr(http_server, "_window_seq_map", {})
    monkeypatch.setattr(http_server, "_window_counter", 1)
    monkeypatch.setattr(http_server, "resolve_attachment_image_blocks", lambda *args, **kwargs: [])


async def _build_client(monkeypatch: pytest.MonkeyPatch, runtime_store: RuntimeStateStore) -> TestClient:
    _reset_http_state(monkeypatch, runtime_store)
    app = http_server.create_app()
    server = TestServer(app)
    client = TestClient(server)
    await client.start_server()
    return client


def _parse_sse_payloads(raw_text: str) -> list[dict | str]:
    payloads: list[dict | str] = []
    for line in raw_text.splitlines():
        if not line.startswith("data: "):
            continue
        data = line[6:]
        payloads.append(data if data == "[DONE]" else json.loads(data))
    return payloads


def test_config_handler_returns_capability_matrix(monkeypatch: pytest.MonkeyPatch) -> None:
    async def _test() -> None:
        runtime_store = RuntimeStateStore()
        monkeypatch.setattr(
            http_server,
            "get_settings",
            lambda: SimpleNamespace(
                model_mapping={
                    "sonnet": {"label": "Sonnet"},
                    "opus": {"label": "Opus"},
                },
                default_effort="medium",
                default_thinking="off",
            ),
        )
        client = await _build_client(monkeypatch, runtime_store)
        try:
            response = await client.get("/api/config")
            assert response.status == 200
            payload = await response.json()
            assert payload["runtime"] == "claude-sdk"
            assert payload["runtimeVersion"] == "0.1.0"
            assert payload["defaultEffort"] == "medium"
            assert payload["defaultThinking"] == "off"
            assert [item["id"] for item in payload["models"]] == ["sonnet", "opus"]

            matrix = payload["capabilityMatrix"]
            assert any(row["capabilityKey"] == "text_stream" and row["level"] == "required" for row in matrix)
            assert any(row["capabilityKey"] == "thinking" and row["level"] == "optional" for row in matrix)
            assert any(row["capabilityKey"] == "usage" and row["level"] == "unsupported" for row in matrix)
        finally:
            await client.close()

    asyncio.run(_test())


def test_chat_stream_returns_session_paused_when_blocking_interaction_exists(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    async def _test() -> None:
        runtime_store = RuntimeStateStore()
        session = await runtime_store.create_session(window_id="primary", project_path="C:/demo", worktree_path=None)
        await runtime_store.create_interaction(
            session_id=session.session_id,
            turn_id="turn-1",
            window_id="primary",
            kind="question",
            blocking=True,
            resume_token="resume:1",
            request_payload={"questions": []},
        )

        async def _unexpected_get_agent(*args, **kwargs):
            raise AssertionError("get_agent should not be called when SESSION_PAUSED is returned")

        monkeypatch.setattr(http_server, "get_agent", _unexpected_get_agent)
        client = await _build_client(monkeypatch, runtime_store)
        try:
            response = await client.post(
                "/api/chat/stream",
                json={"projectPath": "C:/demo", "windowId": "primary", "message": "hello", "model": "sonnet"},
            )
            payload = await response.json()
            assert response.status == 409
            assert payload["errorType"] == "SESSION_PAUSED"
            assert payload["sessionId"] == session.session_id
            assert payload["sessionStatus"] == "paused"
            assert response.headers["X-Session-Id"] == session.session_id
        finally:
            await client.close()

    asyncio.run(_test())


def test_chat_stream_returns_session_error_for_error_session(monkeypatch: pytest.MonkeyPatch) -> None:
    async def _test() -> None:
        runtime_store = RuntimeStateStore()
        session = await runtime_store.create_session(window_id="primary", project_path="C:/demo", worktree_path=None)
        await runtime_store.mark_session_error(session.session_id)

        async def _unexpected_get_agent(*args, **kwargs):
            raise AssertionError("get_agent should not be called when SESSION_ERROR is returned")

        monkeypatch.setattr(http_server, "get_agent", _unexpected_get_agent)
        client = await _build_client(monkeypatch, runtime_store)
        try:
            response = await client.post(
                "/api/chat/stream",
                json={"projectPath": "C:/demo", "windowId": "primary", "message": "hello", "model": "sonnet"},
            )
            payload = await response.json()
            assert response.status == 409
            assert payload["errorType"] == "SESSION_ERROR"
            assert payload["sessionId"] == session.session_id
            assert payload["sessionStatus"] == "error"
        finally:
            await client.close()

    asyncio.run(_test())


def test_chat_stream_returns_session_expired_for_stale_session_header(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    async def _test() -> None:
        runtime_store = RuntimeStateStore()
        session = await runtime_store.create_session(window_id="primary", project_path="C:/demo", worktree_path=None)
        fake_agent = SimpleNamespace(_connected=True, get_current_model=lambda: "sonnet")

        async def _fake_get_agent(window_id: str, project_path: str, worktree_path: str | None = None):
            return fake_agent, session

        monkeypatch.setattr(http_server, "get_agent", _fake_get_agent)
        client = await _build_client(monkeypatch, runtime_store)
        try:
            response = await client.post(
                "/api/chat/stream",
                headers={"X-Session-Id": "stale-session"},
                json={"projectPath": "C:/demo", "windowId": "primary", "message": "hello", "model": "sonnet"},
            )
            payload = await response.json()
            assert response.status == 409
            assert payload["errorType"] == "SESSION_EXPIRED"
            assert payload["sessionId"] == session.session_id
            assert response.headers["X-Session-Id"] == session.session_id
        finally:
            await client.close()

    asyncio.run(_test())


def test_interrupt_cleans_pending_interactions_without_agent(monkeypatch: pytest.MonkeyPatch) -> None:
    async def _test() -> None:
        runtime_store = RuntimeStateStore()
        session = await runtime_store.create_session(window_id="primary", project_path="C:/demo", worktree_path=None)
        await runtime_store.mark_session_running(session.session_id, "turn-1")
        interaction = await runtime_store.create_interaction(
            session_id=session.session_id,
            turn_id="turn-1",
            window_id="primary",
            kind="question",
            blocking=True,
            resume_token="resume:1",
            request_payload={"questions": []},
        )

        client = await _build_client(monkeypatch, runtime_store)
        try:
            response = await client.post("/api/interrupt", json={"windowId": "primary"})
            assert response.status == 200
            assert await response.json() == {"success": True}

            updated = await runtime_store.get_interaction(interaction.interaction_id)
            assert updated is not None
            assert updated.status == "cancelled"
            assert updated.cancel_reason == "interrupted"
            assert await runtime_store.derive_session_status(session.session_id) == "idle"
        finally:
            await client.close()

    asyncio.run(_test())


def test_history_endpoint_returns_session_transcript_and_terminal_interactions(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    async def _test() -> None:
        runtime_store = RuntimeStateStore()
        session = await runtime_store.create_session(window_id="primary", project_path="C:/demo", worktree_path=None)
        await runtime_store.append_user_history(
            session_id=session.session_id,
            turn_id="turn-1",
            window_id="primary",
            client_message_id="msg-1",
            message="请分析客厅",
            attachments=[
                {
                    "attachmentId": "att-1",
                    "contentUrl": "http://localhost:5000/api/chat/attachments/att-1/content",
                    "status": "submitted",
                }
            ],
        )
        await runtime_store.append_event_history(
            session_id=session.session_id,
            turn_id="turn-1",
            window_id="primary",
            event_payload={
                "eventType": "text.completed",
                "payload": {"content": "先看一下空间。"},
            },
        )
        interaction = await runtime_store.create_interaction(
            session_id=session.session_id,
            turn_id="turn-1",
            window_id="primary",
            kind="question",
            blocking=True,
            resume_token="resume:question",
            request_payload={"questions": [{"question": "需要保留电视墙吗？", "header": "电视墙", "options": []}]},
        )
        await runtime_store.submit_interaction(
            interaction.interaction_id,
            {"answers": {"需要保留电视墙吗？": "保留"}},
        )

        client = await _build_client(monkeypatch, runtime_store)
        try:
          history_response = await client.get("/api/history?windowId=primary")
          assert history_response.status == 200
          history_payload = await history_response.json()
          assert history_payload["sessionId"] == session.session_id
          assert history_payload["sessionStatus"] == "idle"
          assert len(history_payload["history"]) == 2
          assert history_payload["history"][0]["kind"] == "user_message"
          assert history_payload["history"][0]["attachments"][0]["attachmentId"] == "att-1"
          assert history_payload["history"][1]["event"]["eventType"] == "text.completed"
          assert len(history_payload["interactions"]) == 1
          assert history_payload["interactions"][0]["status"] == "resolved"

          interaction_response = await client.get("/api/interaction?windowId=primary&includeTerminal=true")
          assert interaction_response.status == 200
          interaction_payload = await interaction_response.json()
          assert interaction_payload["includeTerminal"] is True
          assert len(interaction_payload["interactions"]) == 1
          assert interaction_payload["interactions"][0]["interactionId"] == interaction.interaction_id
        finally:
            await client.close()

    asyncio.run(_test())


def test_chat_stream_continues_recording_history_after_client_stream_disconnect(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    async def _test() -> None:
        runtime_store = RuntimeStateStore()
        session = await runtime_store.create_session(window_id="primary", project_path="C:/demo", worktree_path=None)

        class _FakeAgent:
            _connected = True

            def get_current_model(self) -> str:
                return "sonnet"

            async def set_model(self, model: str) -> None:
                return None

            def clear_runtime_context(self) -> None:
                return None

            async def chat_stream(self, *args, **kwargs):
                yield StreamChunk(type="text", content="断线后继续")
                yield StreamChunk(type="text_complete", content="断线后继续")

        fake_agent = _FakeAgent()

        async def _fake_get_agent(window_id: str, project_path: str, worktree_path: str | None = None):
            return fake_agent, session

        monkeypatch.setattr(http_server, "get_agent", _fake_get_agent)

        write_attempts = {"count": 0}

        async def _fake_try_write_sse_data(response, data):
            write_attempts["count"] += 1
            return False

        monkeypatch.setattr(http_server, "_try_write_sse_data", _fake_try_write_sse_data)

        client = await _build_client(monkeypatch, runtime_store)
        try:
            response = await client.post(
                "/api/chat/stream",
                json={"projectPath": "C:/demo", "windowId": "primary", "message": "hello", "model": "sonnet"},
            )
            assert response.status == 200
            await response.text()

            session_snapshot, history, interactions = await runtime_store.get_history_for_window("primary")
            assert session_snapshot is not None
            assert session_snapshot["status"] == "idle"
            assert interactions == []
            assert [entry["kind"] for entry in history] == [
                "user_message",
                "assistant_event",
                "assistant_event",
                "assistant_event",
            ]
            assert history[1]["event"]["eventType"] == "text.delta"
            assert history[2]["event"]["eventType"] == "text.completed"
            assert history[3]["event"]["eventType"] == "turn.completed"
            assert write_attempts["count"] >= 1
        finally:
            await client.close()

    asyncio.run(_test())


@pytest.mark.parametrize("endpoint", ["/api/clear-history", "/api/agent/close"])
def test_control_plane_shutdown_endpoints_remove_active_session(
    monkeypatch: pytest.MonkeyPatch,
    endpoint: str,
) -> None:
    async def _test() -> None:
        runtime_store = RuntimeStateStore()
        await runtime_store.create_session(window_id="primary", project_path="C:/demo", worktree_path=None)

        client = await _build_client(monkeypatch, runtime_store)
        try:
            response = await client.post(endpoint, json={"windowId": "primary"})
            assert response.status == 200
            assert await response.json() == {"success": True}
            assert await runtime_store.get_active_session("primary") is None
        finally:
            await client.close()

    asyncio.run(_test())


def test_stream_runtime_error_marks_session_error_and_cancels_turn_interactions(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    async def _test() -> None:
        runtime_store = RuntimeStateStore()
        session = await runtime_store.create_session(window_id="primary", project_path="C:/demo", worktree_path=None)

        class _FakeAgent:
            def __init__(self) -> None:
                self._connected = True
                self.created_interaction_id: str | None = None

            def get_current_model(self) -> str:
                return "sonnet"

            async def set_model(self, model: str) -> None:
                return None

            def clear_runtime_context(self) -> None:
                return None

            async def chat_stream(self, *args, **kwargs):
                runtime_context = kwargs["runtime_context"]
                interaction = await runtime_store.create_interaction(
                    session_id=runtime_context["sessionId"],
                    turn_id=runtime_context["turnId"],
                    window_id=runtime_context["windowId"],
                    kind="screenshot",
                    blocking=False,
                    resume_token="resume:none",
                    request_payload={"roomId": "r-1"},
                )
                self.created_interaction_id = interaction.interaction_id
                yield StreamChunk(
                    type="text",
                    content="[API 错误] API 认证失败，请检查 API Key",
                    error_type="api_error",
                    error_content="authentication_failed",
                )

        fake_agent = _FakeAgent()

        async def _fake_get_agent(window_id: str, project_path: str, worktree_path: str | None = None):
            return fake_agent, session

        monkeypatch.setattr(http_server, "get_agent", _fake_get_agent)
        client = await _build_client(monkeypatch, runtime_store)
        try:
            response = await client.post(
                "/api/chat/stream",
                json={"projectPath": "C:/demo", "windowId": "primary", "message": "hello", "model": "sonnet"},
            )
            raw_text = await response.text()
            payloads = _parse_sse_payloads(raw_text)

            terminal = next(item for item in payloads if isinstance(item, dict) and item.get("eventType") == "turn.failed")
            assert terminal["payload"]["stopReason"] == "runtime_error"
            assert terminal["payload"]["error"]["code"] == "AUTH_EXPIRED"
            assert payloads[-1] == "[DONE]"

            assert await runtime_store.derive_session_status(session.session_id) == "error"
            interaction = await runtime_store.get_interaction(fake_agent.created_interaction_id or "")
            assert interaction is not None
            assert interaction.status == "cancelled"
            assert interaction.cancel_reason == "turn_failed"
        finally:
            await client.close()

    asyncio.run(_test())


def test_chat_handler_failure_cancels_turn_interactions_as_turn_failed(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    async def _test() -> None:
        runtime_store = RuntimeStateStore()
        session = await runtime_store.create_session(window_id="primary", project_path="C:/demo", worktree_path=None)

        class _FakeAgent:
            def __init__(self) -> None:
                self.created_interaction_id: str | None = None

            def clear_runtime_context(self) -> None:
                return None

            async def chat(self, message: str, model: str | None = None, runtime_context: dict[str, str] | None = None):
                interaction = await runtime_store.create_interaction(
                    session_id=runtime_context["sessionId"],
                    turn_id=runtime_context["turnId"],
                    window_id=runtime_context["windowId"],
                    kind="screenshot",
                    blocking=False,
                    resume_token="resume:none",
                    request_payload={"roomId": "r-2"},
                )
                self.created_interaction_id = interaction.interaction_id
                raise RuntimeError("sync chat failed")

        fake_agent = _FakeAgent()

        async def _fake_get_agent(window_id: str, project_path: str, worktree_path: str | None = None):
            return fake_agent, session

        monkeypatch.setattr(http_server, "get_agent", _fake_get_agent)
        client = await _build_client(monkeypatch, runtime_store)
        try:
            response = await client.post(
                "/api/chat",
                json={"projectPath": "C:/demo", "windowId": "primary", "message": "hello", "model": "sonnet"},
            )
            payload = await response.json()
            assert response.status == 500
            assert payload["error"] == "sync chat failed"

            interaction = await runtime_store.get_interaction(fake_agent.created_interaction_id or "")
            assert interaction is not None
            assert interaction.status == "cancelled"
            assert interaction.cancel_reason == "turn_failed"
        finally:
            await client.close()

    asyncio.run(_test())


@pytest.mark.parametrize(
    ("endpoint", "payload_builder"),
    [
        (
            "interaction_submit",
            lambda interaction_id: (
                f"/api/interaction/{interaction_id}/submit",
                {"resolutionPayload": {"answers": {"保留电视墙吗？": "保留"}}},
            ),
        ),
        (
            "question_answer",
            lambda interaction_id: (
                "/api/question/answer",
                {"requestId": interaction_id, "answers": {"保留电视墙吗？": "保留"}},
            ),
        ),
    ],
)
def test_question_resolution_endpoints_resume_openai_runtime_binding(
    monkeypatch: pytest.MonkeyPatch,
    endpoint: str,
    payload_builder,
) -> None:
    async def _test() -> None:
        runtime_store = RuntimeStateStore()
        session = await runtime_store.create_session(
            window_id="primary",
            project_path="C:/demo",
            worktree_path=None,
            runtime_id="openai-agents",
            runtime_version="0.1.0",
        )
        await runtime_store.mark_session_running(session.session_id, "turn-1")

        binding = PendingInteractionRuntimeBinding(
            interaction_id="",
            resume_token="",
            runtime_id="openai-agents",
            session_id=session.session_id,
            turn_id="turn-1",
            window_id="primary",
            run_state_json=json.dumps({"context": {"questionAnswersByCallId": {}}}),
            approval_call_id="call-1",
            public_tool_call_id="tc-1",
            projection_state={"toolCallsByProvider": {"call-1": "tc-1"}},
            agent_identity="BIMCanvas",
        )
        interaction = await runtime_store.create_interaction(
            session_id=session.session_id,
            turn_id="turn-1",
            window_id="primary",
            kind="question",
            blocking=True,
            resume_token="resume:test",
            request_payload={"questions": [{"question": "保留电视墙吗？", "header": "电视墙", "options": []}]},
            runtime_binding=binding,
        )

        class _FakeResumeAgent:
            def __init__(self) -> None:
                self.resume_calls: list[dict[str, object]] = []

            def clear_runtime_context(self) -> None:
                return None

            async def resume_interaction(self, **kwargs):
                self.resume_calls.append(kwargs)
                append_event = kwargs["append_event"]
                await append_event(
                    StreamChunk(
                        type="tool_call_complete",
                        tool_call_id="tc-1",
                        tool_output="duplicate-answer",
                        success=True,
                        suppress_public_tool_output=True,
                    )
                )
                return []

        fake_agent = _FakeResumeAgent()

        client = await _build_client(monkeypatch, runtime_store)
        http_server.agents["primary"] = fake_agent
        try:
            path, payload = payload_builder(interaction.interaction_id)
            response = await client.post(path, json=payload)
            assert response.status == 200

            if endpoint == "interaction_submit":
                body = await response.json()
                assert body["interaction"]["status"] == "resolved"
            else:
                assert await response.json() == {"success": True}

            assert len(fake_agent.resume_calls) == 1
            assert fake_agent.resume_calls[0]["interaction_id"] == interaction.interaction_id
            assert fake_agent.resume_calls[0]["resolution_payload"] == {"answers": {"保留电视墙吗？": "保留"}}

            assert await runtime_store.get_runtime_binding(interaction.interaction_id) is None
            assert await runtime_store.derive_session_status(session.session_id) == "idle"

            _, history, interactions = await runtime_store.get_history_for_window("primary")
            event_types = [
                entry["event"]["eventType"]
                for entry in history
                if entry["kind"] == "assistant_event"
            ]
            assert event_types == ["tool.completed", "turn.completed"]
            assert interactions[0]["status"] == "resolved"
        finally:
            await client.close()

    asyncio.run(_test())


def test_duplicate_interaction_submit_is_idempotent_for_openai_resume(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    async def _test() -> None:
        runtime_store = RuntimeStateStore()
        session = await runtime_store.create_session(
            window_id="primary",
            project_path="C:/demo",
            worktree_path=None,
            runtime_id="openai-agents",
            runtime_version="0.1.0",
        )
        await runtime_store.mark_session_running(session.session_id, "turn-1")

        binding = PendingInteractionRuntimeBinding(
            interaction_id="",
            resume_token="",
            runtime_id="openai-agents",
            session_id=session.session_id,
            turn_id="turn-1",
            window_id="primary",
            run_state_json=json.dumps({"context": {"questionAnswersByCallId": {}}}),
            approval_call_id="call-1",
            public_tool_call_id="tc-1",
            projection_state={"toolCallsByProvider": {"call-1": "tc-1"}},
            agent_identity="BIMCanvas",
        )
        interaction = await runtime_store.create_interaction(
            session_id=session.session_id,
            turn_id="turn-1",
            window_id="primary",
            kind="question",
            blocking=True,
            resume_token="resume:test",
            request_payload={"questions": [{"question": "保留电视墙吗？", "header": "电视墙", "options": []}]},
            runtime_binding=binding,
        )

        class _FakeResumeAgent:
            def __init__(self) -> None:
                self.resume_count = 0

            def clear_runtime_context(self) -> None:
                return None

            async def resume_interaction(self, **kwargs):
                self.resume_count += 1
                append_event = kwargs["append_event"]
                await append_event(
                    StreamChunk(
                        type="tool_call_complete",
                        tool_call_id="tc-1",
                        success=True,
                        suppress_public_tool_output=True,
                    )
                )
                return []

        fake_agent = _FakeResumeAgent()

        client = await _build_client(monkeypatch, runtime_store)
        http_server.agents["primary"] = fake_agent
        try:
            path = f"/api/interaction/{interaction.interaction_id}/submit"
            payload = {"resolutionPayload": {"answers": {"保留电视墙吗？": "保留"}}}

            first = await client.post(path, json=payload)
            assert first.status == 200

            second = await client.post(path, json=payload)
            assert second.status == 200

            assert fake_agent.resume_count == 1
            assert await runtime_store.get_runtime_binding(interaction.interaction_id) is None
        finally:
            await client.close()

    asyncio.run(_test())
