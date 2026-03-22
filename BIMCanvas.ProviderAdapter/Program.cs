using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Primitives;

Console.OutputEncoding = Encoding.UTF8;

var options = ProviderAdapterOptions.Parse(args);
var runtimeConfig = LoadRuntimeConfig(options.ConfigPath);

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.WebHost.UseUrls($"http://{options.Host}:{options.Port}");

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(new ProviderRegistry(runtimeConfig));
builder.Services.AddHttpClient("proxy")
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        UseCookies = false,
        UseProxy = false
    })
    .SetHandlerLifetime(Timeout.InfiniteTimeSpan);

var app = builder.Build();

app.MapGet("/health", (ProviderRegistry registry) => Results.Ok(new
{
    status = "healthy",
    providers = registry.Count
}));

app.MapMethods(
    "/providers/{providerAlias}/{**rest}",
    ["GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS", "HEAD"],
    ProxyAsync);

Console.WriteLine($"ProviderAdapter listening on http://{options.Host}:{options.Port}");
Console.WriteLine($"Loaded providers: {string.Join(", ", runtimeConfig.Providers.Select(provider => provider.ProviderAlias))}");

await app.RunAsync();

static ProviderAdapterRuntimeConfig LoadRuntimeConfig(string configPath)
{
    if (!File.Exists(configPath))
        throw new InvalidOperationException($"ProviderAdapter 配置文件不存在: {configPath}");

    var json = File.ReadAllText(configPath);
    var config = JsonSerializer.Deserialize<ProviderAdapterRuntimeConfig>(json, new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    });

    if (config == null)
        throw new InvalidOperationException("ProviderAdapter 配置文件格式无效");

    return config;
}

static async Task ProxyAsync(
    HttpContext context,
    string providerAlias,
    string? rest,
    ProviderRegistry registry,
    IHttpClientFactory httpClientFactory)
{
    if (!registry.TryGet(providerAlias, out var provider))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsync($"Unknown provider alias: {providerAlias}");
        return;
    }

    var rewrittenPath = RewritePath(provider.Profile, rest);
    var targetUri = BuildTargetUri(provider.UpstreamBase, rewrittenPath, context.Request.QueryString);

    using var requestMessage = CreateProxyRequest(context, targetUri);
    var client = httpClientFactory.CreateClient("proxy");

    try
    {
        using var responseMessage = await client.SendAsync(
            requestMessage,
            HttpCompletionOption.ResponseHeadersRead,
            context.RequestAborted);

        await CopyProxyResponseAsync(context, responseMessage);
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        // 客户端主动断开时不再写任何响应
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"ProviderAdapter 转发失败: provider={providerAlias}, error={ex.Message}");
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            await context.Response.WriteAsync("Bad Gateway");
        }
    }
}

static HttpRequestMessage CreateProxyRequest(HttpContext context, Uri targetUri)
{
    var requestMessage = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUri);
    var hasBody = (context.Request.ContentLength ?? 0) > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding");
    if (hasBody)
    {
        requestMessage.Content = new StreamContent(context.Request.Body);
    }

    foreach (var header in context.Request.Headers)
    {
        if (IsHopByHopHeader(header.Key) || header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
            continue;

        if (!requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.AsEnumerable()))
        {
            requestMessage.Content ??= new ByteArrayContent(Array.Empty<byte>());
            requestMessage.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.AsEnumerable());
        }
    }

    return requestMessage;
}

static async Task CopyProxyResponseAsync(HttpContext context, HttpResponseMessage responseMessage)
{
    context.Response.StatusCode = (int)responseMessage.StatusCode;

    foreach (var header in responseMessage.Headers)
    {
        if (IsHopByHopHeader(header.Key))
            continue;

        context.Response.Headers[header.Key] = new StringValues(header.Value.ToArray());
    }

    foreach (var header in responseMessage.Content.Headers)
    {
        if (IsHopByHopHeader(header.Key) || header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            continue;

        context.Response.Headers[header.Key] = new StringValues(header.Value.ToArray());
    }

    context.Response.Headers.Remove("transfer-encoding");

    await responseMessage.Content.CopyToAsync(context.Response.Body);
}

static Uri BuildTargetUri(string upstreamBase, string path, QueryString queryString)
{
    var normalizedBase = upstreamBase.TrimEnd('/');
    var normalizedPath = path.StartsWith('/') ? path : $"/{path}";
    return new Uri($"{normalizedBase}{normalizedPath}{queryString.Value}");
}

static string RewritePath(string profile, string? rest)
{
    var path = NormalizePath(rest);

    return profile switch
    {
        ProviderAdapterProfiles.GeminiNativeV1Beta => EnsurePrefixedPath(path, "/v1beta"),
        ProviderAdapterProfiles.OpenAiCompatibleV1 => EnsurePrefixedPath(path, "/v1"),
        ProviderAdapterProfiles.PassthroughV1 => path,
        _ => throw new InvalidOperationException($"Unsupported adapter profile: {profile}")
    };
}

static string NormalizePath(string? rest)
{
    if (string.IsNullOrWhiteSpace(rest))
        return "/";

    var trimmed = rest.Trim('/');
    return string.IsNullOrWhiteSpace(trimmed) ? "/" : $"/{trimmed}";
}

static string EnsurePrefixedPath(string path, string prefix)
{
    if (path.Equals("/", StringComparison.Ordinal))
        return prefix;

    if (path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith($"{prefix}/", StringComparison.OrdinalIgnoreCase))
    {
        return path;
    }

    return $"{prefix}{path}";
}

static bool IsHopByHopHeader(string headerName)
{
    return headerName.Equals("Connection", StringComparison.OrdinalIgnoreCase)
        || headerName.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase)
        || headerName.Equals("Proxy-Authenticate", StringComparison.OrdinalIgnoreCase)
        || headerName.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase)
        || headerName.Equals("TE", StringComparison.OrdinalIgnoreCase)
        || headerName.Equals("Trailer", StringComparison.OrdinalIgnoreCase)
        || headerName.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
        || headerName.Equals("Upgrade", StringComparison.OrdinalIgnoreCase)
        || headerName.Equals("Content-Length", StringComparison.OrdinalIgnoreCase);
}

sealed class ProviderRegistry
{
    private readonly Dictionary<string, ProviderAdapterRuntimeProvider> _providers;

    public ProviderRegistry(ProviderAdapterRuntimeConfig config)
    {
        _providers = config.Providers.ToDictionary(
            provider => provider.ProviderAlias,
            provider => provider,
            StringComparer.Ordinal);
    }

    public int Count => _providers.Count;

    public bool TryGet(string providerAlias, out ProviderAdapterRuntimeProvider provider)
    {
        return _providers.TryGetValue(providerAlias, out provider!);
    }
}

sealed record ProviderAdapterOptions(string ConfigPath, string Host, int Port)
{
    public static ProviderAdapterOptions Parse(string[] args)
    {
        string? configPath = null;
        var host = "127.0.0.1";
        var port = 4101;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--config":
                    configPath = GetRequiredValue(args, ref i, "--config");
                    break;
                case "--host":
                    host = GetRequiredValue(args, ref i, "--host");
                    break;
                case "--port":
                    var portValue = GetRequiredValue(args, ref i, "--port");
                    if (!int.TryParse(portValue, out port))
                        throw new InvalidOperationException($"无效的 ProviderAdapter 端口: {portValue}");
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(configPath))
            throw new InvalidOperationException("必须通过 --config 指定 ProviderAdapter 运行时配置文件");

        return new ProviderAdapterOptions(configPath, host, port);
    }

    private static string GetRequiredValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
            throw new InvalidOperationException($"{optionName} 缺少参数值");

        index++;
        return args[index];
    }
}

sealed class ProviderAdapterRuntimeConfig
{
    public List<ProviderAdapterRuntimeProvider> Providers { get; set; } = new();
}

sealed class ProviderAdapterRuntimeProvider
{
    public string ProviderAlias { get; set; } = "";
    public string Profile { get; set; } = "";
    public string UpstreamBase { get; set; } = "";
}

static class ProviderAdapterProfiles
{
    public const string GeminiNativeV1Beta = "gemini_native_v1beta";
    public const string OpenAiCompatibleV1 = "openai_compatible_v1";
    public const string PassthroughV1 = "passthrough_v1";
}
