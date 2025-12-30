"""Configuration settings for BIMCanvas Agent"""

import os
from dataclasses import dataclass
from functools import lru_cache
from dotenv import load_dotenv

# Load environment variables from .env file
load_dotenv()


@dataclass
class Settings:
    """Application settings"""

    # API Configuration
    anthropic_api_key: str = ""
    model_name: str = "claude-sonnet-4-20250514"
    max_tokens: int = 4096

    # HTTP Server Configuration
    server_host: str = "127.0.0.1"
    server_port: int = 8765

    # Project Configuration
    default_project_path: str = ""

    def __post_init__(self):
        """Load settings from environment variables"""
        self.anthropic_api_key = os.getenv("ANTHROPIC_API_KEY", self.anthropic_api_key)
        self.model_name = os.getenv("MODEL_NAME", self.model_name)
        self.max_tokens = int(os.getenv("MAX_TOKENS", str(self.max_tokens)))
        self.server_host = os.getenv("SERVER_HOST", self.server_host)
        self.server_port = int(os.getenv("SERVER_PORT", str(self.server_port)))
        self.default_project_path = os.getenv("DEFAULT_PROJECT_PATH", self.default_project_path)


@lru_cache()
def get_settings() -> Settings:
    """Get cached settings instance"""
    return Settings()
