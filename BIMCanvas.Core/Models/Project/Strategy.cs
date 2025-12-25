using System;
using BIMCanvas.Core.Models.Shared;

namespace BIMCanvas.Core.Models.Project
{
    /// <summary>
    /// 策略元数据
    /// 对应 schemes/{s}/strategy.json
    /// </summary>
    public class Strategy
    {
        /// <summary>
        /// 策略 ID，格式：s{序号}_{名称}
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 策略名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 设计方法
        /// </summary>
        public StrategyApproach Approach { get; set; }

        /// <summary>
        /// 策略描述（可选）
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// 衍生来源（原创策略为 null）
        /// </summary>
        public StrategyOrigin? Origin { get; set; }

        /// <summary>
        /// 最后验证的 baseline 哈希值
        /// 格式：sha256:{hash}
        /// </summary>
        public string LastValidatedBaselineHash { get; set; } = string.Empty;

        /// <summary>
        /// 策略状态（dirty 机制）
        /// </summary>
        public StrategyStatus Status { get; set; }
    }
}
