using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using BIMCanvas.Core.Converters;
using BIMCanvas.Core.Models.Primitives;
using NetTopologySuite.Geometries;

namespace BIMCanvas.Revit.Services
{
    /// <summary>
    /// 坐标系转换器
    /// 负责 Revit/NTS (feet, 项目坐标系) 与 BIMCanvas Point2D (mm, 归一化坐标系) 之间的转换
    ///
    /// 转换链路：
    /// - Revit XYZ (feet) → Point2D (mm)
    /// - NTS Coordinate (feet) → Point2D (mm)
    /// - NTS Polygon (feet) → Polygon2D (mm)
    /// - NTS LineSegment (feet) → Line2D (mm)
    /// </summary>
    public class CoordinateTransformer
    {
        private readonly Coordinate _origin;
        private readonly double _rotation;

        /// <summary>
        /// 创建坐标转换器
        /// </summary>
        /// <param name="origin">原点位置（NTS Coordinate，Revit 项目坐标系，feet）</param>
        /// <param name="rotation">视图旋转角度（弧度）</param>
        public CoordinateTransformer(Coordinate origin, double rotation)
        {
            _origin = origin ?? throw new ArgumentNullException(nameof(origin));
            _rotation = rotation;
        }

        #region 基础点转换

        /// <summary>
        /// 将 Revit XYZ 转换为 BIMCanvas Point2D
        /// </summary>
        public Point2D ToPoint2D(XYZ revitPoint)
        {
            if (revitPoint == null)
                throw new ArgumentNullException(nameof(revitPoint));

            return TransformToPoint2D(revitPoint.X, revitPoint.Y);
        }

        /// <summary>
        /// 将 NTS Coordinate (feet) 转换为 BIMCanvas Point2D (mm)
        /// </summary>
        public Point2D ToPoint2D(Coordinate coord)
        {
            if (coord == null)
                throw new ArgumentNullException(nameof(coord));

            return TransformToPoint2D(coord.X, coord.Y);
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
        /// 核心坐标变换方法：将 (x, y) feet 坐标转换为 Point2D (mm)
        /// </summary>
        private Point2D TransformToPoint2D(double x, double y)
        {
            // 1. 计算相对于原点的偏移
            var dx = x - _origin.X;
            var dy = y - _origin.Y;

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

        #endregion

        #region NTS 几何转换

        /// <summary>
        /// 将 NTS Polygon (feet) 转换为 BIMCanvas Polygon2D (mm)
        /// 支持内环（孔洞）
        /// </summary>
        public Polygon2D ToPolygon2D(Polygon ntsPolygon)
        {
            if (ntsPolygon == null)
                throw new ArgumentNullException(nameof(ntsPolygon));

            // 转换外环
            var shell = ConvertRingToPoints(ntsPolygon.Shell);

            // 转换内环
            Point2D[][] holes = null;
            if (ntsPolygon.NumInteriorRings > 0)
            {
                var holesList = new List<Point2D[]>();
                for (int i = 0; i < ntsPolygon.NumInteriorRings; i++)
                {
                    var holePoints = ConvertRingToPoints(ntsPolygon.GetInteriorRingN(i));
                    if (holePoints.Length >= 3)
                        holesList.Add(holePoints);
                }
                holes = holesList.ToArray();
            }

            return new Polygon2D(shell, holes);
        }

        /// <summary>
        /// 将 NTS LineSegment (feet) 转换为 BIMCanvas Line2D (mm)
        /// </summary>
        public Line2D ToLine2D(NetTopologySuite.Geometries.LineSegment segment)
        {
            if (segment == null)
                throw new ArgumentNullException(nameof(segment));

            return new Line2D(
                ToPoint2D(segment.P0),
                ToPoint2D(segment.P1)
            );
        }

        /// <summary>
        /// 将 NTS LinearRing 转换为 Point2D 数组（去除闭合点，去重）
        /// </summary>
        private Point2D[] ConvertRingToPoints(LineString ring)
        {
            var points = new List<Point2D>();
            var coords = ring.Coordinates;

            // 跳过最后一个闭合点
            for (int i = 0; i < coords.Length - 1; i++)
            {
                var pt = ToPoint2D(coords[i]);

                // 去重相邻点（阈值 0.01mm）
                if (points.Count == 0 ||
                    Math.Abs(pt.X - points.Last().X) > 0.01 ||
                    Math.Abs(pt.Y - points.Last().Y) > 0.01)
                {
                    points.Add(pt);
                }
            }

            return points.ToArray();
        }

        #endregion

        #region 属性

        /// <summary>
        /// 获取原点位置（NTS Coordinate，Revit 项目坐标系，feet）
        /// </summary>
        public Coordinate Origin => _origin;

        /// <summary>
        /// 获取旋转角度（弧度）
        /// </summary>
        public double Rotation => _rotation;

        #endregion
    }
}
