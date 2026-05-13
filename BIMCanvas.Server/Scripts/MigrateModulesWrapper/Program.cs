using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BIMCanvas.Scripts.MigrateModulesWrapper;

/// <summary>
/// 把 .bcp 项目下的 modules.json / modules-*.json 从裸数组迁移到 wrapper 形态。
/// Phase 0b 独立 CLI 工具，不嵌入 Server 启动流程，不暴露为 endpoint。
/// </summary>
internal static class Program
{
    // modules-{vid}.json 文件名匹配（用于从文件名推断 variantSlug）
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

        var canonicalFiles = Directory.GetFiles(schemesPath, "modules.json", SearchOption.AllDirectories);
        var variantFiles = Directory.GetFiles(schemesPath, "modules-*.json", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        int migrated = 0;
        int skipped = 0;
        int errors = 0;
        var errorFiles = new List<string>();

        foreach (var filePath in canonicalFiles.Concat(variantFiles))
        {
            var relative = Path.GetRelativePath(projectPath, filePath);
            try
            {
                var status = MigrateFile(filePath, dryRun);
                switch (status)
                {
                    case MigrateStatus.Migrated:
                        migrated++;
                        Console.WriteLine($"  [MIGRATED] {relative}");
                        break;
                    case MigrateStatus.SkippedAlreadyWrapper:
                        skipped++;
                        Console.WriteLine($"  [SKIP]     {relative}（已是 wrapper）");
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

        Console.WriteLine();
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

    private static MigrateStatus MigrateFile(string filePath, bool dryRun)
    {
        var content = File.ReadAllText(filePath, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(content))
            return MigrateStatus.SkippedEmpty;

        var token = JToken.Parse(content);

        // 已是 wrapper（含 modules 数组）→ 仅在缺 schemeMetadata 时补齐
        if (token is JObject obj && obj["modules"] is JArray)
        {
            if (obj["schemeMetadata"] is JObject)
                return MigrateStatus.SkippedAlreadyWrapper;

            // 已有 modules 但缺 schemeMetadata（如 VariantController 旧 wrapper {summary, modules}）→ 升级
            var oldSummary = obj.Value<string>("summary") ?? string.Empty;
            var schemeMetadata = BuildSchemeMetadata(filePath, oldSummary);
            var newWrapper = new JObject
            {
                ["schemeMetadata"] = JObject.FromObject(schemeMetadata),
                ["modules"] = obj["modules"]!
            };
            if (!dryRun)
                WriteWrapperAtomic(filePath, newWrapper);
            return MigrateStatus.Migrated;
        }

        // 裸数组 → 包成 wrapper
        if (token is JArray arr)
        {
            var schemeMetadata = BuildSchemeMetadata(filePath, summary: string.Empty);
            var wrapper = new JObject
            {
                ["schemeMetadata"] = JObject.FromObject(schemeMetadata),
                ["modules"] = arr
            };
            if (!dryRun)
                WriteWrapperAtomic(filePath, wrapper);
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
            // 历史 variant 文件无法回查 variant.json.state → 标 "unknown" 占位
            // alt-prev-* 历史归档识别为 prev-adopted
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

    private static void WriteWrapperAtomic(string filePath, JObject wrapper)
    {
        var json = wrapper.ToString(Formatting.Indented);
        var tmpPath = filePath + ".tmp";
        File.WriteAllText(tmpPath, json, Encoding.UTF8);
        File.Move(tmpPath, filePath, overwrite: true);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("BIMCanvas modules.json wrapper 迁移工具（Phase 0b）");
        Console.WriteLine();
        Console.WriteLine("用法:");
        Console.WriteLine("  dotnet run --project BIMCanvas.Server\\Scripts\\MigrateModulesWrapper -- <project-path> [--dry-run]");
        Console.WriteLine();
        Console.WriteLine("行为:");
        Console.WriteLine("  扫描 <project-path>/schemes/**/modules.json 与 modules-*.json，把裸数组包成 wrapper。");
        Console.WriteLine("  已是 wrapper 但缺 schemeMetadata 的文件会补 best-effort 字段后重写。");
        Console.WriteLine();
        Console.WriteLine("注意事项（必读）:");
        Console.WriteLine("  1. 必须先 git 存档项目目录，本脚本不主动 commit。");
        Console.WriteLine("  2. 必须先部署 Server wrapper 适配版本（Phase 0b 之后）再跑——旧 Server 启动会因读不到裸数组挂掉。");
        Console.WriteLine("  3. 多个 .bcp 项目分别执行。");
        Console.WriteLine("  4. --dry-run 仅预演不写入。");
    }

    private enum MigrateStatus
    {
        Migrated,
        SkippedAlreadyWrapper,
        SkippedEmpty
    }

    /// <summary>
    /// 与 BIMCanvas.Server.Models.SchemeMetadata schema 对齐（独立 CLI 不引 Server，本地复刻一份）。
    /// camelCase 序列化由 Newtonsoft.Json 通过 JsonProperty 控制。
    /// </summary>
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
