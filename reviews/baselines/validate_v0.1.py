"""Validate v0.1 normalized baseline traces."""

from __future__ import annotations

import json
import sys
from pathlib import Path
from typing import Any, Callable


ScenarioValidator = Callable[[dict[str, Any], Path], None]


def _iter_trace_files(target: Path) -> list[Path]:
    if target.is_file():
        return [target]
    if target.is_dir():
        return sorted(path for path in target.rglob("*.normalized.json") if path.is_file())
    raise FileNotFoundError(f"Path not found: {target}")


def _require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def _require_list(trace: dict[str, Any], key: str, source: Path) -> list[dict[str, Any]]:
    value = trace.get(key)
    _require(isinstance(value, list), f"{source}: '{key}' must be a list")
    return value


def _event_payload(event: dict[str, Any]) -> dict[str, Any]:
    payload = event.get("payload")
    return payload if isinstance(payload, dict) else {}


def _event_stop_reason(event: dict[str, Any]) -> str | None:
    stop_reason = _event_payload(event).get("stopReason")
    if isinstance(stop_reason, str):
        return stop_reason
    legacy_stop_reason = event.get("stopReason")
    return legacy_stop_reason if isinstance(legacy_stop_reason, str) else None


def _event_error(event: dict[str, Any]) -> Any:
    payload_error = _event_payload(event).get("error")
    if payload_error is not None:
        return payload_error
    return event.get("error")


def _find_next_match(
    items: list[dict[str, Any]],
    start_index: int,
    predicate: Callable[[dict[str, Any]], bool],
    description: str,
) -> tuple[int, dict[str, Any]]:
    for index in range(start_index, len(items)):
        item = items[index]
        if predicate(item):
            return index, item
    raise ValueError(f"Missing {description}")


def _find_next_event(
    events: list[dict[str, Any]],
    start_index: int,
    event_type: str,
    predicate: Callable[[dict[str, Any]], bool] | None = None,
) -> tuple[int, dict[str, Any]]:
    def _matches(event: dict[str, Any]) -> bool:
        if event.get("eventType") != event_type:
            return False
        return predicate(event) if predicate is not None else True

    return _find_next_match(events, start_index, _matches, f"eventType: {event_type}")


def _find_next_timeline_entry(
    timeline: list[dict[str, Any]],
    start_index: int,
    predicate: Callable[[dict[str, Any]], bool],
    description: str,
) -> tuple[int, dict[str, Any]]:
    return _find_next_match(timeline, start_index, predicate, f"timeline entry: {description}")


def validate_standard_chat_turn(trace: dict[str, Any], source: Path) -> None:
    events = _require_list(trace, "events", source)
    _require(events, f"{source}: 'events' must be a non-empty list")

    index = 0
    index, text_completed = _find_next_event(events, index, "text.completed")
    index, turn_completed = _find_next_event(events, index + 1, "turn.completed")

    _require(
        text_completed.get("turnId") == turn_completed.get("turnId"),
        f"{source}: turn.completed.turnId must equal text.completed.turnId",
    )
    _require(
        _event_stop_reason(turn_completed) == "completed",
        f"{source}: turn.completed.stopReason must be 'completed'",
    )
    _require(
        _event_error(turn_completed) is None,
        f"{source}: turn.completed.payload.error must be absent",
    )


def validate_single_tool_call(trace: dict[str, Any], source: Path) -> None:
    events = _require_list(trace, "events", source)
    _require(events, f"{source}: 'events' must be a non-empty list")

    index = 0
    index, tool_started = _find_next_event(events, index, "tool.started")
    index, tool_output = _find_next_event(events, index + 1, "tool.output")
    index, tool_completed = _find_next_event(events, index + 1, "tool.completed")
    _, turn_completed = _find_next_event(events, index + 1, "turn.completed")

    tool_call_id = tool_started.get("toolCallId")
    _require(tool_call_id, f"{source}: tool.started.toolCallId must exist")
    _require(
        tool_output.get("toolCallId") == tool_call_id == tool_completed.get("toolCallId"),
        f"{source}: tool.started/tool.output/tool.completed toolCallId must match",
    )
    _require(
        _event_payload(tool_completed).get("success") is not None,
        f"{source}: tool.completed.payload.success must be present",
    )
    _require(
        turn_completed.get("turnId") == tool_started.get("turnId"),
        f"{source}: turn.completed.turnId must equal tool.started.turnId",
    )
    _require(
        _event_stop_reason(turn_completed) == "completed",
        f"{source}: turn.completed.stopReason must be 'completed'",
    )


def validate_blocking_interaction_question(trace: dict[str, Any], source: Path) -> None:
    events = _require_list(trace, "events", source)
    interaction_events = _require_list(trace, "interactionEvents", source)
    interaction_queries = _require_list(trace, "interactionQueries", source)
    timeline = _require_list(trace, "timeline", source)

    tool_name_is_question = lambda event: _event_payload(event).get("toolName") == "AskUserQuestion" or event.get("toolName") == "AskUserQuestion"

    index = 0
    index, tool_started = _find_next_event(events, index, "tool.started", tool_name_is_question)
    index, tool_completed = _find_next_event(events, index + 1, "tool.completed", tool_name_is_question)
    _, turn_completed = _find_next_event(events, index + 1, "turn.completed")

    pushed = next(
        (
            item
            for item in interaction_events
            if item.get("event") == "interaction.pushed"
            and isinstance(item.get("record"), dict)
            and item["record"].get("kind") == "question"
            and item["record"].get("blocking") is True
        ),
        None,
    )
    _require(pushed is not None, f"{source}: missing interaction.pushed(kind=question, blocking=true)")

    resolved = next(
        (
            item
            for item in interaction_events
            if item.get("event") == "interaction.resolved"
            and isinstance(item.get("record"), dict)
            and item["record"].get("kind") == "question"
        ),
        None,
    )
    _require(resolved is not None, f"{source}: missing interaction.resolved(kind=question)")

    pushed_record = pushed["record"]
    resolved_record = resolved["record"]

    _require(
        pushed_record.get("interactionId") == resolved_record.get("interactionId"),
        f"{source}: interaction.pushed/resolved interactionId must match",
    )
    _require(
        pushed_record.get("turnId") == tool_started.get("turnId") == tool_completed.get("turnId") == turn_completed.get("turnId"),
        f"{source}: pushed/tool.completed/turn.completed turnId must match",
    )
    _require(
        _event_stop_reason(turn_completed) == "completed",
        f"{source}: turn.completed.stopReason must be 'completed'",
    )

    query_hit_found = False
    interaction_id = pushed_record.get("interactionId")
    for query in interaction_queries:
        interactions = query.get("interactions")
        if not isinstance(interactions, list):
            continue
        if any(isinstance(item, dict) and item.get("interactionId") == interaction_id for item in interactions):
            query_hit_found = True
            break
    _require(
        query_hit_found,
        f"{source}: interactionQueries must contain a pending query hit for the pushed interaction",
    )

    timeline_index = 0
    timeline_index, _ = _find_next_timeline_entry(
        timeline,
        timeline_index,
        lambda item: item.get("stream") == "chat"
        and item.get("eventType") == "tool.started"
        and item.get("toolName") == "AskUserQuestion",
        "chat tool.started(AskUserQuestion)",
    )
    timeline_index, _ = _find_next_timeline_entry(
        timeline,
        timeline_index + 1,
        lambda item: item.get("stream") == "interaction"
        and item.get("event") == "interaction.pushed"
        and item.get("kind") == "question"
        and item.get("blocking") is True,
        "interaction.pushed(kind=question, blocking=true)",
    )
    timeline_index, _ = _find_next_timeline_entry(
        timeline,
        timeline_index + 1,
        lambda item: item.get("stream") == "interaction"
        and item.get("event") == "interaction.resolved"
        and item.get("kind") == "question",
        "interaction.resolved(kind=question)",
    )
    timeline_index, _ = _find_next_timeline_entry(
        timeline,
        timeline_index + 1,
        lambda item: item.get("stream") == "chat"
        and item.get("eventType") == "tool.completed"
        and item.get("toolName") == "AskUserQuestion",
        "chat tool.completed(AskUserQuestion)",
    )
    _find_next_timeline_entry(
        timeline,
        timeline_index + 1,
        lambda item: item.get("stream") == "chat" and item.get("eventType") == "turn.completed",
        "chat turn.completed",
    )


SCENARIO_VALIDATORS: dict[str, ScenarioValidator] = {
    "standard_chat_turn": validate_standard_chat_turn,
    "single_tool_call": validate_single_tool_call,
    "blocking_interaction_question": validate_blocking_interaction_question,
}


def validate_trace_file(source: Path) -> None:
    with source.open("r", encoding="utf-8") as handle:
        trace = json.load(handle)

    scenario = trace.get("scenario")
    _require(isinstance(scenario, str), f"{source}: 'scenario' must be a string")

    validator = SCENARIO_VALIDATORS.get(scenario)
    _require(validator is not None, f"{source}: unsupported scenario '{scenario}'")
    validator(trace, source)


def main(argv: list[str]) -> int:
    if len(argv) != 2:
        print("Usage: python reviews/baselines/validate_v0.1.py <trace-file-or-directory>")
        return 2

    target = Path(argv[1])

    try:
        trace_files = _iter_trace_files(target)
        _require(trace_files, f"No *.normalized.json files found under: {target}")

        for trace_file in trace_files:
            validate_trace_file(trace_file)
            print(f"PASS {trace_file}")
        return 0
    except Exception as exc:
        print(f"FAIL {exc}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
