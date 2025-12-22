namespace BIMCanvas.Core.Models.Revit
{
    /// <summary>
    /// 画布元数据
    /// </summary>
    public class Metadata
    {
        /// <summary>
        /// 布置高度（毫米），家具回写时使用
        /// </summary>
        /// <remarks>
        /// 默认值：0mm（地面高度）
        /// 由 Revit 插件导出时设置，回写时用于确定家具实例的 Z 坐标
        /// </remarks>
        public double PlacementElevation { get; set; } = 0;

        // === 坐标变换参数（原 CoordinateTransform 类） ===

        /// <summary>
        /// 坐标原点在 Revit 项目坐标系中的位置（毫米）
        /// </summary>
        public double[] Origin { get; set; } = new double[3]; // [x, y, z]

        /// <summary>
        /// 视图旋转角度（弧度）
        /// </summary>
        public double Rotation { get; set; }

        /// <summary>
        /// 原点计算方法："boundingBox" 或 "cropBox"
        /// </summary>
        public string Method { get; set; } = "boundingBox";
    }
}
