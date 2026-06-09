using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BIMCanvas.Core.Converters.Json;
using BIMCanvas.Core.Models.Computed;
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
    private readonly ModuleFileTopologyService _topologyService;
    private readonly ILogger<PluginValidatorOrchestrator> _logger;
    private readonly JsonSerializerSettings _settings;

    public PluginValidatorOrchestrator(
        PluginValidatorRuntime runtime,
        ModulesWriterService writer,
        ProjectContext projectContext,
        ModuleFileTopologyService topologyService,
        ILogger<PluginValidatorOrchestrator> logger)
    {
        _runtime = runtime;
        _writer = writer;
        _projectContext = projectContext;
        _topologyService = topologyService;
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

        // §2.8 整合：拓扑解析只在 C# 这一处跑，把"已解析视图"注入 stdin 请求；
        // Python 验证器删掉自建拓扑层、纯消费这三块（resolvedLeaves/zoneGeometry/pathIssues）。
        var topology = _topologyService.Build(projectPath);
        var resolved = topology.GetResolvedLeaves(zoneIds, variantId);   // 纯文件映射 + pathIssues（§6-3 去几何）
        request["resolvedLeaves"] = JArray.FromObject(resolved.ResolvedLeaves, JsonSerializer.Create(_settings));
        request["pathIssues"] = JArray.FromObject(resolved.PathIssues, JsonSerializer.Create(_settings));
        // 几何只 validate 需要（normalize 仅按 resolvedLeaves 选文件做 facing 写回，不碰几何）。
        if (mode == "validate")
            request["zoneGeometry"] = BuildZoneGeometry(projectPath, topology);

        var result = await _runtime.InvokeAsync(mode, request, ct);
        await PersistWritebackAsync(projectPath, result["writeback"] as JArray);
        return result["report"] as JObject ?? new JObject();
    }

    /// <summary>
    /// 组 validate 的 zoneGeometry（契约-②，叉口-1：几何由 C# 在边界处合并）。
    /// designZones = room_zones.json 的 Room（带 computedBoundary）＋ per-scheme 叶子（Designable，RawBoundary 来自解析器）；
    /// exclusionZones = exclusions.json。镜像旧 _load_zone_data（room_zones + scheme leaves），叶子源升级为解析器（adopted-aware）。
    /// 用本服务 _settings（含 Polygon2DConverter）序列化 → 边界形态与 Python 过去从盘读到的逐字一致。
    /// </summary>
    private JObject BuildZoneGeometry(string projectPath, ModuleFileTopology topology)
    {
        var designZones = new List<Zone>();
        designZones.AddRange(ReadZones(Path.Combine(projectPath, "computed", "room_zones.json")));
        designZones.AddRange(PerSchemeZoneTreeBuilder.AllLeafZones(projectPath, topology));
        var exclusionZones = ReadZones(Path.Combine(projectPath, "computed", "exclusions.json"));

        return new JObject
        {
            ["designZones"] = JArray.FromObject(designZones, JsonSerializer.Create(_settings)),
            ["exclusionZones"] = JArray.FromObject(exclusionZones, JsonSerializer.Create(_settings)),
        };
    }

    private List<Zone> ReadZones(string path)
    {
        if (!File.Exists(path))
            return new List<Zone>();
        return JsonConvert.DeserializeObject<List<Zone>>(File.ReadAllText(path), _settings) ?? new List<Zone>();
    }

    /// <summary>
    /// 把脚本回传的 writeback 落盘：每条 {path（相对 project）, wrapper}。
    /// 防路径穿越 + 过 <see cref="ProjectContext.CheckWriteAllowed"/> 写 gate，再经 ModulesWriterService 原子写。
    /// </summary>
    private async Task PersistWritebackAsync(string projectPath, JArray? writeback)
    {
        if (writeback == null) return;
        var projectFull = Path.GetFullPath(projectPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var projectPrefix = projectFull + Path.DirectorySeparatorChar;

        foreach (var entry in writeback.OfType<JObject>())
        {
            var rel = (string?)entry["path"];
            if (string.IsNullOrWhiteSpace(rel)) continue;

            var abs = Path.GetFullPath(Path.Combine(projectPath, rel));
            // 防穿越：必须严格落在项目目录内（带分隔符前缀，挡 sibling 目录如 {project}-evil；
            // 写 gate 只拦 baseline/computed，挡不住 ../ 逃出 project，故此处是首道防线）
            if (!abs.Equals(projectFull, StringComparison.OrdinalIgnoreCase)
                && !abs.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase))
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
