using Newtonsoft.Json;
using BIMCanvas.Core.Converters.Json;

namespace BIMCanvas.Core.Models.Primitives
{
    /// <summary>
    /// 二维坐标点（绝对位置），JSON 格式：[x, y]
    /// </summary>
    [JsonConverter(typeof(Point2DConverter))]
    public readonly struct Point2D
    {
        public double X { get; }
        public double Y { get; }

        public Point2D(double x, double y)
        {
            X = x;
            Y = y;
        }

        // 运算符重载
        public static Point2D operator +(Point2D p, Vec2D v) => new Point2D(p.X + v.X, p.Y + v.Y);
        public static Point2D operator -(Point2D p, Vec2D v) => new Point2D(p.X - v.X, p.Y - v.Y);
        public static Vec2D operator -(Point2D a, Point2D b) => new Vec2D(a.X - b.X, a.Y - b.Y);

        public override string ToString() => $"[{X}, {Y}]";
    }
}
