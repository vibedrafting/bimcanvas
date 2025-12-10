using System.Collections.Generic;
using System.Linq;
using BIMCanvas.Core.Models.Primitives;
using NetTopologySuite.Geometries;

namespace BIMCanvas.Core.Algorithms.Geometries
{
    /// <summary>
    /// Polygon2D ↔ NTS Polygon 转换（内部使用），支持内环
    /// </summary>
    internal static class NtsAdapter
    {
        private static readonly GeometryFactory Factory = new GeometryFactory();

        /// <summary>
        /// Polygon2D → NTS Polygon（支持内环）
        /// </summary>
        internal static Polygon ToNtsPolygon(Polygon2D polygon)
        {
            // 创建外环
            var shellCoords = polygon.Vertices
                .Select(p => new Coordinate(p.X, p.Y))
                .ToList();
            // NTS 需要闭合多边形
            shellCoords.Add(shellCoords[0]);
            var shell = Factory.CreateLinearRing(shellCoords.ToArray());

            // 创建内环
            LinearRing[] holes = null;
            if (polygon.HasHoles)
            {
                holes = polygon.Holes.Select(hole =>
                {
                    var holeCoords = hole
                        .Select(p => new Coordinate(p.X, p.Y))
                        .ToList();
                    holeCoords.Add(holeCoords[0]);
                    return Factory.CreateLinearRing(holeCoords.ToArray());
                }).ToArray();
            }

            return Factory.CreatePolygon(shell, holes);
        }

        /// <summary>
        /// NTS Polygon → Polygon2D（支持内环）
        /// </summary>
        internal static Polygon2D FromNtsPolygon(Polygon nts)
        {
            // 提取外环（移除最后一个重复的闭合点）
            var shellCoords = nts.ExteriorRing.Coordinates;
            var vertices = shellCoords
                .Take(shellCoords.Length - 1)
                .Select(c => new Point2D(c.X, c.Y))
                .ToArray();

            // 提取内环
            Point2D[][] holes = null;
            if (nts.NumInteriorRings > 0)
            {
                var holesList = new List<Point2D[]>();
                for (int i = 0; i < nts.NumInteriorRings; i++)
                {
                    var holeCoords = nts.GetInteriorRingN(i).Coordinates;
                    var holeVertices = holeCoords
                        .Take(holeCoords.Length - 1)
                        .Select(c => new Point2D(c.X, c.Y))
                        .ToArray();
                    holesList.Add(holeVertices);
                }
                holes = holesList.ToArray();
            }

            return new Polygon2D(vertices, holes);
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
