using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using BIMCanvas.Core.Models.Primitives;

namespace BIMCanvas.Core.Converters.Json
{
    /// <summary>
    /// Point2D ↔ [x, y] 格式转换
    /// </summary>
    public class Point2DConverter : JsonConverter<Point2D>
    {
        public override Point2D ReadJson(JsonReader reader, Type objectType, Point2D existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var array = JArray.Load(reader);
            if (array.Count != 2)
                throw new JsonException("Point2D must be an array of 2 numbers");

            return new Point2D(array[0].Value<double>(), array[1].Value<double>());
        }

        public override void WriteJson(JsonWriter writer, Point2D value, JsonSerializer serializer)
        {
            writer.WriteStartArray();
            writer.WriteValue(value.X);
            writer.WriteValue(value.Y);
            writer.WriteEndArray();
        }
    }
}
