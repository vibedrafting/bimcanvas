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
using BIMCanvas.Server.Services.PluginSecurity;
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
        private readonly PluginValidatorOrchestrator _validatorOrchestrator;

        public ModulesController(
            ModuleLibraryService libraryService,
            ModuleNormalizationService normalizationService,
            ModulesReaderService modulesReader,
            ModulesWriterService modulesWriter,
            ProjectContext projectContext,
            IHubContext<CanvasHub> hubContext,
            ILogger<ModulesController> logger,
            PluginValidatorOrchestrator validatorOrchestrator)
        {
            _libraryService = libraryService;
            _normalizationService = normalizationService;
            _modulesReader = modulesReader;
            _modulesWriter = modulesWriter;
            _projectContext = projectContext;
            _hubContext = hubContext;
            _logger = logger;
            _validatorOrchestrator = validatorOrchestrator;
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
        public async Task<ActionResult<ModuleNormalizationReport>> NormalizeModules([FromBody] NormalizeModulesRequest? request)
        {
            if (!_projectContext.IsLoaded)
                return BadRequest(new { message = "没有加载的项目" });

            var projectPath = _projectContext.GetActiveWorktreePath() ?? _projectContext.CurrentProjectPath!;
            if (!Directory.Exists(projectPath))
                return NotFound(new { message = $"项目目录不存在: {projectPath}" });

            var zoneIdsReq = request?.ZoneIds;
            var variantIdReq = request?.VariantId;
            if (!string.IsNullOrWhiteSpace(variantIdReq) && (zoneIdsReq == null || zoneIdsReq.Count == 0))
                return BadRequest(new { message = "variantId 非空时必须显式指定 zoneIds，不允许全分区扫描变体" });

            try
            {
                var report = await _validatorOrchestrator.RunAsync("normalize", projectPath, zoneIdsReq, variantIdReq);
                return Ok(report);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "模块数据规范化失败: {Path}", projectPath);
                return StatusCode(500, new { message = $"模块数据规范化失败: {ex.Message}" });
            }
        }

        // 旧 C# 规范化实现：包A 已委派插件 validators 脚本，本方法保留为 parity 对照，
        // 待用户验收一致后由「任务8」删除。
        private ActionResult<ModuleNormalizationReport> NormalizeModulesLegacy([FromBody] NormalizeModulesRequest? request)
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

}
