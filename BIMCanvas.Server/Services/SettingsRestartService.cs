using System.Threading;
using Microsoft.Extensions.Hosting;

namespace BIMCanvas.Server.Services;

/// <summary>
/// 统一配置页触发的实例重启协调器。
/// 写入重启标志文件后优雅停机，由 Program.cs 尾部逻辑检测标志并启动新实例。
/// Docker 环境下由 restart policy 接管。
/// </summary>
public sealed class SettingsRestartService
{
    /// <summary>重启标志文件路径（临时目录，跨进程可见）</summary>
    public static readonly string RestartFlagPath =
        Path.Combine(Path.GetTempPath(), "bimcanvas_restart.flag");

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
            _logger.LogWarning("收到实例重启请求，写入重启标志并准备停止当前进程。");

            // 写入重启标志文件，app.Run() 退出后由 Program.cs 检测并启动新实例
            File.WriteAllText(RestartFlagPath, DateTime.Now.ToString("o"));

            _appLifetime.StopApplication();
        });

        return true;
    }
}
