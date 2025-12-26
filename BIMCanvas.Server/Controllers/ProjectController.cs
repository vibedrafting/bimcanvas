using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BIMCanvas.Core.Models.Computed;
using BIMCanvas.Core.Models.Layout;
using BIMCanvas.Core.Models.Project;
using BIMCanvas.Core.Models.Revit;
using BIMCanvas.Server.Dtos;
using BIMCanvas.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BIMCanvas.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProjectController : ControllerBase
    {
        private readonly ILogger<ProjectController> _logger;
        private readonly ProjectContext _projectContext;
        private readonly ProjectService _projectService;
        private readonly JsonSerializerSettings _jsonSettings;

        public ProjectController(
            ILogger<ProjectController> logger,
            ProjectContext projectContext,
            ProjectService projectService)
        {
            _logger = logger;
            _projectContext = projectContext;
            _projectService = projectService;
            _jsonSettings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            };
        }

        /// <summary>
        /// 获取当前项目数据（单项目模式：无需 path 参数）
        /// </summary>
        /// <returns>聚合后的 ProjectData</returns>
        [HttpGet]
        public ActionResult<ProjectData> GetProjectData()
        {
            if (!_projectContext.IsLoaded)
            {
                return BadRequest(new { message = "没有加载的项目" });
            }

            var path = _projectContext.CurrentProjectPath!;

            if (!Directory.Exists(path))
            {
                return NotFound($"项目目录不存在: {path}");
            }

            try
            {
                _logger.LogInformation("加载项目数据: {Path}", path);
                var projectData = LoadProjectData(path);
                return Ok(projectData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载项目数据失败: {Path}", path);
                return StatusCode(500, new { message = $"加载项目数据失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 获取当前项目状态
        /// </summary>
        [HttpGet("status")]
        public ActionResult GetProjectStatus()
        {
            return Ok(new
            {
                isLoaded = _projectContext.IsLoaded,
                projectPath = _projectContext.CurrentProjectPath,
                sourceBcpPath = _projectContext.SourceBcpPath
            });
        }

        /// <summary>
        /// 打开 BCP 文件（带冲突检测）
        /// </summary>
        [HttpPost("open")]
        public ActionResult<ProjectLoadResult> OpenProject([FromBody] OpenProjectRequest request)
        {
            if (string.IsNullOrEmpty(request.BcpFilePath))
            {
                return BadRequest(new ProjectLoadResult
                {
                    Status = "Error",
                    Message = "BCP 文件路径不能为空"
                });
            }

            if (!System.IO.File.Exists(request.BcpFilePath))
            {
                return NotFound(new ProjectLoadResult
                {
                    Status = "Error",
                    Message = $"BCP 文件不存在: {request.BcpFilePath}"
                });
            }

            // 检测冲突
            var (hasConflict, existingPath) = _projectService.CheckProjectConflict(request.BcpFilePath);
            if (hasConflict)
            {
                return Conflict(new ProjectLoadResult
                {
                    Status = "Conflict",
                    ExistingPath = existingPath,
                    ProjectName = Path.GetFileNameWithoutExtension(request.BcpFilePath),
                    Message = $"项目 '{Path.GetFileNameWithoutExtension(request.BcpFilePath)}' 已存在"
                });
            }

            // 无冲突，直接加载
            try
            {
                var projectPath = _projectService.LoadProject(request.BcpFilePath);
                _projectContext.SetProject(projectPath, request.BcpFilePath);

                return Ok(new ProjectLoadResult
                {
                    Status = "Success",
                    ProjectPath = projectPath
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载项目失败");
                return StatusCode(500, new ProjectLoadResult
                {
                    Status = "Error",
                    Message = ex.Message
                });
            }
        }

        /// <summary>
        /// 解决冲突（覆盖或使用已存在）
        /// </summary>
        [HttpPost("resolve-conflict")]
        public ActionResult<ProjectLoadResult> ResolveConflict([FromBody] ConflictResolutionRequest request)
        {
            if (string.IsNullOrEmpty(request.BcpFilePath))
            {
                return BadRequest(new ProjectLoadResult
                {
                    Status = "Error",
                    Message = "BCP 文件路径不能为空"
                });
            }

            try
            {
                string projectPath;

                if (request.Resolution == "UseExisting")
                {
                    // 使用已存在的项目
                    var bcpFileName = Path.GetFileNameWithoutExtension(request.BcpFilePath);
                    projectPath = Path.Combine(ProjectService.DefaultProjectsRoot, bcpFileName);

                    if (!Directory.Exists(projectPath))
                    {
                        return NotFound(new ProjectLoadResult
                        {
                            Status = "Error",
                            Message = $"项目目录不存在: {projectPath}"
                        });
                    }
                }
                else // Overwrite
                {
                    // 覆盖：删除旧目录并重新解压
                    projectPath = _projectService.LoadProject(request.BcpFilePath, overwrite: true);
                }

                _projectContext.SetProject(projectPath, request.BcpFilePath);

                return Ok(new ProjectLoadResult
                {
                    Status = "Success",
                    ProjectPath = projectPath
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "解决冲突失败");
                return StatusCode(500, new ProjectLoadResult
                {
                    Status = "Error",
                    Message = ex.Message
                });
            }
        }

        /// <summary>
        /// 加载并聚合项目数据
        /// </summary>
        private ProjectData LoadProjectData(string projectPath)
        {
            var data = new ProjectData();

            // 1. 读取 project.json
            var projectJsonPath = Path.Combine(projectPath, "project.json");
            if (System.IO.File.Exists(projectJsonPath))
            {
                data.Project = ReadJson<Project>(projectJsonPath);
            }

            // 2. 读取 baseline 数据
            data.Baseline = LoadBaselineData(projectPath);

            // 3. 读取当前激活策略数据
            if (!string.IsNullOrEmpty(data.Project.ActiveSchemeId))
            {
                data.ActiveScheme = LoadSchemeData(projectPath, data.Project.ActiveSchemeId);
            }

            // 4. 读取 computed 数据
            data.Computed = LoadComputedData(projectPath);

            return data;
        }

        /// <summary>
        /// 加载 Baseline 层数据
        /// </summary>
        private BaselineData LoadBaselineData(string projectPath)
        {
            var baselinePath = Path.Combine(projectPath, "baseline");
            var data = new BaselineData();

            if (!Directory.Exists(baselinePath))
            {
                _logger.LogWarning("baseline 目录不存在: {Path}", baselinePath);
                return data;
            }

            // metadata.json
            var metadataPath = Path.Combine(baselinePath, "metadata.json");
            if (System.IO.File.Exists(metadataPath))
            {
                data.Metadata = ReadJson<BaselineManifest>(metadataPath);
            }

            // architecture.json
            var architecturePath = Path.Combine(baselinePath, "architecture.json");
            if (System.IO.File.Exists(architecturePath))
            {
                var arch = ReadJson<Architecture>(architecturePath);
                data.Walls = arch.Walls ?? new List<Wall>();
                data.Columns = arch.Columns ?? new List<Column>();
            }

            // openings.json
            var openingsPath = Path.Combine(baselinePath, "openings.json");
            if (System.IO.File.Exists(openingsPath))
            {
                data.Openings = ReadJson<List<Opening>>(openingsPath) ?? new List<Opening>();
            }

            // rooms.json
            var roomsPath = Path.Combine(baselinePath, "rooms.json");
            if (System.IO.File.Exists(roomsPath))
            {
                data.Rooms = ReadJson<List<Room>>(roomsPath) ?? new List<Room>();
            }

            // location_lines.json
            var locationLinesPath = Path.Combine(baselinePath, "location_lines.json");
            if (System.IO.File.Exists(locationLinesPath))
            {
                data.LocationLines = ReadJson<List<LocationLine>>(locationLinesPath) ?? new List<LocationLine>();
            }

            _logger.LogDebug("Baseline 数据加载完成: Walls={Walls}, Columns={Columns}, Openings={Openings}, Rooms={Rooms}",
                data.Walls.Count, data.Columns.Count, data.Openings.Count, data.Rooms.Count);

            return data;
        }

        /// <summary>
        /// 加载策略层数据
        /// </summary>
        private SchemeData LoadSchemeData(string projectPath, string schemeId)
        {
            var schemePath = Path.Combine(projectPath, "schemes", schemeId);
            var data = new SchemeData();

            if (!Directory.Exists(schemePath))
            {
                _logger.LogWarning("策略目录不存在: {Path}", schemePath);
                return data;
            }

            // strategy.json
            var strategyPath = Path.Combine(schemePath, "strategy.json");
            if (System.IO.File.Exists(strategyPath))
            {
                data.Strategy = ReadJson<Strategy>(strategyPath);
            }

            // zones.json
            var zonesPath = Path.Combine(schemePath, "zones.json");
            if (System.IO.File.Exists(zonesPath))
            {
                data.Zones = ReadJson<List<Zone>>(zonesPath) ?? new List<Zone>();
            }

            // finishes.json
            var finishesPath = Path.Combine(schemePath, "finishes.json");
            if (System.IO.File.Exists(finishesPath))
            {
                data.Finishes = ReadJson<List<FinishSegment>>(finishesPath) ?? new List<FinishSegment>();
            }

            // modules.json
            var modulesPath = Path.Combine(schemePath, "modules.json");
            if (System.IO.File.Exists(modulesPath))
            {
                data.Modules = ReadJson<List<Module>>(modulesPath) ?? new List<Module>();
            }

            _logger.LogDebug("策略数据加载完成: SchemeId={Id}, Zones={Zones}, Modules={Modules}",
                schemeId, data.Zones.Count, data.Modules.Count);

            return data;
        }

        /// <summary>
        /// 加载 Computed 层数据
        /// </summary>
        private ComputedDataDto LoadComputedData(string projectPath)
        {
            var computedPath = Path.Combine(projectPath, "computed");
            var data = new ComputedDataDto();

            if (!Directory.Exists(computedPath))
            {
                _logger.LogDebug("computed 目录不存在: {Path}", computedPath);
                return data;
            }

            // zones.json (房间区域)
            var zonesPath = Path.Combine(computedPath, "zones.json");
            if (System.IO.File.Exists(zonesPath))
            {
                data.Zones = ReadJson<List<Zone>>(zonesPath) ?? new List<Zone>();
            }

            // exclusions.json (禁区)
            var exclusionsPath = Path.Combine(computedPath, "exclusions.json");
            if (System.IO.File.Exists(exclusionsPath))
            {
                data.Exclusions = ReadJson<List<Zone>>(exclusionsPath) ?? new List<Zone>();
            }

            _logger.LogDebug("Computed 数据加载完成: Zones={ZoneCount}, Exclusions={ExclusionCount}",
                data.Zones.Count, data.Exclusions.Count);

            return data;
        }

        /// <summary>
        /// 读取 JSON 文件并反序列化
        /// </summary>
        private T ReadJson<T>(string path) where T : new()
        {
            var json = System.IO.File.ReadAllText(path, Encoding.UTF8);
            return JsonConvert.DeserializeObject<T>(json, _jsonSettings) ?? new T();
        }
    }
}
