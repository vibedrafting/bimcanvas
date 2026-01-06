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

        private FileSystemWatcher? _watcher;
        private readonly object _lock = new();
        private CancellationTokenSource? _debounceCts;
        private CancellationTokenSource? _serviceCts;

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
            IHubContext<CanvasHub> hubContext)
        {
            _logger = logger;
            _projectContext = projectContext;
            _hubContext = hubContext;
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

                _watcher = new FileSystemWatcher(schemesPath)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                    Filter = "*.json",
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };

                _watcher.Changed += OnFileChanged;
                _watcher.Created += OnFileChanged;
                _watcher.Renamed += OnFileRenamed;

                _logger.LogInformation("开始监听项目文件: {Path}", schemesPath);
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

            // 检查是否在 Git 操作期间
            if (_projectContext.IsGitOperationInProgress)
            {
                _logger.LogDebug("Git 操作进行中，跳过文件变化: {Path}", e.FullPath);
                return;
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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "广播更新失败");
            }
        }

        public void Dispose()
        {
            _serviceCts?.Cancel();
            _serviceCts?.Dispose();
            _debounceCts?.Dispose();
            StopWatching();
        }
    }
}
