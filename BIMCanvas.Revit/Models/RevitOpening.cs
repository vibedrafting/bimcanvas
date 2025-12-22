using BIMCanvas.Core.Models.Revit;
using BIMCanvas.Core.Models.Geometry;
using NetTopologySuite.Geometries;
using NetTopologySuite.Mathematics;
using System.Collections.Generic;
using System.Numerics;
using System.Windows.Documents;

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
        /// Revit 原始元素 ID（用于追溯）
        /// </summary>
        public int ElementId { get; set; }

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
        /// 面向方向/内外开启方向（NTS Vector2D，单位向量）
        /// </summary>
        public Vector2D FacingDirection { get; set; }

        /// <summary>
        /// 左右开启方向（NTS Vector2D列表，单位向量）
        /// </summary>
        public List<Vector2D> HandDirections { get; set; }
    }
}
