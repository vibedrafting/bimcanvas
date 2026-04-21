namespace BIMCanvas.Server.Models;

/// <summary>
/// Web 客户端配置（<BIMCANVAS_HOME>/web_config.json）
/// </summary>
public class WebConfig
{
    public Dictionary<string, LayerPreset>? LayerPresets { get; set; }
}

/// <summary>
/// 图层预设配置
/// </summary>
public class LayerPreset
{
    public List<string> EnabledLayers { get; set; } = new();
}
