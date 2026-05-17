using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BIMCanvas.Server.Services;
using BIMCanvas.Server.Services.Plugins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BIMCanvas.Server.Controllers
{
    /// <summary>
    /// (组5 §5.B.6 / 主真理源 §3.10) 跨 scene 只读 artifact 访问端点。
    /// <para>
    /// 路由:<c>GET /api/scheme/scenes/{sceneId}/{artifactKind}</c>。
    /// </para>
    /// <para>
    /// 调用方:Agent 端 <c>mcp__canvas__load_scene_artifact</c> 工具(canvas_core.py L121-180)。
    /// 工具通过 HTTP session 调本端点,把 artifact 整文件文本返回给 LLM。
    /// </para>
    /// <para>
    /// <b>语义</b>:
    /// <list type="bullet">
    /// <item><c>sceneId</c>:目标 scene 的唯一 id(允许 <c>== activeSceneId</c>,plugin 作者无需区分自己 vs 他人)</item>
    /// <item><c>artifactKind</c>:枚举 modules / zones / semantic_plan / reference_analysis / readme</item>
    /// <item>只读端点,**不**走 V12b 写入 gate</item>
    /// <item>Phase 1 整文件读,不支持 path 子路径(主真理源 §3.10)</item>
    /// </list>
    /// </para>
    /// </summary>
    [ApiController]
    [Route("api/scheme/scenes/{sceneId}")]
    public class SceneArtifactsController : ControllerBase
    {
        private readonly ILogger<SceneArtifactsController> _logger;
        private readonly ProjectContext _projectContext;

        public SceneArtifactsController(
            ILogger<SceneArtifactsController> logger,
            ProjectContext projectContext)
        {
            _logger = logger;
            _projectContext = projectContext;
        }

        [HttpGet("{artifactKind}")]
        public IActionResult GetSceneArtifact(string sceneId, string artifactKind)
        {
            if (string.IsNullOrWhiteSpace(sceneId))
            {
                return BadRequest(new { code = "invalid_scene_id", message = "sceneId 不能为空" });
            }

            if (!_projectContext.IsLoaded || string.IsNullOrWhiteSpace(_projectContext.CurrentProjectPath))
            {
                return StatusCode(404, new { code = "no_project_loaded", message = "未加载项目" });
            }

            var projectPath = _projectContext.GetActiveWorktreePath() ?? _projectContext.CurrentProjectPath!;

            var kind = (artifactKind ?? string.Empty).Trim().ToLowerInvariant();
            switch (kind)
            {
                case "modules":
                    return ReadModulesAggregated(projectPath, sceneId);
                case "zones":
                    return ReadZones(projectPath);
                case "semantic_plan":
                    return ReadAggregatedJsonFiles(projectPath, sceneId, "semantic_plan.json");
                case "reference_analysis":
                    return ReadAggregatedJsonFiles(projectPath, sceneId, "reference_analysis.json");
                case "readme":
                    return ReadReadme(projectPath);
                default:
                    return BadRequest(new
                    {
                        code = "invalid_artifact_kind",
                        message = "artifactKind 必须是 modules / zones / semantic_plan / reference_analysis / readme 之一",
                        receivedValue = artifactKind ?? string.Empty,
                    });
            }
        }

        // ---------- modules:聚合返回 schemes/{sceneId}/ 下所有叶子 modules.json ----------

        private IActionResult ReadModulesAggregated(string projectPath, string sceneId)
        {
            var sceneSchemesRoot = PluginPaths.SceneSchemesRoot(projectPath, sceneId);
            if (!Directory.Exists(sceneSchemesRoot))
            {
                // legacy 兼容:M2 路径未生效时回退到旧路径 schemes/{zoneId}/modules.json
                // (查找全 scene 共享的 schemes/ 下叶子 modules.json)
                sceneSchemesRoot = Path.Combine(projectPath, "schemes");
                if (!Directory.Exists(sceneSchemesRoot))
                {
                    return StatusCode(404, new { code = "artifact_not_found", message = "schemes 目录不存在", sceneId, artifactKind = "modules" });
                }
            }

            var modulesFiles = Directory.GetFiles(sceneSchemesRoot, "modules.json", SearchOption.AllDirectories);
            var aggregated = new List<object>();
            foreach (var file in modulesFiles)
            {
                try
                {
                    var content = System.IO.File.ReadAllText(file, Encoding.UTF8);
                    var relativeFromScene = Path.GetRelativePath(sceneSchemesRoot, file).Replace('\\', '/');
                    aggregated.Add(new { relativePath = relativeFromScene, content });
                }
                catch (Exception exc)
                {
                    _logger.LogWarning(exc, "读取 modules.json 失败: {File}", file);
                }
            }

            return Ok(new { sceneId, artifactKind = "modules", files = aggregated });
        }

        // ---------- zones:schemes/zones.json 全 scene 共享 ----------

        private IActionResult ReadZones(string projectPath)
        {
            var zonesPath = Path.Combine(projectPath, "schemes", "zones.json");
            if (!System.IO.File.Exists(zonesPath))
            {
                return StatusCode(404, new { code = "artifact_not_found", message = "schemes/zones.json 不存在", artifactKind = "zones" });
            }

            try
            {
                var content = System.IO.File.ReadAllText(zonesPath, Encoding.UTF8);
                return Content(content, "application/json", Encoding.UTF8);
            }
            catch (Exception exc)
            {
                _logger.LogError(exc, "读取 zones.json 失败");
                return StatusCode(500, new { code = "read_failed", message = exc.Message });
            }
        }

        // ---------- semantic_plan / reference_analysis:聚合 schemes/{sceneId}/{designZoneId}/<fileName> ----------

        private IActionResult ReadAggregatedJsonFiles(string projectPath, string sceneId, string fileName)
        {
            var sceneSchemesRoot = PluginPaths.SceneSchemesRoot(projectPath, sceneId);
            if (!Directory.Exists(sceneSchemesRoot))
            {
                // legacy 兼容:回退到旧路径(全 scene 共享 schemes/)
                sceneSchemesRoot = Path.Combine(projectPath, "schemes");
                if (!Directory.Exists(sceneSchemesRoot))
                {
                    return StatusCode(404, new { code = "artifact_not_found", message = "schemes 目录不存在", sceneId, artifactKind = fileName.Replace(".json", "") });
                }
            }

            var matches = Directory.GetFiles(sceneSchemesRoot, fileName, SearchOption.AllDirectories);
            var aggregated = new List<object>();
            foreach (var file in matches)
            {
                try
                {
                    var content = System.IO.File.ReadAllText(file, Encoding.UTF8);
                    var relativeFromScene = Path.GetRelativePath(sceneSchemesRoot, file).Replace('\\', '/');
                    aggregated.Add(new { relativePath = relativeFromScene, content });
                }
                catch (Exception exc)
                {
                    _logger.LogWarning(exc, "读取 {FileName} 失败: {File}", fileName, file);
                }
            }

            if (aggregated.Count == 0)
            {
                return StatusCode(404, new
                {
                    code = "artifact_not_found",
                    message = $"未找到 {fileName}",
                    sceneId,
                    artifactKind = fileName.Replace(".json", ""),
                });
            }

            return Ok(new
            {
                sceneId,
                artifactKind = fileName.Replace(".json", ""),
                files = aggregated,
            });
        }

        // ---------- readme:项目根 README.md(平台级 baseline) ----------

        private IActionResult ReadReadme(string projectPath)
        {
            var readmePath = Path.Combine(projectPath, "README.md");
            if (!System.IO.File.Exists(readmePath))
            {
                return StatusCode(404, new { code = "artifact_not_found", message = "项目根 README.md 不存在", artifactKind = "readme" });
            }

            try
            {
                var content = System.IO.File.ReadAllText(readmePath, Encoding.UTF8);
                return Content(content, "text/markdown", Encoding.UTF8);
            }
            catch (Exception exc)
            {
                _logger.LogError(exc, "读取 README.md 失败");
                return StatusCode(500, new { code = "read_failed", message = exc.Message });
            }
        }
    }
}
