using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using BIMCanvas.Core.Models.Geometry;

namespace BIMCanvas.Core.Converters.Json
{
    /// <summary>
    /// AABB ↔ [minX, minY, maxX, maxY] 格式转换
    /// </summary>
    public class AABBConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(AABB);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            var array = JArray.Load(reader);
            if (array.Count != 4)
                throw new JsonException("AABB must be an array of 4 numbers [minX, minY, maxX, maxY]");

            return new AABB(
                array[0].Value<double>(),
                array[1].Value<double>(),
                array[2].Value<double>(),
                array[3].Value<double>()
            );
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            var aabb = (AABB)value!;
            writer.WriteStartArray();
            writer.WriteValue(aabb.MinX);
            writer.WriteValue(aabb.MinY);
            writer.WriteValue(aabb.MaxX);
            writer.WriteValue(aabb.MaxY);
            writer.WriteEndArray();
        }
    }
}
