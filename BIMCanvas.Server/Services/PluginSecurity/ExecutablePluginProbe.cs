using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using BIMCanvas.Server.Exceptions;
using BIMCanvas.Server.Services.Plugins;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace BIMCanvas.Server.Services.PluginSecurity;

/// <summary>
/// trust-time 执行 plugin Python 代码做 dry-import + register dry-run
/// (主真理源 v1.1 §3.12 / R1 / R9 / V13 T6c)。
/// <para>
/// <b>本类是唯一允许执行 plugin Python 代码的 Server 端 Service</b>;
/// 只能由 <see cref="PluginLifecycleService"/>.Trust 在用户点 [信任并激活] 后调用,
/// 绝不允许在 install-time / 列表 / scaffold 等场景被调。
/// </para>
/// <para>
/// 实现策略 (模板 §4.2 末"最小占位"原则):
/// 子进程跑 <c>python &lt;probe.py&gt; pluginRoot entry namespace</c>,probe.py 内
/// <c>importlib.util.spec_from_file_location</c> 加载 entry + 调 <c>register(builder)</c>
/// dry-run + 收集工具名,JSON 写到 stdout。
/// 失败 (非 0 退出 / 超时 / register 抛异常) 一律抛 <see cref="PluginProbeFailedException"/>,
/// plugin 保持 untrusted。
/// </para>
/// </summary>
public sealed class ExecutablePluginProbe
{
    private readonly ILogger<ExecutablePluginProbe> _logger;
    private readonly string _agentProjectPath;
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// SDK 可见性纪律:probe 子进程的 cwd / PYTHONPATH 必须包含 BIMCanvas.Agent 根目录,
    /// 与 <c>Program.cs:603</c> 的 Agent 主进程启动对称(`WorkingDirectory = agentProjectPath` +
    /// `python -m src.main`),否则 plugin 的 `from bimcanvas_plugin_sdk import ...` 会
    /// `ModuleNotFoundError`。若 Phase 2 出现第二个需要拉起 plugin 子进程的场景,
    /// 再抽 <c>IPluginRuntimeEnvironment</c> 服务统一管理。
    /// </summary>
    public ExecutablePluginProbe(ILogger<ExecutablePluginProbe> logger, string agentProjectPath)
    {
        _logger = logger;
        _agentProjectPath = agentProjectPath;
    }

    /// <summary>
    /// 对 plugin 执行 dry-import + register dry-run。
    /// 返回 register 内通过 <c>@builder.tool(name=...)</c> 注册的工具名清单 (可空)。
    /// 抛 <see cref="PluginProbeFailedException"/> 时调用方应保持 plugin trustState=Untrusted。
    /// </summary>
    public IReadOnlyList<string> Probe(string pluginRoot, JObject manifest)
    {
        if (manifest is null) throw new ArgumentNullException(nameof(manifest));
        if (string.IsNullOrWhiteSpace(pluginRoot) || !Directory.Exists(pluginRoot))
            throw new PluginProbeFailedException($"pluginRoot 不存在: {pluginRoot}");

        var entryRel = (string?)manifest["mcpTools"];
        if (string.IsNullOrEmpty(entryRel))
        {
            // plugin 不提供 MCP tools 是合法情况 (纯 prompt / agents 类 plugin):Probe 直接通过
            _logger.LogInformation("plugin {Root} 未声明 mcpTools,Probe 跳过 Python 执行", pluginRoot);
            return Array.Empty<string>();
        }
        var entryAbs = Path.GetFullPath(Path.Combine(pluginRoot, entryRel));
        if (!File.Exists(entryAbs))
            throw new PluginProbeFailedException($"mcpTools 入口文件不存在: {entryAbs}");

        var ns = (string?)manifest["mcpNamespace"]
            ?? (string?)manifest["name"]
            ?? "plugin";

        Directory.CreateDirectory(PluginPaths.RuntimeRoot);
        var scriptPath = Path.Combine(PluginPaths.RuntimeRoot, $"probe-{Guid.NewGuid():N}.py");
        File.WriteAllText(scriptPath, ProbeScript, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        try
        {
            var python = "python"; // Python 解释器命令已硬编码(与 Program.cs 启动 Agent 保持一致)
            var psi = new ProcessStartInfo
            {
                FileName = python,
                WorkingDirectory = _agentProjectPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            psi.ArgumentList.Add(scriptPath);
            psi.ArgumentList.Add(pluginRoot);
            psi.ArgumentList.Add(entryAbs);
            psi.ArgumentList.Add(ns);
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            psi.EnvironmentVariables["PYTHONPATH"] = _agentProjectPath;

            using var process = Process.Start(psi)
                ?? throw new PluginProbeFailedException("无法启动 python 子进程 (Process.Start 返回 null)");

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            if (!process.WaitForExit((int)ProcessTimeout.TotalMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
                throw new PluginProbeFailedException(
                    $"ExecutablePluginProbe 超时 ({ProcessTimeout.TotalSeconds:F0}s)", stderr);
            }

            if (process.ExitCode != 0)
            {
                throw new PluginProbeFailedException(
                    $"ExecutablePluginProbe 非 0 退出 (code={process.ExitCode}): {stdout}", stderr);
            }

            JObject result;
            try
            {
                result = JObject.Parse(stdout.Trim());
            }
            catch (Exception ex)
            {
                throw new PluginProbeFailedException(
                    $"ExecutablePluginProbe 输出非 JSON ({ex.Message}); raw stdout: {stdout}", stderr);
            }

            var ok = (bool?)result["ok"] ?? false;
            if (!ok)
            {
                var err = (string?)result["error"] ?? "unknown";
                var type = (string?)result["type"];
                throw new PluginProbeFailedException(
                    $"plugin register dry-run 失败: {err}" + (type is null ? "" : $" ({type})"), stderr);
            }

            var tools = (result["tools"] as JArray)
                ?.Select(t => t.Value<string>())
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => s!)
                .ToList()
                ?? new List<string>();

            // 工具名与 core-base 7 个底座工具的冲突检测 (M0 占位:硬编码 core-base 工具名;
            // M1 / 组3 完成后接入真实清单 — 主真理源 §3.10 + 模板 §4.2)
            var conflicts = tools.Intersect(CoreBaseToolNames, StringComparer.OrdinalIgnoreCase).ToList();
            if (conflicts.Count > 0)
            {
                throw new PluginProbeFailedException(
                    $"plugin 工具名与 core-base 冲突: {string.Join(", ", conflicts)}");
            }

            return tools;
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { /* 清理失败不致命 */ }
        }
    }

    /// <summary>
    /// core-base 7 个底座工具名占位 (主真理源 §3.10 + B0 spike)。
    /// M0 阶段为防 plugin 占用同名,先用平台已规划的工具名硬编码;
    /// M1 / 组3 完成 canvas_core.py 拆分后,改为从该模块动态读取。
    /// </summary>
    private static readonly string[] CoreBaseToolNames = new[]
    {
        "list_project_scenes",
        "load_scene_artifact",
        // 其余 5 个底座工具名留待组3 拆分 canvas_core.py 后补全
    };

    /// <summary>
    /// Probe Python 脚本 —— 极小:能 import 文件 + 调 register + 收集工具名。
    /// 复杂兼容性 (builder 完整 API 模拟) 留 dogfood 阶段迭代 (模板 §4.2 末)。
    /// </summary>
    private const string ProbeScript = @"
import importlib.util, json, sys, traceback

class _FakeBuilder:
    def __init__(self):
        self.tools = []
    def __getattr__(self, name):
        def stub(*args, **kwargs):
            return stub
        return stub
    def tool(self, *args, **kwargs):
        name = kwargs.get('name') or (args[0] if args else None)
        def deco(fn):
            if name:
                self.tools.append(name)
            return fn
        return deco

try:
    plugin_root = sys.argv[1]
    entry = sys.argv[2]
    ns = sys.argv[3]
    sys.path.insert(0, plugin_root)
    spec = importlib.util.spec_from_file_location(f'bimcanvas_probe_{ns}', entry)
    if spec is None or spec.loader is None:
        print(json.dumps({'ok': False, 'error': f'spec_from_file_location 失败: {entry}'}))
        sys.exit(1)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    if not hasattr(module, 'register'):
        print(json.dumps({'ok': False, 'error': 'missing register(builder) function'}))
        sys.exit(1)
    builder = _FakeBuilder()
    module.register(builder)
    print(json.dumps({'ok': True, 'tools': builder.tools}))
except SystemExit:
    raise
except BaseException as e:
    print(json.dumps({'ok': False, 'error': str(e), 'type': type(e).__name__, 'traceback': traceback.format_exc()}))
    sys.exit(1)
";
}
