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
        var topology = _topologyService.Build(projectPath);
        // 几何只 validate 需要（normalize 仅按 resolvedLeaves 选文件做 facing 写回，不碰几何）。
        var zoneGeometry = mode == "validate" ? BuildZoneGeometry(projectPath, topology) : null;
        var request = BuildScopeRequest(mode, projectPath, topology, zoneGeometry, zoneIds, variantId);

        var result = await _runtime.InvokeAsync(mode, request, ct);
        await PersistWritebackAsync(projectPath, result["writeback"] as JArray);
        return result["report"] as JObject ?? new JObject();
    }

    /// <summary>
    /// 批量运行同一 mode 的多个 scope：一个 python 子进程跑完全部，插件 + shapely 只导入一次。
    /// 拓扑与 zoneGeometry 只构建一次（几何与 scope 无关，始终按 adopted 叶子；各 scope 仅 resolvedLeaves 不同）。
    /// 返回与 scopes 等长、顺序对齐的 report 列表；单个 scope 失败合成错误报告，不连累其余。
    /// 取代"逐 scope 调 RunAsync 各起一次子进程"的 N 次冷启动。
    /// </summary>
    public async Task<List<JObject>> RunBatchAsync(
        string mode,
        string projectPath,
        IReadOnlyList<ValidatorScope> scopes,
        CancellationToken ct = default)
    {
        var topology = _topologyService.Build(projectPath);
        var zoneGeometry = mode == "validate" ? BuildZoneGeometry(projectPath, topology) : null;

        var requests = scopes
            .Select(s => BuildScopeRequest(
                mode, projectPath, topology,
                zoneGeometry == null ? null : (JObject)zoneGeometry.DeepClone(), // JToken 不能挂多个父，逐请求克隆
                s.ZoneIds, s.VariantId))
            .ToList();

        var items = await _runtime.InvokeBatchAsync(requests, ct);

        var reports = new List<JObject>(items.Count);
        foreach (var item in items)
        {
            if ((bool?)item["ok"] == true && item["result"] is JObject r)
            {
                await PersistWritebackAsync(projectPath, r["writeback"] as JArray);
                reports.Add(r["report"] as JObject ?? new JObject());
            }
            else
            {
                // 单 scope 失败不连累整批：合成错误报告（camelCase，对齐前端 report 形态）
                var err = (string?)item["error"] ?? "unknown";
                _logger.LogWarning("[ValidatorBatch] scope 校验失败: {Err}", err);
                reports.Add(BuildErrorReport($"校验失败: {err}"));
            }
        }
        return reports;
    }

    /// <summary>
    /// 组单个 scope 的 stdin 请求（§2.8：拓扑解析只在 C# 这一处跑，"已解析视图"注入请求；
    /// Python 验证器纯消费 resolvedLeaves/zoneGeometry/pathIssues、不自建拓扑层）。
    /// 单次与批量共用；zoneGeometry 由调用方决定是否传（批量时为避免多父需传克隆）。
    /// </summary>
    private JObject BuildScopeRequest(
        string mode, string projectPath, ModuleFileTopology topology,
        JObject? zoneGeometry, IReadOnlyList<string>? zoneIds, string? variantId)
    {
        var request = new JObject
        {
            ["projectPath"] = projectPath,
            ["zoneIds"] = zoneIds == null ? null : JArray.FromObject(zoneIds),
            ["variantId"] = variantId,
            ["mode"] = mode,
        };
        var resolved = topology.GetResolvedLeaves(zoneIds, variantId);   // 纯文件映射 + pathIssues（§6-3 去几何）
        request["resolvedLeaves"] = JArray.FromObject(resolved.ResolvedLeaves, JsonSerializer.Create(_settings));
        request["pathIssues"] = JArray.FromObject(resolved.PathIssues, JsonSerializer.Create(_settings));
        if (mode == "validate" && zoneGeometry != null)
            request["zoneGeometry"] = zoneGeometry;
        return request;
    }

    /// <summary>单 scope 校验失败时的合成报告（camelCase，对齐前端 ModuleNormalizationReport / SchemeValidationReport）。</summary>
    private static JObject BuildErrorReport(string message) => new JObject
    {
        ["isValid"] = false,
        ["totalModules"] = 0,
        ["normalizedCount"] = 0,
        ["errorCount"] = 1,
        ["warningCount"] = 0,
        ["elapsedMs"] = 0,
        ["diagnostics"] = new JArray
        {
            new JObject
            {
                ["code"] = "VALIDATOR_SCOPE_FAILED",
                ["severity"] = "error",
                ["message"] = message,
                ["moduleId"] = "",
            }
        },
    };

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

/// <summary>一个校验 scope：canonical（两者皆 null）或某设计区的某变体。</summary>
public sealed record ValidatorScope(IReadOnlyList<string>? ZoneIds, string? VariantId);
