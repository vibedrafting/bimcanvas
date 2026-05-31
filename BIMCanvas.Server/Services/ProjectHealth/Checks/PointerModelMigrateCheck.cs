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
    /// 指针模型存量迁移（P1 承重墙落地后的一次性数据迁移）。每个设计区 schemes/{dz}/：
    ///   ① 几何：legacy canonical modules（{dz}/[{leaf}/]modules.json，排除 variants/ 与 main/）
    ///      → {dz}/main/{相对路径}，并写父 {dz}/DESIGN.md frontmatter adopted: main
    ///   ② 合同：semantic_plan.json 的 tag → DESIGN.md 正文节：
    ///      spatial-skeleton → 父 {dz}/DESIGN.md（§3.2：父存客观骨架）
    ///      strategic-plan / construction-brief → {dz}/main/DESIGN.md（§3.2：方案存战略/简报）
    ///   ③ reference_analysis.json 最新定稿 → 父 DESIGN.md「参考分析·冻结」节（历史交 git，不迁）
    /// 非①范围 tag（multi-plan-overview 等）：告警 + 保留原 semantic_plan.json，不删、不迁（裁决 #10，留人工）。
    /// 幂等：{dz}/DESIGN.md 已存在 → 跳过。原子写入（.tmp + rename）。仅用 JObject/JArray + 文件系统，零 Core/Server 依赖。
    /// </summary>
    public class PointerModelMigrateCheck : IProjectHealthCheck
    {
        public string Id => "pointer-model";
        public string Description => "指针模型迁移：canonical → {dz}/main/ + 父 adopted:main；semantic_plan/reference → DESIGN.md 正文节";

        public CheckInspectionResult Inspect(string projectPath)
        {
            var result = new CheckInspectionResult { CheckId = Id, CheckDescription = Description };
            var schemesPath = Path.Combine(projectPath, "schemes");
            if (!Directory.Exists(schemesPath))
                return result;

            foreach (var dzDir in EnumerateDesignZoneDirs(schemesPath))
            {
                try
                {
                    var dz = Path.GetFileName(dzDir);
                    if (File.Exists(Path.Combine(dzDir, "DESIGN.md")))
                        continue; // 已迁移

                    var legacyModules = FindLegacyCanonicalModules(dzDir);
                    var hasSemantic = File.Exists(Path.Combine(dzDir, "semantic_plan.json"));
                    var hasReference = File.Exists(Path.Combine(dzDir, "reference_analysis.json"));
                    if (legacyModules.Count == 0 && !hasSemantic && !hasReference)
                        continue;

                    var parts = new List<string>();
                    if (legacyModules.Count > 0) parts.Add($"{legacyModules.Count} 个 canonical modules → main/");
                    if (hasSemantic)
                    {
                        var (sk, st, br, multi, unknown) = ParseSemanticPlan(Path.Combine(dzDir, "semantic_plan.json"));
                        var tags = new List<string>();
                        if (sk != null) tags.Add("skeleton→父");
                        if (st != null) tags.Add("strategic→main");
                        if (br != null) tags.Add("brief→main");
                        if (tags.Count > 0) parts.Add("semantic_plan: " + string.Join("/", tags));
                        if (multi) parts.Add("含 multi-plan-overview（保留不迁，需人工）");
                        if (unknown) parts.Add("含未识别 tag（保留不迁）");
                    }
                    if (hasReference) parts.Add("reference_analysis→父冻结节");

                    result.Issues.Add(new HealthIssue
                    {
                        RelativePath = ToRelative(projectPath, dzDir),
                        IssueType = "needs-pointer-migration",
                        Description = string.Join("；", parts)
                    });
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{ToRelative(projectPath, dzDir)}: {ex.Message}");
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

            foreach (var dzDir in EnumerateDesignZoneDirs(schemesPath))
            {
                try
                {
                    MigrateDesignZone(projectPath, dzDir, result);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{ToRelative(projectPath, dzDir)}: {ex.Message}");
                }
            }

            return result;
        }

        // ---------- 迁移单个设计区 ----------

        private void MigrateDesignZone(string projectPath, string dzDir, CheckRepairResult result)
        {
            var dz = Path.GetFileName(dzDir);
            var parentDesignDoc = Path.Combine(dzDir, "DESIGN.md");
            if (File.Exists(parentDesignDoc))
            {
                result.Skipped.Add($"{ToRelative(projectPath, dzDir)} (已迁移)");
                return;
            }

            var legacyModules = FindLegacyCanonicalModules(dzDir);
            var semanticPath = Path.Combine(dzDir, "semantic_plan.json");
            var referencePath = Path.Combine(dzDir, "reference_analysis.json");
            var hasSemantic = File.Exists(semanticPath);
            var hasReference = File.Exists(referencePath);
            if (legacyModules.Count == 0 && !hasSemantic && !hasReference)
                return; // 无可迁移内容

            // ① 移动几何到 main/（保留相对嵌套）
            foreach (var src in legacyModules)
            {
                var rel = Path.GetRelativePath(dzDir, src).Replace('\\', '/');
                var target = Path.Combine(dzDir, "main", rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(src, target, overwrite: true);
                result.Migrated.Add($"{ToRelative(projectPath, src)} → {ToRelative(projectPath, target)}");
            }

            // ② semantic_plan tag 拆分
            string? skeleton = null, strategic = null, brief = null;
            var multiPlanOrUnknown = false;
            if (hasSemantic)
            {
                var parsed = ParseSemanticPlan(semanticPath);
                skeleton = parsed.skeleton; strategic = parsed.strategic; brief = parsed.brief;
                multiPlanOrUnknown = parsed.hasMultiPlan || parsed.hasUnknown;
            }

            // ③ reference 最新定稿
            string? reference = hasReference ? ParseLatestReference(referencePath) : null;

            // 写父 DESIGN.md（adopted:main + skeleton + reference 冻结节）
            WriteAtomicText(parentDesignDoc, BuildParentDesignDoc(dz, skeleton, reference));
            result.Migrated.Add($"{ToRelative(projectPath, parentDesignDoc)} (adopted:main)");

            // 写方案 main/DESIGN.md（strategic + brief）
            var schemeDoc = BuildSchemeDesignDoc(dz, strategic, brief);
            if (schemeDoc != null)
            {
                var schemeDocPath = Path.Combine(dzDir, "main", "DESIGN.md");
                WriteAtomicText(schemeDocPath, schemeDoc);
                result.Migrated.Add($"{ToRelative(projectPath, schemeDocPath)}");
            }

            // 删源（历史交 git）；含非①范围 tag 则保留 semantic_plan 待人工
            if (hasSemantic)
            {
                if (multiPlanOrUnknown)
                    result.Skipped.Add($"{ToRelative(projectPath, semanticPath)} (保留：含 multi-plan-overview/未识别 tag，需人工处理)");
                else
                    File.Delete(semanticPath);
            }
            if (hasReference)
                File.Delete(referencePath);
        }

        // ---------- 解析 ----------

        private static (string? skeleton, string? strategic, string? brief, bool hasMultiPlan, bool hasUnknown)
            ParseSemanticPlan(string path)
        {
            var content = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(content))
                return (null, null, null, false, false);

            if (JToken.Parse(content) is not JObject root || root["Entries"] is not JArray entries)
                return (null, null, null, false, false);

            string? skeleton = null, strategic = null, brief = null;
            bool multi = false, unknown = false;
            foreach (var item in entries)
            {
                if (item is not JObject entry) continue;
                var tag = entry.Value<string>("Tag");
                var text = ExtractContent(entry["Content"]);
                switch (tag)
                {
                    case "spatial-skeleton": case "v0.1": skeleton = text; break;
                    case "strategic-plan": case "v0.2": strategic = text; break;
                    case "construction-brief": case "v0.3": brief = text; break;
                    case "multi-plan-overview": case "v0.2-meta": multi = true; break;
                    default: unknown = true; break;
                }
            }
            return (skeleton, strategic, brief, multi, unknown);
        }

        private static string? ParseLatestReference(string path)
        {
            var content = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(content))
                return null;

            var root = JToken.Parse(content);
            // reference_analysis.json 顶层数组（tag v1/v2/v3…），取最后一条定稿
            if (root is JArray arr && arr.Count > 0)
                return ExtractContent((arr.Last as JObject)?["Content"]) ?? arr.Last?.ToString(Formatting.Indented);
            if (root is JObject obj && obj["Entries"] is JArray entries && entries.Count > 0)
                return ExtractContent((entries.Last as JObject)?["Content"]);
            return content; // 兜底：整体作为冻结快照
        }

        private static string? ExtractContent(JToken? token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;
            return token.Type == JTokenType.String
                ? token.Value<string>()
                : token.ToString(Formatting.Indented);
        }

        // ---------- 构建 DESIGN.md 文本 ----------

        private static string BuildParentDesignDoc(string dz, string? skeleton, string? reference)
        {
            var sb = new StringBuilder();
            sb.Append("---\nadopted: main\n---\n\n");
            sb.Append($"# {dz} · 分区设计合同\n");
            if (!string.IsNullOrWhiteSpace(skeleton))
                sb.Append($"\n## 空间骨架（客观几何·冻结）\n\n{skeleton!.Trim()}\n");
            if (!string.IsNullOrWhiteSpace(reference))
                sb.Append($"\n## 参考分析·冻结输入\n\n{reference!.Trim()}\n");
            return sb.ToString();
        }

        private static string? BuildSchemeDesignDoc(string dz, string? strategic, string? brief)
        {
            if (string.IsNullOrWhiteSpace(strategic) && string.IsNullOrWhiteSpace(brief))
                return null;
            var sb = new StringBuilder();
            sb.Append($"# {dz} · 方案 main\n");
            if (!string.IsNullOrWhiteSpace(strategic))
                sb.Append($"\n## 战略\n\n{strategic!.Trim()}\n");
            if (!string.IsNullOrWhiteSpace(brief))
                sb.Append($"\n## 施工简报\n\n{brief!.Trim()}\n");
            return sb.ToString();
        }

        // ---------- 文件系统辅助 ----------

        /// <summary>schemes/ 下的设计区目录候选 = 不以 "_" 开头的一级子目录（排除 _unzoned 等特殊桶）。</summary>
        private static IEnumerable<string> EnumerateDesignZoneDirs(string schemesPath)
        {
            return Directory.EnumerateDirectories(schemesPath, "*", SearchOption.TopDirectoryOnly)
                .Where(dir =>
                {
                    var name = Path.GetFileName(dir);
                    return !string.IsNullOrWhiteSpace(name) && !name.StartsWith("_", StringComparison.Ordinal);
                });
        }

        /// <summary>{dz}/ 下的 legacy canonical modules.json（相对路径不以 variants/ 或 main/ 开头）。</summary>
        private static List<string> FindLegacyCanonicalModules(string dzDir)
        {
            if (!Directory.Exists(dzDir))
                return new List<string>();
            return Directory.GetFiles(dzDir, "modules.json", SearchOption.AllDirectories)
                .Where(f =>
                {
                    var rel = Path.GetRelativePath(dzDir, f).Replace('\\', '/');
                    return !rel.StartsWith("variants/", StringComparison.OrdinalIgnoreCase)
                        && !rel.StartsWith("main/", StringComparison.OrdinalIgnoreCase)
                        && !rel.Equals("main/modules.json", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();
        }

        private static void WriteAtomicText(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, content, new UTF8Encoding(false));
            File.Move(tmp, path, overwrite: true);
        }

        private static string ToRelative(string projectPath, string filePath)
        {
            return Path.GetRelativePath(projectPath, filePath).Replace('\\', '/');
        }
    }
}
