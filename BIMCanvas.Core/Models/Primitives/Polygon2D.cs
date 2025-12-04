using System;
using System.Linq;

namespace BIMCanvas.Core.Models.Primitives
{
    /// <summary>
    /// 二维多边形，JSON 格式：[[x,y], ...]（隐式闭合）
    /// </summary>
    public class Polygon2D
    {
        public Point2D[] Vertices { get; }

        public Polygon2D(Point2D[] vertices)
        {
            Vertices = vertices ?? throw new ArgumentNullException(nameof(vertices));
        }

        /// <summary>
        /// 计算轴对齐包围盒
        /// </summary>
        public AABB ComputeAABB()
        {
            if (Vertices.Length == 0)
                return new AABB(0, 0, 0, 0);

            var minX = Vertices.Min(v => v.X);
            var maxX = Vertices.Max(v => v.X);
            var minY = Vertices.Min(v => v.Y);
            var maxY = Vertices.Max(v => v.Y);

            return new AABB(minX, minY, maxX, maxY);
        }

        /// <summary>
        /// 计算多边形中心点（顶点平均）
        /// </summary>
        public Point2D ComputeCenter()
        {
            if (Vertices.Length == 0)
                return new Point2D(0, 0);

            var sumX = Vertices.Sum(v => v.X);
            var sumY = Vertices.Sum(v => v.Y);
            return new Point2D(sumX / Vertices.Length, sumY / Vertices.Length);
        }

        /// <summary>
        /// 顶点数量
        /// </summary>
        public int VertexCount => Vertices.Length;

        public override string ToString()
        {
            var points = string.Join(", ", Vertices.Select(v => v.ToString()));
            return $"[{points}]";
        }
    }
}
