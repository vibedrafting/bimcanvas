"""PluginLaunchContext Python 端镜像 (主真理源 v1.1 §3.3 / 组3 任务模板 §4.4)。

字段一一对应 C# `BIMCanvas.Server.Models.Plugins.PluginLaunchContext` record,
反序列化时 camelCase → snake_case。enum 序列化值与 C# CamelCaseEnumConverter 一致
(LaunchMode.Projectless → "projectless" / LaunchMode.ProjectBound → "projectBound" 等)。

不可变 (V14 T10):所有 record / dataclass 均 frozen。

注入路径 (主真理源 §3.3):
- 组2 Server 启动 Python 子进程前,把序列化 JSON 写入临时文件,
  路径写入环境变量 BIMCANVAS_LAUNCH_CONTEXT;
- Python 端 resolve_launch_context() 读取 → 解析 → os.unlink 删除文件;
- 三段式 fallback 见 resolve_launch_context() 文档。
"""

from __future__ import annotations

import json
import logging
import os
from dataclasses import dataclass, field
from enum import Enum
from pathlib import Path
from typing import Optional

logger = logging.getLogger(__name__)


class LaunchMode(str, Enum):
    """对应 C# LaunchMode enum,camelCase 序列化值。"""

    PROJECTLESS = "projectless"
    PROJECT_BOUND = "projectBound"


class TrustMode(str, Enum):
    """对应 C# TrustMode enum。Phase 1 实际只会 FULL_TRUST。"""

    FULL_TRUST = "fullTrust"
    UNTRUSTED = "untrusted"


class SceneStatus(str, Enum):
    """对应 C# SceneStatus enum。Phase 1 只 ACTIVE 合法。"""

    ACTIVE = "active"


class SourceKind(str, Enum):
    """对应 C# SourceKind enum。"""

    GITHUB = "github"
    LOCAL = "local"
    ZIP = "zip"


@dataclass(frozen=True)
class ScenePluginRef:
    """project.json.scenes[].plugin (C# ScenePluginRef)。"""

    id: str
    version_range: str


@dataclass(frozen=True)
class ProjectScene:
    """project.json.scenes[] 单项 (C# ProjectScene)。"""

    scene_id: str
    scene: str
    plugin: ScenePluginRef
    status: SceneStatus
    created_at: str  # ISO 8601,Python 端不强制 datetime 化


@dataclass(frozen=True)
class ProjectScenesSummary:
    """scenes 快照 (C# ProjectScenesSummary)。"""

    scenes: tuple[ProjectScene, ...]
    active_scene_id: Optional[str]


@dataclass(frozen=True)
class PluginLockSummary:
    """plugins.lock.json 中 active scene 的 lock 投影 (C# PluginLockSummary)。"""

    plugin_id: str
    version: str
    source_url: Optional[str]
    resolved_commit: Optional[str]
    source_kind: SourceKind
    manifest_checksum: str
    scaffold_checksum: Optional[str]
    trusted_at: Optional[str]
    installed_at: str


@dataclass(frozen=True)
class PluginLaunchContext:
    """Agent 子进程启动上下文 (C# PluginLaunchContext)。

    字段顺序与 C# record positional 参数一致。Projectless 时 project_path /
    active_scene_id / scenes / lock 必须为 None;ProjectBound 时必须非 None。
    校验在 __post_init__ 中执行,违反抛 ValueError (V14 T12)。
    """

    active_plugin_id: Optional[str]
    active_plugin_root: Optional[str]
    mode: LaunchMode
    project_path: Optional[str]
    active_scene_id: Optional[str]
    scenes: Optional[ProjectScenesSummary]
    lock: Optional[PluginLockSummary]
    server_url: str
    trust_mode: TrustMode
    read_only_scene_ids: tuple[str, ...] = field(default_factory=tuple)

    def __post_init__(self) -> None:
        if self.mode is LaunchMode.PROJECT_BOUND:
            if self.project_path is None:
                raise ValueError("LaunchMode.PROJECT_BOUND 时 project_path 必须非空")
            if self.active_scene_id is None:
                raise ValueError("LaunchMode.PROJECT_BOUND 时 active_scene_id 必须非空")
            if self.scenes is None:
                raise ValueError("LaunchMode.PROJECT_BOUND 时 scenes 必须非空")
            if self.active_plugin_id is None:
                raise ValueError("LaunchMode.PROJECT_BOUND 时 active_plugin_id 必须非空")
        elif self.mode is LaunchMode.PROJECTLESS:
            if self.project_path is not None:
                raise ValueError("LaunchMode.PROJECTLESS 时 project_path 必须为 None")
            if self.active_scene_id is not None:
                raise ValueError("LaunchMode.PROJECTLESS 时 active_scene_id 必须为 None")
            if self.scenes is not None:
                raise ValueError("LaunchMode.PROJECTLESS 时 scenes 必须为 None")


# ---------- 反序列化 ----------


def _normalize_str(value) -> Optional[str]:
    """C# 端 non-nullable string 字段可能传空字符串,Python 端统一视为 None。"""
    if value is None:
        return None
    if isinstance(value, str) and value == "":
        return None
    return value


def _parse_scene_plugin_ref(data: dict) -> ScenePluginRef:
    return ScenePluginRef(id=data["id"], version_range=data["versionRange"])


def _parse_project_scene(data: dict) -> ProjectScene:
    return ProjectScene(
        scene_id=data["sceneId"],
        scene=data["scene"],
        plugin=_parse_scene_plugin_ref(data["plugin"]),
        status=SceneStatus(data["status"]),
        created_at=data["createdAt"],
    )


def _parse_scenes_summary(data: Optional[dict]) -> Optional[ProjectScenesSummary]:
    if data is None:
        return None
    return ProjectScenesSummary(
        scenes=tuple(_parse_project_scene(s) for s in data.get("scenes", [])),
        active_scene_id=_normalize_str(data.get("activeSceneId")),
    )


def _parse_lock_summary(data: Optional[dict]) -> Optional[PluginLockSummary]:
    if data is None:
        return None
    return PluginLockSummary(
        plugin_id=data["pluginId"],
        version=data["version"],
        source_url=_normalize_str(data.get("sourceUrl")),
        resolved_commit=_normalize_str(data.get("resolvedCommit")),
        source_kind=SourceKind(data["sourceKind"]),
        manifest_checksum=data["manifestChecksum"],
        scaffold_checksum=_normalize_str(data.get("scaffoldChecksum")),
        trusted_at=_normalize_str(data.get("trustedAt")),
        installed_at=data["installedAt"],
    )


def parse_launch_context(data: dict) -> PluginLaunchContext:
    """从已解析的 dict (camelCase) 构造 PluginLaunchContext。"""
    return PluginLaunchContext(
        active_plugin_id=_normalize_str(data.get("activePluginId")),
        active_plugin_root=_normalize_str(data.get("activePluginRoot")),
        mode=LaunchMode(data["mode"]),
        project_path=_normalize_str(data.get("projectPath")),
        active_scene_id=_normalize_str(data.get("activeSceneId")),
        scenes=_parse_scenes_summary(data.get("scenes")),
        lock=_parse_lock_summary(data.get("lock")),
        server_url=data["serverUrl"],
        trust_mode=TrustMode(data["trustMode"]),
        read_only_scene_ids=tuple(data.get("readOnlySceneIds", []) or []),
    )


def from_json_file(path: Path) -> PluginLaunchContext:
    """读取 JSON 文件 (UTF-8, 兼容 BOM) 并解析为 PluginLaunchContext。

    注:Server 端用 System.Text.Json + File.WriteAllText 写文件可能带 UTF-8 BOM,
    用 utf-8-sig 编码自动跳过 BOM,兼容带/不带 BOM 两种情况。
    """
    with path.open("r", encoding="utf-8-sig") as f:
        data = json.load(f)
    return parse_launch_context(data)


# ---------- 三段式 fallback ----------


def _build_projectless_fallback(active_plugin_id: Optional[str]) -> PluginLaunchContext:
    """根据 BIMCANVAS_ACTIVE_PLUGIN 或 None 构造 Projectless 模式 context。

    BIMCANVAS_HOME / BIMCANVAS_SERVER_URL 必须由 Server (或开发者) 已注入;
    缺失时返回 fallback 默认值 (http://127.0.0.1:0)。
    """
    server_url = os.getenv("BIMCANVAS_SERVER_URL", "").strip() or "http://127.0.0.1:0"

    bimcanvas_home = os.getenv("BIMCANVAS_HOME", "").strip()
    active_plugin_root: Optional[str] = None
    if active_plugin_id and bimcanvas_home:
        candidate = Path(bimcanvas_home) / "plugins" / active_plugin_id
        if candidate.exists():
            active_plugin_root = str(candidate.resolve())

    return PluginLaunchContext(
        active_plugin_id=active_plugin_id,
        active_plugin_root=active_plugin_root,
        mode=LaunchMode.PROJECTLESS,
        project_path=None,
        active_scene_id=None,
        scenes=None,
        lock=None,
        server_url=server_url,
        trust_mode=TrustMode.FULL_TRUST,
        read_only_scene_ids=(),
    )


def resolve_launch_context() -> PluginLaunchContext:
    """三段式 fallback (主真理源 v1.1 §3.3 + 组3 计划 §1-D):

    priority 1: BIMCANVAS_LAUNCH_CONTEXT 环境变量指向 JSON 文件
                → 读 + 解析 + os.unlink 删除文件
    priority 2: BIMCANVAS_ACTIVE_PLUGIN 环境变量 (plugin id)
                → 构造 Projectless context (active_plugin_id 设为该值)
    priority 3: 都没
                → Projectless context, active_plugin_id = None (legacy / fresh install)
    """
    launch_path_str = os.getenv("BIMCANVAS_LAUNCH_CONTEXT", "").strip()
    if launch_path_str:
        launch_path = Path(launch_path_str)
        if not launch_path.exists():
            logger.warning(
                "BIMCANVAS_LAUNCH_CONTEXT 指向的文件不存在,回退 Projectless: %s",
                launch_path,
            )
        else:
            try:
                ctx = from_json_file(launch_path)
            except (json.JSONDecodeError, KeyError, ValueError) as exc:
                logger.error(
                    "解析 BIMCANVAS_LAUNCH_CONTEXT 文件失败 (%s),回退 Projectless: %s",
                    exc,
                    launch_path,
                )
            else:
                try:
                    launch_path.unlink()
                except OSError as exc:
                    logger.warning(
                        "无法删除 LaunchContext 临时文件 %s: %s", launch_path, exc
                    )
                logger.info("LaunchContext 已注入: mode=%s, active=%s",
                            ctx.mode.value, ctx.active_plugin_id)
                return ctx

    legacy_active = os.getenv("BIMCANVAS_ACTIVE_PLUGIN", "").strip() or None
    return _build_projectless_fallback(legacy_active)
