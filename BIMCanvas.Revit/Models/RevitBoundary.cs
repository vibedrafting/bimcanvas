using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace BIMCanvas.Revit.Models
{
    /// <summary>
    /// Revit 边界轮廓中间模型
    /// 存储 Revit 原生 XYZ 坐标（英尺，项目坐标系）
    /// 后期统一转换为 Core 层的 Boundary
    /// </summary>
    public class RevitBoundary
    {
        /// <summary>
        /// 边界 ID（格式：b1, b2, ...）
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 边界顶点列表（Revit 原生 XYZ 坐标）
        /// 单位：英尺（Revit 内部单位）
        /// 坐标系：项目坐标系
        /// </summary>
        public List<XYZ> Vertices { get; set; } = new List<XYZ>();
    }
}
