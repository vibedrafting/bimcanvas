using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using BIMCanvas.Core.Models.Primitives;

namespace BIMCanvas.Core.Converters.Json
{
    /// <summary>
    /// Line2D ↔ [[x1,y1], [x2,y2]] 格式转换
    /// </summary>
    public class Line2DConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Line2D);
        }

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;

            var array = JArray.Load(reader);
            if (array.Count != 2)
                throw new JsonException("Line2D must be an array of 2 points");

            var start = (JArray)array[0];
            var end = (JArray)array[1];

            if (start.Count != 2 || end.Count != 2)
                throw new JsonException("Each point must be an array of 2 numbers");

            return new Line2D(
                new Point2D(start[0].Value<double>(), start[1].Value<double>()),
                new Point2D(end[0].Value<double>(), end[1].Value<double>())
            );
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var line = (Line2D)value;
            writer.WriteStartArray();
            // Start point
            writer.WriteStartArray();
            writer.WriteValue(line.Start.X);
            writer.WriteValue(line.Start.Y);
            writer.WriteEndArray();
            // End point
            writer.WriteStartArray();
            writer.WriteValue(line.End.X);
            writer.WriteValue(line.End.Y);
            writer.WriteEndArray();
            writer.WriteEndArray();
        }
    }
}
