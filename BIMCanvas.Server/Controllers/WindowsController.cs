using System;
using System.Collections.Generic;
using System.Linq;
using BIMCanvas.Server.Services;
using BIMCanvas.Server.Services.Git;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BIMCanvas.Server.Controllers
{
    /// <summary>
    /// 窗口管理 API
    /// 用于多窗口场景中的分支锁管理和 Worktree 隔离
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class WindowsController : ControllerBase
    {
        private readonly ILogger<WindowsController> _logger;
        private readonly BranchLockManager _lockManager;
        private readonly ProjectContext _projectContext;

        public WindowsController(
            ILogger<WindowsController> logger,
            BranchLockManager lockManager,
            ProjectContext projectContext)
        {
            _logger = logger;
            _lockManager = lockManager;
            _projectContext = projectContext;
        }

        /// <summary>
        /// 获取所有分支锁
        /// </summary>
        /// <returns>锁信息列表</returns>
        [HttpGet("locks")]
        public ActionResult<List<BranchLockInfo>> GetAllLocks()
        {
            _logger.LogDebug(">>> [WindowsController] GetAllLocks called");

            var locks = _lockManager.GetAllLocks();
            var result = locks.Select(kvp => new BranchLockInfo
            {
                Branch = kvp.Key,
                WindowId = kvp.Value
            }).ToList();

            return Ok(result);
        }

        /// <summary>
        /// 获取指定分支的锁信息
        /// </summary>
        /// <param name="branch">分支名</param>
        /// <returns>锁信息</returns>
        [HttpGet("locks/{branch}")]
        public ActionResult<BranchLockInfo?> GetLock(string branch)
        {
            var owner = _lockManager.GetOwner(branch);
            if (owner == null)
            {
                return Ok(new { isLocked = false });
            }

            return Ok(new BranchLockInfo
            {
                Branch = branch,
                WindowId = owner
            });
        }

        /// <summary>
        /// 申请分支锁
        /// </summary>
        /// <param name="request">锁请求</param>
        /// <returns>是否成功</returns>
        [HttpPost("lock")]
        public ActionResult AcquireLock([FromBody] BranchLockRequest request)
        {
            _logger.LogInformation(">>> [WindowsController] AcquireLock: Branch={Branch}, Window={Window}",
                request?.BranchName ?? "(null)", request?.WindowId ?? "(null)");

            if (string.IsNullOrEmpty(request?.BranchName))
            {
                return BadRequest(new { success = false, message = "分支名不能为空" });
            }

            if (string.IsNullOrEmpty(request.WindowId))
            {
                return BadRequest(new { success = false, message = "窗口 ID 不能为空" });
            }

            var success = _lockManager.TryAcquire(request.BranchName, request.WindowId);

            if (success)
            {
                _logger.LogInformation("分支锁获取成功: {Branch} -> {Window}",
                    request.BranchName, request.WindowId);
                return Ok(new { success = true });
            }
            else
            {
                var owner = _lockManager.GetOwner(request.BranchName);
                _logger.LogWarning("分支锁获取失败: {Branch} 已被 {Owner} 锁定",
                    request.BranchName, owner);
                return Conflict(new
                {
                    success = false,
                    message = $"分支 '{request.BranchName}' 已被其他窗口锁定",
                    lockedBy = owner
                });
            }
        }

        /// <summary>
        /// 释放分支锁
        /// </summary>
        /// <param name="request">锁请求</param>
        /// <returns>是否成功</returns>
        [HttpDelete("lock")]
        public ActionResult ReleaseLock([FromBody] BranchLockRequest request)
        {
            _logger.LogInformation(">>> [WindowsController] ReleaseLock: Branch={Branch}, Window={Window}",
                request?.BranchName ?? "(null)", request?.WindowId ?? "(null)");

            if (string.IsNullOrEmpty(request?.BranchName))
            {
                return BadRequest(new { success = false, message = "分支名不能为空" });
            }

            if (string.IsNullOrEmpty(request.WindowId))
            {
                return BadRequest(new { success = false, message = "窗口 ID 不能为空" });
            }

            var success = _lockManager.Release(request.BranchName, request.WindowId);

            if (success)
            {
                _logger.LogInformation("分支锁释放成功: {Branch}", request.BranchName);
                return Ok(new { success = true });
            }
            else
            {
                _logger.LogWarning("分支锁释放失败: {Branch} (非持有者或不存在)", request.BranchName);
                return BadRequest(new
                {
                    success = false,
                    message = "无法释放锁：您不是锁的持有者或锁不存在"
                });
            }
        }

        /// <summary>
        /// 释放窗口持有的所有锁（窗口关闭时调用）
        /// </summary>
        /// <param name="windowId">窗口 ID</param>
        /// <returns>释放的锁数量</returns>
        [HttpDelete("locks/window/{windowId}")]
        public ActionResult ReleaseWindowLocks(string windowId)
        {
            _logger.LogInformation(">>> [WindowsController] ReleaseWindowLocks: {Window}", windowId);

            if (string.IsNullOrEmpty(windowId))
            {
                return BadRequest(new { success = false, message = "窗口 ID 不能为空" });
            }

            var count = _lockManager.ReleaseAllForWindow(windowId);

            _logger.LogInformation("释放窗口 {Window} 的所有锁: {Count} 个", windowId, count);
            return Ok(new { success = true, releasedCount = count });
        }

        /// <summary>
        /// 获取指定窗口锁定的分支列表
        /// </summary>
        /// <param name="windowId">窗口 ID</param>
        /// <returns>分支列表</returns>
        [HttpGet("locks/window/{windowId}")]
        public ActionResult<List<string>> GetWindowBranches(string windowId)
        {
            var branches = _lockManager.GetBranchesLockedByWindow(windowId).ToList();
            return Ok(branches);
        }

        /// <summary>
        /// 检查分支是否可用（未被锁定）
        /// </summary>
        /// <param name="branch">分支名</param>
        /// <param name="windowId">可选的窗口 ID（检查是否被自己锁定）</param>
        /// <returns>可用性信息</returns>
        [HttpGet("available/{branch}")]
        public ActionResult CheckAvailability(string branch, [FromQuery] string? windowId = null)
        {
            var isLocked = _lockManager.IsLocked(branch);

            if (!isLocked)
            {
                return Ok(new { available = true });
            }

            // 如果被锁定，检查是否是自己锁的
            if (!string.IsNullOrEmpty(windowId))
            {
                var isLockedBySelf = _lockManager.IsLockedBy(branch, windowId);
                if (isLockedBySelf)
                {
                    return Ok(new { available = true, reason = "self" });
                }
            }

            var owner = _lockManager.GetOwner(branch);
            return Ok(new
            {
                available = false,
                lockedBy = owner
            });
        }
        // ========== Worktree 隔离相关端点 ==========

        /// <summary>
        /// 激活窗口（切换当前工作的 Worktree）
        /// 前端窗口切换时调用，让 Server 知道当前激活的窗口
        /// </summary>
        [HttpPost("activate")]
        public ActionResult ActivateWindow([FromBody] ActivateWindowRequest request)
        {
            if (string.IsNullOrEmpty(request?.WindowId))
            {
                return BadRequest(new { message = "窗口 ID 不能为空" });
            }

            // 更新 ProjectContext 的活跃窗口
            _projectContext.ActiveWindowId = request.WindowId;

            // 获取该窗口的 Worktree 路径
            var worktreePath = _projectContext.GetWorktreePath(request.WindowId);

            _logger.LogInformation("激活窗口: {WindowId}, Worktree: {Path}",
                request.WindowId, worktreePath ?? "(主仓库)");

            return Ok(new
            {
                success = true,
                windowId = request.WindowId,
                worktreePath = worktreePath
            });
        }

        /// <summary>
        /// 注册窗口的 Worktree（创建虚拟窗口时调用）
        /// </summary>
        [HttpPost("register-worktree")]
        public ActionResult RegisterWorktree([FromBody] RegisterWorktreeRequest request)
        {
            if (string.IsNullOrEmpty(request?.WindowId) || string.IsNullOrEmpty(request?.WorktreePath))
            {
                return BadRequest(new { message = "窗口 ID 和 Worktree 路径不能为空" });
            }

            _projectContext.RegisterWindowWorktree(request.WindowId, request.WorktreePath);

            _logger.LogInformation("注册窗口 Worktree: {WindowId} -> {Path}",
                request.WindowId, request.WorktreePath);

            return Ok(new { success = true });
        }

        /// <summary>
        /// 注销窗口的 Worktree（关闭虚拟窗口时调用）
        /// </summary>
        [HttpDelete("worktree/{windowId}")]
        public ActionResult UnregisterWorktree(string windowId)
        {
            _projectContext.UnregisterWindowWorktree(windowId);

            // 如果关闭的是当前激活窗口，清空 ActiveWindowId
            if (_projectContext.ActiveWindowId == windowId)
            {
                _projectContext.ActiveWindowId = null;
            }

            _logger.LogInformation("注销窗口 Worktree: {WindowId}", windowId);

            return Ok(new { success = true });
        }

        /// <summary>
        /// 获取当前激活窗口信息
        /// </summary>
        [HttpGet("active")]
        public ActionResult GetActiveWindow()
        {
            var activeWindowId = _projectContext.ActiveWindowId;
            var worktreePath = _projectContext.GetActiveWorktreePath();

            return Ok(new
            {
                activeWindowId = activeWindowId,
                worktreePath = worktreePath
            });
        }
    }

    #region DTOs

    /// <summary>
    /// 分支锁信息
    /// </summary>
    public class BranchLockInfo
    {
        /// <summary>
        /// 分支名
        /// </summary>
        public string Branch { get; set; } = string.Empty;

        /// <summary>
        /// 持有锁的窗口 ID
        /// </summary>
        public string WindowId { get; set; } = string.Empty;
    }

    /// <summary>
    /// 分支锁请求
    /// </summary>
    public class BranchLockRequest
    {
        /// <summary>
        /// 分支名
        /// </summary>
        public string BranchName { get; set; } = string.Empty;

        /// <summary>
        /// 窗口 ID
        /// </summary>
        public string WindowId { get; set; } = string.Empty;
    }

    /// <summary>
    /// 激活窗口请求
    /// </summary>
    public class ActivateWindowRequest
    {
        /// <summary>
        /// 窗口 ID
        /// </summary>
        public string WindowId { get; set; } = string.Empty;
    }

    /// <summary>
    /// 注册 Worktree 请求
    /// </summary>
    public class RegisterWorktreeRequest
    {
        /// <summary>
        /// 窗口 ID
        /// </summary>
        public string WindowId { get; set; } = string.Empty;

        /// <summary>
        /// Worktree 绝对路径
        /// </summary>
        public string WorktreePath { get; set; } = string.Empty;
    }

    #endregion
}
