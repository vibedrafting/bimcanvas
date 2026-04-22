"""Path resolution and sandbox boundary check."""

from __future__ import annotations

from pathlib import Path


def resolve_path(working_directory: Path, file_path: str) -> Path:
    """Resolve *file_path* against *working_directory* and verify it stays inside."""
    candidate = Path(file_path)
    if not candidate.is_absolute():
        candidate = working_directory / candidate
    resolved = candidate.resolve()
    if working_directory not in (resolved, *resolved.parents):
        raise ValueError(f"Path escapes working directory: {file_path}")
    return resolved
