using System.Collections.Generic;

namespace BIMCanvas.Core.Models.Revit
{
    /// <summary>
    /// 建筑构造数据容器
    /// 对应 baseline/architecture.json
    /// </summary>
    public class Architecture
    {
        /// <summary>
        /// 墙体列表
        /// </summary>
        public List<Wall> Walls { get; set; } = new List<Wall>();

        /// <summary>
        /// 柱子列表
        /// </summary>
        public List<Column> Columns { get; set; } = new List<Column>();
    }
}
