namespace BIMCanvas.Core.Models.Shared
{
    /// <summary>
    /// 策略设计方法枚举
    /// </summary>
    public enum StrategyApproach
    {
        /// <summary>
        /// 动线优先
        /// </summary>
        CirculationFirst,

        /// <summary>
        /// 主家具优先
        /// </summary>
        FurnitureFirst,

        /// <summary>
        /// 空间利用率优先
        /// </summary>
        SpaceEfficiency,

        /// <summary>
        /// 风格变体
        /// </summary>
        StyleVariation,

        /// <summary>
        /// 自定义
        /// </summary>
        Custom
    }
}
