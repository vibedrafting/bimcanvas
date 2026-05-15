using System.Collections.Generic;

namespace BIMCanvas.Server.Services.ProjectHealth
{
    /// <summary>
    /// 项目健康检查 + 修复的最小接口。每个具体 schema 升级/数据规整需求实现一个。
    /// 实现要点：
    ///   - 实现只操作 JObject/JArray，**不依赖 BIMCanvas.Core 业务模型**——保证 CLI 通过 Compile Include 共享源码时零依赖
    ///   - Repair 必须原子写入（.tmp + rename），中断不损坏现有文件
    ///   - Inspect 不写盘
    /// </summary>
    public interface IProjectHealthCheck
    {
        /// <summary>稳定 ID，用于 DTO 中标识来源，如 "phase0-tag" / "phase0b-wrapper"。</summary>
        string Id { get; }

        /// <summary>中文短描述，用于 UI 展示。</summary>
        string Description { get; }

        /// <summary>只查不改，返回发现的问题列表 + 解析错误。</summary>
        CheckInspectionResult Inspect(string projectPath);

        /// <summary>实际修复。返回已迁移 / 跳过 / 失败的文件清单。</summary>
        CheckRepairResult Repair(string projectPath);
    }

    public class HealthIssue
    {
        /// <summary>相对项目根目录的路径，前端展示用。</summary>
        public string RelativePath { get; set; } = "";

        /// <summary>问题类型常量，如 "legacy-version-field" / "naked-array" / "missing-scheme-metadata"。</summary>
        public string IssueType { get; set; } = "";

        /// <summary>中文一句话说明，前端可直接渲染。</summary>
        public string Description { get; set; } = "";
    }

    public class CheckInspectionResult
    {
        public string CheckId { get; set; } = "";
        public string CheckDescription { get; set; } = "";
        public List<HealthIssue> Issues { get; set; } = new List<HealthIssue>();
        public List<string> Errors { get; set; } = new List<string>();
    }

    public class CheckRepairResult
    {
        public string CheckId { get; set; } = "";
        public string CheckDescription { get; set; } = "";
        public List<string> Migrated { get; set; } = new List<string>();
        public List<string> Skipped { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();
    }

    public class ProjectInspectionReport
    {
        public List<CheckInspectionResult> Checks { get; set; } = new List<CheckInspectionResult>();
        public int TotalIssues { get; set; }
    }

    public class ProjectRepairReport
    {
        /// <summary>修复前自动 commit 的 hash；工作区已干净或无 git 时为 null。</summary>
        public string? SnapshotCommitHash { get; set; }

        public List<CheckRepairResult> Checks { get; set; } = new List<CheckRepairResult>();
    }

    /// <summary>
    /// Git commit 抽象。Server 端用 GitWorktreeServiceCommitter 桥接到 GitWorktreeService；
    /// CLI 不引入 GitWorktreeService 依赖，直接传 null 即可。
    /// </summary>
    public interface IGitCommitter
    {
        /// <summary>
        /// 在 workingDir 跑 git add . + commit。工作区干净返回 false 不抛错；非 0 git 错误抛 InvalidOperationException。
        /// </summary>
        bool TryCommit(string workingDir, string message, out string? commitHash);
    }
}
