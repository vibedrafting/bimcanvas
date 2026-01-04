using System;
using System.Collections.Generic;
using BIMCanvas.Server.Dtos;
using BIMCanvas.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BIMCanvas.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GitController : ControllerBase
    {
        private readonly ILogger<GitController> _logger;
        private readonly ProjectContext _projectContext;
        private readonly GitWorktreeService _gitService;

        public GitController(
            ILogger<GitController> logger,
            ProjectContext projectContext,
            GitWorktreeService gitService)
        {
            _logger = logger;
            _projectContext = projectContext;
            _gitService = gitService;
        }

        /// <summary>
        /// 获取分支列表
        /// </summary>
        /// <returns>分支信息列表</returns>
        [HttpGet("branches")]
        public ActionResult<List<GitBranchInfo>> GetBranches()
        {
            // 检查项目是否已加载
            if (!_projectContext.IsLoaded)
            {
                _logger.LogWarning("尝试获取分支列表，但没有加载项目");
                return Ok(new List<GitBranchInfo>());
            }

            var projectPath = _projectContext.CurrentProjectPath!;

            // 检查是否是 Git 仓库
            if (!_gitService.IsGitRepository(projectPath))
            {
                _logger.LogWarning("项目目录不是 Git 仓库: {Path}", projectPath);
                return Ok(new List<GitBranchInfo>());
            }

            try
            {
                var branches = _gitService.GetBranchesWithDetails(projectPath);
                _logger.LogDebug("获取分支列表成功: {Count} 个分支", branches.Count);
                return Ok(branches);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取分支列表失败");
                return StatusCode(500, new { message = $"获取分支列表失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 切换分支
        /// </summary>
        /// <param name="request">切换请求</param>
        /// <returns>操作结果</returns>
        [HttpPost("checkout")]
        public ActionResult Checkout([FromBody] CheckoutBranchRequest request)
        {
            if (string.IsNullOrEmpty(request.BranchName))
            {
                return BadRequest(new { message = "分支名不能为空" });
            }

            // 检查项目是否已加载
            if (!_projectContext.IsLoaded)
            {
                return BadRequest(new { message = "没有加载的项目" });
            }

            var projectPath = _projectContext.CurrentProjectPath!;

            // 检查是否是 Git 仓库
            if (!_gitService.IsGitRepository(projectPath))
            {
                return BadRequest(new { message = "项目目录不是 Git 仓库" });
            }

            try
            {
                // 检查是否有未提交的更改
                if (_gitService.HasUncommittedChanges(projectPath))
                {
                    return Conflict(new
                    {
                        message = "存在未提交的更改，请先提交或暂存更改",
                        hasUncommittedChanges = true
                    });
                }

                _gitService.CheckoutBranch(projectPath, request.BranchName);
                _logger.LogInformation("切换到分支: {Branch}", request.BranchName);

                return Ok(new
                {
                    success = true,
                    currentBranch = request.BranchName
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "切换分支失败: {Branch}", request.BranchName);
                return StatusCode(500, new { message = $"切换分支失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 获取当前分支
        /// </summary>
        /// <returns>当前分支名</returns>
        [HttpGet("current")]
        public ActionResult<object> GetCurrentBranch()
        {
            if (!_projectContext.IsLoaded)
            {
                return Ok(new { branch = (string?)null });
            }

            var projectPath = _projectContext.CurrentProjectPath!;

            if (!_gitService.IsGitRepository(projectPath))
            {
                return Ok(new { branch = (string?)null });
            }

            try
            {
                var branch = _gitService.GetCurrentBranch(projectPath);
                return Ok(new { branch });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取当前分支失败");
                return StatusCode(500, new { message = $"获取当前分支失败: {ex.Message}" });
            }
        }
    }
}
