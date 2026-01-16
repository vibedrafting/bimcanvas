namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// 孤儿 Worktree 清理服务
    /// 在 Server 启动时扫描并清理未被任何窗口锁定的 Worktree
    /// 用于处理浏览器崩溃/强制关闭后遗留的 Worktree
    /// </summary>
    public class OrphanWorktreeCleanupService : IHostedService
    {
        private readonly ILogger<OrphanWorktreeCleanupService> _logger;
        private readonly GitWorktreeService _gitWorktreeService;
        private readonly ProjectContext _projectContext;

        public OrphanWorktreeCleanupService(
            ILogger<OrphanWorktreeCleanupService> logger,
            GitWorktreeService gitWorktreeService,
            ProjectContext projectContext)
        {
            _logger = logger;
            _gitWorktreeService = gitWorktreeService;
            _projectContext = projectContext;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // 启动时执行一次清理（延迟执行，确保 ProjectContext 已初始化）
            _ = Task.Run(async () =>
            {
                // 等待一小段时间让项目加载完成
                await Task.Delay(2000, cancellationToken);
                CleanupOrphanWorktrees();
            }, cancellationToken);

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// 清理孤儿 Worktree
        ///
        /// 清理策略：Server 重启后，直接清理所有以 "window-" 开头的 Worktree
        /// 原因：Server 重启后 BranchLockManager 是空的，所有客户端都会重新连接，
        /// 因此 window-* 的 Worktree 都是残留的孤儿
        /// </summary>
        private void CleanupOrphanWorktrees()
        {
            var projectPath = _projectContext.CurrentProjectPath;
            if (string.IsNullOrEmpty(projectPath))
            {
                _logger.LogDebug("项目未加载，跳过孤儿 Worktree 清理");
                return;
            }

            try
            {
                var worktrees = _gitWorktreeService.GetWorktrees(projectPath);
                var worktreesDir = _gitWorktreeService.GetWorktreesDir(projectPath);
                var cleanedCount = 0;

                foreach (var wt in worktrees)
                {
                    // 跳过主仓库
                    if (!wt.Path.StartsWith(worktreesDir, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var worktreeName = Path.GetFileName(wt.Path);

                    // 直接清理所有 window-* 开头的 Worktree
                    // Server 重启后没有活跃窗口，这些都是残留的孤儿
                    if (worktreeName.StartsWith("window-", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("清理孤儿 Worktree: {Name} (分支: {Branch})", worktreeName, wt.Branch ?? "(detached)");

                        try
                        {
                            _gitWorktreeService.RemoveWorktree(projectPath, worktreeName, deleteBranch: false);
                            cleanedCount++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "清理孤儿 Worktree 失败: {Name}", worktreeName);
                        }
                    }
                }

                if (cleanedCount > 0)
                {
                    _logger.LogInformation("启动时清理了 {Count} 个孤儿 Worktree", cleanedCount);
                }
                else
                {
                    _logger.LogDebug("未发现孤儿 Worktree");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "孤儿 Worktree 清理服务执行失败");
            }
        }
    }
}
