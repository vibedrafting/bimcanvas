using BIMCanvas.Core.Models.Geometry;

namespace BIMCanvas.Core.Models.Revit
{
    /// <summary>
    /// 完成面定位线
    /// 对应 baseline/location_lines.json 中的每条线
    /// </summary>
    public class LocationLine
    {
        /// <summary>
        /// 定位线 ID，格式：ll{序号}
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 所属墙体 ID
        /// </summary>
        public string WallId { get; set; } = string.Empty;

        /// <summary>
        /// 所属房间 ID
        /// </summary>
        public string RoomId { get; set; } = string.Empty;

        /// <summary>
        /// 哪一侧："interior" | "exterior"
        /// </summary>
        public string Side { get; set; } = "interior";

        /// <summary>
        /// 定位线坐标
        /// </summary>
        public Line2D? Line { get; set; }

        /// <summary>
        /// 线段长度（mm），冗余存储便于计算
        /// </summary>
        public double Length { get; set; }
    }
}
