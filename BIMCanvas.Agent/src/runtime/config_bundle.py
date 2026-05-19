"""Shared runtime configuration bundle consumed by host adapters.

组3 改造 (主真理源 v1.1 §3.4 + 组3 任务模板 §4.3):
- ConfigBundle 新增 launch_context / active_plugin_* / mcp_servers_spec / diagnostics 字段
- _build_skill_index 支持 active plugin 同名覆盖 (v3.7 silent override + logger.info 记录)
- 新增 _build_mcp_servers:始终注册 core "canvas" server,active plugin 加载失败时
  diagnostics 追加 + 该 plugin disable,不影响 core (V11 T3)
- 新增 to_snapshot_dict:Golden Snapshot 测试用
"""

from __future__ import annotations

import importlib.util
import logging
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import TYPE_CHECKING, Any

import yaml

from ..config.loader import AgentConfig, get_config_loader
from ..mcp.canvas_core import CORE_ALLOWED_TOOLS, build_core_server

if TYPE_CHECKING:
    import aiohttp

    from .launch_context import PluginLaunchContext

logger = logging.getLogger(__name__)

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
    # 工具权限重设计 v3.2 §4 / §7.1:
    # - tools_allow: 主控允许工具列表 (空 list = SDK 全开,直接传给 ClaudeAgentOptions.allowed_tools)
    # - tools_deny: 主控禁止工具列表 (deny 优先于 allow,跟随 SDK 语义)
    # - agents_allow / agents_deny: SubAgent 装配开关 (build 时已应用过滤到 shared_agents)
    tools_allow: list[str]
    tools_deny: list[str]
    agents_allow: list[str]
    agents_deny: list[str]
    mcp_tool_names: tuple[str, ...]
    bimcanvas_home: Path
    # 组3 新增字段 (主真理源 v1.1 §3.4 五层投影)
    launch_context: "PluginLaunchContext"
    active_plugin_id: str | None
    active_plugin_root: Path | None
    active_plugin_paths: tuple[Path, ...] = field(default_factory=tuple)
    """传给 ClaudeAgentOptions.plugins=[{type:local, path}] 的目录列表
    (BIMCANVAS_HOME 本身 + active plugin root)。"""
    mcp_servers_spec: dict[str, Any] = field(default_factory=dict)
    """mcp_servers dict: {namespace: McpServer}。始终含 "canvas" key。"""
    diagnostics: tuple[str, ...] = field(default_factory=tuple)
    """plugin 加载告警 / overrides 决议日志,供 Web settings 显示与 plugin 作者调试。"""

    def to_snapshot_dict(self) -> dict[str, Any]:
        """生成稳定的 dict 形态供 Golden Snapshot 测试 diff 使用。

        排除:
        - Path 对象转相对 bimcanvas_home 的字符串 (跨环境稳定)
        - mcp_servers_spec 提取 namespace + tool 名,不含 server 对象本身
        - system_prompt 取 SHA1 hex (前 200 字符,防全文 diff 噪声)
        - launch_context 取关键字段而非全量序列化
        """
        import hashlib

        def rel(p: Path | None) -> str | None:
            if p is None:
                return None
            try:
                return str(p.resolve().relative_to(self.bimcanvas_home.resolve())).replace("\\", "/")
            except ValueError:
                return str(p).replace("\\", "/")

        prompt_hash = hashlib.sha1(
            self.system_prompt[:200].encode("utf-8")
        ).hexdigest()

        # mcp_servers_spec 中 server dict 结构 {type, name, instance},不直接含 tools 列表。
        # snapshot 从 mcp_tool_names ("mcp__ns__tool") 反推 namespace → tools 映射。
        mcp_servers_snapshot: dict[str, list[str]] = {
            ns: [] for ns in self.mcp_servers_spec.keys()
        }
        for full_name in self.mcp_tool_names:
            if not full_name.startswith("mcp__"):
                continue
            parts = full_name.split("__", 2)
            if len(parts) != 3:
                continue
            _, namespace, tool_name = parts
            mcp_servers_snapshot.setdefault(namespace, []).append(tool_name)
        mcp_servers_snapshot = {k: sorted(v) for k, v in mcp_servers_snapshot.items()}

        return {
            "launch_mode": self.launch_context.mode.value,
            "active_plugin_id": self.active_plugin_id,
            "active_plugin_root": rel(self.active_plugin_root),
            "active_plugin_paths": sorted(rel(p) for p in self.active_plugin_paths if p is not None),
            "system_prompt_sha1_prefix": prompt_hash,
            "system_prompt_length": len(self.system_prompt),
            "agents_keys": sorted(self.shared_agents.keys()),
            "skill_names": sorted(self.skill_index.keys()),
            "mcp_servers": {k: mcp_servers_snapshot[k] for k in sorted(mcp_servers_snapshot.keys())},
            "mcp_tool_names": sorted(self.mcp_tool_names),
            "tools_allow": sorted(self.tools_allow),
            "tools_deny": sorted(self.tools_deny),
            "agents_allow": sorted(self.agents_allow),
            "agents_deny": sorted(self.agents_deny),
            "diagnostics": list(self.diagnostics),
            "scenes_count": len(self.launch_context.scenes.scenes) if self.launch_context.scenes else 0,
            "active_scene_id": self.launch_context.active_scene_id,
        }


def _parse_skill_frontmatter(skill_path: Path) -> dict[str, str]:
    content = skill_path.read_text(encoding="utf-8-sig")
    m = _FRONTMATTER_RE.match(content)
    if not m:
        raise ValueError(f"No YAML frontmatter in {skill_path}")
    return yaml.safe_load(m.group(1))


def strip_skill_frontmatter(content: str) -> str:
    m = re.match(r"^---\r?\n[\s\S]*?\r?\n---\r?\n?", content)
    return content[len(m.group(0)):].strip() if m else content.strip()


def _scan_skill_dir(
    skills_dir: Path,
    skill_index: dict[str, Path],
    skill_metas: dict[str, SkillMeta],
) -> None:
    """扫描 skills 目录,把每个子目录的 SKILL.md 录入 (后写入者覆盖)。"""
    if not skills_dir.is_dir():
        return
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


def _build_skill_index(
    config_root: Path,
    active_plugin_root: Path | None = None,
) -> tuple[dict[str, Path], dict[str, SkillMeta], list[str]]:
    """构建 skill 索引,合并 base + active plugin 两层。

    v3.7 silent override:plugin skill 与 base 同名时默认覆盖 base 同名,不再要求
    manifest 显式声明 overrides。覆盖决定通过 logger.info + diagnostics 双重记录,
    供 Server 日志审计 + Web settings 展示 + plugin 作者调试。

    Args:
        config_root: BIMCANVAS_HOME / core-base skill 根
        active_plugin_root: active plugin 物理目录;None 时只扫 base

    Returns:
        (skill_index, skill_metas, diagnostics)
    """
    skill_index: dict[str, Path] = {}
    skill_metas: dict[str, SkillMeta] = {}
    diagnostics: list[str] = []

    base_skills_dir = config_root / "skills"
    _scan_skill_dir(base_skills_dir, skill_index, skill_metas)
    base_names = set(skill_index.keys())

    if active_plugin_root is not None:
        plugin_skills_dir = active_plugin_root / "skills"
        if plugin_skills_dir.is_dir():
            for entry in sorted(plugin_skills_dir.iterdir(), key=lambda i: i.name.lower()):
                if not entry.is_dir():
                    continue
                skill_path = (entry / "SKILL.md").resolve()
                if not skill_path.is_file():
                    diagnostics.append(f"plugin skill 缺少 SKILL.md: {skill_path}")
                    continue
                skill_index[entry.name] = skill_path
                fm = _parse_skill_frontmatter(skill_path)
                skill_metas[entry.name] = SkillMeta(
                    name=fm.get("name", entry.name),
                    description=fm.get("description", "").strip(),
                    path=skill_path,
                )
                if entry.name in base_names:
                    msg = (
                        f"plugin skill '{entry.name}' (来自 {active_plugin_root.name}) "
                        f"覆盖 core-base 同名 skill"
                    )
                    logger.info(msg)
                    diagnostics.append(msg)

    return skill_index, skill_metas, diagnostics


def _build_mcp_servers(
    launch_context: "PluginLaunchContext",
    active_plugin_root: Path | None,
    plugin_manifest: dict,
    session: "aiohttp.ClientSession | None",
) -> tuple[dict[str, Any], list[str], list[str]]:
    """构建 mcp_servers dict + 聚合工具名 + diagnostics。

    主真理源 v1.1 §3.8 / 组3 任务模板 §4.3。

    Returns:
        (mcp_servers_spec, tool_names, diagnostics)

    Raises:
        PluginRegisterError: namespace 冲突 (fail-fast,V11 T2)
    """
    from bimcanvas_plugin_sdk import (
        McpServerBuilder,
        PluginContext,
        PluginRegisterError,
    )

    diagnostics: list[str] = []
    core_server = build_core_server(launch_context, session)
    servers: dict[str, Any] = {"canvas": core_server}
    tool_names: list[str] = list(CORE_ALLOWED_TOOLS)

    if (
        active_plugin_root is None
        or not plugin_manifest
        or not plugin_manifest.get("mcpTools")
    ):
        return servers, tool_names, diagnostics

    plugin_id = launch_context.active_plugin_id or active_plugin_root.name
    namespace = plugin_manifest.get("mcpNamespace") or plugin_id

    if namespace == "canvas":
        raise PluginRegisterError(
            f"plugin {plugin_id} 的 mcpNamespace 不能为 'canvas' (保留给 core-base)"
        )
    if namespace in servers:
        raise PluginRegisterError(
            f"plugin {plugin_id} 的 mcpNamespace '{namespace}' 与已注册 server 冲突"
        )

    entry_relpath = plugin_manifest["mcpTools"]
    entry_path = (active_plugin_root / entry_relpath).resolve()

    if not entry_path.is_file():
        diagnostics.append(
            f"plugin {plugin_id} 的 mcpTools 文件不存在: {entry_path};该 plugin 被 disable"
        )
        return servers, tool_names, diagnostics

    plugin_ctx = PluginContext(
        server_url=launch_context.server_url,
        project_path=launch_context.project_path,
        active_plugin_id=plugin_id,
        active_scene_id=launch_context.active_scene_id,
        logger=logging.getLogger(f"bimcanvas.plugin.{plugin_id}"),
        session=session,
    )

    builder = McpServerBuilder(namespace=namespace, context=plugin_ctx)

    module_name = f"bimcanvas_plugin_{plugin_id.replace('-', '_')}_entry"
    try:
        spec = importlib.util.spec_from_file_location(module_name, entry_path)
        if spec is None or spec.loader is None:
            raise PluginRegisterError(
                f"无法构造 module spec: {entry_path}"
            )
        module = importlib.util.module_from_spec(spec)
        sys.modules[module_name] = module
        spec.loader.exec_module(module)

        register_fn = getattr(module, "register", None)
        if register_fn is None or not callable(register_fn):
            raise PluginRegisterError(
                f"plugin {plugin_id} 的 mcp_tools/{entry_relpath} 缺少 `register(builder)` 入口函数"
            )
        register_fn(builder)
    except PluginRegisterError:
        raise
    except Exception as exc:  # noqa: BLE001 - 其他异常隔离为 plugin disable
        diagnostics.append(
            f"plugin {plugin_id} 加载失败 ({type(exc).__name__}: {exc});该 plugin 被 disable"
        )
        sys.modules.pop(module_name, None)
        logger.warning("plugin %s 加载失败,已 disable", plugin_id, exc_info=True)
        return servers, tool_names, diagnostics

    plugin_server = builder.build()
    servers[namespace] = plugin_server
    tool_names.extend(builder.tool_names)
    diagnostics.append(
        f"plugin {plugin_id} 已加载: namespace={namespace}, tools={list(builder.tool_names)}"
    )

    return servers, tool_names, diagnostics


def build_config_bundle(
    launch_context: "PluginLaunchContext | None" = None,
    session: "aiohttp.ClientSession | None" = None,
) -> ConfigBundle:
    """Build a fresh host-facing config bundle.

    组3 改造 (主真理源 v1.1 §3.4 五层投影):
    1. resolve_launch_context (若未传入)
    2. resolve_active_plugin (取 plugin root + manifest + overrides)
    3. 加载 system_prompt + agents + skills,各自合并 base + active plugin
    4. _build_mcp_servers 构造 {canvas, [active-plugin-ns]} dict
    5. permissions allow 聚合所有 mcp 工具名

    Args:
        launch_context: 已注入的 PluginLaunchContext;为 None 时调 resolve_launch_context
        session: long-lived aiohttp session,供 plugin 工具使用;为 None 时 plugin 网络工具不可用
    """
    if launch_context is None:
        from .launch_context import resolve_launch_context

        launch_context = resolve_launch_context()

    loader = get_config_loader()
    tools_cfg = loader.load_tools_config()
    bimcanvas_home = loader.config_dir.resolve()

    (
        active_plugin_root,
        plugin_manifest,
    ) = loader.resolve_active_plugin(launch_context)

    system_prompt = loader.load_system_prompt(active_plugin_root)

    # 工具权限重设计 v3.2 §7.3:对已加载 SubAgent 应用 agents.allow / deny 过滤
    # - agents.allow 非空 → 白名单模式;agents.allow 空 → 全部已加载 SubAgent 都通过
    # - 再应用 deny (与 SDK 处理 tools 一致,deny 后于 allow 生效但优先级更高)
    loaded_agents = dict(loader.load_agents(active_plugin_root))
    loaded_names = set(loaded_agents.keys())
    if tools_cfg.agents_allow:
        effective_names = {n for n in loaded_names if n in tools_cfg.agents_allow}
    else:
        effective_names = loaded_names
    effective_names -= set(tools_cfg.agents_deny)
    shared_agents = {n: loaded_agents[n] for n in effective_names}

    dropped = loaded_names - set(shared_agents.keys())
    if dropped:
        logger.info(
            "agents 过滤后,以下 SubAgent 不会被装配: %s "
            "(agents.allow=%s, agents.deny=%s)",
            sorted(dropped), tools_cfg.agents_allow, tools_cfg.agents_deny,
        )

    # 主真理源 v1.2 §3.5 折中方案:core-base skills 在新布局下从 plugins/core-base/skills/ 读
    skill_index, skill_metas, skill_diag = _build_skill_index(
        loader._resolve_core_base_root(), active_plugin_root
    )

    mcp_servers, mcp_tool_names, mcp_diag = _build_mcp_servers(
        launch_context, active_plugin_root, plugin_manifest, session
    )

    # plugins=[...] 路径列表 (主真理源 §3.7 + 组3 任务模板 §4.4):
    # BIMCANVAS_HOME 本身 (core-base / 旧布局 base) + active plugin root
    active_plugin_paths: list[Path] = [bimcanvas_home]
    if active_plugin_root is not None and active_plugin_root != bimcanvas_home:
        active_plugin_paths.append(active_plugin_root)

    diagnostics = tuple(skill_diag + mcp_diag)

    return ConfigBundle(
        system_prompt=system_prompt,
        shared_agents=shared_agents,
        skill_index=skill_index,
        skill_metas=skill_metas,
        tools_allow=list(tools_cfg.tools_allow),
        tools_deny=list(tools_cfg.tools_deny),
        agents_allow=list(tools_cfg.agents_allow),
        agents_deny=list(tools_cfg.agents_deny),
        mcp_tool_names=tuple(mcp_tool_names),
        bimcanvas_home=bimcanvas_home,
        launch_context=launch_context,
        active_plugin_id=launch_context.active_plugin_id,
        active_plugin_root=active_plugin_root,
        active_plugin_paths=tuple(active_plugin_paths),
        mcp_servers_spec=mcp_servers,
        diagnostics=diagnostics,
    )
