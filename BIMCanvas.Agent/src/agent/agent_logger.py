"""Agent Logger - Comprehensive logging for Agent activities."""

import json
import logging
import sys
import time
from datetime import datetime
from typing import Any


def _can_encode_unicode() -> bool:
    """Check if terminal supports extended Unicode characters."""
    encoding = getattr(sys.stdout, 'encoding', None) or ''
    return 'utf' in encoding.lower()


# Auto-detect Unicode support
_USE_UNICODE = _can_encode_unicode()


class Symbols:
    """Terminal symbols (adaptive based on encoding capability)."""
    ARROW_RIGHT = "\u25b6" if _USE_UNICODE else ">>"  # ▶
    ARROW_LEFT = "\u25c0" if _USE_UNICODE else "<<"   # ◀

# ANSI color codes for console output
# 设计哲学：低调为主，颜色只用于关键标记
class Colors:
    # 基础格式
    RESET = "\033[0m"
    BOLD = "\033[1m"
    DIM = "\033[2m"

    # ─────────────────────────────────────────────────────
    # 亮度层级（信息重要性）
    # ─────────────────────────────────────────────────────
    PRIMARY = "\033[97m"      # 主要信息（亮白）- AI 回应
    SECONDARY = "\033[2m"     # 次要信息（暗淡）- 思考过程、技术细节
    TERTIARY = "\033[90m"     # 背景信息（暗灰）- 时间戳、分隔线

    # ─────────────────────────────────────────────────────
    # 角色颜色（谁在说话/做事）
    # ─────────────────────────────────────────────────────
    USER = "\033[92m"         # 用户输入（亮绿）
    USER_DIM = "\033[32m"     # 用户内容（暗绿）
    AI = "\033[97m"           # AI 输出（亮白）
    TOOL = "\033[33m"         # 工具调用（暗黄）- 操作感
    SUBAGENT = "\033[36m"     # SubAgent（暗青）- 与外层 Agent 呼应

    # ─────────────────────────────────────────────────────
    # 状态颜色（发生了什么）
    # ─────────────────────────────────────────────────────
    SUCCESS = "\033[92m"      # 成功（亮绿）
    WARNING = "\033[93m"      # 警告（亮黄）
    WARNING_DIM = "\033[33m"  # 警告内容（暗黄）
    ERROR = "\033[91m"        # 错误（亮红）
    ERROR_DIM = "\033[31m"    # 错误内容（暗红）

    # ─────────────────────────────────────────────────────
    # 背景色（仅用于重要分隔）
    # ─────────────────────────────────────────────────────
    BG_SUBAGENT = "\033[46m"  # SubAgent 分隔（青底）
    BG_DISPATCH = "\033[45m"  # 派发分隔（洋红底）
    BLACK = "\033[30m"        # 黑字（配合背景色）
    WHITE = "\033[37m"        # 白字（配合背景色）


class AgentLogger:
    """
    Structured logger for Agent activities.

    Provides formatted console output for:
    - User messages
    - AI thinking process
    - AI responses
    - Tool usage (including SubAgent dispatch)
    - SubAgent activities
    - Errors and warnings
    """

    def __init__(self, name: str = "MainAgent", window_seq: int = 0):
        self.name = name
        self.window_seq = window_seq  # 0 = primary, 2+ = 虚拟窗口
        self.logger = logging.getLogger(f"agent.{name}")
        self._indent_level = 0
        self._in_subagent = False
        self._current_subagent = None
        # 并行 SubAgent 支持
        self._subagent_counter = 0  # 全局序号
        self._active_subagents: dict[str, dict] = {}  # subagent_id → {seq, name, short_name, type, start_time}
        # 流式输出状态：当前行是否已输出前缀
        self._streaming_line_has_prefix = False

    def _timestamp(self) -> str:
        """Get formatted timestamp."""
        return datetime.now().strftime("%H:%M:%S.%f")[:-3]

    def _indent(self) -> str:
        """Get indentation string."""
        return "  " * self._indent_level

    def _get_window_prefix(self) -> str:
        """Get window prefix for multi-window log differentiation."""
        if self.window_seq == 0:
            return "[Agent]"
        else:
            return f"[Agent#{self.window_seq}]"

    def _print(self, text: str) -> None:
        """Print to console with window prefix and proper encoding safety."""
        prefix = self._get_window_prefix()
        output = f"{prefix} {text}"
        try:
            print(output, flush=True)
        except UnicodeEncodeError:
            # Fallback: replace unencodable characters
            encoding = sys.stdout.encoding or 'utf-8'
            safe_text = output.encode(encoding, errors='replace').decode(encoding)
            print(safe_text, flush=True)

    def _print_streaming(self, text: str, color: str = "") -> None:
        """Print streaming content with window prefix on each line.

        用于流式输出（thinking/response delta），确保每行都有窗口前缀。
        """
        if not text:
            return

        prefix = self._get_window_prefix()
        reset = Colors.RESET if color else ""
        lines = text.split('\n')

        for i, line in enumerate(lines):
            if i > 0:
                # 换行：结束当前行，开始新行
                print()
                self._streaming_line_has_prefix = False

            if line:  # 有内容要输出
                if not self._streaming_line_has_prefix:
                    # 当前行还没有前缀，添加前缀
                    try:
                        print(f"{prefix} {color}{line}{reset}", end="", flush=True)
                    except UnicodeEncodeError:
                        encoding = sys.stdout.encoding or 'utf-8'
                        safe_line = f"{prefix} {color}{line}{reset}".encode(encoding, errors='replace').decode(encoding)
                        print(safe_line, end="", flush=True)
                    self._streaming_line_has_prefix = True
                else:
                    # 当前行已有前缀，直接追加内容
                    try:
                        print(f"{color}{line}{reset}", end="", flush=True)
                    except UnicodeEncodeError:
                        encoding = sys.stdout.encoding or 'utf-8'
                        safe_line = f"{color}{line}{reset}".encode(encoding, errors='replace').decode(encoding)
                        print(safe_line, end="", flush=True)

    def _separator(self, char: str = "─", length: int = 60) -> None:
        """Print a separator line."""
        self._print(f"{Colors.DIM}{char * length}{Colors.RESET}")

    def _truncate(self, text: str, max_len: int) -> str:
        """Truncate text to max length, keeping meaningful content."""
        if not text:
            return ""
        if len(text) <= max_len:
            return text
        return text[:max_len] + ".."

    def _get_subagent_label(self, subagent_id: str = None) -> str:
        """Get formatted label for SubAgent (e.g., '[#1 客厅家具]')."""
        if subagent_id and subagent_id in self._active_subagents:
            info = self._active_subagents[subagent_id]
            seq = info.get('seq', '?')
            short_name = info.get('short_name', '')
            return f"[#{seq} {short_name}]"
        elif self._in_subagent:
            return f"[{self._current_subagent}]"
        else:
            return "[MainAgent]"

    # ─────────────────────────────────────────────────────
    # User Input
    # ─────────────────────────────────────────────────────

    def log_session_start(self) -> None:
        """Log start of a new interaction session."""
        self._separator("═")
        self._print(
            f"{Colors.SUCCESS}{Colors.BOLD}[START]{Colors.RESET} "
            f"{Colors.TERTIARY}{self._timestamp()}{Colors.RESET}"
        )

    def log_user_message(self, message: str) -> None:
        """Log user input message."""
        self.log_session_start()
        self._print(
            f"{Colors.USER}{Colors.BOLD}[USER]{Colors.RESET} "
            f"{Colors.USER_DIM}{message}{Colors.RESET}"
        )

    # ─────────────────────────────────────────────────────
    # AI Thinking
    # ─────────────────────────────────────────────────────

    def log_thinking_start(self) -> None:
        """Log start of thinking process."""
        agent_label = f"[{self._current_subagent}]" if self._in_subagent else "[MainAgent]"
        self._print(
            f"{self._indent()}{Colors.TERTIARY}"
            f"{agent_label} [THINK] ...{Colors.RESET}"
        )

    def log_thinking(self, content: str, is_delta: bool = False) -> None:
        """Log thinking content."""
        if is_delta:
            # For streaming: ensure each line has window prefix (暗灰，退居背景)
            self._print_streaming(content, Colors.SECONDARY)
        else:
            # For complete thinking block
            self._print(f"{self._indent()}{Colors.SECONDARY}{content}{Colors.RESET}")

    def log_thinking_end(self) -> None:
        """Log end of thinking process."""
        print()  # New line after streaming thinking
        self._streaming_line_has_prefix = False  # 重置流式状态
        self._print(f"{self._indent()}{Colors.TERTIARY}─ thinking complete ─{Colors.RESET}")

    # ─────────────────────────────────────────────────────
    # AI Response
    # ─────────────────────────────────────────────────────

    def log_response_start(self) -> None:
        """Log start of AI response."""
        agent_label = f"[{self._current_subagent}]" if self._in_subagent else "[MainAgent]"
        self._print(
            f"{self._indent()}{Colors.AI}{Colors.BOLD}"
            f"{agent_label} [AI]{Colors.RESET}"
        )

    def log_response(self, content: str, is_delta: bool = False) -> None:
        """Log response content."""
        if is_delta:
            # For streaming: ensure each line has window prefix (白色，主要信息)
            self._print_streaming(content, Colors.PRIMARY)
        else:
            # For complete response
            self._print(f"{self._indent()}{Colors.PRIMARY}{content}{Colors.RESET}")

    def log_response_end(self) -> None:
        """Log end of AI response."""
        print()  # New line after streaming response
        self._streaming_line_has_prefix = False  # 重置流式状态

    # ─────────────────────────────────────────────────────
    # Tool Usage
    # ─────────────────────────────────────────────────────

    def log_tool_use(self, tool_name: str, tool_input: dict,
                      subagent_id: str = None) -> None:
        """Log tool invocation with SubAgent tracking."""
        # Task 工具的 DISPATCH 信息已移到 enter_subagent() 中输出，这里跳过
        if tool_name == "Task":
            return

        # 获取正确的标签（支持并行 SubAgent）
        agent_label = self._get_subagent_label(subagent_id)

        # 普通工具 - 暗黄色（操作感）
        self._print(
            f"    {Colors.TOOL}{agent_label} "
            f"[TOOL] {Colors.BOLD}{tool_name}{Colors.RESET}"
        )
        # Format tool input nicely (技术细节，暗淡)
        input_str = self._format_tool_input(tool_input)
        if input_str:
            self._print(f"      {Colors.SECONDARY}{input_str}{Colors.RESET}")

    def log_tool_result(self, tool_name: str, result: Any, is_error: bool = False) -> None:
        """Log tool result."""
        agent_label = f"[{self._current_subagent}]" if self._in_subagent else "[MainAgent]"

        if is_error:
            self._print(
                f"{self._indent()}{Colors.ERROR}{agent_label} "
                f"Tool Error ({tool_name}): {Colors.ERROR_DIM}{result}{Colors.RESET}"
            )
        else:
            # Truncate long results (次要信息，暗淡)
            result_str = str(result)
            if len(result_str) > 300:
                result_str = result_str[:300] + "..."
            self._print(
                f"{self._indent()}{Colors.SECONDARY}{agent_label} "
                f"Result ({tool_name}): {result_str}{Colors.RESET}"
            )

    def log_permission_error(self, message: str) -> None:
        """Log permission error (SDK permission request)."""
        agent_label = f"[{self._current_subagent}]" if self._in_subagent else "[MainAgent]"
        # 截断过长的消息
        if len(message) > 200:
            message = message[:200] + "..."
        self._print(
            f"{self._indent()}{Colors.WARNING}{Colors.BOLD}"
            f"[PERMISSION]{Colors.RESET} {Colors.WARNING_DIM}"
            f"{agent_label} {message}{Colors.RESET}"
        )

    def _format_tool_input(self, tool_input: dict) -> str:
        """Format tool input for display."""
        if not tool_input:
            return ""

        # Handle common tool inputs
        if "file_path" in tool_input:
            return f"file: {tool_input['file_path']}"
        if "pattern" in tool_input:
            return f"pattern: {tool_input['pattern']}"
        if "command" in tool_input:
            cmd = tool_input['command']
            if len(cmd) > 100:
                cmd = cmd[:100] + "..."
            return f"command: {cmd}"

        # Default: JSON format
        try:
            formatted = json.dumps(tool_input, ensure_ascii=False)
            if len(formatted) > 150:
                formatted = formatted[:150] + "..."
            return formatted
        except:
            return str(tool_input)[:150]

    # ─────────────────────────────────────────────────────
    # SubAgent Management
    # ─────────────────────────────────────────────────────

    def enter_subagent(self, subagent_type: str,
                        subagent_id: str = None,
                        description: str = None) -> int:
        """
        Mark entering a SubAgent context.

        Args:
            subagent_type: Type of SubAgent (e.g., 'layout-agent')
            subagent_id: Unique ID for this SubAgent instance
            description: Task description for this SubAgent

        Returns:
            Assigned sequence number
        """
        self._in_subagent = True
        self._current_subagent = subagent_type
        # 不再累加缩进，使用固定缩进

        # 分配序号并记录信息
        self._subagent_counter += 1
        seq = self._subagent_counter
        short_name = self._truncate(description, 8) if description else f"任务{seq}"

        if subagent_id:
            self._active_subagents[subagent_id] = {
                'seq': seq,
                'name': description,
                'short_name': short_name,
                'type': subagent_type,
                'start_time': time.time()
            }

        # 输出 DISPATCH 信息（带序号，暗青色）
        self._print(
            f"\n{Colors.BG_DISPATCH}{Colors.WHITE}{Colors.BOLD}"
            f" SUBAGENT #{seq} DISPATCH {Colors.RESET}"
        )
        self._print(f"{Colors.SUBAGENT}├─ Type: {Colors.BOLD}{subagent_type}{Colors.RESET}")
        if description:
            prompt = description[:200] + "..." if len(description) > 200 else description
            self._print(f"{Colors.SUBAGENT}└─ Task: {Colors.RESET}{Colors.SECONDARY}{prompt}{Colors.RESET}")

        # 输出 SUBAGENT 头（顶格，暗青色）
        self._print(
            f"{Colors.BG_SUBAGENT}{Colors.BLACK}{Colors.BOLD}"
            f" {Symbols.ARROW_RIGHT} SUBAGENT #{seq}: {subagent_type} {Colors.RESET}"
        )
        if description:
            self._print(f"  {Colors.SECONDARY}任务: {description}{Colors.RESET}")
        return seq

    def exit_subagent(self, subagent_name: str = None, result_summary: str = None,
                       subagent_id: str = None) -> None:
        """Mark exiting a SubAgent context."""

        # 获取 SubAgent 信息
        seq = None
        elapsed = ""
        display_name = subagent_name or "SubAgent"

        if subagent_id and subagent_id in self._active_subagents:
            info = self._active_subagents[subagent_id]
            seq = info.get('seq')
            display_name = info.get('type', display_name)
            start_time = info.get('start_time')
            if start_time:
                elapsed = f" (耗时 {time.time() - start_time:.1f}s)"
            # 清理
            del self._active_subagents[subagent_id]

        if result_summary:
            self._print(
                f"{Colors.SUBAGENT}[{display_name}] Result: "
                f"{Colors.RESET}{Colors.PRIMARY}{result_summary[:200]}{Colors.RESET}"
            )

        # 显示带序号的完成信息（暗青色）
        seq_str = f"#{seq} " if seq else ""
        self._print(
            f"{Colors.BG_SUBAGENT}{Colors.BLACK}{Colors.BOLD}"
            f" {Symbols.ARROW_LEFT} SUBAGENT {seq_str}COMPLETE{elapsed} {Colors.RESET}\n"
        )
        # 不再递减缩进（使用固定缩进）
        self._in_subagent = False
        self._current_subagent = None

    # ─────────────────────────────────────────────────────
    # Events and Status
    # ─────────────────────────────────────────────────────

    def log_event(self, event_type: str, data: dict = None) -> None:
        """Log a generic event."""
        agent_label = f"[{self._current_subagent}]" if self._in_subagent else "[MainAgent]"
        self._print(
            f"{self._indent()}{Colors.TERTIARY}{agent_label} Event: {event_type}{Colors.RESET}"
        )
        if data:
            self._print(f"{self._indent()}  {Colors.SECONDARY}{data}{Colors.RESET}")

    def log_error(self, error: str) -> None:
        """Log an error."""
        self._print(
            f"{Colors.ERROR}{Colors.BOLD}[ERROR]{Colors.RESET} "
            f"{Colors.ERROR_DIM}{error}{Colors.RESET}"
        )

    def log_warning(self, warning: str) -> None:
        """Log a warning."""
        self._print(
            f"{Colors.WARNING}{Colors.BOLD}[WARN]{Colors.RESET} "
            f"{Colors.WARNING_DIM}{warning}{Colors.RESET}"
        )

    def log_info(self, info: str) -> None:
        """Log info message."""
        agent_label = f"[{self._current_subagent}]" if self._in_subagent else "[MainAgent]"
        self._print(
            f"{self._indent()}{Colors.SECONDARY}{agent_label} {info}{Colors.RESET}"
        )

    def log_complete(self, model: str | None = None) -> None:
        """Log completion of the entire interaction.

        Args:
            model: Optional model identifier to display as a model stamp
        """
        model_stamp = ""
        if model:
            model_stamp = f" {Colors.TERTIARY}[{model}]{Colors.RESET}"

        self._print(
            f"{Colors.SUCCESS}{Colors.BOLD}[COMPLETE]{Colors.RESET} "
            f"{Colors.TERTIARY}{self._timestamp()}{Colors.RESET}{model_stamp}"
        )
        self._separator("═")


# Global logger instances (keyed by window_seq for multi-window support)
_agent_loggers: dict[int, AgentLogger] = {}


def get_agent_logger(name: str = "MainAgent", window_seq: int = 0) -> AgentLogger:
    """Get or create the agent logger for a specific window.

    Args:
        name: Logger name (e.g., "MainAgent")
        window_seq: Window sequence number (0 = primary, 2+ = virtual windows)

    Returns:
        AgentLogger instance for the specified window
    """
    global _agent_loggers
    if window_seq not in _agent_loggers:
        _agent_loggers[window_seq] = AgentLogger(name, window_seq)
    return _agent_loggers[window_seq]
