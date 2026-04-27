from __future__ import annotations

import asyncio
import hashlib
import sys
from pathlib import Path
from typing import Any

import pytest


AGENT_ROOT = Path(__file__).resolve().parents[1]
if str(AGENT_ROOT) not in sys.path:
    sys.path.insert(0, str(AGENT_ROOT))

from src.mcp import canvas
from src.mcp.canvas import analyze_image
from src.reference_analysis.client import ReferenceAnalysisResult, _split_abc_sections
from src.reference_analysis.prompts import REFERENCE_ANALYSIS_PROMPT_V1


REFERENCE_ANALYSIS_PROMPT_V1_SHA256 = "8fdcb0247bb5c89caf8d08bce6a5aa63cc219dfd80662422e95f69e89eed5799"


class _FakeReferenceAnalysisClient:
    prompts: list[str] = []

    def __init__(self, config: Any) -> None:
        self.config = config

    def analyze(self, reference: Any, prompt: str) -> ReferenceAnalysisResult:
        del reference
        self.prompts.append(prompt)
        raw_text = "A. 空间整体描述\nB. 带文字注释家具清单\nC. 逐个家具分析"
        section_a, section_b, section_c = _split_abc_sections(raw_text)
        return ReferenceAnalysisResult(
            raw_text=raw_text,
            section_a=section_a,
            section_b=section_b,
            section_c=section_c,
            response_id="resp_test",
            model="gpt-test",
        )


@pytest.fixture(autouse=True)
def _fake_backend(monkeypatch: pytest.MonkeyPatch) -> None:
    _FakeReferenceAnalysisClient.prompts = []
    monkeypatch.setattr(canvas, "load_chatgpt_backend_config", lambda: object())
    monkeypatch.setattr(canvas, "ReferenceAnalysisClient", _FakeReferenceAnalysisClient)


def test_analyze_image_defaults_to_custom_and_requires_task() -> None:
    result = asyncio.run(analyze_image.handler({
        "projectPath": "C:/demo/project",
        "base64": "abc",
    }))

    assert result["is_error"] is True
    assert result["content"][0]["text"] == "error: task is required when analysisMode=custom"
    assert _FakeReferenceAnalysisClient.prompts == []


def test_analyze_image_rejects_task_for_reference_layout() -> None:
    result = asyncio.run(analyze_image.handler({
        "projectPath": "C:/demo/project",
        "base64": "abc",
        "analysisMode": "reference_layout",
        "task": "只分析床头朝向",
    }))

    assert result["is_error"] is True
    assert result["content"][0]["text"] == "error: task is not allowed when analysisMode=reference_layout"
    assert _FakeReferenceAnalysisClient.prompts == []


def test_analyze_image_rejects_invalid_analysis_mode() -> None:
    result = asyncio.run(analyze_image.handler({
        "projectPath": "C:/demo/project",
        "base64": "abc",
        "analysisMode": "reference",
    }))

    assert result["is_error"] is True
    assert result["content"][0]["text"] == "error: invalid analysisMode"
    assert _FakeReferenceAnalysisClient.prompts == []


def test_analyze_image_keeps_image_source_xor_rule() -> None:
    result = asyncio.run(analyze_image.handler({
        "projectPath": "C:/demo/project",
        "attachmentId": "att_1",
        "base64": "abc",
        "task": "识别图中文字",
    }))

    assert result["is_error"] is True
    assert result["content"][0]["text"] == "error: provide exactly one of attachmentId/path/base64"
    assert _FakeReferenceAnalysisClient.prompts == []


def test_analyze_image_reference_layout_uses_original_prompt_and_returns_abc() -> None:
    result = asyncio.run(analyze_image.handler({
        "projectPath": "C:/demo/project",
        "base64": "abc",
        "analysisMode": "reference_layout",
    }))

    structured = result["structuredContent"]
    assert _FakeReferenceAnalysisClient.prompts == [REFERENCE_ANALYSIS_PROMPT_V1]
    assert structured["analysisMode"] == "reference_layout"
    assert structured["sectionA"] == "A. 空间整体描述"
    assert structured["sectionB"] == "B. 带文字注释家具清单"
    assert structured["sectionC"] == "C. 逐个家具分析"
    assert structured["rawText"].startswith("A. 空间整体描述")


def test_analyze_image_custom_prompt_returns_task_without_abc_sections() -> None:
    task = "只识别图中的文字标注，并区分家具名称和空间名称。"

    result = asyncio.run(analyze_image.handler({
        "projectPath": "C:/demo/project",
        "base64": "abc",
        "task": task,
    }))

    structured = result["structuredContent"]
    assert len(_FakeReferenceAnalysisClient.prompts) == 1
    assert _FakeReferenceAnalysisClient.prompts[0] != REFERENCE_ANALYSIS_PROMPT_V1
    assert task in _FakeReferenceAnalysisClient.prompts[0]
    assert structured["analysisMode"] == "custom"
    assert structured["task"] == task
    assert structured["sectionA"] is None
    assert structured["sectionB"] is None
    assert structured["sectionC"] is None
    assert structured["rawText"].startswith("A. 空间整体描述")


def test_reference_layout_prompt_text_is_unchanged() -> None:
    digest = hashlib.sha256(REFERENCE_ANALYSIS_PROMPT_V1.encode("utf-8")).hexdigest()

    assert digest == REFERENCE_ANALYSIS_PROMPT_V1_SHA256


def test_analyze_image_schema_exposes_modes_and_task() -> None:
    schema = analyze_image.input_schema

    assert schema["required"] == ["projectPath"]
    assert schema["additionalProperties"] is False
    assert schema["properties"]["analysisMode"]["enum"] == ["custom", "reference_layout"]
    assert "task" in schema["properties"]


def test_analyze_image_description_limits_custom_to_read_failure_fallback() -> None:
    description = analyze_image.description
    analysis_mode_description = analyze_image.input_schema["properties"]["analysisMode"]["description"]
    task_description = analyze_image.input_schema["properties"]["task"]["description"]

    assert "Read 看图失败后的兜底工具" in description
    assert "image result suppressed" in description
    assert "The image couldn't be loaded from that path" in description
    assert "禁止调用 custom" in description
    assert "Read 同一图片失败后兜底使用" in analysis_mode_description
    assert "Read 看图失败后的兜底识图" in task_description


def test_analyze_image_description_limits_reference_layout_to_reference_workflow() -> None:
    description = analyze_image.description
    analysis_mode_description = analyze_image.input_schema["properties"]["analysisMode"]["description"]

    assert "参考图分析 + 设计" in description
    assert "generate-reference-analysis" in description
    assert "Stage A" in description
    assert "constrained planning" in description
    assert "禁止在 chat/query/edit" in description
    assert "free mode planning" in description
    assert "reference_layout 只允许在 generate-reference-analysis Stage A 中使用" in analysis_mode_description
