using System;
using System.Text.Json.Serialization;

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
/// 实际读写实现属于组2 范围。
/// </para>
/// </summary>
public sealed record PluginInstallState(
    [property: JsonIgnore] string PluginId,
    [property: JsonPropertyName("trustState")] TrustState TrustState,
    [property: JsonPropertyName("installedAt")] DateTimeOffset InstalledAt,
    [property: JsonPropertyName("trustedAt")] DateTimeOffset? TrustedAt,
    [property: JsonPropertyName("sourceUrl")] string? SourceUrl,
    [property: JsonPropertyName("resolvedCommit")] string? ResolvedCommit,
    [property: JsonPropertyName("sourceKind")] SourceKind SourceKind,
    [property: JsonPropertyName("manifestChecksum")] string ManifestChecksum,
    [property: JsonPropertyName("installedVersion")] string InstalledVersion
);
