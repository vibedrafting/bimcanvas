using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BIMCanvas.Scripts.MigrateProjectSchema;

/// <summary>
/// BIMCanvas .bcp 项目 schema 一次性清洗工具。
/// 合并 Phase 0（version → tag 字段重命名）+ Phase 0b（modules.json wrapper 升级）。
/// 独立 CLI，不嵌入 Server。
/// </summary>
internal static class Program
{
    private static readonly Regex VariantFilenameRegex = new(
        @"^modules-(?<variantId>[A-Za-z0-9_\-]+)\.json$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static int Main(string[] args)
    {
        if (args.Length == 0 || args.Any(a => a == "-h" || a == "--help"))
        {
            PrintUsage();
            return 0;
        }

        var projectPath = args[0];
        var dryRun = args.Skip(1).Any(a => a == "--dry-run");
        var onlyArg = args.Skip(1).FirstOrDefault(a => a.StartsWith("--only="))?["--only=".Length..];
        var only = onlyArg?.ToLowerInvariant();

        if (only != null && only != "tag" && only != "wrapper")
        {
            Console.Error.WriteLine($"[Error] --only 只接受 'tag' 或 'wrapper'，收到: {only}");
            return 2;
        }

        if (!Directory.Exists(projectPath))
        {
            Console.Error.WriteLine($"[Error] 项目目录不存在: {projectPath}");
            return 2;
        }

        var schemesPath = Path.Combine(projectPath, "schemes");
        if (!Directory.Exists(schemesPath))
        {
            Console.Error.WriteLine($"[Error] schemes 目录不存在: {schemesPath}");
            return 2;
        }

        Console.WriteLine($"[Migrate] 项目: {projectPath}");
        Console.WriteLine($"[Migrate] 模式: {(dryRun ? "dry-run（不写入）" : "实际迁移")}");
        Console.WriteLine($"[Migrate] 范围: {only ?? "全部（Phase 0 tag + Phase 0b wrapper）"}");
        Console.WriteLine();

        int migrated = 0;
        int skipped = 0;
        int errors = 0;
        var errorFiles = new List<string>();

        // ============================================================
        // Phase 0: semantic_plan.json / reference_analysis.json 字段重命名
        // ============================================================
        if (only is null or "tag")
        {
            Console.WriteLine("--- Phase 0: semantic_plan / reference_analysis 字段重命名 ---");

            foreach (var filePath in Directory.GetFiles(schemesPath, "semantic_plan.json", SearchOption.AllDirectories))
            {
                ProcessFile(filePath, projectPath, MigrateSemanticPlan, dryRun, ref migrated, ref skipped, ref errors, errorFiles);
            }
            foreach (var filePath in Directory.GetFiles(schemesPath, "reference_analysis.json", SearchOption.AllDirectories))
            {
                ProcessFile(filePath, projectPath, MigrateReferenceAnalysis, dryRun, ref migrated, ref skipped, ref errors, errorFiles);
            }
            Console.WriteLine();
        }

        // ============================================================
        // Phase 0b: modules.json / modules-*.json 裸数组 → wrapper
        // ============================================================
        if (only is null or "wrapper")
        {
            Console.WriteLine("--- Phase 0b: modules.json wrapper 升级 ---");

            foreach (var filePath in Directory.GetFiles(schemesPath, "modules.json", SearchOption.AllDirectories))
            {
                ProcessFile(filePath, projectPath, MigrateModules, dryRun, ref migrated, ref skipped, ref errors, errorFiles);
            }
            foreach (var filePath in Directory.GetFiles(schemesPath, "modules-*.json", SearchOption.AllDirectories)
                                              .Where(f => !f.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase)))
            {
                ProcessFile(filePath, projectPath, MigrateModules, dryRun, ref migrated, ref skipped, ref errors, errorFiles);
            }
            Console.WriteLine();
        }

        Console.WriteLine("=== 迁移摘要 ===");
        Console.WriteLine($"  已迁移：{migrated}");
        Console.WriteLine($"  已跳过：{skipped}");
        Console.WriteLine($"  错误数：{errors}");
        if (errors > 0)
        {
            Console.WriteLine();
            Console.WriteLine("错误文件列表：");
            foreach (var line in errorFiles)
                Console.WriteLine($"  - {line}");
        }

        return errors > 0 ? 1 : 0;
    }

    private delegate MigrateStatus FileMigrator(string filePath, bool dryRun);

    private static void ProcessFile(
        string filePath,
        string projectPath,
        FileMigrator migrator,
        bool dryRun,
        ref int migrated,
        ref int skipped,
        ref int errors,
        List<string> errorFiles)
    {
        var relative = Path.GetRelativePath(projectPath, filePath);
        try
        {
            var status = migrator(filePath, dryRun);
            switch (status)
            {
                case MigrateStatus.Migrated:
                    migrated++;
                    Console.WriteLine($"  [MIGRATED] {relative}");
                    break;
                case MigrateStatus.SkippedAlready:
                    skipped++;
                    Console.WriteLine($"  [SKIP]     {relative}（已是新 schema）");
                    break;
                case MigrateStatus.SkippedEmpty:
                    skipped++;
                    Console.WriteLine($"  [SKIP]     {relative}（空文件）");
                    break;
            }
        }
        catch (Exception ex)
        {
            errors++;
            errorFiles.Add($"{relative}: {ex.Message}");
            Console.Error.WriteLine($"  [ERROR]    {relative}: {ex.Message}");
        }
    }

    // ------------------------------------------------------------
    // Phase 0: semantic_plan.json
    //   Versions → Entries（数组容器键）
    //   条目内 Version → Tag
    //   条目内 ReferenceAnalysisVersion → ReferenceAnalysisTag
    //   顶层 referenceAnalysis（LegacyEmbedded）保持不动
    // ------------------------------------------------------------
    private static MigrateStatus MigrateSemanticPlan(string filePath, bool dryRun)
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

        if (!dryRun)
            WriteAtomic(filePath, newRoot);
        return MigrateStatus.Migrated;
    }

    // ------------------------------------------------------------
    // Phase 0: reference_analysis.json
    //   顶层数组，每条目 Version → Tag
    // ------------------------------------------------------------
    private static MigrateStatus MigrateReferenceAnalysis(string filePath, bool dryRun)
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

        if (!dryRun)
            WriteAtomic(filePath, arr);
        return MigrateStatus.Migrated;
    }

    // ------------------------------------------------------------
    // Phase 0b: modules.json / modules-*.json
    //   裸数组 → wrapper {schemeMetadata, modules}
    //   旧 wrapper {summary, modules} → {schemeMetadata: {summary,...}, modules}
    // ------------------------------------------------------------
    private static MigrateStatus MigrateModules(string filePath, bool dryRun)
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
                ["schemeMetadata"] = JObject.FromObject(schemeMetadata),
                ["modules"] = obj["modules"]!
            };
            if (!dryRun)
                WriteAtomic(filePath, newWrapper);
            return MigrateStatus.Migrated;
        }

        if (token is JArray arr)
        {
            var schemeMetadata = BuildSchemeMetadata(filePath, summary: string.Empty);
            var wrapper = new JObject
            {
                ["schemeMetadata"] = JObject.FromObject(schemeMetadata),
                ["modules"] = arr
            };
            if (!dryRun)
                WriteAtomic(filePath, wrapper);
            return MigrateStatus.Migrated;
        }

        throw new InvalidOperationException("文件既不是裸数组也不是 wrapper，无法识别");
    }

    private static SchemeMetadataDto BuildSchemeMetadata(string filePath, string summary)
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

        return new SchemeMetadataDto
        {
            Summary = summary ?? string.Empty,
            VariantSlug = variantSlug,
            AdoptedAt = null,
            SourceWorkflow = sourceWorkflow
        };
    }

    // ------------------------------------------------------------
    // 共用 helpers
    // ------------------------------------------------------------
    private static void RenameProperty(JObject obj, string oldName, string newName)
    {
        if (!obj.ContainsKey(oldName))
            return;
        if (obj.ContainsKey(newName))
        {
            obj.Remove(oldName);
            return;
        }
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

    private static void PrintUsage()
    {
        Console.WriteLine("BIMCanvas .bcp 项目 schema 一次性清洗工具");
        Console.WriteLine("  - Phase 0  semantic_plan / reference_analysis 字段重命名（Version → Tag 等）");
        Console.WriteLine("  - Phase 0b modules.json 裸数组 → wrapper {schemeMetadata, modules}");
        Console.WriteLine();
        Console.WriteLine("用法:");
        Console.WriteLine("  dotnet run --project BIMCanvas.Server\\Scripts\\MigrateProjectSchema -- <project-path> [选项]");
        Console.WriteLine();
        Console.WriteLine("选项:");
        Console.WriteLine("  --dry-run         预演不写入");
        Console.WriteLine("  --only=tag        只跑 Phase 0（semantic_plan / reference_analysis）");
        Console.WriteLine("  --only=wrapper    只跑 Phase 0b（modules.json wrapper）");
        Console.WriteLine();
        Console.WriteLine("注意:");
        Console.WriteLine("  1. 必须先 git 存档项目目录（脚本不主动 commit）。");
        Console.WriteLine("  2. 必须先部署 Phase 0 + 0b 适配的 Server 版本再跑。");
        Console.WriteLine("  3. 多个 .bcp 项目分别执行。");
    }

    private enum MigrateStatus
    {
        Migrated,
        SkippedAlready,
        SkippedEmpty
    }

    private sealed class SchemeMetadataDto
    {
        [JsonProperty("summary")]
        public string Summary { get; set; } = string.Empty;

        [JsonProperty("variantSlug")]
        public string? VariantSlug { get; set; }

        [JsonProperty("adoptedAt")]
        public string? AdoptedAt { get; set; }

        [JsonProperty("sourceWorkflow")]
        public string SourceWorkflow { get; set; } = "single-plan";
    }
}
