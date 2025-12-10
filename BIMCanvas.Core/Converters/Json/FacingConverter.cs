using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using BIMCanvas.Core.Models.Document;
using BIMCanvas.Core.Models.Primitives;

namespace BIMCanvas.Core.Converters.Json
{
    /// <summary>
    /// Facing ↔ "north" | [dx, dy] 格式转换
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

            // 语义字符串格式："north"
            if (token.Type == JTokenType.String)
            {
                var semanticStr = token.Value<string>()!;
                var direction = Facing.ParseSemantic(semanticStr);
                return new Facing(direction);
            }

            // 向量格式：[dx, dy]
            if (token.Type == JTokenType.Array)
            {
                var array = (JArray)token;
                if (array.Count != 2)
                    throw new JsonException("Facing vector must be an array of 2 numbers");

                var vector = new Vec2D(array[0].Value<double>(), array[1].Value<double>());
                return new Facing(vector);
            }

            throw new JsonException($"Facing must be a string or array, got {token.Type}");
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            var facing = (Facing)value!;
            if (facing.IsSemantic)
            {
                // 使用小写字符串格式
                writer.WriteValue(facing.GetSemanticString());
            }
            else
            {
                var vector = facing.Vector!.Value;
                writer.WriteStartArray();
                writer.WriteValue(vector.X);
                writer.WriteValue(vector.Y);
                writer.WriteEndArray();
            }
        }
    }
}
