using System.Collections.Generic;
using BIMCanvas.Core.Models.Layout;

namespace BIMCanvas.Server.Dtos
{
    /// <summary>
    /// 获取模块数据响应
    /// </summary>
    public class SchemeModulesResponse
    {
        /// <summary>
        /// 数据源标识（如 "main", "worktree:ai-job-v", "branch:feat/xxx"）
        /// </summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>
        /// 关联的分支名
        /// </summary>
        public string? Branch { get; set; }

        /// <summary>
        /// 模块列表（合并所有分区的模块）
        /// </summary>
        public List<Module> Modules { get; set; } = new List<Module>();
    }

    /// <summary>
    /// 保存模块数据请求
    /// </summary>
    public class SaveSchemeModulesRequest
    {
        /// <summary>
        /// 要保存的模块列表
        /// </summary>
        public List<Module> Modules { get; set; } = new List<Module>();

        /// <summary>
        /// 提交信息（可选，如果提供则自动提交）
        /// </summary>
        public string? CommitMessage { get; set; }
    }

    /// <summary>
    /// 保存模块数据响应
    /// </summary>
    public class SaveSchemeModulesResponse
    {
        /// <summary>
        /// 是否保存成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 保存的模块数量
        /// </summary>
        public int SavedCount { get; set; }

        /// <summary>
        /// 提交信息（如果自动提交了）
        /// </summary>
        public CommitInfo? Commit { get; set; }
    }
}
