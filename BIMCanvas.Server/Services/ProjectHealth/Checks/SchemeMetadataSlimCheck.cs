using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BIMCanvas.Server.Services.ProjectHealth.Checks
{
    /// <summary>
    /// Phase E 反向迁移：把 modules.json 的 schemeMetadata 瘦身为只含 summary 一个字段。
    /// 删除 variantSlug / adoptedAt / sourceWorkflow（信息可从路径 / git log 反推 / UI 不区分）。
    /// 运行顺序应在 ModulesWrapperCheck 之后（确保所有 modules.json 已是 wrapper 形态）。
    /// </summary>
    public class SchemeMetadataSlimCheck : IProjectHealthCheck
    {
        public string Id => "phase-e-metadata-slim";
        public string Description => "Phase E：schemeMetadata 瘦身（删 variantSlug / adoptedAt / sourceWorkflow，保留 summary）";

        private static readonly string[] LegacyFields = { "variantSlug", "adoptedAt", "sourceWorkflow" };

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
                            IssueType = "legacy-scheme-metadata-fields",
                            Description = issue
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
                    var status = SlimFile(filePath);
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

        private static string? InspectFile(string filePath)
        {
            var content = File.ReadAllText(filePath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(content)) return null;

            var token = JToken.Parse(content);
            if (token is not JObject obj)
                return null; // 非 wrapper 形态由 ModulesWrapperCheck 处理
            if (obj["schemeMetadata"] is not JObject metadata)
                return null;

            foreach (var field in LegacyFields)
            {
                if (metadata[field] != null)
                    return $"schemeMetadata 含已废弃字段 {field}";
            }
            return null;
        }

        private static SlimStatus SlimFile(string filePath)
        {
            var content = File.ReadAllText(filePath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(content))
                return SlimStatus.SkippedEmpty;

            var token = JToken.Parse(content);
            if (token is not JObject obj)
                return SlimStatus.SkippedNotWrapper;
            if (obj["schemeMetadata"] is not JObject metadata)
                return SlimStatus.SkippedNotWrapper;

            var removed = false;
            foreach (var field in LegacyFields)
            {
                if (metadata.Remove(field))
                    removed = true;
            }

            if (!removed)
                return SlimStatus.SkippedAlready;

            WriteAtomic(filePath, obj);
            return SlimStatus.Slimmed;
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

        private static void Record(SlimStatus status, string relative, CheckRepairResult result)
        {
            switch (status)
            {
                case SlimStatus.Slimmed: result.Migrated.Add(relative); break;
                case SlimStatus.SkippedAlready:
                case SlimStatus.SkippedNotWrapper:
                case SlimStatus.SkippedEmpty: result.Skipped.Add(relative); break;
            }
        }

        private enum SlimStatus { Slimmed, SkippedAlready, SkippedNotWrapper, SkippedEmpty }
    }
}
