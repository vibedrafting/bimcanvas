from __future__ import annotations

import shutil
import sys
from pathlib import Path

import pytest


AGENT_ROOT = Path(__file__).resolve().parents[1]
REPO_ROOT = AGENT_ROOT.parent
TEMPLATE_ROOT = REPO_ROOT / "BIMCanvas.Server" / "Templates" / "global-config"
if str(AGENT_ROOT) not in sys.path:
    sys.path.insert(0, str(AGENT_ROOT))

from src.agent.factory import create_agent
from src.config.loader import ConfigLoader, get_config_loader
from src.config.settings import get_settings
from src.runtime import build_config_bundle


def _reset_config_caches() -> None:
    get_config_loader.cache_clear()
    get_settings.cache_clear()


def _prepare_bimcanvas_home(tmp_path: Path) -> Path:
    home = tmp_path / "BIMCanvasHome"
    shutil.copytree(TEMPLATE_ROOT / "agent", home)
    shutil.copy2(TEMPLATE_ROOT / "server" / "web_config.json", home / "web_config.json")
    return home


def _configure_test_home(monkeypatch: pytest.MonkeyPatch, home: Path) -> None:
    monkeypatch.setenv("BIMCANVAS_HOME", str(home))
    monkeypatch.delenv("AGENT_RUNTIME_PROVIDER", raising=False)
    monkeypatch.setattr(ConfigLoader, "DEFAULT_CONFIG_DIR", home)
    _reset_config_caches()


def test_build_config_bundle_collects_shared_assets(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    home = _prepare_bimcanvas_home(tmp_path)
    _configure_test_home(monkeypatch, home)

    bundle = build_config_bundle()

    assert bundle.system_prompt.strip()
    assert "layout-agent" in bundle.shared_agents
    assert bundle.skill_index
    assert bundle.bimcanvas_home == home.resolve()
    assert "mcp__canvas__validate_layout" in bundle.mcp_tool_names
    assert all(path.is_absolute() and path.name == "SKILL.md" for path in bundle.skill_index.values())


def test_build_config_bundle_raises_when_skill_markdown_is_missing(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    home = _prepare_bimcanvas_home(tmp_path)
    _configure_test_home(monkeypatch, home)

    skill_dir = next(path for path in (home / "skills").iterdir() if path.is_dir())
    (skill_dir / "SKILL.md").unlink()
    _reset_config_caches()

    with pytest.raises(FileNotFoundError, match="Missing skill file"):
        build_config_bundle()


def test_factory_injects_bundle_into_runtime_instances(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    home = _prepare_bimcanvas_home(tmp_path)
    _configure_test_home(monkeypatch, home)

    claude_agent = create_agent(
        "claude",
        project_path=str(tmp_path),
        working_directory=str(tmp_path),
        window_seq=1,
    )
    openai_agent = create_agent(
        "openai",
        project_path=str(tmp_path),
        working_directory=str(tmp_path),
        window_seq=2,
    )

    assert getattr(claude_agent, "_bundle", None) is not None
    assert getattr(openai_agent, "_bundle", None) is not None
    assert getattr(claude_agent, "_subagents", {})
