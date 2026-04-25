from __future__ import annotations

import sys
from pathlib import Path


AGENT_ROOT = Path(__file__).resolve().parents[1]
if str(AGENT_ROOT) not in sys.path:
    sys.path.insert(0, str(AGENT_ROOT))

from src.mcp.canvas import _resolve_screenshot_viewports, request_background_screenshot


def _resolved(args: dict) -> list[dict]:
    viewports, err = _resolve_screenshot_viewports(args)
    assert err is None
    assert viewports is not None
    return viewports


def test_screenshot_tool_schema_keeps_project_path_required() -> None:
    schema = request_background_screenshot.input_schema

    assert schema["required"] == ["projectPath"]
    assert schema["additionalProperties"] is False


def test_screenshot_tool_descriptions_emphasize_required_project_path() -> None:
    schema = request_background_screenshot.input_schema
    tool_description = request_background_screenshot.description
    project_path_description = schema["properties"]["projectPath"]["description"]

    assert "projectPath" in tool_description
    assert "必须传入" in tool_description
    assert "不可省略" in tool_description
    assert "项目路径" in project_path_description
    assert "禁止使用 BIMCANVAS_HOME" in project_path_description
    assert "skill/plugin 目录" in project_path_description


def test_screenshot_params_default_to_full_project_shot() -> None:
    assert _resolved({"projectPath": "C:/demo/project"}) == [{"mode": "full"}]


def test_screenshot_params_accept_simple_target_id() -> None:
    assert _resolved({"projectPath": "C:/demo/project", "targetId": "rz_3"}) == [{"id": "rz_3"}]


def test_screenshot_params_accept_simple_target_ids() -> None:
    assert _resolved({"projectPath": "C:/demo/project", "targetIds": ["rz_1", "rz_2"]}) == [
        {"id": "rz_1"},
        {"id": "rz_2"},
    ]


def test_screenshot_params_single_inputs_win_over_batch_inputs() -> None:
    assert _resolved({
        "projectPath": "C:/demo/project",
        "targetId": "rz_main",
        "shots": [{"targetId": "rz_ignored"}],
    }) == [{"id": "rz_main"}]

    assert _resolved({
        "projectPath": "C:/demo/project",
        "viewport": {"mode": "zone", "zoneId": "rz_view"},
        "shots": [{"targetId": "rz_ignored"}],
    }) == [{"mode": "zone", "zoneId": "rz_view"}]


def test_screenshot_params_shot_target_id_wins_over_shot_viewport() -> None:
    assert _resolved({
        "projectPath": "C:/demo/project",
        "shots": [
            {"targetId": "rz_1", "viewport": {"mode": "full"}},
            {"viewport": {"mode": "zone", "zoneId": "rz_2"}},
        ],
    }) == [
        {"id": "rz_1"},
        {"mode": "zone", "zoneId": "rz_2"},
    ]


def test_screenshot_params_keep_advanced_bounds_viewports() -> None:
    bounds = {"minX": 0, "minY": 1, "maxX": 2, "maxY": 3}

    assert _resolved({
        "projectPath": "C:/demo/project",
        "viewport": {"bounds": bounds},
    }) == [{"mode": "bounds", "bounds": bounds}]

    assert _resolved({
        "projectPath": "C:/demo/project",
        "shots": [{"viewport": {"mode": "bounds", "bounds": bounds}}],
    }) == [{"mode": "bounds", "bounds": bounds}]


def test_screenshot_params_empty_targets_fall_back_to_full() -> None:
    assert _resolved({"projectPath": "C:/demo/project", "targetIds": ["", "  "]}) == [
        {"mode": "full"}
    ]
    assert _resolved({"projectPath": "C:/demo/project", "shots": []}) == [{"mode": "full"}]


def test_screenshot_params_reject_invalid_objects() -> None:
    viewports, err = _resolve_screenshot_viewports({
        "projectPath": "C:/demo/project",
        "shots": ["rz_1"],
    })
    assert viewports is None
    assert err == "shots[0] 必须是对象"

    viewports, err = _resolve_screenshot_viewports({
        "projectPath": "C:/demo/project",
        "shots": [{"viewport": "bad"}],
    })
    assert viewports is None
    assert err == "shots[0] viewport 必须是对象"
