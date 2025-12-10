namespace BIMCanvas.Core.Models.Shared
{
    /// <summary>
    /// 完成面来源枚举
    /// 用于追踪完成面厚度的设置来源
    /// </summary>
    public enum FinishSource
    {
        /// <summary>
        /// 房间类型默认值
        /// </summary>
        RoomDefault,

        /// <summary>
        /// 工作区标签覆盖
        /// </summary>
        ZoneOverride,

        /// <summary>
        /// 用户手动设置
        /// </summary>
        UserOverride
    }
}
