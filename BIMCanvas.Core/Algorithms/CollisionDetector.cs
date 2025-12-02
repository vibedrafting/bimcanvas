using System.Collections.Generic;
using BIMCanvas.Core.Models;

namespace BIMCanvas.Core.Algorithms
{
    /// <summary>
    /// 碰撞检测 - 基于 AABB
    /// </summary>
    public class CollisionDetector
    {
        /// <summary>
        /// 检查模块是否可放置
        /// </summary>
        /// <param name="module">待放置的模块</param>
        /// <param name="zone">目标区域</param>
        /// <param name="existingModules">已存在的模块</param>
        /// <returns>是否可放置</returns>
        public bool CanPlace(Module module, Zone zone, List<Module> existingModules)
        {
            var bounds = module.Bounds;

            // 约束1: 必须在 innerBoundary 内
            if (!IsInsidePolygon(bounds, zone.InnerBoundary))
            {
                return false;
            }

            // 约束2: 不能与禁区重叠
            if (zone.ExclusionAreas != null)
            {
                foreach (var exclusion in zone.ExclusionAreas)
                {
                    if (AabbIntersects(bounds, exclusion.Rect))
                    {
                        return false;
                    }
                }
            }

            // 约束3: 不能与其他模块重叠
            if (existingModules != null)
            {
                foreach (var existing in existingModules)
                {
                    if (existing.Id == module.Id) continue; // 跳过自身
                    if (AabbIntersects(bounds, existing.Bounds))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 检查两个 AABB 是否相交
        /// </summary>
        /// <param name="a">AABB A [minX, minY, maxX, maxY]</param>
        /// <param name="b">AABB B [minX, minY, maxX, maxY]</param>
        /// <returns>是否相交</returns>
        public bool AabbIntersects(double[] a, double[] b)
        {
            if (a == null || b == null || a.Length < 4 || b.Length < 4)
                return false;

            // 不相交条件：任一方向上完全分离
            return !(a[2] < b[0] || a[0] > b[2] || a[3] < b[1] || a[1] > b[3]);
        }

        /// <summary>
        /// 检查 AABB 是否完全在多边形内
        /// </summary>
        /// <param name="bounds">AABB [minX, minY, maxX, maxY]</param>
        /// <param name="polygon">多边形顶点列表</param>
        /// <returns>是否在多边形内</returns>
        public bool IsInsidePolygon(double[] bounds, List<double[]> polygon)
        {
            if (bounds == null || polygon == null || polygon.Count < 3 || bounds.Length < 4)
                return false;

            // 检查 AABB 的四个角点是否都在多边形内
            double minX = bounds[0], minY = bounds[1], maxX = bounds[2], maxY = bounds[3];

            return PointInPolygon(minX, minY, polygon) &&
                   PointInPolygon(maxX, minY, polygon) &&
                   PointInPolygon(maxX, maxY, polygon) &&
                   PointInPolygon(minX, maxY, polygon);
        }

        /// <summary>
        /// 射线法判断点是否在多边形内
        /// </summary>
        private bool PointInPolygon(double x, double y, List<double[]> polygon)
        {
            int n = polygon.Count;
            bool inside = false;

            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = polygon[i][0], yi = polygon[i][1];
                double xj = polygon[j][0], yj = polygon[j][1];

                if (((yi > y) != (yj > y)) &&
                    (x < (xj - xi) * (y - yi) / (yj - yi) + xi))
                {
                    inside = !inside;
                }
            }

            return inside;
        }
    }
}
