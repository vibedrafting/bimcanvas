using System.Text.Json.Serialization;

namespace BIMCanvas.Server.Models.Plugins;

/// <summary>
/// Agent 子进程对 active plugin 代码的信任模式 (主真理源 v1.1 §3.3 / §6.1 R9)。
/// Phase 1 实际只会以 <see cref="FullTrust"/> 启动 Agent;<see cref="Untrusted"/>
/// 是 Phase 2+ sandbox 模式过渡占位 (PluginLifecycleService 在 Phase 1 不会让
/// Untrusted plugin 走到 Agent 启动)。
/// </summary>
[JsonConverter(typeof(CamelCaseEnumConverter))]
public enum TrustMode
{
    /// <summary>
    /// 用户已 [信任并激活] plugin,Agent 完全执行其 Python 代码。
    /// </summary>
    FullTrust,

    /// <summary>
    /// 未信任,占位值。Phase 1 不会出现在已启动的 Agent 上下文里;
    /// Phase 2+ 用于"沙箱试运行"等中间模式。
    /// </summary>
    Untrusted,
}
