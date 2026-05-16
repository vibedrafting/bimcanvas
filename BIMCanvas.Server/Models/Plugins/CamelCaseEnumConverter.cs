using System.Text.Json;
using System.Text.Json.Serialization;

namespace BIMCanvas.Server.Models.Plugins;

/// <summary>
/// 统一 camelCase enum 序列化 converter。BIMCanvas plugin 契约 enum 全部使用此 converter,
/// 保证 JSON 输出形态与主真理源 v1.1 §3.13 / §3.9 示例一致
/// (例:trustState = "trusted",sourceKind = "github",mode = "projectBound")。
/// 不允许整数值落地,避免 plugin 端按数字猜测 enum 含义。
/// </summary>
public sealed class CamelCaseEnumConverter : JsonStringEnumConverter
{
    public CamelCaseEnumConverter() : base(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
    {
    }
}
