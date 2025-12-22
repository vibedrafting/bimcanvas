using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using BIMCanvas.Core.Converters.Json;

namespace BIMCanvas.Core.Models.Geometry
{
    /// <summary>
    /// 二维多边形，支持一个外环和多个内环（孔洞）
    /// JSON 格式：
    /// - 简单格式（无内环）：[[x,y], ...]
    /// - 完整格式（有内环）：{ "shell": [[x,y], ...], "holes": [[[x,y], ...], ...] }
    /// </summary>
    [JsonConverter(typeof(Polygon2DConverter))]
    public class Polygon2D
    {
        /// <summary>
        /// 外环顶点（隐式闭合）
        /// </summary>
        public Point2D[] Vertices { get; }

        /// <summary>
        /// 内环（孔洞）列表，每个内环是隐式闭合的顶点数组
        /// </summary>
        public Point2D[][] Holes { get; }

        /// <summary>
        /// 是否有内环
        /// </summary>
        public bool HasHoles => Holes != null && Holes.Length > 0;

        /// <summary>
        /// 创建简单多边形（无内环）
        /// </summary>
        public Polygon2D(Point2D[] vertices)
            : this(vertices, null)
        {
        }

        /// <summary>
        /// 创建带内环的多边形
        /// </summary>
        public Polygon2D(Point2D[] vertices, Point2D[][] holes)
        {
            Vertices = vertices ?? throw new ArgumentNullException(nameof(vertices));
            Holes = holes ?? Array.Empty<Point2D[]>();
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
