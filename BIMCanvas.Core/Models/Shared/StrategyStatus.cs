namespace BIMCanvas.Core.Models.Shared
{
    /// <summary>
    /// 策略状态枚举
    /// 用于 dirty 机制判断策略与 baseline 的一致性
    /// </summary>
    public enum StrategyStatus
    {
        /// <summary>
        /// 有效：策略与当前 baseline 一致
        /// </summary>
        Valid,

        /// <summary>
        /// 脏：baseline 已变更，策略需要验证
        /// </summary>
        Dirty,

        /// <summary>
        /// 无效：策略与 baseline 不兼容，需要修复
        /// </summary>
        Invalid
    }
}
