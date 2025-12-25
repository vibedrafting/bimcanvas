using System;
using Newtonsoft.Json;
using BIMCanvas.Core.Models.Shared;

namespace BIMCanvas.Core.Converters.Json
{
    /// <summary>
    /// StrategyStatus 枚举的 JSON 转换器
    /// 序列化为小写字符串："valid" | "dirty" | "invalid"
    /// </summary>
    public class StrategyStatusConverter : JsonConverter<StrategyStatus>
    {
        public override StrategyStatus ReadJson(JsonReader reader, Type objectType,
            StrategyStatus existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.String)
            {
                var value = reader.Value?.ToString()?.ToLowerInvariant();
                switch (value)
                {
                    case "valid": return StrategyStatus.Valid;
                    case "dirty": return StrategyStatus.Dirty;
                    case "invalid": return StrategyStatus.Invalid;
                    default: throw new JsonSerializationException($"Unknown StrategyStatus: {value}");
                }
            }
            throw new JsonSerializationException("Expected string for StrategyStatus");
        }

        public override void WriteJson(JsonWriter writer, StrategyStatus value, JsonSerializer serializer)
        {
            string strValue;
            switch (value)
            {
                case StrategyStatus.Valid: strValue = "valid"; break;
                case StrategyStatus.Dirty: strValue = "dirty"; break;
                case StrategyStatus.Invalid: strValue = "invalid"; break;
                default: throw new ArgumentOutOfRangeException(nameof(value));
            }
            writer.WriteValue(strValue);
        }
    }
}
