using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BIMCanvas.Core.Converters.Json;
using BIMCanvas.Core.Models.Computed;
using BIMCanvas.Core.Models.Geometry;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// Zone 递归嵌套模型的单一拓扑解析器（唯一主人）。
    ///
    /// 状态机（契约-①，蓝图 §2.2）：走到某节点 zonePath →
    ///   · 存在 {zonePath}/zones.json → 该节点是【容器】（用户给定分区，共享层）：不在本级布置，递归下钻每个子 dz_*；
    ///   · 不存在                     → 该节点是【设计区】（本级跑①）：读 {zonePath}/DESIGN.md 的 adopted slug，
    ///       再看 {zonePath}/{slug}/zones.json 有无 —— 有 = 该方案内部 AI 分区（叶子在 {slug}/{dz}/modules.json）；
    ///       无 = 单叶子方案（{slug}/modules.json）。
    /// 容器/设计区判据**只看 {node}/zones.json 存在与否**，绝不为 rz_/dz_ 写特例。
    ///
    /// 全局 schemes/zones.json 退化为**纯 baseline 房间拓扑**（rz_*，Revit 导出），**不再承载 subZones**——
    /// 所有 subZones（用户给定 / AI 产生）都在 scheme 树内由本解析器按需读取。
    ///
    /// 不回头看：无 legacy / _unzoned / 迁移兜底；无 adopted 的设计区不产 canonical 路径。
    /// </summary>
    public class ModuleFileTopologyService
    {
        private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
        private static readonly StringComparer ZoneComparer = StringComparer.OrdinalIgnoreCase;
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
            Converters = { new Polygon2DConverter(), new Point2DConverter() }
        };

        public ModuleFileTopology Build(string projectPath)
        {
            return BuildFromSchemesPath(Path.Combine(projectPath, "schemes"));
        }

        public static ModuleFileTopology BuildFromSchemesPath(string schemesPath)
        {
            var zonesPath = Path.Combine(schemesPath, "zones.json");
            // 全局 zones.json 缺失 / 空 → 空拓扑（不再 CreateLegacy）。
            var zones = File.Exists(zonesPath)
                ? ReadJson<List<Zone>>(zonesPath) ?? new List<Zone>()
                : new List<Zone>();

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
        /// 带 variantId 静态重载：variantId 为空 → 解析 adopted 当前生效方案；
        /// 非空 → 解析**指定候选 slug 自身的** per-scheme 叶子结构（读该候选自己的 {slug}/zones.json），须显式 requestedZoneIds。
        /// </summary>
        public static IReadOnlyList<ModuleFileEntry> FindExistingCanonicalModuleFiles(
            string schemesPath,
            IReadOnlyCollection<string>? requestedZoneIds,
            string? variantId)
        {
            var topology = BuildFromSchemesPath(schemesPath);
            return topology.GetExistingCanonicalModuleFiles(requestedZoneIds, variantId);
        }

        /// <summary>
        /// 把叶子 zoneId 反查为其设计区祖先**路径**（多段，如 rz_6/dz_客厅）——统一"递归向上找设计区祖先"helper，
        /// 收口原 SchemeDataService / ProjectController 各自内嵌的 segments[0] 单段反推（二者逐字相同）。
        /// 容器嵌套叶子也正确：rz_6/dz_客厅/{slug}/dz_1 的祖先 = rz_6/dz_客厅（非 rz_6）。
        /// </summary>
        public static string ResolveDesignZoneIdForLeaf(string schemesPath, string leafZoneId)
        {
            if (string.IsNullOrWhiteSpace(leafZoneId))
                return leafZoneId;

            var topology = BuildFromSchemesPath(schemesPath);
            return topology.ResolveDesignZoneId(leafZoneId);
        }

        /// <summary>
        /// 构建叶子分区的 modules JSON 文件名。variantId 为空 → canonical "modules.json"；
        /// 非空 → "modules-{variantId}.json"。
        /// 【残留登记】仅服务于场景④ module-relocation 的变体文件命名（MVP 不迁移、当前无调用方）；
        /// 不属递归拓扑 legacy，§2.7-6 嘱勿擅删场景④读路径，保留待场景④立项时处置。
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

        private static T? ReadJson<T>(string path)
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            return JsonConvert.DeserializeObject<T>(json, JsonSettings);
        }

        /// <summary>
        /// 枚举某设计区某 slug（adopted 或显式候选 variantId）**自身**的叶子 modules 文件路径。
        /// 必补-1：候选叶子结构永远读该 slug 自己的 {designZonePath}/{slug}/zones.json，绝不复用 adopted 叶子集
        /// （分区思维候选得 2+ 叶子、线性思维候选得单文件，各按自身结构），否则 2 叶子候选被当单文件 → 验证静默漏检。
        /// </summary>
        internal static IEnumerable<ModuleFileEntry> EnumerateSchemeLeaves(
            string schemesPath, string designZonePath, string slug)
        {
            var schemeDir = CombineSegments(schemesPath, designZonePath, slug);
            var schemeZonesJson = Path.Combine(schemeDir, "zones.json");
            if (File.Exists(schemeZonesJson))
            {
                var leafZones = ReadJson<List<Zone>>(schemeZonesJson) ?? new List<Zone>();
                foreach (var lz in leafZones)
                {
                    if (string.IsNullOrWhiteSpace(lz.Id))
                        continue;
                    yield return ModuleFileEntry.FromFile(
                        schemesPath, Path.Combine(schemeDir, lz.Id, "modules.json"), lz.Id);
                }
            }
            else
            {
                var selfLeaf = LastSegment(designZonePath);
                yield return ModuleFileEntry.FromFile(
                    schemesPath, Path.Combine(schemeDir, "modules.json"), selfLeaf);
            }
        }

        internal static string CombineSegments(string root, params string[] multiSegmentParts)
        {
            var result = root;
            foreach (var part in multiSegmentParts)
            {
                foreach (var seg in (part ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries))
                    result = Path.Combine(result, seg);
            }
            return result;
        }

        internal static string LastSegment(string zonePath)
        {
            var segs = (zonePath ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
            return segs.Length > 0 ? segs[^1] : (zonePath ?? string.Empty);
        }

        private sealed class TopologyBuilder
        {
            private readonly string _schemesPath;
            private readonly List<Zone> _zones;
            private readonly Dictionary<string, ModuleFileEntry> _canonicalByZoneId = new Dictionary<string, ModuleFileEntry>(ZoneComparer);
            // 节点路径（设计区 or 容器，多段）→ 其当前生效叶子 id 集
            private readonly Dictionary<string, List<string>> _leafZoneIdsByNode = new Dictionary<string, List<string>>(ZoneComparer);
            private readonly HashSet<string> _containerZoneIds = new HashSet<string>(ZoneComparer);   // 容器节点路径集
            private readonly HashSet<string> _designZoneIds = new HashSet<string>(ZoneComparer);      // 设计区节点路径集
            // 叶子 id → 其设计区祖先路径（多段）；供统一祖先 helper + resolvedLeaves
            private readonly Dictionary<string, string> _designZoneIdByLeafId = new Dictionary<string, string>(ZoneComparer);
            // 叶子 id → RawBoundary（供 GetLeafGeometrySource；几何源，叉口-1 P1 只出 RawBoundary）
            private readonly Dictionary<string, Polygon2D?> _rawBoundaryByLeafId = new Dictionary<string, Polygon2D?>(ZoneComparer);
            private readonly List<ResolvedLeaf> _resolvedLeaves = new List<ResolvedLeaf>();
            // 一次 Build 内每设计区 adopted slug 只读一次 DESIGN.md
            private readonly Dictionary<string, string?> _adoptedSlugCache = new Dictionary<string, string?>(ZoneComparer);

            public TopologyBuilder(string schemesPath, List<Zone> zones)
            {
                _schemesPath = schemesPath;
                _zones = zones;
            }

            public ModuleFileTopology Build()
            {
                // 全局 zones.json 仅取 rz_* baseline 顶层 zone 作递归入口（不再读其 subZones）。
                foreach (var zone in _zones)
                {
                    if (string.IsNullOrWhiteSpace(zone.Id))
                        continue;
                    Walk(zone.Id, zone);
                }

                return new ModuleFileTopology(
                    _schemesPath,
                    _canonicalByZoneId,
                    _leafZoneIdsByNode,
                    _containerZoneIds,
                    _designZoneIds,
                    _designZoneIdByLeafId,
                    _rawBoundaryByLeafId,
                    _resolvedLeaves);
            }

            /// <summary>递归状态机：解析节点 zonePath（多段）；node 携带该节点的 RawBoundary（单叶子设计区几何源）。</summary>
            private List<string> Walk(string zonePath, Zone node)
            {
                var nodeDir = CombineSegments(_schemesPath, zonePath);
                var nodeZonesJson = Path.Combine(nodeDir, "zones.json");

                if (File.Exists(nodeZonesJson))
                {
                    // ── 容器（用户给定分区，共享层）：递归下钻每个子 dz_*
                    _containerZoneIds.Add(zonePath);
                    var children = ReadJson<List<Zone>>(nodeZonesJson) ?? new List<Zone>();
                    var leaves = new List<string>();
                    foreach (var child in children)
                    {
                        if (string.IsNullOrWhiteSpace(child.Id))
                            continue;
                        leaves.AddRange(Walk(zonePath + "/" + child.Id, child));
                    }
                    _leafZoneIdsByNode[zonePath] = leaves;
                    return leaves;
                }

                // ── 设计区（本级跑①）
                _designZoneIds.Add(zonePath);
                var slug = ResolveAdoptedCached(zonePath);
                if (string.IsNullOrEmpty(slug))
                {
                    // 无 adopted：登记设计区为单叶子占位（叶子=自身），不产 canonical 路径（不回落 legacy）。
                    var selfLeaf = LastSegment(zonePath);
                    _designZoneIdByLeafId[selfLeaf] = zonePath;
                    _rawBoundaryByLeafId[selfLeaf] = node.RawBoundary;
                    var only = new List<string> { selfLeaf };
                    _leafZoneIdsByNode[zonePath] = only;
                    return only;
                }

                var resolved = RegisterSchemeLeaves(zonePath, node, slug);
                _leafZoneIdsByNode[zonePath] = resolved;
                return resolved;
            }

            /// <summary>读该设计区 adopted slug 自身的 {slug}/zones.json，登记叶子（canonical 路径 + 祖先 + 几何）。</summary>
            private List<string> RegisterSchemeLeaves(string zonePath, Zone node, string slug)
            {
                var schemeDir = CombineSegments(_schemesPath, zonePath, slug);
                var schemeZonesJson = Path.Combine(schemeDir, "zones.json");
                var ids = new List<string>();

                if (File.Exists(schemeZonesJson))
                {
                    // 该方案内部 AI 分区：叶子在 {slug}/{dz}/modules.json，几何取各叶子自身 RawBoundary
                    var leafZones = ReadJson<List<Zone>>(schemeZonesJson) ?? new List<Zone>();
                    foreach (var lz in leafZones)
                    {
                        if (string.IsNullOrWhiteSpace(lz.Id))
                            continue;
                        RegisterLeaf(lz.Id, zonePath, Path.Combine(schemeDir, lz.Id, "modules.json"), lz.RawBoundary);
                        ids.Add(lz.Id);
                    }
                }
                else
                {
                    // 单叶子方案：{slug}/modules.json，叶子=设计区自身，几何取设计区 RawBoundary
                    var selfLeaf = LastSegment(zonePath);
                    RegisterLeaf(selfLeaf, zonePath, Path.Combine(schemeDir, "modules.json"), node.RawBoundary);
                    ids.Add(selfLeaf);
                }

                return ids;
            }

            private void RegisterLeaf(string leafId, string designZonePath, string absModulesPath, Polygon2D? raw)
            {
                if (!_canonicalByZoneId.ContainsKey(leafId))
                    _canonicalByZoneId[leafId] = ModuleFileEntry.FromFile(_schemesPath, absModulesPath, leafId);

                _designZoneIdByLeafId[leafId] = designZonePath;
                _rawBoundaryByLeafId[leafId] = raw;
                _resolvedLeaves.Add(new ResolvedLeaf(
                    leafId, _canonicalByZoneId[leafId].RelativePath, designZonePath, isContainer: false));
            }

            private string? ResolveAdoptedCached(string designZonePath)
            {
                if (!_adoptedSlugCache.TryGetValue(designZonePath, out var slug))
                {
                    slug = SchemeDesignDocService.ResolveAdoptedSlug(_schemesPath, designZonePath);
                    _adoptedSlugCache[designZonePath] = slug;
                }
                return slug;
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
        private readonly Dictionary<string, List<string>> _leafZoneIdsByNode;
        private readonly HashSet<string> _containerZoneIds;
        private readonly HashSet<string> _designZoneIds;
        private readonly Dictionary<string, string> _designZoneIdByLeafId;
        private readonly Dictionary<string, Polygon2D?> _rawBoundaryByLeafId;
        private readonly List<ResolvedLeaf> _resolvedLeaves;

        internal ModuleFileTopology(
            string schemesPath,
            Dictionary<string, ModuleFileEntry> canonicalByZoneId,
            Dictionary<string, List<string>> leafZoneIdsByNode,
            HashSet<string> containerZoneIds,
            HashSet<string> designZoneIds,
            Dictionary<string, string> designZoneIdByLeafId,
            Dictionary<string, Polygon2D?> rawBoundaryByLeafId,
            List<ResolvedLeaf> resolvedLeaves)
        {
            SchemesPath = schemesPath;
            _canonicalByZoneId = canonicalByZoneId;
            _leafZoneIdsByNode = leafZoneIdsByNode;
            _containerZoneIds = containerZoneIds;
            _designZoneIds = designZoneIds;
            _designZoneIdByLeafId = designZoneIdByLeafId;
            _rawBoundaryByLeafId = rawBoundaryByLeafId;
            _resolvedLeaves = resolvedLeaves;
        }

        public string SchemesPath { get; }

        /// <summary>
        /// 判定 zonePath 是否为设计区（本级跑①的节点）。容器返回 false。
        /// MVP 顶层设计区单段（rz_3）；递归容器子设计区多段（rz_6/dz_客厅）。
        /// </summary>
        public bool IsDesignZoneId(string zoneId)
        {
            return !string.IsNullOrWhiteSpace(zoneId) && _designZoneIds.Contains(zoneId);
        }

        /// <summary>
        /// 列出设计区/容器节点下当前生效（adopted）叶子分区 id。
        /// 设计区单叶子 → [自身]；设计区 AI 分区 → [dz_1..n]；容器 → 其全部子设计区叶子。
        /// 无 adopted 的设计区 → [自身]（占位，无 modules 文件）。未知节点 → 空。
        /// </summary>
        public IReadOnlyList<string> GetLeafZoneIds(string designZoneId)
        {
            if (string.IsNullOrWhiteSpace(designZoneId))
                return Array.Empty<string>();

            if (_leafZoneIdsByNode.TryGetValue(designZoneId, out var leaves))
                return leaves;

            return Array.Empty<string>();
        }

        public IReadOnlyList<ModuleFileEntry> GetExistingCanonicalModuleFiles(IReadOnlyCollection<string>? requestedZoneIds)
        {
            return GetExistingCanonicalModuleFiles(requestedZoneIds, variantId: null);
        }

        /// <summary>
        /// 解析 modules JSON 文件路径列表。
        /// variantId 为空 → adopted 当前生效方案（_canonicalByZoneId）。
        /// variantId 非空 → 按**该候选 slug 自身**的 per-scheme zones.json 枚举叶子（必补-1，废弃 SwapToVariant 式换段复用）；
        /// 须显式 requestedZoneIds（设计区路径），不允许全分区扫描候选。
        /// </summary>
        public IReadOnlyList<ModuleFileEntry> GetExistingCanonicalModuleFiles(
            IReadOnlyCollection<string>? requestedZoneIds,
            string? variantId)
        {
            if (!string.IsNullOrWhiteSpace(variantId))
            {
                ModuleFileTopologyService.EnsureSafeVariantId(variantId);
                if (requestedZoneIds == null || requestedZoneIds.Count == 0)
                    throw new ArgumentException(
                        "variantId 非空时必须显式指定 requestedZoneIds（设计区路径）；不允许全分区候选扫描",
                        nameof(requestedZoneIds));

                return requestedZoneIds
                    .Where(dz => !string.IsNullOrWhiteSpace(dz))
                    .SelectMany(dz => ModuleFileTopologyService.EnumerateSchemeLeaves(SchemesPath, dz, variantId!))
                    .Where(entry => File.Exists(entry.FilePath))
                    .GroupBy(entry => entry.FilePath, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();
            }

            var targetZoneIds = ExpandTargetZoneIds(requestedZoneIds);
            var candidates = targetZoneIds == null
                ? _canonicalByZoneId.Values
                : targetZoneIds
                    .Where(zoneId => _canonicalByZoneId.ContainsKey(zoneId))
                    .Select(zoneId => _canonicalByZoneId[zoneId]);

            return candidates
                .Where(entry => File.Exists(entry.FilePath))
                .GroupBy(entry => entry.FilePath, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }

        /// <summary>
        /// 契约-② resolvedLeaves 视图（纯文件映射 + pathIssues，§6-3 去冗余：不含几何）。
        /// variantId 非空 → 按该候选自身分区结构产出（P3 采纳前逐个验证候选 _cand-x 时传，必补-1）。
        /// 几何（computedBoundary/exclusionZones）由 P3 在 ValidationController 合并（叉口-1 裁定）。
        /// </summary>
        public ResolvedTopologyView GetResolvedLeaves(
            IReadOnlyCollection<string>? requestedZoneIds,
            string? variantId = null)
        {
            List<ResolvedLeaf> leaves;
            if (!string.IsNullOrWhiteSpace(variantId))
            {
                ModuleFileTopologyService.EnsureSafeVariantId(variantId);
                if (requestedZoneIds == null || requestedZoneIds.Count == 0)
                    throw new ArgumentException(
                        "variantId 非空时必须显式指定 requestedZoneIds（设计区路径）",
                        nameof(requestedZoneIds));

                leaves = requestedZoneIds
                    .Where(dz => !string.IsNullOrWhiteSpace(dz))
                    .SelectMany(dz => ModuleFileTopologyService.EnumerateSchemeLeaves(SchemesPath, dz, variantId!)
                        .Select(entry => new ResolvedLeaf(entry.ZoneId, entry.RelativePath, dz, isContainer: false)))
                    .ToList();
            }
            else
            {
                var targetZoneIds = ExpandTargetZoneIds(requestedZoneIds);
                leaves = targetZoneIds == null
                    ? _resolvedLeaves.ToList()
                    : _resolvedLeaves.Where(l => targetZoneIds.Contains(l.LeafZoneId)).ToList();
            }

            return new ResolvedTopologyView(leaves, GetPathIssues(requestedZoneIds, variantId));
        }

        /// <summary>
        /// 叶子几何源（§2.2 / 叉口-1：P1 只出叶子 RawBoundary 源 + sibling）。
        /// 供 P3 passage 派生与 zoneGeometry 富化（computedBoundary/exclusionZones 由 P3 合并）。
        /// 入参 = 设计区/容器节点路径；返回该节点下当前生效叶子集，各带 RawBoundary 与同节点 sibling。
        /// </summary>
        public IReadOnlyList<LeafGeometry> GetLeafGeometrySource(string zonePath)
        {
            if (string.IsNullOrWhiteSpace(zonePath) || !_leafZoneIdsByNode.TryGetValue(zonePath, out var leaves))
                return Array.Empty<LeafGeometry>();

            return leaves.Select(leafId => new LeafGeometry(
                leafId,
                _designZoneIdByLeafId.TryGetValue(leafId, out var dz) ? dz : zonePath,
                _rawBoundaryByLeafId.TryGetValue(leafId, out var rb) ? rb : null,
                leaves.Where(other => !ZoneEquals(other, leafId)).ToList())).ToList();
        }

        /// <summary>
        /// 叶子 zoneId → 设计区祖先路径（多段）。未登记则回退 leafZoneId 本身。
        /// 统一"递归向上找设计区祖先"helper（收口 SchemeDataService / ProjectController 重复实现）。
        /// </summary>
        public string ResolveDesignZoneId(string leafZoneId)
        {
            if (!string.IsNullOrWhiteSpace(leafZoneId)
                && _designZoneIdByLeafId.TryGetValue(leafZoneId, out var designZonePath))
                return designZonePath;
            return leafZoneId;
        }

        public IReadOnlyList<ModuleFilePathIssue> GetPathIssues(IReadOnlyCollection<string>? requestedZoneIds)
        {
            return GetPathIssues(requestedZoneIds, variantId: null);
        }

        /// <summary>
        /// 路径完整性校验（E013/E014）。盯防-3 判据（递归模型，不再靠 FromFile 派生 zoneId 判候选）：
        ///   合法 modules.json = {designZonePath}/{slug}/modules.json 或 {designZonePath}/{slug}/{leaf}/modules.json
        ///   （slug 任意——adopted 或隐藏候选 _cand-x 都合法；候选另行按 variantId 验证）。
        ///   非法 E013 = 设计区直落 {dz}/modules.json（缺 slug 层 legacy-spot）/ schemes 根直落 / 容器直落 / 层级过深。
        /// 设计区前缀靠 _designZoneIds（已知设计区路径集）匹配，故 _cand-x 与 {slug}/{leaf} 嵌套不会被误报。
        /// E014（重复）在干净递归模型下结构性不可能（同 canonical 路径物理唯一、跨候选非重复），不主动扫描。
        /// </summary>
        public IReadOnlyList<ModuleFilePathIssue> GetPathIssues(
            IReadOnlyCollection<string>? requestedZoneIds, string? variantId)
        {
            if (!Directory.Exists(SchemesPath))
                return Array.Empty<ModuleFilePathIssue>();

            var requested = (requestedZoneIds == null || requestedZoneIds.Count == 0)
                ? null
                : new HashSet<string>(requestedZoneIds.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);

            var issues = new List<ModuleFilePathIssue>();
            foreach (var file in Directory.GetFiles(SchemesPath, "modules.json", SearchOption.AllDirectories))
            {
                var dirRel = Path.GetRelativePath(SchemesPath, Path.GetDirectoryName(file) ?? SchemesPath).Replace('\\', '/');
                var dirSegs = dirRel == "." ? Array.Empty<string>() : dirRel.Split('/', StringSplitOptions.RemoveEmptyEntries);

                var (valid, designZonePath) = ClassifyModulesLocation(dirSegs);
                if (valid)
                    continue;

                // requestedZoneIds 过滤：只报落在请求设计区子树下的问题
                if (requested != null && !dirSegs.Any(seg => requested.Contains(seg)) &&
                    (designZonePath == null || !requested.Contains(designZonePath)))
                    continue;

                var entry = ModuleFileEntry.FromFile(SchemesPath, file);
                issues.Add(ModuleFilePathIssue.InvalidPath(
                    entry,
                    GetExpectedPathDescription(designZonePath, dirSegs),
                    CountModulesBestEffort(file)));
            }

            return issues;
        }

        /// <summary>
        /// 分类某 modules.json 所在目录段：是否合法 + 命中的设计区路径（如有）。
        /// 合法 = 命中设计区前缀 P，余下为 [slug] 或 [slug,leaf]。
        /// </summary>
        private (bool valid, string? designZonePath) ClassifyModulesLocation(string[] dirSegs)
        {
            // 找最长的、属于已知设计区的前缀
            for (var len = dirSegs.Length; len >= 1; len--)
            {
                var prefix = string.Join("/", dirSegs.Take(len));
                if (_designZoneIds.Contains(prefix))
                {
                    var remainder = dirSegs.Length - len; // slug=1，slug/leaf=2
                    return (remainder == 1 || remainder == 2, prefix);
                }
            }
            return (false, null);
        }

        public bool TryResolveZoneDirectory(string zoneId, out string zoneDirectory)
        {
            if (_canonicalByZoneId.TryGetValue(zoneId, out var entry))
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
                // 容器/设计区 → 展开为其叶子（叶子才是 canonical key）
                if (zoneId != null && _leafZoneIdsByNode.TryGetValue(zoneId, out var leafIds))
                {
                    foreach (var leafId in leafIds)
                        result.Add(leafId);
                }
            }

            return result;
        }

        private string GetExpectedPathDescription(string? designZonePath, string[] dirSegs)
        {
            if (designZonePath != null && _containerZoneIds.Contains(designZonePath))
                return "容器分区不承载 modules.json；请写入其叶子设计区的方案目录";

            if (dirSegs.Length == 0)
                return "schemes 根不承载 modules.json；请写入 {designZone}/{slug}/[{leaf}/]modules.json";

            var dz = designZonePath ?? dirSegs[0];
            return $"应位于方案目录：schemes/{dz}/{{slug}}/[{{leaf}}/]modules.json（缺 slug 层或层级不符）";
        }

        private static bool ZoneEquals(string a, string b)
            => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static int? CountModulesBestEffort(string filePath)
        {
            // 仅认 wrapper {schemeMetadata, modules}；裸数组返回 null（不再支持）
            try
            {
                var token = JToken.Parse(File.ReadAllText(filePath, Encoding.UTF8));
                if (token is JObject obj && obj["modules"] is JArray inner)
                    return inner.Count;
                return null;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>契约-② resolvedLeaves 单元：纯文件映射（§6-3 去冗余，不含几何）。</summary>
    public sealed class ResolvedLeaf
    {
        public ResolvedLeaf(string leafZoneId, string modulesPath, string designZoneId, bool isContainer)
        {
            LeafZoneId = leafZoneId;
            ModulesPath = modulesPath;
            DesignZoneId = designZoneId;
            IsContainer = isContainer;
        }

        public string LeafZoneId { get; }
        /// <summary>相对 schemes、posix（/ 分隔）。</summary>
        public string ModulesPath { get; }
        /// <summary>设计区路径（多段，如 rz_6/dz_客厅）。</summary>
        public string DesignZoneId { get; }
        public bool IsContainer { get; }
    }

    /// <summary>契约-② 注入视图：resolvedLeaves[] + pathIssues[]（供 P3 序列化进 validator stdin 请求）。</summary>
    public sealed class ResolvedTopologyView
    {
        public ResolvedTopologyView(IReadOnlyList<ResolvedLeaf> resolvedLeaves, IReadOnlyList<ModuleFilePathIssue> pathIssues)
        {
            ResolvedLeaves = resolvedLeaves;
            PathIssues = pathIssues;
        }

        public IReadOnlyList<ResolvedLeaf> ResolvedLeaves { get; }
        public IReadOnlyList<ModuleFilePathIssue> PathIssues { get; }
    }

    /// <summary>叶子几何源（叉口-1：P1 只出 RawBoundary 源 + sibling；computedBoundary/exclusions 由 P3 合并）。</summary>
    public sealed class LeafGeometry
    {
        public LeafGeometry(string leafZoneId, string designZoneId, Polygon2D? rawBoundary, IReadOnlyList<string> siblingLeafIds)
        {
            LeafZoneId = leafZoneId;
            DesignZoneId = designZoneId;
            RawBoundary = rawBoundary;
            SiblingLeafIds = siblingLeafIds;
        }

        public string LeafZoneId { get; }
        public string DesignZoneId { get; }
        public Polygon2D? RawBoundary { get; }
        public IReadOnlyList<string> SiblingLeafIds { get; }
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

        public static ModuleFileEntry FromFile(string schemesPath, string filePath, string? zoneIdOverride = null)
        {
            var relativePath = Path.GetRelativePath(schemesPath, filePath).Replace('\\', '/');
            var zoneId = zoneIdOverride;
            if (string.IsNullOrWhiteSpace(zoneId))
            {
                zoneId = Path.GetFileName(Path.GetDirectoryName(filePath) ?? "");
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
