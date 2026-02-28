using System;
using BIMCanvas.Core.Converters;
using BIMCanvas.Core.Models.Geometry;
using NetTopologySuite.Geometries;

namespace BIMCanvas.Core.Algorithms.Spatial
{
    /// <summary>重叠详情（用于填充 Diagnostic 的修正指引字段）</summary>
    public struct OverlapInfo
    {
        public bool HasOverlap;
        public double OverlapAreaMm2;
        public double PenetrationDepthMm;
        public string PenetrationDirection;
    }

    /// <summary>
    /// 碰撞检测器
    /// </summary>
    public static class CollisionDetector
    {
        /// <summary>
        /// 检测两个多边形是否相交（包括接触）
        /// </summary>
        public static bool Intersects(Polygon2D a, Polygon2D b)
        {
            var ntsA = NtsConverter.ToNtsPolygon(a);
            var ntsB = NtsConverter.ToNtsPolygon(b);
            return ntsA.Intersects(ntsB);
        }

        /// <summary>
        /// 检测两个多边形是否有重叠区域（不包括仅边界接触）
        /// </summary>
        public static bool Overlaps(Polygon2D a, Polygon2D b)
        {
            var ntsA = NtsConverter.ToNtsPolygon(a);
            var ntsB = NtsConverter.ToNtsPolygon(b);
            // Overlaps 返回 true 当两个几何体有共同的内部点
            return ntsA.Overlaps(ntsB) || ntsA.Contains(ntsB) || ntsB.Contains(ntsA);
        }

        /// <summary>
        /// 检测内部多边形是否完全在外部多边形内
        /// </summary>
        public static bool IsWithin(Polygon2D inner, Polygon2D outer)
        {
            var ntsInner = NtsConverter.ToNtsPolygon(inner);
            var ntsOuter = NtsConverter.ToNtsPolygon(outer);
            return ntsOuter.Contains(ntsInner);
        }

        /// <summary>
        /// 检测点是否在多边形内
        /// </summary>
        public static bool Contains(Polygon2D polygon, Point2D point)
        {
            var ntsPolygon = NtsConverter.ToNtsPolygon(polygon);
            var ntsPoint = NtsConverter.ToNtsPoint(point);
            return ntsPolygon.Contains(ntsPoint);
        }

        /// <summary>
        /// 快速 AABB 碰撞预检测
        /// </summary>
        public static bool AABBIntersects(Polygon2D a, Polygon2D b)
        {
            var aabbA = a.ComputeAABB();
            var aabbB = b.ComputeAABB();
            return aabbA.Intersects(aabbB);
        }

        /// <summary>
        /// 带容差的包含检测：outer 膨胀 toleranceMm 后判断是否包含 inner。
        /// 用于 E001 检测，容忍 room zone 边界与实际墙面的微小偏差。
        /// </summary>
        public static bool IsWithinTolerant(Polygon2D inner, Polygon2D outer, double toleranceMm = 10.0)
        {
            var ntsInner = NtsConverter.ToNtsPolygon(inner);
            var ntsOuter = NtsConverter.ToNtsPolygon(outer);

            // 快速路径：精确包含
            if (ntsOuter.Contains(ntsInner))
                return true;

            // 容差路径：膨胀 outer 后再检测
            try
            {
                var buffered = ntsOuter.Buffer(toleranceMm);
                return buffered.Contains(ntsInner);
            }
            catch (TopologyException)
            {
                return false;
            }
        }

        /// <summary>
        /// 带容差的重叠检测：穿透深度超过阈值才视为真实碰撞。
        /// 用于 PlacementValidator，只需布尔结果。
        /// SchemeValidator 应直接使用 ComputeOverlapInfo 获取详情。
        /// </summary>
        public static bool OverlapsTolerant(Polygon2D a, Polygon2D b, double toleranceMm = 10.0)
        {
            var info = ComputeOverlapInfo(a, b);
            return info.HasOverlap && info.PenetrationDepthMm > toleranceMm;
        }

        /// <summary>
        /// 计算重叠详情：面积、穿透深度、穿透方向。
        /// PenetrationDepthMm = 交集各子几何包络矩形最小维度的最大值，不依赖方向判断。
        /// PenetrationDirection 表示障碍物在模块的哪个方位，Agent 应朝相反方向移动。
        /// </summary>
        public static OverlapInfo ComputeOverlapInfo(Polygon2D moduleBounds, Polygon2D obstacle)
        {
            var ntsModule = NtsConverter.ToNtsPolygon(moduleBounds);
            var ntsObstacle = NtsConverter.ToNtsPolygon(obstacle);

            if (!ntsModule.Intersects(ntsObstacle))
                return new OverlapInfo { HasOverlap = false };

            try
            {
                var intersection = ntsModule.Intersection(ntsObstacle);
                if (intersection.IsEmpty || intersection.Area < 1e-6)
                    return new OverlapInfo { HasOverlap = false };

                // 穿透深度：遍历交集所有子几何，每个取 Min(W,H)，整体取 Max
                double maxDepth = 0;
                for (int i = 0; i < intersection.NumGeometries; i++)
                {
                    var part = intersection.GetGeometryN(i);
                    if (part.Area < 1e-6) continue;
                    var partEnv = part.EnvelopeInternal;
                    double partDepth = Math.Min(partEnv.Width, partEnv.Height);
                    maxDepth = Math.Max(maxDepth, partDepth);
                }

                if (maxDepth < 1e-6)
                    return new OverlapInfo { HasOverlap = false };

                // 穿透方向：障碍物相对于模块中心的方位
                var moduleCentroid = ntsModule.Centroid.Coordinate;
                var overlapCentroid = intersection.Centroid.Coordinate;
                double dx = overlapCentroid.X - moduleCentroid.X;
                double dy = overlapCentroid.Y - moduleCentroid.Y;
                string direction = Math.Abs(dx) >= Math.Abs(dy)
                    ? (dx >= 0 ? "east" : "west")
                    : (dy >= 0 ? "north" : "south");

                return new OverlapInfo
                {
                    HasOverlap = true,
                    OverlapAreaMm2 = Math.Round(intersection.Area, 1),
                    PenetrationDepthMm = Math.Round(maxDepth, 1),
                    PenetrationDirection = direction
                };
            }
            catch (TopologyException)
            {
                return new OverlapInfo { HasOverlap = false };
            }
        }
    }
}
