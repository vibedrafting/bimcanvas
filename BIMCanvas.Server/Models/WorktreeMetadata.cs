using System;
using System.Collections.Generic;

namespace BIMCanvas.Server.Models
{
    /// <summary>
    /// Worktree 元数据文件根对象
    /// 用于持久化记录每个 Worktree 的创建意图和关联信息
    /// </summary>
    public class WorktreeMetadata
    {
        /// <summary>
        /// 元数据格式版本号
        /// </summary>
        public string Version { get; set; } = "1.0";

        /// <summary>
        /// Worktree 条目列表
        /// </summary>
        public List<WorktreeEntry> Worktrees { get; set; } = new();
    }

    /// <summary>
    /// 单个 Worktree 的元数据条目
    /// </summary>
    public class WorktreeEntry
    {
        /// <summary>
        /// Worktree 名称（如 "job_001", "parallel_001"）
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Git 分支名称（如 "temp/job_001_20260124_143022", "feature/new-layout"）
        /// </summary>
        public string BranchName { get; set; } = string.Empty;

        /// <summary>
        /// 创建意图：
        /// - "isolation": 隔离环境（临时分支，删除 worktree 时一并删除分支）
        /// - "parallel": 并行开发（长期分支，删除 worktree 时保留分支）
        /// </summary>
        public string Intent { get; set; } = "isolation";

        /// <summary>
        /// 基准分支名称（创建时的起点分支）
        /// </summary>
        public string BaseBranch { get; set; } = "master";

        /// <summary>
        /// 创建时间（UTC）
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 创建者（"Agent" 或 "User"）
        /// </summary>
        public string CreatedBy { get; set; } = "Agent";
    }
}
