using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using BIMCanvas.Core.Algorithms.Spatial;
using BIMCanvas.Core.Models.Semantic;
using BIMCanvas.Core.Models.Geometry;

namespace BIMCanvas.Core.Converters.Json
{
    /// <summary>
    /// Facing ↔ JSON 格式转换。
    /// 反序列化兼容三种输入：新对象 / 旧字符串 / 旧向量数组。
    /// 序列化统一输出对象格式：{ value: [dx, dy] | null, semantic: string | null }
    /// </summary>
    public class FacingConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Facing);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            var token = JToken.Load(reader);

            // 旧格式：语义字符串 "north"
            if (token.Type == JTokenType.String)
            {
                var semanticStr = token.Value<string>()!;
                if (!FacingHelper.TrySemanticToVector(semanticStr, out var semanticValue))
                {
                    throw new JsonException(
                        $"facing 值 '{semanticStr}' 无效。" +
                        $"合法值（小写英文全称）：north / south / east / west / " +
                        $"northeast / northwest / southeast / southwest");
                }

                return new Facing(semanticValue, null);
            }

            // 旧格式：向量数组 [x, y]
            if (token.Type == JTokenType.Array)
            {
                var vector = token.ToObject<Vec2D>(serializer);
                return new Facing(vector.Normalize(), null);
            }

            // 新格式：{ value, semantic }
            if (token.Type == JTokenType.Object)
            {
                var obj = (JObject)token;
                Vec2D? value = null;
                var valueToken = obj["value"];
                if (valueToken != null && valueToken.Type != JTokenType.Null)
                {
                    value = valueToken.ToObject<Vec2D>(serializer);
                }

                string? semantic = null;
                var semanticToken = obj["semantic"];
                if (semanticToken != null && semanticToken.Type != JTokenType.Null)
                {
                    if (semanticToken.Type != JTokenType.String)
                        throw new JsonException("facing.semantic 必须是字符串或 null");

                    semantic = semanticToken.Value<string>();
                }

                return new Facing(value, semantic);
            }

            throw new JsonException("facing 必须是对象、字符串或 [x, y] 数组格式");
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            var facing = (Facing)value!;
            writer.WriteStartObject();

            writer.WritePropertyName("value");
            if (facing.Value.HasValue)
            {
                writer.WriteStartArray();
                writer.WriteValue(facing.Value.Value.X);
                writer.WriteValue(facing.Value.Value.Y);
                writer.WriteEndArray();
            }
            else
            {
                writer.WriteNull();
            }

            writer.WritePropertyName("semantic");
            if (string.IsNullOrWhiteSpace(facing.Semantic))
            {
                writer.WriteNull();
            }
            else
            {
                writer.WriteValue(facing.Semantic);
            }

            writer.WriteEndObject();
        }
    }
}
