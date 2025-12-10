using System.Collections.Generic;
using BIMCanvas.Core.Models.Primitives;

namespace BIMCanvas.Core.Models.Document
{
    /// <summary>
    /// 完成面定位边界（墙柱连续组合轮廓，已过滤外墙）
    /// 用于 Server 端计算墙面完成面定位线
    /// </summary>
    public class FinishLocationBoundary
    {
        /// <summary>
        /// 边界 ID，格式：flb_{序号}
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 关联的 Revit 元素 ID 列表（墙、柱）
        /// </summary>
        public List<int> ElementIds { get; set; } = new List<int>();

        /// <summary>
        /// 边界轮廓多边形
        /// </summary>
        public Polygon2D? Polygon { get; set; }
    }
}
