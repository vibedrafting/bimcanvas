namespace BIMCanvas.Core.Models.Document
{
    /// <summary>
    /// 区域功能标签枚举（细粒度分类）
    /// 支持多标签组合
    /// </summary>
    public enum ZoneTag
    {
        // ============ 多媒体/视听 ============

        /// <summary>
        /// 电视媒体区
        /// </summary>
        TvMedia,

        /// <summary>
        /// 音视频区
        /// </summary>
        AudioVideo,

        // ============ 休息/睡眠 ============

        /// <summary>
        /// 睡眠区
        /// </summary>
        Sleep,

        /// <summary>
        /// 休息区
        /// </summary>
        Rest,

        /// <summary>
        /// 阅读区
        /// </summary>
        Reading,

        // ============ 工作/学习 ============

        /// <summary>
        /// 工作区
        /// </summary>
        Work,

        /// <summary>
        /// 学习区
        /// </summary>
        Study,

        // ============ 收纳 ============

        /// <summary>
        /// 衣柜收纳区
        /// </summary>
        WardrobeStorage,

        /// <summary>
        /// 鞋柜收纳区
        /// </summary>
        ShoeStorage,

        /// <summary>
        /// 通用收纳区
        /// </summary>
        GeneralStorage,

        // ============ 餐饮 ============

        /// <summary>
        /// 用餐区
        /// </summary>
        Dining,

        /// <summary>
        /// 烹饪区
        /// </summary>
        Cooking,

        /// <summary>
        /// 备餐区
        /// </summary>
        FoodPrep,

        /// <summary>
        /// 吧台区
        /// </summary>
        Bar,

        // ============ 卫浴 ============

        /// <summary>
        /// 淋浴区
        /// </summary>
        Shower,

        /// <summary>
        /// 浴缸区
        /// </summary>
        Bathtub,

        /// <summary>
        /// 马桶区
        /// </summary>
        Toilet,

        /// <summary>
        /// 洗漱区
        /// </summary>
        Washing,

        /// <summary>
        /// 洗衣区
        /// </summary>
        Laundry,

        // ============ 其他 ============

        /// <summary>
        /// 梳妆区
        /// </summary>
        Vanity,

        /// <summary>
        /// 入口区
        /// </summary>
        Entry,

        /// <summary>
        /// 通道区
        /// </summary>
        Passage,

        /// <summary>
        /// 展示区
        /// </summary>
        Display,

        /// <summary>
        /// 植物区
        /// </summary>
        Plants
    }
}
