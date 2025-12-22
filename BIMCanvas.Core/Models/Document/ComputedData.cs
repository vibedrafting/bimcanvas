using System.Collections.Generic;
using BIMCanvas.Core.Models.CanvasData;

namespace BIMCanvas.Core.Models.Document
{
    /// <summary>
    /// 从 Revit 数据派生的计算结果
    /// </summary>
    public class ComputedData
    {
        /// <summary>
        /// 设计区域列表（禁区、房间、设计区）
        /// </summary>
        public List<Zone> Zones { get; set; } = new List<Zone>();

        /// <summary>
        /// 墙面完成面列表
        /// </summary>
        public List<WallFinish> WallFinishes { get; set; } = new List<WallFinish>();
    }
}
