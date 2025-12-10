using System;
using System.Linq;
using System.Collections.Generic;
using BIMCanvas.Core.Converters;
using NetTopologySuite.Geometries;
using NetTopologySuite.Mathematics;

namespace BIMCanvas.Revit.Services
{
    /// <summary>
    /// NTS 几何对象坐标变换器
    ///
    /// 输入：NTS 几何对象（Revit 项目坐标系，feet）
    /// 输出：NTS 几何对象（归一化坐标系，mm）
    ///
    /// 变换流程（所有公开方法统一执行）：
    /// 1. 原点偏移：相对于指定原点计算偏移量
    /// 2. 旋转归一化：反向旋转消除视图旋转
    /// 3. 单位转换：feet → mm
    ///
    /// 职责边界：
    /// - 本类只做坐标变换，不做类型转换
    /// - Revit API ↔ NTS 类型转换由 RevitNtsConverter 负责
    /// - NTS ↔ Core.Models 类型转换由 NtsConverter 负责
    /// </summary>
    public class CoordinateTransformer
    {
        private readonly Coordinate _origin;
        private readonly double _rotation;
        private static readonly GeometryFactory Factory = new GeometryFactory();

        /// <summary>
        /// 创建坐标变换器
        /// </summary>
        /// <param name="origin">原点位置（NTS Coordinate，Revit 项目坐标系，feet）</param>
        /// <param name="rotation">视图旋转角度（弧度，正值表示逆时针）</param>
        public CoordinateTransformer(Coordinate origin, double rotation)
        {
            _origin = origin ?? throw new ArgumentNullException(nameof(origin));
            _rotation = rotation;
        }

        #region 公开变换方法

        /// <summary>
        /// 变换单个坐标点
        /// </summary>
        /// <param name="coord">输入坐标（feet，项目坐标系）</param>
        /// <returns>输出坐标（mm，归一化坐标系）</returns>
        public Coordinate TransformCoordinate(Coordinate coord)
        {
            if (coord == null)
                throw new ArgumentNullException(nameof(coord));

            return Transform(coord.X, coord.Y);
        }

        /// <summary>
        /// 变换线段
        /// </summary>
        /// <param name="segment">输入线段（feet，项目坐标系）</param>
        /// <returns>输出线段（mm，归一化坐标系）</returns>
        public LineSegment TransformLineSegment(LineSegment segment)
        {
            if (segment == null)
                throw new ArgumentNullException(nameof(segment));

            return new LineSegment(
                TransformCoordinate(segment.P0),
                TransformCoordinate(segment.P1)
            );
        }

        /// <summary>
        /// 变换方向向量（仅旋转，不平移和缩放）
        /// </summary>
        /// <param name="vector">输入向量（项目坐标系方向）</param>
        /// <returns>输出向量（归一化坐标系方向）</returns>
        public Vector2D TransformVector2D(Vector2D vector)
        {
            if (Math.Abs(_rotation) > 1e-6)
            {
                var cosR = Math.Cos(-_rotation);
                var sinR = Math.Sin(-_rotation);
                return new Vector2D(
                    vector.X * cosR - vector.Y * sinR,
                    vector.X * sinR + vector.Y * cosR
                );
            }
            return vector;
        }

        /// <summary>
        /// 变换多边形（支持内环/孔洞）
        /// </summary>
        /// <param name="ntsPolygon">输入多边形（feet，项目坐标系）</param>
        /// <returns>输出多边形（mm，归一化坐标系）</returns>
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

        #endregion

        #region 私有变换方法

        /// <summary>
        /// 核心变换逻辑：原点偏移 → 旋转归一化 → 单位转换
        /// </summary>
        private Coordinate Transform(double x, double y)
        {
            // 1. 原点偏移：计算相对于原点的位置
            var dx = x - _origin.X;
            var dy = y - _origin.Y;

            // 2. 旋转归一化：反向旋转消除视图旋转
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

        /// <summary>
        /// 变换环（去除闭合点、去重相邻点、重新闭合）
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
        /// 原点位置（Revit 项目坐标系，feet）
        /// </summary>
        public Coordinate Origin => _origin;

        /// <summary>
        /// 视图旋转角度（弧度）
        /// </summary>
        public double Rotation => _rotation;

        #endregion
    }
}
