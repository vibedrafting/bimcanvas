"""OpenAI Agents SDK adapter for the BIMCanvas host contract."""

from __future__ import annotations

import asyncio
import importlib
import json
import logging
import sys
import uuid
from pathlib import Path
from typing import Any

from typing_extensions import TypedDict

from ..config.loader import get_config_loader
from ..config.settings import get_settings
from ..runtime import PendingInteractionRuntimeBinding, RuntimeSessionRecord, StreamChunk
from ..runtime.openai_stream import OpenAIStreamTranslator
from .errors import TurnPausedError

logger = logging.getLogger(__name__)


class QuestionOption(TypedDict, total=False):
    label: str
    description: str


class QuestionDef(TypedDict, total=False):
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
        _load_openai_agents_module()
        if model:
            self._current_model = model
        self._connected = True

    async def disconnect(self) -> None:
        if self._active_stream_result is not None:
            try:
                self._active_stream_result.cancel()
            except Exception:
                pass
        self._active_stream_result = None
        self._connected = False

    async def set_model(self, model: str) -> bool:
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
        parts: list[str] = []
        async for chunk in self.chat_stream(
            user_message,
            model=model,
            runtime_context=runtime_context,
        ):
            if chunk.type in {"text", "text_complete"} and chunk.content:
                parts.append(chunk.content)
        return "".join(parts)

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
        try:
            if not self.is_connected:
                await self.connect(effort=effort, thinking=thinking, model=model)
            elif model and model != self._current_model:
                await self.set_model(model)

            agents = _load_openai_agents_module()
            translator = OpenAIStreamTranslator(turn_id=(runtime_context or {}).get("turnId", "turn"))
            run_context = self._build_run_context(runtime_context=runtime_context, canvas_context=context)
            starting_agent = self._build_root_agent(agents, model=model)
            input_items = self._build_input_items(
                user_message=user_message,
                images=images or [],
                image_blocks=image_blocks or [],
                canvas_context=context or {},
            )

            result = agents.Runner.run_streamed(
                starting_agent=starting_agent,
                input=input_items,
                context=run_context,
                max_turns=30,
            )
            self._active_stream_result = result

            async for event in result.stream_events():
                for chunk in translator.translate(event):
                    yield chunk

            if getattr(result, "interruptions", None):
                interaction_id = await self._push_pending_question_interaction(
                    result=result,
                    translator=translator,
                    runtime_context=runtime_context,
                )
                raise TurnPausedError(interaction_id)
        finally:
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
        state = await agents.RunState.from_json(
            self._build_root_agent(agents, model=self._current_model),
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

        translator = OpenAIStreamTranslator(
            turn_id=session.active_turn_id or binding.turn_id,
            projection_state=binding.projection_state,
        )

        result = agents.Runner.run_streamed(
            self._build_root_agent(agents, model=self._current_model),
            state,
            context=resume_context,
        )
        self._active_stream_result = result

        appended_events: list[dict[str, Any]] = []
        try:
            async for event in result.stream_events():
                for chunk in translator.translate(event):
                    if chunk.tool_call_id == binding.public_tool_call_id:
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

            return appended_events
        finally:
            self._active_stream_result = None

    def _build_root_agent(self, agents: Any, *, model: str | None) -> Any:
        system_prompt = self._config_loader.load_system_prompt()
        system_prompt = system_prompt + f"\n\n工作目录: {self.working_directory}"
        return agents.Agent(
            name="BIMCanvas",
            instructions=system_prompt,
            tools=self._build_tools(agents),
            model=model or self._current_model,
        )

    def _build_tools(self, agents: Any) -> list[Any]:
        function_tool = agents.function_tool
        working_directory = Path(self.working_directory or self.project_path or ".").resolve()

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

        @function_tool(name_override="AskUserQuestion", needs_approval=True)
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

        return [
            read_file,
            write_file,
            edit_file,
            glob_files,
            grep_files,
            run_shell,
            ask_user_question,
        ]

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

    async def _push_pending_question_interaction(
        self,
        *,
        result: Any,
        translator: OpenAIStreamTranslator,
        runtime_context: dict[str, str] | None,
    ) -> str:
        from ..server.http_server import push_openai_question_interaction

        interruption = next(iter(getattr(result, "interruptions", [])), None)
        if interruption is None:
            raise RuntimeError("Expected OpenAI interruption but none was present")

        questions = self._extract_questions_from_interruption(interruption)
        approval_call_id = getattr(interruption, "call_id", None)
        public_tool_call_id = translator.get_public_tool_call_id(approval_call_id)
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
        if not isinstance(arguments, str) or not arguments:
            return []
        try:
            payload = json.loads(arguments)
        except json.JSONDecodeError:
            return []
        questions = payload.get("questions", [])
        return questions if isinstance(questions, list) else []
