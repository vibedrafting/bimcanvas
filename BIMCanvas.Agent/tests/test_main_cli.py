from __future__ import annotations

import sys
from pathlib import Path


AGENT_ROOT = Path(__file__).resolve().parents[1]
if str(AGENT_ROOT) not in sys.path:
    sys.path.insert(0, str(AGENT_ROOT))

from src import main as agent_main


def test_main_accepts_server_managed_cli_args(monkeypatch) -> None:
    captured: dict[str, object] = {}

    monkeypatch.setattr(agent_main, "ensure_server_managed_startup", lambda: None)
    monkeypatch.setattr(
        agent_main,
        "run_server",
        lambda host=None, port=None: captured.update({"host": host, "port": port}),
    )

    agent_main.main(
        [
            "--serve",
            "--host",
            "127.0.0.1",
            "--port",
            "8865",
            "--managed-by-server",
            "--managed-agent-root",
            str(AGENT_ROOT),
            "--managed-home",
            "C:/Users/test/Documents/BIMCanvas",
        ]
    )

    assert captured == {"host": "127.0.0.1", "port": 8865}
