using NetTopologySuite.Geometries;
using BIMCanvas.Core.Models.Document;
using BIMCanvas.Core.Models.Primitives;

namespace BIMCanvas.Revit.Models
{
    /// <summary>
    /// Revit 层门窗数据（使用 NTS + Core 几何类型）
    /// </summary>
    public class RevitOpening
    {
        /// <summary>
        /// 门窗唯一标识（如 d001, win001）
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// 类型：门或窗
        /// </summary>
        public OpeningType Type { get; set; }

        /// <summary>
        /// 定位点（NTS Coordinate，英尺单位）
        /// </summary>
        public Coordinate LocationPoint { get; set; }

        /// <summary>
        /// 定位线（NTS LineSegment，英尺单位）
        /// </summary>
        public LineSegment LocationLine { get; set; }

        /// <summary>
        /// 面向方向（Core Vec2D，单位向量）
        /// </summary>
        public Vec2D FacingDirection { get; set; }

        /// <summary>
        /// 左右开启方向列表（Core Vec2D，仅门有值）
        /// </summary>
        public Vec2D[] HandDirections { get; set; }
    }
}
