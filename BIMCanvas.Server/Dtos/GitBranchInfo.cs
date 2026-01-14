namespace BIMCanvas.Server.Dtos
{
    /// <summary>
    /// Git 分支信息 DTO
    /// </summary>
    public class GitBranchInfo
    {
        /// <summary>
        /// 分支 ID（等同于分支名）
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 分支名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 是否为当前分支
        /// </summary>
        public bool IsCurrent { get; set; }

        /// <summary>
        /// 最新提交信息
        /// </summary>
        public CommitInfo? Commit { get; set; }
    }

    /// <summary>
    /// Git 提交信息
    /// </summary>
    public class CommitInfo
    {
        /// <summary>
        /// 提交哈希（短格式，7位）
        /// </summary>
        public string Hash { get; set; } = string.Empty;

        /// <summary>
        /// 提交信息
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 相对时间（如 "5m ago"）
        /// </summary>
        public string Time { get; set; } = string.Empty;

        /// <summary>
        /// 作者名称
        /// </summary>
        public string Author { get; set; } = string.Empty;
    }

    /// <summary>
    /// 切换分支请求
    /// </summary>
    public class CheckoutBranchRequest
    {
        /// <summary>
        /// 要切换到的分支名
        /// </summary>
        public string BranchName { get; set; } = string.Empty;

        /// <summary>
        /// 分支不存在时是否自动创建（默认 false）
        /// </summary>
        public bool CreateIfNotExist { get; set; } = false;

        /// <summary>
        /// 切换前是否自动提交未保存的更改（默认 false）
        /// </summary>
        public bool CommitBeforeCheckout { get; set; } = false;

        /// <summary>
        /// 切换前是否放弃未保存的更改（默认 false）
        /// 与 CommitBeforeCheckout 互斥，优先级：Discard > Commit
        /// </summary>
        public bool DiscardBeforeCheckout { get; set; } = false;

        /// <summary>
        /// 自动提交时的提交信息（可选）
        /// </summary>
        public string? CommitMessage { get; set; }
    }

    /// <summary>
    /// 提交请求
    /// </summary>
    public class CommitRequest
    {
        /// <summary>
        /// 提交信息（可选，为空则自动生成）
        /// </summary>
        public string? Message { get; set; }
    }

    /// <summary>
    /// 创建 Worktree 请求
    /// </summary>
    public class CreateWorktreeRequest
    {
        /// <summary>
        /// Worktree 名称（如 "ai-storage"）
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 关联的分支名（如 "feat/ai-storage"）
        /// 如果不存在会自动创建
        /// </summary>
        public string BranchName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Worktree 信息 DTO
    /// </summary>
    public class WorktreeInfoDto
    {
        /// <summary>
        /// Worktree 名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Worktree 完整路径
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// 关联的分支名
        /// </summary>
        public string? Branch { get; set; }

        /// <summary>
        /// 当前 HEAD 的 commit hash
        /// </summary>
        public string? CommitHash { get; set; }

        /// <summary>
        /// 是否是主工作区
        /// </summary>
        public bool IsMain { get; set; }
    }

    /// <summary>
    /// 合并分支请求
    /// </summary>
    public class MergeRequest
    {
        /// <summary>
        /// 要合并的源分支名
        /// </summary>
        public string SourceBranch { get; set; } = string.Empty;

        /// <summary>
        /// 可选的合并提交信息
        /// </summary>
        public string? CommitMessage { get; set; }
    }

    /// <summary>
    /// 合并结果 DTO
    /// </summary>
    public class MergeResultDto
    {
        /// <summary>
        /// 是否合并成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 是否存在冲突
        /// </summary>
        public bool HasConflicts { get; set; }

        /// <summary>
        /// 结果消息
        /// </summary>
        public string? Message { get; set; }
    }
}
