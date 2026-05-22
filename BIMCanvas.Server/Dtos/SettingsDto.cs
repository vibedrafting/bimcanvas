using Newtonsoft.Json.Linq;

namespace BIMCanvas.Server.Dtos;

public sealed class SettingsSnapshotDto
{
    public SettingsGroupDto Server { get; set; } = new();
    public SettingsGroupDto Web { get; set; } = new();
    public SettingsGroupDto Agent { get; set; } = new();
    public SettingsGroupDto Ccr { get; set; } = new();
    public SettingsRuntimeDto Runtime { get; set; } = new();
}

public sealed class SettingsRuntimeDto
{
    public string Mode { get; set; } = "direct";
    public string EffectiveDefaultModelPath { get; set; } = "agent.claude.defaultModel";
    public string EffectiveDefaultModelValue { get; set; } = "";
    public bool DockerManagedRestart { get; set; }
    public string RestartBehavior { get; set; } = "manual";
    public string RestartHint { get; set; } = "";
    public RuntimeServiceEndpointDto Server { get; set; } = new();
    public RuntimeServiceEndpointDto Web { get; set; } = new();
    public RuntimeServiceEndpointDto Agent { get; set; } = new();
    public RuntimeServiceEndpointDto Ccr { get; set; } = new();
}

public sealed class RuntimeServiceEndpointDto
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public bool ManagedByServer { get; set; }
    public bool AutoShifted { get; set; }
    public string ConfiguredUrl { get; set; } = "";
    public string ActualUrl { get; set; } = "";
    public int? ConfiguredPort { get; set; }
    public int? ActualPort { get; set; }
}

public sealed class SettingsGroupDto
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string SourceFile { get; set; } = "";
    public string ApplyMode { get; set; } = "restart";
    public bool RequiresRestart { get; set; }
    public JObject Values { get; set; } = new();
    public List<SettingsFieldDto> Fields { get; set; } = new();
}

public sealed class SettingsFieldDto
{
    public string Path { get; set; } = "";
    public string Label { get; set; } = "";

    /// <summary>控件类型:string | number | bool | enum | json(复杂字段降级为内嵌 JSON 编辑器)。</summary>
    public string Type { get; set; } = "string";

    /// <summary>enum 类型的可选值（其它类型为 null）。与服务端校验取值同源。</summary>
    public List<string>? EnumValues { get; set; }

    /// <summary>字段说明（"描述符供文档"：仅在可视化模式由控件旁提示渲染）。</summary>
    public string? Help { get; set; }

    public string ApplyMode { get; set; } = "restart";
    public bool Sensitive { get; set; }
}

public sealed class UpdateSettingsRequestDto
{
    public JObject? Server { get; set; }
    public JObject? Web { get; set; }
    public JObject? Agent { get; set; }
    public JObject? Ccr { get; set; }
}

public sealed class UpdateSettingsResponseDto
{
    public bool Success { get; set; }
    public List<string> ChangedGroups { get; set; } = new();
    public List<string> RestartRequiredGroups { get; set; } = new();
    public SettingsSnapshotDto Settings { get; set; } = new();
}

public sealed class LlmEndpointTestRequestDto
{
    public string RuntimeProvider { get; set; } = "claude";
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "";
    public string? ApiMode { get; set; }
}

public sealed class LlmEndpointTestResultDto
{
    public bool Success { get; set; }
    public long LatencyMs { get; set; }
    public int? StatusCode { get; set; }
    public string ErrorType { get; set; } = "unknown";
    public string ErrorMessage { get; set; } = "";
    public string SampleResponseSnippet { get; set; } = "";
    public string RequestUrl { get; set; } = "";
}
