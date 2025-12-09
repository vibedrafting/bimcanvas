using System;
using BIMCanvas.Core.Algorithms.Geometries;
using BIMCanvas.Core.Models.Document;
using BIMCanvas.Core.Models.Primitives;

namespace BIMCanvas.Core.Algorithms.Spatial
{
    /// <summary>
    /// AI 布置意图 → 精确几何转换
    /// </summary>
    public static class GeometryNormalizer
    {
        /// <summary>
        /// 创建带朝向的矩形边界
        /// </summary>
        /// <param name="center">矩形中心点</param>
        /// <param name="size">尺寸向量 (width, height)，width 沿朝向方向</param>
        /// <param name="facing">朝向</param>
        /// <returns>旋转后的矩形多边形</returns>
        public static Polygon2D CreateRectangle(Point2D center, Vec2D size, Facing facing)
        {
            // 先创建朝北（Y轴正方向）的矩形
            var rect = GeometryHelper.CreateRectangle(center, size.X, size.Y);

            // 计算需要旋转的角度
            var facingVector = facing.GetVector();
            // 朝向向量对应的角度（相对于Y轴正方向/north）
            var targetAngle = Math.Atan2(facingVector.X, facingVector.Y);

            // 如果不是朝北，需要旋转
            if (Math.Abs(targetAngle) > 1e-10)
            {
                rect = GeometryHelper.RotatePolygon(rect, targetAngle, center);
            }

            return rect;
        }

        /// <summary>
        /// 从 AABB 和朝向创建精确边界
        /// </summary>
        /// <param name="aabb">轴对齐包围盒</param>
        /// <param name="facing">朝向</param>
        /// <returns>旋转后的矩形多边形</returns>
        public static Polygon2D CreateFromAABB(AABB aabb, Facing facing)
        {
            var center = aabb.Center;
            var size = new Vec2D(aabb.Width, aabb.Height);
            return CreateRectangle(center, size, facing);
        }

        /// <summary>
        /// 根据 AI 放置指令创建模块边界
        /// </summary>
        /// <param name="centerX">中心点 X 坐标</param>
        /// <param name="centerY">中心点 Y 坐标</param>
        /// <param name="width">模块宽度（沿朝向方向）</param>
        /// <param name="depth">模块深度（垂直于朝向方向）</param>
        /// <param name="facing">朝向</param>
        /// <returns>模块边界多边形</returns>
        public static Polygon2D CreateModuleBounds(
            double centerX,
            double centerY,
            double width,
            double depth,
            Facing facing)
        {
            var center = new Point2D(centerX, centerY);
            var size = new Vec2D(width, depth);
            return CreateRectangle(center, size, facing);
        }
    }
}
