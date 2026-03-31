using System.Text;
using Newtonsoft.Json.Linq;

namespace BIMCanvas.Server.Services;

/// <summary>
/// Development 模式下的本地私有配置种子初始化。
/// 仅负责：
/// 1. 初始化 *.dev.local.json 占位文件
/// 2. 仅在运行时配置首次创建时，将本地私有配置作为初始化种子写入运行时 JSON
/// </summary>
public sealed class DevelopmentLocalConfigBootstrapService
{
    private const string ManifestRelativePath = "development-config/manifest.json";
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly string[] RoutedFields = ["default", "think", "background", "longContext"];

    private readonly BootstrapTemplateService _templateService;

    public DevelopmentLocalConfigBootstrapService(BootstrapTemplateService templateService)
    {
        _templateService = templateService;
    }

    public void EnsureInitialized(bool initializeAgentRuntime, bool initializeCcrRuntime)
    {
        var configDir = ConfigService.GetConfigDir();
        _templateService.EnsureInitializedFromManifest(ManifestRelativePath, configDir);

        if (initializeAgentRuntime)
        {
            ApplyAgentBootstrap();
        }

        if (initializeCcrRuntime)
        {
            ApplyCcrBootstrap();
        }
    }

    private static void ApplyAgentBootstrap()
    {
        var runtime = LoadJsonObject(ConfigService.GetAgentConfigPath());
        var local = LoadJsonObject(ConfigService.GetDevLocalAgentConfigPath());
        var changed = false;

        changed |= CopyStringIfPresent(runtime, local, "baseUrl");
        changed |= CopyStringIfPresent(runtime, local, "apiKey");

        if (changed)
        {
            SaveJsonObject(ConfigService.GetAgentConfigPath(), runtime);
        }
    }

    private static void ApplyCcrBootstrap()
    {
        var runtime = LoadJsonObject(ConfigService.GetCcrConfigPath());
        var local = LoadJsonObject(ConfigService.GetDevLocalCcrConfigPath());
        var changed = false;

        if (ShouldCopyProviders(local))
        {
            runtime["Providers"] = local["Providers"]!.DeepClone();
            changed = true;
        }

        var localRouter = local["Router"] as JObject;
        if (localRouter != null)
        {
            var runtimeRouter = runtime["Router"] as JObject ?? new JObject();
            var routerChanged = false;
            foreach (var field in RoutedFields)
            {
                routerChanged |= CopyStringIfPresent(runtimeRouter, localRouter, field);
            }

            if (routerChanged)
            {
                runtime["Router"] = runtimeRouter;
                changed = true;
            }
        }

        if (changed)
        {
            SaveJsonObject(ConfigService.GetCcrConfigPath(), runtime);
        }
    }

    private static bool ShouldCopyProviders(JObject local)
    {
        var localProviders = local["Providers"] as JArray;
        return localProviders is { Count: > 0 };
    }

    private static bool CopyStringIfPresent(JObject target, JObject source, string propertyName)
    {
        var sourceToken = source[propertyName];
        if (IsNullOrWhiteSpace(sourceToken))
        {
            return false;
        }

        target[propertyName] = sourceToken!.DeepClone();
        return true;
    }

    private static bool IsNullOrWhiteSpace(JToken? token)
    {
        return token == null
            || token.Type == JTokenType.Null
            || (token.Type == JTokenType.String && string.IsNullOrWhiteSpace(token.Value<string>()));
    }

    private static JObject LoadJsonObject(string path)
    {
        if (!File.Exists(path))
        {
            return new JObject();
        }

        var json = File.ReadAllText(path, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new JObject();
        }

        var token = JToken.Parse(json);
        return token as JObject
            ?? throw new InvalidOperationException($"配置文件顶层必须是 JSON 对象: {path}");
    }

    private static void SaveJsonObject(string path, JObject value)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, value.ToString(Newtonsoft.Json.Formatting.Indented), Utf8NoBom);
    }
}
