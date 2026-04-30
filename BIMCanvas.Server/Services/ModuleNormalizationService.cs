using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using BIMCanvas.Core.Converters.Json;
using BIMCanvas.Core.Models.Geometry;
using BIMCanvas.Core.Models.Layout;
using BIMCanvas.Core.Models.Semantic;
using BIMCanvas.Core.Validation;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// Server 端模块数据规范化服务。
    /// 当前负责将 Agent 写入的 facing.semantic 收敛为 facing.value，
    /// 后续可继续承载同类“数据规范化但非验证”的能力。
    /// </summary>
    public class ModuleNormalizationService
    {
        private readonly ILogger<ModuleNormalizationService> _logger;
        private readonly JsonSerializerSettings _jsonSettings;

        public ModuleNormalizationService(ILogger<ModuleNormalizationService> logger)
        {
            _logger = logger;
            _jsonSettings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Converters = { new Polygon2DConverter(), new Point2DConverter(), new FacingConverter() }
            };
        }

        public ModuleNormalizationReport NormalizeModules(string projectPath, IReadOnlyCollection<string>? zoneIds = null)
        {
            var sw = Stopwatch.StartNew();
            var targetSet = zoneIds is { Count: > 0 }
                ? new HashSet<string>(zoneIds)
                : null;

            var modules = LoadModules(projectPath, targetSet);
            var diagnostics = NormalizeFacings(modules, out var normalizedCount);

            PersistModules(projectPath, modules);
            sw.Stop();

            _logger.LogInformation(
                "[ModuleNormalize] 完成: {Total} 个模块, {Normalized} 个规范化, {Errors} 个错误, {ElapsedMs}ms",
                modules.Count,
                normalizedCount,
                diagnostics.Count(d => d.Severity == "error"),
                sw.ElapsedMilliseconds);

            return new ModuleNormalizationReport
            {
                TotalModules = modules.Count,
                NormalizedCount = normalizedCount,
                ErrorCount = diagnostics.Count(d => d.Severity == "error"),
                WarningCount = diagnostics.Count(d => d.Severity == "warning"),
                Diagnostics = diagnostics,
                ElapsedMs = sw.ElapsedMilliseconds
            };
        }

        private List<Module> LoadModules(string projectPath, HashSet<string>? targetZoneIds)
        {
            var schemesPath = Path.Combine(projectPath, "schemes");
            var allModules = new List<Module>();

            if (!Directory.Exists(schemesPath))
                return allModules;

            IEnumerable<string> moduleFiles;
            if (targetZoneIds is { Count: > 0 })
            {
                var targeted = new List<string>();
                foreach (var zoneId in targetZoneIds)
                {
                    var zoneDir = ResolveZoneDirectory(schemesPath, zoneId);
                    var path = Path.Combine(zoneDir, "modules.json");
                    if (File.Exists(path))
                        targeted.Add(path);
                }
                moduleFiles = targeted;
            }
            else
            {
                var zoneFiles = Directory.GetFiles(schemesPath, "modules.json", SearchOption.AllDirectories)
                    .Where(f =>
                    {
                        var dir = Path.GetFileName(Path.GetDirectoryName(f) ?? "");
                        return dir.StartsWith("rz_") || dir.StartsWith("dz_") || dir == "_unzoned";
                    })
                    .ToList();

                if (zoneFiles.Count > 0)
                {
                    moduleFiles = zoneFiles;
                }
                else
                {
                    var legacyPath = Path.Combine(schemesPath, "modules.json");
                    moduleFiles = File.Exists(legacyPath)
                        ? new[] { legacyPath }
                        : Enumerable.Empty<string>();
                }
            }

            foreach (var modulesPath in moduleFiles)
            {
                var zoneId = Path.GetFileName(Path.GetDirectoryName(modulesPath)!);
                try
                {
                    var modules = ReadJson<List<Module>>(modulesPath) ?? new List<Module>();
                    foreach (var module in modules)
                    {
                        module.ZoneId ??= zoneId;
                    }
                    allModules.AddRange(modules);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"模块数据解析失败 | 文件: {modulesPath} | Zone: {zoneId} | 原始错误: {ex.Message}", ex);
                }
            }

            return allModules;
        }

        private static List<Diagnostic> NormalizeFacings(List<Module> modules, out int normalizedCount)
        {
            var diagnostics = new List<Diagnostic>();
            normalizedCount = 0;

            foreach (var module in modules)
            {
                module.Items ??= new List<ModuleItem>();

                if (module.Facing.TryResolveSemanticValue(out var semanticValue))
                {
                    module.Facing = new Facing(semanticValue.Normalize(), null);
                    normalizedCount++;
                    continue;
                }

                if (module.Facing.HasSemantic)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.InvalidFacingSemantic,
                        "error",
                        $"模块 {module.Id} ({module.ModuleName ?? "未命名"}) 的 facing.semantic '{module.Facing.Semantic}' 无效",
                        module.Id,
                        module.ModuleName));
                    continue;
                }

                if (!module.Facing.Value.HasValue)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.MissingFacingValue,
                        "error",
                        $"模块 {module.Id} ({module.ModuleName ?? "未命名"}) 缺少 facing.value",
                        module.Id,
                        module.ModuleName));
                    continue;
                }

                if (!module.Facing.HasFiniteValue() || module.Facing.HasZeroValue() || !module.Facing.TryGetNormalizedValue(out var normalizedValue))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.InvalidFacingValue,
                        "error",
                        $"模块 {module.Id} ({module.ModuleName ?? "未命名"}) 的 facing.value 不是有效单位向量",
                        module.Id,
                        module.ModuleName));
                    continue;
                }

                if (!IsSameVector(module.Facing.Value.Value, normalizedValue))
                {
                    normalizedCount++;
                }
                module.Facing = new Facing(normalizedValue, null);
            }

            return diagnostics;
        }

        private static bool IsSameVector(Vec2D a, Vec2D b)
        {
            const double tolerance = 1e-9;
            return Math.Abs(a.X - b.X) <= tolerance && Math.Abs(a.Y - b.Y) <= tolerance;
        }

        private void PersistModules(string projectPath, List<Module> modules)
        {
            if (modules.Count == 0)
                return;

            var schemesPath = Path.Combine(projectPath, "schemes");
            var byZone = modules
                .GroupBy(m => m.ZoneId ?? "_unzoned")
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var kvp in byZone)
            {
                var zoneDir = ResolveZoneDirectory(schemesPath, kvp.Key);
                if (!Directory.Exists(zoneDir))
                    Directory.CreateDirectory(zoneDir);

                var modulesPath = Path.Combine(zoneDir, "modules.json");
                var toSave = kvp.Value.Select(m =>
                {
                    m.ZoneId = null;
                    return m;
                }).ToList();

                WriteJson(modulesPath, toSave);

                foreach (var module in kvp.Value)
                {
                    module.ZoneId = kvp.Key;
                }
            }
        }

        private string ResolveZoneDirectory(string schemesPath, string zoneId)
        {
            var directPath = Path.Combine(schemesPath, zoneId);
            if (Directory.Exists(directPath))
                return directPath;

            if (Directory.Exists(schemesPath))
            {
                foreach (var parentDir in Directory.GetDirectories(schemesPath))
                {
                    var nestedPath = Path.Combine(parentDir, zoneId);
                    if (Directory.Exists(nestedPath))
                        return nestedPath;
                }
            }

            return directPath;
        }

        private T ReadJson<T>(string path) where T : new()
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            return JsonConvert.DeserializeObject<T>(json, _jsonSettings) ?? new T();
        }

        private void WriteJson<T>(string path, T data)
        {
            var json = JsonConvert.SerializeObject(data, Formatting.Indented, _jsonSettings);
            File.WriteAllText(path, json, Encoding.UTF8);
        }
    }

    public class ModuleNormalizationReport
    {
        public bool IsValid => ErrorCount == 0;

        public int TotalModules { get; set; }

        public int NormalizedCount { get; set; }

        public int ErrorCount { get; set; }

        public int WarningCount { get; set; }

        public List<Diagnostic> Diagnostics { get; set; } = new List<Diagnostic>();

        public long ElapsedMs { get; set; }
    }
}
