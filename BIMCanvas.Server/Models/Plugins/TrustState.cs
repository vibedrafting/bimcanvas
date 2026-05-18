namespace BIMCanvas.Server.Models.Plugins;

/// <summary>
/// Plugin 在 BIMCANVAS_HOME/plugins-state.json 中的信任状态
/// (主真理源 v1.1 §3.2 trustState 子状态 / §3.13 平台集中存储)。
/// PluginTrustService 是唯一允许修改此值的 Service;plugin 代码不可触达该文件。
/// <para>
/// 序列化为 camelCase 字符串("untrusted" / "trusted") —— 由 Program.cs 全局
/// <c>StringEnumConverter(CamelCaseNamingStrategy)</c> 接管,本 enum 不再单独标注 converter。
/// </para>
/// </summary>
public enum TrustState
{
    /// <summary>
    /// StaticPluginValidator 通过、ExecutablePluginProbe 尚未执行,
    /// 不能进入 active (V13 T6b)。
    /// </summary>
    Untrusted,

    /// <summary>
    /// ExecutablePluginProbe 通过 + 用户已点 [信任并激活],
    /// 可被 PluginLifecycleService 设为 active (V13 T6c)。
    /// </summary>
    Trusted,
}
