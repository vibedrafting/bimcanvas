using System;
using Autodesk.Revit.DB;
using BIMCanvas.Core.Converters;
using BIMCanvas.Core.Models.Primitives;

namespace BIMCanvas.Revit.Adapters
{
    /// <summary>
    /// 坐标系转换适配器
    /// 负责 Revit XYZ (feet, 项目坐标系) 与 BIMCanvas Point2D (mm, 视图坐标系) 之间的转换
    /// </summary>
    public class CoordinateAdapter
    {
        private readonly XYZ _viewOrigin;
        private readonly double _viewRotation;

        /// <summary>
        /// 基于视图创建坐标转换器
        /// </summary>
        /// <param name="view">Revit 平面视图</param>
        public CoordinateAdapter(View view)
        {
            if (view == null)
                throw new ArgumentNullException(nameof(view));

            _viewOrigin = GetViewOrigin(view);
            _viewRotation = GetViewRotation(view);
        }

        /// <summary>
        /// 将 Revit XYZ 坐标转换为 BIMCanvas Point2D
        /// </summary>
        /// <param name="revitPoint">Revit 坐标 (feet)</param>
        /// <returns>BIMCanvas 坐标 (mm)</returns>
        public Point2D ToPoint2D(XYZ revitPoint)
        {
            if (revitPoint == null)
                throw new ArgumentNullException(nameof(revitPoint));

            // 1. 计算相对于视图原点的偏移
            var dx = revitPoint.X - _viewOrigin.X;
            var dy = revitPoint.Y - _viewOrigin.Y;

            // 2. 应用视图旋转（如果有）
            double localX, localY;
            if (Math.Abs(_viewRotation) > 1e-6)
            {
                var cosR = Math.Cos(-_viewRotation);
                var sinR = Math.Sin(-_viewRotation);
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
        /// 将 BIMCanvas Point2D 坐标转换为 Revit XYZ
        /// </summary>
        /// <param name="point">BIMCanvas 坐标 (mm)</param>
        /// <param name="elevation">标高 (feet)，默认为 0</param>
        /// <returns>Revit 坐标 (feet)</returns>
        public XYZ ToXYZ(Point2D point, double elevation = 0)
        {
            // 1. 单位转换：mm → feet
            var localX = UnitConverter.ToFeet(point.X);
            var localY = UnitConverter.ToFeet(point.Y);

            // 2. 反向旋转
            double dx, dy;
            if (Math.Abs(_viewRotation) > 1e-6)
            {
                var cosR = Math.Cos(_viewRotation);
                var sinR = Math.Sin(_viewRotation);
                dx = localX * cosR - localY * sinR;
                dy = localX * sinR + localY * cosR;
            }
            else
            {
                dx = localX;
                dy = localY;
            }

            // 3. 加上视图原点偏移
            return new XYZ(
                _viewOrigin.X + dx,
                _viewOrigin.Y + dy,
                elevation
            );
        }

        /// <summary>
        /// 获取视图裁剪框的左下角作为原点
        /// </summary>
        private XYZ GetViewOrigin(View view)
        {
            // 尝试获取视图裁剪框
            if (view.CropBoxActive)
            {
                var cropBox = view.CropBox;
                return cropBox.Min;
            }

            // 如果没有裁剪框，使用项目原点
            return XYZ.Zero;
        }

        /// <summary>
        /// 获取视图旋转角度（弧度）
        /// </summary>
        private double GetViewRotation(View view)
        {
            // 通过 RightDirection 计算视图旋转角度
            var rightDir = view.RightDirection;
            return Math.Atan2(rightDir.Y, rightDir.X);
        }

        /// <summary>
        /// 获取视图原点（用于调试）
        /// </summary>
        public XYZ ViewOrigin => _viewOrigin;

        /// <summary>
        /// 获取视图旋转角度（弧度，用于调试）
        /// </summary>
        public double ViewRotation => _viewRotation;
    }
}
