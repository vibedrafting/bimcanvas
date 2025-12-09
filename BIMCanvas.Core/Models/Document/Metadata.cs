namespace BIMCanvas.Core.Models.Document
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

        /// <summary>
        /// 坐标转换配置（用于回写）
        /// </summary>
        /// <remarks>
        /// 记录导出时的坐标转换参数（原点、旋转），确保回写时坐标精确对应
        /// </remarks>
        public CoordinateTransform? CoordinateTransform { get; set; }
    }
}
