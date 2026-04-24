"""ChatGPT 后端配置加载。"""

from __future__ import annotations

import os
from dataclasses import dataclass


DEFAULT_BASE_URL = "https://chatgpt.com/backend-api/codex/responses"
DEFAULT_MODEL = "gpt-5.4"
DEFAULT_INSTRUCTIONS = "you are a helpful assistant"
DEFAULT_USER_AGENT = (
    "codex-tui/0.122.0 (Manjaro 26.1.0-pre; x86_64) "
    "vscode/3.0.12 (codex-tui; 0.122.0)"
)
DEFAULT_VERSION = "0.122.0"
DEFAULT_ORIGINATOR = "codex_cli_rs"
DEFAULT_TIMEOUT_SECONDS = 600


@dataclass
class ChatGPTBackendConfig:
    """ChatGPT codex/responses 端点的连接参数。"""

    access_token: str
    base_url: str = DEFAULT_BASE_URL
    model: str = DEFAULT_MODEL
    instructions: str = DEFAULT_INSTRUCTIONS
    user_agent: str = DEFAULT_USER_AGENT
    version: str = DEFAULT_VERSION
    originator: str = DEFAULT_ORIGINATOR
    timeout_seconds: int = DEFAULT_TIMEOUT_SECONDS

    @classmethod
    def from_env_fallback(cls) -> "ChatGPTBackendConfig":
        """脱离 BIMCANVAS_HOME 环境时的兜底：只从环境变量读取。

        专供 test/ 下独立 CLI 调试使用；正式路���走 load_chatgpt_backend_config。
        """
        token = (os.getenv("CHATGPT_ACCESS_TOKEN") or "").strip()
        if not token:
            raise ValueError(
                "CHATGPT_ACCESS_TOKEN 未设置，无法脱离 BIMCANVAS_HOME 运行调试。"
            )
        return cls(
            access_token=token,
            base_url=(os.getenv("CHATGPT_BASE_URL") or DEFAULT_BASE_URL).strip(),
            model=(os.getenv("CHATGPT_MODEL") or DEFAULT_MODEL).strip(),
        )


def load_chatgpt_backend_config() -> ChatGPTBackendConfig:
    """从 <BIMCANVAS_HOME>/config.json 的 chatgptBackend 节加载配置。

    优先级：环境变量 > config.json 字段 > dataclass 默认值。

    缺 access_token 时抛 ReferenceAnalysisError("config_missing", ...)。
    """
    # 延迟导入，避免循环依赖
    from ..config.loader import get_config_loader
    from .client import ReferenceAnalysisError

    loader = get_config_loader()
    config = loader.load_config()
    raw = config.get("chatgptBackend")
    if raw is None:
        raw = {}
    if not isinstance(raw, dict):
        raise ReferenceAnalysisError(
            "config_missing",
            "config.json `chatgptBackend` 必须是对象",
        )

    access_token = (os.getenv("CHATGPT_ACCESS_TOKEN") or str(raw.get("accessToken") or "")).strip()
    if not access_token:
        raise ReferenceAnalysisError(
            "config_missing",
            "chatgptBackend.accessToken 未配置（也未设置 CHATGPT_ACCESS_TOKEN 环境变量）",
        )

    base_url = (os.getenv("CHATGPT_BASE_URL") or str(raw.get("baseUrl") or DEFAULT_BASE_URL)).strip() or DEFAULT_BASE_URL
    model = (os.getenv("CHATGPT_MODEL") or str(raw.get("model") or DEFAULT_MODEL)).strip() or DEFAULT_MODEL

    timeout_raw = raw.get("timeoutSeconds", DEFAULT_TIMEOUT_SECONDS)
    try:
        timeout = int(timeout_raw)
    except (TypeError, ValueError):
        timeout = DEFAULT_TIMEOUT_SECONDS
    if timeout < 30:
        timeout = 30
    if timeout > 1200:
        timeout = 1200

    return ChatGPTBackendConfig(
        access_token=access_token,
        base_url=base_url,
        model=model,
        instructions=str(raw.get("instructions") or DEFAULT_INSTRUCTIONS),
        user_agent=str(raw.get("userAgent") or DEFAULT_USER_AGENT),
        version=str(raw.get("version") or DEFAULT_VERSION),
        originator=str(raw.get("originator") or DEFAULT_ORIGINATOR),
        timeout_seconds=timeout,
    )
