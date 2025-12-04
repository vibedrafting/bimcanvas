namespace BIMCanvas.Core.Models.Document
{
    /// <summary>
    /// 画布元数据
    /// </summary>
    public class Metadata
    {
        /// <summary>
        /// 来源 Revit 视图 ID
        /// </summary>
        public long RevitViewId { get; set; }

        /// <summary>
        /// 标高 ID，家具回写依赖
        /// </summary>
        public long LevelId { get; set; }

        /// <summary>
        /// 网格大小（毫米），默认 500
        /// </summary>
        public double? GridSize { get; set; }
    }
}
