using System;

namespace BIMCanvas.Core.Models.Project
{
    /// <summary>
    /// 策略衍生来源
    /// 记录策略从哪个源策略衍生
    /// </summary>
    public class StrategyOrigin
    {
        /// <summary>
        /// 来源策略 ID
        /// </summary>
        public string SourceStrategyId { get; set; } = string.Empty;

        /// <summary>
        /// 来源仓库路径
        /// </summary>
        public string SourceRepo { get; set; } = string.Empty;

        /// <summary>
        /// 来源分支名
        /// </summary>
        public string SourceBranch { get; set; } = string.Empty;

        /// <summary>
        /// 来源提交哈希
        /// </summary>
        public string SourceCommit { get; set; } = string.Empty;

        /// <summary>
        /// 衍生时间
        /// </summary>
        public DateTime DerivedAt { get; set; }

        /// <summary>
        /// 衍生原因（用户描述）
        /// </summary>
        public string DerivationReason { get; set; } = string.Empty;
    }
}
