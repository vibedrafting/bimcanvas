using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using BIMCanvas.Core.Models.Primitives;

namespace BIMCanvas.Core.Converters.Json
{
    /// <summary>
    /// Polygon2D ↔ [[x,y], ...] 格式转换（隐式闭合）
    /// </summary>
    public class Polygon2DConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Polygon2D);
        }

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;

            var array = JArray.Load(reader);
            var vertices = new List<Point2D>();

            foreach (var item in array)
            {
                if (item.Type != JTokenType.Array || ((JArray)item).Count != 2)
                    throw new JsonException("Each vertex must be an array of 2 numbers");

                var point = (JArray)item;
                vertices.Add(new Point2D(point[0].Value<double>(), point[1].Value<double>()));
            }

            return new Polygon2D(vertices.ToArray());
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var polygon = (Polygon2D)value;
            writer.WriteStartArray();
            foreach (var vertex in polygon.Vertices)
            {
                writer.WriteStartArray();
                writer.WriteValue(vertex.X);
                writer.WriteValue(vertex.Y);
                writer.WriteEndArray();
            }
            writer.WriteEndArray();
        }
    }
}
