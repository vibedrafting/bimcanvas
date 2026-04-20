"""Configuration settings for BIMCanvas Agent"""

import logging
import os
from pathlib import Path
from dataclasses import dataclass, field
from functools import lru_cache
from urllib.parse import urlparse
from dotenv import load_dotenv

from .loader import get_config_loader
from ..runtime.providers import DEFAULT_RUNTIME_PROVIDER, OPENAI_RUNTIME_ID, normalize_runtime_provider

logger = logging.getLogger(__name__)
_CLAUDE_MODEL_ALIASES = frozenset({"opus", "sonnet", "haiku"})

# Load environment variables from .env file
load_dotenv()


@dataclass
class Settings:
    """
    Application settings

    配置来源：
    - 直连模式：base_url / api_key 来自 config.json，
      模型映射通过 modelMapping 设置 ANTHROPIC_DEFAULT_*_MODEL 环境变量
    - CCR 模式：base_url / api_key 来自 Server 注入的网关环境变量

    环境变量说明（与 Claude Code 隔离）：
    - AGENT_SDK_API_KEY: Agent SDK 专用 API Key
    - AGENT_SDK_BASE_URL: Agent SDK 专用 Base URL
    """

    runtime_provider: str
    anthropic_api_key: str
    openai_api_key: str
    base_url: str
    openai_api: str
    openai_disable_tracing: bool
    default_effort: str              # "low"/"medium"/"high"/"max", 默认 "medium"
    default_thinking: str            # "off"/"adaptive", 默认 "off"
    max_thinking_tokens: int | None  # thinking token 预算上限，None/-1/空 = 不限制
    tools: list[str]
    server_host: str
    server_port: int
    default_project_path: str
    model_mapping: dict = field(default_factory=dict)   # {"opus": {"id": "...", "label": "..."}, ...}

    @classmethod
    def load(cls) -> "Settings":
        """从配置文件加载运行时设置。"""
        loader = get_config_loader()
        config = loader.load_config()

        # 从配置文件读取
        direct_api_key = config.get('apiKey', '')
        direct_base_url = config.get('baseUrl', '')
        runtime_provider = normalize_runtime_provider(
            os.getenv('AGENT_RUNTIME_PROVIDER') or config.get('runtimeProvider') or DEFAULT_RUNTIME_PROVIDER
        )
        openai_api = "chat_completions"
        openai_disable_tracing = False
        default_effort = config.get('defaultEffort', 'medium')
        default_thinking = config.get('defaultThinking', 'off')
        raw_thinking_tokens = config.get('maxThinkingTokens', None)
        max_thinking_tokens = None if raw_thinking_tokens in (None, '', -1) else int(raw_thinking_tokens)
        tools = config.get('tools', ['Read', 'Glob', 'Grep', 'Task'])
        host = '127.0.0.1'
        port = 8865
        ccr_managed = runtime_provider != OPENAI_RUNTIME_ID and _is_ccr_managed_mode()

        # 模型映射（两种模式都加载，用于 /api/config 返回下拉菜单）
        model_mapping = config.get('modelMapping', {})

        if runtime_provider == OPENAI_RUNTIME_ID:
            api_key = os.getenv('OPENAI_API_KEY', '').strip() or direct_api_key
            base_url = os.getenv('OPENAI_BASE_URL', '').strip() or direct_base_url
            if not api_key:
                raise ValueError("OpenAI runtime requires OPENAI_API_KEY or config.json apiKey")
            _validate_openai_model_configuration(loader.config_dir, model_mapping)
            openai_api = _resolve_openai_api_mode(
                os.getenv("OPENAI_API_MODE", "").strip() or config.get("openaiApi"),
                base_url,
            )
            openai_disable_tracing = _resolve_openai_disable_tracing(
                os.getenv("OPENAI_TRACING_DISABLED", "").strip() or config.get("openaiDisableTracing"),
                base_url,
            )
            logger.info(
                "使用 OpenAI Agents Runtime (api=%s, tracing=%s)",
                openai_api,
                "disabled" if openai_disable_tracing else "enabled",
            )
        elif ccr_managed:
            api_key = os.getenv('AGENT_SDK_API_KEY', '').strip()
            base_url = os.getenv('AGENT_SDK_BASE_URL', '').strip()

            missing_vars = []
            if not base_url:
                missing_vars.append('AGENT_SDK_BASE_URL')
            if not api_key:
                missing_vars.append('AGENT_SDK_API_KEY')

            if missing_vars:
                missing_vars_display = ', '.join(missing_vars)
                raise ValueError(
                    "检测到 CCR 托管环境变量，但缺少必需项: "
                    f"{missing_vars_display}"
                )

            logger.info("使用 CCR 网关连接 Agent SDK")
        else:
            api_key = direct_api_key
            base_url = direct_base_url

            # 直连模式：从 modelMapping 设置 Claude Code CLI 模型映射环境变量
            _apply_model_mapping(model_mapping)

        env_thinking_tokens = os.getenv('MAX_THINKING_TOKENS')
        if env_thinking_tokens is not None:
            max_thinking_tokens = None if env_thinking_tokens in ('', '-1') else int(env_thinking_tokens)
        host = os.getenv('SERVER_HOST', host)
        port = int(os.getenv('SERVER_PORT', str(port)))
        project_path = os.getenv('DEFAULT_PROJECT_PATH', '')

        return cls(
            runtime_provider=runtime_provider,
            anthropic_api_key=api_key,
            openai_api_key=api_key,
            base_url=base_url,
            openai_api=openai_api,
            openai_disable_tracing=openai_disable_tracing,
            max_thinking_tokens=max_thinking_tokens,
            default_effort=default_effort,
            default_thinking=default_thinking,
            tools=tools,
            server_host=host,
            server_port=port,
            default_project_path=project_path,
            model_mapping=model_mapping,
        )


@lru_cache()
def get_settings() -> Settings:
    """Get cached settings instance"""
    return Settings.load()


def _is_ccr_managed_mode() -> bool:
    """通过 Server 注入的网关环境变量判断是否处于 CCR 托管模式。"""
    api_key = os.getenv('AGENT_SDK_API_KEY', '').strip()
    base_url = os.getenv('AGENT_SDK_BASE_URL', '').strip()
    return bool(api_key or base_url)


def _apply_model_mapping(model_mapping: dict) -> None:
    """直连模式下，将 config.json 的 modelMapping 转换为 Claude Code CLI 环境变量。"""
    family_env_map = {
        'opus':   'ANTHROPIC_DEFAULT_OPUS_MODEL',
        'sonnet': 'ANTHROPIC_DEFAULT_SONNET_MODEL',
        'haiku':  'ANTHROPIC_DEFAULT_HAIKU_MODEL',
    }
    for family, env_name in family_env_map.items():
        entry = model_mapping.get(family, {})
        model_id = entry.get('id') if isinstance(entry, dict) else entry
        if model_id:
            os.environ[env_name] = model_id
            logger.info(f"模型映射: {family} → {model_id}")


def _is_official_openai_base_url(base_url: str | None) -> bool:
    normalized = (base_url or "").strip()
    if not normalized:
        return True

    parsed = urlparse(normalized)
    host = (parsed.netloc or parsed.path).strip().lower()
    if host.endswith("/v1"):
        host = host[:-3]
    return host in {"api.openai.com", "api.openai.com:443"}


def _parse_optional_bool(value: object) -> bool | None:
    if value is None or value == "":
        return None
    if isinstance(value, bool):
        return value

    normalized = str(value).strip().lower()
    if normalized in {"1", "true", "yes", "on"}:
        return True
    if normalized in {"0", "false", "no", "off"}:
        return False
    raise ValueError(
        "OpenAI runtime openaiDisableTracing/OPENAI_TRACING_DISABLED must be a boolean value."
    )


def _resolve_openai_api_mode(raw_value: object, base_url: str) -> str:
    normalized = str(raw_value or "").strip().lower().replace("-", "_")
    if not normalized:
        return "chat_completions"
    if normalized not in {"responses", "chat_completions"}:
        raise ValueError(
            "OpenAI runtime openaiApi/OPENAI_API_MODE must be 'responses' or 'chat_completions'."
        )
    if normalized == "responses" and not _is_official_openai_base_url(base_url):
        raise ValueError(
            "OpenAI runtime 'responses' API is not supported with third-party OpenAI-compatible endpoints "
            "in BIMCanvas v0.1. Please set openaiApi='chat_completions' (default), or point baseUrl back to "
            "the official OpenAI endpoint if you intend to opt into the experimental 'responses' path."
        )
    return normalized


def _resolve_openai_disable_tracing(raw_value: object, base_url: str) -> bool:
    parsed = _parse_optional_bool(raw_value)
    if parsed is not None:
        return parsed
    return not _is_official_openai_base_url(base_url)


def _validate_openai_model_configuration(config_dir: Path, model_mapping: dict) -> None:
    for model_key, entry in model_mapping.items():
        normalized_key = str(model_key).strip()
        if normalized_key.lower() in _CLAUDE_MODEL_ALIASES:
            raise ValueError(
                "OpenAI runtime requires config.json modelMapping keys to be real OpenAI model ids; "
                f"found Claude alias '{normalized_key}'."
            )

        configured_id = ""
        if isinstance(entry, dict):
            configured_id = str(entry.get("id", "")).strip()
        elif isinstance(entry, str):
            configured_id = entry.strip()

        if configured_id and configured_id != normalized_key:
            raise ValueError(
                "OpenAI runtime requires config.json modelMapping key and id to match the real model id; "
                f"found key '{normalized_key}' with id '{configured_id}'."
            )

    web_config_path = config_dir / "web_config.json"
    if not web_config_path.exists():
        raise ValueError(
            f"OpenAI runtime requires <BIMCANVAS_HOME>/web_config.json, but it was not found: {web_config_path}"
        )

    import json

    with web_config_path.open("r", encoding="utf-8-sig") as handle:
        web_config = json.load(handle)

    default_model = str(web_config.get("defaultModel", "")).strip()
    if not default_model:
        raise ValueError("OpenAI runtime requires web_config.json defaultModel to be set to a real OpenAI model id.")
    if default_model.lower() in _CLAUDE_MODEL_ALIASES:
        raise ValueError(
            "OpenAI runtime does not accept Claude aliases in web_config.json defaultModel; "
            f"found '{default_model}'."
        )
