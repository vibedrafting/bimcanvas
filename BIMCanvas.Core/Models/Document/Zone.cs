using System.Collections.Generic;
using BIMCanvas.Core.Models.Primitives;

namespace BIMCanvas.Core.Models.Document
{
    /// <summary>
    /// 区域功能类型
    /// </summary>
    public enum ZoneFunction
    {
        Living,
        Dining,
        MasterBedroom,
        Bedroom,
        Study,
        Kitchen,
        Bathroom,
        Entrance,
        Balcony,
        Corridor,
        Storage
    }

    /// <summary>
    /// 设计区域
    /// </summary>
    public class Zone
    {
        /// <summary>
        /// 区域 ID，格式：z{序号}
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 区域名称（用户可见）
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 功能类型
        /// </summary>
        public ZoneFunction Function { get; set; }

        /// <summary>
        /// 可用空间轮廓（已扣除完成面）
        /// </summary>
        public Polygon2D? InnerBoundary { get; set; }

        /// <summary>
        /// 禁止布置区列表
        /// </summary>
        public List<ExclusionArea> ExclusionAreas { get; set; } = new List<ExclusionArea>();

        /// <summary>
        /// 关联的门窗 ID 列表
        /// </summary>
        public List<string> Openings { get; set; } = new List<string>();
    }
}
