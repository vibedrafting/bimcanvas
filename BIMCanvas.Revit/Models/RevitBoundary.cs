using NetTopologySuite.Geometries;
using System.Collections.Generic;
using System.Windows.Documents;

namespace BIMCanvas.Revit.Models
{
    /// <summary>
    /// Revit 边界轮廓中间模型
    /// 使用 NTS Polygon 存储几何数据（英尺，Revit 项目坐标系）
    /// 后期统一转换为 Core 层的 Boundary
    /// </summary>
    public class RevitBoundary
    {
        /// <summary>
        /// 边界 ID（格式：boundary_001, boundary_002, ...）
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Revit 原始元素组的 ID 列表（用于追溯）
        /// </summary>
        public List<int> ElementIds { get; set; }

        /// <summary>
        /// 边界多边形（NTS Polygon，feet 单位，Revit 项目坐标系）
        /// 支持外环 + 内环（holes）
        /// </summary>
        public Polygon Boundary { get; set; }
    }
}
