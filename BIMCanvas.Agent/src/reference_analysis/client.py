"""参考分析客户端：封装 ChatGPT codex/responses 端点的 SSE 调用。"""

from __future__ import annotations

import re
import socket
import uuid
from dataclasses import dataclass
from typing import Literal
from urllib import error as urllib_error

from .config import ChatGPTBackendConfig
from .payload import ReferenceSource, build_request_headers, build_request_payload
from .sse import StreamClientError, collect_final_response, iter_sse_from_http


ReferenceAnalysisErrorType = Literal[
    "config_missing",
    "attachment_missing",
    "token_expired",
    "upstream_http",
    "stream_incomplete",
    "timeout",
    "parse_failed",
]


class ReferenceAnalysisError(Exception):
    """参考分析工具链的统一异常。"""

    def __init__(self, error_type: ReferenceAnalysisErrorType, message: str) -> None:
        super().__init__(message)
        self.error_type: ReferenceAnalysisErrorType = error_type
        self.message = message


@dataclass
class ReferenceAnalysisResult:
    raw_text: str
    section_a: str | None
    section_b: str | None
    section_c: str | None
    response_id: str
    model: str


class ReferenceAnalysisClient:
    """ChatGPT 后端 codex/responses 同步客户端。

    同步实现；MCP 工具用 asyncio.to_thread 包一层使其可在事件循环中调用。
    """

    def __init__(self, config: ChatGPTBackendConfig) -> None:
        self._config = config

    def analyze(
        self,
        reference: ReferenceSource,
        prompt: str,
        *,
        timeout_seconds: int | None = None,
    ) -> ReferenceAnalysisResult:
        cfg = self._config
        timeout = int(timeout_seconds or cfg.timeout_seconds)
        session_id = f"codex-cli-{uuid.uuid4().hex}"

        try:
            payload = build_request_payload(
                prompt=prompt,
                model=cfg.model,
                reference=reference,
                instructions=cfg.instructions,
            )
            headers = build_request_headers(
                access_token=cfg.access_token,
                user_agent=cfg.user_agent,
                version=cfg.version,
                originator=cfg.originator,
                session_id=session_id,
            )
        except ValueError as exc:
            raise ReferenceAnalysisError("config_missing", str(exc)) from exc

        try:
            response = collect_final_response(
                iter_sse_from_http(
                    payload,
                    url=cfg.base_url,
                    headers=headers,
                    timeout_seconds=timeout,
                )
            )
        except StreamClientError as exc:
            raise _map_stream_error(exc, timeout) from exc
        except socket.timeout as exc:
            raise ReferenceAnalysisError(
                "timeout",
                f"reference_analysis_timeout: {timeout}s",
            ) from exc
        except urllib_error.URLError as exc:
            reason = getattr(exc, "reason", exc)
            if isinstance(reason, socket.timeout):
                raise ReferenceAnalysisError(
                    "timeout",
                    f"reference_analysis_timeout: {timeout}s",
                ) from exc
            raise ReferenceAnalysisError(
                "upstream_http",
                f"reference_analysis_network: {exc}",
            ) from exc

        raw_text = _extract_assistant_text(response)
        section_a, section_b, section_c = _split_abc_sections(raw_text)
        response_id = str(response.get("id") or "")
        model_name = str(response.get("model") or cfg.model)

        return ReferenceAnalysisResult(
            raw_text=raw_text,
            section_a=section_a,
            section_b=section_b,
            section_c=section_c,
            response_id=response_id,
            model=model_name,
        )


def _map_stream_error(exc: StreamClientError, timeout: int) -> ReferenceAnalysisError:
    """把 sse 层的 StreamClientError 映射到业务错误码。"""
    message = str(exc)
    # 形如 "HTTP 401: ..."
    http_match = re.match(r"HTTP\s+(\d+)\s*:\s*(.*)", message, flags=re.DOTALL)
    if http_match:
        status = int(http_match.group(1))
        body = (http_match.group(2) or "").strip()[:400]
        if status in (401, 403):
            return ReferenceAnalysisError(
                "token_expired",
                f"chatgpt_backend_unauthorized: HTTP {status}（accessToken 可能已过期）。{body}",
            )
        return ReferenceAnalysisError(
            "upstream_http",
            f"chatgpt_backend_http_{status}: {body}",
        )

    if "未找到完整 response JSON" in message:
        return ReferenceAnalysisError(
            "stream_incomplete",
            f"reference_analysis_stream_incomplete: {message}",
        )

    if "timed out" in message.lower() or "timeout" in message.lower():
        return ReferenceAnalysisError(
            "timeout",
            f"reference_analysis_timeout: {timeout}s ({message})",
        )

    return ReferenceAnalysisError(
        "upstream_http",
        f"chatgpt_backend_error: {message}",
    )


def _extract_assistant_text(response: dict) -> str:
    """从 response.output[].content[].text 里拼接 assistant 文本。"""
    output = response.get("output")
    if not isinstance(output, list):
        return ""

    parts: list[str] = []
    for item in output:
        if not isinstance(item, dict):
            continue
        if item.get("type") != "message":
            continue
        content = item.get("content")
        if not isinstance(content, list):
            continue
        for block in content:
            if not isinstance(block, dict):
                continue
            if block.get("type") not in {"output_text", "text"}:
                continue
            text = block.get("text")
            if isinstance(text, str) and text:
                parts.append(text)

    return "\n".join(parts).strip()


_SECTION_PATTERN = re.compile(
    r"(?m)^\s*([ABCＡＢＣ])[\s\.、．:：)\)]+",
)


def _split_abc_sections(text: str) -> tuple[str | None, str | None, str | None]:
    """正则切 A/B/C 三段；失败时三个都返回 None。"""
    if not text:
        return None, None, None

    markers: dict[str, tuple[int, int]] = {}
    for match in _SECTION_PATTERN.finditer(text):
        key_char = match.group(1)
        # 全角 A/B/C 归一
        if key_char in ("Ａ",):
            key = "A"
        elif key_char in ("Ｂ",):
            key = "B"
        elif key_char in ("Ｃ",):
            key = "C"
        else:
            key = key_char
        if key in markers:
            continue
        markers[key] = (match.start(), match.end())

    if "A" not in markers or "B" not in markers or "C" not in markers:
        return None, None, None

    a_start = markers["A"][0]
    b_start = markers["B"][0]
    c_start = markers["C"][0]

    if not (a_start < b_start < c_start):
        return None, None, None

    section_a = text[a_start:b_start].strip() or None
    section_b = text[b_start:c_start].strip() or None
    section_c = text[c_start:].strip() or None

    return section_a, section_b, section_c
