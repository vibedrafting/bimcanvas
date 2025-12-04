using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using BIMCanvas.Core.Models.Primitives;

namespace BIMCanvas.Core.Converters.Json
{
    /// <summary>
    /// AABB ↔ [minX, minY, maxX, maxY] 格式转换
    /// </summary>
    public class AABBConverter : JsonConverter<AABB>
    {
        public override AABB ReadJson(JsonReader reader, Type objectType, AABB existingValue, bool hasExistingValue, JsonSerializer serializer)
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

        public override void WriteJson(JsonWriter writer, AABB value, JsonSerializer serializer)
        {
            writer.WriteStartArray();
            writer.WriteValue(value.MinX);
            writer.WriteValue(value.MinY);
            writer.WriteValue(value.MaxX);
            writer.WriteValue(value.MaxY);
            writer.WriteEndArray();
        }
    }
}
