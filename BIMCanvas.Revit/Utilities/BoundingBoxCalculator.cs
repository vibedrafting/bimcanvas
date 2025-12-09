using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace BIMCanvas.Revit.Utilities
{
    /// <summary>
    /// 包围盒计算工具
    /// </summary>
    public static class BoundingBoxCalculator
    {
        /// <summary>
        /// 计算所有轮廓的包围盒左下角（作为坐标原点）
        /// </summary>
        /// <param name="curveLoops">轮廓曲线列表</param>
        /// <returns>包围盒左下角坐标（Revit 项目坐标系，feet）</returns>
        public static XYZ CalculateOrigin(List<CurveLoop> curveLoops)
        {
            if (curveLoops == null || curveLoops.Count == 0)
                return XYZ.Zero;

            double minX = double.MaxValue;
            double minY = double.MaxValue;

            foreach (var loop in curveLoops)
            {
                foreach (Curve curve in loop)
                {
                    var p0 = curve.GetEndPoint(0);
                    var p1 = curve.GetEndPoint(1);
                    minX = Math.Min(minX, Math.Min(p0.X, p1.X));
                    minY = Math.Min(minY, Math.Min(p0.Y, p1.Y));
                }
            }

            return new XYZ(minX, minY, 0);
        }

        /// <summary>
        /// 计算所有轮廓的包围盒左下角（作为坐标原点）- NTS Polygon 版本
        /// </summary>
        /// <param name="polygons">NTS Polygon 列表（feet 单位）</param>
        /// <returns>包围盒左下角坐标（Revit 项目坐标系，feet）</returns>
        public static XYZ CalculateOriginFromPolygons(List<NetTopologySuite.Geometries.Polygon> polygons)
        {
            if (polygons == null || polygons.Count == 0)
                return XYZ.Zero;

            double minX = double.MaxValue;
            double minY = double.MaxValue;

            foreach (var polygon in polygons)
            {
                foreach (var coord in polygon.Coordinates)
                {
                    minX = Math.Min(minX, coord.X);
                    minY = Math.Min(minY, coord.Y);
                }
            }

            return new XYZ(minX, minY, 0);
        }
    }
}
