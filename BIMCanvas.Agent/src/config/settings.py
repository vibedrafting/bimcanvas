"""Configuration settings for BIMCanvas Agent."""

from __future__ import annotations

import logging
import os
from dataclasses import dataclass, field
from functools import lru_cache
from urllib.parse import urlparse

from dotenv import load_dotenv

from .loader import get_config_loader, get_provider_config, resolve_runtime_provider
from ..runtime.providers import CLAUDE_RUNTIME_ID, OPENAI_RUNTIME_ID

logger = logging.getLogger(__name__)

_CLAUDE_MODEL_ALIASES = ("opus", "sonnet", "haiku")
_CLAUDE_MODEL_ALIAS_SET = frozenset(_CLAUDE_MODEL_ALIASES)
_DEFAULT_CLAUDE_EFFORT = "low"
_DEFAULT_CLAUDE_THINKING = "adaptive"
_DEFAULT_CLAUDE_MAX_THINKING_TOKENS = 8000

# Load environment variables from .env file.
load_dotenv()


@dataclass
class Settings:
    """
    Application settings.

    配置来源：
    - Claude 直连：来自 config.json claude.*
    - Claude CCR 托管：base_url / api_key 来自 Server 注入的网关环境变量
    - OpenAI 直连：来自 config.json openai.*，可被 OPENAI_* 环境变量覆盖
    """

    runtime_provider: str
    anthropic_api_key: str
    openai_api_key: str
    base_url: str
    openai_api: str
    openai_disable_tracing: bool
    default_effort: str | None
    default_thinking: str | None
    max_thinking_tokens: int | None
    server_host: str
    server_port: int
    default_project_path: str
    default_model: str
    model_mapping: dict[str, dict[str, str]] = field(default_factory=dict)

    @classmethod
    def load(cls) -> "Settings":
        """从配置文件加载运行时设置。"""
        loader = get_config_loader()
        config = loader.load_config()
        runtime_provider = resolve_runtime_provider(config)

        claude_config = get_provider_config(config, CLAUDE_RUNTIME_ID)
        openai_config = get_provider_config(config, OPENAI_RUNTIME_ID)

        server_host = os.getenv("SERVER_HOST", "127.0.0.1")
        server_port = int(os.getenv("SERVER_PORT", "8865"))
        project_path = os.getenv("DEFAULT_PROJECT_PATH", "")

        if runtime_provider == OPENAI_RUNTIME_ID:
            settings_payload = _load_openai_settings(openai_config)
        else:
            settings_payload = _load_claude_settings(claude_config)

        return cls(
            runtime_provider=runtime_provider,
            anthropic_api_key=settings_payload["anthropic_api_key"],
            openai_api_key=settings_payload["openai_api_key"],
            base_url=settings_payload["base_url"],
            openai_api=settings_payload["openai_api"],
            openai_disable_tracing=settings_payload["openai_disable_tracing"],
            default_effort=settings_payload["default_effort"],
            default_thinking=settings_payload["default_thinking"],
            max_thinking_tokens=settings_payload["max_thinking_tokens"],
            server_host=server_host,
            server_port=server_port,
            default_project_path=project_path,
            default_model=settings_payload["default_model"],
            model_mapping=settings_payload["model_mapping"],
        )


@lru_cache()
def get_settings() -> Settings:
    """Get cached settings instance."""
    return Settings.load()


def _load_claude_settings(claude_config: dict) -> dict[str, object]:
    direct_api_key = _read_string(claude_config.get("apiKey"))
    direct_base_url = _read_string(claude_config.get("baseUrl"))
    model_mapping = _sanitize_claude_model_mapping(claude_config.get("modelMapping"))
    default_model = _resolve_claude_default_model(
        claude_config.get("defaultModel"),
        model_mapping,
    )
    default_effort = _resolve_claude_effort(claude_config.get("defaultEffort"))
    default_thinking = _resolve_claude_thinking(claude_config.get("defaultThinking"))
    max_thinking_tokens = _resolve_optional_int(
        claude_config.get("maxThinkingTokens"),
        source="config.json claude.maxThinkingTokens",
        default=_DEFAULT_CLAUDE_MAX_THINKING_TOKENS,
    )

    if _is_ccr_managed_mode():
        api_key = os.getenv("AGENT_SDK_API_KEY", "").strip()
        base_url = os.getenv("AGENT_SDK_BASE_URL", "").strip()

        missing_vars = []
        if not base_url:
            missing_vars.append("AGENT_SDK_BASE_URL")
        if not api_key:
            missing_vars.append("AGENT_SDK_API_KEY")
        if missing_vars:
            raise ValueError(
                "检测到 CCR 托管环境变量，但缺少必需项: "
                f"{', '.join(missing_vars)}"
            )

        logger.info("使用 CCR 网关连接 Claude Runtime")
    else:
        api_key = direct_api_key
        base_url = direct_base_url
        _apply_model_mapping(model_mapping)

    env_thinking_tokens = os.getenv("MAX_THINKING_TOKENS")
    if env_thinking_tokens is not None:
        max_thinking_tokens = _resolve_optional_int(
            env_thinking_tokens,
            source="MAX_THINKING_TOKENS",
            default=max_thinking_tokens,
        )

    return {
        "anthropic_api_key": api_key,
        "openai_api_key": "",
        "base_url": base_url,
        "openai_api": "chat_completions",
        "openai_disable_tracing": False,
        "default_effort": default_effort,
        "default_thinking": default_thinking,
        "max_thinking_tokens": max_thinking_tokens,
        "default_model": default_model,
        "model_mapping": model_mapping,
    }


def _load_openai_settings(openai_config: dict) -> dict[str, object]:
    direct_api_key = _read_string(openai_config.get("apiKey"))
    direct_base_url = _read_string(openai_config.get("baseUrl"))
    api_key = os.getenv("OPENAI_API_KEY", "").strip() or direct_api_key
    base_url = os.getenv("OPENAI_BASE_URL", "").strip() or direct_base_url

    if not api_key:
        raise ValueError(
            "OpenAI runtime requires OPENAI_API_KEY or config.json openai.apiKey."
        )

    model_mapping = _sanitize_openai_model_mapping(
        openai_config.get("modelMapping"),
    )
    default_model = _resolve_openai_default_model(
        openai_config.get("defaultModel"),
        model_mapping,
    )
    openai_api = _resolve_openai_api_mode(
        os.getenv("OPENAI_API_MODE", "").strip() or openai_config.get("apiMode"),
        base_url,
    )
    openai_disable_tracing = _resolve_openai_disable_tracing(
        os.getenv("OPENAI_TRACING_DISABLED", "").strip() or openai_config.get("disableTracing"),
        base_url,
    )

    logger.info(
        "使用 OpenAI Runtime (api=%s, tracing=%s)",
        openai_api,
        "disabled" if openai_disable_tracing else "enabled",
    )

    return {
        "anthropic_api_key": "",
        "openai_api_key": api_key,
        "base_url": base_url,
        "openai_api": openai_api,
        "openai_disable_tracing": openai_disable_tracing,
        "default_effort": None,
        "default_thinking": None,
        "max_thinking_tokens": None,
        "default_model": default_model,
        "model_mapping": model_mapping,
    }


def _is_ccr_managed_mode() -> bool:
    """通过 Server 注入的网关环境变量判断是否处于 CCR 托管模式。"""
    api_key = os.getenv("AGENT_SDK_API_KEY", "").strip()
    base_url = os.getenv("AGENT_SDK_BASE_URL", "").strip()
    return bool(api_key or base_url)


def _read_string(value: object) -> str:
    if value is None:
        return ""
    return str(value).strip()


def _apply_model_mapping(model_mapping: dict[str, dict[str, str]]) -> None:
    """直连模式下，将 claude.modelMapping 转换为 Claude Code CLI 环境变量。"""
    family_env_map = {
        "opus": "ANTHROPIC_DEFAULT_OPUS_MODEL",
        "sonnet": "ANTHROPIC_DEFAULT_SONNET_MODEL",
        "haiku": "ANTHROPIC_DEFAULT_HAIKU_MODEL",
    }
    for family, env_name in family_env_map.items():
        model_id = _read_string(model_mapping.get(family, {}).get("id"))
        if model_id:
            os.environ[env_name] = model_id
            logger.info("模型映射: %s -> %s", family, model_id)


def _resolve_claude_effort(raw_value: object) -> str:
    normalized = _read_string(raw_value).lower() or _DEFAULT_CLAUDE_EFFORT
    if normalized not in {"low", "medium", "high", "max"}:
        raise ValueError(
            "config.json claude.defaultEffort 必须是 low / medium / high / max。"
        )
    return normalized


def _resolve_claude_default_model(
    raw_value: object,
    model_mapping: dict[str, dict[str, str]],
) -> str:
    normalized = _read_string(raw_value).lower() or "opus"
    if normalized not in _CLAUDE_MODEL_ALIAS_SET:
        raise ValueError(
            "config.json claude.defaultModel 必须是 opus / sonnet / haiku。"
        )
    if normalized not in model_mapping:
        raise ValueError(
            "config.json claude.defaultModel 必须存在于 claude.modelMapping 中；"
            f"检测到 '{normalized}'。"
        )
    return normalized


def _resolve_claude_thinking(raw_value: object) -> str:
    normalized = _read_string(raw_value).lower() or _DEFAULT_CLAUDE_THINKING
    if normalized not in {"off", "adaptive"}:
        raise ValueError(
            "config.json claude.defaultThinking 必须是 off / adaptive。"
        )
    return normalized


def _resolve_optional_int(
    raw_value: object,
    *,
    source: str,
    default: int | None,
) -> int | None:
    if raw_value in (None, ""):
        return default
    if raw_value == -1 or str(raw_value).strip() == "-1":
        return None
    try:
        return int(raw_value)
    except (TypeError, ValueError) as exc:
        raise ValueError(f"{source} 必须是整数、空值或 -1。") from exc


def _sanitize_claude_model_mapping(raw_value: object) -> dict[str, dict[str, str]]:
    raw_mapping = {} if raw_value in (None, "") else raw_value
    if not isinstance(raw_mapping, dict):
        raise ValueError("config.json claude.modelMapping 必须是对象。")

    unexpected = sorted(str(key) for key in raw_mapping.keys() if str(key).strip() not in _CLAUDE_MODEL_ALIAS_SET)
    if unexpected:
        raise ValueError(
            "config.json claude.modelMapping 只允许 opus / sonnet / haiku；"
            f"检测到非法 key: {', '.join(unexpected)}。"
        )

    mapping: dict[str, dict[str, str]] = {}
    for alias in _CLAUDE_MODEL_ALIASES:
        entry = raw_mapping.get(alias, {})
        if isinstance(entry, dict):
            model_id = _read_string(entry.get("id"))
            label = _read_string(entry.get("label")) or alias.capitalize()
        elif isinstance(entry, str):
            model_id = entry.strip()
            label = alias.capitalize()
        else:
            raise ValueError(f"config.json claude.modelMapping.{alias} 必须是对象或字符串。")

        mapping[alias] = {
            "id": model_id,
            "label": label,
        }

    return mapping


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
        "OpenAI runtime openai.disableTracing/OPENAI_TRACING_DISABLED 必须是布尔值或 null。"
    )


def _resolve_openai_api_mode(raw_value: object, base_url: str) -> str:
    normalized = _read_string(raw_value).lower().replace("-", "_") or "chat_completions"
    if normalized not in {"responses", "chat_completions"}:
        raise ValueError(
            "OpenAI runtime openai.apiMode/OPENAI_API_MODE 必须是 'responses' 或 'chat_completions'。"
        )
    if normalized == "responses" and not _is_official_openai_base_url(base_url):
        raise ValueError(
            "OpenAI runtime 仅在官方 OpenAI endpoint 下支持 openai.apiMode='responses'。"
            " 第三方 OpenAI-compatible endpoint 请使用 'chat_completions'。"
        )
    return normalized


def _resolve_openai_disable_tracing(raw_value: object, base_url: str) -> bool:
    parsed = _parse_optional_bool(raw_value)
    if parsed is not None:
        return parsed
    return not _is_official_openai_base_url(base_url)


def _sanitize_openai_model_mapping(
    raw_value: object,
) -> dict[str, dict[str, str]]:
    raw_mapping = {} if raw_value in (None, "") else raw_value
    if not isinstance(raw_mapping, dict):
        raise ValueError("config.json openai.modelMapping 必须是对象。")

    mapping: dict[str, dict[str, str]] = {}
    for model_key, entry in raw_mapping.items():
        normalized_key = _read_string(model_key)
        if not normalized_key:
            raise ValueError("config.json openai.modelMapping 不允许空 key。")
        if normalized_key.lower() in _CLAUDE_MODEL_ALIAS_SET:
            raise ValueError(
                "OpenAI runtime 要求 openai.modelMapping 的 key 必须是真实 model id；"
                f"检测到 Claude alias '{normalized_key}'。"
            )

        if isinstance(entry, dict):
            configured_id = _read_string(entry.get("id"))
            label = _read_string(entry.get("label")) or normalized_key
        elif isinstance(entry, str):
            configured_id = entry.strip()
            label = normalized_key
        else:
            raise ValueError(
                f"config.json openai.modelMapping.{normalized_key} 必须是对象或字符串。"
            )

        if configured_id and configured_id != normalized_key:
            raise ValueError(
                "OpenAI runtime 要求 openai.modelMapping 的 key 和 id 完全一致；"
                f"检测到 key='{normalized_key}', id='{configured_id}'。"
            )

        mapping[normalized_key] = {
            "id": normalized_key,
            "label": label,
        }

    return mapping


def _resolve_openai_default_model(
    raw_value: object,
    model_mapping: dict[str, dict[str, str]],
) -> str:
    default_model = _read_string(raw_value)
    if not default_model:
        raise ValueError(
            "OpenAI runtime requires config.json openai.defaultModel to be set to a real OpenAI model id."
        )
    if default_model.lower() in _CLAUDE_MODEL_ALIAS_SET:
        raise ValueError(
            "OpenAI runtime does not accept Claude aliases in config.json openai.defaultModel; "
            f"found '{default_model}'."
        )
    if default_model not in model_mapping:
        raise ValueError(
            "OpenAI runtime requires config.json openai.defaultModel to exist in "
            f"openai.modelMapping; found '{default_model}'."
        )
    return default_model
