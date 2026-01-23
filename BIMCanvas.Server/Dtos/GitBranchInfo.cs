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

        /// <summary>
        /// 创建新分支时的基准分支（可选）
        /// 仅当 CreateIfNotExist=true 时使用
        /// 如果不指定，默认基于当前 HEAD
        /// </summary>
        public string? BaseBranch { get; set; }
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

        /// <summary>
        /// Worktree 名称（可选）
        /// 如果指定，则在该 Worktree 目录中执行提交
        /// 如果不指定，则在主仓库中执行提交
        /// </summary>
        public string? WorktreeName { get; set; }
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
        /// 目标分支名（如 "feat/ai-storage"）
        /// - 分支已存在：检出到 Worktree（场景 A：并行开发）
        /// - 分支不存在：基于 BaseBranch 创建新分支（场景 B：隔离环境）
        /// </summary>
        public string Branch { get; set; } = string.Empty;

        /// <summary>
        /// 基准分支（可选）
        /// 仅当 Branch 不存在时使用，作为新分支的创建起点
        /// 如果不指定，默认基于当前 HEAD
        /// </summary>
        public string? BaseBranch { get; set; }
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
        /// 要合并的源分支名（要合并进来的分支）
        /// </summary>
        public string SourceBranch { get; set; } = string.Empty;

        /// <summary>
        /// 目标分支名（可选，合并到哪个分支）
        /// 如果指定，会先切换到目标分支再执行合并
        /// 如果不指定，则合并到当前分支
        /// 注意：与 WorktreeName 互斥，WorktreeName 优先
        /// </summary>
        public string? TargetBranch { get; set; }

        /// <summary>
        /// Worktree 名称（可选）
        /// 如果指定，则在该 Worktree 中执行合并（用于场景 F：目标分支已被 Worktree 检出）
        /// </summary>
        public string? WorktreeName { get; set; }

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

    /// <summary>
    /// AI Job 创建请求（高级端口）
    /// 一键创建 Agent 工作环境，自动生成分支名
    /// </summary>
    public class AiJobRequest
    {
        /// <summary>
        /// Worktree 名称（可选）
        /// 如果不指定，自动生成格式：job-{序号}-{时间戳后2位}
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// 基准分支（新分支基于此分支创建）
        /// 可选：不传则自动使用当前分支
        /// </summary>
        public string? BaseBranch { get; set; }
    }

    /// <summary>
    /// AI Job 完成请求
    /// </summary>
    public class AiJobCompleteRequest
    {
        /// <summary>
        /// 修改总结（展示给用户）
        /// </summary>
        public string Summary { get; set; } = string.Empty;
    }

    /// <summary>
    /// AI Job 完成响应
    /// </summary>
    public class AiJobCompleteResponse
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 是否有新的提交（SubAgent 是否有修改文件）
        /// </summary>
        public bool HasCommit { get; set; }

        /// <summary>
        /// AI Job 关联的分支名
        /// </summary>
        public string? BranchName { get; set; }
    }

    /// <summary>
    /// AI Job 创建响应
    /// </summary>
    public class AiJobResponse
    {
        /// <summary>
        /// AI Job 名称（自动生成或用户指定）
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 创建的 Worktree 完整路径
        /// </summary>
        public string WorktreePath { get; set; } = string.Empty;

        /// <summary>
        /// 自动生成的分支名（如 "feat/job-1-52-20260116-143052"）
        /// </summary>
        public string BranchName { get; set; } = string.Empty;
    }
}
