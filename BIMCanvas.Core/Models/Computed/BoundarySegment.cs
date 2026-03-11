using Newtonsoft.Json;
using BIMCanvas.Core.Models.Geometry;

namespace BIMCanvas.Core.Models.Computed
{
    /// <summary>
    /// Zone 边界段：描述一段边界的物理含义（wall/passage/door/window）
    /// 有序数组，首尾闭合，完整描述 zone 的边界语义。
    /// </summary>
    public class BoundarySegment
    {
        /// <summary>
        /// 关联 ID：
        /// - door/window: Opening.Id（如 "d_5"、"wi_5"）
        /// - passage: 相邻 zone Id（如 "dz_1"），未匹配到 sibling 时为 null
        /// - wall: null（JSON 中省略）
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Id { get; set; }

        /// <summary>
        /// 段类型："wall" | "passage" | "door" | "window"
        /// </summary>
        public string Type { get; set; } = "wall";

        /// <summary>段起点</summary>
        public Point2D Start { get; set; }

        /// <summary>段终点</summary>
        public Point2D End { get; set; }
    }
}
