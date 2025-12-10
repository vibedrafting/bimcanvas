using Autodesk.Revit.DB;
using NetTopologySuite.Geometries;
using NetTopologySuite.Mathematics;
using System.Collections.Generic;
using BIMCanvas.Core.Algorithms.Geometries;
using LineSegment = NetTopologySuite.Geometries.LineSegment;

namespace BIMCanvas.Revit.Converters
{
    /// <summary>
    /// Revit 几何对象 ↔ NTS 几何对象 转换器
    /// 仅处理 Revit API ↔ NTS 层的转换，不直接转换到 Core.Models
    /// </summary>
    public static class RevitNtsConverter
    {
        #region Coordinate ↔ XYZ

        /// <summary>
        /// NTS Coordinate → Revit XYZ
        /// </summary>
        public static XYZ ToXYZ(this Coordinate coord, double z = 0)
        {
            return new XYZ(coord.X, coord.Y, z);
        }

        /// <summary>
        /// Revit XYZ → NTS Coordinate
        /// </summary>
        public static Coordinate ToCoordinate(this XYZ point)
        {
            if (point == null)
            {
                point = XYZ.Zero;
            }
            return new Coordinate(point.X, point.Y);
        }

        #endregion

        #region LineSegment ↔ Line

        /// <summary>
        /// NTS LineSegment → Revit Line
        /// </summary>
        public static Line ToLine(this LineSegment lineSegment, double z = 0)
        {
            XYZ p0 = new XYZ(lineSegment.P0.X, lineSegment.P0.Y, z);
            XYZ p1 = new XYZ(lineSegment.P1.X, lineSegment.P1.Y, z);
            return Line.CreateBound(p0, p1);
        }

        /// <summary>
        /// Revit Line → NTS LineSegment
        /// </summary>
        public static LineSegment ToLineSegment(this Line line)
        {
            Coordinate p0 = new Coordinate(line.GetEndPoint(0).X, line.GetEndPoint(0).Y);
            Coordinate p1 = new Coordinate(line.GetEndPoint(1).X, line.GetEndPoint(1).Y);
            return new LineSegment(p0, p1);
        }

        #endregion

        #region Vector2D ↔ XYZ

        /// <summary>
        /// Revit XYZ → NTS Vector2D
        /// </summary>
        public static Vector2D ToVector2D(this XYZ xyz)
        {
            return new Vector2D(xyz.X, xyz.Y);
        }

        #endregion

        #region CurveLoop / PlanarFace → NTS Polygon

        /// <summary>
        /// Revit CurveLoop → NTS Polygon
        /// 假设 CurveLoop 全部由 Line 组成，否则返回 null
        /// </summary>
        public static Polygon ToPolygon(this CurveLoop curveLoop, bool autoIntersection = false)
        {
            if (curveLoop == null)
                return null;

            var segments = new List<LineSegment>();
            foreach (Curve curve in curveLoop)
            {
                if (curve is Line line)
                    segments.Add(line.ToLineSegment());
                else
                    return null;
            }

            return segments.GeneratePolygon(autoIntersection);
        }

        /// <summary>
        /// Revit PlanarFace → NTS Polygon 列表（外环 + 内环）
        /// 返回的列表中，第一个是外环，后续是内环（洞口）
        /// 假设 CurveLoop 全部由 Line 组成，否则返回 null
        /// </summary>
        /// <remarks>
        /// TODO: 返回分离的多边形列表，未组合为带内环的单个 Polygon
        /// 该方法当前未被引用，后续需要时再改造
        /// </remarks>
        public static List<Polygon> ToPolygons(this PlanarFace planarFace, bool autoIntersection = false)
        {
            if (planarFace == null)
                return null;

            var polygons = new List<Polygon>();
            IList<CurveLoop> loops = planarFace.GetEdgesAsCurveLoops();
            if (loops == null || loops.Count == 0)
                return null;

            foreach (var curveLoop in loops)
            {
                var segments = new List<LineSegment>();
                foreach (Curve curve in curveLoop)
                {
                    if (curve is Line line)
                        segments.Add(line.ToLineSegment());
                    else
                        return null;
                }
                polygons.Add(segments.GeneratePolygon(autoIntersection));
            }

            return polygons;
        }

        #endregion
    }
}
