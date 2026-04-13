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
    /// 参考分析结果（AI 友好设计）
    /// </summary>
    public class ReferenceAnalysis
    {
        /// <summary>
        /// 参考图附件 ID（可选）
        /// </summary>
        public string SourceImageId { get; set; }

        /// <summary>
        /// 关联性等级：partially_related（部分相关）、structurally_related（结构相关）
        /// </summary>
        public string Relevance { get; set; }

        /// <summary>
        /// 参考分析内容（Markdown 格式）
        /// AI 在此自由组织 confirmedConstraints（硬约束）和 referenceHints（软提示）
        /// </summary>
        public string Content { get; set; }

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
