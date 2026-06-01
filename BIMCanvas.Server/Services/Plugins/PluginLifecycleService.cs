using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BIMCanvas.Server.Exceptions;
using BIMCanvas.Server.Models.Plugins;
using BIMCanvas.Server.Services.PluginSecurity;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace BIMCanvas.Server.Services.Plugins;

/// <summary>
/// Plugin 四态生命周期编排 (主真理源 v1.1 §3.2)。
/// <para>
/// 状态机:installed → trusted → active → bound → launched。
/// 子状态:trustState ∈ {Untrusted, Trusted}。
/// </para>
/// <para>
/// 关键纪律 (V13 T6b/T6c / R1 / R9):
/// - <see cref="ActivateAsync"/> 必须先校验 trustState == Trusted,否则抛 <see cref="PluginNotTrustedException"/>。
/// - <see cref="TrustAsync"/> 是用户点 [信任并激活] 时唯一允许触发 <see cref="ExecutablePluginProbe"/> 的入口。
/// - install / uninstall / activate / bind / launch 互不耦合,四态独立流转。
/// </para>
/// </summary>
public sealed class PluginLifecycleService
{
    private readonly PluginInstallService _installService;
    private readonly ExecutablePluginProbe _probe;
    private readonly PluginTrustService _trustService;
    private readonly ILogger<PluginLifecycleService> _logger;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public PluginLifecycleService(
        PluginInstallService installService,
        ExecutablePluginProbe probe,
        PluginTrustService trustService,
        ILogger<PluginLifecycleService> logger)
    {
        _installService = installService;
        _probe = probe;
        _trustService = trustService;
        _logger = logger;
    }

    /// <summary>
    /// 安装 plugin (转发到 <see cref="PluginInstallService"/>)。R1:不调 Probe。
    /// </summary>
    public Task<PluginInstallState> InstallAsync(string repoUrl, string? gitRef, CancellationToken ct = default)
        => _installService.InstallAsync(repoUrl, gitRef, ct);

    /// <summary>
    /// 信任 plugin:调 <see cref="ExecutablePluginProbe"/> + 通过则 MarkTrusted (V13 T6c)。
    /// Probe 失败抛 <see cref="PluginProbeFailedException"/>,plugin 保持 untrusted。
    /// </summary>
    public async Task TrustAsync(string pluginId, CancellationToken ct = default)
    {
        var state = await _trustService.GetStateAsync(pluginId, ct)
            ?? throw new PluginNotFoundException(pluginId);

        if (state.TrustState == TrustState.Trusted)
        {
            _logger.LogInformation("plugin '{Id}' 已 Trusted,Trust 操作 no-op", pluginId);
            return;
        }

        var manifestPath = PluginPaths.PluginManifestFile(pluginId);
        if (!File.Exists(manifestPath))
            throw new PluginNotFoundException(pluginId);
        var manifest = JObject.Parse(File.ReadAllText(manifestPath));

        var pluginRoot = PluginPaths.PluginRoot(pluginId);
        var tools = _probe.Probe(pluginRoot, manifest); // 失败抛 PluginProbeFailedException

        await _trustService.MarkTrustedAsync(pluginId, ct);
        _logger.LogInformation(
            "plugin '{Id}' 已 Trusted (probe 发现工具 {Count} 个)", pluginId, tools.Count);
    }

    /// <summary>
    /// 激活 plugin:写 <c>server_config.json.agent.activePlugin</c>。
    /// <b>关键约束</b>:必须 trustState == Trusted,否则抛 <see cref="PluginNotTrustedException"/> (V13 T6b)。
    /// </summary>
    public async Task ActivateAsync(string pluginId, CancellationToken ct = default)
    {
        var state = await _trustService.GetStateAsync(pluginId, ct)
            ?? throw new PluginNotFoundException(pluginId);
        if (state.TrustState != TrustState.Trusted)
            throw new PluginNotTrustedException(pluginId);

        await WriteActivePluginAsync(pluginId, ct);
        _logger.LogInformation("plugin '{Id}' 已激活 (server_config.json.agent.activePlugin)", pluginId);
    }

    /// <summary>
    /// 反激活:清空 <c>server_config.json.agent.activePlugin</c> (核回 core-base)。
    /// </summary>
    public Task DeactivateAsync(CancellationToken ct = default)
        => WriteActivePluginAsync(null, ct);

    /// <summary>
    /// 信任 + 激活组合 (主真理源 §2.1 步骤 7:首次激活按钮专用)。
    /// </summary>
    public async Task TrustAndActivateAsync(string pluginId, CancellationToken ct = default)
    {
        await TrustAsync(pluginId, ct);
        await ActivateAsync(pluginId, ct);
    }

    /// <summary>
    /// 卸载:删 plugins-state.json 记录 + 删 plugin 目录。如果该 plugin 当前是 activePlugin,
    /// 一并清空 activePlugin (避免 Server 启动时 LaunchContext 引用不存在的 plugin)。
    /// </summary>
    public async Task UninstallAsync(string pluginId, CancellationToken ct = default)
    {
        var pluginRoot = PluginPaths.PluginRoot(pluginId);

        // 如果当前 activePlugin 是它,清空
        var serverConfig = ConfigService.Load();
        if (string.Equals(serverConfig.Agent.ActivePlugin, pluginId, StringComparison.OrdinalIgnoreCase))
        {
            await WriteActivePluginAsync(null, ct);
        }

        await _trustService.RemoveStateAsync(pluginId, ct);

        if (Directory.Exists(pluginRoot))
        {
            try { PluginPaths.DeleteDirectoryResilient(pluginRoot); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "删除 plugin 目录失败 (将由用户手动清理): {Root}", pluginRoot);
            }
        }
        _logger.LogInformation("plugin '{Id}' 已卸载", pluginId);
    }

    /// <summary>
    /// 构造 PluginLaunchContext (主真理源 §3.3)。
    /// 不写文件;调用方自行决定是否 <see cref="WriteLaunchContextFileAsync"/>。
    /// </summary>
    public PluginLaunchContext BuildLaunchContext(string? projectPath)
    {
        var serverConfig = ConfigService.Load();
        var activePluginId = serverConfig.Agent.ActivePlugin ?? "core-base";
        var activePluginRoot = PluginPaths.PluginRoot(activePluginId);
        // 项目去插件态:只要打开了项目就 ProjectBound(命名空间 = active plugin id);
        // 不再携带 sceneId / scenes / lock / readOnlySceneIds。
        var mode = projectPath is not null
            ? LaunchMode.ProjectBound
            : LaunchMode.Projectless;

        // ServerUrl 复用 Agent baseUrl 解析 (Agent 子进程通过它回调 Server REST)
        var serverPort = serverConfig.Server.Port > 0 ? serverConfig.Server.Port : 5000;
        var serverUrl = $"http://127.0.0.1:{serverPort}";

        return new PluginLaunchContext(
            ActivePluginId: activePluginId,
            ActivePluginRoot: activePluginRoot,
            Mode: mode,
            ProjectPath: mode == LaunchMode.ProjectBound ? projectPath : null,
            ServerUrl: serverUrl,
            TrustMode: TrustMode.FullTrust
        );
    }

    /// <summary>
    /// 把 LaunchContext 序列化到 <c>.runtime/launch-context-{pid}.json</c> (主真理源 §4.10)。
    /// Agent 子进程通过 CLI arg 拿到路径,读完后由 Agent 端删除 (组3 责任)。
    /// </summary>
    public async Task<string> WriteLaunchContextFileAsync(PluginLaunchContext context, int pid, CancellationToken ct = default)
    {
        Directory.CreateDirectory(PluginPaths.RuntimeRoot);
        var path = PluginPaths.LaunchContextPath(pid);
        var settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
            NullValueHandling = NullValueHandling.Ignore,
            // enum 字符串化由各 enum 类型上的 [JsonConverter] attribute 控制(LaunchMode / TrustMode / SourceKind 已标注)。
        };
        var json = JsonConvert.SerializeObject(context, settings);
        await File.WriteAllTextAsync(path, json, Utf8NoBom, ct);
        return path;
    }

    // ─── 内部 ─────────────────────────────────────────────────────────────────

    private static Task WriteActivePluginAsync(string? pluginId, CancellationToken ct)
    {
        // activePlugin 落在统一配置文件的 server 段下的 agent 子段(server.agent.activePlugin),
        // 与顶层 agent runtime 段(provider 配置)是不同路径,互不冲突。
        var server = ConfigService.LoadSection(ConfigService.SectionServer);
        if (server["agent"] is not JObject agent)
        {
            agent = new JObject();
            server["agent"] = agent;
        }
        agent["activePlugin"] = pluginId is null ? JValue.CreateNull() : new JValue(pluginId);

        ConfigService.SaveSection(ConfigService.SectionServer, server);
        return Task.CompletedTask;
    }
}
