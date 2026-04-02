#!/bin/bash
set -euo pipefail

log() {
    echo "[start-agent.sh] $*"
}

wait_for_path() {
    local target="$1"
    local seconds="${2:-60}"
    local elapsed=0

    while [ ! -e "$target" ]; do
        if [ "$elapsed" -ge "$seconds" ]; then
            log "Timed out waiting for $target"
            return 1
        fi

        sleep 1
        elapsed=$((elapsed + 1))
    done

    return 0
}

if [ "$#" -gt 0 ]; then
    exec "$@"
fi

export BIMCANVAS_HOME="${BIMCANVAS_HOME:-/data}"
export BIMCANVAS_AGENT_MANAGED_BY_SERVER="${BIMCANVAS_AGENT_MANAGED_BY_SERVER:-1}"
export BIMCANVAS_SERVER_URL="${BIMCANVAS_SERVER_URL:-http://bimcanvas-server:5000}"
export SERVER_HOST="${SERVER_HOST:-0.0.0.0}"
export SERVER_PORT="${SERVER_PORT:-8865}"

log "BIMCANVAS_HOME=$BIMCANVAS_HOME"
log "BIMCANVAS_SERVER_URL=$BIMCANVAS_SERVER_URL"

wait_for_path "$BIMCANVAS_HOME/config.json" 120
wait_for_path "$BIMCANVAS_HOME/server_config.json" 120
wait_for_path "$BIMCANVAS_HOME/BIMCANVAS.md" 120
wait_for_path "$BIMCANVAS_HOME/agents" 120
wait_for_path "$BIMCANVAS_HOME/.claude-plugin/plugin.json" 120

if [ -n "${BIMCANVAS_CCR_BASE_URL:-}" ]; then
    CCR_ENABLED="$(python3 - <<'PY'
import json
import os
from pathlib import Path

config_path = Path(os.environ["BIMCANVAS_HOME"]) / "server_config.json"
try:
    data = json.loads(config_path.read_text(encoding="utf-8-sig"))
    enabled = bool(((data.get("ccr") or {}).get("enabled")))
except Exception:
    enabled = False
print("1" if enabled else "0")
PY
)"

    if [ "$CCR_ENABLED" = "1" ]; then
        export AGENT_SDK_API_KEY="${AGENT_SDK_API_KEY:-bimcanvas-ccr}"
        export AGENT_SDK_BASE_URL="${BIMCANVAS_CCR_BASE_URL%/}"
        log "AI mode: CCR gateway ($AGENT_SDK_BASE_URL)"
    else
        unset AGENT_SDK_API_KEY || true
        unset AGENT_SDK_BASE_URL || true
        log "AI mode: direct Anthropic-compatible endpoint"
    fi
else
    log "AI mode: direct Anthropic-compatible endpoint"
fi

exec /opt/venv/bin/bimcanvas-agent --serve
