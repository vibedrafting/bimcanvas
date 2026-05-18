using System.Collections.Generic;
using Newtonsoft.Json;

namespace BIMCanvas.Server.Models.Plugins;

/// <summary>
/// Agent 子进程启动上下文 (主真理源 v1.1 §3.3 + 模板 §4.4)。
///
/// <para>
/// 生成时机:程序启动 + 项目 binding 完成后,由 Server 一次性序列化通过 stdin / env / file
/// 注入 Python 子进程。本 record 不可变 (init-only positional record),Server 与 Agent
/// 都不应在生成后再改写。
/// </para>
///
/// <para>
/// 设计纪律 (V14 / 主真理源 §3.3):
/// - immutable:任何 mutate 尝试都是 bug,V14 T10 用 reflection 校验所有属性 init-only。
/// - LaunchMode 是平台内部 enum,不暴露给 plugin 作者:Python 端 PluginContext 只暴露
///   server_url / project_path / activePluginId / activeSceneId 四类窄字段。
/// - Projectless 时 ProjectPath / ActiveSceneId / Scenes / Lock / ReadOnlySceneIds 均无意义,
///   Server gate 一律 403 (V12a)。
/// - ProjectBound 时 ProjectPath / ActiveSceneId / Scenes / Lock 必须非空 (V14 T11)。
/// </para>
///
/// 序列化:Newtonsoft.Json + <c>CamelCasePropertyNamesContractResolver</c> +
/// <c>StringEnumConverter(CamelCaseNamingStrategy)</c>,JSON 形态完全 camelCase
/// (字段命名约束在调用方 settings 中配置,本 record 不再标 attribute)。
/// 特例:<c>Lock</c> 是 C# 关键字,在 JSON 中要落为 "lock";靠 ContractResolver 自动 lower-case 即可,无需额外 attribute。
/// </summary>
public sealed record PluginLaunchContext(
    string ActivePluginId,
    string ActivePluginRoot,
    LaunchMode Mode,
    string? ProjectPath,
    string? ActiveSceneId,
    ProjectScenesSummary? Scenes,
    [property: JsonProperty("lock")] PluginLockSummary? Lock,
    string ServerUrl,
    TrustMode TrustMode,
    IReadOnlyList<string> ReadOnlySceneIds
);
