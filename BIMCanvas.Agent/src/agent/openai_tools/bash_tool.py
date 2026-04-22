"""Bash tool — improved from current implementation with timeout and output truncation."""

from __future__ import annotations

import asyncio
from pathlib import Path

_DEFAULT_TIMEOUT = 120.0
_MAX_OUTPUT_CHARS = 100_000


async def run_shell(
    working_dir: Path,
    command: str,
    *,
    timeout: float | None = None,
) -> str:
    """Execute a shell command and return stdout/stderr.

    Parameters
    ----------
    timeout : float, optional
        Seconds before the process is killed (default 120).
    """
    effective_timeout = timeout if timeout and timeout > 0 else _DEFAULT_TIMEOUT

    proc = await asyncio.create_subprocess_shell(
        command,
        cwd=str(working_dir),
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
    )

    try:
        stdout_bytes, stderr_bytes = await asyncio.wait_for(
            proc.communicate(), timeout=effective_timeout
        )
    except asyncio.TimeoutError:
        proc.kill()
        await proc.communicate()
        return f"Command timed out after {effective_timeout}s"

    stdout_text = stdout_bytes.decode("utf-8", errors="replace")
    stderr_text = stderr_bytes.decode("utf-8", errors="replace")

    if proc.returncode == 0:
        output = stdout_text.strip() or "(no output)"
    else:
        output = f"exit={proc.returncode}\nSTDOUT:\n{stdout_text}\nSTDERR:\n{stderr_text}".strip()

    if len(output) > _MAX_OUTPUT_CHARS:
        output = output[:_MAX_OUTPUT_CHARS] + f"\n\n[Output truncated at {_MAX_OUTPUT_CHARS} characters]"

    return output
