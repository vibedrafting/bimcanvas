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
    model_mapping: dict | None = None,
    permissions: dict | None = None,
) -> None:
    config_path = home / "config.json"
    config = _read_json(config_path)
    config["runtimeProvider"] = OPENAI_RUNTIME_ID
    config["apiKey"] = api_key
    config["baseUrl"] = "https://api.openai.com/v1"
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
