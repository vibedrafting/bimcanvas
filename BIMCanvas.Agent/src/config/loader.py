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

    def _validate_bootstrap_layout(self) -> None:
        """校验 BIMCANVAS_HOME 是否已由 Server 初始化完成。"""
        required_paths = [
            ("config.json", "file"),
            ("BIMCANVAS.md", "file"),
            ("agents/layout-agent.md", "file"),
            (".claude-plugin/plugin.json", "file"),
            ("skills", "directory"),
        ]

        missing: list[str] = []

        if not self.config_dir.exists():
            missing.append(str(self.config_dir))
        else:
            for relative_path, path_type in required_paths:
                target_path = self.config_dir / relative_path
                exists = target_path.is_file() if path_type == "file" else target_path.is_dir()
                if not exists:
                    missing.append(relative_path)

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

    def load_system_prompt(self) -> str:
        """
        加载 BIMCANVAS.md 作为系统提示词

        Returns:
            系统提示词内容
        """
        if self._system_prompt is not None:
            return self._system_prompt

        prompt_path = self.config_dir / "BIMCANVAS.md"
        if not prompt_path.exists():
            raise FileNotFoundError(f"系统提示词文件不存在: {prompt_path}")

        with open(prompt_path, 'r', encoding='utf-8-sig') as f:
            self._system_prompt = f.read()

        return self._system_prompt

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


    def load_agents(self) -> dict[str, AgentConfig]:
        """
        加载 agents/ 目录下所有子 Agent 配置

        Returns:
            子 Agent 名称到配置的映射字典（可能为空）
        """
        if self._agents is not None:
            return self._agents

        agents_dir = self.config_dir / "agents"
        if not agents_dir.exists():
            logger.warning(f"agents 目录不存在: {agents_dir}，SubAgent 功能不可用")
            self._agents = {}
            return self._agents

        result = {}

        for md_file in agents_dir.glob("*.md"):
            try:
                agent_config = self._parse_agent_md(md_file)
                result[agent_config.name] = agent_config
                logger.debug(f"已加载子 Agent: {agent_config.name}")
            except Exception as e:
                logger.warning(f"解析子 Agent 配置失败 {md_file}: {e}")

        if not result:
            logger.warning(f"agents 目录为空或所有文件解析失败: {agents_dir}，SubAgent 功能不可用")

        self._agents = result
        return self._agents

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

    def clear_cache(self) -> None:
        """清除配置缓存"""
        self._config = None
        self._system_prompt = None
        self._agents = None


@lru_cache()
def get_config_loader() -> ConfigLoader:
    """获取全局 ConfigLoader 实例（单例）"""
    return ConfigLoader()
