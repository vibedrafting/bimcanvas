using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BIMCanvas.Scripts.MigrateSemanticPlanTag;

/// <summary>
/// 把 .bcp 项目下的 semantic_plan.json / reference_analysis.json 从旧字段名（Versions/Version/ReferenceAnalysisVersion）
/// 迁移到 Phase 0 新字段名（Entries/Tag/ReferenceAnalysisTag）。
/// 独立 CLI 工具，不嵌入 Server 启动流程，不暴露为 endpoint。
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args.Any(a => a == "-h" || a == "--help"))
        {
            PrintUsage();
            return 0;
        }

        var projectPath = args[0];
        var dryRun = args.Skip(1).Any(a => a == "--dry-run");

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
        Console.WriteLine();

        int migrated = 0;
        int skipped = 0;
        int errors = 0;

        // 1. semantic_plan.json
        var planFiles = Directory.GetFiles(schemesPath, "semantic_plan.json", SearchOption.AllDirectories);
        foreach (var filePath in planFiles)
        {
            var relative = Path.GetRelativePath(projectPath, filePath);
            try
            {
                var status = MigrateSemanticPlan(filePath, dryRun);
                ReportStatus(status, relative, ref migrated, ref skipped);
            }
            catch (Exception ex)
            {
                errors++;
                Console.Error.WriteLine($"  [ERROR]    {relative}: {ex.Message}");
            }
        }

        // 2. reference_analysis.json
        var refFiles = Directory.GetFiles(schemesPath, "reference_analysis.json", SearchOption.AllDirectories);
        foreach (var filePath in refFiles)
        {
            var relative = Path.GetRelativePath(projectPath, filePath);
            try
            {
                var status = MigrateReferenceAnalysis(filePath, dryRun);
                ReportStatus(status, relative, ref migrated, ref skipped);
            }
            catch (Exception ex)
            {
                errors++;
                Console.Error.WriteLine($"  [ERROR]    {relative}: {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("=== 迁移摘要 ===");
        Console.WriteLine($"  已迁移：{migrated}");
        Console.WriteLine($"  已跳过：{skipped}");
        Console.WriteLine($"  错误数：{errors}");

        return errors > 0 ? 1 : 0;
    }

    private static void ReportStatus(MigrateStatus status, string relative, ref int migrated, ref int skipped)
    {
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

    /// <summary>
    /// semantic_plan.json schema 迁移：
    ///   Versions → Entries（数组容器键）
    ///   条目内 Version → Tag
    ///   条目内 ReferenceAnalysisVersion → ReferenceAnalysisTag
    /// 顶层 referenceAnalysis（LegacyEmbedded）字段保持不动。
    /// </summary>
    private static MigrateStatus MigrateSemanticPlan(string filePath, bool dryRun)
    {
        var content = File.ReadAllText(filePath, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(content))
            return MigrateStatus.SkippedEmpty;

        var root = JToken.Parse(content);
        if (root is not JObject obj)
            throw new InvalidOperationException("semantic_plan.json 顶层不是 JSON 对象");

        // 已是新 schema（含 Entries 且不含 Versions）→ 跳过
        var hasOldVersions = obj.ContainsKey("Versions");
        var hasNewEntries = obj.ContainsKey("Entries");
        if (!hasOldVersions && hasNewEntries)
            return MigrateStatus.SkippedAlready;
        if (!hasOldVersions && !hasNewEntries)
            return MigrateStatus.SkippedEmpty;  // 既无旧也无新，可能空文件

        // 容器键 Versions → Entries
        var versionsArray = obj["Versions"] as JArray;
        if (versionsArray == null)
            throw new InvalidOperationException("Versions 字段不是数组");

        var entriesArray = new JArray();
        foreach (var item in versionsArray)
        {
            if (item is not JObject entry)
                throw new InvalidOperationException("Versions 条目不是对象");

            // 条目内字段重命名
            RenameProperty(entry, "Version", "Tag");
            RenameProperty(entry, "ReferenceAnalysisVersion", "ReferenceAnalysisTag");

            entriesArray.Add(entry);
        }

        var newRoot = new JObject();
        // 保留顶层 referenceAnalysis（LegacyEmbedded）字段
        if (obj["referenceAnalysis"] != null)
            newRoot["referenceAnalysis"] = obj["referenceAnalysis"];
        newRoot["Entries"] = entriesArray;

        if (!dryRun)
            WriteAtomic(filePath, newRoot);

        return MigrateStatus.Migrated;
    }

    /// <summary>
    /// reference_analysis.json schema 迁移：顶层数组，每条目内 Version → Tag。
    /// </summary>
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

        // 已是新 schema（任一条目含 Tag 字段且不含 Version）→ 跳过
        var first = arr[0] as JObject;
        if (first == null)
            throw new InvalidOperationException("reference_analysis.json 条目不是对象");
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

    /// <summary>
    /// 重命名 JObject 中的字段，保留值不变；目标字段已存在则不覆盖；源字段不存在则跳过。
    /// 保持字段在 JSON 输出中的相对顺序（移除 + 末尾追加）。
    /// </summary>
    private static void RenameProperty(JObject obj, string oldName, string newName)
    {
        if (!obj.ContainsKey(oldName))
            return;
        if (obj.ContainsKey(newName))
        {
            // 已有新字段，直接删旧字段
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
        Console.WriteLine("BIMCanvas semantic_plan / reference_analysis 字段重命名工具（Phase 0）");
        Console.WriteLine();
        Console.WriteLine("用法:");
        Console.WriteLine("  dotnet run --project BIMCanvas.Server\\Scripts\\MigrateSemanticPlanTag -- <project-path> [--dry-run]");
        Console.WriteLine();
        Console.WriteLine("行为:");
        Console.WriteLine("  semantic_plan.json:");
        Console.WriteLine("    Versions → Entries（数组容器键）");
        Console.WriteLine("    条目内 Version → Tag");
        Console.WriteLine("    条目内 ReferenceAnalysisVersion → ReferenceAnalysisTag");
        Console.WriteLine("    顶层 referenceAnalysis（LegacyEmbedded）保持不动");
        Console.WriteLine("  reference_analysis.json:");
        Console.WriteLine("    每条目 Version → Tag");
        Console.WriteLine();
        Console.WriteLine("注意事项:");
        Console.WriteLine("  1. 必须先 git 存档项目目录。");
        Console.WriteLine("  2. 必须先部署 Server Phase 0 适配版本再跑。");
        Console.WriteLine("  3. 多个 .bcp 项目分别执行。");
        Console.WriteLine("  4. --dry-run 仅预演不写入。");
    }

    private enum MigrateStatus
    {
        Migrated,
        SkippedAlready,
        SkippedEmpty
    }
}
