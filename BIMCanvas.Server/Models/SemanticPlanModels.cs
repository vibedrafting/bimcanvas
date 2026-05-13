using System.Collections.Generic;
using Newtonsoft.Json;

namespace BIMCanvas.Server.Models
{
    /// <summary>
    /// 语义方案条目
    /// </summary>
    public class SemanticPlanEntry
    {
        public string ZoneId { get; set; }
        public string Tag { get; set; }
        public string PlanType { get; set; }
        public string Content { get; set; }
        public string Timestamp { get; set; }
        // Nullable：无参考图流程下该字段语义上缺省（derived 流程不消费 reference analysis）。
        public string? ReferenceAnalysisTag { get; set; }
    }

    /// <summary>
    /// 旧版嵌入式参考分析（仅用于向后兼容读取）
    /// </summary>
    public class LegacyEmbeddedReferenceAnalysis
    {
        public string SourceImageId { get; set; }
        public string Relevance { get; set; }
        public string Content { get; set; }
        public string Timestamp { get; set; }
    }

    /// <summary>
    /// 参考分析条目
    /// </summary>
    public class ReferenceAnalysisEntry
    {
        public string Tag { get; set; }
        public string SourceImageId { get; set; }
        /// <summary>
        /// 完整、自包含、可单独阅读的参考分析快照内容。
        /// </summary>
        public string Content { get; set; }
        public string Timestamp { get; set; }
    }

    /// <summary>
    /// 语义方案文档
    /// </summary>
    public class SemanticPlanDocument
    {
        public List<SemanticPlanEntry> Entries { get; set; }

        [JsonProperty("referenceAnalysis", NullValueHandling = NullValueHandling.Ignore)]
        public LegacyEmbeddedReferenceAnalysis LegacyEmbeddedReferenceAnalysis { get; set; }
    }
}
