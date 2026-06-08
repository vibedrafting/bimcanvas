"""canvas_vision 识图后端配置加载（多 provider 可配）。

支持的 provider：
- apiyi : OpenAI Chat Completions 格式（JSON，image_url base64 data URL），多模型可选
- aoment: 自定义 multipart/form-data，固定 image-recognition-g2

从 instance.config.json `agent.imageRecognition` 读取：
  {
    "provider": "apiyi",
    "providers": {
      "apiyi":  { "apiKey": "", "endpoint": "...", "model": "gemini-3.5-flash", "timeoutSeconds": 90 },
      "aoment": { "apiKey": "", "endpoint": "...", "model": "image-recognition-g2", "timeoutSeconds": 90 }
    }
  }
优先级：环境变量（provider 专属）> providers.<provider> 字段 > 默认值。
"""

from __future__ import annotations

import os
from dataclasses import dataclass


DEFAULT_PROVIDER = "apiyi"
DEFAULT_TIMEOUT_SECONDS = 90

# 各 provider 的默认 endpoint / model
_PROVIDER_DEFAULTS: dict[str, dict[str, str]] = {
    "apiyi": {
        "endpoint": "https://api.apiyi.com/v1/chat/completions",
        "model": "gemini-3.5-flash",
    },
    "aoment": {
        "endpoint": "https://www.aoment.com/api/aoment/v1/image/recognitions",
        "model": "image-recognition-g2",
    },
}

# apiKey 缺失时的注册引导链接
_PROVIDER_SIGNUP: dict[str, str] = {
    "apiyi": "https://www.apiyi.com",
    "aoment": "https://www.aoment.com",
}

# provider 专属环境变量（覆盖 config 的 apiKey）
_PROVIDER_ENV_KEY: dict[str, str] = {
    "apiyi": "APIYI_API_KEY",
    "aoment": "AOMENT_API_KEY",
}


class RecognitionConfigError(Exception):
    """识图配置加载异常。"""

    def __init__(self, message: str) -> None:
        super().__init__(message)
        self.message = message


@dataclass
class RecognitionConfig:
    """选中 provider 的识图端点连接参数。"""

    provider: str
    api_key: str
    endpoint: str
    model: str
    timeout_seconds: int = DEFAULT_TIMEOUT_SECONDS


def load_recognition_config() -> RecognitionConfig:
    """从 instance.config.json 的 `imageRecognition` 节加载选中 provider 的配置。

    provider 由 `imageRecognition.provider`（或 env IMAGE_RECOGNITION_PROVIDER）决定，默认 apiyi。
    缺 apiKey 时抛 RecognitionConfigError（含 provider 专属注册引导链接）。
    """
    # 延迟导入，避免循环依赖
    from ..config.loader import get_config_loader

    loader = get_config_loader()
    config = loader.load_config()
    raw = config.get("imageRecognition")
    if raw is None:
        raw = {}
    if not isinstance(raw, dict):
        raise RecognitionConfigError("config.json `imageRecognition` 必须是对象")

    provider = (
        os.getenv("IMAGE_RECOGNITION_PROVIDER") or str(raw.get("provider") or DEFAULT_PROVIDER)
    ).strip().lower()
    if provider not in _PROVIDER_DEFAULTS:
        raise RecognitionConfigError(
            f"imageRecognition.provider 不支持: {provider!r}（可选 {' / '.join(_PROVIDER_DEFAULTS)}）"
        )

    providers = raw.get("providers")
    section = providers.get(provider) if isinstance(providers, dict) else None
    if not isinstance(section, dict):
        section = {}

    defaults = _PROVIDER_DEFAULTS[provider]
    env_key = _PROVIDER_ENV_KEY[provider]

    api_key = (os.getenv(env_key) or str(section.get("apiKey") or "")).strip()
    if not api_key:
        signup = _PROVIDER_SIGNUP[provider]
        raise RecognitionConfigError(
            f"imageRecognition.providers.{provider}.apiKey 未配置（也未设置 {env_key} 环境变量）。"
            f"请到 {signup} （{provider} 用户中心）注册并获取 API Key，"
            f"填入 instance.config.json 的 agent.imageRecognition.providers.{provider}.apiKey。"
        )

    endpoint = str(section.get("endpoint") or defaults["endpoint"]).strip() or defaults["endpoint"]
    model = str(section.get("model") or defaults["model"]).strip() or defaults["model"]

    timeout_raw = section.get("timeoutSeconds", DEFAULT_TIMEOUT_SECONDS)
    try:
        timeout = int(timeout_raw)
    except (TypeError, ValueError):
        timeout = DEFAULT_TIMEOUT_SECONDS
    if timeout < 30:
        timeout = 30
    if timeout > 600:
        timeout = 600

    return RecognitionConfig(
        provider=provider,
        api_key=api_key,
        endpoint=endpoint,
        model=model,
        timeout_seconds=timeout,
    )
