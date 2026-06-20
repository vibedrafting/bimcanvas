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
    /// 通用 artifact IO 端点 (scene-agnostic;持久数据按物理 zone 组织,不耦合运行时 plugin)。
    /// <para>
    /// 路由前缀 <c>api/scheme</c>:
    /// <list type="bullet">
    /// <item><c>GET  /artifacts/{artifactKind}</c>:聚合读 schemes/ 下所有同名 artifact</item>
    /// <item><c>GET  /artifacts/{artifactKind}?path={subPath}</c>:精确读单文件 <c>schemes/{subPath}/{artifactKind}.json</c></item>
    /// <item><c>POST /artifacts/{artifactKind}</c> body <c>{ path?, content }</c>:写到 <c>schemes/{path?}/{artifactKind}.json</c></item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>artifactKind 校验</b>:字符集 <c>^[a-z][a-z0-9_-]*$</c>(plugin-agnostic,不再 enum 硬编码 domain 词)。
    /// <b>Reserved 通用 kind</b>(<c>modules</c> / <c>zones</c> / <c>readme</c>):走原有专用 reader,**不允许 POST 写**(modules 走 AI 直写 Write/Edit;zones 是 baseline 派生;readme 是 baseline)。
    /// </para>
    /// <para>
    /// <b>POST 安全防线</b>:(1) artifactKind / path 字符集 + 防 ".." 穿越;
    /// (2) <see cref="ProjectContext.CheckWriteAllowed(string)"/> 写入 gate(baseline/computed 只读);
    /// (3) 最终绝对路径必须落在 <c>schemes/</c> 内。
    /// </para>
    /// <para>
    /// <b>SignalR 通用事件</b> <c>SceneArtifactUpdated</c>:payload <c>{artifactKind, path?, plugin?, timestamp}</c>。
    /// (旧 SemanticPlanController 已删除,domain 事件 SemanticPlanUpdated / ReferenceAnalysisUpdated 随之移除。)
    /// </para>
    /// </summary>
    [ApiController]
    [Route("api/scheme")]
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

        [HttpGet("artifacts/{artifactKind}")]
        public IActionResult GetSceneArtifact(string artifactKind, [FromQuery] string? path = null, [FromQuery] string? variantId = null)
        {
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

            // 指针模型:kind=modules + variantId 非空 → 解析「指定候选变体自身」的叶子 modules
            // (线性 = slug 根单文件;分区 = slug 内各叶子;统一覆盖,复用 validate 同款 topology variantId 解析)。
            // path 此时须为设计区路径(裸 zoneId 或容器路径),candidate slug 由 variantId 给出、不进 path。
            if (kind == "modules" && !string.IsNullOrWhiteSpace(variantId))
            {
                return ReadVariantModulesForDesignZone(projectPath, path, variantId!);
            }

            // 精确读:path 非空时 → 读单文件 schemes/{path}/{artifactKind}.json
            if (!string.IsNullOrWhiteSpace(path))
            {
                return ReadSingleArtifact(projectPath, kind, path);
            }

            // 聚合读:reserved 通用 kind 走专用 reader,其他走通用聚合
            switch (kind)
            {
                case "modules":
                    return ReadModulesAggregated(projectPath);
                case "zones":
                    return ReadZones(projectPath);
                case "readme":
                    return ReadReadme(projectPath);
                default:
                    return ReadAggregatedJsonFiles(projectPath, kind + ".json");
            }
        }

        // ============================================================
        // POST — 写 artifact (新增,业务下沉派单纲领 §4.1)
        // ============================================================

        [HttpPost("artifacts/{artifactKind}")]
        public async Task<IActionResult> SaveSceneArtifact(
            string artifactKind,
            [FromBody] SaveSceneArtifactRequest request)
        {
            if (request is null)
                return BadRequest(new { code = "invalid_body", message = "body 不能为空" });

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
            var schemesRoot = Path.Combine(projectPath, "schemes");
            var fileName = kind + ".json";
            var targetAbsolute = subPath.Length > 0
                ? Path.Combine(schemesRoot, subPath.Replace('/', Path.DirectorySeparatorChar), fileName)
                : Path.Combine(schemesRoot, fileName);

            string schemesRootFull, targetFull;
            try
            {
                schemesRootFull = Path.GetFullPath(schemesRoot);
                targetFull = Path.GetFullPath(targetAbsolute);
            }
            catch (Exception ex)
            {
                return BadRequest(new { code = "invalid_path", message = "路径解析失败: " + ex.Message });
            }

            var schemesRootWithSep = schemesRootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                   + Path.DirectorySeparatorChar;
            if (!targetFull.StartsWith(schemesRootWithSep, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new
                {
                    code = "path_escape",
                    message = "path 解析后逃逸 schemes/ 目录"
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
                artifactKind = kind,
                path = subPath.Length > 0 ? subPath : null,
                plugin = pluginId,
                timestamp,
            });

            _logger.LogInformation(
                "[SceneArtifact] 已保存 kind={Kind} path={Path} plugin={Plugin}",
                kind, subPath.Length > 0 ? subPath : "-", pluginId ?? "-");

            return Ok(new
            {
                saved = true,
                artifactKind = kind,
                path = subPath.Length > 0 ? subPath : null,
                timestamp,
            });
        }

        // ============================================================
        // GET reader 子函数(部分搬自旧版本,逻辑不变)
        // ============================================================

        /// <summary>精确读单文件 schemes/{path}/{kind}.json,返回纯 JSON 内容(application/json)。</summary>
        private IActionResult ReadSingleArtifact(string projectPath, string kind, string path)
        {
            var subPath = path.Trim().Trim('/', '\\');
            if (subPath.Length == 0)
                return BadRequest(new { code = "invalid_path", message = "path 不能为空字符串(留空走聚合读)" });
            if (subPath.Contains("..") || subPath.Contains('\\') || !PathSegmentPattern.IsMatch(subPath))
                return BadRequest(new { code = "invalid_path", message = "path 仅允许 [a-zA-Z0-9_/-]+,禁止 .. / \\ / 前导斜杠" });

            // 指针模型:modules 的「裸设计区路径」(path=zoneId,无 slug 段) 须经拓扑解析 adopted 指针,
            // 与聚合读 ReadModulesAggregated 同一收敛点;否则会去拼旧 canonical schemes/{zoneId}/modules.json
            // (指针模型下不存在)→404。slug 限定路径(path=zoneId/slug[/leaf]) 仍走下方直读、不解析指针;
            // legacy 项目(无 zones.json)或非设计区也落下方直读(零回归)。
            if (kind == "modules" && !subPath.Contains('/'))
            {
                var topology = Services.ModuleFileTopologyService.BuildFromSchemesPath(Path.Combine(projectPath, "schemes"));
                if (topology.IsDesignZoneId(subPath))
                    return ReadAdoptedModulesForDesignZone(projectPath, topology, subPath);
            }

            var schemesRoot = Path.Combine(projectPath, "schemes");
            var fileName = kind + ".json";
            var targetAbsolute = Path.Combine(schemesRoot, subPath.Replace('/', Path.DirectorySeparatorChar), fileName);

            string schemesRootFull, targetFull;
            try
            {
                schemesRootFull = Path.GetFullPath(schemesRoot);
                targetFull = Path.GetFullPath(targetAbsolute);
            }
            catch (Exception ex)
            {
                return BadRequest(new { code = "invalid_path", message = "路径解析失败: " + ex.Message });
            }

            var schemesRootWithSep = schemesRootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                   + Path.DirectorySeparatorChar;
            if (!targetFull.StartsWith(schemesRootWithSep, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { code = "path_escape", message = "path 解析后逃逸 schemes/ 目录" });

            if (!System.IO.File.Exists(targetFull))
            {
                var relForReport = Path.GetRelativePath(projectPath, targetFull).Replace('\\', '/');
                return StatusCode(404, new
                {
                    code = "artifact_not_found",
                    message = $"未找到 {relForReport}",
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

        // ---------- modules:聚合返回 schemes/ 下所有叶子 modules.json ----------

        private IActionResult ReadModulesAggregated(string projectPath)
        {
            var schemesRoot = Path.Combine(projectPath, "schemes");
            if (!Directory.Exists(schemesRoot))
            {
                return StatusCode(404, new { code = "artifact_not_found", message = "schemes 目录不存在", artifactKind = "modules" });
            }

            // 指针模型：经拓扑收敛只取各设计区 adopted 方案的 modules（排除 _ 隐藏候选 / 落选 slug）。
            // 拓扑 canonical 条目已重定向到 adopted slug 路径；无 DESIGN.md/adopted 的设计区不产 canonical 条目（不回头看，不再回落 legacy）。
            var topology = Services.ModuleFileTopologyService.BuildFromSchemesPath(schemesRoot);
            var entries = topology.GetExistingCanonicalModuleFiles(null);
            var aggregated = new List<object>();
            foreach (var entry in entries)
            {
                try
                {
                    var content = System.IO.File.ReadAllText(entry.FilePath, Encoding.UTF8);
                    aggregated.Add(new { relativePath = entry.RelativePath, content });
                }
                catch (Exception exc)
                {
                    _logger.LogWarning(exc, "读取 modules.json 失败: {File}", entry.FilePath);
                }
            }

            return Ok(new { artifactKind = "modules", files = aggregated });
        }

        // ---------- modules:裸设计区路径 → 经拓扑解析 adopted 当前生效方案 ----------

        /// <summary>
        /// 指针模型下精确读「单个设计区」当前生效(adopted)方案的 modules。
        /// 复用拓扑收敛(与 ReadModulesAggregated / validate 同一解析点):
        /// 单叶设计区 → 1 个叶子条目;有 subZones 的容器 → 各叶子的 adopted modules;无 DESIGN.md/adopted → 无条目(空,不回落 legacy)。
        /// 返回形态与聚合读一致 { files:[{relativePath, content}] },让调用方看到解析后的真实 slug 路径(也教会指针模型)。
        /// </summary>
        private IActionResult ReadAdoptedModulesForDesignZone(string projectPath, Services.ModuleFileTopology topology, string designZoneId)
        {
            IReadOnlyList<Services.ModuleFileEntry> entries;
            try
            {
                entries = topology.GetExistingCanonicalModuleFiles(new[] { designZoneId }, variantId: null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SceneArtifact] 解析设计区 {Zone} 的 adopted modules 失败", designZoneId);
                return StatusCode(500, new { code = "read_failed", message = ex.Message });
            }

            if (entries.Count == 0)
            {
                return StatusCode(404, new
                {
                    code = "artifact_not_found",
                    message = $"设计区 {designZoneId} 无当前生效(adopted)的 modules(可能尚未采纳任何方案或未布置)。读某个隐藏候选/具体方案请用 path={designZoneId}/{{slug}}。",
                    artifactKind = "modules",
                    path = designZoneId,
                });
            }

            var aggregated = new List<object>();
            foreach (var entry in entries)
            {
                try
                {
                    var content = System.IO.File.ReadAllText(entry.FilePath, Encoding.UTF8);
                    aggregated.Add(new { relativePath = entry.RelativePath, content });
                }
                catch (Exception exc)
                {
                    _logger.LogWarning(exc, "读取 modules.json 失败: {File}", entry.FilePath);
                }
            }

            return Ok(new { artifactKind = "modules", path = designZoneId, files = aggregated });
        }

        // ---------- modules:指定候选变体 slug → 枚举该候选自身叶子 modules ----------

        /// <summary>
        /// 指针模型下精确读「指定候选变体(variantId=slug)」的叶子 modules——线性变体取 slug 根单文件、
        /// 分区思维变体取 slug 内各叶子,统一覆盖(直读 schemes/{zone}/{slug}/modules.json 对分区变体会 404,故走拓扑)。
        /// 复用 validate 同款 topology variantId 解析(FindExistingCanonicalModuleFiles(zone, variantId));
        /// 返回形态与 adopted 读一致 { files:[{relativePath, content}] }。candidate 不存在/未写完 → 空 files(404)。
        /// 主用方:canvas_vision 截图+识图 的 id→名称 图例注入(渲染同源 variantId)。
        /// </summary>
        private IActionResult ReadVariantModulesForDesignZone(string projectPath, string? designZoneId, string variantId)
        {
            var zone = (designZoneId ?? string.Empty).Trim().Trim('/', '\\');
            if (zone.Length == 0)
                return BadRequest(new { code = "invalid_path", message = "variantId 非空时 path 必须为设计区路径(不能为空)" });
            if (zone.Contains("..") || zone.Contains('\\') || !PathSegmentPattern.IsMatch(zone))
                return BadRequest(new { code = "invalid_path", message = "path 仅允许 [a-zA-Z0-9_/-]+,禁止 .. / \\ / 前导斜杠" });

            IReadOnlyList<Services.ModuleFileEntry> entries;
            try
            {
                entries = Services.ModuleFileTopologyService.FindExistingCanonicalModuleFiles(
                    Path.Combine(projectPath, "schemes"), new[] { zone }, variantId);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { code = "invalid_variant", message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SceneArtifact] 解析候选 {Zone}/{Variant} 的 modules 失败", zone, variantId);
                return StatusCode(500, new { code = "read_failed", message = ex.Message });
            }

            if (entries.Count == 0)
            {
                return StatusCode(404, new
                {
                    code = "artifact_not_found",
                    message = $"候选 {zone}/{variantId} 无 modules(可能尚未写入或候选不存在)",
                    artifactKind = "modules",
                    path = zone,
                    variantId,
                });
            }

            var aggregated = new List<object>();
            foreach (var entry in entries)
            {
                try
                {
                    var content = System.IO.File.ReadAllText(entry.FilePath, Encoding.UTF8);
                    aggregated.Add(new { relativePath = entry.RelativePath, content });
                }
                catch (Exception exc)
                {
                    _logger.LogWarning(exc, "读取 modules.json 失败: {File}", entry.FilePath);
                }
            }

            return Ok(new { artifactKind = "modules", path = zone, variantId, files = aggregated });
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

        // ---------- 通用聚合:schemes/ 下所有同名 fileName 文件 ----------

        private IActionResult ReadAggregatedJsonFiles(string projectPath, string fileName)
        {
            var schemesRoot = Path.Combine(projectPath, "schemes");
            if (!Directory.Exists(schemesRoot))
            {
                return StatusCode(404, new { code = "artifact_not_found", message = "schemes 目录不存在", artifactKind = fileName.Replace(".json", "") });
            }

            var matches = Directory.GetFiles(schemesRoot, fileName, SearchOption.AllDirectories);
            var aggregated = new List<object>();
            foreach (var file in matches)
            {
                try
                {
                    var content = System.IO.File.ReadAllText(file, Encoding.UTF8);
                    var relativeFromScene = Path.GetRelativePath(schemesRoot, file).Replace('\\', '/');
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
                    artifactKind = fileName.Replace(".json", ""),
                });
            }

            return Ok(new
            {
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

    /// <summary>POST /api/scheme/artifacts/{artifactKind} 请求 body。</summary>
    public class SaveSceneArtifactRequest
    {
        /// <summary>
        /// schemes/ 内的相对子路径(如 "rz_3" 或 "rz_3/variants/abc")。
        /// 落盘路径 = schemes/{path}/{artifactKind}.json。
        /// 留空 / null 时落到 schemes/{artifactKind}.json。
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
