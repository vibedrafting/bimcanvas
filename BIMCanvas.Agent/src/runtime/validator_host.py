"""平台 validator host：被 Server 的 PluginValidatorRuntime 以子进程方式拉起。

协议（包A · 2026-05-27 决议）：
- argv[1] = pluginRoot（active plugin 绝对路径）
- argv[2] = entry（plugin validators 入口 .py 绝对路径）
- stdin   = 请求 JSON（UTF-8）：
    · 单次：{mode, projectPath, zoneIds?, variantId?, ...} → run(request)
    · 批量：{batch: [<单次请求>, ...]} → 模块只加载一次，逐 sub-request 调 run()
- stdout  = 单行 JSON 信封：
    · 单次：{ok: true, result: {...}} 或 {ok: false, error, type, traceback}
    · 批量：{ok: true, result: {batch: [{ok,result}|{ok:false,error,...}, ...]}}

host 仅做 importlib 加载 + 调用 entry.run(request)；具体校验/规范化逻辑全在 plugin
脚本里（domain 代码）。PYTHONPATH 由 Server 设为 Agent 根，故 entry 可
`from bimcanvas_plugin_sdk import geometry`。

批量动机：normalize/validate 端点过去每个 scope（canonical + 各变体）起一个 python
子进程，冷启动 + shapely 导入是耗时大头。批量让"加载一次插件、循环跑 N 个 scope"，
把 N 次冷启动收为 1 次；插件 run() 契约不变（仍 per-scope）。

硬化（指挥部 review）：加载 + run 期间把 sys.stdout 重定向到缓冲区，信封只在末尾经
**原始 stdout 干净通道**发出——防插件脚本/其依赖的 print 污染 JSON 信封导致 Server 解析失败。
"""

from __future__ import annotations

import contextlib
import importlib.util
import io
import json
import sys
import traceback


def _emit(stream, payload: dict) -> None:
    stream.write(json.dumps(payload, ensure_ascii=False))
    stream.flush()


def _main() -> int:
    real_stdout = sys.stdout  # 信封专用干净通道（在任何重定向之前抓住）

    try:
        plugin_root = sys.argv[1]
        entry = sys.argv[2]
    except IndexError:
        _emit(real_stdout, {"ok": False, "error": "缺少 argv: pluginRoot / entry"})
        return 1

    try:
        raw = sys.stdin.read()
        if raw:
            raw = raw.lstrip("﻿")  # 容错：剥离可能的 BOM，防 json.loads 在前导 BOM 上失败
        request = json.loads(raw) if raw and raw.strip() else {}
    except Exception as e:  # noqa: BLE001
        _emit(real_stdout, {"ok": False, "error": f"请求 JSON 解析失败: {e}"})
        return 1

    captured = io.StringIO()
    try:
        # 加载 + run 期间隔离 stdout：插件脚本的任何 print 落到 captured，不污染信封通道
        with contextlib.redirect_stdout(captured):
            # PYTHONPATH 已含 Agent 根（bimcanvas_plugin_sdk 可导入）；pluginRoot 入 path 供入口内相对导入
            if plugin_root not in sys.path:
                sys.path.insert(0, plugin_root)

            spec = importlib.util.spec_from_file_location("bimcanvas_validator_entry", entry)
            if spec is None or spec.loader is None:
                _emit(real_stdout, {"ok": False, "error": f"spec_from_file_location 失败: {entry}"})
                return 1
            module = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(module)  # 插件 + shapely 导入只发生这一次（批量复用之）

            run = getattr(module, "run", None)
            if run is None or not callable(run):
                _emit(real_stdout, {"ok": False, "error": "validators 入口缺少 run(request) 函数"})
                return 1

            batch = request.get("batch") if isinstance(request, dict) else None
            if isinstance(batch, list):
                # 批量：逐 sub-request 调 run()，单项异常隔离成该项的 {ok:false}，不连累整批
                items = []
                for sub in batch:
                    try:
                        items.append({"ok": True, "result": run(sub)})
                    except BaseException as item_err:  # noqa: BLE001
                        items.append({
                            "ok": False,
                            "error": str(item_err),
                            "type": type(item_err).__name__,
                            "traceback": traceback.format_exc(),
                        })
                result = {"batch": items}
            else:
                result = run(request)

        _emit(real_stdout, {"ok": True, "result": result})
        return 0
    except BaseException as e:  # noqa: BLE001
        _emit(real_stdout, {
            "ok": False,
            "error": str(e),
            "type": type(e).__name__,
            "traceback": traceback.format_exc(),
            "capturedStdout": captured.getvalue()[:1000],
        })
        return 1


if __name__ == "__main__":
    sys.exit(_main())
