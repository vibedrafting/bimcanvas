using Newtonsoft.Json;
using BIMCanvas.Core.Converters.Json;

namespace BIMCanvas.Core.Models.Primitives
{
    /// <summary>
    /// 轴对齐包围盒 (Axis-Aligned Bounding Box)，JSON 格式：[minX, minY, maxX, maxY]
    /// </summary>
    [JsonConverter(typeof(AABBConverter))]
    public readonly struct AABB
    {
        public double MinX { get; }
        public double MinY { get; }
        public double MaxX { get; }
        public double MaxY { get; }

        public AABB(double minX, double minY, double maxX, double maxY)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        /// <summary>
        /// 宽度
        /// </summary>
        public double Width => MaxX - MinX;

        /// <summary>
        /// 高度
        /// </summary>
        public double Height => MaxY - MinY;

        /// <summary>
        /// 中心点
        /// </summary>
        public Point2D Center => new Point2D(
            (MinX + MaxX) / 2,
            (MinY + MaxY) / 2
        );

        /// <summary>
        /// 判断点是否在包围盒内
        /// </summary>
        public bool Contains(Point2D point)
        {
            return point.X >= MinX && point.X <= MaxX &&
                   point.Y >= MinY && point.Y <= MaxY;
        }

        /// <summary>
        /// 判断两个包围盒是否相交
        /// </summary>
        public bool Intersects(AABB other)
        {
            return !(MaxX < other.MinX || MinX > other.MaxX ||
                     MaxY < other.MinY || MinY > other.MaxY);
        }

        /// <summary>
        /// 从两个点创建包围盒
        /// </summary>
        public static AABB FromPoints(Point2D p1, Point2D p2)
        {
            var minX = p1.X < p2.X ? p1.X : p2.X;
            var maxX = p1.X > p2.X ? p1.X : p2.X;
            var minY = p1.Y < p2.Y ? p1.Y : p2.Y;
            var maxY = p1.Y > p2.Y ? p1.Y : p2.Y;
            return new AABB(minX, minY, maxX, maxY);
        }

        public override string ToString() => $"[{MinX}, {MinY}, {MaxX}, {MaxY}]";
    }
}
