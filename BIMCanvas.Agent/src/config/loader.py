"""Configuration loader for BIMCanvas Agent."""

import os
import re
import json
import logging
from pathlib import Path
from dataclasses import dataclass
from typing import Optional
from functools import lru_cache

logger = logging.getLogger(__name__)
CLAUDE_RUNTIME_ID = "claude"
OPENAI_RUNTIME_ID = "openai"
_LEGACY_AGENT_ROOT_FIELDS = frozenset(
    {
        "baseUrl",
        "apiKey",
        "defaultEffort",
        "defaultThinking",
        "maxThinkingTokens",
        "modelMapping",
        "permissions",
        "openaiApi",
        "openaiDisableTracing",
        "tools",
    }
)


def resolve_bimcanvas_home() -> Path:
    """解析统一的 BIMCanvas 配置根目录。"""
    configured_home = os.getenv("BIMCANVAS_HOME", "").strip()
    if configured_home:
        return Path(
            os.path.expandvars(os.path.expanduser(configured_home))
        ).resolve()

    if os.name == "nt":
        return (Path.home() / "Documents" / "BIMCanvas").resolve()

    return (Path.home() / ".bimcanvas").resolve()


@dataclass
class AgentConfig:
    """子 Agent 配置"""
    name: str
    description: str
    tools: list[str]
    model: str
    prompt: str


def ensure_agent_config_schema(config: dict) -> None:
    """校验 Agent config.json 已切换到新的 provider 分域结构。"""
    if not isinstance(config, dict):
        raise ValueError("config.json 顶层必须是 JSON 对象。")

    legacy_fields = sorted(field for field in _LEGACY_AGENT_ROOT_FIELDS if field in config)
    if legacy_fields:
        fields_display = ", ".join(legacy_fields)
        raise ValueError(
            "检测到已废弃的旧版 Agent config.json 顶层字段: "
            f"{fields_display}。BIMCanvas 现在只接受新 schema："
            "{ runtimeProvider, claude, openai }，不会自动迁移旧结构。"
        )

    if "runtimeProvider" not in config:
        raise ValueError(
            "config.json 缺少 runtimeProvider。BIMCanvas 现在只接受新 schema："
            "{ runtimeProvider, claude, openai }。"
        )

    _normalize_runtime_provider(
        str(config.get("runtimeProvider", "")),
        source="config.json runtimeProvider",
    )

    for provider in (CLAUDE_RUNTIME_ID, OPENAI_RUNTIME_ID):
        section = config.get(provider)
        if not isinstance(section, dict):
            raise ValueError(
                f"config.json 必须包含对象类型的 `{provider}` 分域。"
            )

    chatgpt_backend = config.get("chatgptBackend")
    if chatgpt_backend is not None and not isinstance(chatgpt_backend, dict):
        raise ValueError(
            "config.json `chatgptBackend` 必须是对象（或整节缺失）。"
        )


def resolve_runtime_provider(config: dict) -> str:
    """解析当前生效的 runtimeProvider（支持环境变量覆盖）。"""
    ensure_agent_config_schema(config)

    configured_provider = _normalize_runtime_provider(
        str(config.get("runtimeProvider", "")),
        source="config.json runtimeProvider",
    )
    override = os.getenv("AGENT_RUNTIME_PROVIDER", "").strip()
    if not override:
        return configured_provider

    return _normalize_runtime_provider(
        override,
        source="AGENT_RUNTIME_PROVIDER",
    )


def _normalize_runtime_provider(value: str, *, source: str) -> str:
    from ..runtime.providers import normalize_runtime_provider

    return normalize_runtime_provider(
        value,
        source=source,
    )


def get_provider_config(config: dict, provider: str | None = None) -> dict:
    """返回指定 provider 的配置分域。"""
    ensure_agent_config_schema(config)
    resolved_provider = provider or resolve_runtime_provider(config)
    section = config.get(resolved_provider)
    if not isinstance(section, dict):
        raise ValueError(f"config.json `{resolved_provider}` 必须是对象。")
    return section


class ConfigLoader:
    """
    统一配置加载器

    职责：
    1. 校验 Server 已完成 BIMCANVAS_HOME 初始化
    2. 加载 config.json 并展开环境变量
    3. 加载 BIMCANVAS.md 作为系统提示词
    4. 加载 agents/*.md 作为子 Agent 配置
    """

    DEFAULT_CONFIG_DIR = resolve_bimcanvas_home()

    def __init__(self, config_dir: Path | str = None):
        """
        初始化配置加载器

        Args:
            config_dir: 配置目录路径，默认为 BIMCANVAS_HOME
        """
        if config_dir is None:
            self.config_dir = self.DEFAULT_CONFIG_DIR
        else:
            self.config_dir = Path(config_dir) if isinstance(config_dir, str) else config_dir

        # 调试日志：打印配置目录
        logger.debug(f"配置目录: {self.config_dir}")

        # 缓存
        self._config: Optional[dict] = None
        self._system_prompt: Optional[str] = None
        self._agents: Optional[dict[str, AgentConfig]] = None

        # Agent 不允许独立初始化配置，必须由 Server 先完成根目录初始化
        self._validate_bootstrap_layout()

    def _resolve_core_base_root(self) -> Path:
        """返回 core-base 资源根:新布局 plugins/core-base/ 优先,旧布局回退根目录(软兼容)。

        主真理源 v1.2 §3.5 + 组5 §5.A.6 Templates 重组:
        新布局 Templates/plugins/core-base/ 通过 BootstrapTemplateService.EnsurePluginInitialized
        初始化到 <BIMCANVAS_HOME>/plugins/core-base/。旧布局指 BIMCANVAS_HOME 根直接含
        BIMCANVAS.md/agents/skills 的过渡形态(组5 改造前)。
        """
        new_layout = self.config_dir / "plugins" / "core-base"
        if new_layout.is_dir():
            return new_layout
        return self.config_dir

    def _validate_bootstrap_layout(self) -> None:
        """校验 BIMCANVAS_HOME 是否已由 Server 初始化完成。

        主真理源 v1.2 §3.5 折中方案 + 组5 §5.A.6 Templates 重组:
        - config.json 在 BIMCANVAS_HOME 根目录 (平台级 Agent runtime 配置)
        - PLATFORM_CONTRACT.md 在 BIMCANVAS_HOME 根目录 (平台契约铁律)
        - BIMCANVAS.md / .claude-plugin / skills 在 core-base 资源根
          (新布局 plugins/core-base/,旧布局回退根目录)
        """
        # 1. 必须始终在 BIMCANVAS_HOME 根目录的平台级文件
        root_required = [
            ("config.json", "file"),
            ("PLATFORM_CONTRACT.md", "file"),
        ]
        # 2. core-base 资源 (新布局优先,旧布局回退)
        core_base_root = self._resolve_core_base_root()
        core_base_required = [
            ("BIMCANVAS.md", "file"),
            (".claude-plugin/plugin.json", "file"),
            ("skills", "directory"),
        ]

        missing: list[str] = []

        if not self.config_dir.exists():
            missing.append(str(self.config_dir))
        else:
            for relative_path, path_type in root_required:
                target_path = self.config_dir / relative_path
                exists = target_path.is_file() if path_type == "file" else target_path.is_dir()
                if not exists:
                    missing.append(relative_path)
            for relative_path, path_type in core_base_required:
                target_path = core_base_root / relative_path
                exists = target_path.is_file() if path_type == "file" else target_path.is_dir()
                if not exists:
                    # 显示相对 BIMCANVAS_HOME 的路径,便于诊断
                    try:
                        display = str(target_path.relative_to(self.config_dir))
                    except ValueError:
                        display = str(target_path)
                    missing.append(display)

        if not missing:
            return

        missing_display = ", ".join(missing)
        raise FileNotFoundError(
            "BIMCanvas Agent 配置未初始化，缺少: "
            f"{missing_display}。请先启动 BIMCanvas.Server 完成 <BIMCANVAS_HOME> 初始化。"
            f" 当前配置根目录: {self.config_dir}"
        )

    def load_config(self) -> dict:
        """
        加载 config.json

        Returns:
            配置字典，已展开环境变量
        """
        if self._config is not None:
            return self._config

        config_path = self.config_dir / "config.json"
        if not config_path.exists():
            raise FileNotFoundError(f"配置文件不存在: {config_path}")

        with open(config_path, 'r', encoding='utf-8-sig') as f:
            self._config = json.load(f)

        self._expand_env_vars(self._config)
        return self._config

    def load_system_prompt(self, active_plugin_root: Path | None = None) -> str:
        """加载系统提示词:PLATFORM_CONTRACT + active plugin 替换式拼接 (主真理源 v1.2 §3.5)。

        折中方案语义:
        - PLATFORM_CONTRACT.md (~35 行平台铁律) 始终在 prompt 顶部,plugin 不能覆盖
        - active plugin 的 BIMCANVAS.md **完全替换** core-base (不再叠加 core-base 100+ 行)
        - 未指定 active_plugin_root 时,core-base 作为默认 active plugin (Projectless 模式
          或没装 domain plugin 时的兜底)

        Args:
            active_plugin_root: active plugin 物理目录;None 时使用 core-base 作为默认 active plugin

        Returns:
            完整系统提示词:`PLATFORM_CONTRACT + 边界 + active plugin BIMCANVAS.md`
        """
        # 1. 读 PLATFORM_CONTRACT.md (永远在场,不可被 plugin 覆盖)
        contract_path = self.config_dir / "PLATFORM_CONTRACT.md"
        if not contract_path.is_file():
            raise FileNotFoundError(
                f"平台契约文件缺失: {contract_path}。"
                " 请先启动 BIMCanvas.Server 完成 <BIMCANVAS_HOME> 初始化。"
            )
        with open(contract_path, 'r', encoding='utf-8-sig') as f:
            contract = f.read()

        # 2. 决定 active plugin (None → core-base 作为默认)
        if active_plugin_root is None:
            active_plugin_root = self._resolve_core_base_root()
            plugin_id = "core-base"
        else:
            plugin_id = active_plugin_root.name

        # 3. 读 active plugin BIMCANVAS.md
        active_prompt_path = active_plugin_root / "BIMCANVAS.md"
        if not active_prompt_path.is_file():
            raise FileNotFoundError(
                f"active plugin BIMCANVAS.md 缺失: {active_prompt_path}"
            )
        with open(active_prompt_path, 'r', encoding='utf-8-sig') as f:
            active_prompt = f.read()

        # 4. 拼接:PLATFORM_CONTRACT + 边界 + active plugin
        return (
            f"{contract}\n\n"
            f"---\n## Active Plugin Contract: {plugin_id}\n---\n\n"
            f"{active_prompt}"
        )

    def load_tools(self) -> list[str] | None:
        """
        加载主 Agent 工具白名单（向后兼容方法）

        Returns:
            工具名称列表，或 None（表示默认全开）
        """
        allowed, _ = self.load_permissions()
        return allowed

    def load_permissions(self) -> tuple[list[str] | None, list[str]]:
        """
        加载当前 provider 的工具权限配置。

        Returns:
            (allowed_tools, disallowed_tools) 元组
            - allowed_tools: 允许的工具列表，None 表示默认全开
            - disallowed_tools: 禁止的工具列表
        """
        config = self.load_config()
        provider = resolve_runtime_provider(config)
        provider_config = get_provider_config(config, provider)
        permissions = provider_config.get("permissions", {})

        if permissions in (None, {}):
            return None, []
        if not isinstance(permissions, dict):
            raise ValueError(f"config.json `{provider}.permissions` 必须是对象。")

        allow = permissions.get("allow")
        deny = permissions.get("deny", [])

        if allow == []:
            allow = None
        if allow is not None and not isinstance(allow, list):
            raise ValueError(f"config.json `{provider}.permissions.allow` 必须是数组或 null。")
        if not isinstance(deny, list):
            raise ValueError(f"config.json `{provider}.permissions.deny` 必须是数组。")

        return allow, deny


    def load_agents(
        self,
        active_plugin_root: Path | None = None,
        declared_overrides: list[str] | tuple[str, ...] = (),
    ) -> dict[str, AgentConfig]:
        """
        加载 agents 配置,合并 core-base 与 active plugin 两层。

        组3 改造 (主真理源 v1.1 §3.6 / 组3 任务模板 §4.2 + §4.5):
        - 先扫 <BIMCANVAS_HOME>/agents/*.md (core-base / 旧布局 base)
        - 若 active_plugin_root 非空,再扫 <active_plugin_root>/agents/*.md
        - 同名 agent: active plugin 必须在 manifest 显式声明 `overrides.agents`
          包含该 name,否则抛 OverrideNotDeclaredError (防 domain 静默覆盖平台 agent)
        - BIMCanvas 自己 glob + 解析 + 显式传给 SDK,不依赖 SDK plugin 机制扫描 agents
          (主真理源 §2.4 卡点 C 关键纠正)

        Args:
            active_plugin_root: active plugin 物理目录;None 时只扫 base
            declared_overrides: active plugin manifest.overrides.agents 字段值

        Returns:
            agent name → AgentConfig 字典 (可能为空)
        """
        # 缓存只针对默认 (None) 调用
        if active_plugin_root is None and not declared_overrides and self._agents is not None:
            return self._agents

        result: dict[str, AgentConfig] = {}
        # 主真理源 v1.2 §3.5 折中方案:core-base agents 在新布局下从 plugins/core-base/agents/ 读
        agents_dir = self._resolve_core_base_root() / "agents"
        if agents_dir.exists():
            for md_file in agents_dir.glob("*.md"):
                try:
                    agent_config = self._parse_agent_md(md_file)
                    result[agent_config.name] = agent_config
                    logger.debug(f"已加载 base agent: {agent_config.name}")
                except Exception as e:
                    logger.warning(f"解析 base agent 配置失败 {md_file}: {e}")
        else:
            logger.warning(f"base agents 目录不存在: {agents_dir}")

        if active_plugin_root is not None:
            plugin_agents_dir = active_plugin_root / "agents"
            if plugin_agents_dir.exists():
                overrides_set = frozenset(declared_overrides or ())
                for md_file in plugin_agents_dir.glob("*.md"):
                    try:
                        agent_config = self._parse_agent_md(md_file)
                    except Exception as e:
                        logger.warning(f"解析 plugin agent 配置失败 {md_file}: {e}")
                        continue

                    if agent_config.name in result and agent_config.name not in overrides_set:
                        from bimcanvas_plugin_sdk import OverrideNotDeclaredError

                        raise OverrideNotDeclaredError(
                            f"plugin agent '{agent_config.name}' (来自 {active_plugin_root.name}) "
                            f"与 base 同名,但 manifest.overrides.agents 未声明该名字。"
                            f"请在 plugin manifest 加入 \"overrides\": {{ \"agents\": [\"{agent_config.name}\"] }} "
                            f"以明确覆盖意图。"
                        )

                    result[agent_config.name] = agent_config
                    logger.debug(f"已加载 plugin agent: {agent_config.name}")

        if not result:
            logger.warning("未加载任何 agent (base + plugin 都为空)")

        # 仅在默认调用路径缓存
        if active_plugin_root is None and not declared_overrides:
            self._agents = result
        return result

    def _parse_agent_md(self, file_path: Path) -> AgentConfig:
        """
        解析子 Agent .md 文件（YAML frontmatter + Markdown）

        文件格式：
        ---
        name: agent-name
        description: Agent 描述
        tools: Read, Glob, Write
        model: inherit
        ---

        （提示词内容）
        """
        with open(file_path, 'r', encoding='utf-8-sig') as f:
            content = f.read()

        # 匹配 YAML frontmatter
        pattern = r'^---\s*\n(.*?)\n---\s*\n(.*)$'
        match = re.match(pattern, content, re.DOTALL)

        if not match:
            raise ValueError(f"无效的 Agent 配置文件格式（缺少 YAML frontmatter）: {file_path}")

        frontmatter_str = match.group(1)
        prompt_content = match.group(2).strip()

        # 简单 YAML 解析
        frontmatter = self._parse_simple_yaml(frontmatter_str)

        if 'name' not in frontmatter:
            raise ValueError(f"Agent 配置缺少 name 字段: {file_path}")
        if 'description' not in frontmatter:
            raise ValueError(f"Agent 配置缺少 description 字段: {file_path}")

        # 解析 tools 字段
        tools_str = frontmatter.get('tools', '')
        tools = [t.strip() for t in tools_str.split(',') if t.strip()] if tools_str else []

        return AgentConfig(
            name=frontmatter['name'],
            description=frontmatter['description'],
            tools=tools,
            model=frontmatter.get('model', 'inherit'),
            prompt=prompt_content
        )

    def _parse_simple_yaml(self, yaml_str: str) -> dict:
        """简单 YAML 解析（仅支持 key: value 格式）"""
        result = {}
        for line in yaml_str.strip().split('\n'):
            line = line.strip()
            if not line or line.startswith('#'):
                continue

            if ':' in line:
                key, _, value = line.partition(':')
                key = key.strip()
                value = value.strip()

                # 移除引号
                if (value.startswith('"') and value.endswith('"')) or \
                   (value.startswith("'") and value.endswith("'")):
                    value = value[1:-1]

                result[key] = value

        return result

    def _expand_env_vars(self, config: dict) -> None:
        """
        递归展开配置中的环境变量引用

        约定：以 $ 开头的字符串值表示环境变量引用
        """
        for key, value in config.items():
            if isinstance(value, str) and value.startswith('$'):
                env_name = value[1:]
                env_value = os.getenv(env_name, '')
                config[key] = env_value
                if not env_value:
                    logger.warning(f"环境变量未设置: {env_name}")
            elif isinstance(value, dict):
                self._expand_env_vars(value)

    def resolve_active_plugin(
        self, launch_context
    ) -> tuple[Path | None, dict, list[str], list[str]]:
        """根据 PluginLaunchContext 解析 active plugin 目录与 manifest。

        组3 任务模板 §4.2 末段 + 主真理源 v1.1 §3.4 / §3.8。

        Args:
            launch_context: runtime.launch_context.PluginLaunchContext 实例

        Returns:
            (active_plugin_root, plugin_manifest, overrides_agents, overrides_skills)
            - active_plugin_root: Path 或 None (Projectless / active_plugin_id 为 None)
            - plugin_manifest: dict (bimcanvas-plugin.json 内容);无 manifest 时返回 {}
            - overrides_agents / overrides_skills: list[str]

        Raises:
            PluginManifestError: manifest 字段不合法 (含 .. 逃逸 / namespace=canvas 等)
        """
        if (
            launch_context is None
            or not getattr(launch_context, "active_plugin_id", None)
            or not getattr(launch_context, "active_plugin_root", None)
        ):
            return None, {}, [], []

        active_root = Path(launch_context.active_plugin_root)
        if not active_root.is_dir():
            logger.warning(
                "active plugin root 不存在: %s,跳过 plugin 加载", active_root
            )
            return None, {}, [], []

        manifest_path = active_root / "bimcanvas-plugin.json"
        manifest: dict = {}
        if manifest_path.is_file():
            with open(manifest_path, "r", encoding="utf-8-sig") as f:
                manifest = json.load(f)
        else:
            logger.warning(
                "bimcanvas-plugin.json 不存在 (%s),按无 manifest 的 plugin 处理",
                manifest_path,
            )

        # 最小校验 (组1 JSONSchema 是完整校验,这里只做安全相关项)
        from bimcanvas_plugin_sdk import PluginManifestError

        mcp_namespace = manifest.get("mcpNamespace")
        if mcp_namespace == "canvas":
            raise PluginManifestError(
                f"plugin {launch_context.active_plugin_id} 的 mcpNamespace 不能为 'canvas' "
                f"(保留给 core-base)"
            )

        mcp_tools = manifest.get("mcpTools")
        if mcp_tools:
            if not isinstance(mcp_tools, str):
                raise PluginManifestError(
                    f"plugin {launch_context.active_plugin_id} 的 mcpTools 必须是字符串路径"
                )
            if ".." in Path(mcp_tools).parts:
                raise PluginManifestError(
                    f"plugin {launch_context.active_plugin_id} 的 mcpTools 路径含 '..' 逃逸: {mcp_tools}"
                )
            if not mcp_tools.endswith(".py"):
                raise PluginManifestError(
                    f"plugin {launch_context.active_plugin_id} 的 mcpTools 必须是 .py 文件: {mcp_tools}"
                )

        overrides = manifest.get("overrides", {}) or {}
        overrides_agents = list(overrides.get("agents", []) or [])
        overrides_skills = list(overrides.get("skills", []) or [])

        return active_root, manifest, overrides_agents, overrides_skills

    def clear_cache(self) -> None:
        """清除配置缓存"""
        self._config = None
        self._system_prompt = None
        self._agents = None


@lru_cache()
def get_config_loader() -> ConfigLoader:
    """获取全局 ConfigLoader 实例（单例）"""
    return ConfigLoader()
