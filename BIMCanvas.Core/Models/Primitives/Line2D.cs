using System;

namespace BIMCanvas.Core.Models.Primitives
{
    /// <summary>
    /// 二维线段，JSON 格式：[[x1,y1], [x2,y2]]
    /// </summary>
    public class Line2D
    {
        public Point2D Start { get; set; }
        public Point2D End { get; set; }

        public Line2D(Point2D start, Point2D end)
        {
            Start = start;
            End = end;
        }

        /// <summary>
        /// 默认构造函数（用于反序列化）
        /// </summary>
        public Line2D() : this(new Point2D(0, 0), new Point2D(0, 0))
        {
        }

        /// <summary>
        /// 线段长度
        /// </summary>
        public double Length
        {
            get
            {
                var dx = End.X - Start.X;
                var dy = End.Y - Start.Y;
                return Math.Sqrt(dx * dx + dy * dy);
            }
        }

        /// <summary>
        /// 线段中点
        /// </summary>
        public Point2D Midpoint => new Point2D(
            (Start.X + End.X) / 2,
            (Start.Y + End.Y) / 2
        );

        /// <summary>
        /// 线段方向向量（单位向量）
        /// </summary>
        public Vec2D Direction => (End - Start).Normalize();

        public override string ToString() => $"[{Start}, {End}]";
    }
}
