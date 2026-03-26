using System.Threading;
using Microsoft.Extensions.Hosting;

namespace BIMCanvas.Server.Services;

/// <summary>
/// 统一配置页触发的实例重启协调器。
/// 通过优雅停机让 Docker restart policy 接管重启。
/// </summary>
public sealed class SettingsRestartService
{
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly ILogger<SettingsRestartService> _logger;
    private int _restartRequested;

    public SettingsRestartService(
        IHostApplicationLifetime appLifetime,
        ILogger<SettingsRestartService> logger)
    {
        _appLifetime = appLifetime;
        _logger = logger;
    }

    public bool TryScheduleRestart()
    {
        if (Interlocked.Exchange(ref _restartRequested, 1) == 1)
        {
            return false;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(800);
            _logger.LogWarning("收到实例重启请求，准备停止当前进程。");
            _appLifetime.StopApplication();
        });

        return true;
    }
}
