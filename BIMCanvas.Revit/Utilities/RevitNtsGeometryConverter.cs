using Autodesk.Revit.DB;
using NetTopologySuite.Geometries;
using NetTopologySuite.Mathematics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LineSegment = NetTopologySuite.Geometries.LineSegment;
using BIMCanvas.Core.Algorithms.Geometries;

namespace BIMCanvas.Revit.Utilities
{
    public static class RevitNtsGeometryConverter
    {
        /// <summary>
        /// 转XYZ
        /// </summary>
        /// <param name="coord"></param>
        /// <param name="z"></param>
        /// <returns></returns>
        public static XYZ ToXYZ(this Coordinate coord, double z = 0)
        {
            return new XYZ(coord.X, coord.Y, z);
        }

        /// <summary>
        /// 转Line
        /// </summary>
        /// <param name="lineSegment">原二维线</param>
        /// <param name="z">Line的Z</param>
        /// <returns>生成的Line</returns>
        public static Line ToLine(this LineSegment lineSegment, double z = 0)
        {
            XYZ p0 = new XYZ(lineSegment.P0.X, lineSegment.P0.Y, z);
            XYZ p1 = new XYZ(lineSegment.P1.X, lineSegment.P1.Y, z);
            Line line = Line.CreateBound(p0, p1);
            return line;
        }

        /// <summary>
        /// 转Coordinate
        /// </summary>
        /// <param name="point"></param>
        /// <returns></returns>
        public static Coordinate ToCoordinate(this XYZ point)
        {
            if (point == null)
            {
                point = XYZ.Zero;
            }
            Coordinate coordinate = new Coordinate(point.X, point.Y);
            return coordinate;
        }

        /// <summary>
        /// 转Vector2D
        /// </summary>
        /// <param name="xyz"></param>
        /// <returns></returns>
        public static Vector2D ToVector2D(this XYZ xyz)
        {
            return new Vector2D(xyz.X, xyz.Y);
        }

        /// <summary>
        /// 转LineSegment
        /// </summary>
        /// <param name="line"></param>
        /// <returns></returns>
        public static LineSegment ToLineSegment(this Line line)
        {
            Coordinate p0 = new Coordinate(line.GetEndPoint(0).X, line.GetEndPoint(0).Y);
            Coordinate p1 = new Coordinate(line.GetEndPoint(1).X, line.GetEndPoint(1).Y);

            LineSegment lineSegment = new LineSegment(p0, p1);

            return lineSegment;
        }

        /// <summary>
        /// 将PlanarFace的所有边界CurveLoop转换为NTS Polygon（外环+所有洞口）。
        /// 外环为第一个，后续为内环（洞口）。<br/>
        /// 【假设CurveLoop全部由Line组成，否则抛出异常】
        /// </summary>
        /// <param name="planarFace">Revit的PlanarFace</param>
        /// <param name="autoIntersection">是否自动补全交点</param>
        /// <returns>所有环的Polygon集合（外环在前，依次为洞口）</returns>
        /// <exception cref="System.ArgumentNullException"/>
        /// <exception cref="System.InvalidOperationException"/>
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
                var segments = new List<NetTopologySuite.Geometries.LineSegment>();
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

        /// <summary>
        /// 将PlanarFace的所有边界CurveLoop转换为NTS Polygon（外环+所有洞口）。
        /// 外环为第一个，后续为内环（洞口）。<br/>
        /// 【假设CurveLoop全部由Line组成，否则抛出异常】
        /// </summary>
        /// <param name="curveLoop">Revit的CurveLoop</param>
        /// <param name="autoIntersection">是否自动补全交点</param>
        /// <returns>所有环的Polygon集合（外环在前，依次为洞口）</returns>
        /// <exception cref="System.ArgumentNullException"/>
        /// <exception cref="System.InvalidOperationException"/>
        public static Polygon ToPolygon(this CurveLoop curveLoop, bool autoIntersection = false)
        {
            if (curveLoop == null)
                return null;
            var segments = new List<NetTopologySuite.Geometries.LineSegment>();
            foreach (Curve curve in curveLoop)
            {
                if (curve is Line line)
                    segments.Add(line.ToLineSegment());
                else
                    return null;
            }

            return segments.GeneratePolygon(autoIntersection);
        }

    }
}
