using System.Collections.Generic;
using BIMCanvas.Core.Models.Layout;
using Newtonsoft.Json;

namespace BIMCanvas.Server.Models
{
    /// <summary>
    /// modules.json wrapper 的方案级元数据。
    /// 由 AI 在 register_variant 时显式填入；Server 不再派生。
    /// </summary>
    public class SchemeMetadata
    {
        /// <summary>
        /// 一句话设计意图。
        ///   canonical modules.json：默认空字符串
        ///   variant modules.json：register_variant 时由 AI 提交的 summary
        ///   adopt 晋升 / 降级：透传源 wrapper 的 summary
        /// </summary>
        public string Summary { get; set; } = string.Empty;
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
