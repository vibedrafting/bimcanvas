namespace BIMCanvas.Server.Models.Plugins;

/// <summary>
/// Agent 子进程启动上下文 (主真理源 v1.1 §3.3,「项目去插件态」后精简)。
///
/// <para>
/// 生成时机:程序启动 + 项目打开后,由 Server 一次性序列化通过 file 注入 Python 子进程。
/// 本 record 不可变 (init-only positional record),Server 与 Agent 都不应在生成后再改写。
/// </para>
///
/// <para>
/// 北极星(项目 = 被动数据基底,不记录哪个插件执行过):LaunchContext 只携带
/// 「当前激活插件身份 + 运行模式 + 项目路径 + Server 回调地址」,**不再携带
/// scenes / activeSceneId / lock / readOnlySceneIds** 等项目侧插件运行态。
/// </para>
///
/// <para>
/// 设计纪律:
/// - immutable:任何 mutate 尝试都是 bug。
/// - LaunchMode 是平台内部 enum,不暴露给 plugin 作者:Python 端 PluginContext 只暴露
///   server_url / project_path / active_plugin_id 三类窄字段。
/// - Projectless 时 ProjectPath 为 null,Server gate 一律 403 (V12a)。
/// </para>
///
/// 序列化:Newtonsoft.Json + <c>DefaultContractResolver + CamelCaseNamingStrategy</c> +
/// <c>StringEnumConverter(CamelCaseNamingStrategy)</c>,JSON 形态完全 camelCase。
/// </summary>
public sealed record PluginLaunchContext(
    string ActivePluginId,
    string ActivePluginRoot,
    LaunchMode Mode,
    string? ProjectPath,
    string ServerUrl,
    TrustMode TrustMode
);
