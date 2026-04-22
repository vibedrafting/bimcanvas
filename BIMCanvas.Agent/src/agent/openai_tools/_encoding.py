"""File encoding and line-ending detection — ported from Claude Code utils/fileRead.ts."""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

_BOM_UTF8 = b"\xef\xbb\xbf"
_BOM_UTF16_LE = b"\xff\xfe"
_PROBE_SIZE = 4096


@dataclass(frozen=True)
class FileMetadata:
    """Detected encoding and line-ending style of a text file."""

    encoding: str  # "utf-8-sig" | "utf-16-le" | "utf-8"
    line_endings: str  # "CRLF" | "LF"


def detect(path: Path) -> FileMetadata:
    """Detect encoding (BOM) and line endings of *path*."""
    raw = path.read_bytes()
    head = raw[:_PROBE_SIZE]

    if head.startswith(_BOM_UTF16_LE):
        encoding = "utf-16-le"
    elif head.startswith(_BOM_UTF8):
        encoding = "utf-8-sig"
    else:
        encoding = "utf-8"

    sample = head.decode(encoding, errors="replace")[:_PROBE_SIZE]
    crlf_count = sample.count("\r\n")
    lf_count = sample.count("\n") - crlf_count
    line_endings = "CRLF" if crlf_count > lf_count else "LF"

    return FileMetadata(encoding=encoding, line_endings=line_endings)


def read_text(path: Path) -> tuple[str, FileMetadata]:
    """Read file content as text, normalizing line endings to LF.

    Returns ``(content_with_lf, metadata)`` so callers can write back
    in the original encoding / line-ending style.
    """
    meta = detect(path)
    content = path.read_text(encoding=meta.encoding, errors="replace")
    if meta.line_endings == "CRLF":
        content = content.replace("\r\n", "\n")
    return content, meta
