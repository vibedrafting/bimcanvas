"""Configuration settings for BIMCanvas Agent"""

import os
from dataclasses import dataclass
from functools import lru_cache
from dotenv import load_dotenv

from .loader import get_config_loader

# Load environment variables from .env file
load_dotenv()


@dataclass
class Settings:
    """
    Application settings

    加载优先级：环境变量 > config.json
    """

    anthropic_api_key: str
    model_name: str
    max_tokens: int
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
        model = config.get('model', 'claude-sonnet-4-20250514')
        max_tokens = config.get('maxTokens', 4096)
        tools = config.get('tools', ['Read', 'Glob', 'Grep', 'Task'])
        host = server.get('host', '127.0.0.1')
        port = server.get('port', 8765)

        # 环境变量覆盖
        api_key = os.getenv('ANTHROPIC_API_KEY', api_key)
        env_model = os.getenv('MODEL_NAME')
        if env_model:
            print(f"环境变量覆盖模型: {env_model}")
            model = env_model
        else:
            print(f"使用配置模型: {model}")

        max_tokens = int(os.getenv('MAX_TOKENS', str(max_tokens)))
        host = os.getenv('SERVER_HOST', host)
        port = int(os.getenv('SERVER_PORT', str(port)))
        project_path = os.getenv('DEFAULT_PROJECT_PATH', '')

        return cls(
            anthropic_api_key=api_key,
            model_name=model,
            max_tokens=max_tokens,
            tools=tools,
            server_host=host,
            server_port=port,
            default_project_path=project_path,
        )


@lru_cache()
def get_settings() -> Settings:
    """Get cached settings instance"""
    return Settings.load()
