using Newtonsoft.Json;

namespace BIMCanvas.Server.Models
{
    /// <summary>
    /// variants/{slug}/variant.json 的载荷。
    /// 由 variant-design-agent / module-relocation-agent / VariantController.Adopt（降级路径）写入。
    /// state 取值：explore-generated / relocation / prev-adopted / unknown。
    /// </summary>
    public class VariantMetadata
    {
        [JsonProperty("slug")]
        public string Slug { get; set; } = "";

        /// <summary>ISO8601 创建时间；list_variants 排序键。</summary>
        [JsonProperty("createdAt")]
        public string? CreatedAt { get; set; }

        /// <summary>产生路径标记，决定 Web 端展示样式与 schemeMetadata.sourceWorkflow 派生。</summary>
        [JsonProperty("state")]
        public string State { get; set; } = "unknown";

        /// <summary>一句话设计意图，供 VariantNavigatorBar chip tooltip 显示。</summary>
        [JsonProperty("summary")]
        public string Summary { get; set; } = "";

        /// <summary>克隆来源 slug；"canonical" 表示从 canonical 克隆，其他值为源 variant slug。</summary>
        [JsonProperty("sourceSlug", NullValueHandling = NullValueHandling.Ignore)]
        public string? SourceSlug { get; set; }
    }
}
