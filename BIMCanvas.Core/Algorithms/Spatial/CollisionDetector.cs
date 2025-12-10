using BIMCanvas.Core.Converters;
using BIMCanvas.Core.Models.Primitives;

namespace BIMCanvas.Core.Algorithms.Spatial
{
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
    }
}
