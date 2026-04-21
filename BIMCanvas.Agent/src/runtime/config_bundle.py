"""Shared runtime configuration bundle consumed by host adapters."""

from __future__ import annotations

import re
from dataclasses import dataclass
from pathlib import Path

import yaml

from ..config.loader import AgentConfig, get_config_loader
from ..mcp import CANVAS_ALLOWED_TOOLS

_FRONTMATTER_RE = re.compile(r"^---\r?\n([\s\S]*?)\r?\n---")


@dataclass(frozen=True)
class SkillMeta:
    """Skill metadata extracted from SKILL.md frontmatter."""

    name: str
    description: str
    path: Path


@dataclass(frozen=True)
class ConfigBundle:
    """Shared configuration snapshot built once by the host layer."""

    system_prompt: str
    shared_agents: dict[str, AgentConfig]
    skill_index: dict[str, Path]
    skill_metas: dict[str, SkillMeta]
    permissions_allow: list[str] | None
    permissions_deny: list[str]
    mcp_tool_names: tuple[str, ...]
    bimcanvas_home: Path


def _parse_skill_frontmatter(skill_path: Path) -> dict[str, str]:
    content = skill_path.read_text(encoding="utf-8-sig")
    m = _FRONTMATTER_RE.match(content)
    if not m:
        raise ValueError(f"No YAML frontmatter in {skill_path}")
    return yaml.safe_load(m.group(1))


def strip_skill_frontmatter(content: str) -> str:
    m = re.match(r"^---\r?\n[\s\S]*?\r?\n---\r?\n?", content)
    return content[len(m.group(0)):].strip() if m else content.strip()


def _build_skill_index(config_root: Path) -> tuple[dict[str, Path], dict[str, SkillMeta]]:
    skills_dir = config_root / "skills"
    skill_index: dict[str, Path] = {}
    skill_metas: dict[str, SkillMeta] = {}

    for entry in sorted(skills_dir.iterdir(), key=lambda item: item.name.lower()):
        if not entry.is_dir():
            continue
        skill_path = (entry / "SKILL.md").resolve()
        if not skill_path.is_file():
            raise FileNotFoundError(f"Missing skill file: {skill_path}")
        skill_index[entry.name] = skill_path
        fm = _parse_skill_frontmatter(skill_path)
        skill_metas[entry.name] = SkillMeta(
            name=fm.get("name", entry.name),
            description=fm.get("description", "").strip(),
            path=skill_path,
        )

    return skill_index, skill_metas


def build_config_bundle() -> ConfigBundle:
    """Build a fresh host-facing config bundle from BIMCANVAS_HOME."""

    loader = get_config_loader()
    permissions_allow, permissions_deny = loader.load_permissions()
    bimcanvas_home = loader.config_dir.resolve()
    skill_index, skill_metas = _build_skill_index(bimcanvas_home)

    return ConfigBundle(
        system_prompt=loader.load_system_prompt(),
        shared_agents=dict(loader.load_agents()),
        skill_index=skill_index,
        skill_metas=skill_metas,
        permissions_allow=list(permissions_allow) if permissions_allow is not None else None,
        permissions_deny=list(permissions_deny or []),
        mcp_tool_names=tuple(CANVAS_ALLOWED_TOOLS),
        bimcanvas_home=bimcanvas_home,
    )
