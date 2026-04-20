"""Runtime provider metadata and capability declarations."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any


CLAUDE_RUNTIME_ID = "claude-sdk"
OPENAI_RUNTIME_ID = "openai-agents"
DEFAULT_RUNTIME_PROVIDER = CLAUDE_RUNTIME_ID
RUNTIME_VERSION = "0.1.0"


@dataclass(frozen=True)
class RuntimeDescriptor:
    runtime_id: str
    runtime_version: str
    capability_matrix: tuple[dict[str, Any], ...]


_CLAUDE_CAPABILITY_MATRIX: tuple[dict[str, Any], ...] = (
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

_OPENAI_CAPABILITY_MATRIX: tuple[dict[str, Any], ...] = (
    {
        "capabilityKey": "text_stream",
        "level": "required",
        "providerMapping": "RawResponsesStreamEvent(response.output_text.delta) + MessageOutputItem",
        "frontendFallback": None,
        "notes": "OpenAI raw delta and semantic message items are projected to the shared MainStream contract.",
    },
    {
        "capabilityKey": "tool_call_lifecycle",
        "level": "required",
        "providerMapping": "RunItemStreamEvent(tool_called/tool_output) -> tool.started/tool.output/tool.completed",
        "frontendFallback": None,
        "notes": "provider call_id is kept private; host generates opaque toolCallId values.",
    },
    {
        "capabilityKey": "interaction_query",
        "level": "required",
        "providerMapping": "GET /api/interaction?windowId=<id>",
        "frontendFallback": None,
        "notes": "Pending question truth remains in the host interaction store.",
    },
    {
        "capabilityKey": "interaction_submit",
        "level": "required",
        "providerMapping": "POST /api/interaction/{id}/submit -> RunState resume",
        "frontendFallback": None,
        "notes": "submit resolves the public record first, then resumes the paused RunState using the private binding.",
    },
    {
        "capabilityKey": "interaction_cancel",
        "level": "required",
        "providerMapping": "POST /api/interaction/{id}/cancel",
        "frontendFallback": None,
        "notes": "cancel terminates the public interaction and drops the private resume binding.",
    },
    {
        "capabilityKey": "question_pause_resume",
        "level": "required",
        "providerMapping": "FunctionTool(needs_approval=True) -> ToolApprovalItem -> PendingInteractionRecord",
        "frontendFallback": None,
        "notes": "OpenAI native RunState becomes the durable pause/resume checkpoint behind resumeToken.",
    },
    {
        "capabilityKey": "screenshot_async",
        "level": "required",
        "providerMapping": "PendingInteractionRecord(kind=screenshot, blocking=false)",
        "frontendFallback": None,
        "notes": "Screenshot interactions stay host-owned and keep resume:none semantics.",
    },
    {
        "capabilityKey": "thinking",
        "level": "optional",
        "providerMapping": "RawResponsesStreamEvent(response.reasoning*.delta) + ReasoningItem",
        "frontendFallback": "hide-thinking-panel",
        "notes": "Only emitted when the selected OpenAI model returns reasoning content.",
    },
    {
        "capabilityKey": "usage",
        "level": "unsupported",
        "providerMapping": None,
        "frontendFallback": "hide-token-usage",
        "notes": "v0.1 still keeps token usage outside the public protocol.",
    },
    {
        "capabilityKey": "subtask_causality",
        "level": "unsupported",
        "providerMapping": None,
        "frontendFallback": "hide-subtask-activity-panel",
        "notes": "BIMCanvas OpenAI Runtime v0.1 收口为 chat_completions + streaming 主路径。configured subagents（含 layout-agent）在 chat_completions 下不注册；helper workers（delegate_query_task/delegate_edit_task）保留 SDK 原生 Agent.as_tool() 机制，但其 subtask 事件链在第三方 OpenAI-compatible provider 下不作完整可见性承诺，前端走 hide-subtask-activity-panel 降级。responses + 官方 OpenAI endpoint 下保留 layout-agent 作为实验性 opt-in，不作稳定承诺。",
    },
    {
        "capabilityKey": "trace",
        "level": "unsupported",
        "providerMapping": None,
        "frontendFallback": "disable-trace-export",
        "notes": "Provider raw trace IDs remain private to the adapter.",
    },
    {
        "capabilityKey": "permission_pause_resume",
        "level": "unsupported",
        "providerMapping": None,
        "frontendFallback": "keep-approval-ui-disabled",
        "notes": "OpenAI v0.1 first pass only wires AskUserQuestion -> kind=question.",
    },
)


_DESCRIPTORS: dict[str, RuntimeDescriptor] = {
    CLAUDE_RUNTIME_ID: RuntimeDescriptor(
        runtime_id=CLAUDE_RUNTIME_ID,
        runtime_version=RUNTIME_VERSION,
        capability_matrix=_CLAUDE_CAPABILITY_MATRIX,
    ),
    OPENAI_RUNTIME_ID: RuntimeDescriptor(
        runtime_id=OPENAI_RUNTIME_ID,
        runtime_version=RUNTIME_VERSION,
        capability_matrix=_OPENAI_CAPABILITY_MATRIX,
    ),
}


def normalize_runtime_provider(value: str | None) -> str:
    normalized = (value or DEFAULT_RUNTIME_PROVIDER).strip().lower()
    if normalized in {"claude", "claude-sdk"}:
        return CLAUDE_RUNTIME_ID
    if normalized in {"openai", "openai-agents"}:
        return OPENAI_RUNTIME_ID
    return normalized or DEFAULT_RUNTIME_PROVIDER


def get_runtime_descriptor(runtime_provider: str | None = None) -> RuntimeDescriptor:
    normalized = normalize_runtime_provider(runtime_provider)
    descriptor = _DESCRIPTORS.get(normalized)
    if descriptor is None:
        raise ValueError(f"Unsupported runtime provider: {runtime_provider}")
    return descriptor


def build_capability_matrix(runtime_provider: str | None = None) -> list[dict[str, Any]]:
    descriptor = get_runtime_descriptor(runtime_provider)
    return [dict(row) for row in descriptor.capability_matrix]
