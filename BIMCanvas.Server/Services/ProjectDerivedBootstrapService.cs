using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BIMCanvas.Core.Models.Computed;
using BIMCanvas.Core.Models.Project;
using BIMCanvas.Core.Services;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// 项目条件派生初始化。
    /// 根据项目实际状态生成/补齐 baseline、schemes、computed、zones、git 等派生产物。
    /// </summary>
    public sealed class ProjectDerivedBootstrapService
    {
        public sealed class BootstrapResult
        {
            public bool WasComputedRegenerated { get; init; }
            public bool WasComputedRegeneratedBecauseBaselineChanged { get; init; }
            public bool ZonesExistedBeforeBootstrap { get; init; }
        }

        private sealed class ComputedDataEnsureState
        {
            public bool WasRegenerated { get; init; }
            public bool WasRegeneratedBecauseBaselineChanged { get; init; }
        }

        private readonly ILogger<ProjectDerivedBootstrapService> _logger;
        private readonly ManifestService _manifestService;
        private readonly StrategyService _strategyService;
        private readonly ComputedDataService _computedDataService;
        private readonly GitWorktreeService _gitService;
        private readonly JsonSerializerSettings _jsonSettings;

        public ProjectDerivedBootstrapService(
            ILogger<ProjectDerivedBootstrapService> logger,
            ManifestService manifestService,
            StrategyService strategyService,
            ComputedDataService computedDataService,
            GitWorktreeService gitService)
        {
            _logger = logger;
            _manifestService = manifestService;
            _strategyService = strategyService;
            _computedDataService = computedDataService;
            _gitService = gitService;
            _jsonSettings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Formatting = Formatting.Indented
            };
        }

        public BootstrapResult EnsureInitialized(string projectPath, bool refreshProjectMetadata)
        {
            var zonesExistedBeforeBootstrap = File.Exists(Path.Combine(projectPath, "schemes", "zones.json"));

            var baselineHash = EnsureBaselineManifest(projectPath);
            var activeStrategyId = EnsureSchemesDirectory(projectPath, baselineHash);
            EnsureProjectJson(projectPath, activeStrategyId, refreshProjectMetadata);

            var computedState = EnsureComputedData(projectPath);
            EnsureZonesInitializedFromComputed(projectPath);
            RefreshZoneDirectories(projectPath);
            InitializeGitRepository(projectPath);

            return new BootstrapResult
            {
                WasComputedRegenerated = computedState.WasRegenerated,
                WasComputedRegeneratedBecauseBaselineChanged = computedState.WasRegeneratedBecauseBaselineChanged,
                ZonesExistedBeforeBootstrap = zonesExistedBeforeBootstrap
            };
        }

        /// <summary>
        /// 缺失时从 computed/room_zones.json 初始化 schemes/zones.json。
        /// 一旦 schemes/zones.json 已存在，就保留现有分区设计，不再覆盖。
        /// </summary>
        public void EnsureZonesInitializedFromComputed(string projectPath)
        {
            var roomZonesPath = Path.Combine(projectPath, "computed", "room_zones.json");
            var zonesPath = Path.Combine(projectPath, "schemes", "zones.json");
            var zonesDir = Path.GetDirectoryName(zonesPath);

            if (!string.IsNullOrEmpty(zonesDir))
            {
                Directory.CreateDirectory(zonesDir);
            }

            if (File.Exists(zonesPath))
            {
                _logger.LogDebug("schemes/zones.json 已存在，保留现有分区设计，跳过初始分区初始化");
                return;
            }

            if (!File.Exists(roomZonesPath))
            {
                File.WriteAllText(zonesPath, "[]", Encoding.UTF8);
                _logger.LogWarning("computed/room_zones.json 不存在，创建空的 schemes/zones.json");
                return;
            }

            var roomZonesJson = File.ReadAllText(roomZonesPath, Encoding.UTF8);
            File.WriteAllText(zonesPath, roomZonesJson, Encoding.UTF8);

            _logger.LogInformation("schemes/zones.json 缺失，已从 computed/room_zones.json 初始化");
        }

        /// <summary>
        /// 基于 schemes/zones.json 创建或刷新分区子目录。
        /// </summary>
        public void RefreshZoneDirectories(string projectPath)
        {
            var zonesPath = Path.Combine(projectPath, "schemes", "zones.json");
            var schemesPath = Path.Combine(projectPath, "schemes");

            if (!File.Exists(zonesPath))
            {
                _logger.LogWarning("schemes/zones.json 不存在，跳过分区目录创建");
                return;
            }

            try
            {
                var zonesJson = File.ReadAllText(zonesPath, Encoding.UTF8);
                var zones = JsonConvert.DeserializeObject<List<Zone>>(zonesJson) ?? new List<Zone>();

                var createdCount = 0;
                foreach (var zone in zones)
                {
                    if (string.IsNullOrEmpty(zone.Id))
                    {
                        continue;
                    }

                    createdCount += CreateZoneDirectory(schemesPath, zone.Id, zone);
                }

                _logger.LogInformation("创建/刷新了 {Count} 个分区目录", createdCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建分区目录失败");
            }
        }

        private string EnsureBaselineManifest(string projectPath)
        {
            var baselinePath = Path.Combine(projectPath, "baseline");

            if (!Directory.Exists(baselinePath))
            {
                throw new DirectoryNotFoundException($"baseline 目录不存在: {baselinePath}");
            }

            var existingHash = _manifestService.GetBaselineHash(baselinePath);
            if (!string.IsNullOrEmpty(existingHash))
            {
                _logger.LogDebug("baseline.manifest 已存在，hash = {Hash}", existingHash);
                return existingHash;
            }

            _logger.LogInformation("计算 baseline 哈希...");
            var hash = BaselineHashService.ComputeBaselineHash(baselinePath);
            _logger.LogInformation("Baseline Hash: {Hash}", hash);

            _manifestService.WriteBaselineManifest(baselinePath, hash);
            return hash;
        }

        private string EnsureSchemesDirectory(string projectPath, string baselineHash)
        {
            var schemesPath = Path.Combine(projectPath, "schemes");

            if (!Directory.Exists(schemesPath))
            {
                Directory.CreateDirectory(schemesPath);
                _logger.LogInformation("创建 schemes/ 目录");
            }

            var existingStrategies = _strategyService.GetAllStrategyIds(schemesPath);
            if (existingStrategies.Count > 0)
            {
                _logger.LogDebug("已存在 {Count} 个策略，跳过默认策略创建", existingStrategies.Count);
                return existingStrategies[0];
            }

            return _strategyService.CreateDefaultStrategy(schemesPath, baselineHash);
        }

        private void EnsureProjectJson(string projectPath, string activeStrategyId, bool refreshProjectMetadata)
        {
            var projectJsonPath = Path.Combine(projectPath, "project.json");
            var projectName = Path.GetFileName(projectPath);
            var schemesPath = Path.Combine(projectPath, "schemes");
            var strategyIds = _strategyService.GetAllStrategyIds(schemesPath);
            var desiredSchemes = strategyIds
                .Select(id => new SchemeRef
                {
                    Id = id,
                    Path = "./schemes",
                    Name = id.Contains("_", StringComparison.Ordinal)
                        ? id.Substring(id.IndexOf('_') + 1)
                        : id
                })
                .ToList();

            var shouldWrite = refreshProjectMetadata || !File.Exists(projectJsonPath);
            Project project;

            if (File.Exists(projectJsonPath))
            {
                try
                {
                    var json = File.ReadAllText(projectJsonPath, Encoding.UTF8);
                    project = JsonConvert.DeserializeObject<Project>(json) ?? new Project();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "project.json 解析失败，将按默认结构重建: {Path}", projectJsonPath);
                    project = new Project();
                    shouldWrite = true;
                }
            }
            else
            {
                project = new Project();
            }

            if (string.IsNullOrWhiteSpace(project.Id))
            {
                project.Id = $"proj_{projectName}";
                shouldWrite = true;
            }

            if (string.IsNullOrWhiteSpace(project.Name))
            {
                project.Name = projectName;
                shouldWrite = true;
            }

            if (string.IsNullOrWhiteSpace(project.Version))
            {
                project.Version = "3.0";
                shouldWrite = true;
            }

            if (string.IsNullOrWhiteSpace(project.CoordinateSystem))
            {
                project.CoordinateSystem = "cartesian_mm_yUp";
                shouldWrite = true;
            }

            if (project.CreatedAt == default)
            {
                project.CreatedAt = DateTime.Now;
                shouldWrite = true;
            }

            if (!SchemeRefsEqual(project.Schemes, desiredSchemes))
            {
                project.Schemes = desiredSchemes;
                shouldWrite = true;
            }

            if (!string.Equals(project.ActiveSchemeId, activeStrategyId, StringComparison.Ordinal))
            {
                project.ActiveSchemeId = activeStrategyId;
                shouldWrite = true;
            }

            if (!shouldWrite)
            {
                return;
            }

            project.UpdatedAt = DateTime.Now;

            var updatedJson = JsonConvert.SerializeObject(project, _jsonSettings);
            File.WriteAllText(projectJsonPath, updatedJson, Encoding.UTF8);
            _logger.LogInformation("更新 project.json: ActiveSchemeId = {Id}, Schemes.Count = {Count}",
                activeStrategyId, project.Schemes?.Count ?? 0);
        }

        private ComputedDataEnsureState EnsureComputedData(string projectPath)
        {
            var validation = _computedDataService.AnalyzeComputedData(projectPath);
            if (validation.IsValid)
            {
                _logger.LogDebug("computed 数据有效，跳过生成");
                return new ComputedDataEnsureState();
            }

            _computedDataService.GenerateComputedData(projectPath);
            return new ComputedDataEnsureState
            {
                WasRegenerated = true,
                WasRegeneratedBecauseBaselineChanged = validation.BaselineHashChanged
            };
        }

        private void InitializeGitRepository(string projectPath)
        {
            try
            {
                var initialized = _gitService.InitializeRepository(projectPath);
                if (initialized)
                {
                    _logger.LogInformation("Git 仓库初始化完成（单仓库 + 多分支架构）");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Git 仓库初始化失败（非致命错误）");
            }
        }

        private int CreateZoneDirectory(string parentDir, string zoneId, Zone zone)
        {
            var zoneDir = Path.Combine(parentDir, zoneId);
            var count = 0;

            if (zone.SubZones != null && zone.SubZones.Count > 0)
            {
                Directory.CreateDirectory(zoneDir);
                _logger.LogDebug("创建容器分区目录: {ZoneId}", zoneId);

                foreach (var subZone in zone.SubZones)
                {
                    if (!string.IsNullOrEmpty(subZone.Id))
                    {
                        count += CreateZoneDirectory(zoneDir, subZone.Id, subZone);
                    }
                }
            }
            else
            {
                if (!Directory.Exists(zoneDir))
                {
                    Directory.CreateDirectory(zoneDir);
                }

                var modulesPath = Path.Combine(zoneDir, "modules.json");
                if (!File.Exists(modulesPath))
                {
                    File.WriteAllText(modulesPath, "[]", Encoding.UTF8);
                }

                count++;
                _logger.LogDebug("创建叶子分区目录: {ZoneId}", zoneId);
            }

            return count;
        }

        private static bool SchemeRefsEqual(IReadOnlyList<SchemeRef>? left, IReadOnlyList<SchemeRef> right)
        {
            if (left == null)
            {
                return right.Count == 0;
            }

            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                var leftItem = left[i];
                var rightItem = right[i];
                if (!string.Equals(leftItem.Id, rightItem.Id, StringComparison.Ordinal) ||
                    !string.Equals(leftItem.Path, rightItem.Path, StringComparison.Ordinal) ||
                    !string.Equals(leftItem.Name, rightItem.Name, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
