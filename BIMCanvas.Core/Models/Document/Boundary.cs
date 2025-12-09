using BIMCanvas.Core.Models.Primitives;

namespace BIMCanvas.Core.Models.Document
{
    /// <summary>
    /// 边界轮廓，格式：b{序号}
    /// </summary>
    public class Boundary
    {
        /// <summary>
        /// 边界 ID
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 边界轮廓多边形
        /// </summary>
        public Polygon2D? Polygon { get; set; }
    }
}
