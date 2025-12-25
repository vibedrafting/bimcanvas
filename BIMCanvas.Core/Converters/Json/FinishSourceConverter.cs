using System;
using Newtonsoft.Json;
using BIMCanvas.Core.Models.Shared;

namespace BIMCanvas.Core.Converters.Json
{
    /// <summary>
    /// FinishSource 枚举的 JSON 转换器
    /// 序列化为 snake_case 字符串："room_default" | "zone_override" | "user_override"
    /// </summary>
    public class FinishSourceConverter : JsonConverter<FinishSource>
    {
        public override FinishSource ReadJson(JsonReader reader, Type objectType,
            FinishSource existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.String)
            {
                var value = reader.Value?.ToString()?.ToLowerInvariant();
                switch (value)
                {
                    case "room_default": return FinishSource.RoomDefault;
                    case "zone_override": return FinishSource.ZoneOverride;
                    case "user_override": return FinishSource.UserOverride;
                    default: throw new JsonSerializationException($"Unknown FinishSource: {value}");
                }
            }
            throw new JsonSerializationException("Expected string for FinishSource");
        }

        public override void WriteJson(JsonWriter writer, FinishSource value, JsonSerializer serializer)
        {
            string strValue;
            switch (value)
            {
                case FinishSource.RoomDefault: strValue = "room_default"; break;
                case FinishSource.ZoneOverride: strValue = "zone_override"; break;
                case FinishSource.UserOverride: strValue = "user_override"; break;
                default: throw new ArgumentOutOfRangeException(nameof(value));
            }
            writer.WriteValue(strValue);
        }
    }
}
