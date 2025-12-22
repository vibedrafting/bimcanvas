using System;
using Newtonsoft.Json;
using BIMCanvas.Core.Converters.Json;

namespace BIMCanvas.Core.Models.Geometry
{
    /// <summary>
    /// 二维向量（相对偏移），JSON 格式：[dx, dy]
    /// </summary>
    [JsonConverter(typeof(Vec2DConverter))]
    public readonly struct Vec2D
    {
        public double X { get; }
        public double Y { get; }

        public Vec2D(double x, double y)
        {
            X = x;
            Y = y;
        }

        /// <summary>
        /// 向量长度
        /// </summary>
        public double Length => Math.Sqrt(X * X + Y * Y);

        /// <summary>
        /// 返回单位向量
        /// </summary>
        public Vec2D Normalize()
        {
            var len = Length;
            if (len < 1e-10)
                return new Vec2D(0, 0);
            return new Vec2D(X / len, Y / len);
        }

        // 运算符重载
        public static Vec2D operator +(Vec2D a, Vec2D b) => new Vec2D(a.X + b.X, a.Y + b.Y);
        public static Vec2D operator -(Vec2D a, Vec2D b) => new Vec2D(a.X - b.X, a.Y - b.Y);
        public static Vec2D operator *(Vec2D v, double scalar) => new Vec2D(v.X * scalar, v.Y * scalar);
        public static Vec2D operator *(double scalar, Vec2D v) => new Vec2D(v.X * scalar, v.Y * scalar);
        public static Vec2D operator /(Vec2D v, double scalar) => new Vec2D(v.X / scalar, v.Y / scalar);
        public static Vec2D operator -(Vec2D v) => new Vec2D(-v.X, -v.Y);

        /// <summary>
        /// 点积
        /// </summary>
        public static double Dot(Vec2D a, Vec2D b) => a.X * b.X + a.Y * b.Y;

        /// <summary>
        /// 叉积（返回 Z 分量）
        /// </summary>
        public static double Cross(Vec2D a, Vec2D b) => a.X * b.Y - a.Y * b.X;

        public override string ToString() => $"[{X}, {Y}]";
    }
}
