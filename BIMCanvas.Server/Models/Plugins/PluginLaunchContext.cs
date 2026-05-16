using System.Collections.Generic;
using System.Text.Json.Serialization;

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
/// 序列化:System.Text.Json + CamelCaseEnumConverter,JSON 形态完全 camelCase。
/// </summary>
public sealed record PluginLaunchContext(
    [property: JsonPropertyName("activePluginId")] string ActivePluginId,
    [property: JsonPropertyName("activePluginRoot")] string ActivePluginRoot,
    [property: JsonPropertyName("mode")] LaunchMode Mode,
    [property: JsonPropertyName("projectPath")] string? ProjectPath,
    [property: JsonPropertyName("activeSceneId")] string? ActiveSceneId,
    [property: JsonPropertyName("scenes")] ProjectScenesSummary? Scenes,
    [property: JsonPropertyName("lock")] PluginLockSummary? Lock,
    [property: JsonPropertyName("serverUrl")] string ServerUrl,
    [property: JsonPropertyName("trustMode")] TrustMode TrustMode,
    [property: JsonPropertyName("readOnlySceneIds")] IReadOnlyList<string> ReadOnlySceneIds
);
