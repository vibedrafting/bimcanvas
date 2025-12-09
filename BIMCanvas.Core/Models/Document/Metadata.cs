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
    }
}
