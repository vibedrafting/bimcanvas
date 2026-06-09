"""PluginLaunchContext Python 端镜像 (主真理源 v1.1 §3.3,「项目去插件态」后精简)。

字段一一对应 C# `BIMCanvas.Server.Models.Plugins.PluginLaunchContext` record,
反序列化时 camelCase → snake_case。enum 序列化值与 C# CamelCaseEnumConverter 一致
(LaunchMode.Projectless → "projectless" / LaunchMode.ProjectBound → "projectBound")。

北极星(项目 = 被动数据基底,不记录哪个插件执行过):LaunchContext 只携带
「active plugin 身份 + 运行模式 + 项目路径 + Server 回调地址」,**不再携带
scenes / activeSceneId / lock / readOnlySceneIds**。

不可变:所有 dataclass frozen。

注入路径 (主真理源 §3.3):
- Server 启动 Python 子进程前,把序列化 JSON 写入临时文件,
  路径写入环境变量 BIMCANVAS_LAUNCH_CONTEXT;
- Python 端 resolve_launch_context() 读取 → 解析 → os.unlink 删除文件;
- 三段式 fallback 见 resolve_launch_context() 文档。
"""

from __future__ import annotations

import json
import logging
import os
from dataclasses import dataclass, replace
from enum import Enum
from pathlib import Path
from typing import Optional

logger = logging.getLogger(__name__)

# 进程级缓存:首次成功 resolve 到含 active_plugin_id 的注入 context 后填充。
# Why: 注入文件一次性(resolve 读后 os.unlink),Server 不设 BIMCANVAS_ACTIVE_PLUGIN env;
# 若每次 build_config_bundle 都重新 resolve,第二次起 fallback 到 active_plugin_id=None,
# ProjectBound 无法构造。active plugin 身份 / server_url / trust_mode 在 Agent 进程生命周期内
# 不变(平台化=一进程一激活 plugin),仅 project_path 随窗口/项目变,故缓存 base 供
# build_project_bound_context 复用,只用 HTTP 传来的 project_path override。
_cached_base: Optional["PluginLaunchContext"] = None


class LaunchMode(str, Enum):
    """对应 C# LaunchMode enum,camelCase 序列化值。"""

    PROJECTLESS = "projectless"
    PROJECT_BOUND = "projectBound"


class TrustMode(str, Enum):
    """对应 C# TrustMode enum。Phase 1 实际只会 FULL_TRUST。"""

    FULL_TRUST = "fullTrust"
    UNTRUSTED = "untrusted"


@dataclass(frozen=True)
class PluginLaunchContext:
    """Agent 子进程启动上下文 (C# PluginLaunchContext)。

    字段顺序与 C# record positional 参数一致。Projectless 时 project_path 为 None;
    ProjectBound 时必须非 None。校验在 __post_init__ 中执行,违反抛 ValueError。
    """

    active_plugin_id: Optional[str]
    active_plugin_root: Optional[str]
    mode: LaunchMode
    project_path: Optional[str]
    server_url: str
    trust_mode: TrustMode

    def __post_init__(self) -> None:
        if self.mode is LaunchMode.PROJECT_BOUND:
            if self.project_path is None:
                raise ValueError("LaunchMode.PROJECT_BOUND 时 project_path 必须非空")
            if self.active_plugin_id is None:
                raise ValueError("LaunchMode.PROJECT_BOUND 时 active_plugin_id 必须非空")
        elif self.mode is LaunchMode.PROJECTLESS:
            if self.project_path is not None:
                raise ValueError("LaunchMode.PROJECTLESS 时 project_path 必须为 None")


# ---------- 反序列化 ----------


def _normalize_str(value) -> Optional[str]:
    """C# 端 non-nullable string 字段可能传空字符串,Python 端统一视为 None。"""
    if value is None:
        return None
    if isinstance(value, str) and value == "":
        return None
    return value


def parse_launch_context(data: dict) -> PluginLaunchContext:
    """从已解析的 dict (camelCase) 构造 PluginLaunchContext。

    note:旧版本的 scenes / activeSceneId / lock / readOnlySceneIds 字段即使仍出现在
    JSON 中也会被忽略(只取本 record 的 6 个字段),保证新旧 launch-context 文件兼容。
    """
    return PluginLaunchContext(
        active_plugin_id=_normalize_str(data.get("activePluginId")),
        active_plugin_root=_normalize_str(data.get("activePluginRoot")),
        mode=LaunchMode(data["mode"]),
        project_path=_normalize_str(data.get("projectPath")),
        server_url=data["serverUrl"],
        trust_mode=TrustMode(data["trustMode"]),
    )


def from_json_file(path: Path) -> PluginLaunchContext:
    """读取 JSON 文件 (UTF-8, 兼容 BOM) 并解析为 PluginLaunchContext。

    注:Server 端写文件可能带 UTF-8 BOM,用 utf-8-sig 编码自动跳过 BOM。
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
        server_url=server_url,
        trust_mode=TrustMode.FULL_TRUST,
    )


def _set_cached_base(ctx: "PluginLaunchContext") -> None:
    """只增不毁:仅当 ctx 含非空 active_plugin_id 时填充进程级缓存,避免后续 fallback 的
    None 覆盖已缓存的有效身份。"""
    global _cached_base
    if ctx.active_plugin_id:
        _cached_base = ctx


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
                _set_cached_base(ctx)
                return ctx

    legacy_active = os.getenv("BIMCANVAS_ACTIVE_PLUGIN", "").strip() or None
    return _build_projectless_fallback(legacy_active)


def build_project_bound_context(project_path: str) -> PluginLaunchContext:
    """构造 ProjectBound launch context(对称 _build_projectless_fallback)。

    真因:project_path 不在注入文件里(注入文件本身就是 mode=projectless,只含 active
    plugin 身份),它只经 create_agent / MainAgent 的请求参数到达。本工厂用进程级缓存的
    base 身份(active_plugin_id / active_plugin_root / server_url / trust_mode,来自注入
    文件)+ 传入的 project_path override 成 PROJECT_BOUND。

    缓存为空时先触发一次 resolve_launch_context()(消费注入文件并填缓存);仍拿不到
    active_plugin_id 则 fail-fast(逃生口语义:缓存无源 = Server 未注入 active plugin)。

    Raises:
        RuntimeError: 缓存无源(无 active_plugin_id),ProjectBound 不可构造。
    """
    base = _cached_base
    if base is None:
        base = resolve_launch_context()
    if base is None or base.active_plugin_id is None:
        raise RuntimeError(
            "build_project_bound_context: 无 active_plugin_id"
            "(注入文件未提供 active plugin 且 BIMCANVAS_ACTIVE_PLUGIN env 未设),"
            "ProjectBound 不可构造,fail-fast。"
        )
    # replace 触发 __post_init__ 校验:PROJECT_BOUND 要求 project_path / active_plugin_id
    # 非空,二者此处均满足,不会抛。
    return replace(
        base,
        mode=LaunchMode.PROJECT_BOUND,
        project_path=project_path,
    )
