using System;
using System.Collections.Generic;

namespace BIMCanvas.Server.Models
{
    /// <summary>
    /// 语义方案版本
    /// </summary>
    public class SemanticPlanVersion
    {
        public string ZoneId { get; set; }
        public string Version { get; set; }
        public string PlanType { get; set; }
        public string Content { get; set; }
        public string Timestamp { get; set; }
    }

    /// <summary>
    /// 参考约束（硬约束）
    /// </summary>
    public class ReferenceConstraint
    {
        /// <summary>
        /// 约束类型：negativeSpace（非侵占细节）、furnitureSelection（家具选型）、anchorPoint（关键锚点）
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// 约束描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 约束来源：用户确认、几何验证
        /// </summary>
        public string Source { get; set; }
    }

    /// <summary>
    /// 参考提示（软提示）
    /// </summary>
    public class ReferenceHint
    {
        /// <summary>
        /// 提示类型：zoningIntent（分区意图）、designPrinciple（设计理念）、furnitureRelation（家具关系）
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// 提示描述
        /// </summary>
        public string Description { get; set; }
    }

    /// <summary>
    /// 参考分析结果
    /// </summary>
    public class ReferenceAnalysis
    {
        /// <summary>
        /// 参考图附件 ID
        /// </summary>
        public string SourceImageId { get; set; }

        /// <summary>
        /// 关联性等级：partially_related（部分相关）、structurally_related（结构相关）
        /// </summary>
        public string Relevance { get; set; }

        /// <summary>
        /// 确认的约束（硬约束）
        /// </summary>
        public List<ReferenceConstraint> ConfirmedConstraints { get; set; }

        /// <summary>
        /// 参考提示（软提示）
        /// </summary>
        public List<ReferenceHint> ReferenceHints { get; set; }

        /// <summary>
        /// 已知差异
        /// </summary>
        public List<string> KnownDifferences { get; set; }

        /// <summary>
        /// 用户确认记录
        /// </summary>
        public List<string> UserConfirmations { get; set; }

        /// <summary>
        /// 时间戳
        /// </summary>
        public string Timestamp { get; set; }
    }

    /// <summary>
    /// 语义方案文档（新结构）
    /// </summary>
    public class SemanticPlanDocument
    {
        /// <summary>
        /// 版本历史
        /// </summary>
        public List<SemanticPlanVersion> Versions { get; set; }

        /// <summary>
        /// 参考分析结果（可选）
        /// </summary>
        public ReferenceAnalysis ReferenceAnalysis { get; set; }
    }
}
