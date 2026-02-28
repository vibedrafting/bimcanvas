using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BIMCanvas.Core.Models.Computed;
using BIMCanvas.Core.Models.Geometry;
using BIMCanvas.Core.Models.Layout;
using BIMCanvas.Core.Models.Revit;
using BIMCanvas.Core.Models.Shared;
using BIMCanvas.Core.Validation;

namespace BIMCanvas.Core.Algorithms.Spatial
{
    /// <summary>
    /// 方案级全量验证器（"布局编译器"）
    /// 遍历所有模块，检查三类硬性错误：
    /// 1. 模块超出所有设计区
    /// 2. 模块与原建筑（墙/柱）或禁区重叠
    /// 3. 模块之间互相重叠
    /// </summary>
    public static class SchemeValidator
    {
        private const double ErrorThresholdMm = 10.0;

        /// <summary>
        /// 验证整个方案的布局合法性
        /// </summary>
        /// <param name="modules">当前方案的所有模块</param>
        /// <param name="designZones">合法放置区域（Type = Room 或 Designable）</param>
        /// <param name="exclusionZones">禁区（Type = Exclusion）</param>
        /// <param name="walls">墙体列表（来自 baseline）</param>
        /// <param name="columns">柱子列表（来自 baseline）</param>
        /// <returns>验证报告</returns>
        public static SchemeValidationReport Validate(
            IReadOnlyList<Module> modules,
            IReadOnlyList<Zone> designZones,
            IReadOnlyList<Zone> exclusionZones,
            IReadOnlyList<Wall> walls,
            IReadOnlyList<Column> columns)
        {
            var sw = Stopwatch.StartNew();
            var diagnostics = new List<Diagnostic>();

            // ============ Phase 1: 预计算 AABB ============

            var zoneCache = designZones
                .Where(z => z.Type == ZoneType.Room || z.Type == ZoneType.Designable)
                .Select(z =>
                {
                    var boundary = z.ComputedBoundary ?? z.RawBoundary;
                    return (zone: z, boundary: boundary, aabb: boundary?.ComputeAABB());
                })
                .Where(x => x.boundary != null)
                .ToList();

            var exclusionCache = exclusionZones
                .Where(z => z.Type == ZoneType.Exclusion)
                .Select(z =>
                {
                    var boundary = z.RawBoundary ?? z.ComputedBoundary;
                    return (zone: z, boundary: boundary, aabb: boundary?.ComputeAABB());
                })
                .Where(x => x.boundary != null)
                .ToList();

            var wallCache = walls
                .Where(w => w.Polygon != null)
                .Select(w => (wall: w, aabb: w.Polygon!.ComputeAABB()))
                .ToList();

            var columnCache = columns
                .Where(c => c.Polygon != null)
                .Select(c => (column: c, aabb: c.Polygon!.ComputeAABB()))
                .ToList();

            // ============ Phase 2: 逐模块检查 ============

            // 预计算每个模块的 AABB（Phase 3 也会复用）
            var moduleCache = new List<(Module module, AABB aabb)>();

            for (int i = 0; i < modules.Count; i++)
            {
                var module = modules[i];

                // Check 0: bounds 不能为 null
                if (module.Bounds == null)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.MissingBounds,
                        "error",
                        $"模块 {module.Id} ({module.ModuleName ?? "未命名"}) 缺少边界定义",
                        module.Id,
                        module.ModuleName));
                    continue;
                }

                var moduleBounds = module.Bounds;
                var moduleAABB = moduleBounds.ComputeAABB();
                moduleCache.Add((module, moduleAABB));

                // Check 1: 模块是否在任何合法区域内（带 2mm 容差）
                var inAnyZone = false;
                foreach (var (zone, boundary, zoneAABB) in zoneCache)
                {
                    if (zoneAABB.HasValue && moduleAABB.Intersects(zoneAABB.Value))
                    {
                        if (CollisionDetector.IsWithinTolerant(moduleBounds, boundary!))
                        {
                            inAnyZone = true;
                            break;
                        }
                    }
                }

                if (!inAnyZone)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.OutOfBounds,
                        "error",
                        $"模块 {module.Id} ({module.ModuleName ?? "未命名"}) 不在任何设计区域内",
                        module.Id,
                        module.ModuleName));
                }

                // Check 2a: 模块与墙体重叠（穿透深度分级）
                foreach (var (wall, wallAABB) in wallCache)
                {
                    if (moduleAABB.Intersects(wallAABB))
                    {
                        var wallInfo = CollisionDetector.ComputeOverlapInfo(moduleBounds, wall.Polygon!);
                        if (wallInfo.HasOverlap)
                        {
                            string severity = wallInfo.PenetrationDepthMm > ErrorThresholdMm ? "error" : "warning";
                            diagnostics.Add(new Diagnostic(
                                DiagnosticCodes.WallOverlap,
                                severity,
                                $"模块 {module.Id} ({module.ModuleName ?? "未命名"}) 与墙体 {wall.Id} 重叠",
                                module.Id,
                                module.ModuleName,
                                wall.Id,
                                "wall",
                                wallInfo.OverlapAreaMm2,
                                wallInfo.PenetrationDepthMm,
                                wallInfo.PenetrationDirection));
                        }
                    }
                }

                // Check 2b: 模块与柱子重叠（穿透深度分级）
                foreach (var (column, colAABB) in columnCache)
                {
                    if (moduleAABB.Intersects(colAABB))
                    {
                        var colInfo = CollisionDetector.ComputeOverlapInfo(moduleBounds, column.Polygon!);
                        if (colInfo.HasOverlap)
                        {
                            string severity = colInfo.PenetrationDepthMm > ErrorThresholdMm ? "error" : "warning";
                            diagnostics.Add(new Diagnostic(
                                DiagnosticCodes.ColumnOverlap,
                                severity,
                                $"模块 {module.Id} ({module.ModuleName ?? "未命名"}) 与柱子 {column.Id} 重叠",
                                module.Id,
                                module.ModuleName,
                                column.Id,
                                "column",
                                colInfo.OverlapAreaMm2,
                                colInfo.PenetrationDepthMm,
                                colInfo.PenetrationDirection));
                        }
                    }
                }

                // Check 2c: 模块与禁区重叠（穿透深度分级）
                foreach (var (zone, boundary, exclAABB) in exclusionCache)
                {
                    if (exclAABB.HasValue && moduleAABB.Intersects(exclAABB.Value))
                    {
                        var exclInfo = CollisionDetector.ComputeOverlapInfo(moduleBounds, boundary!);
                        if (exclInfo.HasOverlap)
                        {
                            string severity = exclInfo.PenetrationDepthMm > ErrorThresholdMm ? "error" : "warning";
                            diagnostics.Add(new Diagnostic(
                                DiagnosticCodes.ExclusionOverlap,
                                severity,
                                $"模块 {module.Id} ({module.ModuleName ?? "未命名"}) 与禁区 {zone.Id} 重叠 ({zone.Reason})",
                                module.Id,
                                module.ModuleName,
                                zone.Id,
                                "exclusion",
                                exclInfo.OverlapAreaMm2,
                                exclInfo.PenetrationDepthMm,
                                exclInfo.PenetrationDirection));
                        }
                    }
                }
            }

            // ============ Phase 3: 模块间两两重叠检查（穿透深度分级） ============

            for (int i = 0; i < moduleCache.Count; i++)
            {
                var (moduleA, aabbA) = moduleCache[i];

                for (int j = i + 1; j < moduleCache.Count; j++)
                {
                    var (moduleB, aabbB) = moduleCache[j];

                    // AABB 预检
                    if (!aabbA.Intersects(aabbB))
                        continue;

                    var modInfo = CollisionDetector.ComputeOverlapInfo(moduleA.Bounds!, moduleB.Bounds!);
                    if (modInfo.HasOverlap)
                    {
                        string severity = modInfo.PenetrationDepthMm > ErrorThresholdMm ? "error" : "warning";
                        string reverseDir = ReverseDirection(modInfo.PenetrationDirection);

                        // 双向记录（方向互反）
                        diagnostics.Add(new Diagnostic(
                            DiagnosticCodes.ModuleOverlap,
                            severity,
                            $"模块 {moduleA.Id} ({moduleA.ModuleName ?? "未命名"}) 与模块 {moduleB.Id} ({moduleB.ModuleName ?? "未命名"}) 重叠",
                            moduleA.Id,
                            moduleA.ModuleName,
                            moduleB.Id,
                            "module",
                            modInfo.OverlapAreaMm2,
                            modInfo.PenetrationDepthMm,
                            modInfo.PenetrationDirection));

                        diagnostics.Add(new Diagnostic(
                            DiagnosticCodes.ModuleOverlap,
                            severity,
                            $"模块 {moduleB.Id} ({moduleB.ModuleName ?? "未命名"}) 与模块 {moduleA.Id} ({moduleA.ModuleName ?? "未命名"}) 重叠",
                            moduleB.Id,
                            moduleB.ModuleName,
                            moduleA.Id,
                            "module",
                            modInfo.OverlapAreaMm2,
                            modInfo.PenetrationDepthMm,
                            reverseDir));
                    }
                }
            }

            sw.Stop();

            return new SchemeValidationReport
            {
                TotalModules = modules.Count,
                ErrorCount = diagnostics.Count(d => d.Severity == "error"),
                WarningCount = diagnostics.Count(d => d.Severity == "warning"),
                Diagnostics = diagnostics,
                ElapsedMs = sw.ElapsedMilliseconds
            };
        }

        private static string ReverseDirection(string direction)
        {
            switch (direction)
            {
                case "north": return "south";
                case "south": return "north";
                case "east": return "west";
                case "west": return "east";
                default: return direction ?? "unknown";
            }
        }
    }
}
