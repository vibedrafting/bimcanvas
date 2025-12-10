using NetTopologySuite.Geometries;

namespace BIMCanvas.Revit.Models
{
    /// <summary>
    /// Revit 柱子轮廓中间模型
    /// </summary>
    public class RevitColumn
    {
        /// <summary>轮廓 ID（格式：col_001, scol_001, ...）</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Revit 元素 ID</summary>
        public int ElementId { get; set; }

        /// <summary>是否为结构柱</summary>
        public bool IsStructural { get; set; }

        /// <summary>轮廓多边形（NTS Polygon，feet 单位，Revit 项目坐标系）</summary>
        public Polygon Boundary { get; set; }
    }
}
