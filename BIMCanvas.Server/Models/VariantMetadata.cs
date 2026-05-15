namespace BIMCanvas.Server.Models
{
    /// <summary>
    /// GET /api/scheme/variants 返回的 variant 元数据，Server 派生填充，磁盘上无对应文件
    /// （Phase E：variant.json sidecar 已废弃，slug / createdAt 由文件系统派生、
    /// state 由 slug 前缀推断、summary 由变体内 semantic_plan.json 派生）。
    /// state 取值：variant / prev-adopted / unknown。
    /// </summary>
    public class VariantMetadata
    {
        public string Slug { get; set; } = "";

        /// <summary>ISO8601 创建时间（取目录 mtime）；list_variants 排序键。</summary>
        public string? CreatedAt { get; set; }

        /// <summary>由 slug 前缀派生：prev-* → "prev-adopted"，其他 → "variant"。决定 Web 端样式。</summary>
        public string State { get; set; } = "unknown";

        /// <summary>一句话设计意图（变体内 semantic_plan 派生），供 VariantNavigatorBar chip tooltip 显示。</summary>
        public string Summary { get; set; } = "";
    }
}
