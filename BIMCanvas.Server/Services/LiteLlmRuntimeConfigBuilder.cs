using System.Text.Json;
using System.Text.RegularExpressions;
using BIMCanvas.Server.Models;
using YamlDotNet.RepresentationModel;

namespace BIMCanvas.Server.Services;

public static class LiteLlmRuntimeConfigBuilder
{
    private static readonly Regex ProviderAliasPattern = new(
        "^[a-z0-9_]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ModelAliasPattern = new(
        "^bc-(?<providerAlias>[a-z0-9_]+)-(?<family>opus|sonnet|haiku|subagent)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] SupportedProfiles =
    [
        ProviderAdapterProfiles.GeminiNativeV1Beta,
        ProviderAdapterProfiles.OpenAiCompatibleV1,
        ProviderAdapterProfiles.PassthroughV1
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static LiteLlmRuntimeConfigBuildResult Generate(ServerConfig serverConfig, string sourceConfigPath, string configDir)
    {
        if (serverConfig == null)
            throw new ArgumentNullException(nameof(serverConfig));

        if (string.IsNullOrWhiteSpace(sourceConfigPath) || !File.Exists(sourceConfigPath))
            throw new InvalidOperationException($"LiteLLM 配置文件不存在: {sourceConfigPath}");

        Directory.CreateDirectory(configDir);

        var runtimeLiteLlmConfigPath = Path.Combine(
            configDir,
            string.IsNullOrWhiteSpace(serverConfig.ProviderAdapter.RuntimeLiteLlmConfigFileName)
                ? "litellm_runtime.generated.yaml"
                : serverConfig.ProviderAdapter.RuntimeLiteLlmConfigFileName.Trim());
        var runtimeAdapterConfigPath = Path.Combine(
            configDir,
            string.IsNullOrWhiteSpace(serverConfig.ProviderAdapter.RuntimeAdapterConfigFileName)
                ? "provider_adapter_runtime.generated.json"
                : serverConfig.ProviderAdapter.RuntimeAdapterConfigFileName.Trim());

        if (Path.GetFullPath(runtimeLiteLlmConfigPath).Equals(Path.GetFullPath(sourceConfigPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("运行时 LiteLLM 配置文件名不能与源配置文件相同");

        if (Path.GetFullPath(runtimeLiteLlmConfigPath).Equals(Path.GetFullPath(runtimeAdapterConfigPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("LiteLLM 运行时配置文件名不能与 ProviderAdapter 运行时配置文件名相同");

        var mappings = BuildMappings(serverConfig.ProviderAdapter.Mappings);
        var adapterBaseUrl = BuildAdapterBaseUrl(serverConfig.ProviderAdapter.Host, serverConfig.ProviderAdapter.Port);
        var adaptedProviders = new Dictionary<string, ProviderAdapterRuntimeProvider>(StringComparer.Ordinal);

        using var reader = File.OpenText(sourceConfigPath);
        var yaml = new YamlStream();
        yaml.Load(reader);

        if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root)
            throw new InvalidOperationException("LiteLLM 配置文件格式无效，缺少顶层 YAML mapping");

        var modelListNode = GetSequence(root, "model_list")
            ?? throw new InvalidOperationException("LiteLLM 配置文件缺少 model_list");

        foreach (var node in modelListNode.Children)
        {
            if (node is not YamlMappingNode modelNode)
                continue;

            var modelName = GetScalar(modelNode, "model_name");
            if (string.IsNullOrWhiteSpace(modelName))
                throw new InvalidOperationException("LiteLLM model_list 中存在缺少 model_name 的条目");

            if (!TryParseProviderAlias(modelName, out var providerAlias))
                throw new InvalidOperationException($"LiteLLM model_name 格式不符合 bc-{{providerAlias}}-{{family}} 规范: {modelName}");

            var route = ResolveRoute(mappings, providerAlias);
            if (route.Mode == ProviderAdapterRouteMode.Direct)
                continue;

            if (!serverConfig.ProviderAdapter.Enabled)
                throw new InvalidOperationException($"ProviderAdapter 已禁用，但 provider '{providerAlias}' 被配置为 adapter 模式");

            var litellmParamsNode = GetMapping(modelNode, "litellm_params")
                ?? throw new InvalidOperationException($"LiteLLM 条目 '{modelName}' 缺少 litellm_params");

            var upstreamBase = NormalizeUpstreamBase(GetScalar(litellmParamsNode, "api_base"));
            if (string.IsNullOrWhiteSpace(upstreamBase))
                throw new InvalidOperationException($"Provider '{providerAlias}' 处于 adapter 模式，但条目 '{modelName}' 缺少 api_base");

            if (adaptedProviders.TryGetValue(providerAlias, out var existingProvider))
            {
                if (!string.Equals(existingProvider.UpstreamBase, upstreamBase, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Provider '{providerAlias}' 的多个模型条目使用了不同的 api_base，无法生成统一 adapter 配置");

                if (!string.Equals(existingProvider.Profile, route.Profile, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Provider '{providerAlias}' 命中了冲突的 adapter profile 配置");
            }
            else
            {
                adaptedProviders[providerAlias] = new ProviderAdapterRuntimeProvider
                {
                    ProviderAlias = providerAlias,
                    Profile = route.Profile!,
                    UpstreamBase = upstreamBase
                };
            }

            SetScalar(litellmParamsNode, "api_base", $"{adapterBaseUrl}/{providerAlias}");
        }

        using (var writer = new StreamWriter(runtimeLiteLlmConfigPath, false))
        {
            yaml.Save(writer, false);
        }

        var adapterConfig = new ProviderAdapterRuntimeConfig
        {
            Providers = adaptedProviders.Values
                .OrderBy(provider => provider.ProviderAlias, StringComparer.Ordinal)
                .ToList()
        };

        File.WriteAllText(
            runtimeAdapterConfigPath,
            JsonSerializer.Serialize(adapterConfig, JsonOptions));

        return new LiteLlmRuntimeConfigBuildResult(
            runtimeLiteLlmConfigPath,
            runtimeAdapterConfigPath,
            adaptedProviders.Count > 0,
            adaptedProviders.Keys.OrderBy(alias => alias, StringComparer.Ordinal).ToArray());
    }

    private static Dictionary<string, ResolvedProviderMapping> BuildMappings(IEnumerable<ProviderAdapterMapping>? mappings)
    {
        var result = new Dictionary<string, ResolvedProviderMapping>(StringComparer.Ordinal);

        if (mappings == null)
            return result;

        foreach (var mapping in mappings)
        {
            if (mapping == null)
                continue;

            var providerAlias = NormalizeProviderAlias(mapping.ProviderAlias);
            if (string.IsNullOrWhiteSpace(providerAlias))
                throw new InvalidOperationException("providerAdapter.mappings[].providerAlias 不能为空");

            if (!ProviderAliasPattern.IsMatch(providerAlias))
                throw new InvalidOperationException($"providerAlias 必须为小写 snake_case: {providerAlias}");

            var mode = NormalizeRouteMode(mapping.Mode);
            string? profile = null;
            if (mode == ProviderAdapterRouteMode.Adapter)
            {
                profile = NormalizeProfile(mapping.Profile);
                if (!SupportedProfiles.Contains(profile, StringComparer.Ordinal))
                    throw new InvalidOperationException($"不支持的 adapter profile: {profile}");
            }

            if (!result.TryAdd(providerAlias, new ResolvedProviderMapping(providerAlias, mode, profile)))
                throw new InvalidOperationException($"providerAdapter.mappings 中存在重复的 providerAlias: {providerAlias}");
        }

        return result;
    }

    private static ResolvedProviderMapping ResolveRoute(IReadOnlyDictionary<string, ResolvedProviderMapping> mappings, string providerAlias)
    {
        return mappings.TryGetValue(providerAlias, out var route)
            ? route
            : new ResolvedProviderMapping(providerAlias, ProviderAdapterRouteMode.Direct, null);
    }

    private static string NormalizeProviderAlias(string? providerAlias)
    {
        return string.IsNullOrWhiteSpace(providerAlias)
            ? string.Empty
            : providerAlias.Trim().ToLowerInvariant();
    }

    private static ProviderAdapterRouteMode NormalizeRouteMode(string? mode)
    {
        var normalized = string.IsNullOrWhiteSpace(mode)
            ? "direct"
            : mode.Trim().ToLowerInvariant();

        return normalized switch
        {
            "direct" => ProviderAdapterRouteMode.Direct,
            "adapter" => ProviderAdapterRouteMode.Adapter,
            _ => throw new InvalidOperationException($"不支持的 provider route mode: {mode}")
        };
    }

    private static string NormalizeProfile(string? profile)
    {
        if (string.IsNullOrWhiteSpace(profile))
            throw new InvalidOperationException("adapter 模式必须指定 profile");

        return profile.Trim().ToLowerInvariant();
    }

    private static bool TryParseProviderAlias(string modelName, out string providerAlias)
    {
        var match = ModelAliasPattern.Match(modelName);
        if (!match.Success)
        {
            providerAlias = string.Empty;
            return false;
        }

        providerAlias = match.Groups["providerAlias"].Value;
        return true;
    }

    private static string BuildAdapterBaseUrl(string host, int port)
    {
        var builder = new UriBuilder(Uri.UriSchemeHttp, host, port, "providers");
        return builder.Uri.ToString().TrimEnd('/');
    }

    private static string NormalizeUpstreamBase(string? apiBase)
    {
        if (string.IsNullOrWhiteSpace(apiBase))
            return string.Empty;

        if (!Uri.TryCreate(apiBase.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException($"api_base 不是有效的 HTTP/HTTPS 地址: {apiBase}");
        }

        var builder = new UriBuilder(uri)
        {
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private static YamlSequenceNode? GetSequence(YamlMappingNode mapping, string key)
    {
        return TryGetNode(mapping, key) as YamlSequenceNode;
    }

    private static YamlMappingNode? GetMapping(YamlMappingNode mapping, string key)
    {
        return TryGetNode(mapping, key) as YamlMappingNode;
    }

    private static string? GetScalar(YamlMappingNode mapping, string key)
    {
        return TryGetNode(mapping, key) switch
        {
            YamlScalarNode scalarNode => scalarNode.Value,
            _ => null
        };
    }

    private static void SetScalar(YamlMappingNode mapping, string key, string value)
    {
        mapping.Children[new YamlScalarNode(key)] = new YamlScalarNode(value);
    }

    private static YamlNode? TryGetNode(YamlMappingNode mapping, string key)
    {
        return mapping.Children.FirstOrDefault(pair =>
                string.Equals((pair.Key as YamlScalarNode)?.Value, key, StringComparison.Ordinal))
            .Value;
    }

    private sealed record ResolvedProviderMapping(string ProviderAlias, ProviderAdapterRouteMode Mode, string? Profile);
}

public sealed record LiteLlmRuntimeConfigBuildResult(
    string RuntimeLiteLlmConfigPath,
    string RuntimeAdapterConfigPath,
    bool RequiresAdapter,
    IReadOnlyList<string> AdaptedProviders);

public sealed class ProviderAdapterRuntimeConfig
{
    public List<ProviderAdapterRuntimeProvider> Providers { get; set; } = new();
}

public sealed class ProviderAdapterRuntimeProvider
{
    public string ProviderAlias { get; set; } = "";
    public string Profile { get; set; } = "";
    public string UpstreamBase { get; set; } = "";
}

public enum ProviderAdapterRouteMode
{
    Direct,
    Adapter
}

public static class ProviderAdapterProfiles
{
    public const string GeminiNativeV1Beta = "gemini_native_v1beta";
    public const string OpenAiCompatibleV1 = "openai_compatible_v1";
    public const string PassthroughV1 = "passthrough_v1";
}
