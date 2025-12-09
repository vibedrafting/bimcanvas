using Autodesk.Revit.DB;
using BIMCanvas.Core.Models.Document;

namespace BIMCanvas.Revit.Models
{
    /// <summary>
    /// Revit 层门窗数据（保持原生坐标）
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
        /// 定位线起点（Revit 坐标系，英尺）
        /// </summary>
        public XYZ LocationLineStart { get; set; }

        /// <summary>
        /// 定位线终点（Revit 坐标系，英尺）
        /// </summary>
        public XYZ LocationLineEnd { get; set; }
    }
}
