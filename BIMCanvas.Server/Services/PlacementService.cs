using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BIMCanvas.Core.Models.Computed;
using BIMCanvas.Core.Models.Geometry;
using BIMCanvas.Core.Models.Layout;
using BIMCanvas.Core.Models.Shared;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// 布置验证服务
    /// 负责验证 Agent 提交的模块布置数据
    /// </summary>
    public class PlacementService
    {
        private readonly ILogger<PlacementService> _logger;
        private readonly ModulesReaderService _modulesReader;

        public PlacementService(ILogger<PlacementService> logger, ModulesReaderService modulesReader)
        {
            _logger = logger;
            _modulesReader = modulesReader;
        }

        /// <summary>
        /// 验证结果
        /// </summary>
        public class ValidationResult
        {
            /// <summary>
            /// 是否验证通过
            /// </summary>
            public bool IsValid { get; set; }

            /// <summary>
            /// 错误列表
            /// </summary>
            public List<string> Errors { get; set; } = new();

            /// <summary>
            /// 警告列表
            /// </summary>
            public List<string> Warnings { get; set; } = new();
        }

        /// <summary>
        /// 已放置模块数据（从 modules.json 读取）
        /// </summary>
        public class PlacedModule
        {
            /// <summary>
            /// 模块实例 ID
            /// </summary>
            public string Id { get; set; } = string.Empty;

            /// <summary>
            /// 模块库 ID（引用 module_library.json 中的 id）
            /// </summary>
            public string ModuleId { get; set; } = string.Empty;

            /// <summary>
            /// 模块名称
            /// </summary>
            public string ModuleName { get; set; } = string.Empty;

            /// <summary>
            /// 包围盒（4 顶点）
            /// </summary>
            public double[][]? Bounds { get; set; }

            /// <summary>
            /// 朝向
            /// </summary>
            public object? Facing { get; set; }

            /// <summary>
            /// 所属 Zone ID
            /// </summary>
            public string ZoneId { get; set; } = string.Empty;
        }

        /// <summary>
        /// 模块库条目
        /// </summary>
        public class ModuleLibraryEntry
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public List<string> Tags { get; set; } = new();
        }

        /// <summary>
        /// 模块库
        /// </summary>
        public class ModuleLibrary
        {
            public string Version { get; set; } = string.Empty;
            public List<ModuleLibraryEntry> Modules { get; set; } = new();
        }

        /// <summary>
        /// 验证模块布置
        /// </summary>
        /// <param name="projectPath">项目路径</param>
        /// <param name="schemeId">方案 ID</param>
        /// <returns>验证结果</returns>
        public ValidationResult ValidateModules(string projectPath, string schemeId)
        {
            var result = new ValidationResult { IsValid = true };

            // 1. 加载模块库
            var libraryPath = Path.Combine(projectPath, "modules", "module_library.json");
            if (!File.Exists(libraryPath))
            {
                result.IsValid = false;
                result.Errors.Add($"模块库不存在: {libraryPath}");
                return result;
            }

            ModuleLibrary? library;
            try
            {
                var libraryJson = File.ReadAllText(libraryPath, Encoding.UTF8);
                library = JsonConvert.DeserializeObject<ModuleLibrary>(libraryJson);
            }
            catch (JsonException ex)
            {
                result.IsValid = false;
                result.Errors.Add($"模块库解析失败: {ex.Message}");
                return result;
            }

            if (library?.Modules == null || library.Modules.Count == 0)
            {
                result.IsValid = false;
                result.Errors.Add("模块库为空");
                return result;
            }

            // 2. 加载 room_zones.json
            var zonesPath = Path.Combine(projectPath, "computed", "room_zones.json");
            if (!File.Exists(zonesPath))
            {
                result.IsValid = false;
                result.Errors.Add($"房间区域数据不存在: {zonesPath}");
                return result;
            }

            List<Zone>? zones;
            try
            {
                var zonesJson = File.ReadAllText(zonesPath, Encoding.UTF8);
                zones = JsonConvert.DeserializeObject<List<Zone>>(zonesJson);
            }
            catch (JsonException ex)
            {
                result.IsValid = false;
                result.Errors.Add($"房间区域数据解析失败: {ex.Message}");
                return result;
            }

            // 3. 加载已放置模块（指针模型：经拓扑解析到 adopted 方案，不再硬编码 legacy schemes/{schemeId}/modules.json）
            var schemesPath = Path.Combine(projectPath, "schemes");
            var topology = ModuleFileTopologyService.BuildFromSchemesPath(schemesPath);
            var moduleFiles = topology.GetExistingCanonicalModuleFiles(new[] { schemeId });
            if (moduleFiles.Count == 0)
            {
                _logger.LogInformation("方案 {SchemeId} 尚无模块布置", schemeId);
                return result; // 空方案视为有效
            }

            // Phase 0b: 通过 ModulesReaderService 读 wrapper（裸数组会抛错并提示运行迁移脚本）
            var placedModules = new List<PlacedModule>();
            try
            {
                foreach (var mf in moduleFiles)
                {
                    var wrapperModules = _modulesReader.ReadModulesOnly(mf.FilePath);
                    if (wrapperModules == null) continue;
                    placedModules.AddRange(wrapperModules.Select(m => new PlacedModule
                    {
                        Id = m.Id ?? string.Empty,
                        ModuleId = m.ModuleId ?? string.Empty,
                        ModuleName = m.ModuleName ?? string.Empty,
                        Bounds = m.Bounds?.Vertices?.Select(v => new[] { v.X, v.Y }).ToArray(),
                        Facing = m.Facing,
                        ZoneId = m.ZoneId ?? string.Empty
                    }));
                }
            }
            catch (System.Exception ex)
            {
                result.IsValid = false;
                result.Errors.Add($"模块布置数据解析失败: {ex.Message}");
                return result;
            }

            if (placedModules.Count == 0)
            {
                return result; // 空布置视为有效
            }

            // 4. 逐个验证模块
            var libraryDict = library.Modules.ToDictionary(m => m.Id, m => m);
            var zoneDict = zones?.ToDictionary(z => z.Id, z => z) ?? new Dictionary<string, Zone>();

            foreach (var module in placedModules)
            {
                // 4.1 检查 moduleId 有效性
                if (!libraryDict.TryGetValue(module.ModuleId, out var libraryEntry))
                {
                    result.IsValid = false;
                    result.Errors.Add($"模块 {module.Id}: moduleId '{module.ModuleId}' 不存在于模块库中");
                    continue;
                }

                // 4.2 检查 zoneId 有效性
                if (!zoneDict.TryGetValue(module.ZoneId, out var zone))
                {
                    result.IsValid = false;
                    result.Errors.Add($"模块 {module.Id}: zoneId '{module.ZoneId}' 不存在");
                    continue;
                }

                // 4.3 检查标签兼容性（模块的 tags 必须与 zone 的 Tags ∪ OptionalTags 有交集）
                var moduleTags = libraryEntry.Tags.Select(t => t.ToLowerInvariant()).ToHashSet();
                var zoneTags = zone.Tags
                    .Concat(zone.OptionalTags ?? Enumerable.Empty<ZoneTag>())
                    .Select(t => t.ToString().ToLowerInvariant()).ToHashSet();

                if (!moduleTags.Overlaps(zoneTags))
                {
                    result.IsValid = false;
                    result.Errors.Add(
                        $"模块 {module.Id} ({libraryEntry.Name}): 标签不兼容。" +
                        $"模块标签: [{string.Join(", ", libraryEntry.Tags)}], " +
                        $"区域标签: [{string.Join(", ", zone.Tags)}]");
                }

                // 4.4 检查 bounds 有效性
                if (module.Bounds == null || module.Bounds.Length != 4)
                {
                    result.Warnings.Add($"模块 {module.Id}: bounds 格式无效（需要 4 个顶点）");
                }
            }

            // 5. 检查模块间重叠（简化：使用 AABB 检测）
            for (int i = 0; i < placedModules.Count; i++)
            {
                for (int j = i + 1; j < placedModules.Count; j++)
                {
                    var m1 = placedModules[i];
                    var m2 = placedModules[j];

                    if (m1.Bounds != null && m2.Bounds != null && CheckAabbOverlap(m1.Bounds, m2.Bounds))
                    {
                        result.IsValid = false;
                        result.Errors.Add($"模块 {m1.Id} 与模块 {m2.Id} 存在重叠");
                    }
                }
            }

            _logger.LogInformation(
                "验证完成 - 方案: {SchemeId}, 模块数: {Count}, 有效: {IsValid}, 错误数: {ErrorCount}",
                schemeId, placedModules.Count, result.IsValid, result.Errors.Count);

            return result;
        }

        /// <summary>
        /// 检查两个 AABB 是否重叠
        /// </summary>
        private bool CheckAabbOverlap(double[][] bounds1, double[][] bounds2)
        {
            // 计算 AABB
            var (min1, max1) = ComputeAabb(bounds1);
            var (min2, max2) = ComputeAabb(bounds2);

            // 检查分离轴
            return !(max1.X < min2.X || min1.X > max2.X ||
                     max1.Y < min2.Y || min1.Y > max2.Y);
        }

        /// <summary>
        /// 从顶点计算 AABB
        /// </summary>
        private ((double X, double Y) Min, (double X, double Y) Max) ComputeAabb(double[][] vertices)
        {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (var v in vertices)
            {
                if (v.Length >= 2)
                {
                    if (v[0] < minX) minX = v[0];
                    if (v[0] > maxX) maxX = v[0];
                    if (v[1] < minY) minY = v[1];
                    if (v[1] > maxY) maxY = v[1];
                }
            }

            return ((minX, minY), (maxX, maxY));
        }
    }
}
