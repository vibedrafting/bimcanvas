using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using BIMCanvas.Core.Converters;
using NetTopologySuite.Geometries;

namespace BIMCanvas.Revit.Services
{
    /// <summary>
    /// 坐标系转换器
    /// 负责 Revit/NTS (feet, 项目坐标系) 与 NTS (mm, 归一化坐标系) 之间的坐标变换
    ///
    /// 职责：
    /// - 坐标变换（原点偏移 + 旋转）
    /// - 单位转换（feet → mm）
    /// - 输出变换后的 NTS 几何对象
    ///
    /// 不负责：
    /// - NTS → Core.Models 类型转换（由 BIMCanvas.Core.Converters.NtsConverter 负责）
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

        #region 坐标变换方法

        /// <summary>
        /// 将 Revit XYZ (feet) 变换为 NTS Coordinate (mm)
        /// </summary>
        public Coordinate TransformXYZ(XYZ revitPoint)
        {
            if (revitPoint == null)
                throw new ArgumentNullException(nameof(revitPoint));

            return TransformToCoordinate(revitPoint.X, revitPoint.Y);
        }

        /// <summary>
        /// 将 NTS Coordinate (feet) 变换为 NTS Coordinate (mm)
        /// </summary>
        public Coordinate TransformCoordinate(Coordinate coord)
        {
            if (coord == null)
                throw new ArgumentNullException(nameof(coord));

            return TransformToCoordinate(coord.X, coord.Y);
        }

        /// <summary>
        /// 将 NTS Coordinate (mm) 逆变换为 Revit XYZ (feet)
        /// </summary>
        public XYZ ToXYZ(Coordinate coord, double elevation = 0)
        {
            if (coord == null)
                throw new ArgumentNullException(nameof(coord));

            // 1. 单位转换：mm → feet
            var localX = UnitConverter.ToFeet(coord.X);
            var localY = UnitConverter.ToFeet(coord.Y);

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
        /// 核心坐标变换方法：将 (x, y) feet 坐标变换为 Coordinate (mm)
        /// </summary>
        private Coordinate TransformToCoordinate(double x, double y)
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
            return new Coordinate(
                UnitConverter.ToMillimeters(localX),
                UnitConverter.ToMillimeters(localY)
            );
        }

        #endregion

        #region NTS 几何变换

        private static readonly GeometryFactory Factory = new GeometryFactory();

        /// <summary>
        /// 将 NTS Polygon (feet) 变换为 NTS Polygon (mm)
        /// 支持内环（孔洞）
        /// </summary>
        public Polygon TransformPolygon(Polygon ntsPolygon)
        {
            if (ntsPolygon == null)
                throw new ArgumentNullException(nameof(ntsPolygon));

            // 变换外环
            var shell = TransformRing(ntsPolygon.Shell);

            // 变换内环
            LinearRing[] holes = null;
            if (ntsPolygon.NumInteriorRings > 0)
            {
                var holesList = new List<LinearRing>();
                for (int i = 0; i < ntsPolygon.NumInteriorRings; i++)
                {
                    var holeRing = TransformRing(ntsPolygon.GetInteriorRingN(i));
                    if (holeRing != null)
                        holesList.Add(holeRing);
                }
                holes = holesList.ToArray();
            }

            return Factory.CreatePolygon(shell, holes);
        }

        /// <summary>
        /// 将 NTS LineSegment (feet) 变换为 NTS LineSegment (mm)
        /// </summary>
        public NetTopologySuite.Geometries.LineSegment TransformLineSegment(NetTopologySuite.Geometries.LineSegment segment)
        {
            if (segment == null)
                throw new ArgumentNullException(nameof(segment));

            return new NetTopologySuite.Geometries.LineSegment(
                TransformCoordinate(segment.P0),
                TransformCoordinate(segment.P1)
            );
        }

        /// <summary>
        /// 将 NTS LinearRing 变换为新的 LinearRing（去除闭合点，去重后重新闭合）
        /// </summary>
        private LinearRing TransformRing(LineString ring)
        {
            var coords = new List<Coordinate>();
            var sourceCoords = ring.Coordinates;

            // 跳过最后一个闭合点
            for (int i = 0; i < sourceCoords.Length - 1; i++)
            {
                var transformed = TransformCoordinate(sourceCoords[i]);

                // 去重相邻点（阈值 0.01mm）
                if (coords.Count == 0 ||
                    Math.Abs(transformed.X - coords.Last().X) > 0.01 ||
                    Math.Abs(transformed.Y - coords.Last().Y) > 0.01)
                {
                    coords.Add(transformed);
                }
            }

            if (coords.Count < 3)
                return null;

            // NTS LinearRing 需要闭合
            coords.Add(coords[0]);
            return Factory.CreateLinearRing(coords.ToArray());
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
