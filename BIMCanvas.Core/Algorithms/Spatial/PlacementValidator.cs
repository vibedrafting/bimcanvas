using System.Collections.Generic;
using BIMCanvas.Core.Models.CanvasData;
using BIMCanvas.Core.Models.RevitWriteback;
using BIMCanvas.Core.Models.Primitives;
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
        /// <param name="zone">所属区域</param>
        /// <param name="existingModules">已放置的模块列表</param>
        /// <returns>验证结果</returns>
        public static ValidationResult Validate(
            Polygon2D moduleBounds,
            Zone zone,
            IEnumerable<Module> existingModules)
        {
            var violations = new List<Violation>();

            // 1. 检查是否在区域边界内
            if (zone.InnerBoundary != null)
            {
                if (!CollisionDetector.IsWithin(moduleBounds, zone.InnerBoundary))
                {
                    violations.Add(new Violation(
                        "模块超出区域边界",
                        "OUT_OF_BOUNDS",
                        zone.Id));
                }
            }

            // 2. 检查是否与禁区重叠
            foreach (var exclusion in zone.ExclusionAreas)
            {
                if (exclusion.Boundary != null &&
                    CollisionDetector.Overlaps(moduleBounds, exclusion.Boundary))
                {
                    violations.Add(new Violation(
                        $"模块与禁区 {exclusion.Type} 重叠",
                        "EXCLUSION_OVERLAP",
                        exclusion.Id));
                }
            }

            // 3. 检查是否与其他模块重叠
            foreach (var existing in existingModules)
            {
                if (existing.Bounds != null &&
                    CollisionDetector.Overlaps(moduleBounds, existing.Bounds))
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
        /// 验证单个模块实例
        /// </summary>
        public static ValidationResult ValidateModule(
            Module module,
            Zone zone,
            IEnumerable<Module> otherModules)
        {
            if (module.Bounds == null)
            {
                return ValidationResult.Failure(new Violation(
                    "模块缺少边界定义",
                    "MISSING_BOUNDS",
                    module.Id));
            }

            return Validate(module.Bounds, zone, otherModules);
        }

        /// <summary>
        /// 检查模块是否与任何已放置模块冲突
        /// </summary>
        public static bool HasConflict(Polygon2D bounds, IEnumerable<Module> modules)
        {
            foreach (var module in modules)
            {
                if (module.Bounds != null &&
                    CollisionDetector.Overlaps(bounds, module.Bounds))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
