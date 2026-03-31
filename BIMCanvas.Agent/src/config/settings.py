"""Configuration settings for BIMCanvas Agent"""

import logging
import os
from dataclasses import dataclass, field
from functools import lru_cache
from dotenv import load_dotenv

from .loader import get_config_loader

logger = logging.getLogger(__name__)

# Load environment variables from .env file
load_dotenv()


@dataclass
class Settings:
    """
    Application settings

    配置来源：
    - 直连模式：base_url / api_key 来自 config.json，
      模型映射通过 modelMapping 设置 ANTHROPIC_DEFAULT_*_MODEL 环境变量
    - CCR 模式：base_url / api_key 来自 Server 注入的网关环境变量

    环境变量说明（与 Claude Code 隔离）：
    - AGENT_SDK_API_KEY: Agent SDK 专用 API Key
    - AGENT_SDK_BASE_URL: Agent SDK 专用 Base URL
    """

    anthropic_api_key: str
    base_url: str
    default_effort: str              # "low"/"medium"/"high"/"max", 默认 "medium"
    default_thinking: str            # "off"/"adaptive", 默认 "off"
    max_thinking_tokens: int | None  # thinking token 预算上限，None/-1/空 = 不限制
    tools: list[str]
    server_host: str
    server_port: int
    default_project_path: str
    model_mapping: dict = field(default_factory=dict)   # {"opus": {"id": "...", "label": "..."}, ...}

    @classmethod
    def load(cls) -> "Settings":
        """从配置文件加载运行时设置。"""
        loader = get_config_loader()
        config = loader.load_config()
        server = config.get('server', {})

        # 从配置文件读取
        direct_api_key = config.get('apiKey', '')
        direct_base_url = config.get('baseUrl', '')
        default_effort = config.get('defaultEffort', 'medium')
        default_thinking = config.get('defaultThinking', 'off')
        raw_thinking_tokens = config.get('maxThinkingTokens', None)
        max_thinking_tokens = None if raw_thinking_tokens in (None, '', -1) else int(raw_thinking_tokens)
        tools = config.get('tools', ['Read', 'Glob', 'Grep', 'Task'])
        host = server.get('host', '127.0.0.1')
        port = server.get('port', 8865)
        ccr_managed = _is_ccr_managed_mode()

        # 模型映射（两种模式都加载，用于 /api/config 返回下拉菜单）
        model_mapping = config.get('modelMapping', {})

        if ccr_managed:
            api_key = os.getenv('AGENT_SDK_API_KEY', '').strip()
            base_url = os.getenv('AGENT_SDK_BASE_URL', '').strip()

            missing_vars = []
            if not base_url:
                missing_vars.append('AGENT_SDK_BASE_URL')
            if not api_key:
                missing_vars.append('AGENT_SDK_API_KEY')

            if missing_vars:
                missing_vars_display = ', '.join(missing_vars)
                raise ValueError(
                    "检测到 CCR 托管环境变量，但缺少必需项: "
                    f"{missing_vars_display}"
                )

            logger.info("使用 CCR 网关连接 Agent SDK")
        else:
            api_key = direct_api_key
            base_url = direct_base_url

            # 直连模式：从 modelMapping 设置 Claude Code CLI 模型映射环境变量
            _apply_model_mapping(model_mapping)

        env_thinking_tokens = os.getenv('MAX_THINKING_TOKENS')
        if env_thinking_tokens is not None:
            max_thinking_tokens = None if env_thinking_tokens in ('', '-1') else int(env_thinking_tokens)
        host = os.getenv('SERVER_HOST', host)
        port = int(os.getenv('SERVER_PORT', str(port)))
        project_path = os.getenv('DEFAULT_PROJECT_PATH', '')

        return cls(
            anthropic_api_key=api_key,
            base_url=base_url,
            max_thinking_tokens=max_thinking_tokens,
            default_effort=default_effort,
            default_thinking=default_thinking,
            tools=tools,
            server_host=host,
            server_port=port,
            default_project_path=project_path,
            model_mapping=model_mapping,
        )


@lru_cache()
def get_settings() -> Settings:
    """Get cached settings instance"""
    return Settings.load()


def _is_ccr_managed_mode() -> bool:
    """通过 Server 注入的网关环境变量判断是否处于 CCR 托管模式。"""
    api_key = os.getenv('AGENT_SDK_API_KEY', '').strip()
    base_url = os.getenv('AGENT_SDK_BASE_URL', '').strip()
    return bool(api_key or base_url)


def _apply_model_mapping(model_mapping: dict) -> None:
    """直连模式下，将 config.json 的 modelMapping 转换为 Claude Code CLI 环境变量。"""
    family_env_map = {
        'opus':   'ANTHROPIC_DEFAULT_OPUS_MODEL',
        'sonnet': 'ANTHROPIC_DEFAULT_SONNET_MODEL',
        'haiku':  'ANTHROPIC_DEFAULT_HAIKU_MODEL',
    }
    for family, env_name in family_env_map.items():
        entry = model_mapping.get(family, {})
        model_id = entry.get('id') if isinstance(entry, dict) else entry
        if model_id:
            os.environ[env_name] = model_id
            logger.info(f"模型映射: {family} → {model_id}")
