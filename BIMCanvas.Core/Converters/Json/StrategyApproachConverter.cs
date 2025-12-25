using System;
using Newtonsoft.Json;
using BIMCanvas.Core.Models.Shared;

namespace BIMCanvas.Core.Converters.Json
{
    /// <summary>
    /// StrategyApproach 枚举的 JSON 转换器
    /// 序列化为 snake_case 字符串
    /// </summary>
    public class StrategyApproachConverter : JsonConverter<StrategyApproach>
    {
        public override StrategyApproach ReadJson(JsonReader reader, Type objectType,
            StrategyApproach existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.String)
            {
                var value = reader.Value?.ToString()?.ToLowerInvariant();
                switch (value)
                {
                    case "circulation_first": return StrategyApproach.CirculationFirst;
                    case "furniture_first": return StrategyApproach.FurnitureFirst;
                    case "space_efficiency": return StrategyApproach.SpaceEfficiency;
                    case "style_variation": return StrategyApproach.StyleVariation;
                    case "custom": return StrategyApproach.Custom;
                    default: throw new JsonSerializationException($"Unknown StrategyApproach: {value}");
                }
            }
            throw new JsonSerializationException("Expected string for StrategyApproach");
        }

        public override void WriteJson(JsonWriter writer, StrategyApproach value, JsonSerializer serializer)
        {
            string strValue;
            switch (value)
            {
                case StrategyApproach.CirculationFirst: strValue = "circulation_first"; break;
                case StrategyApproach.FurnitureFirst: strValue = "furniture_first"; break;
                case StrategyApproach.SpaceEfficiency: strValue = "space_efficiency"; break;
                case StrategyApproach.StyleVariation: strValue = "style_variation"; break;
                case StrategyApproach.Custom: strValue = "custom"; break;
                default: throw new ArgumentOutOfRangeException(nameof(value));
            }
            writer.WriteValue(strValue);
        }
    }
}
