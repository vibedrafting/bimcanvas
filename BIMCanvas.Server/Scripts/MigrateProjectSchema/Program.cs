using System;
using System.Linq;
using BIMCanvas.Server.Services.ProjectHealth;
using BIMCanvas.Server.Services.ProjectHealth.Checks;
using Microsoft.Extensions.Logging.Abstractions;

namespace BIMCanvas.Scripts.MigrateProjectSchema;

/// <summary>
/// BIMCanvas .bcp 项目 schema 一次性清洗 CLI 工具。
/// 通过 Compile Include 共享 Server 的 ProjectHealth 服务代码——CLI 和 Server endpoint 复用同一份核心算法。
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
        var only = args.Skip(1).FirstOrDefault(a => a.StartsWith("--only="))?["--only=".Length..]?.ToLowerInvariant();

        if (only != null && only != "tag" && only != "wrapper" && only != "tagvalue" && only != "slim" && only != "pointer")
        {
            Console.Error.WriteLine($"[Error] --only 只接受 'tag' / 'wrapper' / 'tagvalue' / 'slim' / 'pointer'，收到: {only}");
            return 2;
        }

        // 按 plan 顺序：Phase 0 字段重命名 → Phase 0b wrapper 升级 → Phase E schemeMetadata 瘦身 → Phase D tag 值语义化
        // → 指针模型迁移（必须最后跑：依赖 wrapper/tag 值已就位，再把 canonical→main/ + semantic_plan→DESIGN.md）
        IProjectHealthCheck[] allChecks =
        {
            new SemanticPlanTagCheck(),
            new ModulesWrapperCheck(),
            new SchemeMetadataSlimCheck(),
            new SemanticPlanTagValueCheck(),
            new PointerModelMigrateCheck()
        };
        var checks = only switch
        {
            "tag" => allChecks.Where(c => c.Id == "phase0-tag"),
            "wrapper" => allChecks.Where(c => c.Id == "phase0b-wrapper"),
            "slim" => allChecks.Where(c => c.Id == "phase-e-metadata-slim"),
            "tagvalue" => allChecks.Where(c => c.Id == "phase-d-tag-value"),
            "pointer" => allChecks.Where(c => c.Id == "pointer-model"),
            _ => allChecks
        };

        var service = new ProjectHealthService(
            checks,
            gitCommitter: null,   // CLI 不接 git 兜底，用户须自行 git 存档
            logger: NullLogger<ProjectHealthService>.Instance);

        Console.WriteLine($"[Migrate] 项目: {projectPath}");
        Console.WriteLine($"[Migrate] 模式: {(dryRun ? "dry-run（只检查不修复）" : "实际迁移")}");
        Console.WriteLine($"[Migrate] 范围: {only ?? "全部"}");
        Console.WriteLine();

        try
        {
            if (dryRun)
            {
                var report = service.InspectAll(projectPath);
                PrintInspection(report);
                return report.Checks.Any(c => c.Errors.Count > 0) ? 1 : 0;
            }
            else
            {
                var report = service.RepairAll(projectPath, autoGitCommit: false);
                PrintRepair(report);
                return report.Checks.Any(c => c.Errors.Count > 0) ? 1 : 0;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Error] {ex.Message}");
            return 2;
        }
    }

    private static void PrintInspection(ProjectInspectionReport report)
    {
        foreach (var check in report.Checks)
        {
            Console.WriteLine($"--- {check.CheckDescription} ---");
            if (check.Issues.Count == 0 && check.Errors.Count == 0)
                Console.WriteLine("  无问题");
            foreach (var issue in check.Issues)
                Console.WriteLine($"  [{issue.IssueType}] {issue.RelativePath} — {issue.Description}");
            foreach (var err in check.Errors)
                Console.Error.WriteLine($"  [ERROR] {err}");
            Console.WriteLine();
        }
        Console.WriteLine("=== 检查摘要 ===");
        Console.WriteLine($"  发现问题：{report.TotalIssues}");
    }

    private static void PrintRepair(ProjectRepairReport report)
    {
        int totalMigrated = 0, totalSkipped = 0, totalErrors = 0;
        foreach (var check in report.Checks)
        {
            Console.WriteLine($"--- {check.CheckDescription} ---");
            foreach (var path in check.Migrated)
                Console.WriteLine($"  [MIGRATED] {path}");
            foreach (var path in check.Skipped)
                Console.WriteLine($"  [SKIP]     {path}");
            foreach (var err in check.Errors)
                Console.Error.WriteLine($"  [ERROR]    {err}");
            totalMigrated += check.Migrated.Count;
            totalSkipped += check.Skipped.Count;
            totalErrors += check.Errors.Count;
            Console.WriteLine();
        }
        Console.WriteLine("=== 迁移摘要 ===");
        Console.WriteLine($"  已迁移：{totalMigrated}");
        Console.WriteLine($"  已跳过：{totalSkipped}");
        Console.WriteLine($"  错误数：{totalErrors}");
    }

    private static void PrintUsage()
    {
        Console.WriteLine("BIMCanvas .bcp 项目 schema 一次性清洗工具");
        Console.WriteLine("  - Phase 0  semantic_plan / reference_analysis 字段重命名");
        Console.WriteLine("  - Phase 0b modules.json 裸数组 → wrapper {schemeMetadata, modules}");
        Console.WriteLine("  - Phase E  schemeMetadata 瘦身（删 variantSlug/adoptedAt/sourceWorkflow，保留 summary）");
        Console.WriteLine("  - Phase D  semantic_plan tag 值语义化（v0.1 → spatial-skeleton 等）");
        Console.WriteLine();
        Console.WriteLine("用法:");
        Console.WriteLine("  dotnet run --project BIMCanvas.Server\\Scripts\\MigrateProjectSchema -- <project-path> [选项]");
        Console.WriteLine();
        Console.WriteLine("选项:");
        Console.WriteLine("  --dry-run         只检查不修复，列出问题清单");
        Console.WriteLine("  --only=tag        只跑 Phase 0（semantic_plan / reference_analysis 字段重命名）");
        Console.WriteLine("  --only=wrapper    只跑 Phase 0b（modules.json wrapper）");
        Console.WriteLine("  --only=slim       只跑 Phase E（schemeMetadata 瘦身）");
        Console.WriteLine("  --only=tagvalue   只跑 Phase D（semantic_plan tag 值映射）");
        Console.WriteLine("  --only=pointer    只跑 指针模型迁移（canonical→{dz}/main/ + semantic_plan/reference→DESIGN.md + 父 adopted:main）");
        Console.WriteLine();
        Console.WriteLine("注意:");
        Console.WriteLine("  1. CLI 不自动 git 存档——请先手动 commit。");
        Console.WriteLine("  2. Server 端入口（首页\"修复\"按钮）会自动 git 存档兜底。");
        Console.WriteLine("  3. 多个 .bcp 项目分别执行。");
    }
}
