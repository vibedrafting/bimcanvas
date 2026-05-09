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
        private readonly ModuleFileTopologyService _moduleFileTopologyService;
        private readonly JsonSerializerSettings _jsonSettings;

        public ModuleNormalizationService(
            ILogger<ModuleNormalizationService> logger,
            ModuleFileTopologyService moduleFileTopologyService)
        {
            _logger = logger;
            _moduleFileTopologyService = moduleFileTopologyService;
            _jsonSettings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Converters = { new Polygon2DConverter(), new Point2DConverter(), new FacingConverter() }
            };
        }

        public ModuleNormalizationReport NormalizeModules(
            string projectPath,
            IReadOnlyCollection<string>? zoneIds = null,
            string? variantId = null)
        {
            var sw = Stopwatch.StartNew();
            var targetSet = zoneIds is { Count: > 0 }
                ? new HashSet<string>(zoneIds)
                : null;

            if (!string.IsNullOrWhiteSpace(variantId) && targetSet == null)
                throw new ArgumentException(
                    "variantId 非空时必须显式指定 zoneIds，不允许全分区扫描变体",
                    nameof(zoneIds));

            // variantId 非空 → 读写 modules-{variantId}.json，canonical 不动
            var moduleFiles = LoadModuleFiles(projectPath, targetSet, variantId);
            var diagnostics = new List<Diagnostic>();
            var normalizedCount = 0;
            var totalModules = 0;

            foreach (var moduleFile in moduleFiles)
            {
                diagnostics.AddRange(NormalizeFacings(moduleFile.Modules, out var fileNormalizedCount));
                normalizedCount += fileNormalizedCount;
                totalModules += moduleFile.Modules.Count;
                PersistModules(moduleFile);
            }
            sw.Stop();

            _logger.LogInformation(
                "[ModuleNormalize] 完成: {Total} 个模块, {Normalized} 个规范化, {Errors} 个错误, {ElapsedMs}ms",
                totalModules,
                normalizedCount,
                diagnostics.Count(d => d.Severity == "error"),
                sw.ElapsedMilliseconds);

            return new ModuleNormalizationReport
            {
                TotalModules = totalModules,
                NormalizedCount = normalizedCount,
                ErrorCount = diagnostics.Count(d => d.Severity == "error"),
                WarningCount = diagnostics.Count(d => d.Severity == "warning"),
                Diagnostics = diagnostics,
                ElapsedMs = sw.ElapsedMilliseconds
            };
        }

        private List<LoadedModuleFile> LoadModuleFiles(
            string projectPath,
            HashSet<string>? targetZoneIds,
            string? variantId = null)
        {
            var schemesPath = Path.Combine(projectPath, "schemes");
            var result = new List<LoadedModuleFile>();

            if (!Directory.Exists(schemesPath))
                return result;

            var topology = _moduleFileTopologyService.Build(projectPath);
            var moduleFiles = topology.GetExistingCanonicalModuleFiles(targetZoneIds, variantId);

            foreach (var moduleFile in moduleFiles)
            {
                try
                {
                    // v1.1: variant 文件可能是 wrapper { summary, modules } 或 legacy 裸数组；canonical 永远是裸数组
                    var (modules, variantSummary) = ReadModulesAndOptionalSummary(moduleFile.FilePath);
                    foreach (var module in modules)
                    {
                        module.ZoneId ??= moduleFile.ZoneId;
                    }
                    result.Add(new LoadedModuleFile(moduleFile.FilePath, moduleFile.ZoneId, modules, variantSummary));
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"模块数据解析失败 | 文件: {moduleFile.FilePath} | Zone: {moduleFile.ZoneId} | 原始错误: {ex.Message}", ex);
                }
            }

            return result;
        }

        /// <summary>
        /// 读 modules 文件，兼容两种 schema：
        ///   - canonical / legacy: 裸 List&lt;Module&gt; 数组 → variantSummary = null
        ///   - v1.1 variant wrapper: { "summary": "...", "modules": [...] } → 抽出两部分
        /// </summary>
        private (List<Module> modules, string? variantSummary) ReadModulesAndOptionalSummary(string path)
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            var token = Newtonsoft.Json.Linq.JToken.Parse(json);
            if (token is Newtonsoft.Json.Linq.JArray arr)
            {
                var modules = arr.ToObject<List<Module>>(JsonSerializer.Create(_jsonSettings)) ?? new List<Module>();
                return (modules, null);
            }
            if (token is Newtonsoft.Json.Linq.JObject obj && obj["modules"] is Newtonsoft.Json.Linq.JArray inner)
            {
                var modules = inner.ToObject<List<Module>>(JsonSerializer.Create(_jsonSettings)) ?? new List<Module>();
                var summary = obj.Value<string>("summary");
                return (modules, summary);
            }
            throw new InvalidOperationException(
                $"modules 文件结构不识别（既不是裸数组也不是 {{summary, modules}} 包裹对象）: {path}");
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

        private void PersistModules(LoadedModuleFile moduleFile)
        {
            if (moduleFile.Modules.Count == 0)
                return;

            var toSave = moduleFile.Modules.Select(m =>
            {
                m.ZoneId = null;
                return m;
            }).ToList();

            // v1.1: 如果原始文件是 variant wrapper（VariantSummary != null），写回时保持 wrapper 形态，
            // 保留原 summary 字段不被 normalize 流程吃掉；canonical 文件继续写裸数组
            if (moduleFile.VariantSummary != null)
            {
                var wrapper = new { summary = moduleFile.VariantSummary, modules = toSave };
                WriteJson(moduleFile.FilePath, wrapper);
            }
            else
            {
                WriteJson(moduleFile.FilePath, toSave);
            }

            foreach (var module in moduleFile.Modules)
            {
                module.ZoneId = moduleFile.ZoneId;
            }
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

        private sealed class LoadedModuleFile
        {
            public LoadedModuleFile(string filePath, string zoneId, List<Module> modules, string? variantSummary = null)
            {
                FilePath = filePath;
                ZoneId = zoneId;
                Modules = modules;
                VariantSummary = variantSummary;
            }

            public string FilePath { get; }

            public string ZoneId { get; }

            public List<Module> Modules { get; }

            /// <summary>
            /// v1.1 wrapper 形态的 summary 字段；canonical / legacy 裸数组文件该字段为 null。
            /// PersistModules 用它判断写回 wrapper 还是裸数组。
            /// </summary>
            public string? VariantSummary { get; }
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
