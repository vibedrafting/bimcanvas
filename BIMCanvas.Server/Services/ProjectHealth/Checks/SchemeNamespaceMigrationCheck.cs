using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BIMCanvas.Server.Services.ProjectHealth.Checks
{
    /// <summary>
    /// scene namespace 迁移:<c>schemes/{zoneId}/*</c> → <c>schemes/{active_scene}/{zoneId}/*</c>。
    /// <para>
    /// active_scene = active plugin id(数据命名空间)。把旧项目(v3.0 / 未带命名空间)的所有 zone
    /// 业务数据(modules / variants / semantic_plan / reference_analysis,含嵌套子分区)整目录迁到
    /// plugin 命名空间下。<c>schemes/zones.json</c> 全 scene 共享(baseline 派生),不迁。
    /// </para>
    /// <para>
    /// target 命名空间 = active plugin id(全局真源 ConfigService.Load().Agent.ActivePlugin;
    /// 空则默认 "interior-layout" —— 历史唯一 domain plugin)。**不依赖 project.json.scenes[]**
    /// (旧项目本就没有 scenes[],这是它的常态)。
    /// </para>
    /// <para>
    /// 幂等:迁移后旧 zone 目录已移走,二次 Inspect 返回空。目标已存在(手工已迁部分)报 error 不覆盖。
    /// 修复前 ProjectHealthService 自动 git commit 兜底。
    /// </para>
    /// </summary>
    public class SchemeNamespaceMigrationCheck : IProjectHealthCheck
    {
        public string Id => "scheme-namespace";
        public string Description => "scene namespace:schemes/{zoneId}/* → schemes/{active_scene}/{zoneId}/*";

        public CheckInspectionResult Inspect(string projectPath)
        {
            var result = new CheckInspectionResult { CheckId = Id, CheckDescription = Description };
            var schemesPath = Path.Combine(projectPath, "schemes");
            if (!Directory.Exists(schemesPath))
                return result;

            var target = ResolveActiveScene();
            foreach (var legacyDir in EnumerateLegacyZoneDirs(schemesPath, target))
            {
                result.Issues.Add(new HealthIssue
                {
                    RelativePath = ToRelative(projectPath, legacyDir),
                    IssueType = "legacy-scheme-zone-path",
                    Description = $"旧 zone 目录需迁到 schemes/{target}/{Path.GetFileName(legacyDir)}/",
                });
            }
            return result;
        }

        public CheckRepairResult Repair(string projectPath)
        {
            var result = new CheckRepairResult { CheckId = Id, CheckDescription = Description };
            var schemesPath = Path.Combine(projectPath, "schemes");
            if (!Directory.Exists(schemesPath))
                return result;

            var target = ResolveActiveScene();
            var legacyDirs = EnumerateLegacyZoneDirs(schemesPath, target).ToList();
            if (legacyDirs.Count == 0)
                return result;

            var targetRoot = Path.Combine(schemesPath, target);
            Directory.CreateDirectory(targetRoot);

            foreach (var legacyDir in legacyDirs)
            {
                try
                {
                    var zoneName = Path.GetFileName(legacyDir);
                    var dest = Path.Combine(targetRoot, zoneName);
                    if (Directory.Exists(dest))
                    {
                        result.Errors.Add($"{ToRelative(projectPath, legacyDir)}: 目标 schemes/{target}/{zoneName}/ 已存在,跳过(请手动检查冲突)");
                        continue;
                    }
                    Directory.Move(legacyDir, dest);
                    result.Migrated.Add($"{ToRelative(projectPath, legacyDir)} → schemes/{target}/{zoneName}/");
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{ToRelative(projectPath, legacyDir)}: 迁移失败 - {ex.Message}");
                }
            }
            return result;
        }

        // ---------- helpers ----------

        /// <summary>
        /// active_scene = active plugin id(全局真源);空 / 异常时默认 "interior-layout"。
        /// 与 BuildLaunchContext 取 active plugin 的方式一致。
        /// </summary>
        private static string ResolveActiveScene()
        {
            try
            {
                var p = BIMCanvas.Server.Services.ConfigService.Load().Agent.ActivePlugin;
                if (!string.IsNullOrWhiteSpace(p))
                    return p;
            }
            catch
            {
                // 配置不可用(如 CLI 上下文)→ 回退默认
            }
            return "interior-layout";
        }

        /// <summary>
        /// 旧 zone 目录:schemes/ 直接子目录,目录名 != target(命名空间根),
        /// 且其下含 zone 业务数据标记(modules.json / semantic_plan.json / reference_analysis.json / variants/)。
        /// 其它 plugin 的命名空间根目录(其直接子才是 zone)不含这些标记,不会被误迁。
        /// </summary>
        private static IEnumerable<string> EnumerateLegacyZoneDirs(string schemesPath, string target)
        {
            foreach (var subdir in Directory.GetDirectories(schemesPath))
            {
                var name = Path.GetFileName(subdir);
                if (string.Equals(name, target, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (HasZoneMarker(subdir))
                    yield return subdir;
            }
        }

        private static bool HasZoneMarker(string dir)
        {
            return File.Exists(Path.Combine(dir, "modules.json"))
                || File.Exists(Path.Combine(dir, "semantic_plan.json"))
                || File.Exists(Path.Combine(dir, "reference_analysis.json"))
                || Directory.Exists(Path.Combine(dir, "variants"));
        }

        private static string ToRelative(string projectPath, string filePath)
            => Path.GetRelativePath(projectPath, filePath).Replace('\\', '/');
    }
}
