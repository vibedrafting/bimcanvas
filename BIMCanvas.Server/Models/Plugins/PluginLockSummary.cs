using System;

namespace BIMCanvas.Server.Models.Plugins;

/// <summary>
/// PluginLaunchContext 引用的 lock 摘要,对应 .bcp 项目内
/// <c>plugins.lock.json</c> 中当前 ActiveSceneId 一项的字段子集
/// (主真理源 v1.1 §3.3 + §3.9 plugins.lock.json 补强字段)。
///
/// 完整 plugins.lock.json 文件由组2 PluginLifecycleService 管理 (按 sceneId 分键);
/// 本 record 只是给 Agent 子进程的只读投影,不承载 lock 文件全量。
///
/// <para>
/// 字段语义:
/// - <c>SourceUrl</c> / <c>ResolvedCommit</c>:仅 GitHub 来源非 null;Local 来源标记 "复现性较弱"。
/// - <c>ScaffoldChecksum</c>:projectMount 物化时 (M2 bind-time) 计算,M1 阶段允许 null。
/// - <c>TrustedAt</c>:首次 [信任并激活] 时刻;Phase 1 trustState 一旦变 trusted 即不可逆。
/// </para>
///
/// 序列化:Newtonsoft.Json + <c>DefaultContractResolver + CamelCaseNamingStrategy</c>(调用方 settings 配置;只转属性名,不转 dict key)。
/// </summary>
public sealed record PluginLockSummary(
    string PluginId,
    string Version,
    string? SourceUrl,
    string? ResolvedCommit,
    SourceKind SourceKind,
    string ManifestChecksum,
    string? ScaffoldChecksum,
    DateTimeOffset? TrustedAt,
    DateTimeOffset InstalledAt
);
