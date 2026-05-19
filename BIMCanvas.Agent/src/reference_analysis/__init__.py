"""Generic 图像分析后端（ChatGPT codex/responses）。

虽然历史包名为 `reference_analysis`,实际内容是通用图像分析 backend:
- HTTP 客户端 (`client.py`) — ChatGPT codex/responses API 调用
- 后端配置 (`config.py`) — `load_chatgpt_backend_config()`
- 图像入参数据类 (`payload.py`) — `ReferenceSource`
- SSE 流处理 (`sse.py`)
- generic prompt 构造器 (`prompts.py`) — `build_custom_image_analysis_prompt(task)`

组5 §5.A.4 拆分后:
- indoor-layout 专属的 reference layout 分析 prompt 已迁到 plugin
  (`plugins/indoor-layout/mcp_tools/lib/reference_prompts/reference_analysis_prompt_v1.md`)
- 任何 plugin 都可通过 `mcp__canvas__analyze_image(task=<prompt-text>)` 复用本后端
- Phase 2 视需要把包名改为 `image_analysis/`(主真理源 §1.1 / §4.1 配套修订由指挥部承接)

暴露给 platform 内部 (MCP 工具 / 独立 CLI) 的公开接口。
"""

from .client import (
    ReferenceAnalysisClient,
    ReferenceAnalysisError,
    ReferenceAnalysisErrorType,
    ReferenceAnalysisResult,
)
from .config import ChatGPTBackendConfig, load_chatgpt_backend_config
from .payload import ReferenceSource
from .prompts import build_custom_image_analysis_prompt

__all__ = [
    "ChatGPTBackendConfig",
    "ReferenceAnalysisClient",
    "ReferenceAnalysisError",
    "ReferenceAnalysisErrorType",
    "ReferenceAnalysisResult",
    "ReferenceSource",
    "build_custom_image_analysis_prompt",
    "load_chatgpt_backend_config",
]
