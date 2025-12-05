using BIMCanvas.Core.Models.Primitives;

namespace BIMCanvas.Core.Models.Document
{
    /// <summary>
    /// 墙面完成面
    /// </summary>
    public class WallFinish
    {
        /// <summary>
        /// 完成面 ID，格式：wf_{uuid}
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 定位线（靠墙侧，方向顺房间）
        /// 由 Revit 插件提供
        /// </summary>
        public Line2D? LocationLine { get; set; }

        /// <summary>
        /// 完成面厚度（mm）
        /// </summary>
        public double Thickness { get; set; }

        /// <summary>
        /// 完成面模块库 ID（可选）
        /// </summary>
        public string? FinishModuleId { get; set; }

        /// <summary>
        /// 禁区轮廓（动态生成）
        /// 根据 LocationLine + Thickness 计算得出
        /// </summary>
        public Polygon2D? ExclusionBoundary { get; set; }

        /// <summary>
        /// 关联的墙体 ID
        /// </summary>
        public string WallId { get; set; } = string.Empty;

        /// <summary>
        /// 关联的房间 ID
        /// </summary>
        public string RoomId { get; set; } = string.Empty;

        /// <summary>
        /// 完成面来源（用于追踪设置来源）
        /// </summary>
        public FinishSource Source { get; set; }
    }
}
