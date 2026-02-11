using System.Collections.Generic;
using System.Runtime.Serialization;
using BIMCanvas.Core.Models.Geometry;
using BIMCanvas.Core.Models.Semantic;
using Newtonsoft.Json;

namespace BIMCanvas.Core.Models.Layout
{
    /// <summary>
    /// 布置模块（最小布置单元）
    /// </summary>
    public class Module
    {
        /// <summary>
        /// 模块实例唯一 ID，格式：m_xxxxxxxx（8 位随机字母数字）
        /// Agent 写入时可不填，Server 反序列化时自动补全
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 模块库中的模块类型 ID
        /// </summary>
        public string ModuleId { get; set; } = string.Empty;

        /// <summary>
        /// 可读名称（如"主卧睡眠模块"）
        /// </summary>
        public string? ModuleName { get; set; }

        /// <summary>
        /// 精确边界（Polygon2D，矩形 4 顶点）
        /// </summary>
        public Polygon2D? Bounds { get; set; }

        /// <summary>
        /// 朝向（语义字符串或向量）
        /// </summary>
        public Facing Facing { get; set; }

        /// <summary>
        /// 所属分区 ID（运行时填充，保存时根据 bounds 位置自动计算）
        /// </summary>
        public string? ZoneId { get; set; }

        /// <summary>
        /// 模块内部家具清单（回写 Revit 用）
        /// </summary>
        public List<ModuleItem> Items { get; set; } = new List<ModuleItem>();

        /// <summary>
        /// 布置原因（给 AI 看）
        /// </summary>
        public string? PlacementReason { get; set; }

        [OnDeserialized]
        internal void OnDeserialized(StreamingContext context)
        {
            if (string.IsNullOrEmpty(Id))
                Id = GenerateModuleId();
        }

        /// <summary>
        /// 生成模块 ID：m_ + 8 位随机字母数字
        /// </summary>
        public static string GenerateModuleId()
        {
            const string chars = "0123456789abcdefghijklmnopqrstuvwxyz";
            var bytes = new byte[8];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            return "m_" + new string(System.Array.ConvertAll(bytes, b => chars[b % chars.Length]));
        }
    }
}
