"""Factory helpers for creating host-facing agent adapters."""

from __future__ import annotations

import aiohttp

from .main_agent import MainAgent
from .openai_agent import OpenAIAgent
from .protocol import HostAgentProtocol
from ..runtime import (
    CLAUDE_RUNTIME_ID,
    OPENAI_RUNTIME_ID,
    build_config_bundle,
    normalize_runtime_provider,
)
from ..runtime.launch_context import (
    build_project_bound_context,
    resolve_launch_context,
)


def create_agent(
    runtime_provider: str,
    *,
    project_path: str,
    working_directory: str | None,
    window_seq: int,
) -> HostAgentProtocol:
    normalized = normalize_runtime_provider(runtime_provider)

    # v3.4 修复:创建 long-lived aiohttp session 供 PluginContext 使用 (D1 工具走 ctx.session)。
    # 旧 canvas.py 工具自己 ClientSession,本来不需要外部 session;v3.4 改成 ctx.session 后必须
    # 平台在 build_config_bundle 时显式注入,否则 PluginContext.session=None,工具调 ctx.session.post
    # 会抛 'NoneType' object has no attribute 'post'。
    # session 生命周期挂到 agent._owned_session,agent.disconnect() 时关闭。
    # 接线总开关:用请求参数 project_path 构造 ProjectBound launch_context 传入,杜绝
    # build_config_bundle 内部无参 resolve 得 projectless(project_path=None)。
    # project_path 为空(纯 projectless 聊天)时维持现状,不强转 ProjectBound。
    launch_context = (
        build_project_bound_context(project_path)
        if project_path
        else resolve_launch_context()
    )

    owned_session = aiohttp.ClientSession()
    bundle = build_config_bundle(launch_context=launch_context, session=owned_session)

    if normalized == OPENAI_RUNTIME_ID:
        agent = OpenAIAgent(
            project_path=project_path,
            working_directory=working_directory,
            window_seq=window_seq,
        )
    elif normalized == CLAUDE_RUNTIME_ID:
        agent = MainAgent(
            project_path=project_path,
            working_directory=working_directory,
            window_seq=window_seq,
        )
    else:
        raise ValueError(f"Unsupported runtime provider: {runtime_provider}")

    # 把 session 所有权移交给 agent,disconnect 时由 agent 负责 close。
    # MainAgent 已有 _owned_session 字段及对应关闭逻辑;OpenAIAgent 在 v3.4 修复中补齐。
    agent._owned_session = owned_session
    agent.configure(bundle)

    # 启动自检(叉口①):传入了 project_path 却没绑成 ProjectBound(最终 bundle 仍为空)
    # → fail-fast,杜绝静默 None 跑完整 session(MCP 采纳/注册/边界工具永久不可用)。
    if project_path and not bundle.launch_context.project_path:
        raise RuntimeError(
            f"create_agent 自检失败:project_path={project_path!r} 已传入但 "
            f"bundle.launch_context.project_path 仍为空,ProjectBound 绑定失败,fail-fast。"
        )
    return agent
