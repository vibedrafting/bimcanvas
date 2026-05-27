"""平台 validator host：被 Server 的 PluginValidatorRuntime 以子进程方式拉起。

协议（包A · 2026-05-27 决议）：
- argv[1] = pluginRoot（active plugin 绝对路径）
- argv[2] = entry（plugin validators 入口 .py 绝对路径）
- stdin   = 请求 JSON：{mode, projectPath, zoneIds?, variantId?, ...}
- stdout  = 单行 JSON 信封：{ok: true, result: {...}} 或 {ok: false, error, type, traceback}

host 仅做 importlib 加载 + 调用 entry.run(request)；具体校验/规范化逻辑全在 plugin
脚本里（domain 代码）。PYTHONPATH 由 Server 设为 Agent 根，故 entry 可
`from bimcanvas_plugin_sdk import geometry`。
"""

from __future__ import annotations

import importlib.util
import json
import sys
import traceback


def _emit(payload: dict) -> None:
    sys.stdout.write(json.dumps(payload, ensure_ascii=False))
    sys.stdout.flush()


def _main() -> int:
    try:
        plugin_root = sys.argv[1]
        entry = sys.argv[2]
    except IndexError:
        _emit({"ok": False, "error": "缺少 argv: pluginRoot / entry"})
        return 1

    try:
        raw = sys.stdin.read()
        request = json.loads(raw) if raw and raw.strip() else {}
    except Exception as e:  # noqa: BLE001
        _emit({"ok": False, "error": f"请求 JSON 解析失败: {e}"})
        return 1

    try:
        # PYTHONPATH 已含 Agent 根（bimcanvas_plugin_sdk 可导入）；pluginRoot 入 path 供入口内相对导入
        if plugin_root not in sys.path:
            sys.path.insert(0, plugin_root)

        spec = importlib.util.spec_from_file_location("bimcanvas_validator_entry", entry)
        if spec is None or spec.loader is None:
            _emit({"ok": False, "error": f"spec_from_file_location 失败: {entry}"})
            return 1
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)

        run = getattr(module, "run", None)
        if run is None or not callable(run):
            _emit({"ok": False, "error": "validators 入口缺少 run(request) 函数"})
            return 1

        result = run(request)
        _emit({"ok": True, "result": result})
        return 0
    except BaseException as e:  # noqa: BLE001
        _emit({
            "ok": False,
            "error": str(e),
            "type": type(e).__name__,
            "traceback": traceback.format_exc(),
        })
        return 1


if __name__ == "__main__":
    sys.exit(_main())
