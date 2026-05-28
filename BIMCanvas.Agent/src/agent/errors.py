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
    主控在 connect() 中识别并重抛此异常,携带 prompt 度量便于诊断。
    """

    def __init__(self, prompt_size: int) -> None:
        super().__init__(
            f"Windows CreateProcess command line exceeded 32767 chars "
            f"(prompt_size={prompt_size}). 已切换 SystemPromptFile 模式,如仍触发请检查 "
            f"其他 CLI 参数(如 plugins / mcp_servers)是否过长。"
        )
        self.prompt_size = prompt_size


class SystemPromptFileWriteError(RuntimeError):
    """system_prompt 落盘到 BIMCANVAS_HOME/cache/ 失败(磁盘满 / 权限 / 路径无效等)。"""

    def __init__(self, path: str, cause: BaseException) -> None:
        super().__init__(f"Failed to write system prompt cache file: {path}: {cause}")
        self.path = path
        self.__cause__ = cause
