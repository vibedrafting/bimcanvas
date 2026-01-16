using System;
using BIMCanvas.Server.Dtos;
using BIMCanvas.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BIMCanvas.Server.Controllers
{
    /// <summary>
    /// 方案数据控制器 - 支持跨分支/Worktree 的模块数据读写
    ///
    /// source 参数格式：
    /// - main: 主仓库当前分支
    /// - worktree:{name}: 指定 Worktree（如 worktree:ai-job-v）
    /// - branch:{name}: 指定分支（暂不支持）
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class SchemeController : ControllerBase
    {
        private readonly ILogger<SchemeController> _logger;
        private readonly ProjectContext _projectContext;
        private readonly SchemeDataService _schemeDataService;

        public SchemeController(
            ILogger<SchemeController> logger,
            ProjectContext projectContext,
            SchemeDataService schemeDataService)
        {
            _logger = logger;
            _projectContext = projectContext;
            _schemeDataService = schemeDataService;
        }

        /// <summary>
        /// 获取指定数据源的模块数据
        /// </summary>
        /// <param name="source">数据源标识（main, worktree:{name}, branch:{name}）</param>
        /// <returns>模块列表</returns>
        [HttpGet("{source}/modules")]
        public ActionResult<SchemeModulesResponse> GetModules(string source)
        {
            _logger.LogInformation(">>> [SchemeController] GetModules called, source={Source}", source);

            if (!_projectContext.IsLoaded)
            {
                _logger.LogWarning("尝试获取模块数据，但没有加载项目");
                return BadRequest(new { error = "未加载项目" });
            }

            try
            {
                var response = _schemeDataService.GetModules(source);
                _logger.LogDebug("获取模块成功: source={Source}, count={Count}",
                    source, response.Modules.Count);
                return Ok(response);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "获取模块数据失败: {Source}", source);
                return BadRequest(new { error = ex.Message });
            }
            catch (NotSupportedException ex)
            {
                _logger.LogWarning(ex, "不支持的 source 格式: {Source}", source);
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取模块数据时发生意外错误");
                return StatusCode(500, new { error = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 保存模块数据到指定数据源
        /// </summary>
        /// <param name="source">数据源标识</param>
        /// <param name="request">保存请求（包含模块列表和可选的提交信息）</param>
        /// <returns>保存结果</returns>
        [HttpPut("{source}/modules")]
        public ActionResult<SaveSchemeModulesResponse> SaveModules(
            string source,
            [FromBody] SaveSchemeModulesRequest request)
        {
            _logger.LogInformation(">>> [SchemeController] SaveModules called, source={Source}, count={Count}",
                source, request?.Modules?.Count ?? 0);

            if (!_projectContext.IsLoaded)
            {
                _logger.LogWarning("尝试保存模块数据，但没有加载项目");
                return BadRequest(new { error = "未加载项目" });
            }

            if (request?.Modules == null)
            {
                return BadRequest(new { error = "请求体无效" });
            }

            try
            {
                var response = _schemeDataService.SaveModules(source, request);
                _logger.LogInformation("保存模块成功: source={Source}, savedCount={Count}",
                    source, response.SavedCount);
                return Ok(response);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "保存模块数据失败: {Source}", source);
                return BadRequest(new { error = ex.Message });
            }
            catch (NotSupportedException ex)
            {
                _logger.LogWarning(ex, "不支持的 source 格式: {Source}", source);
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存模块数据时发生意外错误");
                return StatusCode(500, new { error = "服务器内部错误" });
            }
        }
    }
}
