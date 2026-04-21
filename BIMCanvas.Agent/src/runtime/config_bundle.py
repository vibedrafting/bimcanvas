"""Shared runtime configuration bundle consumed by host adapters."""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

from ..config.loader import AgentConfig, get_config_loader
from ..mcp import CANVAS_ALLOWED_TOOLS


@dataclass(frozen=True)
class ConfigBundle:
    """Shared configuration snapshot built once by the host layer."""

    system_prompt: str
    shared_agents: dict[str, AgentConfig]
    skill_index: dict[str, Path]
    permissions_allow: list[str] | None
    permissions_deny: list[str]
    mcp_tool_names: tuple[str, ...]
    bimcanvas_home: Path


def _build_skill_index(config_root: Path) -> dict[str, Path]:
    skills_dir = config_root / "skills"
    skill_index: dict[str, Path] = {}

    for entry in sorted(skills_dir.iterdir(), key=lambda item: item.name.lower()):
        if not entry.is_dir():
            continue
        skill_path = (entry / "SKILL.md").resolve()
        if not skill_path.is_file():
            raise FileNotFoundError(f"Missing skill file: {skill_path}")
        skill_index[entry.name] = skill_path

    return skill_index


def build_config_bundle() -> ConfigBundle:
    """Build a fresh host-facing config bundle from BIMCANVAS_HOME."""

    loader = get_config_loader()
    permissions_allow, permissions_deny = loader.load_permissions()
    bimcanvas_home = loader.config_dir.resolve()

    return ConfigBundle(
        system_prompt=loader.load_system_prompt(),
        shared_agents=dict(loader.load_agents()),
        skill_index=_build_skill_index(bimcanvas_home),
        permissions_allow=list(permissions_allow) if permissions_allow is not None else None,
        permissions_deny=list(permissions_deny or []),
        mcp_tool_names=tuple(CANVAS_ALLOWED_TOOLS),
        bimcanvas_home=bimcanvas_home,
    )
