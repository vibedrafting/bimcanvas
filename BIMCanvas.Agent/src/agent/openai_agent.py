"""OpenAI Agents SDK adapter for the BIMCanvas host contract."""

from __future__ import annotations

import asyncio
from dataclasses import dataclass
import importlib
import inspect
import json
import logging
import os
import sys
from pathlib import Path
from typing import Any
from urllib.parse import urlparse

from pydantic import BaseModel, Field
from typing_extensions import TypedDict

from ..config.configured_agents import parse_configured_agent_requirements
from ..config.loader import AgentConfig
from ..config.settings import get_settings
from ..mcp.canvas_core import CORE_ALLOWED_TOOLS as CANVAS_ALLOWED_TOOLS
from ..runtime import (
    ConfigBundle,
    PendingInteractionRuntimeBinding,
    RuntimeSessionRecord,
    StreamChunk,
    build_config_bundle,
)
from ..runtime.openai_stream import (
    AGENT_TOOL_RESULT_MARKER,
    OpenAIStreamTranslator,
    SUBTASK_ERROR_MARKER,
)
from .agent_logger import get_agent_logger
from .errors import TurnPausedError

logger = logging.getLogger(__name__)

_OPENAI_LOCAL_TOOL_NAMES = frozenset({
    "Read", "Write", "Edit", "Glob", "Grep", "Bash", "AskUserQuestion", "Skill",
})
_OPENAI_QUERY_DELEGATE_TOOL_ORDER = ("Read", "Glob", "Grep")
_OPENAI_EDIT_DELEGATE_TOOL_ORDER = ("Read", "Write", "Edit", "Glob", "Grep")
_OPENAI_DELEGATE_QUERY_TOOL_NAME = "delegate_query_task"
_OPENAI_DELEGATE_EDIT_TOOL_NAME = "delegate_edit_task"
_OPENAI_LAYOUT_AGENT_NAME = "layout-agent"
_OPENAI_LAYOUT_AGENT_SKILL_NAMES = ("generate-planning", "generate-placement")
_OPENAI_LAYOUT_AGENT_MCP_TOOL_ORDER = (
    *CANVAS_ALLOWED_TOOLS,
)
_OPENAI_DEFAULT_PERMISSION_TOOL_NAMES = frozenset({
    "Read", "Write", "Edit", "Glob", "Grep", "Bash",
    "AskUserQuestion", "Task", "Skill",
    *_OPENAI_LAYOUT_AGENT_MCP_TOOL_ORDER,
})
_OPENAI_RESERVED_AGENT_TOOL_NAMES = frozenset({
    *_OPENAI_LOCAL_TOOL_NAMES,
    _OPENAI_DELEGATE_QUERY_TOOL_NAME,
    _OPENAI_DELEGATE_EDIT_TOOL_NAME,
    *_OPENAI_LAYOUT_AGENT_MCP_TOOL_ORDER,
})
_OPENAI_CONFIGURABLE_PERMISSION_TOOL_NAMES = frozenset({
    *_OPENAI_LOCAL_TOOL_NAMES,
    "Task",
    *_OPENAI_LAYOUT_AGENT_MCP_TOOL_ORDER,
})
_CLAUDE_MODEL_ALIASES = frozenset({"opus", "sonnet", "haiku"})


class QuestionOption(TypedDict, total=False):
    label: str
    description: str


class QuestionDef(TypedDict, total=False):
    id: str
    header: str
    question: str
    options: list[QuestionOption]


class DelegationTaskInput(BaseModel):
    """委派子任务给配置型子代理（如 layout-agent）或 helper agent 的结构化输入。"""

    task_title: str = Field(
        description=(
            "子任务的简短标题，5-15 字，仅用于 UI 气泡和日志追溯。"
            "示例：'为 rz_1 执行施工落位'、'查询公卫家具清单'。"
            "不要把任务的详细上下文塞进这里。"
        )
    )
    task_prompt: str = Field(
        description=(
            "子任务的完整上下文和执行要求。子代理只能看到这个字段里的内容来理解任务。"
            "必须包含以下要素（按任务类型适配）：\n"
            "1) 目标对象标识（如分区 ID rz_1、文件路径）；\n"
            "2) 用户原始需求的关键信息（用户说了什么、要做什么）；\n"
            "3) 上游已完成的工作状态（例如'construction-brief 语义方案已保存'、'已读取 xxx.json'），"
            "以便子代理知道从哪一步开始；\n"
            "4) 预期产出（要写入的文件、要返回的信息）；\n"
            "5) 相关约束或注意事项。\n"
            "写得像在对一个只看到这段文字、不知道前文的同事交代工作——"
            "子代理确实看不到主控的对话历史。"
        )
    )


@dataclass(frozen=True)
class _ConfiguredAgentToolSpec:
    config: AgentConfig
    tool_names: tuple[str, ...]
    model: str | None
    required_permission_names: tuple[str, ...] = ()


@dataclass(frozen=True)
class _BlockedConfiguredAgentSpec:
    name: str
    reasons: tuple[str, ...]


@dataclass(frozen=True)
class _ExplicitConfiguredAgentRequest:
    name: str
    enabled_spec: _ConfiguredAgentToolSpec | None = None
    blocked_spec: _BlockedConfiguredAgentSpec | None = None


def _project_root() -> Path:
    return Path(__file__).resolve().parents[3]


def _load_openai_agents_module() -> Any:
    try:
        return importlib.import_module("agents")
    except ModuleNotFoundError:
        reference_src = _project_root() / "references" / "src" / "openai-agents-python" / "src"
        if str(reference_src) not in sys.path:
            sys.path.insert(0, str(reference_src))
        try:
            return importlib.import_module("agents")
        except ModuleNotFoundError as exc:
            raise RuntimeError(
                "OpenAI runtime dependencies are missing. Run `pip install -e BIMCanvas.Agent` "
                "to install openai-agents and its transitive requirements."
            ) from exc


def _get_attr(value: Any, name: str, default: Any = None) -> Any:
    if isinstance(value, dict):
        return value.get(name, default)
    return getattr(value, name, default)


def _resolve_provider_call_id(value: Any) -> str | None:
    raw_value = _get_attr(value, "raw_item", value)
    return _get_attr(raw_value, "call_id") or _get_attr(raw_value, "id")


class OpenAIAgent:
    """Host-facing adapter built on top of the OpenAI Agents SDK."""

    runtime_id = "openai"
    runtime_version = "0.1.0"

    def __init__(
        self,
        project_path: str | None = None,
        working_directory: str | None = None,
        window_seq: int = 0,
        verbose: bool = True,
    ) -> None:
        self.project_path = project_path
        self.working_directory = working_directory or project_path
        self.window_seq = window_seq
        self.verbose = verbose
        self._bundle: ConfigBundle | None = None
        self._connected = False
        self._current_model: str | None = None
        self._runtime_context: dict[str, str] | None = None
        self._active_stream_result: Any | None = None
        self._sdk_session: Any | None = None
        self._sdk_session_id: str | None = None
        self._phase_one_scope_logged = False
        self._responses_run_fallback_logged = False
        self._configured_subagents_logged = False
        self._agent_logger = get_agent_logger("OpenAIAgent", window_seq=self.window_seq)

    @property
    def is_connected(self) -> bool:
        return self._connected

    def set_runtime_context(self, runtime_context: dict[str, str] | None) -> None:
        self._runtime_context = dict(runtime_context) if runtime_context else None

    def clear_runtime_context(self) -> None:
        self._runtime_context = None

    def configure(self, bundle: ConfigBundle) -> None:
        self._bundle = bundle

    def _require_bundle(self) -> ConfigBundle:
        if self._bundle is None:
            self.configure(build_config_bundle())
        assert self._bundle is not None
        return self._bundle

    async def connect(
        self,
        effort: str | None = None,
        thinking: str | None = None,
        model: str | None = None,
    ) -> None:
        settings = get_settings()
        agents = _load_openai_agents_module()
        openai_module = importlib.import_module("openai")
        async_openai = openai_module.AsyncOpenAI
        client_kwargs: dict[str, Any] = {"api_key": settings.openai_api_key}
        if settings.base_url:
            client_kwargs["base_url"] = settings.base_url
        client = async_openai(**client_kwargs)
        agents.set_default_openai_client(client=client, use_for_tracing=False)
        agents.set_default_openai_api(settings.openai_api)
        agents.set_tracing_disabled(settings.openai_disable_tracing)
        if model:
            self._validate_requested_model(model)
            self._current_model = model
        self._log_phase_one_scope()
        self._connected = True

    async def disconnect(self) -> None:
        if self._active_stream_result is not None:
            try:
                self._active_stream_result.cancel()
            except Exception:
                pass
        self._active_stream_result = None
        self._close_sdk_session()
        self._connected = False

    async def set_model(self, model: str) -> bool:
        self._validate_requested_model(model)
        self._current_model = model
        self._connected = True
        return True

    async def interrupt(self) -> None:
        if self._active_stream_result is not None:
            self._active_stream_result.cancel()

    def get_current_model(self) -> str | None:
        return self._current_model

    async def chat(
        self,
        user_message: str,
        model: str | None = None,
        runtime_context: dict[str, str] | None = None,
    ) -> str:
        delta_parts: list[str] = []
        completed_parts: list[str] = []
        async for chunk in self.chat_stream(
            user_message,
            model=model,
            runtime_context=runtime_context,
        ):
            if chunk.type == "text" and chunk.content:
                delta_parts.append(chunk.content)
            elif chunk.type == "text_complete" and chunk.content:
                completed_parts.append(chunk.content)
        return "".join(completed_parts) if completed_parts else "".join(delta_parts)

    async def chat_stream(
        self,
        user_message: str,
        images: list[str] | None = None,
        image_blocks: list[dict[str, Any]] | None = None,
        client_message_id: str | None = None,
        effort: str | None = None,
        thinking: str | None = None,
        model: str | None = None,
        context: dict[str, Any] | None = None,
        runtime_context: dict[str, str] | None = None,
    ):
        self.set_runtime_context(runtime_context)
        consumer_task: asyncio.Task[Any] | None = None
        try:
            if user_message:
                self._agent_logger.log_user_message(user_message)
            elif images or image_blocks:
                self._agent_logger.log_user_message("[attachment-only message]")
            if client_message_id:
                self._agent_logger.log_info(f"[Attachment] clientMessageId={client_message_id}")

            if not self.is_connected:
                await self.connect(effort=effort, thinking=thinking, model=model)
            elif model and model != self._current_model:
                await self.set_model(model)

            agents = _load_openai_agents_module()
            settings = get_settings()
            translator = OpenAIStreamTranslator(turn_id=(runtime_context or {}).get("turnId", "turn"))
            stream_queue: asyncio.Queue[StreamChunk | Exception | None] = asyncio.Queue()
            buffered_nested_chunks: list[StreamChunk] = []
            use_fallback = self._should_use_responses_run_fallback(settings)
            enabled_permission_tool_names = self._resolve_enabled_permission_tool_names()
            configured_specs, blocked_specs = self._resolve_configured_agent_tool_specs(
                enabled_tool_names=enabled_permission_tool_names,
                inherited_model=model,
            )
            self._log_configured_subagent_availability(
                enabled_specs=configured_specs,
                blocked_specs=blocked_specs,
            )
            explicit_request = self._resolve_explicit_configured_agent_request(
                user_message,
                enabled_specs=configured_specs,
                blocked_specs=blocked_specs,
            )
            if explicit_request and explicit_request.blocked_spec is not None:
                unavailable_message = self._build_explicit_configured_agent_unavailable_message(explicit_request)
                logger.warning(
                    "OpenAI runtime refused to substitute helper workers for explicitly requested configured agent `%s`: %s",
                    explicit_request.name,
                    ", ".join(explicit_request.blocked_spec.reasons),
                )
                unavailable_chunk = StreamChunk(type="text_complete", content=unavailable_message)
                self._log_chunk_for_console(unavailable_chunk)
                yield unavailable_chunk
                self._agent_logger.log_complete(model=model or self._current_model)
                return

            async def _emit_translated_event(
                event: Any,
                *,
                forced_subtask_id: str | None = None,
            ) -> None:
                for chunk in translator.translate(event, forced_subtask_id=forced_subtask_id):
                    await stream_queue.put(chunk)

            async def _buffer_chunk(chunk: StreamChunk) -> None:
                buffered_nested_chunks.append(chunk)

            async def _nested_stream_handler(payload: Any) -> None:
                emit_chunk = _buffer_chunk if use_fallback else stream_queue.put
                try:
                    await self._emit_nested_agent_stream_event(
                        payload=payload,
                        translator=translator,
                        emit_chunk=emit_chunk,
                    )
                except Exception as exc:
                    await emit_chunk(self._build_sdk_error_chunk(exc, error_content="nested_stream_handler"))
                    raise

            run_context = self._build_run_context(runtime_context=runtime_context, canvas_context=context)
            starting_agent = self._build_root_agent(
                agents,
                model=model,
                nested_stream_handler=_nested_stream_handler,
                user_message=user_message,
                enabled_permission_tool_names=enabled_permission_tool_names,
                configured_specs=configured_specs,
                explicit_request=explicit_request,
            )
            sdk_session = self._get_or_create_sdk_session(
                agents,
                session_id=(runtime_context or {}).get("sessionId"),
            )
            input_items = self._build_input_items(
                user_message=user_message,
                images=images or [],
                image_blocks=image_blocks or [],
                canvas_context=context or {},
            )

            if use_fallback:
                self._log_responses_run_fallback()
                result_task = asyncio.create_task(
                    agents.Runner.run(
                        starting_agent=starting_agent,
                        input=input_items,
                        context=run_context,
                        max_turns=30,
                        session=sdk_session,
                    )
                )
                self._active_stream_result = result_task
                self._current_model = model or self._current_model
                result = await result_task

                for chunk in buffered_nested_chunks:
                    self._log_chunk_for_console(chunk)
                    yield chunk
                for chunk in self._translate_result_chunks(result=result, translator=translator):
                    self._log_chunk_for_console(chunk)
                    yield chunk

                if getattr(result, "interruptions", None):
                    interaction_id = await self._push_pending_question_interaction(
                        result=result,
                        translator=translator,
                        runtime_context=runtime_context,
                    )
                    raise TurnPausedError(interaction_id)

                self._agent_logger.log_complete(model=model or self._current_model)
                return

            result = agents.Runner.run_streamed(
                starting_agent=starting_agent,
                input=input_items,
                context=run_context,
                max_turns=30,
                session=sdk_session,
            )
            self._active_stream_result = result
            self._current_model = model or self._current_model

            async def _consume_result() -> None:
                try:
                    async for event in result.stream_events():
                        await _emit_translated_event(event)

                    if getattr(result, "interruptions", None):
                        interaction_id = await self._push_pending_question_interaction(
                            result=result,
                            translator=translator,
                            runtime_context=runtime_context,
                        )
                        await stream_queue.put(TurnPausedError(interaction_id))
                except Exception as exc:
                    await stream_queue.put(exc)
                finally:
                    await stream_queue.put(None)

            consumer_task = asyncio.create_task(_consume_result())

            while True:
                item = await stream_queue.get()
                if item is None:
                    break
                if isinstance(item, TurnPausedError):
                    raise item
                if isinstance(item, Exception):
                    raise item
                if self._should_suppress_root_text_chunk(item, translator=translator):
                    continue
                self._log_chunk_for_console(item)
                yield item

            root_failure_chunk = self._maybe_build_root_failure_summary_chunk(translator=translator)
            if root_failure_chunk is not None:
                self._log_chunk_for_console(root_failure_chunk)
                yield root_failure_chunk

            self._agent_logger.log_complete(model=model or self._current_model)
        finally:
            if consumer_task is not None and not consumer_task.done():
                consumer_task.cancel()
                try:
                    await consumer_task
                except asyncio.CancelledError:
                    pass
            self._active_stream_result = None
            self.clear_runtime_context()

    async def resume_interaction_stream(
        self,
        *,
        interaction_id: str,
        binding: PendingInteractionRuntimeBinding,
        resolution_payload: dict[str, Any],
        session: RuntimeSessionRecord,
    ):
        """恢复 OpenAI Runtime 的 pause 检查点，流式产出 StreamChunk。

        遇到新的 pause（连续 AskUserQuestion）时抛 TurnPausedError，由调用方
        （chat_stream_handler）接住后重新进入 wait-resume 循环。
        """
        agents = _load_openai_agents_module()
        settings = get_settings()
        await self.connect(model=self._current_model)

        answers = resolution_payload.get("answers", {})
        if not isinstance(answers, dict):
            answers = {}

        state_payload = json.loads(binding.run_state_json or "{}")
        resume_context = state_payload.get("context", {})
        if not isinstance(resume_context, dict):
            resume_context = {}
        answers_by_call_id = dict(resume_context.get("questionAnswersByCallId", {}))
        answers_by_call_id[binding.approval_call_id] = answers
        resume_context = dict(resume_context)
        resume_context["questionAnswersByCallId"] = answers_by_call_id
        stream_queue: asyncio.Queue[StreamChunk | Exception | None] = asyncio.Queue()
        buffered_nested_chunks: list[StreamChunk] = []
        translator = OpenAIStreamTranslator(
            turn_id=session.active_turn_id or binding.turn_id,
            projection_state=binding.projection_state,
        )
        use_fallback = self._should_use_responses_run_fallback(settings)
        approved_tool_call_id = (
            binding.public_tool_call_id
            or translator.ensure_public_tool_call_id(binding.approval_call_id)
        )
        consumer_task: asyncio.Task[Any] | None = None

        async def _buffer_chunk(chunk: StreamChunk) -> None:
            buffered_nested_chunks.append(chunk)

        async def _nested_stream_handler(payload: Any) -> None:
            emit_chunk = _buffer_chunk if use_fallback else stream_queue.put
            try:
                await self._emit_nested_agent_stream_event(
                    payload=payload,
                    translator=translator,
                    emit_chunk=emit_chunk,
                )
            except Exception as exc:
                await emit_chunk(self._build_sdk_error_chunk(exc, error_content="nested_stream_handler"))
                raise

        starting_agent = self._build_root_agent(
            agents,
            model=self._current_model,
            nested_stream_handler=_nested_stream_handler,
        )
        sdk_session = self._get_or_create_sdk_session(
            agents,
            session_id=session.session_id,
        )

        async def _emit_translated_event(
            event: Any,
            *,
            forced_subtask_id: str | None = None,
        ) -> None:
            for chunk in translator.translate(event, forced_subtask_id=forced_subtask_id):
                await stream_queue.put(chunk)
        state = await agents.RunState.from_json(
            starting_agent,
            state_payload,
            context_override=resume_context,
        )

        approval_item = None
        for candidate in state.get_interruptions():
            if getattr(candidate, "call_id", None) == binding.approval_call_id:
                approval_item = candidate
                break

        if approval_item is None:
            raise RuntimeError(f"Missing approval item for interaction {interaction_id}")

        state.approve(approval_item)

        runtime_context_for_new_pause = {
            "windowId": session.window_id,
            "sessionId": session.session_id,
            "turnId": binding.turn_id,
        }

        if use_fallback:
            self._log_responses_run_fallback()
            result_task = asyncio.create_task(
                agents.Runner.run(
                    starting_agent,
                    state,
                    context=resume_context,
                    session=sdk_session,
                )
            )
            self._active_stream_result = result_task
            try:
                result = await result_task

                for chunk in buffered_nested_chunks:
                    yield chunk
                for chunk in self._translate_result_chunks(result=result, translator=translator):
                    if approved_tool_call_id and chunk.tool_call_id == approved_tool_call_id:
                        chunk.tool_output = None
                        chunk.suppress_public_tool_output = True
                    yield chunk

                if getattr(result, "interruptions", None):
                    new_interaction_id = await self._push_pending_question_interaction(
                        result=result,
                        translator=translator,
                        runtime_context=runtime_context_for_new_pause,
                    )
                    raise TurnPausedError(new_interaction_id)
            finally:
                self._active_stream_result = None
            return

        result = agents.Runner.run_streamed(
            starting_agent,
            state,
            context=resume_context,
            session=sdk_session,
        )
        self._active_stream_result = result

        try:
            async def _consume_result() -> None:
                try:
                    async for event in result.stream_events():
                        await _emit_translated_event(event)

                    if getattr(result, "interruptions", None):
                        new_interaction_id = await self._push_pending_question_interaction(
                            result=result,
                            translator=translator,
                            runtime_context=runtime_context_for_new_pause,
                        )
                        await stream_queue.put(TurnPausedError(new_interaction_id))
                except Exception as exc:
                    await stream_queue.put(exc)
                finally:
                    await stream_queue.put(None)

            consumer_task = asyncio.create_task(_consume_result())

            while True:
                item = await stream_queue.get()
                if item is None:
                    break
                if isinstance(item, TurnPausedError):
                    raise item
                if isinstance(item, Exception):
                    raise item
                chunk = item
                if approved_tool_call_id and chunk.tool_call_id == approved_tool_call_id:
                    chunk.tool_output = None
                    chunk.suppress_public_tool_output = True
                if self._should_suppress_root_text_chunk(chunk, translator=translator):
                    continue
                yield chunk

            root_failure_chunk = self._maybe_build_root_failure_summary_chunk(translator=translator)
            if root_failure_chunk is not None:
                yield root_failure_chunk
        finally:
            if consumer_task is not None and not consumer_task.done():
                consumer_task.cancel()
                try:
                    await consumer_task
                except asyncio.CancelledError:
                    pass
            self._active_stream_result = None

    def _build_root_agent(
        self,
        agents: Any,
        *,
        model: str | None,
        nested_stream_handler: Any | None,
        user_message: str | None = None,
        enabled_permission_tool_names: list[str] | None = None,
        configured_specs: list[_ConfiguredAgentToolSpec] | None = None,
        explicit_request: _ExplicitConfiguredAgentRequest | None = None,
    ) -> Any:
        resolved_enabled_permission_tool_names = (
            enabled_permission_tool_names
            if enabled_permission_tool_names is not None
            else self._resolve_enabled_permission_tool_names()
        )
        if configured_specs is None:
            configured_specs, blocked_specs = self._resolve_configured_agent_tool_specs(
                enabled_tool_names=resolved_enabled_permission_tool_names,
                inherited_model=model,
            )
            self._log_configured_subagent_availability(
                enabled_specs=configured_specs,
                blocked_specs=blocked_specs,
            )
        tool_by_name = self._build_local_function_tool_map(agents)
        bundle = self._require_bundle()
        system_prompt = bundle.system_prompt
        system_prompt = system_prompt + f"\n\n工作目录: {self.working_directory}"
        system_prompt = system_prompt + self._build_openai_root_appendix(
            explicit_request=explicit_request,
        )
        return agents.Agent(
            name="BIMCanvas",
            instructions=system_prompt,
            tools=self._build_tools(
                agents,
                model=model,
                nested_stream_handler=nested_stream_handler,
                tool_by_name=tool_by_name,
                enabled_permission_tool_names=resolved_enabled_permission_tool_names,
                configured_specs=configured_specs,
                explicit_request=explicit_request,
            ),
            model=model or self._current_model,
        )

    def _build_tools(
        self,
        agents: Any,
        *,
        model: str | None,
        nested_stream_handler: Any | None,
        tool_by_name: dict[str, Any] | None = None,
        enabled_permission_tool_names: list[str] | None = None,
        configured_specs: list[_ConfiguredAgentToolSpec] | None = None,
        explicit_request: _ExplicitConfiguredAgentRequest | None = None,
    ) -> list[Any]:
        resolved_tool_by_name = tool_by_name or self._build_local_function_tool_map(agents)
        resolved_enabled_permission_tool_names = (
            enabled_permission_tool_names
            if enabled_permission_tool_names is not None
            else self._resolve_enabled_permission_tool_names()
        )
        if configured_specs is None:
            configured_specs, blocked_specs = self._resolve_configured_agent_tool_specs(
                enabled_tool_names=resolved_enabled_permission_tool_names,
                inherited_model=model,
            )
            self._log_configured_subagent_availability(
                enabled_specs=configured_specs,
                blocked_specs=blocked_specs,
            )
        local_tool_names = [
            name
            for name in resolved_tool_by_name
            if name in _OPENAI_LOCAL_TOOL_NAMES and name in resolved_enabled_permission_tool_names
        ]
        local_tools = [resolved_tool_by_name[name] for name in local_tool_names]
        configured_tools = [
            self._build_configured_agent_tool(
                agents,
                spec=spec,
                nested_stream_handler=nested_stream_handler,
                tool_by_name=resolved_tool_by_name,
            )
            for spec in self._order_configured_specs_for_root(
                configured_specs=configured_specs,
                explicit_request=explicit_request,
            )
        ]
        return [
            *local_tools,
            *configured_tools,
            *self._build_helper_agent_tools(
                agents,
                model=model,
                nested_stream_handler=nested_stream_handler,
                tool_by_name=resolved_tool_by_name,
                explicit_request=explicit_request,
            ),
        ]

    def _build_local_function_tool_map(self, agents: Any) -> dict[str, Any]:
        from .openai_tools import file_read, file_write, file_edit, glob_tool, grep_tool, bash_tool

        function_tool = agents.function_tool
        working_directory = Path(self.working_directory or self.project_path or ".").resolve()
        tool_context_type = importlib.import_module("agents.tool_context").ToolContext

        def with_tool_context(*, tool_name: str):
            def decorator(fn: Any) -> Any:
                fn.__annotations__["ctx"] = tool_context_type
                return function_tool(name_override=tool_name)(fn)

            return decorator

        @function_tool(name_override="Read")
        async def read_file(file_path: str, offset: int | None = None, limit: int | None = None) -> str:
            """Read a text file. Returns cat -n formatted output. Use offset/limit for large files."""
            return file_read.read(working_directory, file_path, offset=offset, limit=limit)

        @function_tool(name_override="Write")
        async def write_file(file_path: str, content: str) -> str:
            """Write content to a file. Atomic write preserves encoding and line endings."""
            return file_write.write(working_directory, file_path, content)

        @function_tool(name_override="Edit")
        async def edit_file(file_path: str, old_string: str, new_string: str, replace_all: bool = False) -> str:
            """Replace text in a file. old_string must be unique unless replace_all is True."""
            return file_edit.edit(working_directory, file_path, old_string, new_string, replace_all)

        @function_tool(name_override="Glob")
        async def glob_files(pattern: str, path: str | None = None) -> str:
            """Find files by glob pattern. Results sorted by modification time (newest first)."""
            return await glob_tool.glob(working_directory, pattern, path=path)

        @function_tool(name_override="Grep")
        async def grep_files(
            pattern: str,
            path: str | None = None,
            include_glob: str | None = None,
            file_type: str | None = None,
            output_mode: str = "files_with_matches",
            case_insensitive: bool = False,
            line_numbers: bool = True,
            before_context: int | None = None,
            after_context: int | None = None,
            context: int | None = None,
            head_limit: int = 250,
            offset: int = 0,
            multiline: bool = False,
        ) -> str:
            """Search file contents with regex. Modes: files_with_matches, content, count."""
            return await grep_tool.grep(
                working_directory, pattern,
                path=path, include_glob=include_glob, file_type=file_type,
                output_mode=output_mode, case_insensitive=case_insensitive,
                line_numbers=line_numbers, before_context=before_context,
                after_context=after_context, context=context,
                head_limit=head_limit, offset=offset, multiline=multiline,
            )

        @function_tool(name_override="Bash")
        async def run_shell(command: str, timeout: int | None = None) -> str:
            """Run a shell command. Default timeout 120s."""
            return await bash_tool.run_shell(
                working_directory, command,
                timeout=float(timeout) if timeout else None,
            )

        async def ask_user_question(ctx: Any, questions: list[QuestionDef]) -> dict[str, Any]:
            """Ask the user one or more structured questions and resume after the answer arrives."""
            answers_by_call_id = {}
            context_mapping = getattr(ctx, "context", None)
            if isinstance(context_mapping, dict):
                answers_by_call_id = context_mapping.get("questionAnswersByCallId", {})
            answers = answers_by_call_id.get(getattr(ctx, "tool_call_id", None), {})
            if not isinstance(answers, dict):
                answers = {}
            return {"answers": answers}
        ask_user_question.__annotations__["ctx"] = tool_context_type
        ask_user_question = function_tool(
            name_override="AskUserQuestion",
            needs_approval=True,
        )(ask_user_question)

        @with_tool_context(tool_name="mcp__canvas__request_background_screenshot")
        async def canvas_request_background_screenshot(
            ctx: Any,
            projectPath: str | None = None,
            zoneId: str = "",
        ) -> Any:
            """Render a background screenshot for the current project or a specific zone.

            Args:
                projectPath: Optional BIMCanvas project path. Defaults to the current runtime project.
                zoneId: Target zone id for the current single-zone task.
            """
            args: dict[str, Any] = {}
            resolved_project_path = self._resolve_canvas_project_path(ctx, projectPath)
            if resolved_project_path:
                args["projectPath"] = resolved_project_path
            normalized_zone_id = zoneId.strip() if isinstance(zoneId, str) else ""
            if not normalized_zone_id:
                raise ValueError("zoneId is required for mcp__canvas__request_background_screenshot")
            args["viewport"] = {"id": normalized_zone_id}
            return await self._invoke_canvas_tool_impl("request_background_screenshot", args)

        @with_tool_context(tool_name="mcp__canvas__get_zone_boundaries")
        async def canvas_get_zone_boundaries(
            ctx: Any,
            zoneId: str | None = None,
            zoneIds: list[str] | None = None,
        ) -> Any:
            """Read semantic boundary segments for one or more zones.

            Args:
                zoneId: Optional single zone shortcut.
                zoneIds: Optional list of zone IDs.
            """
            del ctx
            args: dict[str, Any] = {}
            resolved_zone_ids = list(zoneIds or [])
            if zoneId:
                resolved_zone_ids = [zoneId, *[value for value in resolved_zone_ids if value != zoneId]]
            if resolved_zone_ids:
                args["zoneIds"] = resolved_zone_ids
            return await self._invoke_canvas_tool_impl("get_zone_boundaries", args)

        @with_tool_context(tool_name="mcp__canvas__save_semantic_plan")
        async def canvas_save_semantic_plan(
            ctx: Any,
            zoneId: str,
            tag: str,
            planType: str,
            content: str,
            referenceAnalysisTag: str | None = None,
            variantId: str | None = None,
        ) -> Any:
            """Save a semantic plan snapshot for the current zone."""
            del ctx
            args: dict[str, Any] = {
                "zoneId": zoneId,
                "tag": tag,
                "planType": planType,
                "content": content,
            }
            if referenceAnalysisTag:
                args["referenceAnalysisTag"] = referenceAnalysisTag
            if variantId:
                args["variantId"] = variantId
            return await self._invoke_canvas_tool_impl("save_semantic_plan", args)

        @with_tool_context(tool_name="mcp__canvas__save_modules")
        async def canvas_save_modules(
            ctx: Any,
            designZoneId: str,
            leafZoneId: str,
            modules: list[dict[str, Any]],
            variantId: str | None = None,
        ) -> Any:
            """Save modules.json wrapper (schemeMetadata derived by Server)."""
            del ctx
            args: dict[str, Any] = {
                "designZoneId": designZoneId,
                "leafZoneId": leafZoneId,
                "modules": modules,
            }
            if variantId:
                args["variantId"] = variantId
            return await self._invoke_canvas_tool_impl("save_modules", args)

        @with_tool_context(tool_name="mcp__canvas__clone_scheme_to_variant")
        async def canvas_clone_scheme_to_variant(
            ctx: Any,
            designZoneId: str,
            newVariantSlugs: list[str],
            sourceVariant: str | None = None,
            overwrite: bool = False,
        ) -> Any:
            """Clone canonical/variant directory to new variant slugs (relocation entry)."""
            del ctx
            args: dict[str, Any] = {
                "designZoneId": designZoneId,
                "newVariantSlugs": newVariantSlugs,
                "overwrite": overwrite,
            }
            if sourceVariant:
                args["sourceVariant"] = sourceVariant
            return await self._invoke_canvas_tool_impl("clone_scheme_to_variant", args)

        @with_tool_context(tool_name="mcp__canvas__load_semantic_plan")
        async def canvas_load_semantic_plan(
            ctx: Any,
            zoneId: str,
            variantId: str | None = None,
        ) -> Any:
            """Load the effective semantic plan for the current zone."""
            del ctx
            args: dict[str, Any] = {"zoneId": zoneId}
            if variantId:
                args["variantId"] = variantId
            return await self._invoke_canvas_tool_impl("load_semantic_plan", args)

        @with_tool_context(tool_name="mcp__canvas__validate_layout")
        async def canvas_validate_layout(
            ctx: Any,
            zoneId: str | None = None,
            zoneIds: list[str] | None = None,
        ) -> Any:
            """Validate layout legality for one zone or for the whole project."""
            del ctx
            args: dict[str, Any] = {}
            resolved_zone_ids = list(zoneIds or [])
            if zoneId:
                resolved_zone_ids = [zoneId, *[value for value in resolved_zone_ids if value != zoneId]]
            if resolved_zone_ids:
                args["zoneIds"] = resolved_zone_ids
            return await self._invoke_canvas_tool_impl("validate_layout", args)

        @with_tool_context(tool_name="mcp__canvas__load_reference_analysis")
        async def canvas_load_reference_analysis(
            ctx: Any,
            zoneId: str,
            tag: str | None = None,
        ) -> Any:
            """Load the latest or a fixed reference-analysis snapshot for the current zone."""
            del ctx
            args = {"zoneId": zoneId}
            if tag:
                args["tag"] = tag
            return await self._invoke_canvas_tool_impl("load_reference_analysis", args)

        @with_tool_context(tool_name="mcp__canvas__save_reference_analysis")
        async def canvas_save_reference_analysis(
            ctx: Any,
            zoneId: str,
            content: str,
            sourceImageId: str | None = None,
        ) -> Any:
            """Save a structured reference-analysis snapshot for the current zone."""
            del ctx
            args = {"zoneId": zoneId, "content": content}
            if sourceImageId:
                args["sourceImageId"] = sourceImageId
            return await self._invoke_canvas_tool_impl("save_reference_analysis", args)

        @with_tool_context(tool_name="mcp__canvas__analyze_image")
        async def canvas_analyze_image(
            ctx: Any,
            projectPath: str | None = None,
            attachmentId: str | None = None,
            path: str | None = None,
            base64: str | None = None,
            analysisMode: str | None = None,
            task: str | None = None,
        ) -> Any:
            """Analyze an image attachment with either a custom task or reference-layout preset."""
            args: dict[str, Any] = {}
            resolved_project_path = self._resolve_canvas_project_path(ctx, projectPath)
            if resolved_project_path:
                args["projectPath"] = resolved_project_path
            if attachmentId:
                args["attachmentId"] = attachmentId
            if path:
                args["path"] = path
            if base64:
                args["base64"] = base64
            if analysisMode:
                args["analysisMode"] = analysisMode
            if task:
                args["task"] = task
            return await self._invoke_canvas_tool_impl("analyze_image", args)

        @function_tool(name_override="Skill")
        async def skill_tool(skill: str) -> str:
            """加载指定 Skill 的工作流指令。在进入 planning 或 placement 阶段时调用。"""
            bundle = self._require_bundle()
            meta = bundle.skill_metas.get(skill)
            if meta is None:
                available = ", ".join(sorted(bundle.skill_metas.keys()))
                return f"Skill '{skill}' 不存在。可用 Skill：{available}"
            content = meta.path.read_text(encoding="utf-8-sig")
            from ..runtime.config_bundle import strip_skill_frontmatter
            return strip_skill_frontmatter(content)

        return {
            "Read": read_file,
            "Write": write_file,
            "Edit": edit_file,
            "Glob": glob_files,
            "Grep": grep_files,
            "Bash": run_shell,
            "AskUserQuestion": ask_user_question,
            "Skill": skill_tool,
            "mcp__canvas__request_background_screenshot": canvas_request_background_screenshot,
            "mcp__canvas__get_zone_boundaries": canvas_get_zone_boundaries,
            "mcp__canvas__save_semantic_plan": canvas_save_semantic_plan,
            "mcp__canvas__load_semantic_plan": canvas_load_semantic_plan,
            "mcp__canvas__validate_layout": canvas_validate_layout,
            "mcp__canvas__load_reference_analysis": canvas_load_reference_analysis,
            "mcp__canvas__save_reference_analysis": canvas_save_reference_analysis,
            "mcp__canvas__save_modules": canvas_save_modules,
            "mcp__canvas__clone_scheme_to_variant": canvas_clone_scheme_to_variant,
            "mcp__canvas__analyze_image": canvas_analyze_image,
        }

    def _build_helper_agent_tools(
        self,
        agents: Any,
        *,
        model: str | None,
        nested_stream_handler: Any | None,
        tool_by_name: dict[str, Any],
        explicit_request: _ExplicitConfiguredAgentRequest | None = None,
    ) -> list[Any]:
        explicit_target_name = explicit_request.name if explicit_request and explicit_request.enabled_spec else None
        query_description = "委派一个通用只读子任务。仅用于补充检索、统计与上下文准备，不得替代用户显式点名的配置型 agent。"
        edit_description = "委派一个通用单一编辑子任务。仅用于局部文件修改，不得调用 MCP、Skill 或用户交互，也不得替代用户显式点名的配置型 agent。"
        if explicit_target_name:
            query_description = (
                f"{query_description} 当前用户已显式点名 `{explicit_target_name}`，"
                "该 helper 只能做辅助取证。"
            )
            edit_description = (
                f"{edit_description} 当前用户已显式点名 `{explicit_target_name}`，"
                "不得把本 helper 当作主子任务。"
            )
        helper_tools = [
            self._build_helper_agent_tool(
                agents,
                model=model,
                nested_stream_handler=nested_stream_handler,
                tool_by_name=tool_by_name,
                delegate_tool_name=_OPENAI_DELEGATE_QUERY_TOOL_NAME,
                worker_type="query-worker",
                child_tool_names=_OPENAI_QUERY_DELEGATE_TOOL_ORDER,
                description=query_description,
            ),
            self._build_helper_agent_tool(
                agents,
                model=model,
                nested_stream_handler=nested_stream_handler,
                tool_by_name=tool_by_name,
                delegate_tool_name=_OPENAI_DELEGATE_EDIT_TOOL_NAME,
                worker_type="edit-worker",
                child_tool_names=_OPENAI_EDIT_DELEGATE_TOOL_ORDER,
                description=edit_description,
            ),
        ]
        return helper_tools

    @staticmethod
    def _order_configured_specs_for_root(
        *,
        configured_specs: list[_ConfiguredAgentToolSpec],
        explicit_request: _ExplicitConfiguredAgentRequest | None,
    ) -> list[_ConfiguredAgentToolSpec]:
        if explicit_request is None or explicit_request.enabled_spec is None:
            return list(configured_specs)
        explicit_name = explicit_request.enabled_spec.config.name
        prioritized = [
            spec for spec in configured_specs
            if spec.config.name == explicit_name
        ]
        remainder = [
            spec for spec in configured_specs
            if spec.config.name != explicit_name
        ]
        return [*prioritized, *remainder]

    def _build_helper_agent_tool(
        self,
        agents: Any,
        *,
        model: str | None,
        nested_stream_handler: Any | None,
        tool_by_name: dict[str, Any],
        delegate_tool_name: str,
        worker_type: str,
        child_tool_names: tuple[str, ...],
        description: str,
    ) -> Any:
        child_agent = self._build_child_agent(
            agents,
            model=model,
            tool_by_name=tool_by_name,
            worker_type=worker_type,
            child_tool_names=child_tool_names,
        )
        return self._call_agent_as_tool(
            child_agent,
            tool_name=delegate_tool_name,
            tool_description=description,
            parameters=DelegationTaskInput,
            input_builder=self._build_delegated_task_input,
            on_stream=nested_stream_handler,
            max_turns=20,
            failure_error_function=self._build_delegate_tool_failure_output,
            session=None,
        )

    def _build_child_agent(
        self,
        agents: Any,
        *,
        model: str | None,
        tool_by_name: dict[str, Any],
        worker_type: str,
        child_tool_names: tuple[str, ...],
    ) -> Any:
        child_tools = [tool_by_name[name] for name in child_tool_names]
        worker_label = "QueryWorker" if worker_type == "query-worker" else "EditWorker"
        return agents.Agent(
            name=f"BIMCanvas{worker_label}",
            instructions=self._build_child_agent_instructions(worker_type),
            tools=child_tools,
            model=model or self._current_model,
        )

    def _build_configured_agent_tool(
        self,
        agents: Any,
        *,
        spec: _ConfiguredAgentToolSpec,
        nested_stream_handler: Any | None,
        tool_by_name: dict[str, Any],
    ) -> Any:
        child_tools = [tool_by_name[name] for name in spec.tool_names]
        child_agent = agents.Agent(
            name=spec.config.name,
            instructions=self._build_configured_agent_instructions(
                config=spec.config,
                tool_names=spec.tool_names,
            ),
            tools=child_tools,
            model=spec.model or self._current_model,
        )
        return self._call_agent_as_tool(
            child_agent,
            tool_name=spec.config.name,
            tool_description=spec.config.description,
            parameters=DelegationTaskInput,
            input_builder=self._build_delegated_task_input,
            on_stream=nested_stream_handler,
            max_turns=30,
            failure_error_function=self._build_delegate_tool_failure_output,
            session=None,
        )

    def _call_agent_as_tool(
        self,
        child_agent: Any,
        **kwargs: Any,
    ) -> Any:
        as_tool = child_agent.as_tool
        try:
            supported_params = set(inspect.signature(as_tool).parameters.keys())
        except (TypeError, ValueError):
            supported_params = set()

        if "custom_output_extractor" in supported_params:
            kwargs["custom_output_extractor"] = self._extract_agent_tool_output

        return as_tool(**kwargs)

    def _build_input_items(
        self,
        *,
        user_message: str,
        images: list[str],
        image_blocks: list[dict[str, Any]],
        canvas_context: dict[str, Any],
    ) -> list[dict[str, Any]]:
        content: list[dict[str, Any]] = []

        for block in image_blocks:
            source = block.get("source", {})
            data = source.get("data")
            media_type = source.get("media_type", "image/png")
            if isinstance(data, str) and data:
                content.append(
                    {
                        "type": "input_image",
                        "image_url": f"data:{media_type};base64,{data}",
                    }
                )

        for image in images:
            data = image.split(",", 1)[1] if "," in image else image
            if data:
                content.append(
                    {
                        "type": "input_image",
                        "image_url": f"data:image/png;base64,{data}",
                    }
                )

        context_block = self._build_context_block(canvas_context)
        if context_block:
            content.append({"type": "input_text", "text": context_block})
        if user_message:
            content.append({"type": "input_text", "text": user_message})

        return [{"role": "user", "content": content or [{"type": "input_text", "text": user_message}]}]

    def _build_run_context(
        self,
        *,
        runtime_context: dict[str, str] | None,
        canvas_context: dict[str, Any] | None,
    ) -> dict[str, Any]:
        return {
            "runtimeContext": dict(runtime_context or {}),
            "questionAnswersByCallId": {},
            "canvasContext": dict(canvas_context or {}),
            "projectPath": self.project_path,
            "workingDirectory": self.working_directory,
        }

    @staticmethod
    def _resolve_canvas_project_path(ctx: Any, project_path: str | None) -> str | None:
        normalized_project_path = (project_path or "").strip()
        if normalized_project_path:
            return normalized_project_path

        context_mapping = getattr(ctx, "context", None)
        if not isinstance(context_mapping, dict):
            return None

        for key in ("projectPath", "workingDirectory"):
            value = context_mapping.get(key)
            if isinstance(value, str) and value.strip():
                return value.strip()
        return None

    async def _invoke_canvas_tool_impl(self, tool_name: str, args: dict[str, Any]) -> Any:
        canvas_module = importlib.import_module("..mcp.canvas", package=__package__)
        impl = getattr(canvas_module, tool_name, None)
        if impl is None:
            # 组5 §5.A.3 后, indoor-layout 专属 5 个工具 (save/load_semantic_plan、
            # save/load_reference_analysis、clone_scheme_to_variant) 已物理迁出到
            # plugin。OpenAI Runtime 当前 (Phase 1) 是硬编码工具列表的兼容性占位,
            # 不支持 plugin 动态加载 (主真理源 §6.3 Phase 2+ 才适配)。
            # 此处 graceful 降级:工具缺失时返回提示而非 AttributeError 崩溃。
            return f"工具 mcp__canvas__{tool_name} 未在 OpenAI Runtime 路径注册。该工具可能已迁出到 plugin (如 indoor-layout),OpenAI Runtime 的 plugin 支持留待 Phase 2;请使用 Claude Runtime 调用此工具。"
        # canvas 工具被 claude_agent_sdk 的 @tool(...) 装饰后，module 顶层绑定的是
        # SdkMcpTool dataclass 实例（无 __call__），原始 async handler 在 .handler 属性上。
        # 见 claude_agent_sdk/__init__.py:130。Claude Runtime 走 MCP server 自动解包；
        # OpenAI Runtime 这条手工路径必须显式走 .handler。
        result = await impl.handler(args)
        return self._normalize_canvas_tool_output(result)

    @staticmethod
    def _normalize_canvas_tool_output(result: Any) -> Any:
        if not isinstance(result, dict):
            return result

        content = result.get("content")
        if not isinstance(content, list):
            return result

        output_blocks: list[dict[str, str]] = []
        saw_non_text = False
        collected_texts: list[str] = []

        for block in content:
            if not isinstance(block, dict):
                continue
            block_type = str(block.get("type") or "").strip().lower()
            if block_type == "text":
                text = str(block.get("text") or "").strip()
                if text:
                    collected_texts.append(text)
                    output_blocks.append({"type": "text", "text": text})
                continue
            if block_type == "image":
                image_data = str(block.get("data") or "").strip()
                if not image_data:
                    continue
                mime_type = str(block.get("mimeType") or "image/png").strip() or "image/png"
                output_blocks.append({
                    "type": "image",
                    "image_url": f"data:{mime_type};base64,{image_data}",
                })
                saw_non_text = True

        if saw_non_text:
            return output_blocks or result
        if collected_texts:
            return "\n\n".join(collected_texts)
        return result

    @staticmethod
    def _build_skills_prompt(bundle: Any) -> str:
        if not bundle.skill_metas:
            return ""
        lines = [f"- {m.name}: {m.description}" for m in bundle.skill_metas.values()]
        return (
            "\n## Skills\n\n"
            "使用 `Skill` 工具按需加载工作流指令。\n\n"
            "可用 Skills：\n"
            + "\n".join(lines)
            + "\n"
        )

    @staticmethod
    def _build_delegated_task_input(options: dict[str, Any]) -> str:
        params = options.get("params", {})
        task_title = params.get("task_title") if isinstance(params, dict) else ""
        task_prompt = params.get("task_prompt") if isinstance(params, dict) else ""

        normalized_title = task_title.strip() if isinstance(task_title, str) else "未命名子任务"
        normalized_prompt = task_prompt.strip() if isinstance(task_prompt, str) else ""

        if not normalized_prompt:
            fallback_prompt = (
                "主控没有提供 task_prompt 字段（这是主控的调用错误）。"
                f"请尽量基于任务标题'{normalized_title}'执行实际工作——"
                "调用你可用的工具（文件写入、MCP 调用等）完成任务，"
                "不要仅输出文字摘要就结束。"
                "如果信息不足以继续，用 1-2 句话说明缺什么后停止。"
            )
            normalized_prompt = fallback_prompt
        sections = [
            "你正在执行主控 Agent 下发的单一子任务。",
            f"任务标题：{normalized_title}",
            "任务要求：",
            normalized_prompt,
        ]
        return "\n\n".join(sections)

    @staticmethod
    def _build_delegate_tool_failure_output(_context: Any, error: Exception) -> str:
        payload = {
            "error": str(error) or error.__class__.__name__,
        }
        return f"{SUBTASK_ERROR_MARKER}{json.dumps(payload, ensure_ascii=False)}"

    @staticmethod
    async def _extract_agent_tool_output(run_result: Any) -> str:
        summary = OpenAIAgent._extract_agent_tool_summary(run_result)
        completed_tool_calls = OpenAIAgent._extract_agent_tool_completed_tool_calls(run_result)
        payload = {
            "summary": summary,
            "completedToolCalls": completed_tool_calls,
        }
        return f"{AGENT_TOOL_RESULT_MARKER}{json.dumps(payload, ensure_ascii=False)}"

    @staticmethod
    def _extract_agent_tool_summary(run_result: Any) -> str | None:
        final_output = getattr(run_result, "final_output", None)
        if isinstance(final_output, str) and final_output.strip():
            return final_output.strip()
        if final_output is not None and str(final_output).strip():
            return str(final_output).strip()

        last_tool_output: str | None = None
        for item in getattr(run_result, "new_items", []):
            item_type = getattr(item, "type", None)
            if item_type == "message_output_item":
                raw_item = getattr(item, "raw_item", item)
                content = getattr(raw_item, "content", None)
                if isinstance(content, list):
                    text_parts: list[str] = []
                    for part in content:
                        part_type = getattr(part, "type", None)
                        if part_type == "output_text":
                            text = getattr(part, "text", None)
                            if isinstance(text, str) and text.strip():
                                text_parts.append(text.strip())
                    if text_parts:
                        return "\n".join(text_parts).strip()
            if item_type == "tool_call_output_item":
                output = getattr(item, "output", None)
                if isinstance(output, str) and output.strip():
                    last_tool_output = output.strip()
                elif output is not None and str(output).strip():
                    last_tool_output = str(output).strip()

        return last_tool_output

    @staticmethod
    def _extract_agent_tool_completed_tool_calls(run_result: Any) -> list[dict[str, str | None]]:
        tool_names_by_call_id: dict[str, str] = {}
        completed_tool_calls: list[dict[str, str | None]] = []

        for item in getattr(run_result, "new_items", []):
            item_type = getattr(item, "type", None)
            raw_item = getattr(item, "raw_item", item)

            if item_type == "tool_call_item":
                provider_call_id = getattr(raw_item, "call_id", None)
                tool_name = getattr(raw_item, "name", None)
                if isinstance(provider_call_id, str) and provider_call_id.strip() and isinstance(tool_name, str) and tool_name.strip():
                    tool_names_by_call_id[provider_call_id.strip()] = tool_name.strip()
                continue

            if item_type != "tool_call_output_item":
                continue

            provider_call_id: str | None = None
            if isinstance(raw_item, dict):
                raw_call_id = raw_item.get("call_id")
                if isinstance(raw_call_id, str) and raw_call_id.strip():
                    provider_call_id = raw_call_id.strip()
            else:
                raw_call_id = getattr(raw_item, "call_id", None)
                if isinstance(raw_call_id, str) and raw_call_id.strip():
                    provider_call_id = raw_call_id.strip()

            if not provider_call_id:
                continue

            output = getattr(item, "output", None)
            if isinstance(output, str):
                normalized_output = output
            elif output is None:
                normalized_output = None
            else:
                normalized_output = str(output)

            completed_tool_calls.append({
                "providerCallId": provider_call_id,
                "toolName": tool_names_by_call_id.get(provider_call_id),
                "output": normalized_output,
            })

        return completed_tool_calls

    def _build_openai_root_appendix(
        self,
        *,
        explicit_request: _ExplicitConfiguredAgentRequest | None = None,
    ) -> str:
        explicit_lines = ""
        if explicit_request and explicit_request.enabled_spec is not None:
            explicit_lines = (
                f"- 用户本轮显式点名了配置型 agent `{explicit_request.name}`。\n"
                f"- 若需要 agent delegation，必须把 `{explicit_request.name}` 作为主子任务目标；"
                "helper agents 只能做辅助取证，不能替代它。\n"
            )
        return (
            "\n\n## OpenAI Runtime Appendix\n"
            "- 当前子任务委派使用原生 `Agent.as_tool()`，不是 Claude `Task`。\n"
            f"- 如需只读分析、统计或检索，优先调用 `{_OPENAI_DELEGATE_QUERY_TOOL_NAME}`。\n"
            f"- 如需单一局部修改，调用 `{_OPENAI_DELEGATE_EDIT_TOOL_NAME}`。\n"
            f"- 当共享权限允许时，`{_OPENAI_LAYOUT_AGENT_NAME}` 会通过运行时 Skill 装配 + 原生 MCP function tools 定向启用，用于显式单区 generate 子任务。\n"
            "- 若某个配置型 agent 因 `openai.permissions.allow/deny` 或当前 Runtime 能力边界未启用，主控不得用 helper sub-agent 冒充它。\n"
            "- helper sub-agent 只执行一个明确子任务，并返回简洁中文摘要供主控汇总。\n"
            f"{explicit_lines}"
        )

    @staticmethod
    def _build_child_agent_instructions(worker_type: str) -> str:
        if worker_type == "query-worker":
            allowed_tools = "Read / Glob / Grep"
            worker_goal = "只读检索、统计与分析"
        else:
            allowed_tools = "Read / Write / Edit / Glob / Grep"
            worker_goal = "单一局部编辑"

        return (
            f"你是 BIMCanvas 的 {worker_type}，只负责父代理下发的单一子任务。\n"
            f"你的目标是完成 {worker_goal}，并输出简洁中文摘要。\n"
            f"你只可使用这些工具：{allowed_tools}。\n"
            "禁止与用户交互，禁止 AskUserQuestion，禁止调用 Skill，禁止调用 MCP，禁止再次委派子任务。\n"
            "不要解释自己是工具，不要输出过程闲聊，只保留完成任务所需的最小文字。"
        )

    def _build_configured_agent_instructions(
        self,
        *,
        config: AgentConfig,
        tool_names: tuple[str, ...],
    ) -> str:
        allowed_tools = " / ".join(tool_names) if tool_names else "（无工具，仅基于上下文作答）"
        project_path = self.project_path or self.working_directory or "（unknown）"
        working_directory = self.working_directory or self.project_path or "（unknown）"
        if config.name == _OPENAI_LAYOUT_AGENT_NAME:
            skills_prompt = self._build_skills_prompt(self._require_bundle())
            return (
                f"{config.prompt}\n\n"
                "## OpenAI Runtime Adapter Appendix\n"
                f"- 当前项目路径：{project_path}\n"
                f"- 当前工作目录：{working_directory}\n"
                f"- 当前可用工具：{allowed_tools}\n"
                f"{skills_prompt}"
            )
        return (
            f"{config.prompt}\n\n"
            "## OpenAI Runtime Adapter Appendix\n"
            f"- 你是配置型子代理 `{config.name}`，通过原生 Agent.as_tool() 被主控调用。\n"
            "- 你只执行主控下发的单一子任务，不自行改写路由。\n"
            f"- 当前项目路径：{project_path}\n"
            f"- 当前工作目录：{working_directory}\n"
            f"- 当前可用工具：{allowed_tools}\n"
            "- 禁止与用户交互，禁止 AskUserQuestion，禁止调用 Skill，禁止调用 MCP，禁止再次委派子代理。\n"
            "- 若任务合同不完整或超出职责，直接简洁上报，不要自行补猜。"
        )

    def _build_context_block(self, context: dict[str, Any] | None) -> str | None:
        if not context:
            return None
        from .main_agent import MainAgent

        return MainAgent._build_context_block(context)

    @staticmethod
    def _build_sdk_error_chunk(exc: Exception, *, error_content: str) -> StreamChunk:
        message = str(exc) or exc.__class__.__name__ or "Provider SDK error."
        return StreamChunk(
            type="text_complete",
            content=message,
            error=message,
            error_type="sdk_error",
            error_content=error_content,
        )

    async def _emit_nested_agent_stream_event(
        self,
        *,
        payload: Any,
        translator: OpenAIStreamTranslator,
        emit_chunk: Any,
    ) -> None:
        tool_call = _get_attr(payload, "tool_call")
        start_chunks, forced_subtask_id = translator.ensure_subtask_started_for_tool_call(tool_call)
        for chunk in start_chunks:
            await emit_chunk(chunk)

        event = _get_attr(payload, "event")
        if event is None:
            self._agent_logger.log_warning("nested_stream_handler: event is None")
            return
        translated_chunks = translator.translate(event, forced_subtask_id=forced_subtask_id)
        if not translated_chunks:
            logger.debug(
                "OpenAI nested stream event produced no translated chunks. event_type=%s",
                _get_attr(event, "type"),
            )
        for chunk in translated_chunks:
            await emit_chunk(chunk)

    def _resolve_sdk_session_id(self, session_id: str | None = None) -> str | None:
        normalized = (session_id or "").strip()
        if normalized:
            return normalized
        runtime_context = self._runtime_context or {}
        runtime_session_id = (runtime_context.get("sessionId") or "").strip()
        return runtime_session_id or None

    def _resolve_sdk_session_db_path(self) -> Path:
        runtime_dir = self._require_bundle().bimcanvas_home / ".runtime"
        runtime_dir.mkdir(parents=True, exist_ok=True)
        return runtime_dir / "openai_agent_sessions.sqlite3"

    def _close_sdk_session(self) -> None:
        session = self._sdk_session
        self._sdk_session = None
        self._sdk_session_id = None
        if session is None:
            return

        close = getattr(session, "close", None)
        if callable(close):
            try:
                close()
            except Exception:
                logger.debug("Failed to close OpenAI SDK session cleanly.", exc_info=True)

    def _get_or_create_sdk_session(
        self,
        agents: Any,
        *,
        session_id: str | None = None,
    ) -> Any | None:
        resolved_session_id = self._resolve_sdk_session_id(session_id)
        if not resolved_session_id:
            return None

        if self._sdk_session is not None and self._sdk_session_id == resolved_session_id:
            return self._sdk_session

        self._close_sdk_session()

        sqlite_session_type = getattr(agents, "SQLiteSession", None)
        if sqlite_session_type is None:
            sqlite_session_module = importlib.import_module("agents.memory.sqlite_session")
            sqlite_session_type = sqlite_session_module.SQLiteSession

        self._sdk_session = sqlite_session_type(
            resolved_session_id,
            db_path=self._resolve_sdk_session_db_path(),
        )
        self._sdk_session_id = resolved_session_id
        return self._sdk_session

    @staticmethod
    def _is_official_openai_base_url(base_url: str | None) -> bool:
        normalized = (base_url or "").strip()
        if not normalized:
            return True

        parsed = urlparse(normalized)
        host = (parsed.netloc or parsed.path).strip().lower()
        if host.endswith("/v1"):
            host = host[:-3]
        return host in {"api.openai.com", "api.openai.com:443"}

    def _should_use_responses_run_fallback(self, settings: Any | None = None) -> bool:
        # BIMCanvas OpenAI Runtime v0.1 收口：chat_completions + streaming 是唯一主路径。
        # 第三方 OpenAI-compatible endpoint + responses 的组合已在 settings._resolve_openai_api_mode
        # 显式抛 ValueError 拦截，不会走到这里；官方 endpoint + responses 也不再做非流式缓冲降级。
        # 函数签名保留以便历史测试平滑过渡，语义固定为 False。
        return False

    def _translate_result_chunks(
        self,
        *,
        result: Any,
        translator: OpenAIStreamTranslator,
    ) -> list[StreamChunk]:
        chunks: list[StreamChunk] = []
        pending_agent_completion_items: list[Any] = []
        for item in getattr(result, "new_items", []):
            if self._is_agent_as_tool_completion_item(item):
                pending_agent_completion_items.append(item)
                continue

            forced_subtask_id: str | None = None
            if self._is_result_message_item(item):
                pending_subtask_id = self._get_latest_pending_agent_completion_subtask_id(
                    pending_agent_completion_items,
                    translator=translator,
                )
                if pending_subtask_id:
                    if self._should_flush_pending_agent_completion_before_message(
                        pending_agent_completion_items[-1],
                        subtask_id=pending_subtask_id,
                        translator=translator,
                    ):
                        chunks.extend(
                            self._flush_pending_agent_completion_items(
                                pending_agent_completion_items,
                                translator=translator,
                            )
                        )
                    else:
                        forced_subtask_id = pending_subtask_id

            chunks.extend(translator.translate_result_item(item, forced_subtask_id=forced_subtask_id))

        chunks.extend(
            self._flush_pending_agent_completion_items(
                pending_agent_completion_items,
                translator=translator,
            )
        )
        return self._finalize_root_response_chunks(chunks, translator=translator)

    @staticmethod
    def _is_agent_as_tool_completion_item(item: Any) -> bool:
        item_type = getattr(item, "type", None)
        if item_type != "tool_call_output_item":
            return False

        tool_origin = getattr(item, "tool_origin", None)
        origin_type = getattr(tool_origin, "type", None)
        if isinstance(origin_type, str):
            return origin_type == "agent_as_tool"

        origin_value = getattr(origin_type, "value", None)
        return isinstance(origin_value, str) and origin_value == "agent_as_tool"

    @staticmethod
    def _is_result_message_item(item: Any) -> bool:
        return getattr(item, "type", None) == "message_output_item"

    @staticmethod
    def _resolve_result_item_call_id(item: Any) -> str | None:
        raw_item = getattr(item, "raw_item", item)
        call_id = getattr(raw_item, "call_id", None)
        if isinstance(call_id, str) and call_id.strip():
            return call_id.strip()
        if isinstance(raw_item, dict):
            raw_call_id = raw_item.get("call_id")
            if isinstance(raw_call_id, str) and raw_call_id.strip():
                return raw_call_id.strip()
        return None

    def _get_latest_pending_agent_completion_subtask_id(
        self,
        pending_items: list[Any],
        *,
        translator: OpenAIStreamTranslator,
    ) -> str | None:
        if not pending_items:
            return None
        provider_call_id = self._resolve_result_item_call_id(pending_items[-1])
        return translator.ensure_subtask_id_for_provider_call(provider_call_id)

    @staticmethod
    def _extract_pending_agent_completion_output(item: Any) -> str:
        output = getattr(item, "output", None)
        if isinstance(output, str) and output.strip():
            return output.strip()

        raw_item = getattr(item, "raw_item", item)
        if isinstance(raw_item, dict):
            raw_output = raw_item.get("output")
            if isinstance(raw_output, str) and raw_output.strip():
                return raw_output.strip()
        else:
            raw_output = getattr(raw_item, "output", None)
            if isinstance(raw_output, str) and raw_output.strip():
                return raw_output.strip()
        return ""

    def _should_flush_pending_agent_completion_before_message(
        self,
        pending_item: Any,
        *,
        subtask_id: str,
        translator: OpenAIStreamTranslator,
    ) -> bool:
        if translator.has_active_tool_calls(subtask_id):
            return False
        if translator.has_subtask_message(subtask_id):
            return True
        return bool(self._extract_pending_agent_completion_output(pending_item))

    @staticmethod
    def _flush_pending_agent_completion_items(
        pending_items: list[Any],
        *,
        translator: OpenAIStreamTranslator,
    ) -> list[StreamChunk]:
        if not pending_items:
            return []

        flushed_chunks: list[StreamChunk] = []
        while pending_items:
            flushed_chunks.extend(translator.translate_result_item(pending_items.pop(0)))
        return flushed_chunks

    @staticmethod
    def _should_suppress_root_text_chunk(
        chunk: StreamChunk,
        *,
        translator: OpenAIStreamTranslator,
    ) -> bool:
        if chunk.subagent_id:
            return False
        if chunk.type not in {"text", "text_complete"}:
            return False
        return translator.has_root_failure_override()

    def _maybe_build_root_failure_summary_chunk(
        self,
        *,
        translator: OpenAIStreamTranslator,
    ) -> StreamChunk | None:
        summary = translator.build_root_failure_summary()
        if not summary:
            return None
        return StreamChunk(type="text_complete", content=summary)

    def _finalize_root_response_chunks(
        self,
        chunks: list[StreamChunk],
        *,
        translator: OpenAIStreamTranslator,
    ) -> list[StreamChunk]:
        if not translator.has_root_failure_override():
            return chunks

        filtered_chunks = [
            chunk
            for chunk in chunks
            if not self._should_suppress_root_text_chunk(chunk, translator=translator)
        ]
        root_failure_chunk = self._maybe_build_root_failure_summary_chunk(translator=translator)
        if root_failure_chunk is not None:
            filtered_chunks.append(root_failure_chunk)
        return filtered_chunks

    def _resolve_configured_agent_tool_specs(
        self,
        *,
        enabled_tool_names: list[str],
        inherited_model: str | None,
    ) -> tuple[list[_ConfiguredAgentToolSpec], list[_BlockedConfiguredAgentSpec]]:
        bundle = self._require_bundle()
        available_tool_names = set(enabled_tool_names) - {"AskUserQuestion"}
        enabled_specs: list[_ConfiguredAgentToolSpec] = []
        blocked_specs: list[_BlockedConfiguredAgentSpec] = []
        seen_agent_names: set[str] = set()

        for name, cfg in bundle.shared_agents.items():
            intrinsic_reasons: list[str] = []
            permission_reasons: list[str] = []
            resolved_tool_names: list[str] = []
            required_permission_names: list[str] = []
            uses_runtime_adapted_layout_agent = name == _OPENAI_LAYOUT_AGENT_NAME
            parsed_requirements = parse_configured_agent_requirements(
                cfg,
                known_local_tool_names=_OPENAI_LOCAL_TOOL_NAMES,
                reserved_tool_names={
                    _OPENAI_DELEGATE_QUERY_TOOL_NAME,
                    _OPENAI_DELEGATE_EDIT_TOOL_NAME,
                },
            )

            if name in seen_agent_names or name in _OPENAI_RESERVED_AGENT_TOOL_NAMES:
                intrinsic_reasons.append("tool name collision")
            seen_agent_names.add(name)

            resolved_model = inherited_model if cfg.model == "inherit" else cfg.model
            normalized_model = (resolved_model or "").strip()
            if normalized_model and normalized_model.lower() in _CLAUDE_MODEL_ALIASES:
                intrinsic_reasons.append(f"unsupported model alias: {normalized_model}")

            if "Task" not in available_tool_names:
                permission_reasons.append("permission-gated: Task")
            else:
                required_permission_names.append("Task")

            if parsed_requirements.requires_skill:
                if not uses_runtime_adapted_layout_agent:
                    intrinsic_reasons.append("Skill")
                elif "Skill" not in available_tool_names:
                    permission_reasons.append("permission-gated: Skill")
                else:
                    required_permission_names.append("Skill")
                    for skill_name in _OPENAI_LAYOUT_AGENT_SKILL_NAMES:
                        if skill_name not in bundle.skill_index:
                            intrinsic_reasons.append(f"missing skill: {skill_name}")
                    resolved_tool_names.append("Skill")

            for tool_name in parsed_requirements.special_tool_names:
                intrinsic_reasons.append(tool_name)

            for tool_name in parsed_requirements.unsupported_tool_names:
                intrinsic_reasons.append(f"unsupported tool: {tool_name}")

            for tool_name in parsed_requirements.mcp_tool_names:
                if not uses_runtime_adapted_layout_agent or tool_name not in _OPENAI_LAYOUT_AGENT_MCP_TOOL_ORDER:
                    intrinsic_reasons.append(tool_name)
                    continue
                if tool_name not in available_tool_names:
                    permission_reasons.append(f"permission-gated: {tool_name}")
                    continue
                if tool_name not in required_permission_names:
                    required_permission_names.append(tool_name)
                resolved_tool_names.append(tool_name)

            for tool_name in parsed_requirements.local_tool_names:
                if tool_name not in _OPENAI_LOCAL_TOOL_NAMES:
                    intrinsic_reasons.append(f"unsupported tool: {tool_name}")
                    continue
                if tool_name not in available_tool_names:
                    permission_reasons.append(f"permission-gated: {tool_name}")
                    continue
                if tool_name not in required_permission_names:
                    required_permission_names.append(tool_name)
                resolved_tool_names.append(tool_name)

            reasons = list(dict.fromkeys([*intrinsic_reasons, *permission_reasons]))
            if reasons:
                blocked_specs.append(
                    _BlockedConfiguredAgentSpec(
                        name=name,
                        reasons=tuple(reasons),
                    )
                )
                continue

            enabled_specs.append(
                _ConfiguredAgentToolSpec(
                    config=cfg,
                    tool_names=tuple(resolved_tool_names),
                    model=resolved_model,
                    required_permission_names=tuple(required_permission_names),
                )
            )

        return enabled_specs, blocked_specs

    @staticmethod
    def _resolve_explicit_configured_agent_request(
        user_message: str,
        *,
        enabled_specs: list[_ConfiguredAgentToolSpec],
        blocked_specs: list[_BlockedConfiguredAgentSpec],
    ) -> _ExplicitConfiguredAgentRequest | None:
        normalized_message = (user_message or "").strip().lower()
        if not normalized_message:
            return None

        enabled_by_name = {spec.config.name: spec for spec in enabled_specs}
        blocked_by_name = {spec.name: spec for spec in blocked_specs}
        candidate_names = sorted(
            set(enabled_by_name) | set(blocked_by_name),
            key=len,
            reverse=True,
        )
        matches = [
            name
            for name in candidate_names
            if name.lower() in normalized_message
        ]
        if len(matches) != 1:
            return None

        requested_name = matches[0]
        return _ExplicitConfiguredAgentRequest(
            name=requested_name,
            enabled_spec=enabled_by_name.get(requested_name),
            blocked_spec=blocked_by_name.get(requested_name),
        )

    @staticmethod
    def _build_explicit_configured_agent_unavailable_message(
        request: _ExplicitConfiguredAgentRequest,
    ) -> str:
        reasons = "、".join(request.blocked_spec.reasons) if request.blocked_spec else "unknown"
        return (
            f"当前无法调用 `{request.name}`，因为它在共享权限/能力检查下未启用：{reasons}。\n"
            "OpenAI runtime 不会用通用 helper worker 冒充这个配置型 agent。\n"
            "如需继续浏览器验收，请先手动更新 `<BIMCANVAS_HOME>/config.json` 的 `openai.permissions.allow` 后重试。"
        )

    def _log_configured_subagent_availability(
        self,
        *,
        enabled_specs: list[_ConfiguredAgentToolSpec],
        blocked_specs: list[_BlockedConfiguredAgentSpec],
    ) -> None:
        if self._configured_subagents_logged:
            return

        if enabled_specs:
            logger.info(
                "OpenAI runtime registered configured agent tools: %s",
                "; ".join(
                    self._format_configured_agent_log_entry(spec)
                    for spec in enabled_specs
                ),
            )

        if blocked_specs:
            logger.warning(
                "OpenAI runtime keeps some configured agents disabled until later phases: %s",
                "; ".join(
                    f"{spec.name} ({', '.join(spec.reasons)})"
                    for spec in blocked_specs
                ),
            )
            for spec in blocked_specs:
                if spec.name != _OPENAI_LAYOUT_AGENT_NAME:
                    continue
                if not any(reason.startswith("permission-gated:") for reason in spec.reasons):
                    continue
                logger.warning(
                    "OpenAI runtime requires manual permission sync for `%s`: update "
                    "<BIMCANVAS_HOME>/config.json openai.permissions.allow to include the shared layout-agent baseline.",
                    spec.name,
                )
                break

        self._configured_subagents_logged = True

    def _format_configured_agent_log_entry(self, spec: _ConfiguredAgentToolSpec) -> str:
        tool_summary = ", ".join(spec.tool_names) or "no tools"
        if spec.config.name == _OPENAI_LAYOUT_AGENT_NAME and "Skill" in spec.tool_names:
            skill_summary = ", ".join(_OPENAI_LAYOUT_AGENT_SKILL_NAMES)
            tool_summary = f"{tool_summary}; skills[{skill_summary}]"
        return f"{spec.config.name} ({tool_summary})"

    def _resolve_enabled_permission_tool_names(self) -> list[str]:
        bundle = self._require_bundle()
        allowed_tools = bundle.permissions_allow
        denied_tools = bundle.permissions_deny

        if allowed_tools is None:
            enabled_names = set(_OPENAI_DEFAULT_PERMISSION_TOOL_NAMES)
        else:
            enabled_names = set(allowed_tools)

        enabled_names -= {
            name
            for name in denied_tools
        }

        unsupported_requested = sorted({
            name
            for name in [*(allowed_tools or []), *denied_tools]
            if name not in _OPENAI_CONFIGURABLE_PERMISSION_TOOL_NAMES
        })
        if unsupported_requested:
            logger.warning(
                "OpenAI runtime ignored unsupported tools from permissions: %s",
                ", ".join(unsupported_requested),
            )

        return sorted(enabled_names)

    def _validate_requested_model(self, model: str | None) -> None:
        normalized_model = (model or "").strip()
        if not normalized_model:
            raise ValueError(
                "OpenAI runtime requires a concrete model id in the request; empty model is not allowed."
            )
        if normalized_model.lower() in _CLAUDE_MODEL_ALIASES:
            raise ValueError(
                "OpenAI runtime does not accept Claude model aliases like "
                f"'{normalized_model}'. Update <BIMCANVAS_HOME>/config.json modelMapping keys "
                "and <BIMCANVAS_HOME>/config.json openai.defaultModel to real OpenAI model ids."
            )

    def _log_chunk_for_console(self, chunk: StreamChunk) -> None:
        if chunk.type == "thinking_complete" and chunk.content:
            self._agent_logger.log_thinking_start()
            self._agent_logger.log_thinking(chunk.content)
            self._agent_logger.log_thinking_end()
            return

        if chunk.type == "tool_call_start":
            self._agent_logger.log_tool_use(chunk.tool_name or chunk.tool_call_id or "Tool", chunk.tool_params or {})
            return

        if chunk.type == "tool_call_complete":
            tool_label = chunk.tool_name or chunk.tool_call_id or "Tool"
            tool_result = chunk.tool_output or chunk.error or "(no output)"
            self._agent_logger.log_tool_result(tool_label, tool_result, is_error=bool(chunk.error))
            return

        if chunk.type == "text_complete" and chunk.content:
            self._agent_logger.log_response_start()
            self._agent_logger.log_response(chunk.content)
            self._agent_logger.log_response_end()
            return

        if chunk.type == "subagent_start":
            self._agent_logger.log_info(f"Subtask started: {chunk.subagent_name or chunk.subagent_id or 'subtask'}")
            return

        if chunk.type == "subagent_complete":
            self._agent_logger.log_info(f"Subtask completed: {chunk.subagent_id or 'subtask'}")

    def _log_phase_one_scope(self) -> None:
        if self._phase_one_scope_logged:
            return
        logger.info(
            "OpenAI runtime available: implemented local tools (%s), "
            "MCP wrappers (%s), helper agents (%s, %s); "
            "actual tool availability driven by config.json permissions.",
            ", ".join(sorted(_OPENAI_LOCAL_TOOL_NAMES)),
            ", ".join(_OPENAI_LAYOUT_AGENT_MCP_TOOL_ORDER),
            _OPENAI_DELEGATE_QUERY_TOOL_NAME,
            _OPENAI_DELEGATE_EDIT_TOOL_NAME,
        )
        self._phase_one_scope_logged = True

    def _log_responses_run_fallback(self) -> None:
        if self._responses_run_fallback_logged:
            return
        logger.warning(
            "OpenAI responses streaming fallback enabled for custom endpoint; "
            "BIMCanvas will project events from Runner.run() results instead of token streaming."
        )
        self._responses_run_fallback_logged = True

    async def _push_pending_question_interaction(
        self,
        *,
        result: Any,
        translator: OpenAIStreamTranslator,
        runtime_context: dict[str, str] | None,
    ) -> str:
        from ..server.http_server import push_openai_question_interaction

        interruptions = list(getattr(result, "interruptions", []))
        interruption = next(
            (
                item
                for item in interruptions
                if getattr(item, "tool_name", None) == "AskUserQuestion"
            ),
            None,
        )
        if interruption is None:
            raise RuntimeError("Expected AskUserQuestion interruption but none was present")

        questions = self._extract_questions_from_interruption(interruption)
        approval_call_id = getattr(interruption, "call_id", None)
        public_tool_call_id = (
            translator.get_public_tool_call_id(approval_call_id)
            or translator.ensure_public_tool_call_id(approval_call_id)
        )
        run_state = result.to_state()
        binding = PendingInteractionRuntimeBinding(
            interaction_id="",
            resume_token="",
            runtime_id=self.runtime_id,
            session_id=(runtime_context or {}).get("sessionId", ""),
            turn_id=(runtime_context or {}).get("turnId", ""),
            window_id=(runtime_context or {}).get("windowId", ""),
            run_state_json=json.dumps(run_state.to_json(), ensure_ascii=False),
            approval_call_id=approval_call_id,
            public_tool_call_id=public_tool_call_id,
            projection_state=translator.snapshot(),
            agent_identity="BIMCanvas",
        )
        interaction = await push_openai_question_interaction(
            questions=questions,
            runtime_context=runtime_context,
            runtime_binding=binding,
        )
        return interaction.interaction_id

    @staticmethod
    def _extract_questions_from_interruption(interruption: Any) -> list[QuestionDef]:
        arguments = getattr(interruption, "arguments", None)
        if isinstance(arguments, str) and arguments:
            try:
                payload = json.loads(arguments)
            except json.JSONDecodeError:
                return []
        elif isinstance(arguments, dict):
            payload = arguments
        else:
            return []
        questions = payload.get("questions", [])
        return questions if isinstance(questions, list) else []
