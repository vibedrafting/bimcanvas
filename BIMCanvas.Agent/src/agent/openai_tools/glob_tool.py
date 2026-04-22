"""Glob tool — ported from Claude Code GlobTool.ts + utils/glob.ts."""

from __future__ import annotations

from pathlib import Path

from . import _ripgrep
from ._path import resolve_path

_VCS_DIRS = (".git", ".svn", ".hg", ".bzr", ".jj", ".sl")


async def glob(
    working_dir: Path,
    pattern: str,
    *,
    path: str | None = None,
) -> str:
    """Return matching file paths sorted by modification time (newest first).

    Uses ripgrep ``--files --glob`` for performance and mtime sorting.
    """
    if path is not None:
        search_dir = resolve_path(working_dir, path)
    else:
        search_dir = working_dir

    args = [
        "--files",
        "--glob", pattern,
        "--sort=modified",
        "--hidden",
    ]
    for vcs in _VCS_DIRS:
        args.extend(["--glob", f"!{vcs}"])

    lines = await _ripgrep.run(args, search_dir)

    if not lines:
        return "No files found"

    results = []
    for line in lines:
        try:
            abs_path = (search_dir / line).resolve()
            rel = abs_path.relative_to(working_dir)
            results.append(str(rel))
        except (ValueError, OSError):
            results.append(line)

    return "\n".join(results)
