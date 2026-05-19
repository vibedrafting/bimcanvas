using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BIMCanvas.Server.Hubs;
using BIMCanvas.Server.Services;
using BIMCanvas.Server.Services.Plugins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BIMCanvas.Server.Controllers
{
    /// <summary>
    /// 跨 scene 通用 artifact IO 端点 (主真理源 v1.1 §3.10 + Server 业务下沉派单纲领 §4.1)。
    /// <para>
    /// 路由前缀 <c>api/scheme/scenes/{sceneId}</c>:
    /// <list type="bullet">
    /// <item><c>GET  /{artifactKind}</c>:聚合读 scene 下所有同名 artifact(向后兼容)</item>
    /// <item><c>GET  /{artifactKind}?path={subPath}</c>:精确读单文件 <c>schemes/{sceneId}/{subPath}/{artifactKind}.json</c></item>
    /// <item><c>POST /artifacts/{artifactKind}</c> body <c>{ path?, content }</c>:写到 <c>schemes/{sceneId}/{path?}/{artifactKind}.json</c></item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>artifactKind 校验</b>:字符集 <c>^[a-z][a-z0-9_-]*$</c>(plugin-agnostic,不再 enum 硬编码 domain 词)。
    /// <b>Reserved 通用 kind</b>(<c>modules</c> / <c>zones</c> / <c>readme</c>):走原有专用 reader,**不允许 POST 写**(modules 走 AI 直写 Write/Edit;zones 是 baseline 派生;readme 是 baseline)。
    /// </para>
    /// <para>
    /// <b>POST 安全防线</b>:(1) artifactKind / path 字符集 + 防 ".." 穿越;
    /// (2) <see cref="ProjectContext.CheckWriteAllowed(string)"/> V12b 路径隔离,scene namespace 之外一律 403;
    /// (3) 最终绝对路径必须落在 <c>schemes/{sceneId}/</c> 内。
    /// </para>
    /// <para>
    /// <b>SignalR 通用事件</b> <c>SceneArtifactUpdated</c>:payload <c>{sceneId, artifactKind, path?, plugin?, timestamp}</c>。
    /// 双轨期内 SemanticPlanController 旧端点仍广播 <c>SemanticPlanUpdated</c> / <c>ReferenceAnalysisUpdated</c>,删旧 controller 时一并清理。
    /// </para>
    /// </summary>
    [ApiController]
    [Route("api/scheme/scenes/{sceneId}")]
    public class SceneArtifactsController : ControllerBase
    {
        private static readonly Regex ArtifactKindPattern = new(@"^[a-z][a-z0-9_-]*$", RegexOptions.Compiled);
        private static readonly Regex PathSegmentPattern = new(@"^[a-zA-Z0-9_/-]+$", RegexOptions.Compiled);

        // Reserved 通用 kind:不允许 plugin 通过 POST 直写。
        // modules 走 AI Write/Edit 直写;zones 是全 scene 共享 baseline 派生;readme 是 baseline。
        private static readonly HashSet<string> ReservedKinds = new(StringComparer.OrdinalIgnoreCase)
        {
            "modules", "zones", "readme"
        };

        private readonly ILogger<SceneArtifactsController> _logger;
        private readonly ProjectContext _projectContext;
        private readonly IHubContext<CanvasHub> _hubContext;

        public SceneArtifactsController(
            ILogger<SceneArtifactsController> logger,
            ProjectContext projectContext,
            IHubContext<CanvasHub> hubContext)
        {
            _logger = logger;
            _projectContext = projectContext;
            _hubContext = hubContext;
        }

        // ============================================================
        // GET — 读 artifact
        // ============================================================

        [HttpGet("{artifactKind}")]
        public IActionResult GetSceneArtifact(string sceneId, string artifactKind, [FromQuery] string? path = null)
        {
            if (string.IsNullOrWhiteSpace(sceneId))
                return BadRequest(new { code = "invalid_scene_id", message = "sceneId 不能为空" });

            if (!_projectContext.IsLoaded || string.IsNullOrWhiteSpace(_projectContext.CurrentProjectPath))
                return StatusCode(404, new { code = "no_project_loaded", message = "未加载项目" });

            var kind = (artifactKind ?? string.Empty).Trim().ToLowerInvariant();
            if (!ArtifactKindPattern.IsMatch(kind))
            {
                return BadRequest(new
                {
                    code = "invalid_artifact_kind",
                    message = "artifactKind 必须匹配 ^[a-z][a-z0-9_-]*$",
                    receivedValue = artifactKind ?? string.Empty,
                });
            }

            var projectPath = _projectContext.GetActiveWorktreePath() ?? _projectContext.CurrentProjectPath!;

            // 精确读:path 非空时 → 读单文件 schemes/{sceneId}/{path}/{artifactKind}.json
            if (!string.IsNullOrWhiteSpace(path))
            {
                return ReadSingleArtifact(projectPath, sceneId, kind, path);
            }

            // 聚合读:reserved 通用 kind 走专用 reader,其他走通用聚合
            switch (kind)
            {
                case "modules":
                    return ReadModulesAggregated(projectPath, sceneId);
                case "zones":
                    return ReadZones(projectPath);
                case "readme":
                    return ReadReadme(projectPath);
                default:
                    return ReadAggregatedJsonFiles(projectPath, sceneId, kind + ".json");
            }
        }

        // ============================================================
        // POST — 写 artifact (新增,业务下沉派单纲领 §4.1)
        // ============================================================

        [HttpPost("artifacts/{artifactKind}")]
        public async Task<IActionResult> SaveSceneArtifact(
            string sceneId,
            string artifactKind,
            [FromBody] SaveSceneArtifactRequest request)
        {
            if (request is null)
                return BadRequest(new { code = "invalid_body", message = "body 不能为空" });

            if (string.IsNullOrWhiteSpace(sceneId))
                return BadRequest(new { code = "invalid_scene_id", message = "sceneId 不能为空" });

            if (!_projectContext.IsLoaded || string.IsNullOrWhiteSpace(_projectContext.CurrentProjectPath))
                return StatusCode(404, new { code = "no_project_loaded", message = "未加载项目" });

            // artifactKind 校验
            var kind = (artifactKind ?? string.Empty).Trim().ToLowerInvariant();
            if (!ArtifactKindPattern.IsMatch(kind))
            {
                return BadRequest(new
                {
                    code = "invalid_artifact_kind",
                    message = "artifactKind 必须匹配 ^[a-z][a-z0-9_-]*$",
                    receivedValue = artifactKind ?? string.Empty,
                });
            }
            if (ReservedKinds.Contains(kind))
            {
                return BadRequest(new
                {
                    code = "reserved_artifact_kind",
                    message = $"artifactKind '{kind}' 是平台 reserved 名(modules 走 AI Write/Edit 直写;zones / readme 是 baseline),不允许通过本端点写入"
                });
            }

            // path 校验(可空)
            var subPath = (request.Path ?? string.Empty).Trim().Trim('/', '\\');
            if (subPath.Length > 0)
            {
                if (subPath.Contains("..") || subPath.Contains('\\') || !PathSegmentPattern.IsMatch(subPath))
                {
                    return BadRequest(new
                    {
                        code = "invalid_path",
                        message = "path 仅允许 [a-zA-Z0-9_/-]+,禁止 .. / \\ / 前导斜杠"
                    });
                }
            }

            // content 校验
            if (request.Content is null)
                return BadRequest(new { code = "invalid_content", message = "content 不能为空" });

            // 拼相对路径 + 绝对路径(防穿越)
            var projectPath = _projectContext.GetActiveWorktreePath() ?? _projectContext.CurrentProjectPath!;
            var sceneRoot = PluginPaths.SceneSchemesRoot(projectPath, sceneId);
            var fileName = kind + ".json";
            var targetAbsolute = subPath.Length > 0
                ? Path.Combine(sceneRoot, subPath.Replace('/', Path.DirectorySeparatorChar), fileName)
                : Path.Combine(sceneRoot, fileName);

            string sceneRootFull, targetFull;
            try
            {
                sceneRootFull = Path.GetFullPath(sceneRoot);
                targetFull = Path.GetFullPath(targetAbsolute);
            }
            catch (Exception ex)
            {
                return BadRequest(new { code = "invalid_path", message = "路径解析失败: " + ex.Message });
            }

            var sceneRootWithSep = sceneRootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                   + Path.DirectorySeparatorChar;
            if (!targetFull.StartsWith(sceneRootWithSep, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new
                {
                    code = "path_escape",
                    message = "path 解析后逃逸 schemes/{sceneId}/ scene namespace"
                });
            }

            // V12b 路径隔离 + V12a 写入 gate
            var relativeForGate = Path.GetRelativePath(projectPath, targetFull).Replace('\\', '/');
            var writeGate = _projectContext.CheckWriteAllowed(relativeForGate);
            if (!writeGate.Allowed)
                return StatusCode(403, new { code = writeGate.Code, message = writeGate.Message });

            // 写盘(原子写:.tmp + rename)
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetFull)!);
                var json = JsonConvert.SerializeObject(request.Content, Formatting.Indented);
                var tmpPath = targetFull + ".tmp";
                await System.IO.File.WriteAllTextAsync(tmpPath, json, Encoding.UTF8);
                System.IO.File.Move(tmpPath, targetFull, overwrite: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SceneArtifact] 写入 {Target} 失败", targetFull);
                return StatusCode(500, new { code = "write_failed", message = $"写入失败: {ex.Message}" });
            }

            // 广播 SignalR 通用事件
            var pluginId = _projectContext.LaunchContext?.ActivePluginId;
            var timestamp = DateTime.UtcNow.ToString("o");
            await _hubContext.Clients.All.SendAsync("SceneArtifactUpdated", new
            {
                sceneId,
                artifactKind = kind,
                path = subPath.Length > 0 ? subPath : null,
                plugin = pluginId,
                timestamp,
            });

            _logger.LogInformation(
                "[SceneArtifact] 已保存 scene={SceneId} kind={Kind} path={Path} plugin={Plugin}",
                sceneId, kind, subPath.Length > 0 ? subPath : "-", pluginId ?? "-");

            return Ok(new
            {
                saved = true,
                sceneId,
                artifactKind = kind,
                path = subPath.Length > 0 ? subPath : null,
                timestamp,
            });
        }

        // ============================================================
        // GET reader 子函数(部分搬自旧版本,逻辑不变)
        // ============================================================

        /// <summary>精确读单文件 schemes/{sceneId}/{path}/{kind}.json,返回纯 JSON 内容(application/json)。</summary>
        private IActionResult ReadSingleArtifact(string projectPath, string sceneId, string kind, string path)
        {
            var subPath = path.Trim().Trim('/', '\\');
            if (subPath.Length == 0)
                return BadRequest(new { code = "invalid_path", message = "path 不能为空字符串(留空走聚合读)" });
            if (subPath.Contains("..") || subPath.Contains('\\') || !PathSegmentPattern.IsMatch(subPath))
                return BadRequest(new { code = "invalid_path", message = "path 仅允许 [a-zA-Z0-9_/-]+,禁止 .. / \\ / 前导斜杠" });

            var sceneRoot = PluginPaths.SceneSchemesRoot(projectPath, sceneId);
            var fileName = kind + ".json";
            var targetAbsolute = Path.Combine(sceneRoot, subPath.Replace('/', Path.DirectorySeparatorChar), fileName);

            string sceneRootFull, targetFull;
            try
            {
                sceneRootFull = Path.GetFullPath(sceneRoot);
                targetFull = Path.GetFullPath(targetAbsolute);
            }
            catch (Exception ex)
            {
                return BadRequest(new { code = "invalid_path", message = "路径解析失败: " + ex.Message });
            }

            var sceneRootWithSep = sceneRootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                   + Path.DirectorySeparatorChar;
            if (!targetFull.StartsWith(sceneRootWithSep, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { code = "path_escape", message = "path 解析后逃逸 schemes/{sceneId}/ scene namespace" });

            if (!System.IO.File.Exists(targetFull))
            {
                var relForReport = Path.GetRelativePath(projectPath, targetFull).Replace('\\', '/');
                return StatusCode(404, new
                {
                    code = "artifact_not_found",
                    message = $"未找到 {relForReport}",
                    sceneId,
                    artifactKind = kind,
                    path = subPath,
                });
            }

            try
            {
                var content = System.IO.File.ReadAllText(targetFull, Encoding.UTF8);
                return Content(content, "application/json", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SceneArtifact] 读 {Target} 失败", targetFull);
                return StatusCode(500, new { code = "read_failed", message = ex.Message });
            }
        }

        // ---------- modules:聚合返回 schemes/{sceneId}/ 下所有叶子 modules.json ----------

        private IActionResult ReadModulesAggregated(string projectPath, string sceneId)
        {
            var sceneSchemesRoot = PluginPaths.SceneSchemesRoot(projectPath, sceneId);
            if (!Directory.Exists(sceneSchemesRoot))
            {
                // legacy 兼容:M2 路径未生效时回退到旧路径 schemes/{zoneId}/modules.json
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

        // ---------- 通用聚合:scene namespace 内所有同名 fileName 文件 ----------

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

    /// <summary>POST /api/scheme/scenes/{sceneId}/artifacts/{artifactKind} 请求 body。</summary>
    public class SaveSceneArtifactRequest
    {
        /// <summary>
        /// scene namespace 内的相对子路径(如 "rz_3" 或 "rz_3/variants/abc")。
        /// 落盘路径 = schemes/{sceneId}/{path}/{artifactKind}.json。
        /// 留空 / null 时落到 schemes/{sceneId}/{artifactKind}.json。
        /// 字符集 [a-zA-Z0-9_/-]+,禁止 .. / \ / 前导斜杠。
        /// </summary>
        public string? Path { get; set; }

        /// <summary>
        /// 文件内容,任意 JSON 对象 / 数组。Server 不校验 schema(plugin 自定义),
        /// 序列化用 Newtonsoft.Json + Indented。
        /// </summary>
        public JToken? Content { get; set; }
    }
}
