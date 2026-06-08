"""aoment 图像识别后端（canvas_vision 识图侧）。

暴露 config 加载接口；HTTP 调用经 canvas_vision 工具内 ctx.session multipart 直发。
"""

from .config import AomentConfig, AomentConfigError, load_aoment_config

__all__ = [
    "AomentConfig",
    "AomentConfigError",
    "load_aoment_config",
]
