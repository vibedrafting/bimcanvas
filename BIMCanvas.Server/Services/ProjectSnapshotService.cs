using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BIMCanvas.Core.Converters.Json;
using BIMCanvas.Core.Models.Computed;
using BIMCanvas.Core.Models.Layout;
using BIMCanvas.Core.Models.Project;
using BIMCanvas.Core.Models.Revit;
using BIMCanvas.Server.Dtos;
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
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
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

            var zoneDirs = Directory.GetDirectories(schemePath)
                .Where(d =>
                {
                    var name = Path.GetFileName(d);
                    return name.StartsWith("rz_") || name.StartsWith("dz_");
                })
                .ToList();

            if (zoneDirs.Count > 0)
            {
                foreach (var zoneDir in zoneDirs)
                {
                    var modulesPath = Path.Combine(zoneDir, "modules.json");
                    if (File.Exists(modulesPath))
                    {
                        var modules = ReadJson<List<Module>>(modulesPath) ?? new List<Module>();
                        allModules.AddRange(modules);
                    }
                }
            }
            else
            {
                var modulesPath = Path.Combine(schemePath, "modules.json");
                if (File.Exists(modulesPath))
                {
                    allModules = ReadJson<List<Module>>(modulesPath) ?? new List<Module>();
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
