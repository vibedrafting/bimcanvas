using System.Text.Json;
using BIMCanvas.Server.Models;

namespace BIMCanvas.Server.Services;

/// <summary>
/// 程序配置服务（BIMCANVAS_HOME）
/// 管理 server_config.json 和 web_config.json
/// 由 Templates/program_manifest.json 驱动初始化
/// </summary>
public static class ConfigService
{
    private const string ManifestFileName = "program_manifest.json";

    private static readonly string ConfigDir = ResolveConfigDir();

    private static readonly string ServerConfigPath = Path.Combine(ConfigDir, "server_config.json");
    private static readonly string WebConfigPath = Path.Combine(ConfigDir, "web_config.json");

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
    /// 确保配置目录和默认配置文件存在（Server 启动时调用）
    /// 读取 program_manifest.json，按清单从 Templates/ 复制模板
    /// </summary>
    public static void EnsureDefaultConfigs()
    {
        Directory.CreateDirectory(ConfigDir);

        var templatesRoot = FindTemplatesRoot();
        if (templatesRoot == null)
            return;

        var manifestPath = Path.Combine(templatesRoot, ManifestFileName);
        if (!File.Exists(manifestPath))
            return;

        var manifestJson = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<ProgramManifest>(manifestJson, ReadOptions);
        if (manifest?.Items == null)
            return;

        foreach (var item in manifest.Items)
        {
            if (!item.Enabled)
                continue;

            var sourcePath = Path.Combine(templatesRoot, item.Name);
            var targetPath = Path.Combine(ConfigDir, item.Target);

            if (File.Exists(targetPath))
                continue;

            if (!File.Exists(sourcePath))
                continue;

            File.Copy(sourcePath, targetPath);
        }
    }

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

    /// <summary>
    /// 查找 Templates 目录
    /// </summary>
    private static string? FindTemplatesRoot()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;

        // 方法1：编译输出目录（bin/Debug/net8.0/Templates/）
        var directPath = Path.Combine(baseDir, "Templates");
        if (Directory.Exists(directPath))
            return directPath;

        // 方法2：向上查找 BIMCanvas.Server/Templates（开发目录）
        var dir = new DirectoryInfo(baseDir);
        for (int i = 0; i < 8 && dir != null; i++)
        {
            var tryPath = Path.Combine(dir.FullName, "BIMCanvas.Server", "Templates");
            if (Directory.Exists(tryPath))
                return tryPath;
            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>
    /// 程序配置清单
    /// </summary>
    private class ProgramManifest
    {
        public string Version { get; set; } = "1.0";
        public List<ProgramManifestItem> Items { get; set; } = new();
    }

    private class ProgramManifestItem
    {
        public string Name { get; set; } = "";
        public string Target { get; set; } = "";
        public string Type { get; set; } = "template";
        public bool Enabled { get; set; } = true;
        public string? Description { get; set; }
    }
}
