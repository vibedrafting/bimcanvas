using BIMCanvas.Core.Models.Primitives;
using System.Collections.Generic;

namespace BIMCanvas.Core.Models.RevitSource
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
        /// 门窗线段
        /// </summary>
        public Line2D? Line { get; set; }

        /// <summary>
        /// 面向方向/内外开启方向（单位向量）
        /// </summary>
        public Vec2D? FacingDirection { get; set; }

        /// <summary>
        /// 左右开启方向列表（单位向量）
        /// 单开门：1个元素；双开门：2个元素；窗户：空列表或null
        /// </summary>
        public List<Vec2D>? HandDirections { get; set; }
    }
}
