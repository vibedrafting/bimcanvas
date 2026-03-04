using BIMCanvas.Core.Models.Geometry;
using System.Collections.Generic;

namespace BIMCanvas.Core.Models.Revit
{
    /// <summary>
    /// 门窗类型
    /// </summary>
    public enum OpeningType
    {
        Door,
        Window
    }

    /// <summary>
    /// 门操作方式
    /// </summary>
    public enum DoorOperationType
    {
        /// <summary>平开门</summary>
        Swing = 0,
        /// <summary>推拉门</summary>
        Sliding = 1
    }

    /// <summary>
    /// 窗操作方式（预留）
    /// </summary>
    public enum WindowOperationType
    {
        /// <summary>固定窗</summary>
        Fixed = 0,
        /// <summary>平开窗</summary>
        Casement = 1,
        /// <summary>推拉窗</summary>
        Sliding = 2
    }

    /// <summary>
    /// 门窗
    /// </summary>
    public class Opening
    {
        /// <summary>
        /// 门窗 ID，格式：d{序号} 或 win{序号}
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 类型：door / window
        /// </summary>
        public OpeningType Type { get; set; }

        /// <summary>
        /// 门操作方式：平开 / 推拉（仅门有效，窗为 null）
        /// JSON 可选字段，缺省时默认 Swing（向后兼容）
        /// </summary>
        public DoorOperationType? DoorOperation { get; set; }

        /// <summary>
        /// 窗操作方式（预留，仅窗有效，门为 null）
        /// </summary>
        public WindowOperationType? WindowOperation { get; set; }

        /// <summary>
        /// 门窗线段
        /// </summary>
        public Line2D? Line { get; set; }

        /// <summary>
        /// 面向方向/内外开启方向（单位向量）
        /// </summary>
        public Vec2D? FacingDirection { get; set; }

        /// <summary>
        /// 左右开启方向列表（单位向量）
        /// 单开门：1个元素；双开门：2个元素；窗户/推拉门：空列表或null
        /// </summary>
        public List<Vec2D>? HandDirections { get; set; }
    }
}
