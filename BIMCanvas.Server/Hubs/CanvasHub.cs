using BIMCanvas.Server.Services;
using BIMCanvas.Server.Services.Git;
using Microsoft.AspNetCore.SignalR;

namespace BIMCanvas.Server.Hubs
{
    /// <summary>
    /// 画布实时通信 Hub
    /// 用于向 Web 客户端推送项目数据更新
    /// </summary>
    public class CanvasHub : Hub
    {
        private readonly ILogger<CanvasHub> _logger;
        private readonly BranchLockManager _branchLockManager;
        private readonly GitWorktreeService _gitWorktreeService;
        private readonly ProjectContext _projectContext;

        public CanvasHub(
            ILogger<CanvasHub> logger,
            BranchLockManager branchLockManager,
            GitWorktreeService gitWorktreeService,
            ProjectContext projectContext)
        {
            _logger = logger;
            _branchLockManager = branchLockManager;
            _gitWorktreeService = gitWorktreeService;
            _projectContext = projectContext;
        }

        /// <summary>
        /// 客户端连接时调用
        /// </summary>
        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation("SignalR 客户端已连接: {ConnectionId}", Context.ConnectionId);

            // 推送初始 Git 状态给新连接的客户端
            await PushGitStatusAsync();

            await base.OnConnectedAsync();
        }

        /// <summary>
        /// 推送当前 Git 状态给调用者
        /// </summary>
        private async Task PushGitStatusAsync()
        {
            if (!_projectContext.IsLoaded || string.IsNullOrEmpty(_projectContext.CurrentProjectPath))
            {
                await Clients.Caller.SendAsync("GitStatusChanged", new
                {
                    isLoaded = false,
                    hasUncommittedChanges = false
                });
                return;
            }

            var projectPath = _projectContext.CurrentProjectPath;

            if (!_gitWorktreeService.IsGitRepository(projectPath))
            {
                await Clients.Caller.SendAsync("GitStatusChanged", new
                {
                    isLoaded = true,
                    isGitRepo = false,
                    hasUncommittedChanges = false
                });
                return;
            }

            try
            {
                var hasChanges = _gitWorktreeService.HasUncommittedChanges(projectPath);
                var currentBranch = _gitWorktreeService.GetCurrentBranch(projectPath);

                await Clients.Caller.SendAsync("GitStatusChanged", new
                {
                    isLoaded = true,
                    isGitRepo = true,
                    hasUncommittedChanges = hasChanges,
                    currentBranch
                });

                _logger.LogDebug("推送 Git 状态: hasChanges={HasChanges}, branch={Branch}", hasChanges, currentBranch);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取 Git 状态失败");
            }
        }

        /// <summary>
        /// 客户端断开时调用
        /// 清理关联的窗口资源（分支锁 + Worktree）
        /// </summary>
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var windowId = Context.Items["WindowId"] as string;

            if (!string.IsNullOrEmpty(windowId))
            {
                _logger.LogInformation("清理断开连接的窗口: {WindowId}", windowId);

                // 1. 获取该窗口锁定的所有分支（在释放锁之前获取）
                var lockedBranches = _branchLockManager.GetBranchesLockedByWindow(windowId).ToList();

                // 2. 释放该窗口的所有分支锁
                var releasedCount = _branchLockManager.ReleaseAllForWindow(windowId);
                _logger.LogInformation("释放分支锁: {Count} 个", releasedCount);

                // 3. 清理关联的 Worktree（只清理虚拟窗口创建的）
                var projectPath = _projectContext.CurrentProjectPath;
                if (!string.IsNullOrEmpty(projectPath))
                {
                    foreach (var branch in lockedBranches)
                    {
                        // 查找关联的 Worktree 并删除（只清理 .worktrees/ 目录下的虚拟窗口 Worktree）
                        try
                        {
                            var worktrees = _gitWorktreeService.GetWorktrees(projectPath);
                            var worktreeToRemove = worktrees.FirstOrDefault(w =>
                                w.Branch == branch && w.Path.Contains(".worktrees"));

                            if (worktreeToRemove != null)
                            {
                                var worktreeName = Path.GetFileName(worktreeToRemove.Path);
                                _gitWorktreeService.RemoveWorktree(projectPath, worktreeName, deleteBranch: false);
                                _logger.LogInformation("清理 Worktree: {Name} (分支: {Branch})", worktreeName, branch);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "清理 Worktree 失败: 分支 {Branch}", branch);
                        }
                    }
                }
            }

            _logger.LogInformation("SignalR 客户端已断开: {ConnectionId}", Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// 注册窗口并获取分支锁（用于断开连接时清理资源）
        /// </summary>
        /// <param name="windowId">窗口 ID</param>
        /// <param name="branchName">分支名（可选，用于获取分支锁）</param>
        /// <returns>是否成功（分支锁获取结果）</returns>
        public Task<bool> RegisterWindow(string windowId, string? branchName = null)
        {
            Context.Items["WindowId"] = windowId;
            _logger.LogInformation("窗口注册: {WindowId} -> {ConnectionId}", windowId, Context.ConnectionId);

            // 如果提供了分支名，自动获取分支锁
            if (!string.IsNullOrEmpty(branchName))
            {
                var success = _branchLockManager.TryAcquire(branchName, windowId);
                if (success)
                {
                    _logger.LogInformation("自动获取分支锁: {Branch} -> {WindowId}", branchName, windowId);
                }
                else
                {
                    var owner = _branchLockManager.GetOwner(branchName);
                    _logger.LogWarning("分支锁获取失败: {Branch} 已被 {Owner} 锁定", branchName, owner);
                }
                return Task.FromResult(success);
            }

            return Task.FromResult(true);
        }

        /// <summary>
        /// 客户端发送更新（双向通信）
        /// </summary>
        public async Task SendUpdate(object data)
        {
            _logger.LogDebug("收到客户端更新: {ConnectionId}", Context.ConnectionId);
            // 广播给所有其他客户端
            await Clients.Others.SendAsync("ReceiveUpdate", data);
        }
    }
}
