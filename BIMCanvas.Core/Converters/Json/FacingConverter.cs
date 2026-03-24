using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using BIMCanvas.Core.Models.Semantic;
using BIMCanvas.Core.Models.Geometry;

namespace BIMCanvas.Core.Converters.Json
{
    /// <summary>
    /// Facing ↔ JSON 格式转换。
    /// 反序列化：只接受 8 个小写英文全称字符串（north/south/east/west/...）
    /// 序列化：语义类型输出字符串，向量类型输出 [dx, dy]（Web端修复前的过渡）
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

            throw new JsonException(
                $"facing 必须是字符串，不接受 {token.Type} 格式。" +
                $"合法值：north / south / east / west / northeast / northwest / southeast / southwest");
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
