"""aoment 图像识别后端配置加载。

canvas_vision 的识图后端配置（独立于 reference_analysis 的 ChatGPT 后端）。
从 instance.config.json `agent` 段的 `aomentBackend` 节读取；优先级 env > config > 默认值。
"""

from __future__ import annotations

import os
from dataclasses import dataclass


DEFAULT_ENDPOINT = "https://www.aoment.com/api/aoment/v1/image/recognitions"
DEFAULT_MODEL = "image-recognition-g2"
DEFAULT_TIMEOUT_SECONDS = 90


class AomentConfigError(Exception):
    """aoment 配置加载异常。"""

    def __init__(self, message: str) -> None:
        super().__init__(message)
        self.message = message


@dataclass
class AomentConfig:
    """aoment 图像识别端点连接参数。"""

    api_key: str
    endpoint: str = DEFAULT_ENDPOINT
    model: str = DEFAULT_MODEL
    timeout_seconds: int = DEFAULT_TIMEOUT_SECONDS


def load_aoment_config() -> AomentConfig:
    """从 instance.config.json 的 `aomentBackend` 节加载配置。

    优先级：环境变量 > config 字段 > 默认值。
    缺 apiKey 时抛 AomentConfigError。
    """
    # 延迟导入，避免循环依赖
    from ..config.loader import get_config_loader

    loader = get_config_loader()
    config = loader.load_config()
    raw = config.get("aomentBackend")
    if raw is None:
        raw = {}
    if not isinstance(raw, dict):
        raise AomentConfigError("config.json `aomentBackend` 必须是对象")

    api_key = (os.getenv("AOMENT_API_KEY") or str(raw.get("apiKey") or "")).strip()
    if not api_key:
        raise AomentConfigError(
            "aomentBackend.apiKey 未配置（也未设置 AOMENT_API_KEY 环境变量）。"
            "请到 https://www.aoment.com/aoment/apidoc#quickstart （Aoment 用户中心）"
            "注册并获取 API Key，填入 instance.config.json 的 agent.aomentBackend.apiKey。"
        )

    endpoint = (os.getenv("AOMENT_ENDPOINT") or str(raw.get("endpoint") or DEFAULT_ENDPOINT)).strip() or DEFAULT_ENDPOINT
    model = (os.getenv("AOMENT_MODEL") or str(raw.get("model") or DEFAULT_MODEL)).strip() or DEFAULT_MODEL

    timeout_raw = raw.get("timeoutSeconds", DEFAULT_TIMEOUT_SECONDS)
    try:
        timeout = int(timeout_raw)
    except (TypeError, ValueError):
        timeout = DEFAULT_TIMEOUT_SECONDS
    if timeout < 30:
        timeout = 30
    if timeout > 600:
        timeout = 600

    return AomentConfig(
        api_key=api_key,
        endpoint=endpoint,
        model=model,
        timeout_seconds=timeout,
    )
