namespace BIMCanvas.Server.Models;

/// <summary>
/// Web 客户端配置（<BIMCANVAS_HOME>/web_config.json）
/// </summary>
public class WebConfig
{
    public string DefaultModel { get; set; } = "sonnet";
    public List<CustomModel> CustomModels { get; set; } = new();
    public Dictionary<string, LayerPreset>? LayerPresets { get; set; }
}

/// <summary>
/// 自定义 AI 模型选项
/// </summary>
public class CustomModel
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
}

/// <summary>
/// 图层预设配置
/// </summary>
public class LayerPreset
{
    public List<string> EnabledLayers { get; set; } = new();
}
