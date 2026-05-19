using System.Collections.Generic;

namespace BIMCanvas.Server.Models.Plugins;

/// <summary>
/// StaticPluginValidator / ExecutablePluginProbe 校验结果 (主真理源 v1.1 §3.12)。
/// 校验类 API 优先返回结构化结果,失败由调用方决定是否抛对应 <see cref="Exceptions.PluginException"/>。
/// </summary>
public sealed record ValidationResult(
    bool Success,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public static ValidationResult Ok() => new(true, System.Array.Empty<string>(), System.Array.Empty<string>());

    public static ValidationResult Failed(IReadOnlyList<string> errors)
        => new(false, errors, System.Array.Empty<string>());

    public static ValidationResult Failed(string error)
        => new(false, new[] { error }, System.Array.Empty<string>());
}
