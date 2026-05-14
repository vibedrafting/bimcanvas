using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BIMCanvas.Server.Services.ProjectHealth.Checks
{
    /// <summary>
    /// Phase 0 字段重命名检查：
    ///   semantic_plan.json:
    ///     - 顶层 Versions → Entries
    ///     - 条目内 Version → Tag
    ///     - 条目内 ReferenceAnalysisVersion → ReferenceAnalysisTag
    ///   reference_analysis.json:
    ///     - 顶层数组，每条目 Version → Tag
    /// 值不变（Phase 0 只做字段重命名，不动值；
    /// semantic_plan tag 值的语义化映射由 Phase D 的 SemanticPlanTagValueCheck 处理）。
    /// </summary>
    public class SemanticPlanTagCheck : IProjectHealthCheck
    {
        public string Id => "phase0-tag";
        public string Description => "Phase 0：semantic_plan / reference_analysis 字段重命名（Version → Tag）";

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
                    var status = InspectSemanticPlan(filePath);
                    if (status != null)
                        result.Issues.Add(new HealthIssue
                        {
                            RelativePath = ToRelative(projectPath, filePath),
                            IssueType = status.Value.issueType,
                            Description = status.Value.description
                        });
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{ToRelative(projectPath, filePath)}: {ex.Message}");
                }
            }

            foreach (var filePath in Directory.GetFiles(schemesPath, "reference_analysis.json", SearchOption.AllDirectories))
            {
                try
                {
                    var status = InspectReferenceAnalysis(filePath);
                    if (status != null)
                        result.Issues.Add(new HealthIssue
                        {
                            RelativePath = ToRelative(projectPath, filePath),
                            IssueType = status.Value.issueType,
                            Description = status.Value.description
                        });
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

            foreach (var filePath in Directory.GetFiles(schemesPath, "reference_analysis.json", SearchOption.AllDirectories))
            {
                try
                {
                    var status = MigrateReferenceAnalysis(filePath);
                    Record(status, ToRelative(projectPath, filePath), result);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{ToRelative(projectPath, filePath)}: {ex.Message}");
                }
            }

            return result;
        }

        // ---------- semantic_plan.json ----------

        private static (string issueType, string description)? InspectSemanticPlan(string filePath)
        {
            var content = File.ReadAllText(filePath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(content)) return null;

            var root = JToken.Parse(content);
            if (root is not JObject obj) return null;

            if (obj.ContainsKey("Versions"))
                return ("legacy-version-field", "顶层有 Versions 字段（应改为 Entries），条目里 Version → Tag");

            return null;
        }

        private static MigrateStatus MigrateSemanticPlan(string filePath)
        {
            var content = File.ReadAllText(filePath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(content))
                return MigrateStatus.SkippedEmpty;

            var root = JToken.Parse(content);
            if (root is not JObject obj)
                throw new InvalidOperationException("semantic_plan.json 顶层不是 JSON 对象");

            var hasOldVersions = obj.ContainsKey("Versions");
            var hasNewEntries = obj.ContainsKey("Entries");
            if (!hasOldVersions && hasNewEntries)
                return MigrateStatus.SkippedAlready;
            if (!hasOldVersions && !hasNewEntries)
                return MigrateStatus.SkippedEmpty;

            var versionsArray = obj["Versions"] as JArray
                                ?? throw new InvalidOperationException("Versions 字段不是数组");

            var entriesArray = new JArray();
            foreach (var item in versionsArray)
            {
                if (item is not JObject entry)
                    throw new InvalidOperationException("Versions 条目不是对象");
                RenameProperty(entry, "Version", "Tag");
                RenameProperty(entry, "ReferenceAnalysisVersion", "ReferenceAnalysisTag");
                entriesArray.Add(entry);
            }

            var newRoot = new JObject();
            if (obj["referenceAnalysis"] != null)
                newRoot["referenceAnalysis"] = obj["referenceAnalysis"];
            newRoot["Entries"] = entriesArray;

            WriteAtomic(filePath, newRoot);
            return MigrateStatus.Migrated;
        }

        // ---------- reference_analysis.json ----------

        private static (string issueType, string description)? InspectReferenceAnalysis(string filePath)
        {
            var content = File.ReadAllText(filePath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(content)) return null;

            var root = JToken.Parse(content);
            if (root is not JArray arr || arr.Count == 0) return null;

            var first = arr[0] as JObject;
            if (first == null) return null;
            if (first.ContainsKey("Version") && !first.ContainsKey("Tag"))
                return ("legacy-version-field", "条目里 Version → Tag");

            return null;
        }

        private static MigrateStatus MigrateReferenceAnalysis(string filePath)
        {
            var content = File.ReadAllText(filePath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(content))
                return MigrateStatus.SkippedEmpty;

            var root = JToken.Parse(content);
            if (root is not JArray arr)
                throw new InvalidOperationException("reference_analysis.json 顶层不是 JSON 数组");
            if (arr.Count == 0)
                return MigrateStatus.SkippedEmpty;

            var first = arr[0] as JObject
                        ?? throw new InvalidOperationException("reference_analysis.json 条目不是对象");
            if (first.ContainsKey("Tag") && !first.ContainsKey("Version"))
                return MigrateStatus.SkippedAlready;

            foreach (var item in arr)
            {
                if (item is not JObject entry)
                    throw new InvalidOperationException("reference_analysis.json 条目不是对象");
                RenameProperty(entry, "Version", "Tag");
            }

            WriteAtomic(filePath, arr);
            return MigrateStatus.Migrated;
        }

        // ---------- helpers ----------

        private static void RenameProperty(JObject obj, string oldName, string newName)
        {
            if (!obj.ContainsKey(oldName)) return;
            if (obj.ContainsKey(newName)) { obj.Remove(oldName); return; }
            var value = obj[oldName]!;
            obj.Remove(oldName);
            obj[newName] = value;
        }

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
