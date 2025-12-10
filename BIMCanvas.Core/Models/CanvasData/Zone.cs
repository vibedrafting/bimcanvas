using System.Collections.Generic;
using BIMCanvas.Core.Models.Primitives;
using BIMCanvas.Core.Models.Shared;

namespace BIMCanvas.Core.Models.CanvasData
{
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
        /// 所属房间 ID
        /// </summary>
        public string RoomId { get; set; } = string.Empty;

        /// <summary>
        /// 功能标签列表（支持多标签）
        /// </summary>
        public List<ZoneTag> Tags { get; set; } = new List<ZoneTag>();

        /// <summary>
        /// 原始边界轮廓（未扣除完成面）
        /// </summary>
        public Polygon2D? RawBoundary { get; set; }

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
