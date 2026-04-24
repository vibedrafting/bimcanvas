"""参考图分析客户端（ChatGPT codex/responses 后端）。

暴露给 MCP 工具 / 独立 CLI 的公开接口。
"""

from .client import (
    ReferenceAnalysisClient,
    ReferenceAnalysisError,
    ReferenceAnalysisErrorType,
    ReferenceAnalysisResult,
)
from .config import ChatGPTBackendConfig, load_chatgpt_backend_config
from .payload import ReferenceSource
from .prompts import REFERENCE_ANALYSIS_PROMPT_V1, load_reference_analysis_prompt

__all__ = [
    "ChatGPTBackendConfig",
    "REFERENCE_ANALYSIS_PROMPT_V1",
    "ReferenceAnalysisClient",
    "ReferenceAnalysisError",
    "ReferenceAnalysisErrorType",
    "ReferenceAnalysisResult",
    "ReferenceSource",
    "load_chatgpt_backend_config",
    "load_reference_analysis_prompt",
]
