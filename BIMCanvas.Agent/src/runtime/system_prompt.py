"""SystemPromptFile 落盘助手 (WP-2 M2)。

把拼好的 system_prompt 文本落盘到 BIMCANVAS_HOME/cache/system_prompt.window_{seq}.runtime.md,
返回 SDK 0.1.51+ 的 SystemPromptFile dict({"type":"file","path":...}),
让 SDK 走 --system-prompt-file 而非 --system-prompt CLI 参数,绕过 Windows
CreateProcess 32767 字符上限。

window_{seq} 后缀对多虚拟窗口并发(http_server 模式)做物理隔离,避免互相覆盖。
"""

from __future__ import annotations

from claude_agent_sdk.types import SystemPromptFile

from ..agent.errors import SystemPromptFileWriteError
from ..config.loader import resolve_bimcanvas_home


def materialize_system_prompt_file(text: str, window_seq: int) -> SystemPromptFile:
    """落盘 system_prompt 到 cache 目录并返回 SDK SystemPromptFile dict。

    Args:
        text: 拼好的 system_prompt 完整文本(含项目路径/工作目录后缀)
        window_seq: 窗口序号(0=primary, 2+=虚拟窗口),用作文件名后缀

    Returns:
        SystemPromptFile TypedDict:{"type": "file", "path": <绝对路径 str>}

    Raises:
        SystemPromptFileWriteError: 落盘失败(磁盘满/权限/路径无效等)
    """
    cache_dir = resolve_bimcanvas_home() / "cache"
    path = cache_dir / f"system_prompt.window_{window_seq}.runtime.md"
    try:
        cache_dir.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")
    except OSError as e:
        raise SystemPromptFileWriteError(str(path), e) from e
    return {"type": "file", "path": str(path)}
