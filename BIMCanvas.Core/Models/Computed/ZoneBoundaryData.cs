using System.Collections.Generic;

namespace BIMCanvas.Core.Models.Computed
{
    /// <summary>
    /// 单个 Zone 的边界段数据包装
    /// </summary>
    public class ZoneBoundaryData
    {
        /// <summary>Zone ID</summary>
        public string ZoneId { get; set; } = string.Empty;

        /// <summary>有序边界段列表，首尾闭合</summary>
        public List<BoundarySegment> Segments { get; set; } = new List<BoundarySegment>();
    }
}
