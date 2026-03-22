namespace BIMCanvas.Server.Models;

/// <summary>
/// Server 启动配置（Documents/BIMCanvas/server_config.json）
/// </summary>
public class ServerConfig
{
    public ServerSection Server { get; set; } = new();
    public StartupSection Startup { get; set; } = new();
    public LiteLlmSection LiteLlm { get; set; } = new();
}

/// <summary>
/// 服务端口配置
/// </summary>
public class ServerSection
{
    /// <summary>
    /// Agent 服务端口（默认 8865）
    /// </summary>
    public int Port { get; set; } = 8865;
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

/// <summary>
/// LiteLLM 启动配置
/// </summary>
public class LiteLlmSection
{
    /// <summary>
    /// 是否启用 LiteLLM 网关
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 是否由 Server 自动启动 LiteLLM
    /// </summary>
    public bool AutoStart { get; set; } = true;

    /// <summary>
    /// LiteLLM 监听主机
    /// </summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>
    /// LiteLLM 监听端口
    /// </summary>
    public int Port { get; set; } = 4000;

    /// <summary>
    /// Python 命令
    /// </summary>
    public string PythonCommand { get; set; } = "python";

    /// <summary>
    /// 当前激活的下游供应商
    /// </summary>
    public string ActiveProvider { get; set; } = "anthropic";

    /// <summary>
    /// 默认模型家族（opus/sonnet/haiku）
    /// </summary>
    public string DefaultModelFamily { get; set; } = "sonnet";

    /// <summary>
    /// LiteLLM 配置文件名（位于 Documents/BIMCanvas 下）
    /// </summary>
    public string ConfigFileName { get; set; } = "litellm_config.yaml";
}
