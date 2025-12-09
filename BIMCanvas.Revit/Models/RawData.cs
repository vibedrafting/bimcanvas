using Autodesk.Revit.DB;
using BIMCanvas.Core.Models.Document;

namespace BIMCanvas.Revit.Models
{
    /// <summary>
    /// 原始边界数据（未转换坐标）
    /// </summary>
    public struct RawBoundary
    {
        public string Id { get; set; }
        public CurveLoop Loop { get; set; }
    }

    /// <summary>
    /// 原始门窗数据（未转换坐标）
    /// </summary>
    public struct RawOpening
    {
        public string Id { get; set; }
        public OpeningType Type { get; set; }
        public Line Line { get; set; } // Revit Line
    }

    /// <summary>
    /// 原始房间数据（未转换坐标）
    /// </summary>
    public struct RawRoom
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public CurveLoop[] Loops { get; set; }
    }
}
