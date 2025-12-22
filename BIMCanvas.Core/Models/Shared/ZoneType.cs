namespace BIMCanvas.Core.Models.Shared
{
    /// <summary>
    /// Zone 类型枚举
    /// </summary>
    public enum ZoneType
    {
        /// <summary>
        /// 禁区（禁止布置家具的区域）
        /// </summary>
        Exclusion,

        /// <summary>
        /// 房间（直接由 Revit Room 轮廓转换）
        /// </summary>
        Room,

        /// <summary>
        /// 设计区（AI/用户划分后的功能区）
        /// </summary>
        Designable
    }
}
