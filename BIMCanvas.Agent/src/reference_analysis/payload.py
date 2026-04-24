"""请求 payload 构造与参考图归一化。"""

from __future__ import annotations

import base64
import mimetypes
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Literal


@dataclass
class ReferenceSource:
    """参考图来源描述。

    - mode="path": value 是本地文件路径，会被读取并��成 data URL
    - mode="base64": value 是纯 base64 字符串或完整 data URL
    """

    mode: Literal["path", "base64"]
    value: str
    mime: str | None = None


def normalize_reference_source(source: ReferenceSource) -> str:
    """统一把参考图归一化为可直接发给后端的 image_url 字符串（data URL 形式）。"""
    value = (source.value or "").strip()
    if not value:
        raise ValueError("参考图来源为空")

    if source.mode == "base64":
        if value.startswith("data:"):
            return value
        mime = (source.mime or "image/png").strip() or "image/png"
        return f"data:{mime};base64,{value}"

    if source.mode == "path":
        file_path = Path(value).expanduser()
        if not file_path.exists():
            raise ValueError(f"参考图文件不存在: {file_path}")
        if not file_path.is_file():
            raise ValueError(f"参考图路径不是文件: {file_path}")

        mime_type = (source.mime or "").strip()
        if not mime_type:
            guessed, _ = mimetypes.guess_type(file_path.name)
            mime_type = guessed or "image/png"
        if not mime_type.startswith("image/"):
            mime_type = "image/png"

        encoded = base64.b64encode(file_path.read_bytes()).decode("ascii")
        return f"data:{mime_type};base64,{encoded}"

    raise ValueError(f"未知的 ReferenceSource.mode: {source.mode}")


def build_request_payload(
    *,
    prompt: str,
    model: str,
    reference: ReferenceSource,
    instructions: str,
) -> dict[str, Any]:
    """构造发给 ChatGPT codex/responses 端点的 SSE 请求体。"""
    prompt_text = (prompt or "").strip()
    if not prompt_text:
        raise ValueError("提示词不能为空")

    model_name = (model or "").strip()
    if not model_name:
        raise ValueError("模型名不能为空")

    image_url = normalize_reference_source(reference)

    content: list[dict[str, str]] = [
        {"type": "input_text", "text": prompt_text},
        {"type": "input_image", "image_url": image_url},
    ]

    return {
        "model": model_name,
        "input": [
            {
                "role": "user",
                "content": content,
            }
        ],
        "tools": [
            {
                "type": "image_generation",
                "output_format": "png",
            }
        ],
        "instructions": instructions or "you are a helpful assistant",
        "tool_choice": "auto",
        "stream": True,
        "store": False,
    }


def build_request_headers(
    *,
    access_token: str,
    user_agent: str,
    version: str,
    originator: str,
    session_id: str,
) -> dict[str, str]:
    """构造伪装成 Codex CLI 的请求 headers。"""
    token = (access_token or "").strip()
    if not token:
        raise ValueError("access_token 不能为空")

    return {
        "Authorization": f"Bearer {token}",
        "user-agent": user_agent,
        "version": version,
        "originator": originator,
        "session_id": session_id,
        "accept": "text/event-stream",
        "Content-Type": "application/json",
    }
