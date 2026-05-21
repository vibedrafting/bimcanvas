"""PluginContext - plugin 作者可见的窄接口 (主真理源 v1.1 §3.3 末段 / 组3 计划 §3.3)。

字段集 (7 个,v3.4 D10 加 scenes):
- server_url:Server REST 基址,plugin 调用 ctx.session.post(f"{ctx.server_url}/...")
- project_path:.bcp 项目目录路径;Projectless 时为 None
- active_plugin_id:当前 active plugin 的 manifest name 字段
- active_scene:运行时 active plugin 标识(= active plugin id,如 "interior-layout");永远有值(含 Projectless)。
  仅作运行时标识,**不进数据路径**;业务数据按物理 zone 组织落 schemes/{zoneId}/...
- logger:平台注入,name=f"bimcanvas.plugin.{active_plugin_id}",plugin 直接 ctx.logger.info(...)
- session:long-lived aiohttp.ClientSession,平台在 _build_mcp_servers 时创建,
  Agent shutdown 时 close;plugin 内每次工具调用复用此 session
- scenes:当前项目 scenes 快照 (v3.4 D10 加入);Projectless 时为 None。
  core-base 的 list_project_scenes 工具需要枚举 scenes,其他 plugin 也可用于跨 scene 操作。
  数据由 PluginLaunchContext.scenes 透传,真理源是 project.json。

不暴露 (平台内部状态):
- LaunchMode / TrustMode (用户 trust 决策不应渗到 plugin 代码)
- PluginLockSummary (lock 是 Server 侧元数据)
"""

from __future__ import annotations

import logging
from dataclasses import dataclass
from typing import TYPE_CHECKING, Optional

if TYPE_CHECKING:
    import aiohttp

    from src.runtime.launch_context import ProjectScenesSummary


@dataclass(frozen=True)
class PluginContext:
    """Plugin 作者可见的运行时上下文。

    平台在 `_build_mcp_servers` 中实例化后通过 McpServerBuilder.context 暴露;
    plugin 作者在 register() 函数体闭包捕获:

        def register(builder):
            ctx = builder.context
            @builder.tool(...)
            async def my_tool(args):
                async with ctx.session.get(f"{ctx.server_url}/api/...") as r:
                    ...
    """

    server_url: str
    project_path: Optional[str]
    active_plugin_id: str
    active_scene: str
    logger: logging.Logger
    session: "aiohttp.ClientSession"
    scenes: "Optional[ProjectScenesSummary]" = None
