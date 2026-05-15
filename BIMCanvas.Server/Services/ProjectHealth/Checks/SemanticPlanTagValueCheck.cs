using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BIMCanvas.Server.Services.ProjectHealth.Checks
{
    /// <summary>
    /// Phase D 语义化 tag 值映射：
    ///   semantic_plan.json 内 Entries[].Tag 旧值 → 新值
    ///     v0.1       → spatial-skeleton
    ///     v0.2       → strategic-plan
    ///     v0.2-meta  → multi-plan-overview
    ///     v0.3       → construction-brief
    /// 仅扫 schemes/**/semantic_plan.json（含 schemes/{dz}/variants/{slug}/semantic_plan.json）；
    /// 不动 reference_analysis.json（v1/v2/v3+ 是真版本序列），不动 modules.json。
    /// </summary>
    public class SemanticPlanTagValueCheck : IProjectHealthCheck
    {
        public string Id => "phase-d-tag-value";
        public string Description => "Phase D：semantic_plan tag 值语义化重命名（v0.1 → spatial-skeleton 等）";

        private static readonly Dictionary<string, string> Mapping =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "v0.1", "spatial-skeleton" },
                { "v0.2", "strategic-plan" },
                { "v0.2-meta", "multi-plan-overview" },
                { "v0.3", "construction-brief" },
            };

        private static readonly HashSet<string> NewTagSet =
            new HashSet<string>(Mapping.Values, StringComparer.Ordinal);

        public CheckInspectionResult Inspect(string projectPath)
        {
            var result = new CheckInspectionResult { CheckId = Id, CheckDescription = Description };
            var schemesPath = Path.Combine(projectPath, "schemes");
            if (!Directory.Exists(schemesPath))
                return result;

            foreach (var filePath in Directory.GetFiles(schemesPath, "semantic_plan.json", SearchOption.AllDirectories))
            {
                try
                {
                    var legacyCount = InspectSemanticPlan(filePath);
                    if (legacyCount > 0)
                    {
                        result.Issues.Add(new HealthIssue
                        {
                            RelativePath = ToRelative(projectPath, filePath),
                            IssueType = "legacy-tag-value",
                            Description = $"含 {legacyCount} 条旧 tag 值（v0.x），需映射到语义化命名"
                        });
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{ToRelative(projectPath, filePath)}: {ex.Message}");
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

            foreach (var filePath in Directory.GetFiles(schemesPath, "semantic_plan.json", SearchOption.AllDirectories))
            {
                try
                {
                    var status = MigrateSemanticPlan(filePath);
                    Record(status, ToRelative(projectPath, filePath), result);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{ToRelative(projectPath, filePath)}: {ex.Message}");
                }
            }

            return result;
        }

        private static int InspectSemanticPlan(string filePath)
        {
            var content = File.ReadAllText(filePath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(content)) return 0;

            var root = JToken.Parse(content);
            if (root is not JObject obj) return 0;

            var entries = obj["Entries"] as JArray;
            if (entries == null || entries.Count == 0) return 0;

            int legacyCount = 0;
            foreach (var item in entries)
            {
                if (item is not JObject entry) continue;
                var tag = entry.Value<string>("Tag");
                if (!string.IsNullOrEmpty(tag) && Mapping.ContainsKey(tag))
                    legacyCount++;
            }
            return legacyCount;
        }

        private static MigrateStatus MigrateSemanticPlan(string filePath)
        {
            var content = File.ReadAllText(filePath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(content))
                return MigrateStatus.SkippedEmpty;

            var root = JToken.Parse(content);
            if (root is not JObject obj)
                throw new InvalidOperationException("semantic_plan.json 顶层不是 JSON 对象");

            var entries = obj["Entries"] as JArray;
            if (entries == null || entries.Count == 0)
                return MigrateStatus.SkippedEmpty;

            bool changed = false;
            bool hasUnknown = false;
            foreach (var item in entries)
            {
                if (item is not JObject entry)
                    throw new InvalidOperationException("Entries 条目不是对象");
                var tag = entry.Value<string>("Tag");
                if (string.IsNullOrEmpty(tag))
                {
                    hasUnknown = true;
                    continue;
                }
                if (Mapping.TryGetValue(tag, out var newTag))
                {
                    entry["Tag"] = newTag;
                    changed = true;
                }
                else if (!NewTagSet.Contains(tag))
                {
                    // 未识别的 tag（既不在旧集，也不在新集）——保留原样，由人工排查
                    hasUnknown = true;
                }
            }

            if (!changed)
                return hasUnknown ? MigrateStatus.SkippedAlready : MigrateStatus.SkippedAlready;

            WriteAtomic(filePath, root);
            return MigrateStatus.Migrated;
        }

        // ---------- helpers ----------

        private static void WriteAtomic(string filePath, JToken content)
        {
            var json = content.ToString(Formatting.Indented);
            var tmpPath = filePath + ".tmp";
            File.WriteAllText(tmpPath, json, Encoding.UTF8);
            File.Move(tmpPath, filePath, overwrite: true);
        }

        private static string ToRelative(string projectPath, string filePath)
        {
            return Path.GetRelativePath(projectPath, filePath).Replace('\\', '/');
        }

        private static void Record(MigrateStatus status, string relative, CheckRepairResult result)
        {
            switch (status)
            {
                case MigrateStatus.Migrated: result.Migrated.Add(relative); break;
                case MigrateStatus.SkippedAlready:
                case MigrateStatus.SkippedEmpty: result.Skipped.Add(relative); break;
            }
        }

        private enum MigrateStatus { Migrated, SkippedAlready, SkippedEmpty }
    }
}
