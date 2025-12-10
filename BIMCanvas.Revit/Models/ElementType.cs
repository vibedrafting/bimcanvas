namespace BIMCanvas.Revit.Models
{
    /// <summary>
    /// 构件类型枚举
    /// 用于 ElementOutline 区分不同类型的建筑构件
    /// </summary>
    public enum OutlineElementType
    {
        /// <summary>
        /// 墙体（含建筑墙、幕墙等）
        /// </summary>
        Wall,

        /// <summary>
        /// 建筑柱
        /// </summary>
        Column,

        /// <summary>
        /// 结构柱
        /// </summary>
        StructuralColumn
    }
}
