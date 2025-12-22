using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using BIMCanvas.Core.Models.Geometry;

namespace BIMCanvas.Core.Converters.Json
{
    /// <summary>
    /// Point2D ↔ [x, y] 格式转换
    /// </summary>
    public class Point2DConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Point2D);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            var array = JArray.Load(reader);
            if (array.Count != 2)
                throw new JsonException("Point2D must be an array of 2 numbers");

            return new Point2D(array[0].Value<double>(), array[1].Value<double>());
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            var point = (Point2D)value!;
            writer.WriteStartArray();
            writer.WriteValue(point.X);
            writer.WriteValue(point.Y);
            writer.WriteEndArray();
        }
    }
}
