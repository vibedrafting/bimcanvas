"""PluginContext - plugin 作者可见的窄接口 (主真理源 v1.1 §3.3,「项目去插件态」后精简)。

字段集 (5 个):
- server_url:Server REST 基址,plugin 调用 ctx.session.post(f"{ctx.server_url}/...")
- project_path:.bcp 项目目录路径;Projectless 时为 None
- active_plugin_id:当前 active plugin 的 manifest name 字段
- logger:平台注入,name=f"bimcanvas.plugin.{active_plugin_id}",plugin 直接 ctx.logger.info(...)
- session:long-lived aiohttp.ClientSession,平台在 _build_mcp_servers 时创建,
  Agent shutdown 时 close;plugin 内每次工具调用复用此 session

北极星(项目 = 被动数据基底,不记录哪个插件执行过):**不再暴露 active_scene / scenes**。
业务数据按物理 zone 组织落 schemes/{zoneId}/...;"当前激活哪个 plugin" 是运行时事实,
plugin 通过系统提示词的 `## Active Plugin` 段感知,不需运行态查询。

不暴露 (平台内部状态):
- LaunchMode / TrustMode (用户 trust 决策不应渗到 plugin 代码)
- PluginLockSummary (lock 是 Server 侧元数据)

get_config():
- 异步方法,返回插件配置 dict(从 Server GET /api/plugins/{id}/config/values 拉取)
- 带 30s TTL 缓存,避免每次 tool call 都发 HTTP 请求
- 配置值在 Server 侧修改后,最多 30s 内生效,无需重启任何进程
"""

from __future__ import annotations

import asyncio
import logging
import time
from dataclasses import dataclass, field
from typing import TYPE_CHECKING, Any, Optional

if TYPE_CHECKING:
    import aiohttp

_CONFIG_CACHE_TTL_SECONDS = 30


@dataclass
class _ConfigCache:
    """插件配置的 TTL 缓存，避免每次 tool call 都发 HTTP。"""
    _lock: asyncio.Lock = field(default_factory=asyncio.Lock, repr=False)
    _values: dict[str, Any] = field(default_factory=dict, repr=False)
    _fetched_at: float = field(default=0.0, repr=False)

    def is_fresh(self) -> bool:
        return time.monotonic() - self._fetched_at < _CONFIG_CACHE_TTL_SECONDS

    def update(self, values: dict[str, Any]) -> None:
        self._values = dict(values)
        self._fetched_at = time.monotonic()

    def get(self) -> dict[str, Any]:
        return dict(self._values)


@dataclass
class PluginContext:
    """Plugin 作者可见的运行时上下文。

    平台在 `_build_mcp_servers` 中实例化后通过 McpServerBuilder.context 暴露;
    plugin 作者在 register() 函数体闭包捕获:

        def register(builder):
            ctx = builder.context
            @builder.tool(...)
            async def my_tool(args):
                config = await ctx.get_config()
                api_key = config.get("MY_API_KEY", "")
    """

    server_url: str
    project_path: Optional[str]
    active_plugin_id: str
    logger: logging.Logger
    session: "aiohttp.ClientSession"
    _config_cache: _ConfigCache = field(default_factory=_ConfigCache, repr=False)

    async def get_config(self) -> dict[str, Any]:
        """返回插件配置 dict，带 30s TTL 缓存，改完配置无需重启即可生效。

        失败时返回缓存值（或空 dict），不抛出异常，不阻断 tool 执行。
        session 为 None 时自动创建临时 session（供 startup 阶段注册的 web_action 使用）。
        """
        async with self._config_cache._lock:
            if self._config_cache.is_fresh():
                return self._config_cache.get()

            try:
                import aiohttp as _aiohttp
                url = f"{self.server_url}/api/plugins/{self.active_plugin_id}/config/values"

                async def _fetch(sess: "_aiohttp.ClientSession") -> None:
                    async with sess.get(url) as resp:
                        if resp.status == 200:
                            data = await resp.json(content_type=None)
                            if isinstance(data, dict):
                                self._config_cache.update(data)
                            else:
                                self._config_cache.update({})
                        else:
                            self.logger.warning(
                                "get_config: HTTP %s from %s，使用缓存值", resp.status, url
                            )

                if self.session is not None:
                    await _fetch(self.session)
                else:
                    # startup 阶段 session=None，临时创建
                    async with _aiohttp.ClientSession() as tmp_session:
                        await _fetch(tmp_session)

            except Exception as exc:  # noqa: BLE001
                self.logger.warning("get_config 失败: %s，使用缓存值", exc)

            return self._config_cache.get()
