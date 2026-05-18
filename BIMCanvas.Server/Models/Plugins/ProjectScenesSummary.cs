using System.Collections.Generic;

namespace BIMCanvas.Server.Models.Plugins;

/// <summary>
/// PluginLaunchContext 时刻的 scenes 快照 (主真理源 v1.1 §3.3 字段
/// <c>ProjectScenesSummary? Scenes</c>)。
///
/// 包含项目内全部 scenes 与当前 active scene 标识,供 Agent 端 PluginContext
/// 投影使用 (PluginContext 只把 <c>activeSceneId</c> + scenes 列表暴露给 plugin 作者,
/// 不暴露 PluginLaunchContext 本身)。
///
/// 仅 <see cref="LaunchMode.ProjectBound"/> 时由 Server 生成;<see cref="LaunchMode.Projectless"/>
/// 时 PluginLaunchContext.Scenes 为 null。
/// 序列化:Newtonsoft.Json + <c>CamelCasePropertyNamesContractResolver</c>(调用方 settings 配置)。
/// </summary>
public sealed record ProjectScenesSummary(
    IReadOnlyList<ProjectScene> Scenes,
    string? ActiveSceneId
);
