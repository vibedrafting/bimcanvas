using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BIMCanvas.Core.Models.Layout;
using BIMCanvas.Server.Hubs;
using BIMCanvas.Server.Models;
using BIMCanvas.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace BIMCanvas.Server.Controllers
{
    /// <summary>
    /// 模块库 API 控制器
    /// 提供模块库 JSON 和 SVG 文件的访问接口
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class ModulesController : ControllerBase
    {
        private readonly ModuleLibraryService _libraryService;
        private readonly ModuleNormalizationService _normalizationService;
        private readonly ModulesReaderService _modulesReader;
        private readonly ModulesWriterService _modulesWriter;
        private readonly ProjectContext _projectContext;
        private readonly IHubContext<CanvasHub> _hubContext;
        private readonly ILogger<ModulesController> _logger;

        public ModulesController(
            ModuleLibraryService libraryService,
            ModuleNormalizationService normalizationService,
            ModulesReaderService modulesReader,
            ModulesWriterService modulesWriter,
            ProjectContext projectContext,
            IHubContext<CanvasHub> hubContext,
            ILogger<ModulesController> logger)
        {
            _libraryService = libraryService;
            _normalizationService = normalizationService;
            _modulesReader = modulesReader;
            _modulesWriter = modulesWriter;
            _projectContext = projectContext;
            _hubContext = hubContext;
            _logger = logger;
        }

        /// <summary>
        /// 获取模块库
        /// GET /api/modules/library
        /// </summary>
        [HttpGet("library")]
        public ActionResult GetLibrary()
        {
            var library = _libraryService.GetModuleLibrary();
            if (library == null)
                return NotFound(new { message = "模块库不存在或无加载项目" });
            return Ok(library);
        }

        /// <summary>
        /// 获取单个 SVG 文件
        /// GET /api/modules/svg/{moduleId}
        /// </summary>
        [HttpGet("svg/{moduleId}")]
        public ActionResult GetSvg(string moduleId)
        {
            var ifNoneMatch = Request.Headers["If-None-Match"].FirstOrDefault();
            var (content, eTag) = _libraryService.GetSvgContent(moduleId);

            if (content == null)
                return NotFound(new { message = $"SVG 不存在: {moduleId}" });

            if (!string.IsNullOrEmpty(ifNoneMatch) && ifNoneMatch == eTag)
                return StatusCode(304);

            Response.Headers["ETag"] = eTag;
            Response.Headers["Cache-Control"] = "public, max-age=86400";
            return Content(content, "image/svg+xml", Encoding.UTF8);
        }

        /// <summary>
        /// 规范化当前项目模块数据。
        /// POST /api/modules/normalize
        /// </summary>
        [HttpPost("normalize")]
        public ActionResult<ModuleNormalizationReport> NormalizeModules([FromBody] NormalizeModulesRequest? request)
        {
            if (!_projectContext.IsLoaded)
                return BadRequest(new { message = "没有加载的项目" });

            var projectPath = _projectContext.GetActiveWorktreePath()
                              ?? _projectContext.CurrentProjectPath!;

            if (!Directory.Exists(projectPath))
                return NotFound(new { message = $"项目目录不存在: {projectPath}" });

            try
            {
                var variantId = request?.VariantId;
                if (!string.IsNullOrWhiteSpace(variantId) && (request?.ZoneIds is null || request.ZoneIds.Count == 0))
                    return BadRequest(new { message = "variantId 非空时必须显式指定 zoneIds，不允许全分区扫描变体" });

                var report = _normalizationService.NormalizeModules(projectPath, request?.ZoneIds, variantId);
                return Ok(report);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "模块数据规范化失败: {Path}", projectPath);
                return StatusCode(500, new { message = $"模块数据规范化失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 保存单个叶子分区 modules（Agent 入口，wrapper 由 Server 派生）。
        /// POST /api/scheme/modules
        /// 即使 body 误传 schemeMetadata 字段也会被 Server 完全忽略。
        /// </summary>
        [HttpPost("/api/scheme/modules")]
        public async Task<ActionResult> SaveSchemeModules([FromBody] SchemeModulesPayload request)
        {
            if (!_projectContext.IsLoaded)
                return BadRequest(new { message = "没有加载的项目" });

            if (request == null)
                return BadRequest(new { message = "请求体为空" });

            if (string.IsNullOrWhiteSpace(request.DesignZoneId))
                return BadRequest(new { message = "designZoneId 必填" });
            if (string.IsNullOrWhiteSpace(request.LeafZoneId))
                return BadRequest(new { message = "leafZoneId 必填" });
            if (request.Modules == null)
                return BadRequest(new { message = "modules 必填（空数组也可以）" });

            if (!string.IsNullOrWhiteSpace(request.VariantId))
            {
                try
                {
                    ModuleFileTopologyService.EnsureSafeVariantId(request.VariantId);
                }
                catch (ArgumentException ex)
                {
                    return BadRequest(new { message = ex.Message });
                }
            }

            var projectPath = _projectContext.GetActiveWorktreePath()
                              ?? _projectContext.CurrentProjectPath!;

            try
            {
                await _modulesWriter.WriteAsync(
                    projectPath,
                    request.DesignZoneId,
                    request.LeafZoneId,
                    string.IsNullOrWhiteSpace(request.VariantId) ? null : request.VariantId,
                    VariantPathMode.New,
                    request.Modules);

                await _hubContext.Clients.All.SendAsync("ReceiveUpdate", new
                {
                    type = "file_changed",
                    file = "modules.json",
                    timestamp = DateTime.UtcNow,
                    action = "reload",
                    trigger = "save_modules"
                });

                _logger.LogInformation(
                    "[SaveSchemeModules] 保存 {Count} 个模块到 {Design}/{Leaf}（variant={Variant}）",
                    request.Modules.Count, request.DesignZoneId, request.LeafZoneId, request.VariantId ?? "-");

                return Ok(new
                {
                    saved = true,
                    designZoneId = request.DesignZoneId,
                    leafZoneId = request.LeafZoneId,
                    variantId = request.VariantId,
                    modulesCount = request.Modules.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SaveSchemeModules] 保存失败");
                return StatusCode(500, new { message = $"保存失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 读取单个叶子分区 modules（完整 wrapper，含 schemeMetadata）。
        /// GET /api/scheme/modules?designZoneId=&leafZoneId=&variantId=
        /// </summary>
        [HttpGet("/api/scheme/modules")]
        public ActionResult GetSchemeModules(
            [FromQuery] string designZoneId,
            [FromQuery] string leafZoneId,
            [FromQuery] string? variantId = null)
        {
            if (!_projectContext.IsLoaded)
                return BadRequest(new { message = "没有加载的项目" });

            if (string.IsNullOrWhiteSpace(designZoneId))
                return BadRequest(new { message = "designZoneId 必填" });
            if (string.IsNullOrWhiteSpace(leafZoneId))
                return BadRequest(new { message = "leafZoneId 必填" });

            if (!string.IsNullOrWhiteSpace(variantId))
            {
                try
                {
                    ModuleFileTopologyService.EnsureSafeVariantId(variantId);
                }
                catch (ArgumentException ex)
                {
                    return BadRequest(new { message = ex.Message });
                }
            }

            var projectPath = _projectContext.GetActiveWorktreePath()
                              ?? _projectContext.CurrentProjectPath!;

            // 新 endpoint 默认按新路径解析（与 save 入口对齐）
            var filePath = _modulesWriter.ResolveModulesPath(
                projectPath,
                designZoneId,
                leafZoneId,
                string.IsNullOrWhiteSpace(variantId) ? null : variantId,
                VariantPathMode.New);

            try
            {
                var wrapper = _modulesReader.Read(filePath);
                if (wrapper == null)
                    return NotFound(new { message = $"未找到 modules: {filePath}" });

                return Ok(wrapper);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[GetSchemeModules] 读取失败: {Path}", filePath);
                return StatusCode(500, new { message = $"读取失败: {ex.Message}" });
            }
        }
    }

    public class NormalizeModulesRequest
    {
        /// <summary>仅规范化这些 Zone 内的模块；为空或 null 时规范化全部模块。</summary>
        public List<string>? ZoneIds { get; set; }

        /// <summary>
        /// 可选。规范化非 canonical 变体文件，如 "alt-1" → 读写每个目标 Zone 下的 modules-alt-1.json。
        /// 仅 module-relocation-agent 使用；非空时必须与非空 ZoneIds 同时提供。
        /// </summary>
        public string? VariantId { get; set; }
    }

    /// <summary>
    /// POST /api/scheme/modules 的请求体。
    /// 注意：不声明 SchemeMetadata 字段——即便客户端传也会被 ModelBinder 丢弃；
    /// Server 总是从 semantic_plan / variantId 派生 schemeMetadata。
    /// </summary>
    public class SchemeModulesPayload
    {
        public string DesignZoneId { get; set; } = string.Empty;
        public string LeafZoneId { get; set; } = string.Empty;
        public string? VariantId { get; set; }
        public List<Module> Modules { get; set; } = new List<Module>();
    }
}
