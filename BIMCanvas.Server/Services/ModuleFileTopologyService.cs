using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BIMCanvas.Core.Converters.Json;
using BIMCanvas.Core.Models.Computed;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// Resolves the canonical modules.json file topology from schemes/zones.json.
    /// </summary>
    public class ModuleFileTopologyService
    {
        private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
        private static readonly StringComparer ZoneComparer = StringComparer.OrdinalIgnoreCase;
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Converters = { new Polygon2DConverter(), new Point2DConverter() }
        };

        public ModuleFileTopology Build(string projectPath)
        {
            return BuildFromSchemesPath(Path.Combine(projectPath, "schemes"));
        }

        public static ModuleFileTopology BuildFromSchemesPath(string schemesPath)
        {
            var zonesPath = Path.Combine(schemesPath, "zones.json");
            if (!File.Exists(zonesPath))
            {
                return ModuleFileTopology.CreateLegacy(schemesPath);
            }

            var zones = ReadJson<List<Zone>>(zonesPath) ?? new List<Zone>();
            if (zones.Count == 0)
            {
                return ModuleFileTopology.CreateLegacy(schemesPath);
            }

            var builder = new TopologyBuilder(schemesPath, zones);
            return builder.Build();
        }

        public static bool TryResolveZoneDirectory(string schemesPath, string zoneId, out string zoneDirectory)
        {
            var topology = BuildFromSchemesPath(schemesPath);
            return topology.TryResolveZoneDirectory(zoneId, out zoneDirectory);
        }

        public static IReadOnlyList<ModuleFileEntry> FindExistingCanonicalModuleFiles(string schemesPath)
        {
            var topology = BuildFromSchemesPath(schemesPath);
            return topology.GetExistingCanonicalModuleFiles(null);
        }

        /// <summary>
        /// 构建叶子分区的 modules JSON 文件名。variantId 为空 → canonical "modules.json"；
        /// 非空 → "modules-{variantId}.json"，仅 module-relocation-agent 生成的变体使用。
        /// </summary>
        public static string BuildVariantFilename(string? variantId)
        {
            if (string.IsNullOrWhiteSpace(variantId))
                return "modules.json";

            EnsureSafeVariantId(variantId);
            return $"modules-{variantId}.json";
        }

        /// <summary>
        /// 校验 variantId 仅含安全字符（字母/数字/下划线/连字符），防止路径穿越。
        /// </summary>
        public static void EnsureSafeVariantId(string variantId)
        {
            if (string.IsNullOrWhiteSpace(variantId))
                throw new ArgumentException("variantId 不能为空", nameof(variantId));

            foreach (var ch in variantId)
            {
                if (!char.IsLetterOrDigit(ch) && ch != '-' && ch != '_')
                    throw new ArgumentException(
                        $"variantId 包含非法字符 '{ch}'，仅允许字母/数字/下划线/连字符",
                        nameof(variantId));
            }
        }

        public static IReadOnlyList<ModuleFileEntry> FindLegacyModuleFiles(string schemesPath)
        {
            if (!Directory.Exists(schemesPath))
                return Array.Empty<ModuleFileEntry>();

            var zoneFiles = Directory.GetFiles(schemesPath, "modules.json", SearchOption.AllDirectories)
                .Where(f =>
                {
                    var dir = Path.GetFileName(Path.GetDirectoryName(f) ?? "");
                    return dir.StartsWith("rz_", StringComparison.OrdinalIgnoreCase) ||
                           dir.StartsWith("dz_", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(dir, "_unzoned", StringComparison.OrdinalIgnoreCase);
                })
                .Select(f => ModuleFileEntry.FromFile(schemesPath, f))
                .ToList();

            if (zoneFiles.Count > 0)
                return zoneFiles;

            var legacyPath = Path.Combine(schemesPath, "modules.json");
            return File.Exists(legacyPath)
                ? new[] { ModuleFileEntry.FromFile(schemesPath, legacyPath, "legacy") }
                : Array.Empty<ModuleFileEntry>();
        }

        private static T? ReadJson<T>(string path)
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            return JsonConvert.DeserializeObject<T>(json, JsonSettings);
        }

        private sealed class TopologyBuilder
        {
            private readonly string _schemesPath;
            private readonly List<Zone> _zones;
            private readonly Dictionary<string, Zone> _zonesById;
            private readonly HashSet<string> _referencedZoneIds;
            private readonly Dictionary<string, ModuleFileEntry> _canonicalByZoneId = new Dictionary<string, ModuleFileEntry>(ZoneComparer);
            private readonly Dictionary<string, List<string>> _leafZoneIdsByContainerId = new Dictionary<string, List<string>>(ZoneComparer);
            private readonly HashSet<string> _containerZoneIds = new HashSet<string>(ZoneComparer);

            public TopologyBuilder(string schemesPath, List<Zone> zones)
            {
                _schemesPath = schemesPath;
                _zones = zones;
                _zonesById = zones
                    .Where(z => !string.IsNullOrWhiteSpace(z.Id))
                    .GroupBy(z => z.Id, ZoneComparer)
                    .ToDictionary(g => g.Key, g => g.First(), ZoneComparer);
                _referencedZoneIds = new HashSet<string>(ZoneComparer);
                CollectReferencedZoneIds(_zones);
            }

            public ModuleFileTopology Build()
            {
                foreach (var zone in _zones)
                {
                    if (string.IsNullOrWhiteSpace(zone.Id) || _referencedZoneIds.Contains(zone.Id))
                        continue;

                    RegisterZone(zone, new List<string> { zone.Id }, new HashSet<string>(ZoneComparer));
                }

                var unzonedEntry = ModuleFileEntry.FromCanonical(_schemesPath, "_unzoned", new[] { "_unzoned" });
                _canonicalByZoneId["_unzoned"] = unzonedEntry;

                return new ModuleFileTopology(
                    _schemesPath,
                    hasZoneTopology: true,
                    _canonicalByZoneId,
                    _leafZoneIdsByContainerId,
                    _containerZoneIds);
            }

            private List<string> RegisterZone(Zone zoneRef, List<string> pathSegments, HashSet<string> activeStack)
            {
                if (string.IsNullOrWhiteSpace(zoneRef.Id))
                    return new List<string>();

                var zone = ResolveFullZone(zoneRef);
                if (!activeStack.Add(zone.Id))
                    return new List<string>();

                try
                {
                    var subZones = GetSubZones(zoneRef, zone);
                    if (subZones.Count > 0)
                    {
                        _containerZoneIds.Add(zone.Id);
                        var leafIds = new List<string>();
                        foreach (var subZoneRef in subZones)
                        {
                            if (string.IsNullOrWhiteSpace(subZoneRef.Id))
                                continue;

                            var childPath = new List<string>(pathSegments) { subZoneRef.Id };
                            leafIds.AddRange(RegisterZone(subZoneRef, childPath, activeStack));
                        }

                        _leafZoneIdsByContainerId[zone.Id] = leafIds;
                        return leafIds;
                    }

                    if (!_canonicalByZoneId.ContainsKey(zone.Id))
                    {
                        _canonicalByZoneId[zone.Id] = ModuleFileEntry.FromCanonical(_schemesPath, zone.Id, pathSegments);
                    }

                    return new List<string> { zone.Id };
                }
                finally
                {
                    activeStack.Remove(zone.Id);
                }
            }

            private Zone ResolveFullZone(Zone zoneRef)
            {
                if (!string.IsNullOrWhiteSpace(zoneRef.Id) &&
                    _zonesById.TryGetValue(zoneRef.Id, out var fullZone))
                {
                    return fullZone;
                }

                return zoneRef;
            }

            private static List<Zone> GetSubZones(Zone zoneRef, Zone fullZone)
            {
                if (zoneRef.SubZones is { Count: > 0 })
                    return zoneRef.SubZones;

                if (fullZone.SubZones is { Count: > 0 })
                    return fullZone.SubZones;

                return new List<Zone>();
            }

            private void CollectReferencedZoneIds(IEnumerable<Zone> zones)
            {
                foreach (var zone in zones)
                {
                    if (zone.SubZones == null)
                        continue;

                    foreach (var subZone in zone.SubZones)
                    {
                        if (!string.IsNullOrWhiteSpace(subZone.Id))
                            _referencedZoneIds.Add(subZone.Id);

                        if (subZone.SubZones is { Count: > 0 })
                            CollectReferencedZoneIds(subZone.SubZones);
                    }
                }
            }
        }

        internal static bool PathsEqual(string left, string right)
        {
            return PathComparer.Equals(NormalizeFullPath(left), NormalizeFullPath(right));
        }

        internal static string NormalizeFullPath(string path)
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    public sealed class ModuleFileTopology
    {
        private readonly Dictionary<string, ModuleFileEntry> _canonicalByZoneId;
        private readonly Dictionary<string, List<string>> _leafZoneIdsByContainerId;
        private readonly HashSet<string> _containerZoneIds;

        internal ModuleFileTopology(
            string schemesPath,
            bool hasZoneTopology,
            Dictionary<string, ModuleFileEntry> canonicalByZoneId,
            Dictionary<string, List<string>> leafZoneIdsByContainerId,
            HashSet<string> containerZoneIds)
        {
            SchemesPath = schemesPath;
            HasZoneTopology = hasZoneTopology;
            _canonicalByZoneId = canonicalByZoneId;
            _leafZoneIdsByContainerId = leafZoneIdsByContainerId;
            _containerZoneIds = containerZoneIds;
        }

        public string SchemesPath { get; }

        public bool HasZoneTopology { get; }

        public static ModuleFileTopology CreateLegacy(string schemesPath)
        {
            return new ModuleFileTopology(
                schemesPath,
                hasZoneTopology: false,
                new Dictionary<string, ModuleFileEntry>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        public IReadOnlyList<ModuleFileEntry> GetExistingCanonicalModuleFiles(IReadOnlyCollection<string>? requestedZoneIds)
        {
            return GetExistingCanonicalModuleFiles(requestedZoneIds, variantId: null);
        }

        /// <summary>
        /// 解析 modules JSON 文件路径列表。
        /// variantId 为空 → 解析 canonical "modules.json"（与单参数重载行为一致）。
        /// variantId 非空 → 解析 "modules-{variantId}.json"，专供 module-relocation-agent 生成的变体使用；
        /// 调用方必须显式指定 requestedZoneIds（不允许全分区扫描变体）。
        /// </summary>
        public IReadOnlyList<ModuleFileEntry> GetExistingCanonicalModuleFiles(
            IReadOnlyCollection<string>? requestedZoneIds,
            string? variantId)
        {
            if (!HasZoneTopology)
                return ModuleFileTopologyService.FindLegacyModuleFiles(SchemesPath);

            if (!string.IsNullOrWhiteSpace(variantId))
            {
                ModuleFileTopologyService.EnsureSafeVariantId(variantId);
                if (requestedZoneIds == null || requestedZoneIds.Count == 0)
                    throw new ArgumentException(
                        "variantId 非空时必须显式指定 requestedZoneIds；不允许全分区变体扫描",
                        nameof(requestedZoneIds));
            }

            var targetZoneIds = ExpandTargetZoneIds(requestedZoneIds);
            var candidates = targetZoneIds == null
                ? _canonicalByZoneId.Values
                : targetZoneIds
                    .Where(zoneId => _canonicalByZoneId.ContainsKey(zoneId))
                    .Select(zoneId => _canonicalByZoneId[zoneId]);

            if (!string.IsNullOrWhiteSpace(variantId))
                candidates = candidates.Select(entry => SwapToVariant(entry, variantId));

            return candidates
                .Where(entry => File.Exists(entry.FilePath))
                .GroupBy(entry => entry.FilePath, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }

        /// <summary>
        /// 同目录下把 canonical entry 替换成指定 variantId 的变体 entry；
        /// 不修改任何元数据（zoneId 保持不变），仅文件名后缀差异。
        /// </summary>
        private ModuleFileEntry SwapToVariant(ModuleFileEntry canonical, string variantId)
        {
            var zoneDir = Path.GetDirectoryName(canonical.FilePath) ?? SchemesPath;
            var variantPath = Path.Combine(
                zoneDir,
                ModuleFileTopologyService.BuildVariantFilename(variantId));
            return ModuleFileEntry.FromFile(SchemesPath, variantPath, canonical.ZoneId);
        }

        public IReadOnlyList<ModuleFilePathIssue> GetPathIssues(IReadOnlyCollection<string>? requestedZoneIds)
        {
            if (!HasZoneTopology || !Directory.Exists(SchemesPath))
                return Array.Empty<ModuleFilePathIssue>();

            var targetZoneIds = ExpandTargetZoneIds(requestedZoneIds);
            var records = Directory.GetFiles(SchemesPath, "modules.json", SearchOption.AllDirectories)
                .Select(path => ModuleFileEntry.FromFile(SchemesPath, path))
                .Where(entry => IsInTarget(entry.ZoneId, targetZoneIds))
                .ToList();

            var issues = new List<ModuleFilePathIssue>();

            foreach (var record in records)
            {
                if (IsCanonical(record))
                    continue;

                issues.Add(ModuleFilePathIssue.InvalidPath(
                    record,
                    GetExpectedPathDescription(record.ZoneId),
                    CountModulesBestEffort(record.FilePath)));
            }

            foreach (var group in records.GroupBy(r => r.ZoneId, StringComparer.OrdinalIgnoreCase))
            {
                if (group.Count() <= 1)
                    continue;

                var expected = GetExpectedPathDescription(group.Key);
                issues.Add(ModuleFilePathIssue.DuplicateFiles(
                    group.Key,
                    group.Select(g => g.RelativePath).ToList(),
                    expected));
            }

            return issues;
        }

        public bool TryResolveZoneDirectory(string zoneId, out string zoneDirectory)
        {
            if (HasZoneTopology && _canonicalByZoneId.TryGetValue(zoneId, out var entry))
            {
                zoneDirectory = Path.GetDirectoryName(entry.FilePath)!;
                return true;
            }

            zoneDirectory = Path.Combine(SchemesPath, zoneId);
            return false;
        }

        public IReadOnlyList<ModuleFileEntry> GetCanonicalEntries()
        {
            return _canonicalByZoneId.Values.ToList();
        }

        private HashSet<string>? ExpandTargetZoneIds(IReadOnlyCollection<string>? requestedZoneIds)
        {
            if (requestedZoneIds == null || requestedZoneIds.Count == 0)
                return null;

            var result = new HashSet<string>(requestedZoneIds.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
            foreach (var zoneId in requestedZoneIds)
            {
                if (zoneId != null && _leafZoneIdsByContainerId.TryGetValue(zoneId, out var leafIds))
                {
                    foreach (var leafId in leafIds)
                        result.Add(leafId);
                }
            }

            return result;
        }

        private static bool IsInTarget(string zoneId, HashSet<string>? targetZoneIds)
        {
            return targetZoneIds == null || targetZoneIds.Contains(zoneId);
        }

        private bool IsCanonical(ModuleFileEntry entry)
        {
            return _canonicalByZoneId.TryGetValue(entry.ZoneId, out var canonical) &&
                   ModuleFileTopologyService.PathsEqual(canonical.FilePath, entry.FilePath);
        }

        private string GetExpectedPathDescription(string zoneId)
        {
            if (_canonicalByZoneId.TryGetValue(zoneId, out var canonical))
                return canonical.RelativePath;

            if (_containerZoneIds.Contains(zoneId))
            {
                var leaves = _leafZoneIdsByContainerId.TryGetValue(zoneId, out var leafIds)
                    ? leafIds
                        .Where(leafId => _canonicalByZoneId.ContainsKey(leafId))
                        .Select(leafId => _canonicalByZoneId[leafId].RelativePath)
                        .ToList()
                    : new List<string>();

                return leaves.Count > 0
                    ? $"容器分区不承载 modules.json；请写入叶子分区：{string.Join(", ", leaves)}"
                    : "容器分区不承载 modules.json";
            }

            return "zones.json 中未定义此叶子分区";
        }

        private static int? CountModulesBestEffort(string filePath)
        {
            try
            {
                var token = JToken.Parse(File.ReadAllText(filePath, Encoding.UTF8));
                return token is JArray array ? array.Count : null;
            }
            catch
            {
                return null;
            }
        }
    }

    public sealed class ModuleFileEntry
    {
        private ModuleFileEntry(string filePath, string relativePath, string zoneId)
        {
            FilePath = filePath;
            RelativePath = relativePath;
            ZoneId = zoneId;
        }

        public string FilePath { get; }

        public string RelativePath { get; }

        public string ZoneId { get; }

        public static ModuleFileEntry FromCanonical(string schemesPath, string zoneId, IReadOnlyList<string> pathSegments)
        {
            var zoneDir = pathSegments.Aggregate(schemesPath, Path.Combine);
            var filePath = Path.Combine(zoneDir, "modules.json");
            return FromFile(schemesPath, filePath, zoneId);
        }

        public static ModuleFileEntry FromFile(string schemesPath, string filePath, string? zoneIdOverride = null)
        {
            var relativePath = Path.GetRelativePath(schemesPath, filePath).Replace('\\', '/');
            var zoneId = zoneIdOverride;
            if (string.IsNullOrWhiteSpace(zoneId))
            {
                zoneId = relativePath.Equals("modules.json", StringComparison.OrdinalIgnoreCase)
                    ? "legacy"
                    : Path.GetFileName(Path.GetDirectoryName(filePath) ?? "");
            }

            return new ModuleFileEntry(filePath, relativePath, zoneId);
        }
    }

    public sealed class ModuleFilePathIssue
    {
        private ModuleFilePathIssue(
            string code,
            string zoneId,
            string actualPath,
            string expectedPath,
            int? moduleCount,
            IReadOnlyList<string> duplicatePaths)
        {
            Code = code;
            ZoneId = zoneId;
            ActualPath = actualPath;
            ExpectedPath = expectedPath;
            ModuleCount = moduleCount;
            DuplicatePaths = duplicatePaths;
        }

        public string Code { get; }

        public string ZoneId { get; }

        public string ActualPath { get; }

        public string ExpectedPath { get; }

        public int? ModuleCount { get; }

        public IReadOnlyList<string> DuplicatePaths { get; }

        public static ModuleFilePathIssue InvalidPath(ModuleFileEntry entry, string expectedPath, int? moduleCount)
        {
            return new ModuleFilePathIssue(
                BIMCanvas.Core.Validation.DiagnosticCodes.InvalidModuleFilePath,
                entry.ZoneId,
                entry.RelativePath,
                expectedPath,
                moduleCount,
                Array.Empty<string>());
        }

        public static ModuleFilePathIssue DuplicateFiles(string zoneId, IReadOnlyList<string> paths, string expectedPath)
        {
            return new ModuleFilePathIssue(
                BIMCanvas.Core.Validation.DiagnosticCodes.DuplicateZoneModuleFiles,
                zoneId,
                string.Join(", ", paths),
                expectedPath,
                null,
                paths);
        }
    }
}
