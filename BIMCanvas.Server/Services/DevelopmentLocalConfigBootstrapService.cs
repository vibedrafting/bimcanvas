using System.Text;
using Newtonsoft.Json.Linq;

namespace BIMCanvas.Server.Services;

/// <summary>
/// Development 模式下的本地私有配置补齐。
/// 仅负责：
/// 1. 初始化 *.dev.local.json 占位文件
/// 2. 将本地私有配置按白名单补齐到运行时 JSON 空字段
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

    public void EnsureInitialized()
    {
        var configDir = ConfigService.GetConfigDir();
        _templateService.EnsureInitializedFromManifest(ManifestRelativePath, configDir);

        ApplyAgentBootstrap();
        ApplyCcrBootstrap();
    }

    private static void ApplyAgentBootstrap()
    {
        var runtime = LoadJsonObject(ConfigService.GetAgentConfigPath());
        var local = LoadJsonObject(ConfigService.GetDevLocalAgentConfigPath());
        var changed = false;

        changed |= CopyStringIfMissing(runtime, local, "baseUrl");
        changed |= CopyStringIfMissing(runtime, local, "apiKey");

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

        if (ShouldCopyProviders(runtime, local))
        {
            runtime["Providers"] = local["Providers"]!.DeepClone();
            changed = true;
        }

        var localRouter = local["Router"] as JObject;
        if (localRouter != null)
        {
            var runtimeRouter = EnsureObject(runtime, "Router");
            foreach (var field in RoutedFields)
            {
                changed |= CopyNestedStringIfMissing(runtimeRouter, localRouter, field);
            }
        }

        if (changed)
        {
            SaveJsonObject(ConfigService.GetCcrConfigPath(), runtime);
        }
    }

    private static bool ShouldCopyProviders(JObject runtime, JObject local)
    {
        var localProviders = local["Providers"] as JArray;
        if (localProviders is not { Count: > 0 })
        {
            return false;
        }

        var runtimeProviders = runtime["Providers"] as JArray;
        return runtimeProviders == null || runtimeProviders.Count == 0;
    }

    private static bool CopyStringIfMissing(JObject target, JObject source, string propertyName)
    {
        return CopyNestedStringIfMissing(target, source, propertyName);
    }

    private static bool CopyNestedStringIfMissing(JObject target, JObject source, string propertyName)
    {
        var sourceToken = source[propertyName];
        if (IsNullOrWhiteSpace(sourceToken))
        {
            return false;
        }

        if (!IsNullOrWhiteSpace(target[propertyName]))
        {
            return false;
        }

        target[propertyName] = sourceToken!.DeepClone();
        return true;
    }

    private static JObject EnsureObject(JObject target, string propertyName)
    {
        if (target[propertyName] is JObject obj)
        {
            return obj;
        }

        obj = new JObject();
        target[propertyName] = obj;
        return obj;
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
