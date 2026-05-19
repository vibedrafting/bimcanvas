"""BIMCanvas Plugin SDK 异常类。"""

from __future__ import annotations


class LaunchContextError(Exception):
    """PluginLaunchContext 注入文件解析失败或字段不合法。"""


class PluginManifestError(Exception):
    """plugin 的 bimcanvas-plugin.json / .claude-plugin/plugin.json 不合法。

    抛出场景:
    - manifest 必填字段缺失
    - mcpTools 路径含 `..` 逃逸
    - mcpNamespace = "canvas" (保留给 core-base)
    """


class PluginRegisterError(Exception):
    """plugin 的 register(builder) 执行失败或 namespace 冲突。"""
