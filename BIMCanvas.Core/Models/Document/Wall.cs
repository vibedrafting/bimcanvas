using BIMCanvas.Core.Models.Primitives;

namespace BIMCanvas.Core.Models.Document
{
    /// <summary>
    /// 墙体轮廓
    /// </summary>
    public class Wall
    {
        /// <summary>
        /// 墙体 ID，格式：w{序号}
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 墙体轮廓多边形
        /// </summary>
        public Polygon2D? Polygon { get; set; }
    }
}
