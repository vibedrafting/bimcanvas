using System;
using BIMCanvas.Core.Models.Revit;
using BIMCanvas.Core.Models.Computed;
using BIMCanvas.Core.Models.Layout;

namespace BIMCanvas.Core.Models.Document
{
    /// <summary>
    /// 设计文档（根对象）
    /// 包含 Revit 原始数据、计算派生数据和方案数据
    /// </summary>
    public class DesignDocument
    {
        // ===== 常规属性 =====

        /// <summary>
        /// 文档唯一标识，格式：canvas_{uuid}
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 项目名称
        /// </summary>
        public string ProjectName { get; set; } = string.Empty;

        /// <summary>
        /// 导出日期
        /// </summary>
        public DateTime ExportDate { get; set; }

        /// <summary>
        /// 版本号，每次修改递增，用于乐观锁
        /// </summary>
        public int Version { get; set; }

        /// <summary>
        /// 坐标系统，固定值：cartesian_mm_yUp
        /// </summary>
        public string CoordinateSystem { get; set; } = "cartesian_mm_yUp";

        // ===== 数据分组 =====

        /// <summary>
        /// Revit 导出的原始建筑数据
        /// </summary>
        public RevitData? Revit { get; set; }

        /// <summary>
        /// 从 Revit 数据派生的计算结果
        /// </summary>
        public ComputedData? Computed { get; set; }

        /// <summary>
        /// 方案数据（AI 生成的布置结果）
        /// </summary>
        public LayoutData? Layout { get; set; }
    }
}
