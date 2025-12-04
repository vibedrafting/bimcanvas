using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using BIMCanvas.Core.Models.Primitives;

namespace BIMCanvas.Core.Converters.Json
{
    /// <summary>
    /// Vec2D ↔ [dx, dy] 格式转换
    /// </summary>
    public class Vec2DConverter : JsonConverter<Vec2D>
    {
        public override Vec2D ReadJson(JsonReader reader, Type objectType, Vec2D existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var array = JArray.Load(reader);
            if (array.Count != 2)
                throw new JsonException("Vec2D must be an array of 2 numbers");

            return new Vec2D(array[0].Value<double>(), array[1].Value<double>());
        }

        public override void WriteJson(JsonWriter writer, Vec2D value, JsonSerializer serializer)
        {
            writer.WriteStartArray();
            writer.WriteValue(value.X);
            writer.WriteValue(value.Y);
            writer.WriteEndArray();
        }
    }
}
