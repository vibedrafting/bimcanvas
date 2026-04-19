from __future__ import annotations

import asyncio
import json
import logging
import shutil
import sys
from pathlib import Path
from types import ModuleType, SimpleNamespace
from typing import get_type_hints

import pytest


AGENT_ROOT = Path(__file__).resolve().parents[1]
REPO_ROOT = AGENT_ROOT.parent
TEMPLATE_ROOT = REPO_ROOT / "BIMCanvas.Server" / "Templates" / "global-config"
if str(AGENT_ROOT) not in sys.path:
    sys.path.insert(0, str(AGENT_ROOT))

from src.agent.openai_agent import OpenAIAgent
from src.config.loader import ConfigLoader, get_config_loader
from src.config.settings import get_settings
from src.runtime import PendingInteractionRuntimeBinding, RuntimeSessionRecord, StreamChunk
from src.runtime.openai_stream import OpenAIStreamTranslator
from src.runtime.providers import OPENAI_RUNTIME_ID, build_capability_matrix


def _reset_config_caches() -> None:
    get_config_loader.cache_clear()
    get_settings.cache_clear()


def _prepare_bimcanvas_home(tmp_path: Path) -> Path:
    home = tmp_path / "BIMCanvasHome"
    shutil.copytree(TEMPLATE_ROOT / "agent", home)
    shutil.copy2(TEMPLATE_ROOT / "server" / "web_config.json", home / "web_config.json")
    return home


def _read_json(path: Path) -> dict:
    with path.open("r", encoding="utf-8-sig") as handle:
        return json.load(handle)


def _write_json(path: Path, payload: dict) -> None:
    path.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")


def _configure_test_home(monkeypatch: pytest.MonkeyPatch, home: Path) -> None:
    monkeypatch.setenv("BIMCANVAS_HOME", str(home))
    monkeypatch.delenv("AGENT_RUNTIME_PROVIDER", raising=False)
    monkeypatch.delenv("OPENAI_API_KEY", raising=False)
    monkeypatch.delenv("OPENAI_BASE_URL", raising=False)
    monkeypatch.setattr(ConfigLoader, "DEFAULT_CONFIG_DIR", home)
    _reset_config_caches()


def _set_openai_runtime_config(
    home: Path,
    *,
    api_key: str = "test-openai-key",
    base_url: str = "https://api.openai.com/v1",
    openai_api: str | None = None,
    openai_disable_tracing: bool | None = None,
    model_mapping: dict | None = None,
    permissions: dict | None = None,
) -> None:
    config_path = home / "config.json"
    config = _read_json(config_path)
    config["runtimeProvider"] = OPENAI_RUNTIME_ID
    config["apiKey"] = api_key
    config["baseUrl"] = base_url
    if openai_api is not None:
        config["openaiApi"] = openai_api
    else:
        config.pop("openaiApi", None)
    if openai_disable_tracing is not None:
        config["openaiDisableTracing"] = openai_disable_tracing
    else:
        config.pop("openaiDisableTracing", None)
    if model_mapping is not None:
        config["modelMapping"] = model_mapping
    if permissions is not None:
        config["permissions"] = permissions
    _write_json(config_path, config)


def _set_web_default_model(home: Path, model_id: str) -> None:
    web_config_path = home / "web_config.json"
    web_config = _read_json(web_config_path)
    web_config["defaultModel"] = model_id
    web_config["customModels"] = [{"id": model_id, "label": model_id}]
    _write_json(web_config_path, web_config)


def _install_fake_tool_context(monkeypatch: pytest.MonkeyPatch) -> None:
    agents_module = ModuleType("agents")
    tool_context_module = ModuleType("agents.tool_context")

    class ToolContext:
        def __init__(self, context: dict | None = None, tool_call_id: str | None = None) -> None:
            self.context = context or {}
            self.tool_call_id = tool_call_id

    tool_context_module.ToolContext = ToolContext
    monkeypatch.setitem(sys.modules, "agents", agents_module)
    monkeypatch.setitem(sys.modules, "agents.tool_context", tool_context_module)


async def _collect_chunks(stream) -> list[StreamChunk]:
    chunks: list[StreamChunk] = []
    async for chunk in stream:
        chunks.append(chunk)
    return chunks


class _FakeTool(SimpleNamespace):
    name: str


class _FakeAgentsModule:
    @staticmethod
    def function_tool(*, name_override: str | None = None, needs_approval: bool = False):
        def decorator(fn):
            get_type_hints(fn)
            return _FakeTool(
                name=name_override or fn.__name__,
                fn=fn,
                needs_approval=needs_approval,
            )

        return decorator


def test_build_tools_registers_phase_one_local_tools_without_name_error(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    home = _prepare_bimcanvas_home(tmp_path)
    _configure_test_home(monkeypatch, home)
    _install_fake_tool_context(monkeypatch)
    _set_openai_runtime_config(
        home,
        model_mapping={"gpt-4.1": {"id": "gpt-4.1", "label": "GPT-4.1"}},
        permissions={"allow": None, "deny": ["Task", "mcp__canvas__validate_layout"]},
    )
    _set_web_default_model(home, "gpt-4.1")
    _reset_config_caches()

    agent = OpenAIAgent(project_path=str(tmp_path), working_directory=str(tmp_path))
    tools = agent._build_tools(_FakeAgentsModule(), model="gpt-4.1", nested_stream_handler=None)
    tool_names = [tool.name for tool in tools]

    assert tool_names == [
        "Read",
        "Write",
        "Edit",
        "Glob",
        "Grep",
        "Bash",
        "AskUserQuestion",
    ]
    assert "Task" not in tool_names
    assert not any(name.startswith("mcp__canvas__") for name in tool_names)


def test_build_tools_respects_permissions_and_warns_for_unsupported_entries(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
    caplog: pytest.LogCaptureFixture,
) -> None:
    home = _prepare_bimcanvas_home(tmp_path)
    _configure_test_home(monkeypatch, home)
    _install_fake_tool_context(monkeypatch)
    _set_openai_runtime_config(
        home,
        model_mapping={"gpt-4.1-mini": {"id": "gpt-4.1-mini", "label": "GPT-4.1 mini"}},
        permissions={
            "allow": ["Read", "Bash", "Task", "Skill", "mcp__canvas__validate_layout"],
            "deny": ["Bash", "Task"],
        },
    )
    _set_web_default_model(home, "gpt-4.1-mini")
    _reset_config_caches()

    agent = OpenAIAgent(project_path=str(tmp_path), working_directory=str(tmp_path))
    with caplog.at_level(logging.WARNING):
        tools = agent._build_tools(_FakeAgentsModule(), model="gpt-4.1-mini", nested_stream_handler=None)

    assert [tool.name for tool in tools] == ["Read"]
    assert "OpenAI phase 1 ignored unsupported tools from permissions: Skill, Task, mcp__canvas__validate_layout" in caplog.text


def test_openai_settings_require_api_key(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    home = _prepare_bimcanvas_home(tmp_path)
    _configure_test_home(monkeypatch, home)
    _set_openai_runtime_config(
        home,
        api_key="",
        model_mapping={"gpt-4.1": {"id": "gpt-4.1", "label": "GPT-4.1"}},
    )
    _set_web_default_model(home, "gpt-4.1")
    _reset_config_caches()

    with pytest.raises(ValueError, match="OpenAI runtime requires OPENAI_API_KEY or config.json apiKey"):
        get_settings()


def test_openai_settings_reject_claude_alias_model_mapping_keys(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    home = _prepare_bimcanvas_home(tmp_path)
    _configure_test_home(monkeypatch, home)
    _set_openai_runtime_config(
        home,
        model_mapping={"opus": {"id": "gpt-4.1", "label": "GPT-4.1"}},
    )
    _set_web_default_model(home, "gpt-4.1")
    _reset_config_caches()

    with pytest.raises(ValueError, match="modelMapping keys to be real OpenAI model ids"):
        get_settings()


def test_openai_settings_reject_claude_alias_default_model(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    home = _prepare_bimcanvas_home(tmp_path)
    _configure_test_home(monkeypatch, home)
    _set_openai_runtime_config(
        home,
        model_mapping={"gpt-4.1": {"id": "gpt-4.1", "label": "GPT-4.1"}},
    )
    _set_web_default_model(home, "opus")
    _reset_config_caches()

    with pytest.raises(ValueError, match="web_config.json defaultModel"):
        get_settings()


def test_openai_agent_rejects_claude_alias_requested_model(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    home = _prepare_bimcanvas_home(tmp_path)
    _configure_test_home(monkeypatch, home)
    _set_openai_runtime_config(
        home,
        model_mapping={"gpt-4.1": {"id": "gpt-4.1", "label": "GPT-4.1"}},
    )
    _set_web_default_model(home, "gpt-4.1")
    _reset_config_caches()

    agent = OpenAIAgent(project_path=str(tmp_path), working_directory=str(tmp_path))
    with pytest.raises(ValueError, match="does not accept Claude model aliases"):
        asyncio.run(agent.set_model("sonnet"))


def test_openai_capability_matrix_marks_subtask_as_unsupported() -> None:
    subtask_row = next(
        row
        for row in build_capability_matrix(OPENAI_RUNTIME_ID)
        if row["capabilityKey"] == "subtask_causality"
    )
    assert subtask_row["level"] == "unsupported"
    assert subtask_row["providerMapping"] is None


def test_openai_settings_default_to_responses_for_custom_base_url(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    home = _prepare_bimcanvas_home(tmp_path)
    _configure_test_home(monkeypatch, home)
    _set_openai_runtime_config(
        home,
        base_url="https://gateway.example.com/v1",
        model_mapping={"gpt-4.1": {"id": "gpt-4.1", "label": "GPT-4.1"}},
    )
    _set_web_default_model(home, "gpt-4.1")
    _reset_config_caches()

    settings = get_settings()

    assert settings.openai_api == "responses"
    assert settings.openai_disable_tracing is True


def test_openai_agent_uses_responses_run_fallback_for_custom_endpoint(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    home = _prepare_bimcanvas_home(tmp_path)
    _configure_test_home(monkeypatch, home)
    _set_openai_runtime_config(
        home,
        base_url="https://gateway.example.com/v1",
        openai_api="responses",
        model_mapping={"gpt-4.1": {"id": "gpt-4.1", "label": "GPT-4.1"}},
    )
    _set_web_default_model(home, "gpt-4.1")
    _reset_config_caches()

    agent = OpenAIAgent(project_path=str(tmp_path), working_directory=str(tmp_path))
    assert agent._should_use_responses_run_fallback(get_settings()) is True


def test_openai_agent_reuses_sdk_session_for_same_host_session(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    home = _prepare_bimcanvas_home(tmp_path)
    _configure_test_home(monkeypatch, home)
    _set_openai_runtime_config(
        home,
        base_url="https://gateway.example.com/v1",
        openai_api="responses",
        model_mapping={"gpt-4.1": {"id": "gpt-4.1", "label": "GPT-4.1"}},
    )
    _set_web_default_model(home, "gpt-4.1")
    _reset_config_caches()

    session_instances: list[object] = []
    run_calls: list[dict[str, object]] = []

    class FakeSQLiteSession:
        def __init__(self, session_id: str, db_path=":memory:") -> None:
            self.session_id = session_id
            self.db_path = Path(db_path)
            session_instances.append(self)

        def close(self) -> None:
            return None

    async def fake_run(*args, **kwargs):
        run_calls.append({"args": args, "kwargs": kwargs})
        return SimpleNamespace(new_items=[], interruptions=[])

    fake_agents = ModuleType("agents")
    fake_agents.SQLiteSession = FakeSQLiteSession
    fake_agents.Runner = SimpleNamespace(run=fake_run)
    monkeypatch.setitem(sys.modules, "agents", fake_agents)

    agent = OpenAIAgent(project_path=str(tmp_path), working_directory=str(tmp_path))
    agent._connected = True
    agent._current_model = "gpt-4.1"
    monkeypatch.setattr(agent, "_build_root_agent", lambda *args, **kwargs: object())
    monkeypatch.setattr(agent, "_build_run_context", lambda **kwargs: {"runtimeContext": kwargs.get("runtime_context") or {}})
    monkeypatch.setattr(
        agent,
        "_build_input_items",
        lambda **kwargs: [{"role": "user", "content": [{"type": "input_text", "text": kwargs["user_message"]}]}],
    )
    monkeypatch.setattr(
        agent,
        "_translate_result_chunks",
        lambda **kwargs: [StreamChunk(type="text_complete", content="ok")],
    )

    asyncio.run(
        _collect_chunks(
            agent.chat_stream(
                "first",
                model="gpt-4.1",
                runtime_context={"windowId": "window-main", "sessionId": "session-1", "turnId": "turn-1"},
            )
        )
    )
    asyncio.run(
        _collect_chunks(
            agent.chat_stream(
                "second",
                model="gpt-4.1",
                runtime_context={"windowId": "window-main", "sessionId": "session-1", "turnId": "turn-2"},
            )
        )
    )

    assert len(session_instances) == 1
    assert len(run_calls) == 2
    assert run_calls[0]["kwargs"]["session"] is session_instances[0]
    assert run_calls[1]["kwargs"]["session"] is session_instances[0]
    assert session_instances[0].db_path == home / ".runtime" / "openai_agent_sessions.sqlite3"


def test_openai_agent_resume_interaction_passes_sdk_session(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    home = _prepare_bimcanvas_home(tmp_path)
    _configure_test_home(monkeypatch, home)
    _set_openai_runtime_config(
        home,
        base_url="https://gateway.example.com/v1",
        openai_api="responses",
        model_mapping={"gpt-4.1": {"id": "gpt-4.1", "label": "GPT-4.1"}},
    )
    _set_web_default_model(home, "gpt-4.1")
    _reset_config_caches()

    session_instances: list[object] = []
    run_calls: list[dict[str, object]] = []
    restored_states: list[object] = []

    class FakeSQLiteSession:
        def __init__(self, session_id: str, db_path=":memory:") -> None:
            self.session_id = session_id
            self.db_path = Path(db_path)
            session_instances.append(self)

        def close(self) -> None:
            return None

    class FakeState:
        def __init__(self) -> None:
            self._interruptions = [SimpleNamespace(call_id="call-1")]
            self.approved_call_id: str | None = None

        def get_interruptions(self) -> list[SimpleNamespace]:
            return self._interruptions

        def approve(self, item: SimpleNamespace) -> None:
            self.approved_call_id = item.call_id

    class FakeRunState:
        @staticmethod
        async def from_json(starting_agent, state_payload, context_override=None):
            state = FakeState()
            state.context_override = context_override
            restored_states.append(state)
            return state

    async def fake_run(*args, **kwargs):
        run_calls.append({"args": args, "kwargs": kwargs})
        return SimpleNamespace(new_items=[], interruptions=[])

    fake_agents = ModuleType("agents")
    fake_agents.SQLiteSession = FakeSQLiteSession
    fake_agents.Runner = SimpleNamespace(run=fake_run)
    fake_agents.RunState = FakeRunState
    monkeypatch.setitem(sys.modules, "agents", fake_agents)

    agent = OpenAIAgent(project_path=str(tmp_path), working_directory=str(tmp_path))
    agent._current_model = "gpt-4.1"

    async def fake_connect(*args, **kwargs) -> None:
        agent._connected = True

    monkeypatch.setattr(agent, "connect", fake_connect)
    monkeypatch.setattr(agent, "_build_root_agent", lambda *args, **kwargs: object())
    monkeypatch.setattr(
        agent,
        "_translate_result_chunks",
        lambda **kwargs: [StreamChunk(type="text_complete", content="resumed")],
    )

    binding = PendingInteractionRuntimeBinding(
        interaction_id="interaction-1",
        resume_token="resume-1",
        runtime_id="openai-agents",
        session_id="session-42",
        turn_id="turn-42",
        window_id="window-main",
        run_state_json=json.dumps({"context": {}}),
        approval_call_id="call-1",
        public_tool_call_id="tc-approved",
        projection_state=None,
        agent_identity="BIMCanvas",
    )
    session = RuntimeSessionRecord(
        session_id="session-42",
        window_id="window-main",
        project_path=str(tmp_path),
        worktree_path=None,
        runtime_id="openai-agents",
    )

    appended_chunks: list[StreamChunk] = []

    async def append_event(chunk: StreamChunk) -> list[dict[str, str]]:
        appended_chunks.append(chunk)
        return [{"eventType": chunk.type}]

    result = asyncio.run(
        agent.resume_interaction(
            interaction_id="interaction-1",
            binding=binding,
            resolution_payload={"answers": {"intent": "continue"}},
            session=session,
            append_event=append_event,
        )
    )

    assert len(session_instances) == 1
    assert run_calls[0]["kwargs"]["session"] is session_instances[0]
    assert restored_states[0].approved_call_id == "call-1"
    assert appended_chunks[0].content == "resumed"
    assert result == [{"eventType": "text_complete"}]


def test_openai_stream_translator_translates_run_result_items() -> None:
    translator = OpenAIStreamTranslator(turn_id="turn-1")
    tool_call_item = SimpleNamespace(
        type="tool_call_item",
        raw_item=SimpleNamespace(
            call_id="call-1",
            name="Read",
            arguments='{"file_path":"README.md"}',
        ),
        tool_origin=SimpleNamespace(type="function"),
    )
    tool_output_item = SimpleNamespace(
        type="tool_call_output_item",
        raw_item={"call_id": "call-1", "type": "function_call_output"},
        output="README contents",
        tool_origin=SimpleNamespace(type="function"),
    )
    message_item = SimpleNamespace(
        type="message_output_item",
        raw_item=SimpleNamespace(
            content=[SimpleNamespace(type="output_text", text="Hi!")],
        ),
    )

    tool_start_chunks = translator.translate_result_item(tool_call_item)
    tool_complete_chunks = translator.translate_result_item(tool_output_item)
    text_chunks = translator.translate_result_item(message_item)

    assert [(chunk.type, chunk.tool_name, chunk.tool_call_id) for chunk in tool_start_chunks] == [
        ("tool_call_start", "Read", "tc-1")
    ]
    assert [(chunk.type, chunk.tool_output, chunk.tool_call_id) for chunk in tool_complete_chunks] == [
        ("tool_call_complete", "README contents", "tc-1")
    ]
    assert [(chunk.type, chunk.content) for chunk in text_chunks] == [
        ("text_complete", "Hi!")
    ]


def test_openai_agent_connect_configures_chat_completions_for_custom_endpoint(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    home = _prepare_bimcanvas_home(tmp_path)
    _configure_test_home(monkeypatch, home)
    _set_openai_runtime_config(
        home,
        base_url="https://gateway.example.com/v1",
        openai_api="chat_completions",
        openai_disable_tracing=True,
        model_mapping={"gpt-4.1": {"id": "gpt-4.1", "label": "GPT-4.1"}},
    )
    _set_web_default_model(home, "gpt-4.1")
    _reset_config_caches()

    fake_agents = ModuleType("agents")
    calls: dict[str, object] = {}

    def set_default_openai_client(*, client, use_for_tracing: bool) -> None:
        calls["client"] = client
        calls["use_for_tracing"] = use_for_tracing

    def set_default_openai_api(api: str) -> None:
        calls["openai_api"] = api

    def set_tracing_disabled(disabled: bool) -> None:
        calls["tracing_disabled"] = disabled

    fake_agents.set_default_openai_client = set_default_openai_client
    fake_agents.set_default_openai_api = set_default_openai_api
    fake_agents.set_tracing_disabled = set_tracing_disabled
    monkeypatch.setitem(sys.modules, "agents", fake_agents)

    fake_openai = ModuleType("openai")

    class AsyncOpenAI:
        def __init__(self, **kwargs) -> None:
            self.kwargs = kwargs

    fake_openai.AsyncOpenAI = AsyncOpenAI
    monkeypatch.setitem(sys.modules, "openai", fake_openai)

    agent = OpenAIAgent(project_path=str(tmp_path), working_directory=str(tmp_path))
    asyncio.run(agent.connect(model="gpt-4.1"))

    assert calls["openai_api"] == "chat_completions"
    assert calls["tracing_disabled"] is True
    assert calls["use_for_tracing"] is False
    assert calls["client"].kwargs["base_url"] == "https://gateway.example.com/v1"


def test_openai_agent_logs_completed_text_and_tool_chunks(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    home = _prepare_bimcanvas_home(tmp_path)
    _configure_test_home(monkeypatch, home)
    _set_openai_runtime_config(
        home,
        model_mapping={"gpt-4.1": {"id": "gpt-4.1", "label": "GPT-4.1"}},
    )
    _set_web_default_model(home, "gpt-4.1")
    _reset_config_caches()

    class FakeLogger:
        def __init__(self) -> None:
            self.calls: list[tuple[str, object]] = []

        def log_thinking_start(self) -> None:
            self.calls.append(("thinking_start", None))

        def log_thinking(self, content: str) -> None:
            self.calls.append(("thinking", content))

        def log_thinking_end(self) -> None:
            self.calls.append(("thinking_end", None))

        def log_tool_use(self, tool_name: str, tool_input: dict) -> None:
            self.calls.append(("tool_use", (tool_name, tool_input)))

        def log_tool_result(self, tool_name: str, result: str, is_error: bool = False) -> None:
            self.calls.append(("tool_result", (tool_name, result, is_error)))

        def log_response_start(self) -> None:
            self.calls.append(("response_start", None))

        def log_response(self, content: str) -> None:
            self.calls.append(("response", content))

        def log_response_end(self) -> None:
            self.calls.append(("response_end", None))

        def log_info(self, info: str) -> None:
            self.calls.append(("info", info))

    agent = OpenAIAgent(project_path=str(tmp_path), working_directory=str(tmp_path))
    fake_logger = FakeLogger()
    agent._agent_logger = fake_logger

    agent._log_chunk_for_console(
        StreamChunk(type="tool_call_start", tool_name="Read", tool_params={"file_path": "README.md"})
    )
    agent._log_chunk_for_console(
        StreamChunk(type="tool_call_complete", tool_name="Read", tool_output="read ok")
    )
    agent._log_chunk_for_console(StreamChunk(type="text_complete", content="你好"))

    assert ("tool_use", ("Read", {"file_path": "README.md"})) in fake_logger.calls
    assert ("tool_result", ("Read", "read ok", False)) in fake_logger.calls
    assert ("response", "你好") in fake_logger.calls
