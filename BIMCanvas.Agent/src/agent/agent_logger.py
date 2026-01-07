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
class Colors:
    RESET = "\033[0m"
    BOLD = "\033[1m"
    DIM = "\033[2m"

    # Text colors
    BLACK = "\033[30m"
    RED = "\033[31m"
    GREEN = "\033[32m"
    YELLOW = "\033[33m"
    BLUE = "\033[34m"
    MAGENTA = "\033[35m"
    CYAN = "\033[36m"
    WHITE = "\033[37m"

    # Bright colors
    BRIGHT_BLACK = "\033[90m"
    BRIGHT_RED = "\033[91m"
    BRIGHT_GREEN = "\033[92m"
    BRIGHT_YELLOW = "\033[93m"
    BRIGHT_BLUE = "\033[94m"
    BRIGHT_MAGENTA = "\033[95m"
    BRIGHT_CYAN = "\033[96m"
    BRIGHT_WHITE = "\033[97m"

    # Background colors
    BG_BLUE = "\033[44m"
    BG_GREEN = "\033[42m"
    BG_YELLOW = "\033[43m"
    BG_MAGENTA = "\033[45m"
    BG_CYAN = "\033[46m"


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

    def __init__(self, name: str = "MainAgent"):
        self.name = name
        self.logger = logging.getLogger(f"agent.{name}")
        self._indent_level = 0
        self._in_subagent = False
        self._current_subagent = None
        # 并行 SubAgent 支持
        self._subagent_counter = 0  # 全局序号
        self._active_subagents: dict[str, dict] = {}  # subagent_id → {seq, name, short_name, type, start_time}

    def _timestamp(self) -> str:
        """Get formatted timestamp."""
        return datetime.now().strftime("%H:%M:%S.%f")[:-3]

    def _indent(self) -> str:
        """Get indentation string."""
        return "  " * self._indent_level

    def _print(self, text: str) -> None:
        """Print to console with proper formatting and encoding safety."""
        try:
            print(text, flush=True)
        except UnicodeEncodeError:
            # Fallback: replace unencodable characters
            encoding = sys.stdout.encoding or 'utf-8'
            safe_text = text.encode(encoding, errors='replace').decode(encoding)
            print(safe_text, flush=True)

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

    def log_user_message(self, message: str) -> None:
        """Log user input message."""
        self._separator("═")
        self._print(
            f"{Colors.BRIGHT_GREEN}{Colors.BOLD}[USER]{Colors.RESET} "
            f"{Colors.DIM}{self._timestamp()}{Colors.RESET}"
        )
        self._print(f"{Colors.GREEN}{message}{Colors.RESET}")
        self._separator()

    # ─────────────────────────────────────────────────────
    # AI Thinking
    # ─────────────────────────────────────────────────────

    def log_thinking_start(self) -> None:
        """Log start of thinking process."""
        agent_label = f"[{self._current_subagent}]" if self._in_subagent else "[MainAgent]"
        self._print(
            f"{self._indent()}{Colors.BRIGHT_MAGENTA}{Colors.BOLD}"
            f"{agent_label} Thinking...{Colors.RESET}"
        )

    def log_thinking(self, content: str, is_delta: bool = False) -> None:
        """Log thinking content."""
        if is_delta:
            # For streaming, just append without newline
            print(f"{Colors.DIM}{Colors.MAGENTA}{content}{Colors.RESET}", end="", flush=True)
        else:
            # For complete thinking block
            self._print(f"{self._indent()}{Colors.DIM}{Colors.MAGENTA}{content}{Colors.RESET}")

    def log_thinking_end(self) -> None:
        """Log end of thinking process."""
        print()  # New line after streaming thinking
        self._print(f"{self._indent()}{Colors.DIM}─ thinking complete ─{Colors.RESET}")

    # ─────────────────────────────────────────────────────
    # AI Response
    # ─────────────────────────────────────────────────────

    def log_response_start(self) -> None:
        """Log start of AI response."""
        agent_label = f"[{self._current_subagent}]" if self._in_subagent else "[MainAgent]"
        self._print(
            f"{self._indent()}{Colors.BRIGHT_CYAN}{Colors.BOLD}"
            f"{agent_label} Response:{Colors.RESET}"
        )

    def log_response(self, content: str, is_delta: bool = False) -> None:
        """Log response content."""
        if is_delta:
            # For streaming, just append without newline
            print(f"{Colors.CYAN}{content}{Colors.RESET}", end="", flush=True)
        else:
            # For complete response
            self._print(f"{self._indent()}{Colors.CYAN}{content}{Colors.RESET}")

    def log_response_end(self) -> None:
        """Log end of AI response."""
        print()  # New line after streaming response

    # ─────────────────────────────────────────────────────
    # Tool Usage
    # ─────────────────────────────────────────────────────

    def log_tool_use(self, tool_name: str, tool_input: dict,
                      subagent_id: str = None) -> None:
        """Log tool invocation with SubAgent tracking."""
        # 获取正确的标签（支持并行 SubAgent）
        agent_label = self._get_subagent_label(subagent_id)

        # Special formatting for Task tool (SubAgent dispatch)
        if tool_name == "Task":
            self._print(
                f"\n{self._indent()}{Colors.BG_MAGENTA}{Colors.WHITE}{Colors.BOLD}"
                f" SUBAGENT DISPATCH {Colors.RESET}"
            )
            subagent_type = tool_input.get("subagent_type", "unknown")
            prompt = tool_input.get("prompt", "")
            self._print(
                f"{self._indent()}{Colors.BRIGHT_MAGENTA}├─ SubAgent: "
                f"{Colors.BOLD}{subagent_type}{Colors.RESET}"
            )
            # Truncate long prompts
            if len(prompt) > 200:
                prompt = prompt[:200] + "..."
            self._print(
                f"{self._indent()}{Colors.BRIGHT_MAGENTA}└─ Task: "
                f"{Colors.RESET}{prompt}"
            )
        else:
            # Regular tool - 使用带序号的标签
            self._print(
                f"{self._indent()}{Colors.BRIGHT_YELLOW}{agent_label} "
                f"Tool: {Colors.BOLD}{tool_name}{Colors.RESET}"
            )
            # Format tool input nicely
            input_str = self._format_tool_input(tool_input)
            if input_str:
                self._print(f"{self._indent()}  {Colors.DIM}{input_str}{Colors.RESET}")

    def log_tool_result(self, tool_name: str, result: Any, is_error: bool = False) -> None:
        """Log tool result."""
        agent_label = f"[{self._current_subagent}]" if self._in_subagent else "[MainAgent]"

        if is_error:
            self._print(
                f"{self._indent()}{Colors.BRIGHT_RED}{agent_label} "
                f"Tool Error ({tool_name}): {result}{Colors.RESET}"
            )
        else:
            # Truncate long results
            result_str = str(result)
            if len(result_str) > 300:
                result_str = result_str[:300] + "..."
            self._print(
                f"{self._indent()}{Colors.DIM}{agent_label} "
                f"Tool Result ({tool_name}): {result_str}{Colors.RESET}"
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
        self._indent_level += 1

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

        # 打印带序号的 SUBAGENT 头
        self._print(
            f"\n{Colors.BG_CYAN}{Colors.BLACK}{Colors.BOLD}"
            f" {Symbols.ARROW_RIGHT} SUBAGENT #{seq}: {subagent_type} {Colors.RESET}"
        )
        if description:
            self._print(f"{self._indent()}{Colors.CYAN}任务: {description}{Colors.RESET}")
        self._separator("─", 50)
        return seq

    def exit_subagent(self, subagent_name: str = None, result_summary: str = None,
                       subagent_id: str = None) -> None:
        """Mark exiting a SubAgent context."""
        self._separator("─", 50)

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
                f"{Colors.BRIGHT_CYAN}[{display_name}] Result: "
                f"{Colors.RESET}{result_summary[:200]}"
            )

        # 显示带序号的完成信息
        seq_str = f"#{seq} " if seq else ""
        self._print(
            f"{Colors.BG_CYAN}{Colors.BLACK}{Colors.BOLD}"
            f" {Symbols.ARROW_LEFT} SUBAGENT {seq_str}COMPLETE{elapsed} {Colors.RESET}\n"
        )
        self._indent_level = max(0, self._indent_level - 1)
        self._in_subagent = False
        self._current_subagent = None

    # ─────────────────────────────────────────────────────
    # Events and Status
    # ─────────────────────────────────────────────────────

    def log_event(self, event_type: str, data: dict = None) -> None:
        """Log a generic event."""
        agent_label = f"[{self._current_subagent}]" if self._in_subagent else "[MainAgent]"
        self._print(
            f"{self._indent()}{Colors.DIM}{agent_label} Event: {event_type}{Colors.RESET}"
        )
        if data:
            self._print(f"{self._indent()}  {Colors.DIM}{data}{Colors.RESET}")

    def log_error(self, error: str) -> None:
        """Log an error."""
        self._print(
            f"{Colors.BRIGHT_RED}{Colors.BOLD}[ERROR]{Colors.RESET} "
            f"{Colors.RED}{error}{Colors.RESET}"
        )

    def log_warning(self, warning: str) -> None:
        """Log a warning."""
        self._print(
            f"{Colors.BRIGHT_YELLOW}{Colors.BOLD}[WARN]{Colors.RESET} "
            f"{Colors.YELLOW}{warning}{Colors.RESET}"
        )

    def log_info(self, info: str) -> None:
        """Log info message."""
        agent_label = f"[{self._current_subagent}]" if self._in_subagent else "[MainAgent]"
        self._print(
            f"{self._indent()}{Colors.DIM}{agent_label} {info}{Colors.RESET}"
        )

    def log_complete(self) -> None:
        """Log completion of the entire interaction."""
        self._separator("═")
        self._print(
            f"{Colors.BRIGHT_GREEN}{Colors.BOLD}[COMPLETE]{Colors.RESET} "
            f"{Colors.DIM}{self._timestamp()}{Colors.RESET}"
        )
        self._separator("═")


# Global logger instance
_agent_logger: AgentLogger | None = None


def get_agent_logger(name: str = "MainAgent") -> AgentLogger:
    """Get or create the agent logger."""
    global _agent_logger
    if _agent_logger is None or _agent_logger.name != name:
        _agent_logger = AgentLogger(name)
    return _agent_logger
