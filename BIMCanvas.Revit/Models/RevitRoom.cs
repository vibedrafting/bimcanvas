using NetTopologySuite.Geometries;

namespace BIMCanvas.Revit.Models
{
    /// <summary>
    /// Revit 房间中间模型（保留原生坐标）
    /// </summary>
    public class RevitRoom
    {
        /// <summary>
        /// 房间 ID（使用 DataId 生成），格式：room_001
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Revit 原始元素 ID（用于追溯）
        /// </summary>
        public int RevitId { get; set; }

        /// <summary>
        /// 房间名称（从 ROOM_NAME 参数提取）
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 标高名称（从 Room.Level 提取）
        /// </summary>
        public string Level { get; set; }

        /// <summary>
        /// 房间边界（NTS Polygon，feet 单位，Revit 项目坐标系）
        /// 支持外环 + 内环（holes）
        /// </summary>
        public Polygon Boundary { get; set; }
    }
}
