using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BIMCanvas.Server.Models.Plugins;
using BIMCanvas.Server.Services.Plugins;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BIMCanvas.Server.Services.PluginSecurity;

/// <summary>
/// 按需调用「当前激活插件」的 validators/ 校验脚本（包A · 2026-05-27 决议）。
/// <para>
/// 与 <see cref="ExecutablePluginProbe"/> 同为"执行 plugin Python"的 Server 端组件：
/// 起 python 子进程跑 Agent 侧 <c>src/runtime/validator_host.py</c>，后者 importlib 加载
/// plugin 的 <c>validators/{pluginId}.py</c> 入口并调 <c>run(request)</c>。validation 的
/// domain 逻辑全在 plugin 脚本里；平台只负责"调用机制 + 几何原语(SDK) + 稳定端点"。
/// </para>
/// <para>
/// 安全：只跑 <see cref="TrustState.Trusted"/> 的 active plugin。输入(请求 JSON)走 stdin、
/// 输出(结果信封)走 stdout 单行 JSON。子进程 cwd / PYTHONPATH = Agent 根目录，与
/// <see cref="ExecutablePluginProbe"/> 对称，使脚本可 <c>from bimcanvas_plugin_sdk import geometry</c>。
/// </para>
/// </summary>
public sealed class PluginValidatorRuntime
{
    private readonly ILogger<PluginValidatorRuntime> _logger;
    private readonly string _agentProjectPath;
    private readonly PluginTrustService _trustService;
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(60);

    public PluginValidatorRuntime(
        ILogger<PluginValidatorRuntime> logger,
        string agentProjectPath,
        PluginTrustService trustService)
    {
        _logger = logger;
        _agentProjectPath = agentProjectPath;
        _trustService = trustService;
    }

    /// <summary>
    /// 调用 active plugin 的校验脚本。
    /// </summary>
    /// <param name="mode">"normalize" | "validate"</param>
    /// <param name="request">透传给脚本的请求对象（projectPath / zoneIds / variantId 等由调用方填）；
    /// 本方法会写入 <c>mode</c> 字段。</param>
    /// <returns>脚本返回的 result 对象（含 report + 可选 writeback）。</returns>
    /// <exception cref="PluginValidatorException">未受信 / 无脚本 / 子进程失败 / 输出非法时抛出。</exception>
    public async Task<JObject> InvokeAsync(string mode, JObject request, CancellationToken ct = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var serverConfig = ConfigService.Load();
        var activePluginId = serverConfig.Agent.ActivePlugin ?? "core-base";

        // 只跑受信插件（R1 / R2：trust 状态存平台侧）
        var state = await _trustService.GetStateAsync(activePluginId, ct);
        if (state is null || state.TrustState != TrustState.Trusted)
            throw new PluginValidatorException($"active plugin '{activePluginId}' 未受信，拒绝执行校验脚本");

        var pluginRoot = PluginPaths.PluginRoot(activePluginId);
        if (!Directory.Exists(pluginRoot))
            throw new PluginValidatorException($"active plugin 目录不存在: {pluginRoot}");

        // 约定入口：validators/{pluginId}.py（与 mcp_tools/<ns>.py 单入口约定对称）
        var entryAbs = Path.Combine(pluginRoot, "validators", $"{activePluginId}.py");
        if (!File.Exists(entryAbs))
            throw new PluginValidatorException(
                $"active plugin '{activePluginId}' 未提供校验脚本: {entryAbs}");

        var hostScript = Path.Combine(_agentProjectPath, "src", "runtime", "validator_host.py");
        if (!File.Exists(hostScript))
            throw new PluginValidatorException($"validator_host 运行器缺失: {hostScript}");

        request["mode"] = mode;
        var requestJson = request.ToString(Formatting.None);

        var psi = new ProcessStartInfo
        {
            FileName = "python", // 与 ExecutablePluginProbe / Program.cs 启动 Agent 保持一致
            WorkingDirectory = _agentProjectPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add(hostScript);
        psi.ArgumentList.Add(pluginRoot);
        psi.ArgumentList.Add(entryAbs);
        psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
        psi.EnvironmentVariables["PYTHONPATH"] = _agentProjectPath;

        using var process = Process.Start(psi)
            ?? throw new PluginValidatorException("无法启动 python 子进程 (Process.Start 返回 null)");

        // 请求体小（< pipe 缓冲），先写满 stdin 再关闭；脚本读完 stdin 才产出 stdout，无死锁
        await process.StandardInput.WriteAsync(requestJson.AsMemory(), ct);
        process.StandardInput.Close();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit((int)ProcessTimeout.TotalMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            throw new PluginValidatorException($"校验脚本超时 ({ProcessTimeout.TotalSeconds:F0}s)");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
            throw new PluginValidatorException(
                $"校验脚本非 0 退出 (code={process.ExitCode}): {Truncate(stdout)} | stderr: {Truncate(stderr)}");

        JObject envelope;
        try
        {
            envelope = JObject.Parse(stdout.Trim());
        }
        catch (Exception ex)
        {
            throw new PluginValidatorException(
                $"校验脚本输出非 JSON ({ex.Message}); stdout: {Truncate(stdout)} | stderr: {Truncate(stderr)}");
        }

        var ok = (bool?)envelope["ok"] ?? false;
        if (!ok)
        {
            var err = (string?)envelope["error"] ?? "unknown";
            var type = (string?)envelope["type"];
            _logger.LogWarning("[PluginValidator] 脚本失败: {Err} ({Type})", err, type);
            throw new PluginValidatorException(
                $"校验脚本执行失败: {err}" + (type is null ? "" : $" ({type})"));
        }

        return envelope["result"] as JObject
            ?? throw new PluginValidatorException("校验脚本未返回 result 对象");
    }

    private static string Truncate(string s, int max = 800)
        => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max) + "…");
}

/// <summary>校验脚本调用失败（未受信 / 缺脚本 / 子进程异常 / 输出非法）。</summary>
public sealed class PluginValidatorException : Exception
{
    public PluginValidatorException(string message) : base(message) { }
}
