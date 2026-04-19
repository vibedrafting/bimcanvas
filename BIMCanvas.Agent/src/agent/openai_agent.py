"""OpenAI Agents SDK adapter for the BIMCanvas host contract."""

from __future__ import annotations

import asyncio
import importlib
import json
import logging
import os
import sys
from pathlib import Path
from typing import Any
from urllib.parse import urlparse

from typing_extensions import TypedDict

from ..config.loader import get_config_loader
from ..config.settings import get_settings
from ..runtime import PendingInteractionRuntimeBinding, RuntimeSessionRecord, StreamChunk
from ..runtime.openai_stream import OpenAIStreamTranslator
from .agent_logger import get_agent_logger
from .errors import TurnPausedError

logger = logging.getLogger(__name__)

_OPENAI_PHASE_ONE_LOCAL_TOOL_ORDER = (
    "Read",
    "Write",
    "Edit",
    "Glob",
    "Grep",
    "Bash",
    "AskUserQuestion",
)
_OPENAI_PHASE_ONE_LOCAL_TOOL_SET = frozenset(_OPENAI_PHASE_ONE_LOCAL_TOOL_ORDER)
_CLAUDE_MODEL_ALIASES = frozenset({"opus", "sonnet", "haiku"})


class QuestionOption(TypedDict, total=False):
    label: str
    description: str


class QuestionDef(TypedDict, total=False):
    id: str
    header: str
    question: str
    options: list[QuestionOption]


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

    runtime_id = "openai-agents"
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
        self._config_loader = get_config_loader()
        self._connected = False
        self._current_model: str | None = None
        self._runtime_context: dict[str, str] | None = None
        self._active_stream_result: Any | None = None
        self._sdk_session: Any | None = None
        self._sdk_session_id: str | None = None
        self._phase_one_scope_logged = False
        self._responses_run_fallback_logged = False
        self._agent_logger = get_agent_logger("OpenAIAgent", window_seq=self.window_seq)

    @property
    def is_connected(self) -> bool:
        return self._connected

    def set_runtime_context(self, runtime_context: dict[str, str] | None) -> None:
        self._runtime_context = dict(runtime_context) if runtime_context else None

    def clear_runtime_context(self) -> None:
        self._runtime_context = None

    async def connect(
        self,
        effort: str | None = None,
        thinking: str | None = None,
        model: str | None = None,
    ) -> None:
        settings = get_settings()
        if settings.openai_api_key:
            os.environ["OPENAI_API_KEY"] = settings.openai_api_key
        if settings.base_url:
            os.environ["OPENAI_BASE_URL"] = settings.base_url
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

            async def _emit_translated_event(
                event: Any,
                *,
                forced_subtask_id: str | None = None,
            ) -> None:
                for chunk in translator.translate(event, forced_subtask_id=forced_subtask_id):
                    await stream_queue.put(chunk)

            run_context = self._build_run_context(runtime_context=runtime_context, canvas_context=context)
            starting_agent = self._build_root_agent(
                agents,
                model=model,
                nested_stream_handler=None,
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

            if self._should_use_responses_run_fallback(settings):
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
                self._log_chunk_for_console(item)
                yield item

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

    async def resume_interaction(
        self,
        *,
        interaction_id: str,
        binding: PendingInteractionRuntimeBinding,
        resolution_payload: dict[str, Any],
        session: RuntimeSessionRecord,
        append_event: Any,
    ) -> list[dict[str, Any]]:
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
        translator = OpenAIStreamTranslator(
            turn_id=session.active_turn_id or binding.turn_id,
            projection_state=binding.projection_state,
        )
        approved_tool_call_id = (
            binding.public_tool_call_id
            or translator.ensure_public_tool_call_id(binding.approval_call_id)
        )
        consumer_task: asyncio.Task[Any] | None = None
        starting_agent = self._build_root_agent(
            agents,
            model=self._current_model,
            nested_stream_handler=None,
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

        if self._should_use_responses_run_fallback(settings):
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
            result = await result_task

            appended_events: list[dict[str, Any]] = []
            try:
                for chunk in self._translate_result_chunks(result=result, translator=translator):
                    if approved_tool_call_id and chunk.tool_call_id == approved_tool_call_id:
                        chunk.tool_output = None
                        chunk.suppress_public_tool_output = True
                    event_data = await append_event(chunk)
                    appended_events.extend(event_data)

                if getattr(result, "interruptions", None):
                    await self._push_pending_question_interaction(
                        result=result,
                        translator=translator,
                        runtime_context={
                            "windowId": session.window_id,
                            "sessionId": session.session_id,
                            "turnId": binding.turn_id,
                        },
                    )

                return appended_events
            finally:
                self._active_stream_result = None

        result = agents.Runner.run_streamed(
            starting_agent,
            state,
            context=resume_context,
            session=sdk_session,
        )
        self._active_stream_result = result

        appended_events: list[dict[str, Any]] = []
        try:
            async def _consume_result() -> None:
                try:
                    async for event in result.stream_events():
                        await _emit_translated_event(event)

                    if getattr(result, "interruptions", None):
                        interaction_id = await self._push_pending_question_interaction(
                            result=result,
                            translator=translator,
                            runtime_context={
                                "windowId": session.window_id,
                                "sessionId": session.session_id,
                                "turnId": binding.turn_id,
                            },
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
                    return appended_events
                if isinstance(item, Exception):
                    raise item
                chunk = item
                if approved_tool_call_id and chunk.tool_call_id == approved_tool_call_id:
                    chunk.tool_output = None
                    chunk.suppress_public_tool_output = True
                event_data = await append_event(chunk)
                appended_events.extend(event_data)

            return appended_events
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
    ) -> Any:
        system_prompt = self._config_loader.load_system_prompt()
        system_prompt = system_prompt + f"\n\n工作目录: {self.working_directory}"
        return agents.Agent(
            name="BIMCanvas",
            instructions=system_prompt,
            tools=self._build_tools(
                agents,
                model=model,
                nested_stream_handler=nested_stream_handler,
            ),
            model=model or self._current_model,
        )

    def _build_tools(
        self,
        agents: Any,
        *,
        model: str | None,
        nested_stream_handler: Any | None,
    ) -> list[Any]:
        function_tool = agents.function_tool
        working_directory = Path(self.working_directory or self.project_path or ".").resolve()
        tool_context_type = importlib.import_module("agents.tool_context").ToolContext

        def resolve_path(file_path: str) -> Path:
            candidate = Path(file_path)
            if not candidate.is_absolute():
                candidate = working_directory / candidate
            resolved = candidate.resolve()
            if working_directory not in (resolved, *resolved.parents):
                raise ValueError(f"Path escapes working directory: {file_path}")
            return resolved

        @function_tool(name_override="Read")
        async def read_file(file_path: str) -> str:
            """Read a UTF-8 text file from the working directory."""
            return resolve_path(file_path).read_text(encoding="utf-8-sig")

        @function_tool(name_override="Write")
        async def write_file(file_path: str, content: str) -> str:
            """Write a UTF-8 text file under the working directory."""
            target = resolve_path(file_path)
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(content, encoding="utf-8")
            return f"Wrote {target}"

        @function_tool(name_override="Edit")
        async def edit_file(file_path: str, old_string: str, new_string: str) -> str:
            """Replace one text fragment in a file."""
            target = resolve_path(file_path)
            content = target.read_text(encoding="utf-8-sig")
            if old_string not in content:
                raise ValueError("old_string not found")
            target.write_text(content.replace(old_string, new_string, 1), encoding="utf-8")
            return f"Edited {target}"

        @function_tool(name_override="Glob")
        async def glob_files(pattern: str) -> list[str]:
            """Return matching relative file paths."""
            return [
                str(path.relative_to(working_directory))
                for path in working_directory.rglob(pattern)
                if path.is_file()
            ]

        @function_tool(name_override="Grep")
        async def grep_files(pattern: str) -> list[str]:
            """Return matching lines with file and line number."""
            matches: list[str] = []
            for path in working_directory.rglob("*"):
                if not path.is_file():
                    continue
                try:
                    content = path.read_text(encoding="utf-8-sig")
                except Exception:
                    continue
                for line_no, line in enumerate(content.splitlines(), start=1):
                    if pattern in line:
                        matches.append(f"{path.relative_to(working_directory)}:{line_no}:{line}")
                        if len(matches) >= 200:
                            return matches
            return matches

        @function_tool(name_override="Bash")
        async def run_shell(command: str) -> str:
            """Run a shell command inside the working directory."""
            process = await asyncio.create_subprocess_shell(
                command,
                cwd=str(working_directory),
                stdout=asyncio.subprocess.PIPE,
                stderr=asyncio.subprocess.PIPE,
            )
            stdout, stderr = await process.communicate()
            stdout_text = stdout.decode("utf-8", errors="replace")
            stderr_text = stderr.decode("utf-8", errors="replace")
            if process.returncode == 0:
                return stdout_text.strip() or "(no output)"
            return f"exit={process.returncode}\nSTDOUT:\n{stdout_text}\nSTDERR:\n{stderr_text}".strip()

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

        tool_by_name = {
            "Read": read_file,
            "Write": write_file,
            "Edit": edit_file,
            "Glob": glob_files,
            "Grep": grep_files,
            "Bash": run_shell,
            "AskUserQuestion": ask_user_question,
        }
        enabled_tool_names = self._resolve_phase_one_tool_names()
        return [tool_by_name[name] for name in enabled_tool_names]

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

    def _build_context_block(self, context: dict[str, Any] | None) -> str | None:
        if not context:
            return None
        from .main_agent import MainAgent

        return MainAgent._build_context_block(context)

    def _resolve_sdk_session_id(self, session_id: str | None = None) -> str | None:
        normalized = (session_id or "").strip()
        if normalized:
            return normalized
        runtime_context = self._runtime_context or {}
        runtime_session_id = (runtime_context.get("sessionId") or "").strip()
        return runtime_session_id or None

    def _resolve_sdk_session_db_path(self) -> Path:
        runtime_dir = self._config_loader.config_dir / ".runtime"
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
        current_settings = settings or get_settings()
        return (
            current_settings.openai_api == "responses"
            and not self._is_official_openai_base_url(current_settings.base_url)
        )

    def _translate_result_chunks(
        self,
        *,
        result: Any,
        translator: OpenAIStreamTranslator,
    ) -> list[StreamChunk]:
        chunks: list[StreamChunk] = []
        for item in getattr(result, "new_items", []):
            chunks.extend(translator.translate_result_item(item))
        return chunks

    def _resolve_phase_one_tool_names(self) -> list[str]:
        allowed_tools, denied_tools = self._config_loader.load_permissions()

        if allowed_tools is None:
            enabled_names = set(_OPENAI_PHASE_ONE_LOCAL_TOOL_SET)
        else:
            enabled_names = {
                name
                for name in allowed_tools
                if name in _OPENAI_PHASE_ONE_LOCAL_TOOL_SET
            }

        enabled_names -= {
            name
            for name in denied_tools
            if name in _OPENAI_PHASE_ONE_LOCAL_TOOL_SET
        }

        unsupported_requested = sorted({
            name
            for name in [*(allowed_tools or []), *denied_tools]
            if name not in _OPENAI_PHASE_ONE_LOCAL_TOOL_SET
        })
        if unsupported_requested:
            logger.warning(
                "OpenAI phase 1 ignored unsupported tools from permissions: %s",
                ", ".join(unsupported_requested),
            )

        return [name for name in _OPENAI_PHASE_ONE_LOCAL_TOOL_ORDER if name in enabled_names]

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
                "and <BIMCANVAS_HOME>/web_config.json defaultModel/customModels to real OpenAI model ids."
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
            "OpenAI phase 1 enabled: local function tools only (%s); SubAgent/Task/Skill/MCP disabled.",
            ", ".join(_OPENAI_PHASE_ONE_LOCAL_TOOL_ORDER),
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
