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
class ThinkingConfig:
    """思考强度配置"""
    enabled: bool = True
    default_level: str = "medium"
    budget: dict[str, int | None] = field(default_factory=lambda: {
        "off": None,
        "low": 5000,
        "medium": 10000,
        "high": 20000
    })

    def get_tokens(self, level: str = None) -> int | None:
        """
        获取指定等级的 token 数量

        Args:
            level: 思考强度等级 ("off", "low", "medium", "high")
                   如果为 None，使用默认等级

        Returns:
            token 数量，None 表示禁用思考
        """
        if not self.enabled:
            return None
        level = level or self.default_level
        if level == "off":
            return None
        return self.budget.get(level, self.budget.get("medium"))


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
    max_tokens: int
    thinking: ThinkingConfig
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
        thinking_config = config.get('thinking', {})

        # 从配置文件读取
        api_key = config.get('apiKey', '')
        base_url = config.get('baseUrl', '')
        model = config.get('model', 'claude-sonnet-4-20250514')
        max_tokens = config.get('maxTokens', 4096)
        tools = config.get('tools', ['Read', 'Glob', 'Grep', 'Task'])
        host = server.get('host', '127.0.0.1')
        port = server.get('port', 8765)

        # 构建 ThinkingConfig
        thinking = ThinkingConfig(
            enabled=thinking_config.get('enabled', True),
            default_level=thinking_config.get('defaultLevel', 'medium'),
            budget=thinking_config.get('budget', {
                "off": None,
                "low": 5000,
                "medium": 10000,
                "high": 20000
            })
        )

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

        max_tokens = int(os.getenv('MAX_TOKENS', str(max_tokens)))
        host = os.getenv('SERVER_HOST', host)
        port = int(os.getenv('SERVER_PORT', str(port)))
        project_path = os.getenv('DEFAULT_PROJECT_PATH', '')

        return cls(
            anthropic_api_key=api_key,
            base_url=base_url,
            model_name=model,
            max_tokens=max_tokens,
            thinking=thinking,
            tools=tools,
            server_host=host,
            server_port=port,
            default_project_path=project_path,
        )


@lru_cache()
def get_settings() -> Settings:
    """Get cached settings instance"""
    return Settings.load()
