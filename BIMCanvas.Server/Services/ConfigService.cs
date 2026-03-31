using System.Text.Json;
using BIMCanvas.Server.Models;

namespace BIMCanvas.Server.Services;

/// <summary>
/// 程序配置服务（BIMCANVAS_HOME）。
/// 仅负责统一路径解析与配置读写，不负责模板初始化。
/// </summary>
public static class ConfigService
{
    private static readonly string ConfigDir = ResolveConfigDir();

    private static readonly string ServerConfigPath = Path.Combine(ConfigDir, "server_config.json");
    private static readonly string WebConfigPath = Path.Combine(ConfigDir, "web_config.json");
    private static readonly string AgentConfigPath = Path.Combine(ConfigDir, "config.json");
    private static readonly string CcrConfigPath = Path.Combine(ConfigDir, "ccr_config.json");

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
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
            return JsonSerializer.Deserialize<ServerConfig>(json, ReadOptions) ?? new ServerConfig();
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
            return JsonSerializer.Deserialize<WebConfig>(json, ReadOptions) ?? new WebConfig();
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

        if (!string.IsNullOrWhiteSpace(config.DefaultModel))
            existing.DefaultModel = config.DefaultModel;

        if (config.CustomModels is { Count: > 0 })
            existing.CustomModels = config.CustomModels;

        if (config.LayerPresets != null)
            existing.LayerPresets = config.LayerPresets;

        var json = JsonSerializer.Serialize(existing, WriteOptions);
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
