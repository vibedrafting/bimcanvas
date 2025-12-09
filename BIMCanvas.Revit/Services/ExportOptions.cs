namespace BIMCanvas.Revit.Services
{
    /// <summary>
    /// 画布导出配置选项
    /// </summary>
    public class ExportOptions
    {
        /// <summary>
        /// 是否显示配置窗口（让用户确认房间类型）
        /// </summary>
        public bool ShowConfigWindow { get; set; } = true;

        /// <summary>
        /// 默认保存路径（可选）
        /// </summary>
        public string? DefaultSavePath { get; set; }

        /// <summary>
        /// 布置高度（毫米），默认 0（地面高度）
        /// </summary>
        public double PlacementElevation { get; set; } = 0;

        /// <summary>
        /// 是否导出墙体轮廓
        /// </summary>
        public bool ExportWalls { get; set; } = true;

        /// <summary>
        /// 是否导出门窗
        /// </summary>
        public bool ExportOpenings { get; set; } = true;

        /// <summary>
        /// 是否导出房间
        /// </summary>
        public bool ExportRooms { get; set; } = true;
    }
}
