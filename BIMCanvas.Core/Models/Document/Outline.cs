using System.Collections.Generic;

namespace BIMCanvas.Core.Models.Document
{
    /// <summary>
    /// 可视化底图（边界轮廓 + 门窗线段）
    /// </summary>
    public class Outline
    {
        /// <summary>
        /// 边界列表
        /// </summary>
        public List<Boundary> Boundarys { get; set; } = new List<Boundary>();

        /// <summary>
        /// 门窗列表
        /// </summary>
        public List<Opening> Openings { get; set; } = new List<Opening>();
    }
}
