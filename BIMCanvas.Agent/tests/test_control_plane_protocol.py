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

from src.agent.main_agent import StreamChunk
from src.runtime import RuntimeStateStore
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
