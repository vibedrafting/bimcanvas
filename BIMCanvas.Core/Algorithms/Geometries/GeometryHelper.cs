using System;
using BIMCanvas.Core.Models.Geometry;

namespace BIMCanvas.Core.Algorithms.Geometries
{
    /// <summary>
    /// 几何运算辅助类
    /// </summary>
    public static class GeometryHelper
    {
        /// <summary>
        /// 计算多边形的 AABB
        /// </summary>
        public static AABB ComputeAABB(Polygon2D polygon)
        {
            return polygon.ComputeAABB();
        }

        /// <summary>
        /// 计算多边形的中心点
        /// </summary>
        public static Point2D ComputeCenter(Polygon2D polygon)
        {
            return polygon.ComputeCenter();
        }

        /// <summary>
        /// 旋转多边形
        /// </summary>
        /// <param name="polygon">原始多边形</param>
        /// <param name="angleRadians">旋转角度（弧度），逆时针为正</param>
        /// <param name="center">旋转中心</param>
        /// <returns>旋转后的新多边形</returns>
        public static Polygon2D RotatePolygon(Polygon2D polygon, double angleRadians, Point2D center)
        {
            var cos = Math.Cos(angleRadians);
            var sin = Math.Sin(angleRadians);

            var newVertices = new Point2D[polygon.Vertices.Length];
            for (int i = 0; i < polygon.Vertices.Length; i++)
            {
                var v = polygon.Vertices[i];
                // 平移到原点
                var dx = v.X - center.X;
                var dy = v.Y - center.Y;
                // 旋转
                var rx = dx * cos - dy * sin;
                var ry = dx * sin + dy * cos;
                // 平移回原位置
                newVertices[i] = new Point2D(rx + center.X, ry + center.Y);
            }

            return new Polygon2D(newVertices);
        }

        /// <summary>
        /// 创建矩形多边形
        /// </summary>
        /// <param name="center">矩形中心</param>
        /// <param name="width">宽度（X方向）</param>
        /// <param name="height">高度（Y方向）</param>
        /// <returns>矩形多边形（4顶点，逆时针）</returns>
        public static Polygon2D CreateRectangle(Point2D center, double width, double height)
        {
            var halfW = width / 2;
            var halfH = height / 2;

            // 逆时针顺序：左下 → 右下 → 右上 → 左上
            var vertices = new[]
            {
                new Point2D(center.X - halfW, center.Y - halfH),
                new Point2D(center.X + halfW, center.Y - halfH),
                new Point2D(center.X + halfW, center.Y + halfH),
                new Point2D(center.X - halfW, center.Y + halfH)
            };

            return new Polygon2D(vertices);
        }

        /// <summary>
        /// 计算两点间距离
        /// </summary>
        public static double Distance(Point2D a, Point2D b)
        {
            return (b - a).Length;
        }

        /// <summary>
        /// 计算点到线段的最短距离
        /// </summary>
        public static double DistanceToLine(Point2D point, Line2D line)
        {
            var lineVec = line.End - line.Start;
            var pointVec = point - line.Start;
            var lineLen = lineVec.Length;

            if (lineLen < 1e-10)
                return (point - line.Start).Length;

            // 投影参数 t
            var t = (pointVec.X * lineVec.X + pointVec.Y * lineVec.Y) / (lineLen * lineLen);
            t = Math.Max(0, Math.Min(1, t)); // 限制在 [0, 1]

            // 最近点
            var closest = new Point2D(
                line.Start.X + t * lineVec.X,
                line.Start.Y + t * lineVec.Y
            );

            return (point - closest).Length;
        }

        /// <summary>
        /// 根据定位线和厚度计算禁区轮廓
        /// </summary>
        /// <param name="locationLine">定位线（靠墙侧，方向顺房间）</param>
        /// <param name="thickness">完成面厚度（mm）</param>
        /// <returns>禁区轮廓（4顶点矩形）</returns>
        public static Polygon2D ComputeExclusionBoundary(Line2D locationLine, double thickness)
        {
            // 计算线段方向向量
            var direction = (locationLine.End - locationLine.Start).Normalize();
            // 计算法向量（向内偏移方向，逆时针旋转90度）
            var normal = new Vec2D(-direction.Y, direction.X);

            // 计算4个顶点
            var p1 = locationLine.Start;
            var p2 = locationLine.End;
            var p3 = new Point2D(p2.X + normal.X * thickness, p2.Y + normal.Y * thickness);
            var p4 = new Point2D(p1.X + normal.X * thickness, p1.Y + normal.Y * thickness);

            return new Polygon2D(new[] { p1, p2, p3, p4 });
        }
    }
}
