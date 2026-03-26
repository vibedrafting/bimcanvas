using Newtonsoft.Json.Linq;

namespace BIMCanvas.Server.Dtos;

public sealed class SettingsSnapshotDto
{
    public SettingsGroupDto Server { get; set; } = new();
    public SettingsGroupDto Web { get; set; } = new();
    public SettingsGroupDto Agent { get; set; } = new();
    public SettingsGroupDto Ccr { get; set; } = new();
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
