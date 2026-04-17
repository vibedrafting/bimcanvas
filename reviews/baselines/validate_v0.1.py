"""Validate v0.1 normalized baseline traces."""

from __future__ import annotations

import json
import sys
from pathlib import Path


MANDATORY_SEQUENCE = ["text.completed", "turn.completed"]


def _iter_trace_files(target: Path) -> list[Path]:
    if target.is_file():
        return [target]
    if target.is_dir():
        return sorted(path for path in target.rglob("*.normalized.json") if path.is_file())
    raise FileNotFoundError(f"Path not found: {target}")


def _require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def _find_next_event(events: list[dict], start_index: int, event_type: str) -> tuple[int, dict]:
    for index in range(start_index, len(events)):
        event = events[index]
        if event.get("eventType") == event_type:
            return index, event
    raise ValueError(f"Missing eventType: {event_type}")


def validate_standard_chat_turn(trace: dict, source: Path) -> None:
    events = trace.get("events")
    _require(isinstance(events, list) and events, f"{source}: 'events' must be a non-empty list")

    index = 0
    matched: dict[str, dict] = {}
    for event_type in MANDATORY_SEQUENCE:
        index, event = _find_next_event(events, index, event_type)
        matched[event_type] = event
        index += 1

    text_completed = matched["text.completed"]
    turn_completed = matched["turn.completed"]

    _require(
        text_completed.get("turnId") == turn_completed.get("turnId"),
        f"{source}: turn.completed.turnId must equal text.completed.turnId",
    )
    _require(
        turn_completed.get("stopReason") == "completed",
        f"{source}: turn.completed.stopReason must be 'completed'",
    )


def validate_trace_file(source: Path) -> None:
    with source.open("r", encoding="utf-8") as handle:
        trace = json.load(handle)

    scenario = trace.get("scenario")
    _require(scenario == "standard_chat_turn", f"{source}: unsupported scenario '{scenario}'")
    validate_standard_chat_turn(trace, source)


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
