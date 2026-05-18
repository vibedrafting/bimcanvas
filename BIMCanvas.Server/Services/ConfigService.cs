using BIMCanvas.Server.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BIMCanvas.Server.Services;

/// <summary>
/// 程序配置服务（BIMCANVAS_HOME）。
/// 仅负责统一路径解析与配置读写，不负责模板初始化。
/// 序列化栈:Newtonsoft.Json + <see cref="CamelCasePropertyNamesContractResolver"/>(全项目约束,见 CLAUDE.md)。
/// </summary>
public static class ConfigService
{
    private static readonly string ConfigDir = ResolveConfigDir();

    private static readonly string ServerConfigPath = Path.Combine(ConfigDir, "server_config.json");
    private static readonly string WebConfigPath = Path.Combine(ConfigDir, "web_config.json");
    private static readonly string AgentConfigPath = Path.Combine(ConfigDir, "config.json");
    private static readonly string CcrConfigPath = Path.Combine(ConfigDir, "ccr_config.json");
    private static readonly string DevLocalAgentConfigPath = Path.Combine(ConfigDir, "config.dev.local.json");
    private static readonly string DevLocalCcrConfigPath = Path.Combine(ConfigDir, "ccr_config.dev.local.json");

    private static readonly JsonSerializerSettings ReadSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
    };

    private static readonly JsonSerializerSettings WriteSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        Formatting = Formatting.Indented,
    };

    /// <summary>
    /// 加载 Server 启动配置，不存在时返回默认配置
    /// </summary>
    public static ServerConfig Load()
    {
        if (!File.Exists(ServerConfigPath))
            return new ServerConfig();

        try
        {
            var json = File.ReadAllText(ServerConfigPath);
            return JsonConvert.DeserializeObject<ServerConfig>(json, ReadSettings) ?? new ServerConfig();
        }
        catch (Exception)
        {
            return new ServerConfig();
        }
    }

    /// <summary>
    /// 加载 Web 客户端配置
    /// </summary>
    public static WebConfig LoadWebConfig()
    {
        if (!File.Exists(WebConfigPath))
            return new WebConfig();

        try
        {
            var json = File.ReadAllText(WebConfigPath);
            return JsonConvert.DeserializeObject<WebConfig>(json, ReadSettings) ?? new WebConfig();
        }
        catch (Exception)
        {
            return new WebConfig();
        }
    }

    /// <summary>
    /// 加载 Web 客户端配置的原始 JSON（供 API 直接返回，避免序列化器转换字典键名）
    /// </summary>
    public static string LoadWebConfigRaw()
    {
        if (!File.Exists(WebConfigPath))
            return "{}";

        try
        {
            return File.ReadAllText(WebConfigPath);
        }
        catch (Exception)
        {
            return "{}";
        }
    }

    /// <summary>
    /// 保存 Web 客户端配置
    /// </summary>
    public static void SaveWebConfig(WebConfig config)
    {
        Directory.CreateDirectory(ConfigDir);

        // 读取现有配置，合并更新（避免局部更新清空其他字段）
        var existing = LoadWebConfig();

        if (config.LayerPresets != null)
            existing.LayerPresets = config.LayerPresets;

        var json = JsonConvert.SerializeObject(existing, WriteSettings);
        File.WriteAllText(WebConfigPath, json);
    }

    /// <summary>
    /// 获取 Server 配置文件路径（供日志输出）
    /// </summary>
    public static string GetConfigPath() => ServerConfigPath;

    /// <summary>
    /// 获取 Server 配置文件路径
    /// </summary>
    public static string GetServerConfigPath() => ServerConfigPath;

    /// <summary>
    /// 获取 Web 配置文件路径
    /// </summary>
    public static string GetWebConfigPath() => WebConfigPath;

    /// <summary>
    /// 获取 Agent 配置文件路径
    /// </summary>
    public static string GetAgentConfigPath() => AgentConfigPath;

    /// <summary>
    /// 获取 CCR 配置文件路径
    /// </summary>
    public static string GetCcrConfigPath() => CcrConfigPath;

    /// <summary>
    /// 获取开发态 Agent 本地补齐配置路径
    /// </summary>
    public static string GetDevLocalAgentConfigPath() => DevLocalAgentConfigPath;

    /// <summary>
    /// 获取开发态 CCR 本地补齐配置路径
    /// </summary>
    public static string GetDevLocalCcrConfigPath() => DevLocalCcrConfigPath;

    /// <summary>
    /// 获取配置目录路径
    /// </summary>
    public static string GetConfigDir() => ConfigDir;

    /// <summary>
    /// 获取最近项目记录文件路径
    /// </summary>
    public static string GetRecentProjectsPath() => Path.Combine(ConfigDir, "recent_projects.json");

    private static string ResolveConfigDir()
    {
        var configuredHome = Environment.GetEnvironmentVariable("BIMCANVAS_HOME");
        if (!string.IsNullOrWhiteSpace(configuredHome))
        {
            return Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(configuredHome.Trim()));
        }

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "BIMCanvas");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".bimcanvas");
    }

}
