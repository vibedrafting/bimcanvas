"""Grep tool — ported from Claude Code GrepTool.ts + utils/ripgrep.ts."""

from __future__ import annotations

from pathlib import Path

from . import _ripgrep
from ._path import resolve_path

_VCS_DIRS = (".git", ".svn", ".hg", ".bzr", ".jj", ".sl")


async def grep(
    working_dir: Path,
    pattern: str,
    *,
    path: str | None = None,
    include_glob: str | None = None,
    file_type: str | None = None,
    output_mode: str = "files_with_matches",
    case_insensitive: bool = False,
    line_numbers: bool = True,
    before_context: int | None = None,
    after_context: int | None = None,
    context: int | None = None,
    head_limit: int = 250,
    offset: int = 0,
    multiline: bool = False,
) -> str:
    """Search file contents with ripgrep.

    Three output modes:
    - ``files_with_matches`` (default): file paths only
    - ``content``: matching lines with optional context
    - ``count``: per-file match counts
    """
    if path is not None:
        search_dir = resolve_path(working_dir, path)
    else:
        search_dir = working_dir

    args = _build_rg_args(
        pattern=pattern,
        output_mode=output_mode,
        case_insensitive=case_insensitive,
        line_numbers=line_numbers,
        before_context=before_context,
        after_context=after_context,
        context=context,
        multiline=multiline,
        include_glob=include_glob,
        file_type=file_type,
    )

    lines = await _ripgrep.run(args, search_dir)

    lines = _apply_pagination(lines, head_limit=head_limit, offset=offset)

    return _format_output(lines, working_dir, search_dir, output_mode, head_limit, offset)


def _build_rg_args(
    *,
    pattern: str,
    output_mode: str,
    case_insensitive: bool,
    line_numbers: bool,
    before_context: int | None,
    after_context: int | None,
    context: int | None,
    multiline: bool,
    include_glob: str | None,
    file_type: str | None,
) -> list[str]:
    args: list[str] = ["--hidden", "--max-columns", "500"]

    for vcs in _VCS_DIRS:
        args.extend(["--glob", f"!{vcs}"])

    if multiline:
        args.extend(["-U", "--multiline-dotall"])

    if case_insensitive:
        args.append("-i")

    if output_mode == "files_with_matches":
        args.append("-l")
    elif output_mode == "count":
        args.append("-c")

    if output_mode == "content" and line_numbers:
        args.append("-n")

    if output_mode == "content":
        if context is not None:
            args.extend(["-C", str(context)])
        else:
            if before_context is not None:
                args.extend(["-B", str(before_context)])
            if after_context is not None:
                args.extend(["-A", str(after_context)])

    if pattern.startswith("-"):
        args.extend(["-e", pattern])
    else:
        args.append(pattern)

    if file_type:
        args.extend(["--type", file_type])

    if include_glob:
        for part in include_glob.split(","):
            part = part.strip()
            if part:
                args.extend(["--glob", part])

    return args


def _apply_pagination(
    lines: list[str], *, head_limit: int, offset: int
) -> list[str]:
    if offset > 0:
        lines = lines[offset:]
    if head_limit > 0:
        lines = lines[:head_limit]
    return lines


def _format_output(
    lines: list[str],
    working_dir: Path,
    search_dir: Path,
    output_mode: str,
    head_limit: int,
    offset: int,
) -> str:
    if not lines:
        if output_mode == "files_with_matches":
            return "No files found"
        return "No matches found"

    result_lines: list[str] = []
    for line in lines:
        result_lines.append(_to_relative(line, working_dir, search_dir))

    result = "\n".join(result_lines)

    if head_limit > 0 and len(lines) == head_limit:
        result += f"\n\n[Results limited to {head_limit} entries (offset={offset}). Use head_limit/offset for pagination.]"

    return result


def _to_relative(line: str, working_dir: Path, search_dir: Path) -> str:
    """Convert absolute/search-relative paths in output lines to working-dir-relative."""
    if not line:
        return line
    colon_idx = line.find(":")
    path_part = line[:colon_idx] if colon_idx != -1 else line
    rest = line[colon_idx:] if colon_idx != -1 else ""

    try:
        abs_path = (search_dir / path_part).resolve()
        rel = str(abs_path.relative_to(working_dir))
        return rel + rest
    except (ValueError, OSError):
        return line
