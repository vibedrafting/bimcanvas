"""canvas_vision 识图后端（多 provider：apiyi / aoment）。

暴露 config 加载接口；HTTP 调用经 canvas_vision 工具内 ctx.session 直发
（apiyi=OpenAI JSON，aoment=multipart）。
"""

from .config import (
    RecognitionConfig,
    RecognitionConfigError,
    load_recognition_config,
)

__all__ = [
    "RecognitionConfig",
    "RecognitionConfigError",
    "load_recognition_config",
]
