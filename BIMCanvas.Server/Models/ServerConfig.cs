namespace BIMCanvas.Server.Models;

/// <summary>
/// Server 启动配置（<BIMCANVAS_HOME>/server_config.json）
/// </summary>
public class ServerConfig
{
    public ServerSection Server { get; set; } = new();
    public WebSection Web { get; set; } = new();
    public AgentSection Agent { get; set; } = new();
    public StartupSection Startup { get; set; } = new();
    public CcrSection Ccr { get; set; } = new();
}

/// <summary>
/// 服务端口配置
/// </summary>
public class ServerSection
{
    /// <summary>
    /// BIMCanvas.Server HTTP 监听端口
    /// </summary>
    public int Port { get; set; } = 5000;
}

/// <summary>
/// Web 开发服务器配置
/// </summary>
public class WebSection
{
    /// <summary>
    /// Development 模式下 Vite dev server 首选端口
    /// </summary>
    public int Port { get; set; } = 5173;
}

/// <summary>
/// Agent 连接与托管配置
/// </summary>
public class AgentSection
{
    /// <summary>
    /// 是否由 Server 自动启动本地 Agent 进程
    /// </summary>
    public bool AutoStart { get; set; } = true;

    /// <summary>
    /// Agent 基址。为空时回退到 http://127.0.0.1:{Port}
    /// </summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>
    /// Agent 健康检查路径
    /// </summary>
    public string HealthPath { get; set; } = "/health";

    /// <summary>
    /// 托管启动 Agent 时使用的监听端口
    /// </summary>
    public int Port { get; set; } = 8865;

    /// <summary>
    /// 托管启动 Agent 时使用的 Python 命令
    /// </summary>
    public string PythonCommand { get; set; } = "python";

    public string GetResolvedBaseUrl()
    {
        var normalized = (BaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        var port = Port > 0 ? Port : 8865;
        return $"http://127.0.0.1:{port}";
    }

    public string GetResolvedHealthPath()
    {
        var path = (HealthPath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/health";
        }

        return path.StartsWith('/') ? path : $"/{path}";
    }

    public int GetResolvedPort()
    {
        return Port > 0 ? Port : 8865;
    }

    public string GetResolvedPythonCommand()
    {
        var pythonCommand = (PythonCommand ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(pythonCommand))
        {
            return pythonCommand;
        }

        return "python";
    }
}

/// <summary>
/// 启动行为配置
/// </summary>
public class StartupSection
{
    /// <summary>
    /// 是否自动打开浏览器（默认 true）
    /// </summary>
    public bool OpenBrowser { get; set; } = true;

    /// <summary>
    /// 浏览器可执行文件路径，null 则使用系统默认
    /// </summary>
    public string? BrowserPath { get; set; }
}

/// <summary>
/// Claude Code Router (CCR) 配置，唯一 API 网关
/// </summary>
public class CcrSection
{
    /// <summary>
    /// 是否启用 CCR 网关
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 是否由 Server 自动启动 CCR
    /// </summary>
    public bool AutoStart { get; set; } = true;

    /// <summary>
    /// CCR 监听主机
    /// </summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>
    /// CCR 监听端口
    /// </summary>
    public int Port { get; set; } = 3456;

    /// <summary>
    /// CCR 配置文件名（位于 <BIMCANVAS_HOME> 下）
    /// </summary>
    public string ConfigFileName { get; set; } = "ccr_config.json";
}
