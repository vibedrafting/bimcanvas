using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using BIMCanvas.Core.Converters;
using BIMCanvas.Core.Models.Primitives;

namespace BIMCanvas.Revit.Services
{
    /// <summary>
    /// 坐标系转换器
    /// 负责 Revit XYZ (feet, 项目坐标系) 与 BIMCanvas Point2D (mm, 归一化坐标系) 之间的转换
    /// </summary>
    public class CoordinateTransformer
    {
        private readonly XYZ _origin;
        private readonly double _rotation;

        /// <summary>
        /// 创建坐标转换器
        /// </summary>
        /// <param name="origin">原点位置（Revit 项目坐标系，feet）</param>
        /// <param name="rotation">视图旋转角度（弧度）</param>
        public CoordinateTransformer(XYZ origin, double rotation)
        {
            _origin = origin ?? throw new ArgumentNullException(nameof(origin));
            _rotation = rotation;
        }

        /// <summary>
        /// 将 Revit XYZ 转换为 BIMCanvas Point2D
        /// </summary>
        public Point2D ToPoint2D(XYZ revitPoint)
        {
            if (revitPoint == null)
                throw new ArgumentNullException(nameof(revitPoint));

            // 1. 计算相对于原点的偏移
            var dx = revitPoint.X - _origin.X;
            var dy = revitPoint.Y - _origin.Y;

            // 2. 应用视图旋转归一化（反向旋转）
            double localX, localY;
            if (Math.Abs(_rotation) > 1e-6)
            {
                var cosR = Math.Cos(-_rotation);
                var sinR = Math.Sin(-_rotation);
                localX = dx * cosR - dy * sinR;
                localY = dx * sinR + dy * cosR;
            }
            else
            {
                localX = dx;
                localY = dy;
            }

            // 3. 单位转换：feet → mm
            return new Point2D(
                UnitConverter.ToMillimeters(localX),
                UnitConverter.ToMillimeters(localY)
            );
        }

        /// <summary>
        /// 将 BIMCanvas Point2D 转换为 Revit XYZ
        /// </summary>
        public XYZ ToXYZ(Point2D point, double elevation = 0)
        {
            // 1. 单位转换：mm → feet
            var localX = UnitConverter.ToFeet(point.X);
            var localY = UnitConverter.ToFeet(point.Y);

            // 2. 正向旋转
            double dx, dy;
            if (Math.Abs(_rotation) > 1e-6)
            {
                var cosR = Math.Cos(_rotation);
                var sinR = Math.Sin(_rotation);
                dx = localX * cosR - localY * sinR;
                dy = localX * sinR + localY * cosR;
            }
            else
            {
                dx = localX;
                dy = localY;
            }

            // 3. 加上原点偏移
            return new XYZ(
                _origin.X + dx,
                _origin.Y + dy,
                elevation
            );
        }

        /// <summary>
        /// 将 Revit CurveLoop 转换为 BIMCanvas Polygon2D
        /// </summary>
        public Polygon2D ToPolygon2D(CurveLoop loop)
        {
            if (loop == null)
                throw new ArgumentNullException(nameof(loop));

            var points = new List<Point2D>();

            foreach (Curve curve in loop)
            {
                var tessellated = curve.Tessellate();
                foreach (XYZ xyz in tessellated)
                {
                    var pt = ToPoint2D(xyz);

                    // 去重相邻点（阈值 0.01mm）
                    if (points.Count == 0 ||
                        Math.Abs(pt.X - points.Last().X) > 0.01 ||
                        Math.Abs(pt.Y - points.Last().Y) > 0.01)
                    {
                        points.Add(pt);
                    }
                }
            }

            // 移除闭合点（Polygon2D 隐式闭合）
            if (points.Count > 0)
            {
                var first = points[0];
                var last = points[points.Count - 1];
                if (Math.Abs(first.X - last.X) < 0.01 &&
                    Math.Abs(first.Y - last.Y) < 0.01)
                {
                    points.RemoveAt(points.Count - 1);
                }
            }

            return new Polygon2D(points.ToArray());
        }

        /// <summary>
        /// 将 Revit Line 转换为 BIMCanvas Line2D
        /// </summary>
        public Line2D ToLine2D(Line line)
        {
            if (line == null)
                throw new ArgumentNullException(nameof(line));

            return new Line2D
            {
                Start = ToPoint2D(line.GetEndPoint(0)),
                End = ToPoint2D(line.GetEndPoint(1))
            };
        }

        /// <summary>
        /// 将 NTS LineSegment (feet) 转换为 BIMCanvas Line2D (mm)
        /// </summary>
        /// <param name="segment">NTS LineSegment（英尺单位）</param>
        /// <returns>Line2D（毫米单位）</returns>
        public Line2D ToLine2D(NetTopologySuite.Geometries.LineSegment segment)
        {
            if (segment == null)
                throw new ArgumentNullException(nameof(segment));

            var startXYZ = new XYZ(segment.P0.X, segment.P0.Y, 0);
            var endXYZ = new XYZ(segment.P1.X, segment.P1.Y, 0);

            return new Line2D
            {
                Start = ToPoint2D(startXYZ),
                End = ToPoint2D(endXYZ)
            };
        }

        /// <summary>
        /// 将 NTS Polygon (feet, Revit 项目坐标系) 转换为 BIMCanvas Polygon2D (mm, 归一化坐标系)
        /// </summary>
        public Polygon2D ToPolygon2D(NetTopologySuite.Geometries.Polygon ntsPolygon)
        {
            if (ntsPolygon == null)
                throw new ArgumentNullException(nameof(ntsPolygon));

            var points = new List<Point2D>();

            // 转换外环顶点
            var shell = ntsPolygon.Shell;
            for (int i = 0; i < shell.NumPoints - 1; i++) // -1 跳过闭合点
            {
                var coord = shell.Coordinates[i];
                var revitPoint = new XYZ(coord.X, coord.Y, 0); // NTS 坐标已经是 feet 单位
                var pt = ToPoint2D(revitPoint);

                // 去重相邻点（阈值 0.01mm）
                if (points.Count == 0 ||
                    Math.Abs(pt.X - points.Last().X) > 0.01 ||
                    Math.Abs(pt.Y - points.Last().Y) > 0.01)
                {
                    points.Add(pt);
                }
            }

            return new Polygon2D(points.ToArray());
        }

        /// <summary>
        /// 获取原点位置（Revit 项目坐标系，feet）
        /// </summary>
        public XYZ Origin => _origin;

        /// <summary>
        /// 获取旋转角度（弧度）
        /// </summary>
        public double Rotation => _rotation;
    }
}
