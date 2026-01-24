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
            _logger.LogInformation(">>> [GitController] Checkout called: Branch={Branch}, CreateIfNotExist={Create}, BaseBranch={BaseBranch}",
                request?.BranchName ?? "(null)",
                request?.CreateIfNotExist ?? false,
                request?.BaseBranch ?? "(null)");

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
                        // 基于指定分支（或当前 HEAD）创建新分支
                        _gitService.CreateBranch(projectPath, request.BranchName, request.BaseBranch);
                        _logger.LogInformation("创建新分支: {Branch}, 基于: {BaseBranch}",
                            request.BranchName, request.BaseBranch ?? "(当前HEAD)");
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
        /// 支持在主仓库或指定 Worktree 中提交
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
                // 确定工作目录：默认主仓库，可指定 Worktree
                var workingDir = projectPath;

                if (!string.IsNullOrEmpty(request?.WorktreeName))
                {
                    var worktreesDir = _gitService.GetWorktreesDir(projectPath);
                    workingDir = System.IO.Path.Combine(worktreesDir, request.WorktreeName);

                    if (!System.IO.Directory.Exists(workingDir))
                    {
                        return NotFound(new { message = $"Worktree '{request.WorktreeName}' 不存在" });
                    }

                    _logger.LogInformation("在 Worktree 中提交: {WorktreeName}", request.WorktreeName);
                }

                // 生成提交信息
                var message = string.IsNullOrEmpty(request?.Message)
                    ? $"自动存档_{DateTime.Now:yyyyMMdd_HHmmss}"
                    : request.Message;

                // 直接调用 Commit（内部会执行 git add . 并处理 nothing to commit）
                var committed = _gitService.TryCommit(workingDir, message);

                if (!committed)
                {
                    return Ok(new
                    {
                        success = true,
                        message = "没有需要提交的更改",
                        committed = false
                    });
                }

                _logger.LogInformation("提交更改: {Message} @ {WorkingDir}", message, workingDir);

                // 获取当前分支信息
                var currentBranch = _gitService.GetCurrentBranch(workingDir);
                var commitInfo = _gitService.GetBranchCommit(workingDir, currentBranch);

                return Ok(new
                {
                    success = true,
                    message = "提交成功",
                    committed = true,
                    commit = commitInfo,
                    worktree = request?.WorktreeName
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
        /// 检查分支是否被 Worktree 占用
        /// </summary>
        /// <param name="branchName">分支名</param>
        /// <returns>占用状态</returns>
        [HttpGet("worktrees/branch/{branchName}/status")]
        public ActionResult<object> GetBranchWorktreeStatus(string branchName)
        {
            _logger.LogInformation(">>> [GitController] GetBranchWorktreeStatus called: {Branch}", branchName);

            if (string.IsNullOrEmpty(branchName))
            {
                return BadRequest(new { message = "分支名不能为空" });
            }

            if (!_projectContext.IsLoaded)
            {
                return Ok(new { isOccupied = false });
            }

            var projectPath = _projectContext.CurrentProjectPath!;

            if (!_gitService.IsGitRepository(projectPath))
            {
                return Ok(new { isOccupied = false });
            }

            try
            {
                var (isOccupied, worktreePath) = _gitService.IsBranchOccupiedByWorktree(projectPath, branchName);
                return Ok(new
                {
                    isOccupied,
                    worktreePath
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查分支占用状态失败: {Branch}", branchName);
                return StatusCode(500, new { message = $"检查分支占用状态失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 创建新的 Worktree
        /// - 分支已存在：检出到 Worktree（场景 A：并行开发）
        /// - 分支不存在：基于 BaseBranch 创建新分支（场景 B：隔离环境）
        /// - 创建前自动存档：如有未提交更改，静默执行自动存档
        /// - 创建前检查分支是否已被其他 Worktree 占用
        /// </summary>
        /// <param name="request">创建请求</param>
        /// <returns>新建的 Worktree 信息</returns>
        [HttpPost("worktrees")]
        public ActionResult<WorktreeInfoDto> CreateWorktree([FromBody] CreateWorktreeRequest request)
        {
            _logger.LogInformation(">>> [GitController] CreateWorktree called: Name={Name}, Branch={Branch}, BaseBranch={BaseBranch}, Intent={Intent}",
                request?.Name ?? "(null)", request?.Branch ?? "(null)", request?.BaseBranch ?? "(null)", request?.Intent ?? "(null)");

            if (string.IsNullOrEmpty(request?.Name))
            {
                return BadRequest(new { message = "Worktree 名称不能为空" });
            }

            if (string.IsNullOrEmpty(request.Branch))
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
                // 检查分支是否已被其他 Worktree 占用
                var (isOccupied, occupiedPath) = _gitService.IsBranchOccupiedByWorktree(projectPath, request.Branch);
                if (isOccupied)
                {
                    var occupiedWorktreeName = System.IO.Path.GetFileName(occupiedPath);
                    _logger.LogWarning("分支 {Branch} 已被 Worktree {Worktree} 占用", request.Branch, occupiedWorktreeName);
                    return Conflict(new
                    {
                        message = $"分支 '{request.Branch}' 已被其他 Worktree 占用",
                        isOccupied = true,
                        occupiedWorktree = occupiedWorktreeName,
                        occupiedPath = occupiedPath
                    });
                }

                // 自动存档：创建 Worktree 前检测到未提交更改，静默执行存档
                if (_gitService.HasUncommittedChanges(projectPath))
                {
                    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    _gitService.Commit(projectPath, $"自动存档_{timestamp}");
                    _logger.LogInformation("创建 Worktree 前自动存档");
                }

                var worktreePath = _gitService.CreateWorktree(projectPath, request.Name, request.Branch, request.BaseBranch, request.Intent);

                var result = new WorktreeInfoDto
                {
                    Name = request.Name,
                    Path = worktreePath,
                    Branch = request.Branch,
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
        /// <param name="deleteBranch">是否同时删除关联分支（场景 B：隔离环境使用）</param>
        /// <returns>操作结果</returns>
        [HttpDelete("worktrees/{name}")]
        public ActionResult DeleteWorktree(string name, [FromQuery] bool deleteBranch = false)
        {
            _logger.LogInformation(">>> [GitController] DeleteWorktree called: {Name}, deleteBranch={DeleteBranch}",
                name, deleteBranch);

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
                _gitService.RemoveWorktree(projectPath, name, deleteBranch);

                var message = deleteBranch
                    ? $"Worktree '{name}' 及关联分支已删除"
                    : $"Worktree '{name}' 已删除";

                _logger.LogInformation("删除 Worktree 成功: {Name}", name);
                return Ok(new { success = true, message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除 Worktree 失败: {Name}", name);
                return StatusCode(500, new { message = $"删除 Worktree 失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 创建 AI Job（高级端口）
        /// 一键创建 Agent 工作环境：自动生成分支名 + 创建 Worktree
        /// name 可选：不传则自动生成
        /// baseBranch 可选：不传则自动使用当前分支
        /// </summary>
        /// <param name="request">AI Job 请求（可为空）</param>
        /// <returns>AI Job 响应（包含自动生成的名称和分支名）</returns>
        [HttpPost("ai-job")]
        public ActionResult<AiJobResponse> CreateAiJob([FromBody] AiJobRequest? request)
        {
            _logger.LogInformation(">>> [GitController] CreateAiJob called: Name={Name}, BaseBranch={BaseBranch}",
                request?.Name ?? "(auto)", request?.BaseBranch ?? "(auto)");

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
                // 获取意图（默认隔离环境）
                var purpose = request?.Purpose ?? WorktreePurpose.Isolation;

                // 自动生成名称（如果未提供）
                var name = request?.Name;
                if (string.IsNullOrEmpty(name))
                {
                    name = GenerateWorktreeName(projectPath, purpose);
                    _logger.LogInformation("自动生成 Worktree 名称: {Name} (Purpose={Purpose})", name, purpose);
                }

                // baseBranch 为空时自动获取当前分支
                var baseBranch = request?.BaseBranch;
                if (string.IsNullOrEmpty(baseBranch))
                {
                    baseBranch = _gitService.GetCurrentBranch(projectPath);
                    _logger.LogInformation("自动使用当前分支作为基准: {Branch}", baseBranch);
                }

                // 检查基准分支是否存在
                var allBranches = _gitService.GetAllBranches(projectPath);
                if (!allBranches.Contains(baseBranch))
                {
                    return NotFound(new { message = $"基准分支 '{baseBranch}' 不存在" });
                }

                // 自动生成分支名：feat/{name}-{yyyyMMdd-HHmmss}
                var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var branchName = $"feat/{name}-{timestamp}";

                // 自动存档：创建前检测到未提交更改，静默执行存档
                if (_gitService.HasUncommittedChanges(projectPath))
                {
                    _gitService.Commit(projectPath, $"自动存档_{DateTime.Now:yyyyMMdd_HHmmss}");
                    _logger.LogInformation("创建 AI Job 前自动存档");
                }

                // 创建 Worktree（复用现有逻辑，AI Job 使用隔离意图）
                var worktreePath = _gitService.CreateWorktree(projectPath, name, branchName, baseBranch, intent: "isolation");

                var result = new AiJobResponse
                {
                    Name = name,
                    WorktreePath = worktreePath,
                    BranchName = branchName
                };

                _logger.LogInformation("创建 AI Job 成功: Name={Name}, Branch={Branch}, Path={Path}",
                    name, branchName, worktreePath);

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建 AI Job 失败: {Name}", request?.Name ?? "(auto)");
                return StatusCode(500, new { message = $"创建 AI Job 失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 根据意图自动生成 Worktree 名称
        /// - Isolation: agent-main-job{n}-{ts}（Agent 任务）
        /// - Parallel: window-{n}-{ts}（虚拟窗口）
        /// </summary>
        private string GenerateWorktreeName(string projectPath, WorktreePurpose purpose)
        {
            var timestamp = DateTime.Now.ToString("HHmmss");
            var shortTs = timestamp.Substring(4);  // 取后2位
            var worktrees = _gitService.GetWorktrees(projectPath);
            var index = worktrees.Count;  // 使用现有 worktree 数量作为序号

            return purpose switch
            {
                WorktreePurpose.Isolation => $"agent-main-job{index}-{shortTs}",  // Agent 任务
                WorktreePurpose.Parallel => $"window-{index}-{shortTs}",          // 虚拟窗口
                _ => $"wt-{index}-{shortTs}"
            };
        }

        /// <summary>
        /// 标记 AI Job 完成
        /// 在 Worktree 中执行 git commit（如有修改），通知 Web 端供用户审查
        /// </summary>
        /// <param name="name">AI Job 名称（Worktree 名称）</param>
        /// <param name="request">完成请求（包含修改总结）</param>
        /// <returns>完成响应</returns>
        [HttpPost("ai-job/{name}/complete")]
        public ActionResult<AiJobCompleteResponse> CompleteAiJob(string name, [FromBody] AiJobCompleteRequest request)
        {
            _logger.LogInformation(">>> [GitController] CompleteAiJob called: Name={Name}, Summary={Summary}",
                name, request?.Summary ?? "(null)");

            if (string.IsNullOrEmpty(name))
            {
                return BadRequest(new { message = "AI Job 名称不能为空" });
            }

            if (string.IsNullOrEmpty(request?.Summary))
            {
                return BadRequest(new { message = "修改总结不能为空" });
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
                // 确认 Worktree 存在
                var worktreesDir = _gitService.GetWorktreesDir(projectPath);
                var worktreePath = System.IO.Path.Combine(worktreesDir, name);

                if (!System.IO.Directory.Exists(worktreePath))
                {
                    return NotFound(new { message = $"AI Job '{name}' 不存在（Worktree 未找到）" });
                }

                // 获取 Worktree 的分支名
                var branchName = _gitService.GetCurrentBranch(worktreePath);

                // 在 Worktree 中提交（如有未提交更改）
                var hasCommit = _gitService.TryCommit(worktreePath, $"AI Job 完成: {request.Summary}");

                if (hasCommit)
                {
                    _logger.LogInformation("AI Job '{Name}' 提交成功", name);
                }
                else
                {
                    _logger.LogInformation("AI Job '{Name}' 没有需要提交的更改", name);
                }

                // TODO: 通知 Web 端（通过 SignalR）
                // 后续实现：await _hubContext.Clients.All.SendAsync("AiJobCompleted", ...)

                var result = new AiJobCompleteResponse
                {
                    Success = true,
                    Message = "AI Job 已完成，等待用户审查",
                    HasCommit = hasCommit,
                    BranchName = branchName
                };

                _logger.LogInformation("AI Job 完成: Name={Name}, HasCommit={HasCommit}, Branch={Branch}",
                    name, hasCommit, branchName);

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "完成 AI Job 失败: {Name}", name);
                return StatusCode(500, new { message = $"完成 AI Job 失败: {ex.Message}" });
            }
        }

        #endregion

        #region Merge API

        /// <summary>
        /// 合并分支
        /// 支持三种模式：
        /// 1. 合并到主仓库当前分支（只传 sourceBranch）
        /// 2. 合并到主仓库指定分支（传 sourceBranch + targetBranch）
        /// 3. 在指定 Worktree 中合并（传 sourceBranch + worktreeName，用于场景 F）
        /// </summary>
        /// <param name="request">合并请求</param>
        /// <returns>合并结果</returns>
        [HttpPost("merge")]
        public ActionResult<MergeResultDto> MergeBranch([FromBody] MergeRequest request)
        {
            _logger.LogInformation(">>> [GitController] MergeBranch called: Source={Source}, Target={Target}, Worktree={Worktree}",
                request?.SourceBranch ?? "(null)",
                request?.TargetBranch ?? "(current)",
                request?.WorktreeName ?? "(main)");

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

                // 确定工作目录
                var workingDir = projectPath;
                string targetDesc;

                // 模式 1：在指定 Worktree 中执行合并（场景 F：目标分支已被 Worktree 检出）
                if (!string.IsNullOrEmpty(request.WorktreeName))
                {
                    var worktreesDir = _gitService.GetWorktreesDir(projectPath);
                    workingDir = System.IO.Path.Combine(worktreesDir, request.WorktreeName);

                    if (!System.IO.Directory.Exists(workingDir))
                    {
                        return NotFound(new { message = $"Worktree '{request.WorktreeName}' 不存在" });
                    }

                    // 检查 Worktree 中是否有未提交更改
                    if (_gitService.HasUncommittedChanges(workingDir))
                    {
                        return Conflict(new
                        {
                            message = $"Worktree '{request.WorktreeName}' 有未提交更改，无法执行合并",
                            hasUncommittedChanges = true
                        });
                    }

                    targetDesc = $"worktree:{request.WorktreeName}";
                    _logger.LogInformation("在 Worktree 中执行合并: {WorktreeName}", request.WorktreeName);
                }
                // 模式 2：在主仓库中切换到目标分支后合并
                else if (!string.IsNullOrEmpty(request.TargetBranch))
                {
                    // 检查目标分支是否存在
                    if (!allBranches.Contains(request.TargetBranch))
                    {
                        return NotFound(new { message = $"目标分支 '{request.TargetBranch}' 不存在" });
                    }

                    var currentBranch = _gitService.GetCurrentBranch(projectPath);
                    if (currentBranch != request.TargetBranch)
                    {
                        // 检查是否有未提交更改
                        if (_gitService.HasUncommittedChanges(projectPath))
                        {
                            return Conflict(new
                            {
                                message = "主仓库有未提交更改，无法切换到目标分支执行合并",
                                hasUncommittedChanges = true
                            });
                        }

                        _gitService.CheckoutBranch(projectPath, request.TargetBranch);
                        _logger.LogInformation("切换到目标分支: {Target}", request.TargetBranch);
                    }

                    targetDesc = request.TargetBranch;
                }
                // 模式 3：合并到主仓库当前分支
                else
                {
                    targetDesc = _gitService.GetCurrentBranch(projectPath);
                }

                // 执行合并
                var mergeResult = _gitService.MergeBranch(workingDir, request.SourceBranch, request.CommitMessage);

                var result = new MergeResultDto
                {
                    Success = mergeResult.Success,
                    HasConflicts = mergeResult.HasConflicts,
                    Message = mergeResult.Message
                };

                if (mergeResult.Success)
                {
                    _logger.LogInformation("合并分支成功: {Source} -> {Target}", request.SourceBranch, targetDesc);
                }
                else
                {
                    _logger.LogWarning("合并分支失败: {Source} -> {Target}, HasConflicts={HasConflicts}",
                        request.SourceBranch, targetDesc, mergeResult.HasConflicts);
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
