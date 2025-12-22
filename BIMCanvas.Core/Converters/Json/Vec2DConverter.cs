using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using BIMCanvas.Core.Models.Geometry;

namespace BIMCanvas.Core.Converters.Json
{
    /// <summary>
    /// Vec2D ↔ [dx, dy] 格式转换
    /// </summary>
    public class Vec2DConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Vec2D);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            var array = JArray.Load(reader);
            if (array.Count != 2)
                throw new JsonException("Vec2D must be an array of 2 numbers");

            return new Vec2D(array[0].Value<double>(), array[1].Value<double>());
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            var vec = (Vec2D)value!;
            writer.WriteStartArray();
            writer.WriteValue(vec.X);
            writer.WriteValue(vec.Y);
            writer.WriteEndArray();
        }
    }
}
