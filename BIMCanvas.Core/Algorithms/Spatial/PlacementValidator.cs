using System.Collections.Generic;
using System.Linq;
using BIMCanvas.Core.Models.Computed;
using BIMCanvas.Core.Models.Layout;
using BIMCanvas.Core.Models.Geometry;
using BIMCanvas.Core.Models.Shared;
using BIMCanvas.Core.Validation;

namespace BIMCanvas.Core.Algorithms.Spatial
{
    /// <summary>
    /// 布置验证器（只验证，不修正）
    /// </summary>
    public static class PlacementValidator
    {
        /// <summary>
        /// 验证模块布置是否合法
        /// </summary>
        /// <param name="moduleBounds">模块边界</param>
        /// <param name="designZone">设计区（Type = Designable）</param>
        /// <param name="exclusionZones">禁区列表（Type = Exclusion）</param>
        /// <param name="existingModules">已放置的模块列表</param>
        /// <returns>验证结果</returns>
        public static ValidationResult Validate(
            Polygon2D moduleBounds,
            Zone designZone,
            IEnumerable<Zone> exclusionZones,
            IEnumerable<Module> existingModules)
        {
            var violations = new List<Violation>();

            // 1. 检查是否在设计区边界内（优先使用 ComputedBoundary）
            var boundary = designZone.ComputedBoundary ?? designZone.RawBoundary;
            if (boundary != null)
            {
                if (!CollisionDetector.IsWithinTolerant(moduleBounds, boundary))
                {
                    violations.Add(new Violation(
                        "模块超出区域边界",
                        "OUT_OF_BOUNDS",
                        designZone.Id));
                }
            }

            // 2. 检查是否与禁区重叠
            foreach (var exclusion in exclusionZones.Where(z => z.Type == ZoneType.Exclusion))
            {
                var exclusionBoundary = exclusion.RawBoundary ?? exclusion.ComputedBoundary;
                if (exclusionBoundary != null &&
                    CollisionDetector.OverlapsTolerant(moduleBounds, exclusionBoundary))
                {
                    violations.Add(new Violation(
                        $"模块与禁区重叠：{exclusion.Reason}",
                        "EXCLUSION_OVERLAP",
                        exclusion.Id));
                }
            }

            // 3. 检查是否与其他模块重叠
            foreach (var existing in existingModules)
            {
                if (existing.Bounds != null &&
                    CollisionDetector.OverlapsTolerant(moduleBounds, existing.Bounds))
                {
                    violations.Add(new Violation(
                        $"模块与已放置模块 {existing.Id} 重叠",
                        "MODULE_OVERLAP",
                        existing.Id));
                }
            }

            return violations.Count > 0
                ? ValidationResult.Failure(violations)
                : ValidationResult.Success();
        }

        /// <summary>
        /// 验证模块布置（简化版，不检查禁区）
        /// </summary>
        public static ValidationResult Validate(
            Polygon2D moduleBounds,
            Zone designZone,
            IEnumerable<Module> existingModules)
        {
            return Validate(moduleBounds, designZone, Enumerable.Empty<Zone>(), existingModules);
        }

        /// <summary>
        /// 验证单个模块实例
        /// </summary>
        public static ValidationResult ValidateModule(
            Module module,
            Zone zone,
            IEnumerable<Zone> exclusionZones,
            IEnumerable<Module> otherModules)
        {
            if (module.Bounds == null)
            {
                return ValidationResult.Failure(new Violation(
                    "模块缺少边界定义",
                    "MISSING_BOUNDS",
                    module.Id));
            }

            return Validate(module.Bounds, zone, exclusionZones, otherModules);
        }

        /// <summary>
        /// 检查模块是否与任何已放置模块冲突
        /// </summary>
        public static bool HasConflict(Polygon2D bounds, IEnumerable<Module> modules)
        {
            foreach (var module in modules)
            {
                if (module.Bounds != null &&
                    CollisionDetector.OverlapsTolerant(bounds, module.Bounds))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
