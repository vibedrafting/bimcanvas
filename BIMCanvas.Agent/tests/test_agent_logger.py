from __future__ import annotations

import sys
import re
from pathlib import Path


AGENT_ROOT = Path(__file__).resolve().parents[1]
if str(AGENT_ROOT) not in sys.path:
    sys.path.insert(0, str(AGENT_ROOT))

from src.agent import agent_logger as agent_logger_module
from src.agent.agent_logger import AgentLogger


ANSI_RE = re.compile(r"\x1b\[[0-9;]*m")


def _strip_ansi(text: str) -> str:
    return ANSI_RE.sub("", text)


def _build_managed_logger(monkeypatch, *, stream_logs: bool = False, tool_logs: bool = False) -> AgentLogger:
    monkeypatch.setenv("BIMCANVAS_AGENT_MANAGED_BY_SERVER", "1")
    if stream_logs:
        monkeypatch.setenv("BIMCANVAS_AGENT_STREAM_LOGS", "1")
    else:
        monkeypatch.delenv("BIMCANVAS_AGENT_STREAM_LOGS", raising=False)

    if tool_logs:
        monkeypatch.setenv("BIMCANVAS_AGENT_TOOL_RESULT_LOGS", "1")
    else:
        monkeypatch.delenv("BIMCANVAS_AGENT_TOOL_RESULT_LOGS", raising=False)

    return AgentLogger(window_seq=1)


def test_managed_streaming_thinking_flushes_without_repeating_think_label(monkeypatch, capsys) -> None:
    logger = _build_managed_logger(monkeypatch)
    monkeypatch.setattr(agent_logger_module, "MANAGED_STREAM_LOG_FLUSH_MAX_CHARS", 3)

    logger.log_thinking_start()
    for part in ["abc", "def", "ghi"]:
        logger.log_thinking(part, is_delta=True)

    intermediate = _strip_ansi(capsys.readouterr().out)
    assert "abcdefghi" in intermediate
    assert intermediate.count("[MainAgent] [THINK]") == 1
    assert intermediate.count("\n") == 1

    logger.log_thinking_end()
    output = _strip_ansi(capsys.readouterr().out)

    assert "─ thinking complete ─" in output


def test_managed_streaming_preserves_model_newlines(monkeypatch, capsys) -> None:
    logger = _build_managed_logger(monkeypatch)

    logger.log_thinking("first line\nsecond line", is_delta=True)
    logger.log_thinking_end()
    output = _strip_ansi(capsys.readouterr().out)

    assert "[Agent#1] first line" in output
    assert "[Agent#1] second line" in output
    assert output.count("\n") == 3


def test_managed_streaming_response_flushes_tail_on_end(monkeypatch, capsys) -> None:
    logger = _build_managed_logger(monkeypatch)

    logger.log_response_start()
    logger.log_response("final tail", is_delta=True)
    intermediate = _strip_ansi(capsys.readouterr().out)
    assert "[MainAgent] [AI]" in intermediate

    logger.log_response_end()
    output = _strip_ansi(capsys.readouterr().out)

    assert "final tail" in output
    assert output.count("\n") == 1


def test_managed_read_result_logs_single_line_summary(monkeypatch, capsys) -> None:
    logger = _build_managed_logger(monkeypatch)
    result = "1->visible line\n2->SECRET_FILE_BODY\n3->more content"

    logger.log_tool_result("Read", result)
    output = capsys.readouterr().out

    assert output.count("\n") == 1
    assert "file content suppressed" in output
    assert "lines=3" in output
    assert "SECRET_FILE_BODY" not in output


def test_managed_image_result_suppresses_base64_payload(monkeypatch, capsys) -> None:
    logger = _build_managed_logger(monkeypatch)
    image_data = "a" * 4096

    logger.log_tool_result(
        "mcp__canvas__request_background_screenshot",
        [{"type": "image", "source": {"data": image_data}}],
    )
    output = capsys.readouterr().out

    assert output.count("\n") == 1
    assert "image result suppressed" in output
    assert image_data[:128] not in output


def test_managed_structured_tool_result_does_not_expand_multiline_json(monkeypatch, capsys) -> None:
    logger = _build_managed_logger(monkeypatch)

    logger.log_tool_result(
        "mcp__canvas__get_zone_boundaries",
        [{"type": "text", "text": "line one\nline two"}],
    )
    output = capsys.readouterr().out

    assert output.count("\n") == 1
    assert "structured result suppressed" in output
    assert "line one" not in output
    assert "line two" not in output


def test_managed_unknown_line_numbered_text_result_is_suppressed(monkeypatch, capsys) -> None:
    logger = _build_managed_logger(monkeypatch)
    result = "\n".join(
        [
            "     1→# 卧室策略",
            "     2→",
            "     3→适用：tags 含 `sleep` 或 `bedroom` 的封闭空间。",
            "     4→---",
            "     5→正文内容",
        ]
    )

    logger.log_tool_result("unknown", result)
    output = capsys.readouterr().out

    assert output.count("\n") == 1
    assert "text result suppressed" in output
    assert "卧室策略" not in output


def test_stream_log_debug_flag_uses_raw_streaming_path(monkeypatch) -> None:
    logger = _build_managed_logger(monkeypatch, stream_logs=True)
    calls: list[str] = []
    monkeypatch.setattr(logger, "_print_streaming", lambda content, color="": calls.append(content))

    logger.log_response("a", is_delta=True)
    logger.log_response("b", is_delta=True)

    assert calls == ["a", "b"]


def test_tool_result_debug_flag_allows_full_payload(monkeypatch, capsys) -> None:
    logger = _build_managed_logger(monkeypatch, tool_logs=True)

    logger.log_tool_result("Read", "alpha\nbeta")
    output = capsys.readouterr().out

    assert "alpha" in output
    assert "beta" in output
    assert "file content suppressed" not in output
