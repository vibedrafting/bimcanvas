"""v3.6 两层 prompt 架构 · ConfigLoader.load_system_prompt 行为契约。

覆盖 4 种 active_plugin_root 状态:
1. None → 只返回 core-base prompt (单层)
2. 显式传 core-base 自身 → 单层 (防 self-stack)
3. 装 domain plugin → core-base + 边界 + plugin (叠加)
4. core-base BIMCANVAS.md 缺失 / plugin BIMCANVAS.md 缺失 → FileNotFoundError + 明确诊断
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

import pytest


AGENT_ROOT = Path(__file__).resolve().parents[1]
if str(AGENT_ROOT) not in sys.path:
    sys.path.insert(0, str(AGENT_ROOT))

from src.config.loader import ConfigLoader, get_config_loader
from src.config.settings import get_settings


CORE_BASE_STUB = "# core-base STUB\n\nplatform base layer for tests."
PLUGIN_STUB = "# domain layer STUB\n\nbusiness rules for tests."


def _reset_config_caches() -> None:
    get_config_loader.cache_clear()
    get_settings.cache_clear()


def _make_minimal_home(tmp_path: Path) -> Path:
    """构造满足 _validate_bootstrap_layout 的最小 BIMCANVAS_HOME。"""
    home = tmp_path / "BIMCanvasHome"
    core_base = home / "plugins" / "core-base"
    (core_base / ".claude-plugin").mkdir(parents=True)
    (core_base / "skills").mkdir(parents=True)

    (home / "config.json").write_text(
        json.dumps({"providers": {}}), encoding="utf-8"
    )
    (core_base / "BIMCANVAS.md").write_text(CORE_BASE_STUB, encoding="utf-8")
    (core_base / ".claude-plugin" / "plugin.json").write_text(
        json.dumps({"name": "core-base", "version": "0.0.0"}), encoding="utf-8"
    )
    return home


def _make_indoor_layout_plugin(home: Path, with_prompt: bool = True) -> Path:
    plugin_root = home / "plugins" / "indoor-layout"
    plugin_root.mkdir(parents=True)
    if with_prompt:
        (plugin_root / "BIMCANVAS.md").write_text(PLUGIN_STUB, encoding="utf-8")
    return plugin_root


def _configure_home(monkeypatch: pytest.MonkeyPatch, home: Path) -> None:
    monkeypatch.setenv("BIMCANVAS_HOME", str(home))
    monkeypatch.delenv("AGENT_RUNTIME_PROVIDER", raising=False)
    monkeypatch.setattr(ConfigLoader, "DEFAULT_CONFIG_DIR", home)
    _reset_config_caches()


def test_returns_core_base_only_when_no_plugin(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    home = _make_minimal_home(tmp_path)
    _configure_home(monkeypatch, home)

    prompt = ConfigLoader().load_system_prompt(None)

    assert prompt == CORE_BASE_STUB
    assert "Domain Plugin Layer" not in prompt


def test_prevents_self_stack_when_core_base_passed_explicitly(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    home = _make_minimal_home(tmp_path)
    _configure_home(monkeypatch, home)

    loader = ConfigLoader()
    prompt_none = loader.load_system_prompt(None)
    prompt_self = loader.load_system_prompt(home / "plugins" / "core-base")

    assert prompt_self == prompt_none


def test_stacks_when_domain_plugin_passed(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    home = _make_minimal_home(tmp_path)
    plugin_root = _make_indoor_layout_plugin(home)
    _configure_home(monkeypatch, home)

    prompt = ConfigLoader().load_system_prompt(plugin_root)

    assert prompt.startswith(CORE_BASE_STUB)
    assert "## Domain Plugin Layer · indoor-layout" in prompt
    assert prompt.endswith(PLUGIN_STUB)


def test_raises_when_core_base_prompt_missing(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    home = _make_minimal_home(tmp_path)
    _configure_home(monkeypatch, home)

    loader = ConfigLoader()
    (home / "plugins" / "core-base" / "BIMCANVAS.md").unlink()

    with pytest.raises(FileNotFoundError, match="core-base/BIMCANVAS.md 是平台基座"):
        loader.load_system_prompt(None)


def test_raises_when_active_plugin_prompt_missing(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    home = _make_minimal_home(tmp_path)
    plugin_root = _make_indoor_layout_plugin(home, with_prompt=False)
    _configure_home(monkeypatch, home)

    with pytest.raises(FileNotFoundError, match="active plugin BIMCANVAS.md 缺失"):
        ConfigLoader().load_system_prompt(plugin_root)
