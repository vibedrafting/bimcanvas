using BIMCanvas.Core.Models.Primitives;
using BIMCanvas.Core.Models.Shared;

namespace BIMCanvas.Core.Models.RevitSource
{
    /// <summary>
    /// 物理房间（对应 Revit Room）
    /// </summary>
    public class Room
    {
        /// <summary>
        /// 房间 ID，格式：room_{uuid}
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 房间名称（用户可见）
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 房间类型
        /// </summary>
        public RoomType Type { get; set; }

        /// <summary>
        /// 房间边界轮廓
        /// </summary>
        public Polygon2D? Boundary { get; set; }
    }
}
