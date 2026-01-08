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

        # 调试日志：打印原始配置
        print(f"[Settings] 配置文件原始内容: {config}")

        # 从配置文件读取
        api_key = config.get('apiKey', '')
        model = config.get('model', 'claude-sonnet-4-20250514')
        max_tokens = config.get('maxTokens', 4096)
        tools = config.get('tools', ['Read', 'Glob', 'Grep', 'Task'])
        host = server.get('host', '127.0.0.1')
        port = server.get('port', 8765)

        # 调试日志：打印配置文件中的模型
        print(f"[Settings] 配置文件中的模型: {model}")

        # 环境变量覆盖
        api_key = os.getenv('ANTHROPIC_API_KEY', api_key)
        env_model = os.getenv('MODEL_NAME')
        if env_model:
            print(f"[Settings] 环境变量 MODEL_NAME 覆盖: {env_model}")
            model = env_model
        max_tokens = int(os.getenv('MAX_TOKENS', str(max_tokens)))
        host = os.getenv('SERVER_HOST', host)
        port = int(os.getenv('SERVER_PORT', str(port)))
        project_path = os.getenv('DEFAULT_PROJECT_PATH', '')

        # 调试日志：打印最终模型
        print(f"[Settings] 最终使用的模型: {model}")

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
