using System;
using System.Collections.Generic;
using System.IO;
using BIMCanvas.Server.Dtos;
using BIMCanvas.Server.Services;
using BIMCanvas.Server.Services.Plugins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace BIMCanvas.Server.Controllers
{
    /// <summary>
    /// 场景库 API。聚合两类场景来源：
    /// 1. 内置场景：Server 二进制旁的 scenes/index.json（pluginId = "__builtin__"）
    /// 2. Plugin 场景：BIMCANVAS_HOME/plugins/*/scenes/index.json
    /// 约定：任何目录下存在 scenes/index.json 即视为贡献场景。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class ScenesController : ControllerBase
    {
        private const string BuiltinPluginId = "__builtin__";
        private static string BuiltinScenesRoot => Path.Combine(AppContext.BaseDirectory, "scenes");
        private static string BuiltinIndexFile => Path.Combine(BuiltinScenesRoot, "index.json");
        private readonly ILogger<ScenesController> _logger;
        private readonly ProjectService _projectService;
        private readonly ProjectContext _projectContext;
        private readonly RecentProjectsService _recentProjectsService;

        public ScenesController(
            ILogger<ScenesController> logger,
            ProjectService projectService,
            ProjectContext projectContext,
            RecentProjectsService recentProjectsService)
        {
            _logger = logger;
            _projectService = projectService;
            _projectContext = projectContext;
            _recentProjectsService = recentProjectsService;
        }

        // ─── GET /api/scenes ───

        /// <summary>
        /// 聚合所有已安装 plugin 的场景列表。无场景时返回 { available: false }。
        /// </summary>
        [HttpGet]
        public ActionResult<ScenesResponse> GetScenes()
        {
            try
            {
                var scenes = new List<SceneItem>();

                // 1. 内置场景
                LoadScenesFromDirectory(BuiltinScenesRoot, BuiltinPluginId, scenes);

                // 2. Plugin 场景
                var pluginsRoot = PluginPaths.PluginsRoot;
                if (Directory.Exists(pluginsRoot))
                {
                    foreach (var pluginDir in Directory.GetDirectories(pluginsRoot))
                    {
                        var pluginId = Path.GetFileName(pluginDir);
                        if (pluginId.StartsWith(".")) continue;
                        LoadScenesFromDirectory(Path.Combine(pluginDir, "scenes"), pluginId, scenes);
                    }
                }

                return Ok(new ScenesResponse { Available = scenes.Count > 0, Scenes = scenes });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "扫描场景库失败");
                return StatusCode(500, new ScenesResponse { Available = false, Error = ex.Message });
            }
        }

        private void LoadScenesFromDirectory(string scenesRoot, string pluginId, List<SceneItem> scenes)
        {
            var indexFile = Path.Combine(scenesRoot, "index.json");
            if (!System.IO.File.Exists(indexFile)) return;
            try
            {
                var index = JObject.Parse(System.IO.File.ReadAllText(indexFile));
                var scenesArr = index["scenes"] as JArray ?? new JArray();
                foreach (var s in scenesArr)
                {
                    var id = (string?)s["id"] ?? "";
                    string? description = null;
                    List<string>? rooms = null;
                    var metaPath = Path.Combine(scenesRoot, id, "meta.json");
                    if (System.IO.File.Exists(metaPath))
                    {
                        try
                        {
                            var meta = JObject.Parse(System.IO.File.ReadAllText(metaPath));
                            description = (string?)meta["description"];
                            rooms = meta["rooms"]?.ToObject<List<string>>();
                        }
                        catch { }
                    }
                    scenes.Add(new SceneItem
                    {
                        PluginId = pluginId,
                        Id = id,
                        DisplayName = (string?)s["displayName"] ?? id,
                        Tags = s["tags"]?.ToObject<List<string>>() ?? new List<string>(),
                        Area = (int?)s["area"],
                        Description = description,
                        Rooms = rooms,
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "读取 '{PluginId}' 的 scenes/index.json 失败，跳过", pluginId);
            }
        }

        // ─── POST /api/scenes/{pluginId}/{sceneId}/create-project ───

        /// <summary>
        /// 从指定 plugin 的场景创建新项目。返回与 POST /api/project/upload 相同的结构。
        /// </summary>
        [HttpPost("{pluginId}/{sceneId}/create-project")]
        public ActionResult<ProjectLoadResult> CreateProjectFromScene(
            string pluginId,
            string sceneId,
            [FromBody] CreateSceneProjectRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.ProjectName))
                return BadRequest(new ProjectLoadResult { Status = "Error", Message = "项目名不能为空" });

            const string idPattern = @"^[a-z0-9-]+$";
            if (pluginId != BuiltinPluginId &&
                !System.Text.RegularExpressions.Regex.IsMatch(pluginId ?? "", idPattern))
                return BadRequest(new ProjectLoadResult { Status = "Error", Message = $"无效的 plugin ID: {pluginId}" });

            if (!System.Text.RegularExpressions.Regex.IsMatch(sceneId ?? "", idPattern))
                return BadRequest(new ProjectLoadResult { Status = "Error", Message = $"无效的场景 ID: {sceneId}" });

            var scenesRoot = pluginId == BuiltinPluginId
                ? BuiltinScenesRoot
                : Path.Combine(PluginPaths.PluginRoot(pluginId), "scenes");

            if (pluginId != BuiltinPluginId && !Directory.Exists(PluginPaths.PluginRoot(pluginId)))
                return NotFound(new ProjectLoadResult { Status = "Error", Message = $"plugin '{pluginId}' 未安装" });

            var bcpPath = Path.Combine(scenesRoot, sceneId, "scene.bcp");
            if (!System.IO.File.Exists(bcpPath))
                return NotFound(new ProjectLoadResult { Status = "Error", Message = $"场景 '{sceneId}' 不存在" });

            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "BIMCanvas", "scenes");
                Directory.CreateDirectory(tempDir);
                var projectFileName = request.ProjectName.Trim() + ".bcp";
                var tempBcpPath = Path.Combine(tempDir, projectFileName);
                System.IO.File.Copy(bcpPath, tempBcpPath, overwrite: true);

                try
                {
                    var (hasConflict, existingPath) = _projectService.CheckProjectConflict(tempBcpPath);
                    if (hasConflict)
                    {
                        return Conflict(new ProjectLoadResult
                        {
                            Status = "Conflict",
                            ExistingPath = existingPath,
                            ProjectName = request.ProjectName.Trim(),
                            Message = $"项目 '{request.ProjectName.Trim()}' 已存在"
                        });
                    }

                    var loadResult = _projectService.LoadProject(tempBcpPath);
                    _projectContext.SetProject(loadResult.ProjectPath, bcpPath);
                    _recentProjectsService.RecordOpen(request.ProjectName.Trim(), loadResult.ProjectPath);
                    BIMCanvas.Server.Logging.ConversationLogger.Initialize(loadResult.ProjectPath);

                    _logger.LogInformation("从场景 '{PluginId}/{SceneId}' 创建项目成功: {ProjectPath}",
                        pluginId, sceneId, loadResult.ProjectPath);

                    return Ok(new ProjectLoadResult
                    {
                        Status = "Success",
                        ProjectPath = loadResult.ProjectPath,
                        Warnings = loadResult.Warnings.Count > 0 ? loadResult.Warnings : null
                    });
                }
                finally
                {
                    try { System.IO.File.Delete(tempBcpPath); } catch { }
                }
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new ProjectLoadResult { Status = "Error", Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "从场景 '{PluginId}/{SceneId}' 创建项目失败", pluginId, sceneId);
                return StatusCode(500, new ProjectLoadResult { Status = "Error", Message = ex.Message });
            }
        }
    }

    // ─── DTOs ───

    public class ScenesResponse
    {
        public bool Available { get; set; }
        public List<SceneItem>? Scenes { get; set; }
        public string? Error { get; set; }
    }

    public class SceneItem
    {
        public string PluginId { get; set; } = "";
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public List<string> Tags { get; set; } = new();
        public int? Area { get; set; }
        public string? Description { get; set; }
        public List<string>? Rooms { get; set; }
    }

    public class CreateSceneProjectRequest
    {
        public string ProjectName { get; set; } = "";
    }
}
