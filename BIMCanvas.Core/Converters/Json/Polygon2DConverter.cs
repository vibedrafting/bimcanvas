using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using BIMCanvas.Core.Models.Primitives;

namespace BIMCanvas.Core.Converters.Json
{
    /// <summary>
    /// Polygon2D JSON 转换器，支持内环
    /// 读取格式：
    /// - 简单格式（无内环）：[[x,y], ...]
    /// - 完整格式（有内环）：{ "shell": [[x,y], ...], "holes": [[[x,y], ...], ...] }
    /// 写入格式：
    /// - 无内环时：[[x,y], ...]（向后兼容）
    /// - 有内环时：{ "shell": [[x,y], ...], "holes": [[[x,y], ...], ...] }
    /// </summary>
    public class Polygon2DConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Polygon2D);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;

            var token = JToken.Load(reader);

            // 完整格式：{ "shell": ..., "holes": ... }
            if (token.Type == JTokenType.Object)
            {
                var obj = (JObject)token;
                var shellToken = obj["shell"];
                var holesToken = obj["holes"];

                if (shellToken == null)
                    throw new JsonException("Polygon2D object format requires 'shell' property");

                var shell = ParseRing(shellToken);
                var holes = ParseHoles(holesToken);

                return new Polygon2D(shell, holes);
            }
            // 简单格式：[[x,y], ...]
            else if (token.Type == JTokenType.Array)
            {
                var shell = ParseRing(token);
                return new Polygon2D(shell);
            }
            else
            {
                throw new JsonException($"Unexpected token type for Polygon2D: {token.Type}");
            }
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var polygon = (Polygon2D)value;

            // 有内环时使用完整格式
            if (polygon.HasHoles)
            {
                writer.WriteStartObject();

                writer.WritePropertyName("shell");
                WriteRing(writer, polygon.Vertices);

                writer.WritePropertyName("holes");
                writer.WriteStartArray();
                foreach (var hole in polygon.Holes)
                {
                    WriteRing(writer, hole);
                }
                writer.WriteEndArray();

                writer.WriteEndObject();
            }
            // 无内环时使用简单格式（向后兼容）
            else
            {
                WriteRing(writer, polygon.Vertices);
            }
        }

        /// <summary>
        /// 解析单个环（外环或内环）
        /// </summary>
        private static Point2D[] ParseRing(JToken token)
        {
            if (token == null || token.Type != JTokenType.Array)
                return Array.Empty<Point2D>();

            var array = (JArray)token;
            var vertices = new List<Point2D>();

            foreach (var item in array)
            {
                if (item.Type != JTokenType.Array || ((JArray)item).Count != 2)
                    throw new JsonException("Each vertex must be an array of 2 numbers");

                var point = (JArray)item;
                vertices.Add(new Point2D(point[0].Value<double>(), point[1].Value<double>()));
            }

            return vertices.ToArray();
        }

        /// <summary>
        /// 解析内环数组
        /// </summary>
        private static Point2D[][] ParseHoles(JToken token)
        {
            if (token == null || token.Type != JTokenType.Array)
                return Array.Empty<Point2D[]>();

            var array = (JArray)token;
            var holes = new List<Point2D[]>();

            foreach (var holeToken in array)
            {
                holes.Add(ParseRing(holeToken));
            }

            return holes.ToArray();
        }

        /// <summary>
        /// 写入单个环
        /// </summary>
        private static void WriteRing(JsonWriter writer, Point2D[] vertices)
        {
            writer.WriteStartArray();
            foreach (var vertex in vertices)
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
