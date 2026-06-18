namespace BIMCanvas.Server.Models;

/// <summary>
/// Web 客户端配置（<BIMCANVAS_HOME>/web_config.json）
/// </summary>
public class WebConfig
{
    public Dictionary<string, LayerPreset>? LayerPresets { get; set; }

    /// <summary>项目健康检查偏好（首页扳手按钮配置面板）。</summary>
    public HealthCheckPrefs? HealthCheck { get; set; }
}

/// <summary>
/// 健康检查偏好。存 web_config.json，跨会话/重装保留。
/// </summary>
public class HealthCheckPrefs
{
    /// <summary>是否在导入/新建/恢复项目时自动跑健康检查。默认 false（手动触发）。</summary>
    public bool AutoCheckOnLoad { get; set; } = false;

    /// <summary>勾选启用的 check id 子集；null = 全部已注册 check。</summary>
    public List<string>? EnabledCheckIds { get; set; }
}

/// <summary>
/// 图层预设配置
/// </summary>
public class LayerPreset
{
    public List<string> EnabledLayers { get; set; } = new();
}
