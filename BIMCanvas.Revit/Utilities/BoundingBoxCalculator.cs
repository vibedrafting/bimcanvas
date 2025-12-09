using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using NetTopologySuite.Geometries;

namespace BIMCanvas.Revit.Utilities
{
    /// <summary>
    /// 包围盒计算工具
    /// </summary>
    public static class BoundingBoxCalculator
    {
        /// <summary>
        /// 计算所有轮廓的包围盒左下角（作为坐标原点）- NTS Polygon 版本
        /// </summary>
        /// <param name="polygons">NTS Polygon 列表（feet 单位）</param>
        /// <returns>包围盒左下角坐标（Revit 项目坐标系，feet）</returns>
        public static XYZ CalculateOriginFromPolygons(List<Polygon> polygons)
        {
            if (polygons == null || polygons.Count == 0)
                return XYZ.Zero;

            var envelope = new Envelope();
            foreach (var polygon in polygons)
            {
                envelope.ExpandToInclude(polygon.EnvelopeInternal);
            }

            return new XYZ(envelope.MinX, envelope.MinY, 0);
        }
    }
}
