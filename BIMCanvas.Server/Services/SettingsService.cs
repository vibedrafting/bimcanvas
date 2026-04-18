using System.Text;
using System.Text.Json;
using BIMCanvas.Server.Dtos;
using BIMCanvas.Server.Models;
using Newtonsoft.Json.Linq;

namespace BIMCanvas.Server.Services;

/// <summary>
/// 统一实例配置聚合服务。
/// 聚合 server/web/agent/ccr 四组配置，继续写回原有 JSON 文件。
/// </summary>
public sealed class SettingsService
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly IReadOnlyList<SettingsFieldDto> ServerFields =
    [
        new() { Path = "agent.autoStart", Label = "自动启动内置 Agent", ApplyMode = "restart" },
        new() { Path = "agent.baseUrl", Label = "Agent 基址", ApplyMode = "restart" },
        new() { Path = "agent.healthPath", Label = "Agent 健康检查路径", ApplyMode = "restart" },
        new() { Path = "agent.port", Label = "Agent 监听端口", ApplyMode = "restart" },
        new() { Path = "agent.pythonCommand", Label = "Python 命令", ApplyMode = "restart" },
        new() { Path = "startup.openBrowser", Label = "自动打开浏览器", ApplyMode = "restart" },
        new() { Path = "startup.browserPath", Label = "浏览器路径", ApplyMode = "restart" },
        new() { Path = "ccr.enabled", Label = "启用 CCR", ApplyMode = "restart" },
        new() { Path = "ccr.autoStart", Label = "自动启动 CCR", ApplyMode = "restart" },
        new() { Path = "ccr.host", Label = "CCR 主机", ApplyMode = "restart" },
        new() { Path = "ccr.port", Label = "CCR 端口", ApplyMode = "restart" }
    ];

    private static readonly IReadOnlyList<SettingsFieldDto> WebFields =
    [
        new() { Path = "defaultModel", Label = "默认模型", ApplyMode = "immediate" },
        new() { Path = "customModels", Label = "自定义模型列表", ApplyMode = "immediate" },
        new() { Path = "layerPresets.User.enabledLayers", Label = "用户图层预设", ApplyMode = "immediate" },
        new() { Path = "layerPresets.Agent.enabledLayers", Label = "Agent 图层预设", ApplyMode = "immediate" }
    ];

    private static readonly IReadOnlyList<SettingsFieldDto> AgentFields =
    [
        new() { Path = "baseUrl", Label = "网关地址", ApplyMode = "restart" },
        new() { Path = "apiKey", Label = "API Key", ApplyMode = "restart", Sensitive = true },
        new() { Path = "defaultEffort", Label = "默认 Effort", ApplyMode = "restart" },
        new() { Path = "defaultThinking", Label = "默认 Thinking", ApplyMode = "restart" },
        new() { Path = "maxThinkingTokens", Label = "最大 Thinking Tokens", ApplyMode = "restart" },
        new() { Path = "server.host", Label = "监听主机", ApplyMode = "restart" },
        new() { Path = "server.port", Label = "监听端口", ApplyMode = "restart" }
    ];

    private static readonly IReadOnlyList<SettingsFieldDto> CcrFields =
    [
        new() { Path = "HOST", Label = "CCR Host", ApplyMode = "restart" },
        new() { Path = "PORT", Label = "CCR Port", ApplyMode = "restart" },
        new() { Path = "LOG", Label = "日志开关", ApplyMode = "restart" },
        new() { Path = "LOG_LEVEL", Label = "日志级别", ApplyMode = "restart" },
        new() { Path = "API_TIMEOUT_MS", Label = "API 超时", ApplyMode = "restart" },
        new() { Path = "Router.default", Label = "默认路由", ApplyMode = "restart" },
        new() { Path = "Router.think", Label = "思考路由", ApplyMode = "restart" },
        new() { Path = "Router.background", Label = "后台路由", ApplyMode = "restart" },
        new() { Path = "Router.longContext", Label = "长上下文路由", ApplyMode = "restart" },
        new() { Path = "Providers[].api_key", Label = "Provider API Key", ApplyMode = "restart", Sensitive = true }
    ];

    private readonly object _syncRoot = new();
    private readonly RuntimeEndpointState _runtimeEndpointState;

    public SettingsService(RuntimeEndpointState runtimeEndpointState)
    {
        _runtimeEndpointState = runtimeEndpointState;
    }

    public SettingsSnapshotDto GetSettings()
    {
        var serverValues = LoadServerValues();
        var webValues = LoadWebValues();
        var agentValues = LoadAgentValues();
        var ccrValues = LoadCcrValues();

        return new SettingsSnapshotDto
        {
            Server = CreateGroup(
                "server",
                "Server",
                Path.GetFileName(ConfigService.GetServerConfigPath()),
                applyMode: "restart",
                requiresRestart: true,
                serverValues,
                ServerFields),
            Web = CreateGroup(
                "web",
                "Web",
                Path.GetFileName(ConfigService.GetWebConfigPath()),
                applyMode: "immediate",
                requiresRestart: false,
                webValues,
                WebFields),
            Agent = CreateGroup(
                "agent",
                "Agent",
                Path.GetFileName(ConfigService.GetAgentConfigPath()),
                applyMode: "restart",
                requiresRestart: true,
                agentValues,
                AgentFields),
            Ccr = CreateGroup(
                "ccr",
                "CCR",
                Path.GetFileName(ConfigService.GetCcrConfigPath()),
                applyMode: "restart",
                requiresRestart: true,
                ccrValues,
                CcrFields),
            Runtime = BuildRuntime(serverValues, webValues)
        };
    }

    public UpdateSettingsResponseDto SaveSettings(UpdateSettingsRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_syncRoot)
        {
            var currentServer = LoadServerValues();
            var currentWeb = LoadWebValues();
            var currentAgent = LoadAgentValues();
            var currentCcr = LoadCcrValues();

            var nextServer = request.Server != null ? ValidateServer(request.Server) : currentServer;
            var nextWeb = request.Web != null ? ValidateWeb(request.Web) : currentWeb;
            var nextAgent = request.Agent != null ? ValidateAgent(request.Agent) : currentAgent;
            var nextCcr = request.Ccr != null ? ValidateCcr(request.Ccr) : currentCcr;

            var changedGroups = new List<string>();

            if (!JToken.DeepEquals(currentServer, nextServer))
            {
                SaveJsonObject(ConfigService.GetServerConfigPath(), nextServer);
                changedGroups.Add("server");
            }

            if (!JToken.DeepEquals(currentWeb, nextWeb))
            {
                SaveJsonObject(ConfigService.GetWebConfigPath(), nextWeb);
                changedGroups.Add("web");
            }

            if (!JToken.DeepEquals(currentAgent, nextAgent))
            {
                SaveJsonObject(ConfigService.GetAgentConfigPath(), nextAgent);
                changedGroups.Add("agent");
            }

            if (!JToken.DeepEquals(currentCcr, nextCcr))
            {
                SaveJsonObject(ConfigService.GetCcrConfigPath(), nextCcr);
                changedGroups.Add("ccr");
            }

            return new UpdateSettingsResponseDto
            {
                Success = true,
                ChangedGroups = changedGroups,
                RestartRequiredGroups = changedGroups.Where(RequiresRestart).ToList(),
                Settings = GetSettings()
            };
        }
    }

    private static SettingsGroupDto CreateGroup(
        string key,
        string title,
        string sourceFile,
        string applyMode,
        bool requiresRestart,
        JObject values,
        IReadOnlyList<SettingsFieldDto> fields)
    {
        return new SettingsGroupDto
        {
            Key = key,
            Title = title,
            SourceFile = sourceFile,
            ApplyMode = applyMode,
            RequiresRestart = requiresRestart,
            Values = values,
            Fields = fields.Select(CloneField).ToList()
        };
    }

    private SettingsRuntimeDto BuildRuntime(JObject serverValues, JObject webValues)
    {
        var isCcrEnabled = serverValues.SelectToken("ccr.enabled")?.Value<bool>() ?? false;
        var dockerManagedRestart = string.Equals(
            Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
            "true",
            StringComparison.OrdinalIgnoreCase);
        var runtimeSnapshot = _runtimeEndpointState.GetSnapshot();

        return new SettingsRuntimeDto
        {
            Mode = isCcrEnabled ? "ccr" : "direct",
            EffectiveDefaultModelPath = "web.defaultModel",
            EffectiveDefaultModelValue = webValues.SelectToken("defaultModel")?.Value<string>() ?? "",
            DockerManagedRestart = dockerManagedRestart,
            RestartBehavior = dockerManagedRestart ? "docker-auto" : "manual",
            RestartHint = dockerManagedRestart
                ? "当前实例运行在 Docker 容器内，点击重启后会由 restart policy 自动拉起。"
                : "当前环境未检测到 Docker 自动重启，点击重启后需要手动重新启动服务。",
            Server = runtimeSnapshot.Server,
            Web = runtimeSnapshot.Web,
            Agent = runtimeSnapshot.Agent,
            Ccr = runtimeSnapshot.Ccr
        };
    }

    private static SettingsFieldDto CloneField(SettingsFieldDto field)
    {
        return new SettingsFieldDto
        {
            Path = field.Path,
            Label = field.Label,
            ApplyMode = field.ApplyMode,
            Sensitive = field.Sensitive
        };
    }

    private static bool RequiresRestart(string groupKey)
    {
        return !string.Equals(groupKey, "web", StringComparison.OrdinalIgnoreCase);
    }

    private static JObject ValidateServer(JObject input)
    {
        try
        {
            _ = input.ToObject<ServerConfig>() ?? throw new InvalidOperationException("server_config.json 内容不能为空对象。");
            return Clone(input);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"server_config.json 校验失败: {ex.Message}", ex);
        }
    }

    private static JObject ValidateWeb(JObject input)
    {
        try
        {
            _ = JsonSerializer.Deserialize<WebConfig>(
                input.ToString(),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }) ?? throw new InvalidOperationException("web_config.json 内容不能为空对象。");
            return Clone(input);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"web_config.json 校验失败: {ex.Message}", ex);
        }
    }

    private static JObject ValidateAgent(JObject input)
    {
        EnsureObject(input, "config.json");
        return Clone(input);
    }

    private static JObject ValidateCcr(JObject input)
    {
        EnsureObject(input, "ccr_config.json");
        return Clone(input);
    }

    private static void EnsureObject(JObject input, string fileName)
    {
        if (!input.HasValues)
        {
            return;
        }

        var token = JToken.Parse(input.ToString());
        if (token.Type != JTokenType.Object)
        {
            throw new InvalidOperationException($"{fileName} 顶层必须是 JSON 对象。");
        }
    }

    private static JObject LoadServerValues()
    {
        return LoadJsonObject(
            ConfigService.GetServerConfigPath(),
            () => ToJObject(new ServerConfig()));
    }

    private static JObject LoadWebValues()
    {
        return LoadJsonObject(
            ConfigService.GetWebConfigPath(),
            () => ToJObject(new WebConfig()));
    }

    private static JObject LoadAgentValues()
    {
        return LoadJsonObject(
            ConfigService.GetAgentConfigPath(),
            () => new JObject());
    }

    private static JObject LoadCcrValues()
    {
        return LoadJsonObject(
            ConfigService.GetCcrConfigPath(),
            () => new JObject());
    }

    private static JObject LoadJsonObject(string path, Func<JObject> defaultFactory)
    {
        if (!File.Exists(path))
        {
            return defaultFactory();
        }

        var json = File.ReadAllText(path, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(json))
        {
            return defaultFactory();
        }

        try
        {
            var token = JToken.Parse(json);
            if (token is JObject obj)
            {
                return obj;
            }

            throw new InvalidOperationException($"配置文件顶层必须是 JSON 对象: {path}");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"读取配置文件失败: {path}，{ex.Message}", ex);
        }
    }

    private static void SaveJsonObject(string path, JObject json)
    {
        var targetDir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        File.WriteAllText(path, json.ToString(Newtonsoft.Json.Formatting.Indented), Utf8NoBom);
    }

    private static JObject ToJObject<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, DefaultJsonOptions);
        return JObject.Parse(json);
    }

    private static JObject Clone(JObject value)
    {
        return (JObject)value.DeepClone();
    }
}

public sealed class RuntimeEndpointState
{
    private readonly object _syncRoot = new();
    private RuntimeServiceEndpointDto _server = CreateEmpty("server", "Server");
    private RuntimeServiceEndpointDto _web = CreateEmpty("web", "Web");
    private RuntimeServiceEndpointDto _agent = CreateEmpty("agent", "Agent");
    private RuntimeServiceEndpointDto _ccr = CreateEmpty("ccr", "CCR");

    public void SetServer(RuntimeServiceEndpointDto value) => SetServerInternal(value, "server", "Server");
    public void SetWeb(RuntimeServiceEndpointDto value) => SetWebInternal(value, "web", "Web");
    public void SetAgent(RuntimeServiceEndpointDto value) => SetAgentInternal(value, "agent", "Agent");
    public void SetCcr(RuntimeServiceEndpointDto value) => SetCcrInternal(value, "ccr", "CCR");

    public RuntimeEndpointSnapshot GetSnapshot()
    {
        lock (_syncRoot)
        {
            return new RuntimeEndpointSnapshot
            {
                Server = Clone(_server, "server", "Server"),
                Web = Clone(_web, "web", "Web"),
                Agent = Clone(_agent, "agent", "Agent"),
                Ccr = Clone(_ccr, "ccr", "CCR")
            };
        }
    }

    private void SetServerInternal(RuntimeServiceEndpointDto? value, string key, string title)
    {
        lock (_syncRoot)
        {
            _server = Clone(value ?? CreateEmpty(key, title), key, title);
        }
    }

    private void SetWebInternal(RuntimeServiceEndpointDto? value, string key, string title)
    {
        lock (_syncRoot)
        {
            _web = Clone(value ?? CreateEmpty(key, title), key, title);
        }
    }

    private void SetAgentInternal(RuntimeServiceEndpointDto? value, string key, string title)
    {
        lock (_syncRoot)
        {
            _agent = Clone(value ?? CreateEmpty(key, title), key, title);
        }
    }

    private void SetCcrInternal(RuntimeServiceEndpointDto? value, string key, string title)
    {
        lock (_syncRoot)
        {
            _ccr = Clone(value ?? CreateEmpty(key, title), key, title);
        }
    }

    private static RuntimeServiceEndpointDto Clone(RuntimeServiceEndpointDto value, string key, string title)
    {
        return new RuntimeServiceEndpointDto
        {
            Key = string.IsNullOrWhiteSpace(value.Key) ? key : value.Key,
            Title = string.IsNullOrWhiteSpace(value.Title) ? title : value.Title,
            ManagedByServer = value.ManagedByServer,
            AutoShifted = value.AutoShifted,
            ConfiguredUrl = value.ConfiguredUrl ?? string.Empty,
            ActualUrl = value.ActualUrl ?? string.Empty,
            ConfiguredPort = value.ConfiguredPort,
            ActualPort = value.ActualPort
        };
    }

    private static RuntimeServiceEndpointDto CreateEmpty(string key, string title)
    {
        return new RuntimeServiceEndpointDto
        {
            Key = key,
            Title = title
        };
    }
}

public sealed class RuntimeEndpointSnapshot
{
    public RuntimeServiceEndpointDto Server { get; set; } = new();
    public RuntimeServiceEndpointDto Web { get; set; } = new();
    public RuntimeServiceEndpointDto Agent { get; set; } = new();
    public RuntimeServiceEndpointDto Ccr { get; set; } = new();
}
