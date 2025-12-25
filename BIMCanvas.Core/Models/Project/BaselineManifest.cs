using System;

namespace BIMCanvas.Core.Models.Project
{
    /// <summary>
    /// Baseline 元数据清单
    /// 对应 baseline/metadata.json
    /// </summary>
    public class BaselineManifest
    {
        /// <summary>
        /// 导出时间
        /// </summary>
        public DateTime ExportDate { get; set; }

        /// <summary>
        /// Revit 版本
        /// </summary>
        public string RevitVersion { get; set; } = string.Empty;

        /// <summary>
        /// 布置高度（毫米），家具回写时使用
        /// </summary>
        public double PlacementElevation { get; set; }

        /// <summary>
        /// 坐标原点 [x, y, z]（毫米）
        /// </summary>
        public double[] Origin { get; set; } = new double[3];

        /// <summary>
        /// 视图旋转角度（弧度）
        /// </summary>
        public double Rotation { get; set; }

        /// <summary>
        /// 原点计算方法："projectBasePoint" | "boundingBox" | "cropBox"
        /// </summary>
        public string TransformMethod { get; set; } = "projectBasePoint";

        /// <summary>
        /// 单位系统："metric_mm"
        /// </summary>
        public string UnitSystem { get; set; } = "metric_mm";

        /// <summary>
        /// Baseline 数据哈希值
        /// 用于 Server 层验证策略与 baseline 的一致性
        /// </summary>
        public string? BaselineHash { get; set; }
    }
}
