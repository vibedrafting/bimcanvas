namespace BIMCanvas.Server.Models;

/// <summary>
/// Server 启动配置（Documents/BIMCanvas/server_config.json）
/// </summary>
public class ServerConfig
{
    public ServerSection Server { get; set; } = new();
    public StartupSection Startup { get; set; } = new();
}

/// <summary>
/// 服务端口配置
/// </summary>
public class ServerSection
{
    /// <summary>
    /// Agent 服务端口（默认 8765）
    /// </summary>
    public int Port { get; set; } = 8765;
}

/// <summary>
/// 启动行为配置
/// </summary>
public class StartupSection
{
    /// <summary>
    /// 默认加载的 .bcp 文件路径，null 则使用 demo_1
    /// </summary>
    public string? DefaultProject { get; set; }

    /// <summary>
    /// 是否自动打开浏览器（默认 true）
    /// </summary>
    public bool OpenBrowser { get; set; } = true;

    /// <summary>
    /// 浏览器可执行文件路径，null 则使用系统默认
    /// </summary>
    public string? BrowserPath { get; set; }
}
