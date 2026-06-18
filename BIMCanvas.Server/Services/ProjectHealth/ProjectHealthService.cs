using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace BIMCanvas.Server.Services.ProjectHealth
{
    /// <summary>
    /// 项目健康检查 + 修复总调度。按 DI 注册顺序依次执行各 IProjectHealthCheck。
    /// Server 走带 IGitCommitter 的构造；CLI 不引 GitWorktreeService，传 null 即可。
    /// </summary>
    public class ProjectHealthService
    {
        private readonly IReadOnlyList<IProjectHealthCheck> _checks;
        private readonly IGitCommitter? _gitCommitter;
        private readonly ILogger<ProjectHealthService> _logger;

        public ProjectHealthService(
            IEnumerable<IProjectHealthCheck> checks,
            IGitCommitter? gitCommitter,
            ILogger<ProjectHealthService> logger)
        {
            _checks = checks.ToList();
            _gitCommitter = gitCommitter;
            _logger = logger;
        }

        /// <summary>已注册 check 的元信息（id + 描述），供前端渲染勾选项。</summary>
        public IReadOnlyList<HealthCheckInfo> ListChecks()
        {
            return _checks.Select(c => new HealthCheckInfo { Id = c.Id, Description = c.Description }).ToList();
        }

        /// <summary>按 id 过滤待跑 check；checkIds 为 null/空 → 全部。未知 id 静默忽略。</summary>
        private IEnumerable<IProjectHealthCheck> SelectChecks(IEnumerable<string>? checkIds)
        {
            if (checkIds == null)
                return _checks;
            var set = new HashSet<string>(checkIds, StringComparer.Ordinal);
            if (set.Count == 0)
                return _checks;
            return _checks.Where(c => set.Contains(c.Id));
        }

        /// <summary>
        /// 只查不改：遍历选中 check 跑 Inspect，聚合 issues。checkIds 为 null → 全部。
        /// </summary>
        public ProjectInspectionReport InspectAll(string projectPath, IEnumerable<string>? checkIds = null)
        {
            ValidateProjectPath(projectPath);

            var report = new ProjectInspectionReport();
            foreach (var check in SelectChecks(checkIds))
            {
                CheckInspectionResult result;
                try
                {
                    result = check.Inspect(projectPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[ProjectHealth] {CheckId} Inspect 失败", check.Id);
                    result = new CheckInspectionResult
                    {
                        CheckId = check.Id,
                        CheckDescription = check.Description,
                        Errors = { $"Inspect 失败: {ex.Message}" }
                    };
                }
                report.Checks.Add(result);
                report.TotalIssues += result.Issues.Count;
            }
            return report;
        }

        /// <summary>
        /// 实际修复：
        ///   1. autoGitCommit=true 时先调 IGitCommitter.TryCommit 存档；项目非 git 仓库会抛 InvalidOperationException
        ///   2. 按 _checks 顺序跑各 Repair；单个 check 抛异常 → 记入 Errors 但继续后续 check
        /// </summary>
        public ProjectRepairReport RepairAll(string projectPath, bool autoGitCommit = true, IEnumerable<string>? checkIds = null)
        {
            ValidateProjectPath(projectPath);

            var selectedChecks = SelectChecks(checkIds).ToList();
            var report = new ProjectRepairReport();

            if (autoGitCommit && _gitCommitter != null)
            {
                var snapshotMessage = $"schema-repair-snapshot-{DateTime.Now:yyyyMMddHHmmss}";
                try
                {
                    var didCommit = _gitCommitter.TryCommit(projectPath, snapshotMessage, out var commitHash);
                    report.SnapshotCommitHash = didCommit ? commitHash : null;
                    _logger.LogInformation(
                        "[ProjectHealth] 修复前存档: {Result}（{Hash}）",
                        didCommit ? "已 commit" : "工作区干净，跳过",
                        commitHash ?? "-");
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"修复前自动 git 存档失败：{ex.Message}。请确认项目是 git 仓库且没有冲突。", ex);
                }
            }

            foreach (var check in selectedChecks)
            {
                CheckRepairResult result;
                try
                {
                    result = check.Repair(projectPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[ProjectHealth] {CheckId} Repair 失败", check.Id);
                    result = new CheckRepairResult
                    {
                        CheckId = check.Id,
                        CheckDescription = check.Description,
                        Errors = { $"Repair 失败: {ex.Message}" }
                    };
                }
                report.Checks.Add(result);
            }

            return report;
        }

        private static void ValidateProjectPath(string projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
                throw new ArgumentException("projectPath 必填", nameof(projectPath));
            if (!Directory.Exists(projectPath))
                throw new DirectoryNotFoundException($"项目目录不存在: {projectPath}");
        }
    }
}
