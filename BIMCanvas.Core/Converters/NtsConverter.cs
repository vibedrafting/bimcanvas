using System.Collections.Generic;
using System.Linq;
using BIMCanvas.Core.Models.Geometry;
using NetTopologySuite.Geometries;
using NetTopologySuite.Mathematics;

namespace BIMCanvas.Core.Converters
{
    /// <summary>
    /// NTS 几何对象 ↔ BIMCanvas.Core.Models 几何对象 转换器
    /// 支持内环（孔洞）
    /// </summary>
    public static class NtsConverter
    {
        private static readonly GeometryFactory Factory = new GeometryFactory();

        #region Polygon2D ↔ NTS Polygon

        /// <summary>
        /// Polygon2D → NTS Polygon（支持内环）
        /// </summary>
        public static Polygon ToNtsPolygon(Polygon2D polygon)
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
        public static Polygon2D FromNtsPolygon(Polygon nts)
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

        #endregion

        #region Point2D ↔ NTS Point

        /// <summary>
        /// Point2D → NTS Point
        /// </summary>
        public static Point ToNtsPoint(Point2D point)
        {
            return Factory.CreatePoint(new Coordinate(point.X, point.Y));
        }

        /// <summary>
        /// NTS Point → Point2D
        /// </summary>
        public static Point2D FromNtsPoint(Point nts)
        {
            return new Point2D(nts.X, nts.Y);
        }

        #endregion

        #region Line2D ↔ NTS LineSegment

        /// <summary>
        /// Line2D → NTS LineSegment
        /// </summary>
        public static LineSegment ToNtsLineSegment(Line2D line)
        {
            return new LineSegment(
                new Coordinate(line.Start.X, line.Start.Y),
                new Coordinate(line.End.X, line.End.Y));
        }

        /// <summary>
        /// NTS LineSegment → Line2D
        /// </summary>
        public static Line2D FromNtsLineSegment(LineSegment nts)
        {
            return new Line2D(
                new Point2D(nts.P0.X, nts.P0.Y),
                new Point2D(nts.P1.X, nts.P1.Y));
        }

        #endregion

        #region Vec2D ↔ NTS Vector2D

        /// <summary>
        /// Vec2D → NTS Vector2D
        /// </summary>
        public static Vector2D ToNtsVector2D(Vec2D vec)
        {
            return new Vector2D(vec.X, vec.Y);
        }

        /// <summary>
        /// NTS Vector2D → Vec2D
        /// </summary>
        public static Vec2D FromNtsVector2D(Vector2D nts)
        {
            return new Vec2D(nts.X, nts.Y);
        }

        #endregion
    }
}
