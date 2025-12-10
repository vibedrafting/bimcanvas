using NetTopologySuite.Geometries;
using System.Collections.Generic;

namespace BIMCanvas.Revit.Models
{
    /// <summary>
    /// Revit 墙面完成面定位边界中间模型
    /// 使用 NTS Polygon 存储几何数据（英尺，Revit 项目坐标系）
    /// 后期统一转换为 Core 层的 FinishLocationBoundary
    /// </summary>
    public class RevitWallFinish
    {
        /// <summary>
        /// 边界 ID（格式：wf_001, wf_002, ...）
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
