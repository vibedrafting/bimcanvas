using BIMCanvas.Core.Models.Shared;

namespace BIMCanvas.Core.Models.CanvasData
{
    /// <summary>
    /// 完成面需求
    /// 记录 Zone 关联的 WallFinish 及其所需类型
    /// </summary>
    public class FinishRequirement
    {
        /// <summary>
        /// 关联的 WallFinish ID
        /// </summary>
        public string WallFinishId { get; set; } = string.Empty;

        /// <summary>
        /// 需要的完成面类型
        /// </summary>
        public FinishType Type { get; set; }
    }
}
