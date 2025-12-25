using BIMCanvas.Core.Models.Shared;

namespace BIMCanvas.Core.Models.Computed
{
    /// <summary>
    /// 完成面段
    /// 对应 schemes/{s}/finishes.json 中的每段完成面
    /// </summary>
    public class FinishSegment
    {
        /// <summary>
        /// 完成面段 ID，格式：fs{序号}
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 引用的定位线 ID（LocationLine.Id）
        /// </summary>
        public string SourceLineId { get; set; } = string.Empty;

        /// <summary>
        /// 范围 [起点偏移mm, 终点偏移mm]，绝对 mm 值
        /// </summary>
        public double[] Range { get; set; } = new double[2];

        /// <summary>
        /// 完成面模块 ID
        /// </summary>
        public string FinishModuleId { get; set; } = string.Empty;

        /// <summary>
        /// 厚度（mm）
        /// </summary>
        public double Thickness { get; set; }

        /// <summary>
        /// 完成面来源
        /// </summary>
        public FinishSource Source { get; set; }

        /// <summary>
        /// 触发区域 ID（仅 ZoneOverride 时有效）
        /// </summary>
        public string? ZoneId { get; set; }

        /// <summary>
        /// 配置原因
        /// </summary>
        public string? Reason { get; set; }
    }
}
