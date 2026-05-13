using System.Collections.Generic;
using BIMCanvas.Core.Models.Layout;
using Newtonsoft.Json;

namespace BIMCanvas.Server.Models
{
    /// <summary>
    /// modules.json wrapper 的设计意图溯源字段。
    /// 由 Server 派生，Agent 写入时即使误传也会被忽略。
    /// </summary>
    public class SchemeMetadata
    {
        /// <summary>
        /// 一句话设计意图，从对应 semantic_plan 的 tag=v0.3 内容首句派生。
        /// </summary>
        public string Summary { get; set; } = string.Empty;

        /// <summary>
        /// 来源变体标识。canonical（无 variantId 写入）为 null。
        /// </summary>
        public string? VariantSlug { get; set; }

        /// <summary>
        /// 采纳时间戳（ISO8601）。常规 save 路径为 null；adopt 路径由 VariantController 填写。
        /// </summary>
        public string? AdoptedAt { get; set; }

        /// <summary>
        /// 产生流程标签。当前已定义：
        ///   "single-plan" —— variantId 为空的常规写入
        ///   "multi-plan-explore" —— variant-design-agent 产物（Phase 5+）
        ///   "relocation" —— module-relocation-agent 产物（Phase 12+）
        ///   "prev-adopted" —— VariantController.Adopt 归档
        ///   "unknown" —— variantId 非空但 variant.json 尚未建立时的占位（Phase 0b）
        /// </summary>
        public string SourceWorkflow { get; set; } = "single-plan";
    }

    /// <summary>
    /// modules.json 顶层 wrapper：所有写入/读取的唯一形态。
    /// </summary>
    public class ModulesWrapper
    {
        [JsonProperty("schemeMetadata")]
        public SchemeMetadata SchemeMetadata { get; set; } = new SchemeMetadata();

        [JsonProperty("modules")]
        public List<Module> Modules { get; set; } = new List<Module>();
    }
}
