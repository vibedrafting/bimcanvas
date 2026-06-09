namespace BIMCanvas.Server.Models
{
    /// <summary>
    /// GET /api/scheme/variants 返回的方案元数据，Server 派生填充，磁盘上无对应文件
    /// （slug / createdAt 由文件系统派生、state 由 slug + adopted 指针推断、
    /// summary 由方案叶子 modules.json 的 schemeMetadata 派生）。
    /// state 取值：adopted / hidden / variant。
    /// </summary>
    public class VariantMetadata
    {
        public string Slug { get; set; } = "";

        /// <summary>ISO8601 创建时间（取目录 mtime）；list_variants 排序键。</summary>
        public string? CreatedAt { get; set; }

        /// <summary>由 slug 与 adopted 指针派生：adopted 指向 → "adopted"，_ 前缀 → "hidden"，其他 → "variant"。决定 Web 端显示。</summary>
        public string State { get; set; } = "unknown";

        /// <summary>一句话设计意图（变体内 semantic_plan 派生），供 VariantNavigatorBar chip tooltip 显示。</summary>
        public string Summary { get; set; } = "";
    }
}
