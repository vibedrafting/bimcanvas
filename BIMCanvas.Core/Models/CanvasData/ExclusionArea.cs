using BIMCanvas.Core.Models.Primitives;

namespace BIMCanvas.Core.Models.CanvasData
{
    /// <summary>
    /// 禁区类型
    /// </summary>
    public enum ExclusionType
    {
        /// <summary>
        /// 门扇开启区域
        /// </summary>
        DoorSwing,

        /// <summary>
        /// 必要通道
        /// </summary>
        Passage,

        /// <summary>
        /// 其他禁区
        /// </summary>
        Other
    }

    /// <summary>
    /// 禁止布置区
    /// </summary>
    public class ExclusionArea
    {
        /// <summary>
        /// 禁区 ID
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 禁区类型
        /// </summary>
        public ExclusionType Type { get; set; }

        /// <summary>
        /// 禁区边界（Polygon2D）
        /// </summary>
        public Polygon2D? Boundary { get; set; }
    }
}
