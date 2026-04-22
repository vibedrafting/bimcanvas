"""Write tool — ported from Claude Code FileWriteTool.ts + utils/file.ts."""

from __future__ import annotations

import os
import tempfile
from pathlib import Path

from ._encoding import FileMetadata, detect
from ._path import resolve_path


def write(
    working_dir: Path,
    file_path: str,
    content: str,
) -> str:
    """Atomically write *content* to *file_path*.

    If the file already exists, its encoding and line-ending style are preserved.
    New files are written as UTF-8 with LF line endings.
    """
    path = resolve_path(working_dir, file_path)
    path.parent.mkdir(parents=True, exist_ok=True)

    if path.is_file():
        meta = detect(path)
    else:
        meta = FileMetadata(encoding="utf-8", line_endings="LF")

    out_content = content
    if meta.line_endings == "CRLF":
        out_content = out_content.replace("\r\n", "\n").replace("\n", "\r\n")

    encoding = meta.encoding if meta.encoding != "utf-8-sig" else "utf-8-sig"

    _atomic_write(path, out_content, encoding)

    rel = path.relative_to(working_dir) if path.is_relative_to(working_dir) else path
    return f"Successfully wrote to {rel}"


def _atomic_write(path: Path, content: str, encoding: str) -> None:
    """Write via temp file + rename for crash safety."""
    fd, tmp_path = tempfile.mkstemp(
        dir=str(path.parent),
        prefix=f".{path.name}.",
        suffix=".tmp",
    )
    try:
        with os.fdopen(fd, "w", encoding=encoding, newline="") as f:
            f.write(content)
        if path.exists():
            os.chmod(tmp_path, path.stat().st_mode)
        os.replace(tmp_path, str(path))
    except BaseException:
        try:
            os.unlink(tmp_path)
        except OSError:
            pass
        raise
