using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BIMCanvas.Core.Converters.Json;
using BIMCanvas.Core.Models.Computed;
using BIMCanvas.Core.Models.Layout;
using BIMCanvas.Core.Models.Project;
using BIMCanvas.Core.Models.Revit;
using BIMCanvas.Server.Dtos;
using BIMCanvas.Server.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BIMCanvas.Server.Services
{
    public class ProjectSnapshotService
    {
        private readonly ILogger<ProjectSnapshotService> _logger;
        private readonly JsonSerializerSettings _jsonSettings;

        public ProjectSnapshotService(ILogger<ProjectSnapshotService> logger)
        {
            _logger = logger;
            _jsonSettings = new JsonSerializerSettings
            {
                ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
                Converters = { new Polygon2DConverter(), new FacingConverter() }
            };
        }

        public ProjectData LoadProjectData(string projectPath, string? strategyId = null)
        {
            if (!Directory.Exists(projectPath))
            {
                throw new DirectoryNotFoundException($"项目目录不存在: {projectPath}");
            }

            var data = new ProjectData();

            var projectJsonPath = Path.Combine(projectPath, "project.json");
            if (File.Exists(projectJsonPath))
            {
                data.Project = ReadJson<Project>(projectJsonPath);
            }
            else
            {
                throw new FileNotFoundException($"project.json 不存在: {projectJsonPath}");
            }

            if (!string.IsNullOrWhiteSpace(strategyId))
            {
                data.Project.ActiveSchemeId = strategyId;
            }

            data.Baseline = LoadBaselineData(projectPath);

            var schemeId = data.Project.ActiveSchemeId;
            if (!string.IsNullOrWhiteSpace(schemeId))
            {
                data.ActiveScheme = LoadSchemeData(projectPath, schemeId);
            }

            data.Computed = LoadComputedData(projectPath);

            return data;
        }

        private BaselineData LoadBaselineData(string projectPath)
        {
            var baselinePath = Path.Combine(projectPath, "baseline");
            var data = new BaselineData();

            if (!Directory.Exists(baselinePath))
            {
                _logger.LogWarning("baseline 目录不存在: {Path}", baselinePath);
                return data;
            }

            var metadataPath = Path.Combine(baselinePath, "metadata.json");
            if (File.Exists(metadataPath))
            {
                data.Metadata = ReadJson<BaselineManifest>(metadataPath);
            }

            var architecturePath = Path.Combine(baselinePath, "architecture.json");
            if (File.Exists(architecturePath))
            {
                var arch = ReadJson<Architecture>(architecturePath);
                data.Walls = arch.Walls ?? new List<Wall>();
                data.Columns = arch.Columns ?? new List<Column>();
            }

            var openingsPath = Path.Combine(baselinePath, "openings.json");
            if (File.Exists(openingsPath))
            {
                data.Openings = ReadJson<List<Opening>>(openingsPath) ?? new List<Opening>();
            }

            var roomsPath = Path.Combine(baselinePath, "rooms.json");
            if (File.Exists(roomsPath))
            {
                data.Rooms = ReadJson<List<Room>>(roomsPath) ?? new List<Room>();
            }

            var locationLinesPath = Path.Combine(baselinePath, "location_lines.json");
            if (File.Exists(locationLinesPath))
            {
                data.LocationLines = ReadJson<List<LocationLine>>(locationLinesPath) ?? new List<LocationLine>();
            }

            return data;
        }

        private SchemeData LoadSchemeData(string projectPath, string schemeId)
        {
            var schemePath = Path.Combine(projectPath, "schemes");
            var data = new SchemeData();

            if (!Directory.Exists(schemePath))
            {
                _logger.LogWarning("schemes 目录不存在: {Path}", schemePath);
                return data;
            }

            var strategyPath = Path.Combine(schemePath, "strategy.json");
            if (File.Exists(strategyPath))
            {
                data.Strategy = ReadJson<Strategy>(strategyPath);
            }

            var zonesPath = Path.Combine(schemePath, "zones.json");
            if (File.Exists(zonesPath))
            {
                data.Zones = ReadJson<List<Zone>>(zonesPath) ?? new List<Zone>();
            }

            var finishesPath = Path.Combine(schemePath, "finishes.json");
            if (File.Exists(finishesPath))
            {
                data.Finishes = ReadJson<List<FinishSegment>>(finishesPath) ?? new List<FinishSegment>();
            }

            data.Modules = LoadAllZoneModules(schemePath);

            _logger.LogDebug("策略数据加载完成: SchemeId={Id}, Zones={Zones}, Modules={Modules}",
                schemeId, data.Zones.Count, data.Modules.Count);

            return data;
        }

        private List<Module> LoadAllZoneModules(string schemePath)
        {
            var allModules = new List<Module>();

            var leafFiles = ProjectService.FindAllLeafModuleFiles(schemePath);

            // modules.json 自 schemeMetadata wrapper 迁移后(commit 7ade7e8 / b9a36ac)统一为
            // `{schemeMetadata, modules}` 对象格式;此处读 wrapper 取 .Modules 跟 ModulesReaderService /
            // VariantController / ModuleNormalizationService 等所有"主"读路径对齐(避免 List<Module>
            // 强类型把 wrapper 当裸数组反序列化时炸 JsonSerializationException)。
            if (leafFiles.Count > 0)
            {
                foreach (var (filePath, zoneId) in leafFiles)
                {
                    try
                    {
                        var wrapper = ReadJson<ModulesWrapper>(filePath) ?? new ModulesWrapper();
                        foreach (var module in wrapper.Modules)
                        {
                            module.ZoneId ??= zoneId;
                        }
                        allModules.AddRange(wrapper.Modules);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "读取截图模块文件失败: {Path}", filePath);
                    }
                }
            }
            else
            {
                var modulesPath = Path.Combine(schemePath, "modules.json");
                if (File.Exists(modulesPath))
                {
                    var wrapper = ReadJson<ModulesWrapper>(modulesPath) ?? new ModulesWrapper();
                    allModules = wrapper.Modules;
                }
            }

            return allModules;
        }

        private ComputedDataDto LoadComputedData(string projectPath)
        {
            var computedPath = Path.Combine(projectPath, "computed");
            var data = new ComputedDataDto();

            if (!Directory.Exists(computedPath))
            {
                return data;
            }

            var zonesPath = Path.Combine(computedPath, "room_zones.json");
            if (File.Exists(zonesPath))
            {
                data.RoomZones = ReadJson<List<Zone>>(zonesPath) ?? new List<Zone>();
            }

            var exclusionsPath = Path.Combine(computedPath, "exclusions.json");
            if (File.Exists(exclusionsPath))
            {
                data.Exclusions = ReadJson<List<Zone>>(exclusionsPath) ?? new List<Zone>();
            }

            return data;
        }

        private T ReadJson<T>(string path) where T : new()
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            return JsonConvert.DeserializeObject<T>(json, _jsonSettings) ?? new T();
        }
    }
}
