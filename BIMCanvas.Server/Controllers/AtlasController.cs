using System;
using System.Collections.Generic;
using System.IO;
using BIMCanvas.Server.Dtos;
using BIMCanvas.Server.Services;
using BIMCanvas.Server.Services.Plugins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BIMCanvas.Server.Controllers
{
    /// <summary>
    /// Atlas plugin 场景库 API。
    /// Atlas 是可插拔内容扩展（BIMCANVAS_HOME/plugins/atlas/），未安装时接口优雅降级。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class AtlasController : ControllerBase
    {
        private const string AtlasPluginId = "atlas";

        private readonly ILogger<AtlasController> _logger;
        private readonly ProjectService _projectService;
        private readonly ProjectContext _projectContext;
        private readonly RecentProjectsService _recentProjectsService;

        public AtlasController(
            ILogger<AtlasController> logger,
            ProjectService projectService,
            ProjectContext projectContext,
            RecentProjectsService recentProjectsService)
        {
            _logger = logger;
            _projectService = projectService;
            _projectContext = projectContext;
            _recentProjectsService = recentProjectsService;
        }

        // ─── helpers ───

        private string AtlasRoot => PluginPaths.PluginRoot(AtlasPluginId);
        private string AtlasScenesRoot => Path.Combine(AtlasRoot, "scenes");
        private string AtlasIndexFile => Path.Combine(AtlasScenesRoot, "index.json");

        private bool IsAtlasInstalled() => System.IO.File.Exists(AtlasIndexFile);

        // ─── GET /api/atlas/scenes ───

        /// <summary>
        /// 返回 atlas 场景列表。未安装 atlas 时返回 { available: false }。
        /// </summary>
        [HttpGet("scenes")]
        public ActionResult<AtlasScenesResponse> GetScenes()
        {
            if (!IsAtlasInstalled())
            {
                _logger.LogDebug("Atlas plugin 未安装，返回 available:false");
                return Ok(new AtlasScenesResponse { Available = false });
            }

            try
            {
                var indexJson = System.IO.File.ReadAllText(AtlasIndexFile);
                var index = JObject.Parse(indexJson);
                var scenesArr = index["scenes"] as JArray ?? new JArray();

                var scenes = new List<AtlasSceneItem>();
                foreach (var s in scenesArr)
                {
                    var id = (string?)s["id"] ?? "";
                    var metaPath = Path.Combine(AtlasScenesRoot, id, "meta.json");

                    // 读取 meta.json（若存在）补充详情
                    string? description = null;
                    List<string>? rooms = null;
                    if (System.IO.File.Exists(metaPath))
                    {
                        try
                        {
                            var meta = JObject.Parse(System.IO.File.ReadAllText(metaPath));
                            description = (string?)meta["description"];
                            rooms = meta["rooms"]?.ToObject<List<string>>();
                        }
                        catch { /* meta 读取失败不阻断列表 */ }
                    }

                    scenes.Add(new AtlasSceneItem
                    {
                        Id = id,
                        DisplayName = (string?)s["displayName"] ?? id,
                        Tags = s["tags"]?.ToObject<List<string>>() ?? new List<string>(),
                        Area = (int?)s["area"],
                        Description = description,
                        Rooms = rooms,
                    });
                }

                return Ok(new AtlasScenesResponse
                {
                    Available = true,
                    Scenes = scenes
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "读取 atlas index.json 失败");
                return StatusCode(500, new AtlasScenesResponse
                {
                    Available = false,
                    Error = ex.Message
                });
            }
        }

        // ─── POST /api/atlas/scenes/{sceneId}/create-project ───

        /// <summary>
        /// 从 atlas 场景创建新项目。复用 ProjectService.LoadProject() 逻辑，
        /// 返回与 POST /api/project/upload 相同的 ProjectLoadResult 结构，
        /// 前端可复用现有冲突对话框 + 健康检查流程。
        /// </summary>
        [HttpPost("scenes/{sceneId}/create-project")]
        public ActionResult<ProjectLoadResult> CreateProjectFromScene(
            string sceneId,
            [FromBody] CreateAtlasProjectRequest request)
        {
            if (!IsAtlasInstalled())
            {
                return NotFound(new ProjectLoadResult
                {
                    Status = "Error",
                    Message = "Atlas plugin 未安装"
                });
            }

            if (string.IsNullOrWhiteSpace(request?.ProjectName))
            {
                return BadRequest(new ProjectLoadResult
                {
                    Status = "Error",
                    Message = "项目名不能为空"
                });
            }

            // 校验 sceneId 格式（与 index.json 中一致的 [a-z0-9-]+ 约定）
            if (string.IsNullOrWhiteSpace(sceneId) ||
                !System.Text.RegularExpressions.Regex.IsMatch(sceneId, @"^[a-z0-9-]+$"))
            {
                return BadRequest(new ProjectLoadResult
                {
                    Status = "Error",
                    Message = $"无效的场景 ID: {sceneId}"
                });
            }

            var bcpPath = Path.Combine(AtlasScenesRoot, sceneId, "scene.bcp");
            if (!System.IO.File.Exists(bcpPath))
            {
                return NotFound(new ProjectLoadResult
                {
                    Status = "Error",
                    Message = $"场景 '{sceneId}' 不存在"
                });
            }

            try
            {
                // 检测同名冲突（以用户指定的项目名为准，而非 .bcp 文件名）
                // 先把 .bcp 临时拷贝为用户期望的项目名，再走冲突检测
                var tempDir = Path.Combine(Path.GetTempPath(), "BIMCanvas", "atlas");
                Directory.CreateDirectory(tempDir);
                var projectFileName = request.ProjectName.Trim() + ".bcp";
                var tempBcpPath = Path.Combine(tempDir, projectFileName);
                System.IO.File.Copy(bcpPath, tempBcpPath, overwrite: true);

                try
                {
                    // 冲突检测
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

                    // 加载项目
                    var loadResult = _projectService.LoadProject(tempBcpPath);
                    _projectContext.SetProject(loadResult.ProjectPath, bcpPath);

                    // 记录最近打开
                    _recentProjectsService.RecordOpen(request.ProjectName.Trim(), loadResult.ProjectPath);

                    // 初始化对话日志
                    BIMCanvas.Server.Logging.ConversationLogger.Initialize(loadResult.ProjectPath);

                    _logger.LogInformation("从 atlas 场景 '{SceneId}' 创建项目成功: {ProjectPath}",
                        sceneId, loadResult.ProjectPath);

                    return Ok(new ProjectLoadResult
                    {
                        Status = "Success",
                        ProjectPath = loadResult.ProjectPath,
                        Warnings = loadResult.Warnings.Count > 0 ? loadResult.Warnings : null
                    });
                }
                finally
                {
                    // 清理临时文件
                    try { System.IO.File.Delete(tempBcpPath); } catch { }
                }
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new ProjectLoadResult
                {
                    Status = "Error",
                    Message = ex.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "从 atlas 场景 '{SceneId}' 创建项目失败", sceneId);
                return StatusCode(500, new ProjectLoadResult
                {
                    Status = "Error",
                    Message = ex.Message
                });
            }
        }
    }

    // ─── DTOs ───

    public class AtlasScenesResponse
    {
        public bool Available { get; set; }
        public List<AtlasSceneItem>? Scenes { get; set; }
        public string? Error { get; set; }
    }

    public class AtlasSceneItem
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public List<string> Tags { get; set; } = new();
        public int? Area { get; set; }
        public string? Description { get; set; }
        public List<string>? Rooms { get; set; }
    }

    public class CreateAtlasProjectRequest
    {
        public string ProjectName { get; set; } = "";
    }
}
