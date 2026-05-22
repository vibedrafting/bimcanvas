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
    tools: list[str] | None
    """工具列表三态(工具权限重设计 v3.2 §5.2):
    - None: `.md` 未声明 / 空值 → 装配时按主控 allow+deny 副本继承
    - list[str]: `.md` 显式列出 → 直接使用,不再继承主控
    """
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

        # C1 (工具权限重设计 v3.2 §6): 旧 `permissions` 字段 fail-fast
        if "permissions" in section:
            raise ValueError(
                f"检测到 config.json 含旧版 `{provider}.permissions` 字段。\n"
                "工具权限配置已重设计 (v3.2),请参考迁移文档手工调整:\n"
                "  docs/Tool_Permissions_Migration.md\n"
                f"  旧 `{provider}.permissions.allow / deny` "
                f"→ 新 `{provider}.tools.allow / deny`\n"
                f"  另外新增 `{provider}.agents.allow / deny` 块需添加 (可填空数组)。\n"
                "BIMCanvas 不会自动迁移旧结构。"
            )

        # C3 (工具权限 v3.3 §3 Phase 3a): config.json 的 tools/agents 已废弃,
        # 工具权限改由 plugin manifest 接管。检测到只 warning 不抛错,
        # 用户可以从配置文件中删除这些字段(详见 docs/Tool_Permissions_Migration.md)。
        for deprecated_field in ("tools", "agents"):
            if deprecated_field in section:
                logger.warning(
                    "config.json 的 %s.%s 字段在 v3.3 已废弃,工具权限改由 plugin manifest "
                    "(<plugin>/bimcanvas-plugin.json 的 tools/agents 字段) 接管,可以从配置"
                    "文件中删除该字段。详见 docs/Tool_Permissions_Migration.md",
                    provider, deprecated_field,
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

        v3.6 两层 prompt 架构 + 组5 §5.A.6 Templates 重组:
        - config.json 在 BIMCANVAS_HOME 根目录 (平台级 Agent runtime 配置)
        - BIMCANVAS.md / .claude-plugin / skills 在 core-base 资源根 (即平台基座 prompt)
        - 原 PLATFORM_CONTRACT.md 内容已并入 core-base/BIMCANVAS.md, 不再单独校验
          (归档参考: .dev/docs/Platform_Contract_Reference.md)
        """
        # 1. 必须始终在 BIMCANVAS_HOME 根目录的平台级运行时配置文件
        #    配置合并:四组运行时配置统一在 instance.config.json(Agent 读其 agent 段);
        #    旧布局回退到独立 config.json(整份即 agent runtime 配置)。
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
            has_unified = (self.config_dir / "instance.config.json").is_file()
            has_legacy = (self.config_dir / "config.json").is_file()
            if not has_unified and not has_legacy:
                missing.append("instance.config.json")
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
        加载 Agent runtime 配置（统一文件 instance.config.json 的 `agent` 段）。

        配置合并:四组运行时配置统一在 instance.config.json。Agent 只消费其 `agent` 段
        (runtimeProvider / claude / openai / chatgptBackend)。旧布局回退到独立 config.json
        (整份即 agent runtime 配置),便于过渡。

        Returns:
            agent runtime 配置字典，已展开环境变量
        """
        if self._config is not None:
            return self._config

        unified_path = self.config_dir / "instance.config.json"
        legacy_path = self.config_dir / "config.json"

        if unified_path.exists():
            with open(unified_path, 'r', encoding='utf-8-sig') as f:
                unified = json.load(f)
            if not isinstance(unified, dict):
                raise ValueError(f"统一配置文件顶层必须是 JSON 对象: {unified_path}")
            agent_section = unified.get("agent")
            if not isinstance(agent_section, dict):
                raise ValueError(
                    f"统一配置文件缺少对象类型的 `agent` 段: {unified_path}"
                )
            self._config = agent_section
        elif legacy_path.exists():
            # 兼容旧布局:独立 config.json 整份即 agent runtime 配置
            with open(legacy_path, 'r', encoding='utf-8-sig') as f:
                self._config = json.load(f)
        else:
            raise FileNotFoundError(
                f"配置文件不存在: {unified_path}（也无旧版 {legacy_path}）"
            )

        self._expand_env_vars(self._config)
        return self._config

    def load_system_prompt(self, active_plugin_root: Path | None = None) -> str:
        """加载系统提示词:core-base 永远在场 + active domain plugin 叠加 (v3.6 两层架构)。

        叠加语义:
        - core-base/BIMCANVAS.md 作为平台基座 prompt **永远在场**,含平台铁律 + 通用角色 +
          chat/query/edit 通用路由(原 PLATFORM_CONTRACT.md 内容已并入此文件 §2)
        - active_plugin_root 为 None 或显式传入 core-base 自身时,只返回基座 prompt (防 self-stack)
        - 装了 domain plugin (如 indoor-layout) 时, 在基座 prompt 后追加 plugin 的 BIMCANVAS.md
          (业务路由扩展 + workflow 调度), 中间用 markdown 分节符 + plugin id 边界标识

        Args:
            active_plugin_root: active domain plugin 物理目录;None 表示无 domain plugin

        Returns:
            完整系统提示词:基座单层, 或 `基座 + 边界 + plugin` 双层
        """
        # 1. core-base/BIMCANVAS.md 必存 (平台基座, 缺失即配置损坏)
        core_base_root = self._resolve_core_base_root()
        core_base_path = core_base_root / "BIMCANVAS.md"
        if not core_base_path.is_file():
            raise FileNotFoundError(
                f"core-base/BIMCANVAS.md 是平台基座 prompt, 不可缺失: {core_base_path}。"
                " 请先启动 BIMCanvas.Server 完成 <BIMCANVAS_HOME> 初始化。"
            )
        with open(core_base_path, 'r', encoding='utf-8-sig') as f:
            core_base_prompt = f.read()

        # 2. 无 plugin 或显式传 core-base 自身 → 单层返回 (防 self-stack)
        if active_plugin_root is None or active_plugin_root.resolve() == core_base_root.resolve():
            return core_base_prompt

        # 3. 装了 domain plugin → 叠加
        active_prompt_path = active_plugin_root / "BIMCANVAS.md"
        if not active_prompt_path.is_file():
            raise FileNotFoundError(
                f"active plugin BIMCANVAS.md 缺失: {active_prompt_path}"
            )
        with open(active_prompt_path, 'r', encoding='utf-8-sig') as f:
            active_prompt = f.read()

        plugin_id = active_plugin_root.name
        return (
            f"{core_base_prompt}\n\n"
            f"---\n## Domain Plugin Layer · {plugin_id}\n---\n\n"
            f"{active_prompt}"
        )

    def load_plugin_manifest_permissions(self, plugin_root: Path) -> dict:
        """加载某个 plugin manifest 的 tools/agents 字段 (工具权限 v3.3 §3 Phase 3a)。

        Schema (v3.3.2 manifest 9 字段方案):
            <plugin_root>/bimcanvas-plugin.json 含必填字段:
              tools.{allow: [...], deny: [...]}
              agents.{allow: [...], deny: [...]}

        Returns:
            dict 形如 {tools: {allow: [...], deny: [...]},
                       agents: {allow: [...], deny: [...]}}
            文件缺失 / 字段缺失时返回默认空对象(用于 robust fallback)。
            JSON 解析失败 → 抛 ValueError(fail-fast)。

        Note:
            本方法不做 fallback 选择,只读单个 manifest。fallback / active 选择
            由 config_bundle._resolve_effective_permissions 完成。
        """
        manifest_path = plugin_root / "bimcanvas-plugin.json"
        default = {
            "tools": {"allow": [], "deny": []},
            "agents": {"allow": [], "deny": []},
        }
        if not manifest_path.is_file():
            logger.warning(
                "plugin manifest 不存在: %s,工具权限走空白默认", manifest_path
            )
            return default

        with open(manifest_path, "r", encoding="utf-8-sig") as f:
            manifest = json.load(f)

        if not isinstance(manifest, dict):
            raise ValueError(
                f"plugin manifest 顶层必须是 JSON 对象: {manifest_path}"
            )

        def _extract_block(key: str) -> dict:
            block = manifest.get(key)
            if not isinstance(block, dict):
                return {"allow": [], "deny": []}
            return {
                "allow": list(block.get("allow") or []),
                "deny": list(block.get("deny") or []),
            }

        return {
            "tools": _extract_block("tools"),
            "agents": _extract_block("agents"),
        }


    def load_agents(
        self,
        active_plugin_root: Path | None = None,
    ) -> dict[str, AgentConfig]:
        """
        加载 agents 配置,合并 core-base 与 active plugin 两层。

        v3.7 silent override 改造 (主真理源 v1.1 §3.6 / 组3 任务模板 §4.2 + §4.5):
        - 先扫 <BIMCANVAS_HOME>/agents/*.md (core-base / 旧布局 base)
        - 若 active_plugin_root 非空,再扫 <active_plugin_root>/agents/*.md
        - 同名 agent 默认由 plugin 那一份覆盖 base,logger.info 记录覆盖决定
          (不再要求 manifest 显式声明 overrides.agents)
        - BIMCanvas 自己 glob + 解析 + 显式传给 SDK,不依赖 SDK plugin 机制扫描 agents
          (主真理源 §2.4 卡点 C 关键纠正)

        Args:
            active_plugin_root: active plugin 物理目录;None 时只扫 base

        Returns:
            agent name → AgentConfig 字典 (可能为空)
        """
        # 缓存只针对默认 (None) 调用
        if active_plugin_root is None and self._agents is not None:
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
                for md_file in plugin_agents_dir.glob("*.md"):
                    try:
                        agent_config = self._parse_agent_md(md_file)
                    except Exception as e:
                        logger.warning(f"解析 plugin agent 配置失败 {md_file}: {e}")
                        continue

                    if agent_config.name in result:
                        logger.info(
                            "plugin agent '%s' (来自 %s) 覆盖 core-base 同名 agent",
                            agent_config.name, active_plugin_root.name,
                        )

                    result[agent_config.name] = agent_config
                    logger.debug(f"已加载 plugin agent: {agent_config.name}")

        if not result:
            logger.warning("未加载任何 agent (base + plugin 都为空)")

        # 仅在默认调用路径缓存
        if active_plugin_root is None:
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

        # 解析 tools 字段 (工具权限重设计 v3.2 §5.2 三态):
        # - 字段完全缺失 → None (继承主控)
        # - 字段存在值为空 → None (继承主控)
        # - 字段存在值非空 → CSV 解析为 list[str] (显式自主,不再继承)
        if 'tools' not in frontmatter:
            tools: list[str] | None = None
        else:
            raw = frontmatter['tools'].strip()
            if not raw:
                tools = None
            else:
                tools = [t.strip() for t in raw.split(',') if t.strip()]

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
    ) -> tuple[Path | None, dict]:
        """根据 PluginLaunchContext 解析 active plugin 目录与 manifest。

        v3.7 silent override 改造:不再返回 overrides_agents / overrides_skills
        (manifest 中的 overrides 字段已废弃,plugin 同名 agent/skill 默认覆盖 base,
        覆盖决定由 load_agents / _build_skill_index 用 logger.info 记录)。

        Args:
            launch_context: runtime.launch_context.PluginLaunchContext 实例

        Returns:
            (active_plugin_root, plugin_manifest)
            - active_plugin_root: Path 或 None (Projectless / active_plugin_id 为 None)
            - plugin_manifest: dict (bimcanvas-plugin.json 内容);无 manifest 时返回 {}

        Raises:
            PluginManifestError: manifest 字段不合法 (含 .. 逃逸 / namespace=canvas 等)
        """
        if (
            launch_context is None
            or not getattr(launch_context, "active_plugin_id", None)
            or not getattr(launch_context, "active_plugin_root", None)
        ):
            return None, {}

        active_root = Path(launch_context.active_plugin_root)
        if not active_root.is_dir():
            logger.warning(
                "active plugin root 不存在: %s,跳过 plugin 加载", active_root
            )
            return None, {}

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

        # 工具权限 v3.3 §3 Phase 3a:
        # 原 mcpTools / mcpNamespace 字段在 v3.3.2 manifest schema 中已删除,
        # 改为约定俗成 mcp_tools/<plugin-name>.py 由 _build_mcp_servers 扫描推断。
        # 因此本处的 mcpTools 路径校验和 mcpNamespace 保留校验都已移除,
        # 路径安全 (无 ..) 由 glob 隐含保证 (不递归),namespace 安全 (≠ canvas)
        # 由 _build_mcp_servers 内文件名校验承担。

        return active_root, manifest

    def clear_cache(self) -> None:
        """清除配置缓存"""
        self._config = None
        self._system_prompt = None
        self._agents = None


@lru_cache()
def get_config_loader() -> ConfigLoader:
    """获取全局 ConfigLoader 实例（单例）"""
    return ConfigLoader()
