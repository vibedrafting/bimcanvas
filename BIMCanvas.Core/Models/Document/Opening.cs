using BIMCanvas.Core.Models.Primitives;

namespace BIMCanvas.Core.Models.Document
{
    /// <summary>
    /// 门窗类型
    /// </summary>
    public enum OpeningType
    {
        Door,
        Window
    }

    /// <summary>
    /// 门窗
    /// </summary>
    public class Opening
    {
        /// <summary>
        /// 门窗 ID，格式：d{序号} 或 win{序号}
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 类型：door / window
        /// </summary>
        public OpeningType Type { get; set; }

        /// <summary>
        /// 门窗线段
        /// </summary>
        public Line2D? Line { get; set; }
    }
}
