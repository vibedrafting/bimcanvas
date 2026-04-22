"""Edit tool — ported from Claude Code FileEditTool.ts + FileEditTool/utils.ts."""

from __future__ import annotations

from pathlib import Path

from ._encoding import read_text
from ._path import resolve_path
from .file_write import _atomic_write

_CURLY_QUOTE_MAP = str.maketrans({
    "‘": "'",   # left single
    "’": "'",   # right single
    "“": '"',   # left double
    "”": '"',   # right double
})


def edit(
    working_dir: Path,
    file_path: str,
    old_string: str,
    new_string: str,
    replace_all: bool = False,
) -> str:
    """Replace text in a file.

    When *replace_all* is ``False`` (the default), *old_string* must appear
    exactly once in the file.  If it appears more than once, an error listing
    the duplicate locations is returned so the caller can provide more context.
    """
    path = resolve_path(working_dir, file_path)

    if not path.is_file():
        raise FileNotFoundError(f"File not found: {file_path}")

    content, meta = read_text(path)

    actual = _find_actual_string(content, old_string)
    if actual is None:
        raise ValueError(
            f"old_string not found in {file_path}. "
            "Make sure the string matches exactly (including whitespace and indentation)."
        )

    if not replace_all:
        count = content.count(actual)
        if count > 1:
            locations = _show_match_locations(content, actual)
            raise ValueError(
                f"old_string appears {count} times in {file_path}. "
                f"Provide more surrounding context to make it unique.\n{locations}"
            )

    if replace_all:
        new_content = content.replace(actual, new_string)
    else:
        new_content = content.replace(actual, new_string, 1)

    if new_content == content:
        return "No changes made (old_string == new_string)."

    out = new_content
    if meta.line_endings == "CRLF":
        out = out.replace("\r\n", "\n").replace("\n", "\r\n")

    _atomic_write(path, out, meta.encoding)

    rel = path.relative_to(working_dir) if path.is_relative_to(working_dir) else path
    return f"Successfully edited {rel}"


# ---------------------------------------------------------------------------
# Helpers ported from FileEditTool/utils.ts
# ---------------------------------------------------------------------------

def _normalize_quotes(s: str) -> str:
    """Normalize curly/smart quotes to ASCII straight quotes."""
    return s.translate(_CURLY_QUOTE_MAP)


def _find_actual_string(content: str, search: str) -> str | None:
    """Find *search* in *content*, falling back to quote-normalized matching.

    Returns the actual substring from the file (preserving original quotes),
    or ``None`` if no match.
    """
    if search in content:
        return search

    norm_content = _normalize_quotes(content)
    norm_search = _normalize_quotes(search)
    if norm_search == search:
        return None

    idx = norm_content.find(norm_search)
    if idx == -1:
        return None
    return content[idx : idx + len(norm_search)]


def _show_match_locations(content: str, search: str, *, max_shown: int = 5) -> str:
    """Return a short listing of where *search* appears (line numbers + context)."""
    lines = content.splitlines()
    locations: list[str] = []
    search_lines = search.splitlines()
    first_line = search_lines[0] if search_lines else search

    for i, line in enumerate(lines, 1):
        if first_line in line:
            snippet = line.strip()[:80]
            locations.append(f"  Line {i}: ...{snippet}...")
            if len(locations) >= max_shown:
                break

    return "\n".join(locations)
