using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BIMCanvas.Server.Services;
using Microsoft.AspNetCore.Mvc;
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
        private readonly ProjectContext _projectContext;
        private readonly ILogger<ModulesController> _logger;

        public ModulesController(
            ModuleLibraryService libraryService,
            ModuleNormalizationService normalizationService,
            ProjectContext projectContext,
            ILogger<ModulesController> logger)
        {
            _libraryService = libraryService;
            _normalizationService = normalizationService;
            _projectContext = projectContext;
            _logger = logger;
        }

        /// <summary>
        /// 获取模块库
        /// GET /api/modules/library
        /// </summary>
        /// <returns>模块库 JSON（svgPath 已转换为 API 路径）</returns>
        [HttpGet("library")]
        public ActionResult GetLibrary()
        {
            var library = _libraryService.GetModuleLibrary();

            if (library == null)
            {
                return NotFound(new { message = "模块库不存在或无加载项目" });
            }

            return Ok(library);
        }

        /// <summary>
        /// 获取单个 SVG 文件
        /// GET /api/modules/svg/{moduleId}
        /// </summary>
        /// <param name="moduleId">模块ID（如 mod_bed_001）</param>
        /// <returns>SVG 文件内容</returns>
        [HttpGet("svg/{moduleId}")]
        public ActionResult GetSvg(string moduleId)
        {
            // 检查 If-None-Match 缓存头
            var ifNoneMatch = Request.Headers["If-None-Match"].FirstOrDefault();

            var (content, eTag) = _libraryService.GetSvgContent(moduleId);

            if (content == null)
            {
                return NotFound(new { message = $"SVG 不存在: {moduleId}" });
            }

            // ETag 匹配，返回 304 Not Modified
            if (!string.IsNullOrEmpty(ifNoneMatch) && ifNoneMatch == eTag)
            {
                return StatusCode(304);
            }

            // 设置缓存头
            Response.Headers["ETag"] = eTag;
            Response.Headers["Cache-Control"] = "public, max-age=86400"; // 24小时缓存

            return Content(content, "image/svg+xml", Encoding.UTF8);
        }

        /// <summary>
        /// 规范化当前项目模块数据。
        /// POST /api/modules/normalize
        /// 当前支持将 facing.semantic 收敛为 facing.value，并清空 semantic。
        /// </summary>
        [HttpPost("normalize")]
        public ActionResult<ModuleNormalizationReport> NormalizeModules([FromBody] NormalizeModulesRequest? request)
        {
            if (!_projectContext.IsLoaded)
            {
                return BadRequest(new { message = "没有加载的项目" });
            }

            var projectPath = _projectContext.GetActiveWorktreePath()
                              ?? _projectContext.CurrentProjectPath!;

            if (!Directory.Exists(projectPath))
            {
                return NotFound(new { message = $"项目目录不存在: {projectPath}" });
            }

            try
            {
                var report = _normalizationService.NormalizeModules(projectPath, request?.ZoneIds);
                return Ok(report);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "模块数据规范化失败: {Path}", projectPath);
                return StatusCode(500, new { message = $"模块数据规范化失败: {ex.Message}" });
            }
        }
    }

    public class NormalizeModulesRequest
    {
        /// <summary>仅规范化这些 Zone 内的模块；为空或 null 时规范化全部模块。</summary>
        public List<string>? ZoneIds { get; set; }
    }
}
