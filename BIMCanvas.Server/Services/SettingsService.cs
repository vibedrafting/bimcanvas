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
        new() { Path = "server.port", Label = "Server 监听端口", ApplyMode = "restart" },
        new() { Path = "web.port", Label = "Web 开发端口", ApplyMode = "restart" },
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
        new() { Path = "layerPresets.User.enabledLayers", Label = "用户图层预设", ApplyMode = "immediate" },
        new() { Path = "layerPresets.Agent.enabledLayers", Label = "Agent 图层预设", ApplyMode = "immediate" }
    ];

    private static readonly IReadOnlyList<SettingsFieldDto> AgentFields =
    [
        new() { Path = "runtimeProvider", Label = "当前 Runtime", ApplyMode = "restart" },
        new() { Path = "claude.baseUrl", Label = "Claude Base URL", ApplyMode = "restart" },
        new() { Path = "claude.apiKey", Label = "Claude API Key", ApplyMode = "restart", Sensitive = true },
        new() { Path = "claude.defaultModel", Label = "Claude 默认模型", ApplyMode = "restart" },
        new() { Path = "claude.defaultEffort", Label = "Claude 默认 Effort", ApplyMode = "restart" },
        new() { Path = "claude.defaultThinking", Label = "Claude 默认 Thinking", ApplyMode = "restart" },
        new() { Path = "claude.maxThinkingTokens", Label = "Claude 最大 Thinking Tokens", ApplyMode = "restart" },
        new() { Path = "claude.modelMapping", Label = "Claude 模型映射", ApplyMode = "restart" },
        new() { Path = "openai.baseUrl", Label = "OpenAI Base URL", ApplyMode = "restart" },
        new() { Path = "openai.apiKey", Label = "OpenAI API Key", ApplyMode = "restart", Sensitive = true },
        new() { Path = "openai.defaultModel", Label = "OpenAI 默认模型", ApplyMode = "restart" },
        new() { Path = "openai.apiMode", Label = "OpenAI API 模式", ApplyMode = "restart" },
        new() { Path = "openai.disableTracing", Label = "OpenAI Tracing", ApplyMode = "restart" },
        new() { Path = "openai.modelMapping", Label = "OpenAI 模型映射", ApplyMode = "restart" }
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
            Runtime = BuildRuntime(serverValues, agentValues)
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

    private SettingsRuntimeDto BuildRuntime(JObject serverValues, JObject agentValues)
    {
        var isCcrEnabled = serverValues.SelectToken("ccr.enabled")?.Value<bool>() ?? false;
        var runtimeProvider = NormalizeRuntimeProvider(
            agentValues.SelectToken("runtimeProvider")?.Value<string>());
        var isClaudeRuntime = string.Equals(runtimeProvider, "claude", StringComparison.OrdinalIgnoreCase);
        var dockerManagedRestart = string.Equals(
            Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
            "true",
            StringComparison.OrdinalIgnoreCase);
        var runtimeSnapshot = _runtimeEndpointState.GetSnapshot();
        var effectiveDefaultModelPath = GetEffectiveDefaultModelPath(runtimeProvider);

        return new SettingsRuntimeDto
        {
            Mode = isCcrEnabled && isClaudeRuntime ? "ccr" : "direct",
            EffectiveDefaultModelPath = effectiveDefaultModelPath,
            EffectiveDefaultModelValue = agentValues.SelectToken(effectiveDefaultModelPath.Replace("agent.", ""))
                ?.Value<string>() ?? "",
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
            var config = JsonSerializer.Deserialize<WebConfig>(
                input.ToString(),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }) ?? throw new InvalidOperationException("web_config.json 内容不能为空对象。");

            config.LayerPresets ??= new Dictionary<string, LayerPreset>();
            config.LayerPresets["User"] = config.LayerPresets.TryGetValue("User", out var userPreset)
                ? userPreset ?? new LayerPreset()
                : new LayerPreset();
            config.LayerPresets["Agent"] = config.LayerPresets.TryGetValue("Agent", out var agentPreset)
                ? agentPreset ?? new LayerPreset()
                : new LayerPreset();

            return ToJObject(config);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"web_config.json 校验失败: {ex.Message}", ex);
        }
    }

    private static JObject ValidateAgent(JObject input)
    {
        EnsureObject(input, "config.json");
        var clone = Clone(input);

        var legacyFields = new[]
        {
            "server",
            "baseUrl",
            "apiKey",
            "defaultEffort",
            "defaultThinking",
            "maxThinkingTokens",
            "modelMapping",
            "permissions",
            "openaiApi",
            "openaiDisableTracing",
            "tools"
        }
        .Where(clone.ContainsKey)
        .ToArray();

        if (legacyFields.Length > 0)
        {
            throw new InvalidOperationException(
                "config.json 检测到旧版顶层字段："
                + string.Join(", ", legacyFields)
                + "。当前只接受新 schema：{ runtimeProvider, claude, openai }。");
        }

        var runtimeProvider = clone["runtimeProvider"]?.Value<string>()?.Trim();
        if (string.IsNullOrWhiteSpace(runtimeProvider))
        {
            throw new InvalidOperationException("config.json.runtimeProvider 必须为 claude 或 openai。");
        }

        runtimeProvider = runtimeProvider.ToLowerInvariant();
        if (runtimeProvider is "claude-sdk" or "openai-agents")
        {
            throw new InvalidOperationException(
                $"config.json.runtimeProvider 不再接受旧值 `{runtimeProvider}`，请改为 claude 或 openai。");
        }

        if (runtimeProvider is not ("claude" or "openai"))
        {
            throw new InvalidOperationException(
                $"config.json.runtimeProvider 不支持 `{runtimeProvider}`，只允许 claude 或 openai。");
        }

        clone["runtimeProvider"] = runtimeProvider;

        var claude = clone["claude"] as JObject
            ?? throw new InvalidOperationException("config.json.claude 必须是对象。");
        var openai = clone["openai"] as JObject
            ?? throw new InvalidOperationException("config.json.openai 必须是对象。");

        ValidateClaudeSection(claude);
        ValidateOpenAiSection(openai);
        return clone;
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
        return ValidateWeb(LoadJsonObject(
            ConfigService.GetWebConfigPath(),
            () => ToJObject(new WebConfig())));
    }

    private static JObject LoadAgentValues()
    {
        var values = LoadJsonObject(
            ConfigService.GetAgentConfigPath(),
            CreateDefaultAgentValues);
        return ValidateAgent(values);
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

    private static JObject CreateDefaultAgentValues()
    {
        return JObject.Parse(
            """
            {
              "runtimeProvider": "claude",
              "claude": {
                "baseUrl": "",
                "apiKey": "",
                "defaultModel": "opus",
                "defaultEffort": "low",
                "defaultThinking": "adaptive",
                "maxThinkingTokens": 8000,
                "permissions": {
                  "allow": [],
                  "deny": []
                },
                "modelMapping": {
                  "opus": { "id": "claude-opus-4-6", "label": "Opus" },
                  "sonnet": { "id": "claude-sonnet-4-20250514", "label": "Sonnet" },
                  "haiku": { "id": "claude-haiku-4-5-20251001", "label": "Haiku" }
                }
              },
              "openai": {
                "baseUrl": "",
                "apiKey": "",
                "defaultModel": "gpt-5",
                "apiMode": "chat_completions",
                "disableTracing": null,
                "permissions": {
                  "allow": [],
                  "deny": []
                },
                "modelMapping": {
                  "gpt-5": { "id": "gpt-5", "label": "GPT-5" }
                }
              }
            }
            """);
    }

    private static void ValidateClaudeSection(JObject section)
    {
        ValidatePermissions(section, "claude.permissions");

        var defaultModel = section["defaultModel"]?.Value<string>()?.Trim();
        if (string.IsNullOrWhiteSpace(defaultModel))
        {
            throw new InvalidOperationException("config.json.claude.defaultModel 必须为 opus / sonnet / haiku。");
        }

        defaultModel = defaultModel.ToLowerInvariant();
        if (defaultModel is not ("opus" or "sonnet" or "haiku"))
        {
            throw new InvalidOperationException("config.json.claude.defaultModel 只允许 opus / sonnet / haiku。");
        }

        section["defaultModel"] = defaultModel;

        var defaultEffort = section["defaultEffort"]?.Value<string>()?.Trim();
        if (!string.IsNullOrWhiteSpace(defaultEffort))
        {
            var normalized = defaultEffort.ToLowerInvariant();
            if (normalized is not ("low" or "medium" or "high" or "max"))
            {
                throw new InvalidOperationException("config.json.claude.defaultEffort 只允许 low / medium / high / max。");
            }

            section["defaultEffort"] = normalized;
        }

        var defaultThinking = section["defaultThinking"]?.Value<string>()?.Trim();
        if (!string.IsNullOrWhiteSpace(defaultThinking))
        {
            var normalized = defaultThinking.ToLowerInvariant();
            if (normalized is not ("off" or "adaptive"))
            {
                throw new InvalidOperationException("config.json.claude.defaultThinking 只允许 off / adaptive。");
            }

            section["defaultThinking"] = normalized;
        }

        if (section["modelMapping"] is JObject modelMapping)
        {
            var invalidKeys = modelMapping.Properties()
                .Select(property => property.Name)
                .Where(name => name is not ("opus" or "sonnet" or "haiku"))
                .ToArray();

            if (invalidKeys.Length > 0)
            {
                throw new InvalidOperationException(
                    "config.json.claude.modelMapping 只允许 opus / sonnet / haiku；检测到："
                    + string.Join(", ", invalidKeys));
            }

            if (modelMapping.Property(defaultModel) == null)
            {
                throw new InvalidOperationException(
                    $"config.json.claude.defaultModel=`{defaultModel}` 必须存在于 claude.modelMapping 中。");
            }
        }
    }

    private static void ValidateOpenAiSection(JObject section)
    {
        ValidatePermissions(section, "openai.permissions");

        var defaultModel = section["defaultModel"]?.Value<string>()?.Trim();
        if (string.IsNullOrWhiteSpace(defaultModel))
        {
            throw new InvalidOperationException("config.json.openai.defaultModel 必须填写真实 OpenAI model id。");
        }

        if (defaultModel.ToLowerInvariant() is "opus" or "sonnet" or "haiku")
        {
            throw new InvalidOperationException(
                "config.json.openai.defaultModel 不允许 Claude alias，必须使用真实 OpenAI model id。");
        }

        section["defaultModel"] = defaultModel;

        var apiMode = section["apiMode"]?.Value<string>()?.Trim();
        if (!string.IsNullOrWhiteSpace(apiMode))
        {
            var normalized = apiMode.ToLowerInvariant().Replace('-', '_');
            if (normalized is not ("chat_completions" or "responses"))
            {
                throw new InvalidOperationException(
                    "config.json.openai.apiMode 只允许 chat_completions / responses。");
            }

            section["apiMode"] = normalized;
        }

        var disableTracing = section["disableTracing"];
        if (disableTracing != null
            && disableTracing.Type != JTokenType.Null
            && disableTracing.Type != JTokenType.Boolean)
        {
            throw new InvalidOperationException(
                "config.json.openai.disableTracing 只允许 null / true / false。");
        }

        if (section["modelMapping"] is JObject modelMapping)
        {
            foreach (var property in modelMapping.Properties())
            {
                if (property.Name is "opus" or "sonnet" or "haiku")
                {
                    throw new InvalidOperationException(
                        $"config.json.openai.modelMapping 不允许 Claude alias `{property.Name}`，必须使用真实 model id。");
                }

                if (property.Value is JObject entry)
                {
                    var configuredId = entry["id"]?.Value<string>()?.Trim();
                    if (!string.IsNullOrWhiteSpace(configuredId)
                        && !string.Equals(configuredId, property.Name, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                        $"config.json.openai.modelMapping 要求 key 和 id 一致；检测到 key=`{property.Name}`、id=`{configuredId}`。");
                    }
                }
            }

            if (modelMapping.Count > 0 && modelMapping.Property(defaultModel) == null)
            {
                throw new InvalidOperationException(
                    $"config.json.openai.defaultModel=`{defaultModel}` 必须存在于 openai.modelMapping 中。");
            }
        }
    }

    private static void ValidatePermissions(JObject section, string path)
    {
        if (section["permissions"] is not JObject permissions)
        {
            return;
        }

        var allow = permissions["allow"];
        if (allow != null && allow.Type != JTokenType.Null && allow.Type != JTokenType.Array)
        {
            throw new InvalidOperationException($"{path}.allow 只允许数组或 null。");
        }

        var deny = permissions["deny"];
        if (deny != null && deny.Type != JTokenType.Array)
        {
            throw new InvalidOperationException($"{path}.deny 只允许数组。");
        }
    }

    private static string NormalizeRuntimeProvider(string? runtimeProvider)
    {
        return string.Equals(runtimeProvider?.Trim(), "openai", StringComparison.OrdinalIgnoreCase)
            ? "openai"
            : "claude";
    }

    private static string GetEffectiveDefaultModelPath(string runtimeProvider)
    {
        return NormalizeRuntimeProvider(runtimeProvider) == "openai"
            ? "agent.openai.defaultModel"
            : "agent.claude.defaultModel";
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
