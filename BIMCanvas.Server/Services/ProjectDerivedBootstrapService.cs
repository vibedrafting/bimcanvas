using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BIMCanvas.Core.Models.Project;
using BIMCanvas.Core.Services;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
                ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
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
                // Path topology is derived from zones.json, including subZones that are
                // represented as { id } references to top-level zone definitions.
                var topology = ModuleFileTopologyService.BuildFromSchemesPath(schemesPath);

                var createdCount = 0;
                foreach (var entry in topology.GetCanonicalEntries())
                {
                    var zoneDir = Path.GetDirectoryName(entry.FilePath);
                    if (string.IsNullOrEmpty(zoneDir))
                        continue;

                    if (!Directory.Exists(zoneDir))
                        Directory.CreateDirectory(zoneDir);

                    if (!File.Exists(entry.FilePath))
                        File.WriteAllText(entry.FilePath, "[]", Encoding.UTF8);

                    createdCount++;
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

        /// <summary>
        /// 用 JObject patch 写回 project.json:只 mutate BIMCanvas 拥有字段,
        /// 任何未知字段 (包括 plugin / 第三方扩展、嵌套对象未知字段、数组中未知元素)
        /// 原样保留。
        ///
        /// 主真理源 v1.1 §3.9 + §4.5 字段所有权清单:
        ///   平台拥有 (本服务负责): id / name / version / createdAt / updatedAt /
        ///                          coordinateSystem / activeSchemeId / schemes
        ///   平台拥有 (本服务不操作): scenes —— 由组2 端点 POST /api/project/{id}/scenes
        ///                            管理,本服务对 scenes 一律透传不动。
        ///   第三方扩展: 任何未列出字段都原样保留。
        ///
        /// 卡点 F (主真理源 §2.4):取代旧 <see cref="JsonConvert.DeserializeObject{Project}"/>
        /// 整对象 round-trip 路径,后者会静默抹除未知字段。
        /// </summary>
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
            JObject root;

            if (File.Exists(projectJsonPath))
            {
                try
                {
                    var json = File.ReadAllText(projectJsonPath, Encoding.UTF8);
                    root = JObject.Parse(json);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "project.json 解析失败，将按默认结构重建: {Path}", projectJsonPath);
                    root = new JObject();
                    shouldWrite = true;
                }
            }
            else
            {
                root = new JObject();
            }

            if (string.IsNullOrWhiteSpace((string?)root["id"]))
            {
                root["id"] = $"proj_{projectName}";
                shouldWrite = true;
            }

            if (string.IsNullOrWhiteSpace((string?)root["name"]))
            {
                root["name"] = projectName;
                shouldWrite = true;
            }

            if (string.IsNullOrWhiteSpace((string?)root["version"]))
            {
                root["version"] = "3.0";
                shouldWrite = true;
            }

            if (string.IsNullOrWhiteSpace((string?)root["coordinateSystem"]))
            {
                root["coordinateSystem"] = "cartesian_mm_yUp";
                shouldWrite = true;
            }

            var createdAtToken = root["createdAt"];
            if (createdAtToken == null || createdAtToken.Type == JTokenType.Null)
            {
                root["createdAt"] = JToken.FromObject(DateTime.Now, JsonSerializer.Create(_jsonSettings));
                shouldWrite = true;
            }

            var currentSchemes = root["schemes"] as JArray;
            if (!SchemeRefsEqualToJArray(currentSchemes, desiredSchemes))
            {
                root["schemes"] = JArray.FromObject(desiredSchemes, JsonSerializer.Create(_jsonSettings));
                shouldWrite = true;
            }

            var currentActiveSchemeId = (string?)root["activeSchemeId"];
            if (!string.Equals(currentActiveSchemeId, activeStrategyId, StringComparison.Ordinal))
            {
                root["activeSchemeId"] = activeStrategyId;
                shouldWrite = true;
            }

            if (!shouldWrite)
            {
                return;
            }

            root["updatedAt"] = JToken.FromObject(DateTime.Now, JsonSerializer.Create(_jsonSettings));

            var ordered = ReorderProjectRoot(root);
            var updatedJson = ordered.ToString(Formatting.Indented);
            File.WriteAllText(projectJsonPath, updatedJson, Encoding.UTF8);
            _logger.LogInformation("更新 project.json: ActiveSchemeId = {Id}, Schemes.Count = {Count}",
                activeStrategyId, desiredSchemes.Count);
        }

        /// <summary>
        /// 写出时把 BIMCanvas 拥有字段按稳定顺序前置,所有未知字段保持原相对顺序追加于后。
        /// diff-friendly + 保留未知字段。字段顺序与 .bcp Schema 文档(docs/bcp-schema-v3.5.md)对齐。
        /// </summary>
        private static JObject ReorderProjectRoot(JObject root)
        {
            string[] ownedOrder =
            {
                "id", "name", "version", "createdAt", "updatedAt",
                "coordinateSystem", "activeSchemeId", "schemes", "scenes"
            };
            var ordered = new JObject();
            foreach (var key in ownedOrder)
            {
                if (root.TryGetValue(key, out var token))
                {
                    ordered[key] = token;
                }
            }
            foreach (var prop in root.Properties())
            {
                if (!ordered.ContainsKey(prop.Name))
                {
                    ordered[prop.Name] = prop.Value;
                }
            }
            return ordered;
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

        /// <summary>
        /// JArray 形态 schemes 与目标 List&lt;SchemeRef&gt; 等价判断 —— 用于
        /// JObject patch 路径下,避免不必要的 schemes 写入触发 file change。
        /// 任何 schemes 数组元素中超出 id/path/name 的扩展字段也会被 JObject 原样保留,
        /// 此处只对 BIMCanvas 拥有的三个字段做相等性比较。
        /// </summary>
        private static bool SchemeRefsEqualToJArray(JArray? left, IReadOnlyList<SchemeRef> right)
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
                if (left[i] is not JObject leftItem)
                {
                    return false;
                }

                var rightItem = right[i];
                if (!string.Equals((string?)leftItem["id"], rightItem.Id, StringComparison.Ordinal) ||
                    !string.Equals((string?)leftItem["path"], rightItem.Path, StringComparison.Ordinal) ||
                    !string.Equals((string?)leftItem["name"], rightItem.Name, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
