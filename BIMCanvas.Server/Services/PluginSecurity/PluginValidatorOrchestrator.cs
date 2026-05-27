using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BIMCanvas.Core.Converters.Json;
using BIMCanvas.Server.Models;
using BIMCanvas.Server.Services;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace BIMCanvas.Server.Services.PluginSecurity;

/// <summary>
/// 校验端点编排（包A · 2026-05-27 决议）：
/// 调 active plugin 的 validators 校验脚本（<see cref="PluginValidatorRuntime"/>）→ 把脚本回传的
/// 规范化结果经现有 <see cref="ModulesWriterService"/> 持久化（过写 gate + 原子 + C# 序列化，保证
/// modules.json 落盘形态与改造前一致）→ 返回脚本产出的冻结报文。
/// 供 <c>/api/modules/normalize</c> 与 <c>/api/validation/layout</c> 两个薄壳端点共用。
/// </summary>
public sealed class PluginValidatorOrchestrator
{
    private readonly PluginValidatorRuntime _runtime;
    private readonly ModulesWriterService _writer;
    private readonly ProjectContext _projectContext;
    private readonly ILogger<PluginValidatorOrchestrator> _logger;
    private readonly JsonSerializerSettings _settings;

    public PluginValidatorOrchestrator(
        PluginValidatorRuntime runtime,
        ModulesWriterService writer,
        ProjectContext projectContext,
        ILogger<PluginValidatorOrchestrator> logger)
    {
        _runtime = runtime;
        _writer = writer;
        _projectContext = projectContext;
        _logger = logger;
        _settings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
            Converters = { new Polygon2DConverter(), new Point2DConverter(), new FacingConverter() }
        };
    }

    /// <summary>
    /// 运行校验脚本（mode = "normalize" | "validate"），持久化回写，返回 report（JObject，已是冻结形态）。
    /// </summary>
    public async Task<JObject> RunAsync(
        string mode,
        string projectPath,
        IReadOnlyList<string>? zoneIds,
        string? variantId,
        CancellationToken ct = default)
    {
        var request = new JObject
        {
            ["projectPath"] = projectPath,
            ["zoneIds"] = zoneIds == null ? null : JArray.FromObject(zoneIds),
            ["variantId"] = variantId,
        };

        var result = await _runtime.InvokeAsync(mode, request, ct);
        await PersistWritebackAsync(projectPath, result["writeback"] as JArray);
        return result["report"] as JObject ?? new JObject();
    }

    /// <summary>
    /// 把脚本回传的 writeback 落盘：每条 {path（相对 project）, wrapper}。
    /// 防路径穿越 + 过 <see cref="ProjectContext.CheckWriteAllowed"/> 写 gate，再经 ModulesWriterService 原子写。
    /// </summary>
    private async Task PersistWritebackAsync(string projectPath, JArray? writeback)
    {
        if (writeback == null) return;
        var projectFull = Path.GetFullPath(projectPath);

        foreach (var entry in writeback.OfType<JObject>())
        {
            var rel = (string?)entry["path"];
            if (string.IsNullOrWhiteSpace(rel)) continue;

            var abs = Path.GetFullPath(Path.Combine(projectPath, rel));
            // 防穿越：必须落在项目目录下
            if (!abs.StartsWith(projectFull, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("[ValidatorWriteback] 跳过越界回写路径: {Rel}", rel);
                continue;
            }

            var relForGate = Path.GetRelativePath(projectPath, abs).Replace('\\', '/');
            var gate = _projectContext.CheckWriteAllowed(relForGate);
            if (!gate.Allowed)
            {
                _logger.LogWarning("[ValidatorWriteback] 写 gate 拒绝 {Rel}: {Code}", relForGate, gate.Code);
                continue;
            }

            if (entry["wrapper"] is not JObject wrapperToken) continue;
            var wrapper = wrapperToken.ToObject<ModulesWrapper>(JsonSerializer.Create(_settings));
            if (wrapper == null) continue;

            await _writer.WriteWrapperAsync(abs, wrapper);
        }
    }
}
