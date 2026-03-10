using BIMCanvas.Server.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// 项目文件监听服务
    /// 监听 schemes/ 目录下的文件变化，通过 SignalR 推送更新
    /// </summary>
    public class ProjectWatcherService : IHostedService, IDisposable
    {
        private readonly ILogger<ProjectWatcherService> _logger;
        private readonly ProjectContext _projectContext;
        private readonly IHubContext<CanvasHub> _hubContext;
        private readonly GitWorktreeService _gitService;
        private readonly ProjectService _projectService;

        private FileSystemWatcher? _watcher;
        private FileSystemWatcher? _projectWatcher; // 监控整个项目目录的 Git 状态变化
        private readonly object _lock = new();
        private CancellationTokenSource? _debounceCts;
        private CancellationTokenSource? _serviceCts;

        // Git 操作标志超时保护：记录首次检测到 Git 操作的时间
        private DateTime? _gitOperationStartTime;

        // 防抖时间：500ms（Agent 可能连续写入多个文件）
        private const int DebounceMs = 500;

        // 需要监听的文件
        private static readonly HashSet<string> WatchedFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            "modules.json",
            "zones.json",
            "finishes.json"
        };

        public ProjectWatcherService(
            ILogger<ProjectWatcherService> logger,
            ProjectContext projectContext,
            IHubContext<CanvasHub> hubContext,
            GitWorktreeService gitService,
            ProjectService projectService)
        {
            _logger = logger;
            _projectContext = projectContext;
            _hubContext = hubContext;
            _gitService = gitService;
            _projectService = projectService;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("ProjectWatcherService 启动中...");
            _serviceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // 启动后台任务：等待项目加载完成后开始监听
            _ = Task.Run(async () =>
            {
                while (!_serviceCts.Token.IsCancellationRequested)
                {
                    if (_projectContext.IsLoaded && _watcher == null)
                    {
                        StartWatching(_projectContext.CurrentProjectPath!);
                    }
                    else if (!_projectContext.IsLoaded && _watcher != null)
                    {
                        StopWatching();
                    }
                    await Task.Delay(1000, _serviceCts.Token);
                }
            }, _serviceCts.Token);

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("ProjectWatcherService 停止中...");
            _serviceCts?.Cancel();
            StopWatching();
            return Task.CompletedTask;
        }

        /// <summary>
        /// 开始监听项目目录
        /// </summary>
        private void StartWatching(string projectPath)
        {
            lock (_lock)
            {
                if (_watcher != null) return;

                var schemesPath = Path.Combine(projectPath, "schemes");
                if (!Directory.Exists(schemesPath))
                {
                    _logger.LogWarning("schemes 目录不存在: {Path}", schemesPath);
                    return;
                }

                // 监控 schemes 目录的 JSON 文件（用于 Canvas 数据刷新）
                _watcher = new FileSystemWatcher(schemesPath)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                    Filter = "*.json",
                    IncludeSubdirectories = true,  // 包含子目录（zones 等）
                    EnableRaisingEvents = true
                };

                _watcher.Changed += OnFileChanged;
                _watcher.Created += OnFileChanged;
                _watcher.Renamed += OnFileRenamed;

                _logger.LogInformation("开始监听项目文件: {Path}", schemesPath);

                // 监控整个项目目录（用于 Git 状态变化推送）
                _projectWatcher = new FileSystemWatcher(projectPath)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    Filter = "*.*",
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };

                _projectWatcher.Changed += OnProjectFileChanged;
                _projectWatcher.Created += OnProjectFileChanged;
                _projectWatcher.Deleted += OnProjectFileChanged;

                _logger.LogInformation("开始监听项目目录（Git 状态）: {Path}", projectPath);
            }
        }

        /// <summary>
        /// 停止监听
        /// </summary>
        private void StopWatching()
        {
            lock (_lock)
            {
                if (_watcher != null)
                {
                    _watcher.EnableRaisingEvents = false;
                    _watcher.Changed -= OnFileChanged;
                    _watcher.Created -= OnFileChanged;
                    _watcher.Renamed -= OnFileRenamed;
                    _watcher.Dispose();
                    _watcher = null;
                    _logger.LogInformation("停止监听项目文件");
                }

                if (_projectWatcher != null)
                {
                    _projectWatcher.EnableRaisingEvents = false;
                    _projectWatcher.Changed -= OnProjectFileChanged;
                    _projectWatcher.Created -= OnProjectFileChanged;
                    _projectWatcher.Deleted -= OnProjectFileChanged;
                    _projectWatcher.Dispose();
                    _projectWatcher = null;
                    _logger.LogInformation("停止监听项目目录（Git 状态）");
                }
            }
        }

        /// <summary>
        /// 项目文件变化事件处理（仅用于 Git 状态推送）
        /// </summary>
        private void OnProjectFileChanged(object sender, FileSystemEventArgs e)
        {
            // 忽略 .git 目录内部的变化（Git 自己的操作）
            if (e.FullPath.Contains(".git") || e.FullPath.Contains(".worktrees"))
            {
                return;
            }

            // 检查是否在 Git 操作期间（超时保护由 OnFileChanged 统一处理）
            if (_projectContext.IsGitOperationInProgress)
            {
                return;
            }

            _logger.LogDebug("检测到项目文件变化: {Path} ({ChangeType})", e.FullPath, e.ChangeType);
            ScheduleGitStatusUpdate();
        }

        /// <summary>
        /// 调度 Git 状态更新（带防抖）
        /// </summary>
        private CancellationTokenSource? _gitDebounceCts;
        private void ScheduleGitStatusUpdate()
        {
            lock (_lock)
            {
                _gitDebounceCts?.Cancel();
                _gitDebounceCts = new CancellationTokenSource();
                var token = _gitDebounceCts.Token;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(DebounceMs, token);
                        if (!token.IsCancellationRequested)
                        {
                            await BroadcastGitStatus();
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        // 被新的变化取消，正常
                    }
                }, token);
            }
        }

        /// <summary>
        /// 文件变化事件处理（带防抖）
        /// </summary>
        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            var fileName = Path.GetFileName(e.FullPath);

            // 检查是否是需要监听的文件
            if (!WatchedFiles.Contains(fileName))
            {
                return;
            }

            // 检查是否在 Git 操作期间（带 30 秒超时保护）
            if (_projectContext.IsGitOperationInProgress)
            {
                if (_gitOperationStartTime == null)
                {
                    _gitOperationStartTime = DateTime.UtcNow;
                }
                else if ((DateTime.UtcNow - _gitOperationStartTime.Value).TotalSeconds > 30)
                {
                    _logger.LogWarning("Git 操作标志超过 30 秒未重置，自动恢复文件监听");
                    _projectContext.IsGitOperationInProgress = false;
                    _gitOperationStartTime = null;
                    // 继续处理本次事件
                }
                else
                {
                    _logger.LogDebug("Git 操作进行中，跳过文件变化: {Path}", e.FullPath);
                    return;
                }
            }
            else
            {
                _gitOperationStartTime = null;
            }

            _logger.LogDebug("检测到文件变化: {Path} ({ChangeType})", e.FullPath, e.ChangeType);
            ScheduleUpdate(fileName);
        }

        /// <summary>
        /// 文件重命名事件处理
        /// </summary>
        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            var fileName = Path.GetFileName(e.FullPath);

            if (!WatchedFiles.Contains(fileName))
            {
                return;
            }

            if (_projectContext.IsGitOperationInProgress)
            {
                return;
            }

            _logger.LogDebug("检测到文件重命名: {OldPath} -> {NewPath}", e.OldFullPath, e.FullPath);
            ScheduleUpdate(fileName);
        }

        /// <summary>
        /// 调度更新（带防抖）
        /// </summary>
        private void ScheduleUpdate(string fileName)
        {
            lock (_lock)
            {
                // 取消之前的防抖定时器
                _debounceCts?.Cancel();
                _debounceCts = new CancellationTokenSource();
                var token = _debounceCts.Token;

                // 延迟执行
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(DebounceMs, token);
                        if (!token.IsCancellationRequested)
                        {
                            await BroadcastUpdate(fileName);
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        // 被新的变化取消，正常
                    }
                }, token);
            }
        }

        /// <summary>
        /// 广播更新给所有 Web 客户端
        /// </summary>
        private async Task BroadcastUpdate(string fileName)
        {
            if (!_projectContext.IsLoaded)
            {
                _logger.LogWarning("项目未加载，跳过广播");
                return;
            }

            try
            {
                // zones.json 变更时刷新分区目录结构（支持 Agent 写入 subZones 后自动创建子目录）
                if (string.Equals(fileName, "zones.json", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(_projectContext.CurrentProjectPath))
                {
                    _projectService.CreateZoneDirectories(_projectContext.CurrentProjectPath);
                    _logger.LogInformation("zones.json 变更，已刷新分区目录结构");
                }

                var updateMessage = new
                {
                    type = "file_changed",
                    file = fileName,
                    timestamp = DateTime.UtcNow,
                    action = "reload"  // 通知客户端重新加载数据
                };

                // 广播给所有客户端
                await _hubContext.Clients.All.SendAsync("ReceiveUpdate", updateMessage);

                _logger.LogInformation("已广播文件变化通知: {FileName}", fileName);

                // 同时广播 Git 状态变化
                await BroadcastGitStatus();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "广播更新失败");
            }
        }

        /// <summary>
        /// 广播 Git 状态给所有客户端
        /// </summary>
        public async Task BroadcastGitStatus()
        {
            if (!_projectContext.IsLoaded || string.IsNullOrEmpty(_projectContext.CurrentProjectPath))
            {
                return;
            }

            var projectPath = _projectContext.CurrentProjectPath;

            if (!_gitService.IsGitRepository(projectPath))
            {
                return;
            }

            try
            {
                var hasChanges = _gitService.HasUncommittedChanges(projectPath);
                var currentBranch = _gitService.GetCurrentBranch(projectPath);

                var gitStatus = new
                {
                    isLoaded = true,
                    isGitRepo = true,
                    hasUncommittedChanges = hasChanges,
                    currentBranch
                };

                await _hubContext.Clients.All.SendAsync("GitStatusChanged", gitStatus);
                _logger.LogDebug("已广播 Git 状态: hasChanges={HasChanges}, branch={Branch}", hasChanges, currentBranch);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "广播 Git 状态失败");
            }
        }

        public void Dispose()
        {
            _serviceCts?.Cancel();
            _serviceCts?.Dispose();
            _debounceCts?.Dispose();
            _gitDebounceCts?.Dispose();
            StopWatching();
        }
    }
}
