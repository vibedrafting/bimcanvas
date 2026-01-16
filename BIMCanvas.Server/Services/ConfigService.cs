using System.Text.Json;
using BIMCanvas.Server.Models;

namespace BIMCanvas.Server.Services;

/// <summary>
/// 配置加载服务（~/.bimcanvas/config.json）
/// </summary>
public static class ConfigService
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".bimcanvas"
    );

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "server_config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// 加载配置文件，不存在时返回默认配置
    /// </summary>
    public static ServerConfig Load()
    {
        if (!File.Exists(ConfigPath))
            return new ServerConfig();

        try
        {
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<ServerConfig>(json, JsonOptions) ?? new ServerConfig();
        }
        catch (Exception)
        {
            // 配置文件解析失败时使用默认值
            return new ServerConfig();
        }
    }

    /// <summary>
    /// 获取配置文件路径（供日志输出）
    /// </summary>
    public static string GetConfigPath() => ConfigPath;
}
