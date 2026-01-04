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
    }
}
