"""Static runtime capability declarations for ControlPlane."""

from __future__ import annotations

from typing import Any


RUNTIME_ID = "claude-sdk"
RUNTIME_VERSION = "0.1.0"


_CAPABILITY_MATRIX: tuple[dict[str, Any], ...] = (
    {
        "capabilityKey": "text_stream",
        "level": "required",
        "providerMapping": "content_block_delta.text_delta + text.completed",
        "frontendFallback": None,
        "notes": "MainStream emits text.delta/text.completed with legacy text/text_complete compatibility.",
    },
    {
        "capabilityKey": "tool_call_lifecycle",
        "level": "required",
        "providerMapping": "ToolUseBlock/tool_result -> tool.started/tool.output/tool.completed",
        "frontendFallback": None,
        "notes": "toolCallId is host-stable and reused across the full lifecycle.",
    },
    {
        "capabilityKey": "interaction_query",
        "level": "required",
        "providerMapping": "GET /api/interaction?windowId=<id>",
        "frontendFallback": None,
        "notes": "Host store is the durable truth source for unresolved interactions.",
    },
    {
        "capabilityKey": "interaction_submit",
        "level": "required",
        "providerMapping": "POST /api/interaction/{id}/submit",
        "frontendFallback": None,
        "notes": "submit is idempotent against already-finalized interaction records.",
    },
    {
        "capabilityKey": "interaction_cancel",
        "level": "required",
        "providerMapping": "POST /api/interaction/{id}/cancel",
        "frontendFallback": None,
        "notes": "cancel is idempotent against already-finalized interaction records.",
    },
    {
        "capabilityKey": "question_pause_resume",
        "level": "required",
        "providerMapping": "AskUserQuestion -> PendingInteractionRecord(kind=question, blocking=true)",
        "frontendFallback": None,
        "notes": "paused is derived from blocking pending interactions; no separate pause truth source exists.",
    },
    {
        "capabilityKey": "screenshot_async",
        "level": "required",
        "providerMapping": "PendingInteractionRecord(kind=screenshot, blocking=false)",
        "frontendFallback": None,
        "notes": "Screenshot requests stay async and do not put the session into paused.",
    },
    {
        "capabilityKey": "thinking",
        "level": "optional",
        "providerMapping": "ThinkingBlock + thinking_delta -> thinking.delta/thinking.completed",
        "frontendFallback": "hide-thinking-panel",
        "notes": "Only emitted when the selected model/runtime actually produces thinking content.",
    },
    {
        "capabilityKey": "usage",
        "level": "unsupported",
        "providerMapping": None,
        "frontendFallback": "hide-token-usage",
        "notes": "Slice C still leaves turn usage absent from the public contract.",
    },
    {
        "capabilityKey": "subtask_causality",
        "level": "optional",
        "providerMapping": "Task/TaskOutput + host root subtask projection",
        "frontendFallback": "hide-subtask-activity-panel",
        "notes": "MainStream carries subtaskId/rootSubtaskId but the frontend has not switched to it yet.",
    },
    {
        "capabilityKey": "trace",
        "level": "unsupported",
        "providerMapping": None,
        "frontendFallback": "disable-trace-export",
        "notes": "Baseline validator exists, but runtime trace export is not exposed yet.",
    },
    {
        "capabilityKey": "permission_pause_resume",
        "level": "unsupported",
        "providerMapping": None,
        "frontendFallback": "keep-approval-ui-disabled",
        "notes": "permission interactions remain a reserved contract slot in v0.1.",
    },
)


def build_capability_matrix() -> list[dict[str, Any]]:
    """Return a JSON-safe copy of the runtime capability matrix."""
    return [dict(row) for row in _CAPABILITY_MATRIX]
