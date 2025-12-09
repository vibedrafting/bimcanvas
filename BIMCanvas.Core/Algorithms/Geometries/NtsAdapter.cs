using System.Linq;
using BIMCanvas.Core.Models.Primitives;
using NetTopologySuite.Geometries;

namespace BIMCanvas.Core.Algorithms.Geometries
{
    /// <summary>
    /// Polygon2D ↔ NTS Polygon 转换（内部使用）
    /// </summary>
    internal static class NtsAdapter
    {
        private static readonly GeometryFactory Factory = new GeometryFactory();

        /// <summary>
        /// Polygon2D → NTS Polygon
        /// </summary>
        internal static Polygon ToNtsPolygon(Polygon2D polygon)
        {
            var coords = polygon.Vertices
                .Select(p => new Coordinate(p.X, p.Y))
                .ToList();
            // NTS 需要闭合多边形
            coords.Add(coords[0]);
            var ring = Factory.CreateLinearRing(coords.ToArray());
            return Factory.CreatePolygon(ring);
        }

        /// <summary>
        /// NTS Polygon → Polygon2D
        /// </summary>
        internal static Polygon2D FromNtsPolygon(Polygon nts)
        {
            var coords = nts.ExteriorRing.Coordinates;
            // 移除最后一个重复的闭合点
            var vertices = coords
                .Take(coords.Length - 1)
                .Select(c => new Point2D(c.X, c.Y))
                .ToArray();
            return new Polygon2D(vertices);
        }

        /// <summary>
        /// Point2D → NTS Point
        /// </summary>
        internal static Point ToNtsPoint(Point2D point)
        {
            return Factory.CreatePoint(new Coordinate(point.X, point.Y));
        }

        /// <summary>
        /// NTS Point → Point2D
        /// </summary>
        internal static Point2D FromNtsPoint(Point nts)
        {
            return new Point2D(nts.X, nts.Y);
        }
    }
}
