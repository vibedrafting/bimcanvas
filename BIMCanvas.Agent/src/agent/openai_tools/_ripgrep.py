"""Ripgrep (rg) subprocess wrapper — ported from Claude Code utils/ripgrep.ts."""

from __future__ import annotations

import asyncio
import shutil
from pathlib import Path

_rg_path: str | None = None


def find_ripgrep() -> str:
    """Return the path to the ``rg`` binary, or raise if not found."""
    global _rg_path
    if _rg_path is None:
        found = shutil.which("rg")
        if found is None:
            raise FileNotFoundError(
                "ripgrep (rg) not found in PATH. "
                "Install it: https://github.com/BurntSushi/ripgrep#installation"
            )
        _rg_path = found
    return _rg_path


def is_available() -> bool:
    """Check whether ripgrep is available on this system."""
    try:
        find_ripgrep()
        return True
    except FileNotFoundError:
        return False


async def run(
    args: list[str],
    cwd: Path,
    *,
    timeout: float = 20.0,
) -> list[str]:
    """Execute ripgrep and return non-empty stdout lines.

    Exit code 0 = matches found, 1 = no matches (both normal).
    Other exit codes raise ``RuntimeError``.
    """
    rg = find_ripgrep()
    proc = await asyncio.create_subprocess_exec(
        rg,
        *args,
        cwd=str(cwd),
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
    )
    try:
        stdout_bytes, stderr_bytes = await asyncio.wait_for(
            proc.communicate(), timeout=timeout
        )
    except asyncio.TimeoutError:
        proc.kill()
        await proc.communicate()
        raise TimeoutError(f"ripgrep timed out after {timeout}s")

    returncode = proc.returncode
    if returncode in (0, 1):
        text = stdout_bytes.decode("utf-8", errors="replace")
        return [line for line in text.splitlines() if line]
    stderr_text = stderr_bytes.decode("utf-8", errors="replace").strip()
    raise RuntimeError(f"ripgrep failed (exit {returncode}): {stderr_text}")
