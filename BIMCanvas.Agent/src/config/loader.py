"""Configuration loader with auto-initialization from templates."""

import os
import re
import json
import shutil
import logging
from pathlib import Path
from dataclasses import dataclass
from typing import Optional
from functools import lru_cache

logger = logging.getLogger(__name__)


@dataclass
class AgentConfig:
    """子 Agent 配置"""
    name: str
    description: str
    tools: list[str]
    model: str
    prompt: str


class ConfigLoader:
    """
    统一配置加载器

    职责：
    1. 检查配置目录是否存在，不存在则从模板初始化
    2. 加载 config.json 并展开环境变量
    3. 加载 BIMCANVAS.md 作为系统提示词
    4. 加载 agents/*.md 作为子 Agent 配置
    """

    TEMPLATES_DIR = Path(__file__).parent / "templates"
    DEFAULT_CONFIG_DIR = Path.home() / ".bimcanvas"

    def __init__(self, config_dir: Path | str = None):
        """
        初始化配置加载器

        Args:
            config_dir: 配置目录路径，默认为 ~/.bimcanvas
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

        # 确保配置存在
        self._ensure_config_exists()

    def _ensure_config_exists(self) -> None:
        """确保配置目录和文件存在，不存在则从 init_manifest.json 初始化"""
        manifest_path = self.TEMPLATES_DIR / "init_manifest.json"
        if not manifest_path.exists():
            raise FileNotFoundError(
                f"模板清单不存在: {manifest_path}\n"
                "请确保项目安装正确"
            )

        with open(manifest_path, 'r', encoding='utf-8') as f:
            manifest = json.load(f)

        # 创建配置目录
        self.config_dir.mkdir(parents=True, exist_ok=True)

        # 创建清单中声明的子目录
        for dir_name in manifest.get("createDirs", []):
            (self.config_dir / dir_name).mkdir(exist_ok=True)

        # 按清单初始化模板文件
        for item in manifest.get("items", []):
            if not item.get("enabled", True):
                logger.debug(f"跳过禁用项: {item['name']}")
                continue

            source_path = self.TEMPLATES_DIR / item["name"]
            target_path = self.config_dir / item["target"]

            if target_path.exists():
                continue

            if not source_path.exists():
                logger.warning(f"模板源文件不存在: {source_path}")
                continue

            target_path.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy(source_path, target_path)
            logger.info(f"已创建配置文件: {target_path}")

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

        with open(config_path, 'r', encoding='utf-8') as f:
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

        with open(prompt_path, 'r', encoding='utf-8') as f:
            self._system_prompt = f.read()

        return self._system_prompt

    def load_tools(self) -> list[str] | None:
        """
        加载主 Agent 工具列表（向后兼容方法）

        Returns:
            工具名称列表，或 None（表示默认全开）
        """
        allowed, _ = self.load_permissions()
        return allowed

    def load_permissions(self) -> tuple[list[str] | None, list[str]]:
        """
        加载工具权限配置

        支持两种格式：
        1. 新格式：permissions: { allow: [...], deny: [...] }
        2. 旧格式：tools: [...]（向后兼容）

        Returns:
            (allowed_tools, disallowed_tools) 元组
            - allowed_tools: 允许的工具列表，None 表示默认全开
            - disallowed_tools: 禁止的工具列表
        """
        config = self.load_config()
        permissions = config.get('permissions', {})

        # 新格式：permissions 结构
        if permissions:
            allow = permissions.get('allow')
            deny = permissions.get('deny', [])
            # 空数组或 null 都返回 None，表示默认全开
            if not allow:
                allow = None
            return allow, deny

        # 兼容旧格式：tools 字段
        tools = config.get('tools')
        if not tools:
            return None, []
        return tools, []


    def load_agents(self) -> dict[str, AgentConfig]:
        """
        加载 agents/ 目录下所有子 Agent 配置

        Returns:
            子 Agent 名称到配置的映射字典
        """
        if self._agents is not None:
            return self._agents

        agents_dir = self.config_dir / "agents"
        if not agents_dir.exists():
            raise FileNotFoundError(f"agents 目录不存在: {agents_dir}")

        self._agents = {}

        for md_file in agents_dir.glob("*.md"):
            try:
                agent_config = self._parse_agent_md(md_file)
                self._agents[agent_config.name] = agent_config
                logger.debug(f"已加载子 Agent: {agent_config.name}")
            except Exception as e:
                logger.warning(f"解析子 Agent 配置失败 {md_file}: {e}")

        if not self._agents:
            raise ValueError(f"agents 目录为空或所有文件解析失败: {agents_dir}")

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
        with open(file_path, 'r', encoding='utf-8') as f:
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
