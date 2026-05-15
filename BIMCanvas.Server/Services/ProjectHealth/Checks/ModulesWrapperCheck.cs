using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BIMCanvas.Server.Services.ProjectHealth.Checks
{
    /// <summary>
    /// Phase 0b modules.json wrapper 升级检查：
    ///   - 裸数组 → {schemeMetadata, modules}
    ///   - 旧 wrapper {summary, modules} → {schemeMetadata: {summary,...}, modules}
    /// schemeMetadata 字段按文件名 best-effort 派生（canonical → single-plan；alt-prev-* → prev-adopted；其他 → unknown）。
    /// </summary>
    public class ModulesWrapperCheck : IProjectHealthCheck
    {
        public string Id => "phase0b-wrapper";
        public string Description => "Phase 0b：modules.json 裸数组 → {schemeMetadata, modules} wrapper";

        private static readonly Regex VariantFilenameRegex = new Regex(
            @"^modules-(?<variantId>[A-Za-z0-9_\-]+)\.json$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public CheckInspectionResult Inspect(string projectPath)
        {
            var result = new CheckInspectionResult { CheckId = Id, CheckDescription = Description };
            var schemesPath = Path.Combine(projectPath, "schemes");
            if (!Directory.Exists(schemesPath))
                return result;

            foreach (var filePath in EnumerateModuleFiles(schemesPath))
            {
                try
                {
                    var issue = InspectFile(filePath);
                    if (issue != null)
                        result.Issues.Add(new HealthIssue
                        {
                            RelativePath = ToRelative(projectPath, filePath),
                            IssueType = issue.Value.issueType,
                            Description = issue.Value.description
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

            foreach (var filePath in EnumerateModuleFiles(schemesPath))
            {
                try
                {
                    var status = MigrateFile(filePath);
                    Record(status, ToRelative(projectPath, filePath), result);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{ToRelative(projectPath, filePath)}: {ex.Message}");
                }
            }

            return result;
        }

        // ---------- 实现 ----------

        private static (string issueType, string description)? InspectFile(string filePath)
        {
            var content = File.ReadAllText(filePath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(content)) return null;

            var token = JToken.Parse(content);
            if (token is JArray)
                return ("naked-array", "裸数组 modules，需包成 wrapper");

            if (token is JObject obj && obj["modules"] is JArray)
            {
                if (obj["schemeMetadata"] is not JObject)
                    return ("missing-scheme-metadata", "wrapper 缺 schemeMetadata 字段（可能是旧 {summary, modules} 形态）");
            }
            return null;
        }

        private static MigrateStatus MigrateFile(string filePath)
        {
            var content = File.ReadAllText(filePath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(content))
                return MigrateStatus.SkippedEmpty;

            var token = JToken.Parse(content);

            if (token is JObject obj && obj["modules"] is JArray)
            {
                if (obj["schemeMetadata"] is JObject)
                    return MigrateStatus.SkippedAlready;

                var oldSummary = obj.Value<string>("summary") ?? string.Empty;
                var schemeMetadata = BuildSchemeMetadata(filePath, oldSummary);
                var newWrapper = new JObject
                {
                    ["schemeMetadata"] = schemeMetadata,
                    ["modules"] = obj["modules"]!
                };
                WriteAtomic(filePath, newWrapper);
                return MigrateStatus.Migrated;
            }

            if (token is JArray arr)
            {
                var schemeMetadata = BuildSchemeMetadata(filePath, summary: string.Empty);
                var wrapper = new JObject
                {
                    ["schemeMetadata"] = schemeMetadata,
                    ["modules"] = arr
                };
                WriteAtomic(filePath, wrapper);
                return MigrateStatus.Migrated;
            }

            throw new InvalidOperationException("文件既不是裸数组也不是 wrapper");
        }

        private static JObject BuildSchemeMetadata(string filePath, string summary)
        {
            var fileName = Path.GetFileName(filePath);
            string? variantSlug = null;
            string sourceWorkflow = "single-plan";

            var match = VariantFilenameRegex.Match(fileName);
            if (match.Success)
            {
                variantSlug = match.Groups["variantId"].Value;
                sourceWorkflow = variantSlug.StartsWith("alt-prev-", StringComparison.OrdinalIgnoreCase)
                    ? "prev-adopted"
                    : "unknown";
            }

            return new JObject
            {
                ["summary"] = summary ?? string.Empty,
                ["variantSlug"] = variantSlug,
                ["adoptedAt"] = null,
                ["sourceWorkflow"] = sourceWorkflow
            };
        }

        private static System.Collections.Generic.IEnumerable<string> EnumerateModuleFiles(string schemesPath)
        {
            foreach (var f in Directory.GetFiles(schemesPath, "modules.json", SearchOption.AllDirectories))
                yield return f;
            foreach (var f in Directory.GetFiles(schemesPath, "modules-*.json", SearchOption.AllDirectories))
            {
                if (!f.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase))
                    yield return f;
            }
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
