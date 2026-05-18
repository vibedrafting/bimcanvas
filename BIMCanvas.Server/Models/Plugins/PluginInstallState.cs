using System;
using Newtonsoft.Json;

namespace BIMCanvas.Server.Models.Plugins;

/// <summary>
/// BIMCANVAS_HOME/plugins-state.json 单条记录 (主真理源 v1.1 §3.13 平台集中存储 + §8.2)。
///
/// <para>
/// 文件结构:<c>Dictionary&lt;pluginId, PluginInstallState&gt;</c> ——
/// <c>PluginId</c> 是 dict key,不出现在 value JSON 里 (因此本 record 上 PluginId 标
/// <see cref="JsonIgnoreAttribute"/>);其余字段全部以 camelCase 落 JSON。
/// </para>
///
/// <para>
/// 安全决策 (主真理源 §3.13 + §8.2):此文件位于 <c>BIMCANVAS_HOME/</c>,plugin 代码不可触达;
/// PluginTrustService 是唯一允许 mutate 该文件的 Service。组1 只给出本 record + JSON 形状,
/// 实际读写实现属于组2 范围。字段名走 Newtonsoft <c>DefaultContractResolver + CamelCaseNamingStrategy</c>
/// (在 PluginTrustService 内联配置;只转属性名,不转 dict key)。
/// </para>
/// </summary>
public sealed record PluginInstallState(
    [property: JsonIgnore] string PluginId,
    TrustState TrustState,
    DateTimeOffset InstalledAt,
    DateTimeOffset? TrustedAt,
    string? SourceUrl,
    string? ResolvedCommit,
    SourceKind SourceKind,
    string ManifestChecksum,
    string InstalledVersion
);
