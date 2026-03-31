#!/bin/bash
set -euo pipefail

log() {
    echo "[start.sh] $*"
}

copy_if_missing() {
    local source_path="$1"
    local target_path="$2"

    if [ -e "$target_path" ] || [ ! -e "$source_path" ]; then
        return
    fi

    mkdir -p "$(dirname "$target_path")"
    if [ -d "$source_path" ]; then
        cp -R "$source_path" "$target_path"
    else
        cp "$source_path" "$target_path"
    fi
    log "Initialized $(basename "$target_path")"
}

BIMCANVAS_HOME="${BIMCANVAS_HOME:-/data}"
AGENT_VENV_BIN="/app/BIMCanvas.Agent/venv/bin"
AGENT_VENV_PYTHON="$AGENT_VENV_BIN/python"
export BIMCANVAS_HOME
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Production}"
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://0.0.0.0:5000}"
export SERVER_HOST="${SERVER_HOST:-0.0.0.0}"
export BIMCANVAS_WEB_URL="${BIMCANVAS_WEB_URL:-http://localhost:5000}"
export BIMCANVAS_WEB_DIST="${BIMCANVAS_WEB_DIST:-/app/BIMCanvas.Web/dist}"
export PLAYWRIGHT_BROWSERS_PATH="${PLAYWRIGHT_BROWSERS_PATH:-/root/.local/share/ms-playwright}"
export BIMCANVAS_PYTHON_COMMAND="${BIMCANVAS_PYTHON_COMMAND:-$AGENT_VENV_PYTHON}"
export PATH="${AGENT_VENV_BIN}:/root/.dotnet/tools:${PATH}"

if [ "$#" -gt 0 ]; then
    exec "$@"
fi

log "BIMCANVAS_HOME=$BIMCANVAS_HOME"
log "BIMCANVAS_PYTHON_COMMAND=$BIMCANVAS_PYTHON_COMMAND"

CONFIG_ROOT="$BIMCANVAS_HOME"
TEMPLATE_ROOT="/app/BIMCanvas.Server/Templates/global-config"
SERVER_TEMPLATE_ROOT="$TEMPLATE_ROOT/server"
AGENT_TEMPLATE_ROOT="$TEMPLATE_ROOT/agent"

mkdir -p "$CONFIG_ROOT" "$CONFIG_ROOT/Projects" "$CONFIG_ROOT/screenshots"

SERVER_CONFIG_PATH="$CONFIG_ROOT/server_config.json"
WEB_CONFIG_PATH="$CONFIG_ROOT/web_config.json"
AGENT_CONFIG_PATH="$CONFIG_ROOT/config.json"
CCR_CONFIG_PATH="$CONFIG_ROOT/ccr_config.json"

BOOTSTRAPPED_SERVER_CONFIG=0
BOOTSTRAPPED_WEB_CONFIG=0
BOOTSTRAPPED_AGENT_CONFIG=0
BOOTSTRAPPED_CCR_CONFIG=0

if [ ! -f "$SERVER_CONFIG_PATH" ] && [ -f "$SERVER_TEMPLATE_ROOT/server_config.json" ]; then
    copy_if_missing "$SERVER_TEMPLATE_ROOT/server_config.json" "$SERVER_CONFIG_PATH"
    BOOTSTRAPPED_SERVER_CONFIG=1
fi

if [ ! -f "$WEB_CONFIG_PATH" ] && [ -f "$SERVER_TEMPLATE_ROOT/web_config.json" ]; then
    copy_if_missing "$SERVER_TEMPLATE_ROOT/web_config.json" "$WEB_CONFIG_PATH"
    BOOTSTRAPPED_WEB_CONFIG=1
fi

if [ ! -f "$CCR_CONFIG_PATH" ] && [ -f "$SERVER_TEMPLATE_ROOT/ccr_config.json" ]; then
    copy_if_missing "$SERVER_TEMPLATE_ROOT/ccr_config.json" "$CCR_CONFIG_PATH"
    BOOTSTRAPPED_CCR_CONFIG=1
fi

if [ ! -f "$AGENT_CONFIG_PATH" ] && [ -f "$AGENT_TEMPLATE_ROOT/config.json" ]; then
    copy_if_missing "$AGENT_TEMPLATE_ROOT/config.json" "$AGENT_CONFIG_PATH"
    BOOTSTRAPPED_AGENT_CONFIG=1
fi

copy_if_missing "$AGENT_TEMPLATE_ROOT/BIMCANVAS.md" "$CONFIG_ROOT/BIMCANVAS.md"
copy_if_missing "$AGENT_TEMPLATE_ROOT/agents" "$CONFIG_ROOT/agents"
copy_if_missing "$AGENT_TEMPLATE_ROOT/skills" "$CONFIG_ROOT/skills"
copy_if_missing "$AGENT_TEMPLATE_ROOT/.claude-plugin" "$CONFIG_ROOT/.claude-plugin"

export BOOTSTRAPPED_SERVER_CONFIG
export BOOTSTRAPPED_WEB_CONFIG
export BOOTSTRAPPED_AGENT_CONFIG
export BOOTSTRAPPED_CCR_CONFIG

python3 <<'PY'
import json
import os
from pathlib import Path


def load_json(path: Path) -> dict:
    with path.open("r", encoding="utf-8-sig") as handle:
        return json.load(handle)


def save_json(path: Path, data: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as handle:
        json.dump(data, handle, indent=2, ensure_ascii=False)
        handle.write("\n")


def parse_optional_bool(raw: str | None) -> bool | None:
    if raw is None:
        return None
    value = raw.strip().lower()
    if not value:
        return None
    return value in {"1", "true", "yes", "on"}


home = Path(os.environ["BIMCANVAS_HOME"])
server_config_path = home / "server_config.json"
agent_config_path = home / "config.json"
ccr_user_config_path = home / "ccr_config.json"
ccr_template_path = Path("/app/BIMCanvas.Server/Templates/global-config/server/ccr_config.json")

server_config = load_json(server_config_path)
server_section = server_config.setdefault("server", {})
startup_section = server_config.setdefault("startup", {})
ccr_section = server_config.setdefault("ccr", {})

bootstrapped_server = os.getenv("BOOTSTRAPPED_SERVER_CONFIG") == "1"
bootstrapped_agent = os.getenv("BOOTSTRAPPED_AGENT_CONFIG") == "1"
bootstrapped_ccr = os.getenv("BOOTSTRAPPED_CCR_CONFIG") == "1"

if bootstrapped_server:
    startup_section["openBrowser"] = False

    python_command = os.getenv("BIMCANVAS_PYTHON_COMMAND", "").strip()
    if python_command:
        server_section["pythonCommand"] = python_command

    ccr_enabled_override = parse_optional_bool(os.getenv("CCR_ENABLED"))
    if ccr_enabled_override is not None:
        ccr_section["enabled"] = ccr_enabled_override
        if ccr_enabled_override:
            ccr_section["autoStart"] = True

    ccr_model_family = os.getenv("CCR_MODEL_FAMILY", "").strip()
    if ccr_model_family:
        ccr_section["defaultModelFamily"] = ccr_model_family

ccr_enabled = bool(ccr_section.get("enabled", False))

agent_config = load_json(agent_config_path)
if bootstrapped_agent and not ccr_enabled:
    anthropic_api_key = os.getenv("ANTHROPIC_API_KEY", "").strip()
    anthropic_base_url = os.getenv("ANTHROPIC_BASE_URL", "").strip()
    anthropic_model = os.getenv("ANTHROPIC_MODEL", "").strip()

    if anthropic_api_key:
        agent_config["apiKey"] = anthropic_api_key

    if anthropic_base_url:
        agent_config["baseUrl"] = anthropic_base_url
    elif anthropic_api_key and os.getenv("BOOTSTRAPPED_AGENT_CONFIG") == "1":
        agent_config["baseUrl"] = ""

    if anthropic_model:
        agent_config["model"] = anthropic_model

if bootstrapped_agent:
    save_json(agent_config_path, agent_config)

if ccr_enabled:
    ccr_source_path = ccr_user_config_path if ccr_user_config_path.exists() else ccr_template_path
    if not ccr_source_path.exists():
        raise FileNotFoundError(f"CCR config template not found: {ccr_source_path}")

    ccr_runtime = load_json(ccr_source_path)
    if bootstrapped_ccr:
        providers = ccr_runtime.get("Providers") or []
        ccr_api_key = os.getenv("CCR_API_KEY", "").strip()
        ccr_api_base = os.getenv("CCR_API_BASE", "").strip()
        ccr_provider_name = os.getenv("CCR_PROVIDER_NAME", "").strip()
        target_providers = []

        if ccr_api_key or ccr_api_base:
            if ccr_provider_name:
                target_providers = [
                    provider for provider in providers if provider.get("name") == ccr_provider_name
                ]
                if not target_providers:
                    print(
                        f"[start.sh] CCR_PROVIDER_NAME '{ccr_provider_name}' was not found; "
                        "skipping CCR_API_* overrides"
                    )
            elif len(providers) == 1:
                target_providers = providers
            else:
                print(
                    "[start.sh] Skipping CCR_API_* overrides because ccr_config.json contains "
                    "multiple providers; edit ccr_config.json directly or set CCR_PROVIDER_NAME"
                )

        for provider in target_providers:
            if ccr_api_key:
                provider["api_key"] = ccr_api_key
            if ccr_api_base:
                provider["api_base_url"] = ccr_api_base

        save_json(ccr_user_config_path, ccr_runtime)

if bootstrapped_server:
    save_json(server_config_path, server_config)
PY

log "Starting BIMCanvas Server in Production mode"
cd /app
exec dotnet /app/BIMCanvas.Server/bin/Release/net8.0/BIMCanvas.Server.dll
