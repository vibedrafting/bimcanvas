"""Configuration settings for BIMCanvas Agent"""

import logging
import os
from dataclasses import dataclass
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

    加载优先级：环境变量 > config.json > 默认值

    环境变量说明（与 Claude Code 隔离）：
    - AGENT_SDK_API_KEY: Agent SDK 专用 API Key
    - AGENT_SDK_BASE_URL: Agent SDK 专用 Base URL
    """

    anthropic_api_key: str
    base_url: str
    model_name: str
    default_effort: str              # "low"/"medium"/"high"/"max", 默认 "medium"
    default_thinking: str            # "off"/"adaptive", 默认 "off"
    max_thinking_tokens: int | None  # thinking token 预算上限，None/-1/空 = 不限制
    tools: list[str]
    server_host: str
    server_port: int
    default_project_path: str

    @classmethod
    def load(cls) -> "Settings":
        """从配置文件加载，环境变量覆盖"""
        loader = get_config_loader()
        config = loader.load_config()
        server = config.get('server', {})

        # 从配置文件读取
        api_key = config.get('apiKey', '')
        base_url = config.get('baseUrl', '')
        model = config.get('model', 'claude-sonnet-4-20250514')
        default_effort = config.get('defaultEffort', 'medium')
        default_thinking = config.get('defaultThinking', 'off')
        raw_thinking_tokens = config.get('maxThinkingTokens', None)
        max_thinking_tokens = None if raw_thinking_tokens in (None, '', -1) else int(raw_thinking_tokens)
        tools = config.get('tools', ['Read', 'Glob', 'Grep', 'Task'])
        host = server.get('host', '127.0.0.1')
        port = server.get('port', 8765)

        # 环境变量覆盖
        # API Key: AGENT_SDK_API_KEY > config.json（与 Claude Code 隔离，不使用 ANTHROPIC_API_KEY）
        api_key = os.getenv('AGENT_SDK_API_KEY', api_key)

        # Base URL: AGENT_SDK_BASE_URL > config.json
        base_url = os.getenv('AGENT_SDK_BASE_URL', base_url)

        # Model: MODEL_NAME > config.json
        env_model = os.getenv('MODEL_NAME')
        if env_model:
            logger.info(f"环境变量覆盖模型: {env_model}")
            model = env_model
        else:
            logger.info(f"使用配置模型: {model}")

        env_thinking_tokens = os.getenv('MAX_THINKING_TOKENS')
        if env_thinking_tokens is not None:
            max_thinking_tokens = None if env_thinking_tokens in ('', '-1') else int(env_thinking_tokens)
        host = os.getenv('SERVER_HOST', host)
        port = int(os.getenv('SERVER_PORT', str(port)))
        project_path = os.getenv('DEFAULT_PROJECT_PATH', '')

        return cls(
            anthropic_api_key=api_key,
            base_url=base_url,
            model_name=model,
            max_thinking_tokens=max_thinking_tokens,
            default_effort=default_effort,
            default_thinking=default_thinking,
            tools=tools,
            server_host=host,
            server_port=port,
            default_project_path=project_path,
        )


@lru_cache()
def get_settings() -> Settings:
    """Get cached settings instance"""
    return Settings.load()
