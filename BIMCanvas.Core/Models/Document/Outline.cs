using System.Collections.Generic;

namespace BIMCanvas.Core.Models.Document
{
    /// <summary>
    /// 可视化底图（墙体轮廓 + 门窗线段）
    /// </summary>
    public class Outline
    {
        /// <summary>
        /// 墙体列表
        /// </summary>
        public List<Wall> Walls { get; set; } = new List<Wall>();

        /// <summary>
        /// 门窗列表
        /// </summary>
        public List<Opening> Openings { get; set; } = new List<Opening>();
    }
}
