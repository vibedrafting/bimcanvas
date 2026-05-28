"""Host-facing agent exceptions."""

from __future__ import annotations


class TurnPausedError(RuntimeError):
    """Raised when a streaming turn pauses on a pending blocking interaction."""

    def __init__(self, interaction_id: str) -> None:
        super().__init__(f"Turn paused by interaction {interaction_id}")
        self.interaction_id = interaction_id


class CLICommandLineTooLongError(RuntimeError):
    """Windows CreateProcess 命令行越过 32767 字符上限。

    SDK 把 winerror==206 包成 CLIConnectionError(__cause__ 为 OSError),
    主控在 connect() 中识别并重抛此异常。

    现状(WP-2 M2.2 清理后):M2.1 SystemPromptFile 已绕过 system_prompt 参数路径,
    本异常在常规场景下理论上不再触发;保留作为未来 MCP/plugin/agents/env 等其他
    CLI args 规模爆炸时的兜底诊断,提示运维检查相关配置。
    """

    def __init__(self) -> None:
        super().__init__(
            "Windows CreateProcess command line exceeded 32767 chars. "
            "SystemPromptFile 已绕过 system_prompt 路径,本错误意味着其他 CLI args "
            "(MCP/plugin/agents/env 等)规模爆炸,请检查相关配置。"
        )


class SystemPromptFileWriteError(RuntimeError):
    """system_prompt 落盘到 BIMCANVAS_HOME/cache/ 失败(磁盘满 / 权限 / 路径无效等)。"""

    def __init__(self, path: str, cause: BaseException) -> None:
        super().__init__(f"Failed to write system prompt cache file: {path}: {cause}")
        self.path = path
        self.__cause__ = cause
