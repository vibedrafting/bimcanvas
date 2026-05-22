using BIMCanvas.Server.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System.Text;

namespace BIMCanvas.Server.Services;

/// <summary>
/// 程序配置服务（BIMCANVAS_HOME）。
/// <para>
/// 配置合并:四组运行时配置(server / web / agent / ccr)统一落在单一
/// <c>instance.config.json</c> 的四个顶层段中。本服务负责统一文件的"按段(section)"读写。
/// Plugin 能力配置(plugins/*)与 <c>*.dev.local.json</c> 种子文件不在此列。
/// </para>
/// <para>
/// 序列化栈:Newtonsoft.Json + <see cref="DefaultContractResolver"/> +
/// <see cref="CamelCaseNamingStrategy"/>(只转 C# 属性名,不转 Dictionary key;详见 CLAUDE.md §10)。
/// </para>
/// </summary>
public static class ConfigService
{
    /// <summary>统一运行时配置文件名（四段合并：server/web/agent/ccr）。</summary>
    public const string UnifiedConfigFileName = "instance.config.json";

    public const string SectionServer = "server";
    public const string SectionWeb = "web";
    public const string SectionAgent = "agent";
    public const string SectionCcr = "ccr";

    private static readonly string ConfigDir = ResolveConfigDir();

    private static readonly string UnifiedConfigPath = Path.Combine(ConfigDir, UnifiedConfigFileName);
    private static readonly string DevLocalAgentConfigPath = Path.Combine(ConfigDir, "config.dev.local.json");
    private static readonly string DevLocalCcrConfigPath = Path.Combine(ConfigDir, "ccr_config.dev.local.json");

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly object WriteLock = new();

    private static readonly JsonSerializerSettings ReadSettings = new()
    {
        ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
    };

    private static readonly JsonSerializerSettings WriteSettings = new()
    {
        ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
        Formatting = Formatting.Indented,
    };

    // ─── 统一文件 · 按段读写 ───────────────────────────────────────────────

    /// <summary>读取整份统一配置文件（缺失/为空/损坏时返回空对象）。</summary>
    public static JObject LoadUnified()
    {
        if (!File.Exists(UnifiedConfigPath))
        {
            return new JObject();
        }

        try
        {
            var json = File.ReadAllText(UnifiedConfigPath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new JObject();
            }

            return JToken.Parse(json) as JObject ?? new JObject();
        }
        catch (Exception)
        {
            return new JObject();
        }
    }

    /// <summary>指定段是否存在（且为对象）。用于首启动种子判定。</summary>
    public static bool SectionExists(string sectionKey)
    {
        return LoadUnified()[sectionKey] is JObject;
    }

    /// <summary>读取指定段的副本（缺失时用 defaultFactory，否则空对象）。</summary>
    public static JObject LoadSection(string sectionKey, Func<JObject>? defaultFactory = null)
    {
        if (LoadUnified()[sectionKey] is JObject obj)
        {
            return (JObject)obj.DeepClone();
        }

        return defaultFactory?.Invoke() ?? new JObject();
    }

    /// <summary>写入指定段，保留统一文件中的其它段（进程内串行 + 临时文件原子替换）。</summary>
    public static void SaveSection(string sectionKey, JObject value)
    {
        lock (WriteLock)
        {
            var unified = LoadUnified();
            unified[sectionKey] = value;
            WriteUnified(unified);
        }
    }

    private static void WriteUnified(JObject unified)
    {
        Directory.CreateDirectory(ConfigDir);
        var tmp = UnifiedConfigPath + ".tmp";
        File.WriteAllText(tmp, unified.ToString(Formatting.Indented), Utf8NoBom);
        File.Move(tmp, UnifiedConfigPath, overwrite: true);
    }

    // ─── 强类型 / 原始 读取（基于段） ──────────────────────────────────────

    /// <summary>加载 Server 启动配置（统一文件 server 段），不存在时返回默认配置。</summary>
    public static ServerConfig Load()
    {
        try
        {
            var section = LoadSection(SectionServer);
            return JsonConvert.DeserializeObject<ServerConfig>(section.ToString(), ReadSettings) ?? new ServerConfig();
        }
        catch (Exception)
        {
            return new ServerConfig();
        }
    }

    /// <summary>加载 Web 客户端配置（统一文件 web 段）。</summary>
    public static WebConfig LoadWebConfig()
    {
        try
        {
            var section = LoadSection(SectionWeb);
            return JsonConvert.DeserializeObject<WebConfig>(section.ToString(), ReadSettings) ?? new WebConfig();
        }
        catch (Exception)
        {
            return new WebConfig();
        }
    }

    /// <summary>
    /// 加载 Web 段的原始 JSON（供 API 直接返回，避免序列化器转换字典键名）。
    /// </summary>
    public static string LoadWebConfigRaw()
    {
        return LoadSection(SectionWeb).ToString(Formatting.Indented);
    }

    /// <summary>保存 Web 客户端配置（写回统一文件 web 段，合并避免清空其它字段）。</summary>
    public static void SaveWebConfig(WebConfig config)
    {
        var existing = LoadWebConfig();

        if (config.LayerPresets != null)
        {
            existing.LayerPresets = config.LayerPresets;
        }

        var section = JObject.Parse(JsonConvert.SerializeObject(existing, WriteSettings));
        SaveSection(SectionWeb, section);
    }

    // ─── 路径 getter ──────────────────────────────────────────────────────

    /// <summary>统一配置文件路径（供日志输出）。</summary>
    public static string GetConfigPath() => UnifiedConfigPath;

    /// <summary>统一运行时配置文件路径。</summary>
    public static string GetUnifiedConfigPath() => UnifiedConfigPath;

    /// <summary>获取开发态 Agent 本地补齐配置路径。</summary>
    public static string GetDevLocalAgentConfigPath() => DevLocalAgentConfigPath;

    /// <summary>获取开发态 CCR 本地补齐配置路径。</summary>
    public static string GetDevLocalCcrConfigPath() => DevLocalCcrConfigPath;

    /// <summary>获取配置目录路径。</summary>
    public static string GetConfigDir() => ConfigDir;

    /// <summary>获取最近项目记录文件路径。</summary>
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
