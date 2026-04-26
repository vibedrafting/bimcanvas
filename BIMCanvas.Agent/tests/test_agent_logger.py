from __future__ import annotations

import sys
from pathlib import Path


AGENT_ROOT = Path(__file__).resolve().parents[1]
if str(AGENT_ROOT) not in sys.path:
    sys.path.insert(0, str(AGENT_ROOT))

from src.agent.agent_logger import AgentLogger


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


def test_managed_streaming_thinking_is_coalesced_and_flushed_on_end(monkeypatch, capsys) -> None:
    logger = _build_managed_logger(monkeypatch)

    for _ in range(100):
        logger.log_thinking("x", is_delta=True)

    assert capsys.readouterr().out == ""

    logger.log_thinking_end()
    output = capsys.readouterr().out

    assert "x" * 100 in output
    assert output.count("[THINK]") == 1
    assert output.count("\n") == 2


def test_managed_streaming_response_flushes_tail_on_end(monkeypatch, capsys) -> None:
    logger = _build_managed_logger(monkeypatch)

    logger.log_response("final tail", is_delta=True)
    assert capsys.readouterr().out == ""

    logger.log_response_end()
    output = capsys.readouterr().out

    assert "final tail" in output
    assert output.count("[AI]") == 1
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
