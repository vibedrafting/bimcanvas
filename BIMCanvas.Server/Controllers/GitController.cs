using System;
using System.Collections.Generic;
using System.Linq;
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
            _logger.LogInformation(">>> [GitController] GetBranches called");

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
            _logger.LogInformation(">>> [GitController] Checkout called: {Branch}, CreateIfNotExist={Create}",
                request?.BranchName ?? "(null)", request?.CreateIfNotExist ?? false);

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
                var hasChanges = _gitService.HasUncommittedChanges(projectPath);

                if (hasChanges)
                {
                    // 优先级：Discard > Commit > 返回冲突
                    if (request.DiscardBeforeCheckout)
                    {
                        // 放弃所有未提交的更改
                        _gitService.DiscardChanges(projectPath);
                        _logger.LogInformation("切换分支前放弃更改");
                    }
                    else if (request.CommitBeforeCheckout)
                    {
                        // 自动提交更改
                        var commitMessage = string.IsNullOrEmpty(request.CommitMessage)
                            ? $"自动存档：切换到分支 {request.BranchName} 前保存"
                            : request.CommitMessage;

                        _gitService.Commit(projectPath, commitMessage);
                        _logger.LogInformation("切换分支前自动存档: {Message}", commitMessage);
                    }
                    else
                    {
                        // 返回冲突，让前端决定是否存档
                        return Conflict(new
                        {
                            message = "存在未提交的更改，请先提交或暂存更改",
                            hasUncommittedChanges = true
                        });
                    }
                }

                // 检查分支是否存在
                var allBranches = _gitService.GetAllBranches(projectPath);
                var branchExists = allBranches.Contains(request.BranchName);
                var created = false;

                if (!branchExists)
                {
                    if (request.CreateIfNotExist)
                    {
                        // 基于当前分支创建新分支
                        _gitService.CreateBranch(projectPath, request.BranchName);
                        _logger.LogInformation("创建新分支: {Branch}", request.BranchName);
                        created = true;
                    }
                    else
                    {
                        return NotFound(new { message = $"分支 '{request.BranchName}' 不存在" });
                    }
                }

                _gitService.CheckoutBranch(projectPath, request.BranchName);
                _logger.LogInformation("切换到分支: {Branch}", request.BranchName);

                return Ok(new
                {
                    success = true,
                    currentBranch = request.BranchName,
                    created
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "切换分支失败: {Branch}", request.BranchName);
                return StatusCode(500, new { message = $"切换分支失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 提交当前更改（存档）
        /// </summary>
        /// <param name="request">提交请求</param>
        /// <returns>操作结果</returns>
        [HttpPost("commit")]
        public ActionResult Commit([FromBody] CommitRequest request)
        {
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
                if (!_gitService.HasUncommittedChanges(projectPath))
                {
                    return Ok(new
                    {
                        success = true,
                        message = "没有需要提交的更改",
                        committed = false
                    });
                }

                // 生成提交信息
                var message = string.IsNullOrEmpty(request?.Message)
                    ? $"自动存档_{DateTime.Now:yyyyMMdd_HHmmss}"
                    : request.Message;

                _gitService.Commit(projectPath, message);
                _logger.LogInformation("提交更改: {Message}", message);

                // 获取当前分支信息
                var currentBranch = _gitService.GetCurrentBranch(projectPath);
                var commitInfo = _gitService.GetBranchCommit(projectPath, currentBranch);

                return Ok(new
                {
                    success = true,
                    message = "提交成功",
                    committed = true,
                    commit = commitInfo
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "提交更改失败");
                return StatusCode(500, new { message = $"提交更改失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 放弃所有未提交的更改
        /// </summary>
        /// <returns>操作结果</returns>
        [HttpPost("discard")]
        public ActionResult DiscardChanges()
        {
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
                _gitService.DiscardChanges(projectPath);
                return Ok(new
                {
                    success = true,
                    message = "已放弃所有更改"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "放弃更改失败");
                return StatusCode(500, new { message = $"放弃更改失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 获取工作区状态（是否有未提交的更改）
        /// </summary>
        /// <returns>工作区状态</returns>
        [HttpGet("status")]
        public ActionResult GetStatus()
        {
            // 检查项目是否已加载
            if (!_projectContext.IsLoaded)
            {
                return Ok(new
                {
                    isLoaded = false,
                    hasUncommittedChanges = false
                });
            }

            var projectPath = _projectContext.CurrentProjectPath!;

            // 检查是否是 Git 仓库
            if (!_gitService.IsGitRepository(projectPath))
            {
                return Ok(new
                {
                    isLoaded = true,
                    isGitRepo = false,
                    hasUncommittedChanges = false
                });
            }

            try
            {
                var hasChanges = _gitService.HasUncommittedChanges(projectPath);
                var currentBranch = _gitService.GetCurrentBranch(projectPath);

                return Ok(new
                {
                    isLoaded = true,
                    isGitRepo = true,
                    hasUncommittedChanges = hasChanges,
                    currentBranch
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取工作区状态失败");
                return StatusCode(500, new { message = $"获取工作区状态失败: {ex.Message}" });
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

        #region Worktree API

        /// <summary>
        /// 获取所有 Worktree 列表
        /// </summary>
        /// <returns>Worktree 信息列表</returns>
        [HttpGet("worktrees")]
        public ActionResult<List<WorktreeInfoDto>> GetWorktrees()
        {
            _logger.LogInformation(">>> [GitController] GetWorktrees called");

            if (!_projectContext.IsLoaded)
            {
                return Ok(new List<WorktreeInfoDto>());
            }

            var projectPath = _projectContext.CurrentProjectPath!;

            if (!_gitService.IsGitRepository(projectPath))
            {
                return Ok(new List<WorktreeInfoDto>());
            }

            try
            {
                var worktrees = _gitService.GetWorktrees(projectPath);
                var result = worktrees.Select(wt => new WorktreeInfoDto
                {
                    Name = System.IO.Path.GetFileName(wt.Path),
                    Path = wt.Path,
                    Branch = wt.Branch,
                    CommitHash = wt.CommitHash,
                    IsMain = wt.Path == projectPath
                }).ToList();

                _logger.LogDebug("获取 Worktree 列表成功: {Count} 个", result.Count);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取 Worktree 列表失败");
                return StatusCode(500, new { message = $"获取 Worktree 列表失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 创建新的 Worktree
        /// </summary>
        /// <param name="request">创建请求</param>
        /// <returns>新建的 Worktree 信息</returns>
        [HttpPost("worktrees")]
        public ActionResult<WorktreeInfoDto> CreateWorktree([FromBody] CreateWorktreeRequest request)
        {
            _logger.LogInformation(">>> [GitController] CreateWorktree called: Name={Name}, Branch={Branch}",
                request?.Name ?? "(null)", request?.BranchName ?? "(null)");

            if (string.IsNullOrEmpty(request?.Name))
            {
                return BadRequest(new { message = "Worktree 名称不能为空" });
            }

            if (string.IsNullOrEmpty(request.BranchName))
            {
                return BadRequest(new { message = "分支名不能为空" });
            }

            if (!_projectContext.IsLoaded)
            {
                return BadRequest(new { message = "没有加载的项目" });
            }

            var projectPath = _projectContext.CurrentProjectPath!;

            if (!_gitService.IsGitRepository(projectPath))
            {
                return BadRequest(new { message = "项目目录不是 Git 仓库" });
            }

            try
            {
                var worktreePath = _gitService.CreateWorktree(projectPath, request.Name, request.BranchName);

                var result = new WorktreeInfoDto
                {
                    Name = request.Name,
                    Path = worktreePath,
                    Branch = request.BranchName,
                    IsMain = false
                };

                _logger.LogInformation("创建 Worktree 成功: {Name} @ {Path}", request.Name, worktreePath);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建 Worktree 失败: {Name}", request.Name);
                return StatusCode(500, new { message = $"创建 Worktree 失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 删除 Worktree
        /// </summary>
        /// <param name="name">Worktree 名称</param>
        /// <returns>操作结果</returns>
        [HttpDelete("worktrees/{name}")]
        public ActionResult DeleteWorktree(string name)
        {
            _logger.LogInformation(">>> [GitController] DeleteWorktree called: {Name}", name);

            if (string.IsNullOrEmpty(name))
            {
                return BadRequest(new { message = "Worktree 名称不能为空" });
            }

            if (!_projectContext.IsLoaded)
            {
                return BadRequest(new { message = "没有加载的项目" });
            }

            var projectPath = _projectContext.CurrentProjectPath!;

            if (!_gitService.IsGitRepository(projectPath))
            {
                return BadRequest(new { message = "项目目录不是 Git 仓库" });
            }

            try
            {
                _gitService.RemoveWorktree(projectPath, name);

                _logger.LogInformation("删除 Worktree 成功: {Name}", name);
                return Ok(new { success = true, message = $"Worktree '{name}' 已删除" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除 Worktree 失败: {Name}", name);
                return StatusCode(500, new { message = $"删除 Worktree 失败: {ex.Message}" });
            }
        }

        #endregion

        #region Merge API

        /// <summary>
        /// 合并分支到当前分支
        /// </summary>
        /// <param name="request">合并请求</param>
        /// <returns>合并结果</returns>
        [HttpPost("merge")]
        public ActionResult<MergeResultDto> MergeBranch([FromBody] MergeRequest request)
        {
            _logger.LogInformation(">>> [GitController] MergeBranch called: Source={Source}",
                request?.SourceBranch ?? "(null)");

            if (string.IsNullOrEmpty(request?.SourceBranch))
            {
                return BadRequest(new { message = "源分支名不能为空" });
            }

            if (!_projectContext.IsLoaded)
            {
                return BadRequest(new { message = "没有加载的项目" });
            }

            var projectPath = _projectContext.CurrentProjectPath!;

            if (!_gitService.IsGitRepository(projectPath))
            {
                return BadRequest(new { message = "项目目录不是 Git 仓库" });
            }

            try
            {
                // 检查源分支是否存在
                var allBranches = _gitService.GetAllBranches(projectPath);
                if (!allBranches.Contains(request.SourceBranch))
                {
                    return NotFound(new { message = $"源分支 '{request.SourceBranch}' 不存在" });
                }

                var mergeResult = _gitService.MergeBranch(projectPath, request.SourceBranch, request.CommitMessage);

                var result = new MergeResultDto
                {
                    Success = mergeResult.Success,
                    HasConflicts = mergeResult.HasConflicts,
                    Message = mergeResult.Message
                };

                if (mergeResult.Success)
                {
                    _logger.LogInformation("合并分支成功: {Source} -> 当前分支", request.SourceBranch);
                }
                else
                {
                    _logger.LogWarning("合并分支失败: {Source}, HasConflicts={HasConflicts}",
                        request.SourceBranch, mergeResult.HasConflicts);
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "合并分支失败: {Source}", request.SourceBranch);
                return StatusCode(500, new { message = $"合并分支失败: {ex.Message}" });
            }
        }

        #endregion
    }
}
