using System.Collections.Generic;
using BIMCanvas.Core.Models.Geometry;
using BIMCanvas.Core.Models.Shared;

namespace BIMCanvas.Core.Models.Computed
{
    /// <summary>
    /// 统一的区域类型
    /// 通过 Type 区分禁区、房间、设计区
    /// </summary>
    public class Zone
    {
        // ========== 基础标识 ==========

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

        // ========== 6 项核心字段 ==========

        /// <summary>
        /// 1. 类型：禁区 / 房间 / 设计区
        /// </summary>
        public ZoneType Type { get; set; }

        /// <summary>
        /// 2. 原因：为什么产生这个 Zone（文本，给 AI 看）
        /// </summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>
        /// 3a. 原始轮廓：不考虑完成面扣减
        /// </summary>
        public Polygon2D? RawBoundary { get; set; }

        /// <summary>
        /// 3b. 计算轮廓：扣除完成面后，完成面变动需更新
        /// </summary>
        public Polygon2D? ComputedBoundary { get; set; }

        /// <summary>
        /// 4a. 必备功能标签：该空间的核心功能，对应的家具必须布置
        /// </summary>
        public List<ZoneTag> Tags { get; set; } = new List<ZoneTag>();

        /// <summary>
        /// 4b. 建议功能标签：该空间可选的扩展功能（如主卧的梳妆台）
        /// Agent 匹配到建议标签对应的模块时，应视为可选家具（可放可不放）
        /// </summary>
        public List<ZoneTag> OptionalTags { get; set; } = new List<ZoneTag>();

        /// <summary>
        /// 5. 完成面需求：关联的 WallFinish 及其类型
        /// </summary>
        public List<FinishRequirement> FinishRequirements { get; set; } = new List<FinishRequirement>();

        /// <summary>
        /// 6. 方案 ID：仅 Designable 类型有效，用于多方案系统（当前可为空）
        /// </summary>
        public string? SchemeId { get; set; }

    }
}
