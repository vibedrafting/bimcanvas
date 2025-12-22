using System.Collections.Generic;
using BIMCanvas.Core.Models.RevitSource;

namespace BIMCanvas.Core.Models.Document
{
    /// <summary>
    /// Revit 导出的原始建筑数据
    /// </summary>
    public class RevitData
    {
        /// <summary>
        /// 坐标变换参数
        /// </summary>
        public Metadata? Metadata { get; set; }

        /// <summary>
        /// 墙体轮廓列表
        /// </summary>
        public List<Wall> Walls { get; set; } = new List<Wall>();

        /// <summary>
        /// 柱子轮廓列表
        /// </summary>
        public List<Column> Columns { get; set; } = new List<Column>();

        /// <summary>
        /// 门窗开口列表
        /// </summary>
        public List<Opening> Openings { get; set; } = new List<Opening>();

        /// <summary>
        /// 完成面定位边界列表（墙柱连续组合轮廓，已过滤外墙）
        /// </summary>
        public List<FinishLocationBoundary> FinishLocationBoundaries { get; set; } = new List<FinishLocationBoundary>();

        /// <summary>
        /// 物理房间列表
        /// </summary>
        public List<Room> Rooms { get; set; } = new List<Room>();
    }
}
