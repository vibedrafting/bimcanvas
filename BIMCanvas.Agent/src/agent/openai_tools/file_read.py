"""Read tool — ported from Claude Code FileReadTool.ts (text path only)."""

from __future__ import annotations

from pathlib import Path

from ._encoding import read_text
from ._path import resolve_path

_DEFAULT_LIMIT = 2000


def read(
    working_dir: Path,
    file_path: str,
    *,
    offset: int | None = None,
    limit: int | None = None,
) -> str:
    """Read a text file and return cat -n formatted output.

    Parameters
    ----------
    offset : int, optional
        0-based line offset to start reading from.
    limit : int, optional
        Maximum number of lines to return (default 2000).
    """
    path = resolve_path(working_dir, file_path)

    if not path.exists():
        raise FileNotFoundError(f"File not found: {file_path}")
    if not path.is_file():
        raise ValueError(f"Not a file: {file_path}")

    content, _meta = read_text(path)
    lines = content.splitlines()
    total_lines = len(lines)

    start = offset if offset and offset > 0 else 0
    count = limit if limit and limit > 0 else _DEFAULT_LIMIT

    selected = lines[start : start + count]

    result_lines: list[str] = []
    for i, line in enumerate(selected, start=start + 1):
        result_lines.append(f"{i}\t{line}")

    result = "\n".join(result_lines)

    if start + count < total_lines:
        result += f"\n\n[File has {total_lines} lines total. Showing lines {start + 1}-{start + len(selected)}.]"

    return result
