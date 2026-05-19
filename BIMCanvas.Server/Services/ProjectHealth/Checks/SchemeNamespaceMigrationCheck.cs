using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace BIMCanvas.Server.Services.ProjectHealth.Checks
{
    /// <summary>
    /// v1.1 平台化收尾:schemes/{zoneId}/* → schemes/{sceneId}/{zoneId}/* scene namespace 迁移。
    /// <para>
    /// 触发场景:v1.1 平台化之前,SemanticPlanController 直接写 schemes/{zoneId}/semantic_plan.json /
    /// reference_analysis.json / variants/{variantId}/semantic_plan.json,没有 sceneId 层。
    /// v1.1 平台化引入 scene namespace 后,所有业务数据应位于 schemes/{sceneId}/{zoneId}/ 下。
    /// 本 check 把残留的旧路径整目录迁移到 owning scene 命名空间。
    /// </para>
    /// <para>
    /// owning scene 判定(读 project.json.scenes[] 中 status=active 的 sceneId):
    /// - 0 个 → 全部 issue 标 error,无 owning scene 无法自动迁
    /// - 1 个 → 全部旧 zone 子目录用 Directory.Move 整迁到该 sceneId 下
    /// - ≥2 个 → 全部 issue 标 error,无法自动决定目标 scene,提示用户手动迁
    /// </para>
    /// <para>
    /// 幂等性:Inspect 与 Repair 都基于"schemes 直接子目录名不在 sceneIds 集合内 + 直接含 legacy marker 文件 / 目录"判定。
    /// 第二次执行时旧路径已被 Move 走,Inspect 返回空 issues;不会重复迁移。
    /// 若目标已存在(用户手工已迁部分)则报 error 不覆盖。
    /// </para>
    /// </summary>
    public class SchemeNamespaceMigrationCheck : IProjectHealthCheck
    {
        public string Id => "v1.1-scheme-namespace";
        public string Description => "v1.1 平台化:schemes/{zoneId}/* → schemes/{sceneId}/{zoneId}/* scene namespace 迁移";

        public CheckInspectionResult Inspect(string projectPath)
        {
            var result = new CheckInspectionResult { CheckId = Id, CheckDescription = Description };
            var schemesPath = Path.Combine(projectPath, "schemes");
            if (!Directory.Exists(schemesPath))
                return result;

            var sceneIds = ReadActiveSceneIds(projectPath, result.Errors);

            foreach (var legacyDir in EnumerateLegacyZoneDirs(schemesPath, sceneIds))
            {
                foreach (var filePath in EnumerateLegacyFiles(legacyDir))
                {
                    result.Issues.Add(new HealthIssue
                    {
                        RelativePath = ToRelative(projectPath, filePath),
                        IssueType = "legacy-scheme-zone-path",
                        Description = "旧路径 schemes/{zoneId}/* 需要迁到 schemes/{sceneId}/{zoneId}/* scene namespace"
                    });
                }
            }

            return result;
        }

        public CheckRepairResult Repair(string projectPath)
        {
            var result = new CheckRepairResult { CheckId = Id, CheckDescription = Description };
            var schemesPath = Path.Combine(projectPath, "schemes");
            if (!Directory.Exists(schemesPath))
                return result;

            var sceneIds = ReadActiveSceneIds(projectPath, result.Errors);
            var legacyDirs = EnumerateLegacyZoneDirs(schemesPath, sceneIds).ToList();
            if (legacyDirs.Count == 0)
                return result;

            if (sceneIds.Count == 0)
            {
                foreach (var dir in legacyDirs)
                    result.Errors.Add($"{ToRelative(projectPath, dir)}: 项目内无 active scene,无法决定 owning scene,跳过迁移");
                return result;
            }

            if (sceneIds.Count > 1)
            {
                var scenesList = string.Join(", ", sceneIds);
                foreach (var dir in legacyDirs)
                    result.Errors.Add($"{ToRelative(projectPath, dir)}: 项目内有多个 active scene ({scenesList}),无法自动决定目标 scene,请手动迁移");
                return result;
            }

            var ownSceneId = sceneIds[0];
            var targetRoot = Path.Combine(schemesPath, ownSceneId);
            Directory.CreateDirectory(targetRoot);

            foreach (var legacyDir in legacyDirs)
            {
                try
                {
                    var zoneName = Path.GetFileName(legacyDir);
                    var targetDir = Path.Combine(targetRoot, zoneName);
                    if (Directory.Exists(targetDir))
                    {
                        result.Errors.Add($"{ToRelative(projectPath, legacyDir)}: 目标 schemes/{ownSceneId}/{zoneName}/ 已存在,可能已迁移或有冲突,请手动检查");
                        continue;
                    }
                    Directory.Move(legacyDir, targetDir);
                    result.Migrated.Add($"{ToRelative(projectPath, legacyDir)} → schemes/{ownSceneId}/{zoneName}/");
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{ToRelative(projectPath, legacyDir)}: 迁移失败 - {ex.Message}");
                }
            }

            return result;
        }

        // ---------- helpers ----------

        /// <summary>读 project.json.scenes[] 中 status=active 的 sceneId 集合。CLI 复用本逻辑不依赖 ProjectScene model。</summary>
        private static List<string> ReadActiveSceneIds(string projectPath, List<string> errors)
        {
            var sceneIds = new List<string>();
            var projectJsonPath = Path.Combine(projectPath, "project.json");
            if (!File.Exists(projectJsonPath))
            {
                errors.Add("project.json 不存在,无法读取 scenes 列表");
                return sceneIds;
            }

            try
            {
                var root = JObject.Parse(File.ReadAllText(projectJsonPath, Encoding.UTF8));
                if (root["scenes"] is not JArray scenes)
                    return sceneIds;

                foreach (var token in scenes)
                {
                    if (token is not JObject scene) continue;
                    var sceneId = (string?)scene["sceneId"];
                    var status = (string?)scene["status"] ?? "active";
                    if (!string.IsNullOrEmpty(sceneId) &&
                        string.Equals(status, "active", StringComparison.OrdinalIgnoreCase))
                    {
                        sceneIds.Add(sceneId!);
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"读取 project.json.scenes 失败: {ex.Message}");
            }

            return sceneIds;
        }

        /// <summary>
        /// 枚举 schemes/ 下的"旧 zone 目录":目录名不在 sceneIds 集合内,
        /// 且其下直接含 semantic_plan.json / reference_analysis.json / variants/ 之一。
        /// </summary>
        private static IEnumerable<string> EnumerateLegacyZoneDirs(string schemesPath, IReadOnlyList<string> sceneIds)
        {
            var sceneSet = new HashSet<string>(sceneIds, StringComparer.OrdinalIgnoreCase);
            foreach (var subdir in Directory.GetDirectories(schemesPath))
            {
                var name = Path.GetFileName(subdir);
                if (sceneSet.Contains(name))
                    continue;
                if (HasLegacyMarker(subdir))
                    yield return subdir;
            }
        }

        private static bool HasLegacyMarker(string zoneDir)
        {
            if (File.Exists(Path.Combine(zoneDir, "semantic_plan.json"))) return true;
            if (File.Exists(Path.Combine(zoneDir, "reference_analysis.json"))) return true;
            if (Directory.Exists(Path.Combine(zoneDir, "variants"))) return true;
            return false;
        }

        /// <summary>列出一个 legacy zone 目录里所有受影响的语义/参考分析文件(Inspect 报告用)。</summary>
        private static IEnumerable<string> EnumerateLegacyFiles(string zoneDir)
        {
            var semantic = Path.Combine(zoneDir, "semantic_plan.json");
            if (File.Exists(semantic)) yield return semantic;

            var reference = Path.Combine(zoneDir, "reference_analysis.json");
            if (File.Exists(reference)) yield return reference;

            var variants = Path.Combine(zoneDir, "variants");
            if (Directory.Exists(variants))
            {
                foreach (var f in Directory.GetFiles(variants, "semantic_plan.json", SearchOption.AllDirectories))
                    yield return f;
                foreach (var f in Directory.GetFiles(variants, "reference_analysis.json", SearchOption.AllDirectories))
                    yield return f;
            }
        }

        private static string ToRelative(string projectPath, string filePath)
        {
            return Path.GetRelativePath(projectPath, filePath).Replace('\\', '/');
        }
    }
}
