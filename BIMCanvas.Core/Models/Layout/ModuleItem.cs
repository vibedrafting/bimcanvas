using BIMCanvas.Core.Models.Geometry;

namespace BIMCanvas.Core.Models.Layout
{
    /// <summary>
    /// 模块内部家具
    /// </summary>
    public class ModuleItem
    {
        /// <summary>
        /// 族库中的 Family ID
        /// </summary>
        public string FamilyId { get; set; } = string.Empty;

        /// <summary>
        /// 相对模块中心的偏移
        /// </summary>
        public Vec2D Offset { get; set; }

        /// <summary>
        /// 在模块中的角色（如"主体"、"左床头柜"）
        /// </summary>
        public string? Role { get; set; }
    }
}
