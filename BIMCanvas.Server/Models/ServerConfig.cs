namespace BIMCanvas.Server.Models;

/// <summary>
/// Server 启动配置（Documents/BIMCanvas/server_config.json）
/// </summary>
public class ServerConfig
{
    public ServerSection Server { get; set; } = new();
    public StartupSection Startup { get; set; } = new();
    public LiteLlmSection LiteLlm { get; set; } = new();
    public ProviderAdapterSection ProviderAdapter { get; set; } = new();
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
    /// LiteLLM 模式下的默认模型家族（opus/sonnet/haiku）
    /// </summary>
    public string DefaultModelFamily { get; set; } = "sonnet";

    /// <summary>
    /// LiteLLM 配置文件名（位于 Documents/BIMCanvas 下）
    /// </summary>
    public string ConfigFileName { get; set; } = "litellm_config.yaml";
}

/// <summary>
/// 下游供应商适配层配置
/// </summary>
public class ProviderAdapterSection
{
    /// <summary>
    /// 是否启用 ProviderAdapter
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 是否由 Server 自动启动 ProviderAdapter
    /// </summary>
    public bool AutoStart { get; set; } = true;

    /// <summary>
    /// ProviderAdapter 监听主机
    /// </summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>
    /// ProviderAdapter 监听端口
    /// </summary>
    public int Port { get; set; } = 4101;

    /// <summary>
    /// LiteLLM 运行时配置文件名（位于 Documents/BIMCanvas 下）
    /// </summary>
    public string RuntimeLiteLlmConfigFileName { get; set; } = "litellm_runtime.generated.yaml";

    /// <summary>
    /// ProviderAdapter 运行时配置文件名（位于 Documents/BIMCanvas 下）
    /// </summary>
    public string RuntimeAdapterConfigFileName { get; set; } = "provider_adapter_runtime.generated.json";

    /// <summary>
    /// Provider 路由映射配置
    /// </summary>
    public List<ProviderAdapterMapping> Mappings { get; set; } = ProviderAdapterDefaults.CreateMappings();
}

/// <summary>
/// Provider 路由映射规则
/// </summary>
public class ProviderAdapterMapping
{
    /// <summary>
    /// Provider 别名，需与 bc-{providerAlias}-{family} 对齐
    /// </summary>
    public string ProviderAlias { get; set; } = "";

    /// <summary>
    /// 路由模式：direct / adapter
    /// </summary>
    public string Mode { get; set; } = "direct";

    /// <summary>
    /// Adapter profile 名称
    /// </summary>
    public string? Profile { get; set; }
}

internal static class ProviderAdapterDefaults
{
    public static List<ProviderAdapterMapping> CreateMappings()
    {
        return
        [
            new ProviderAdapterMapping
            {
                ProviderAlias = "gemini_yescode",
                Mode = "adapter",
                Profile = "gemini_native_v1beta"
            },
            new ProviderAdapterMapping
            {
                ProviderAlias = "openai_yescode",
                Mode = "adapter",
                Profile = "openai_compatible_v1"
            },
            new ProviderAdapterMapping
            {
                ProviderAlias = "anthropic",
                Mode = "direct"
            },
            new ProviderAdapterMapping
            {
                ProviderAlias = "openai",
                Mode = "direct"
            },
            new ProviderAdapterMapping
            {
                ProviderAlias = "vertex_ai",
                Mode = "direct"
            }
        ];
    }
}
