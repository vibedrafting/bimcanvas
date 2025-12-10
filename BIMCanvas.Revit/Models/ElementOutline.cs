using NetTopologySuite.Geometries;

namespace BIMCanvas.Revit.Models
{
    /// <summary>
    /// 单构件轮廓数据模型
    /// 按单个构件独立存储轮廓（不做布尔运算合并）
    /// </summary>
    public class ElementOutline
    {
        /// <summary>
        /// 轮廓 ID（格式：outline_001, outline_002, ...）
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Revit 元素 ID（单个构件）
        /// </summary>
        public int ElementId { get; set; }

        /// <summary>
        /// 构件类型（墙/柱/结构柱）
        /// </summary>
        public OutlineElementType Type { get; set; }

        /// <summary>
        /// 轮廓多边形（NTS Polygon，feet 单位，Revit 项目坐标系）
        /// 对于异形柱等情况，可存储非矩形轮廓
        /// </summary>
        public Polygon Boundary { get; set; }
    }
}
