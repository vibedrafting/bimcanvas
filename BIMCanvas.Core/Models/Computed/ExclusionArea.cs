using BIMCanvas.Core.Models.Geometry;

namespace BIMCanvas.Core.Models.Computed
{
    /// <summary>
    /// 禁区
    /// </summary>
    public class ExclusionArea
    {
        /// <summary>
        /// 禁区 ID
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 禁区类型
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// 禁区轮廓
        /// </summary>
        public Polygon2D? Polygon { get; set; }

        /// <summary>
        /// 禁区原因
        /// </summary>
        public string Reason { get; set; } = string.Empty;
    }
}
