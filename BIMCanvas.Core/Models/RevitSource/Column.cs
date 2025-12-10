using BIMCanvas.Core.Models.Primitives;

namespace BIMCanvas.Core.Models.RevitSource
{
    /// <summary>
    /// 单独柱子轮廓
    /// </summary>
    public class Column
    {
        /// <summary>
        /// 柱子 ID，格式：col_{序号}
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Revit 元素 ID
        /// </summary>
        public int ElementId { get; set; }

        /// <summary>
        /// 是否为结构柱（true=结构柱，false=建筑柱）
        /// </summary>
        public bool IsStructural { get; set; }

        /// <summary>
        /// 柱子轮廓多边形
        /// </summary>
        public Polygon2D? Polygon { get; set; }
    }
}
