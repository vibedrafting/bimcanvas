using System.Text.Json.Serialization;

namespace BIMCanvas.Server.Models.Plugins;

/// <summary>
/// Plugin 来源类型 (主真理源 v1.1 §3.9 plugins.lock.json / §3.13 plugins-state.json)。
/// <see cref="Local"/> 标记 "复现性较弱" —— 接收方拿不到 GitHub URL 与 resolvedCommit,
/// 无法重建相同环境;UI / lock 文档需对该 plugin 做警示展示。
/// </summary>
[JsonConverter(typeof(CamelCaseEnumConverter))]
public enum SourceKind
{
    /// <summary>从 GitHub clone:SourceUrl + ResolvedCommit 可完整复现。</summary>
    Github,

    /// <summary>本地路径直接复制 / 软链:无远程 URL,复现性较弱。</summary>
    Local,

    /// <summary>从 zip 包安装:Phase 2 可能扩展;Phase 1 暂作占位。</summary>
    Zip,
}
