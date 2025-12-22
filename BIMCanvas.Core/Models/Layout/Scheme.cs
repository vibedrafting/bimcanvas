namespace BIMCanvas.Core.Models.Layout
{
    /// <summary>
    /// 设计方案（预留）
    /// 用于存储 AI 划分的不同设计分区和方案布置信息
    /// 与 Zone.SchemeId 关联
    /// </summary>
    public class Scheme
    {
        /// <summary>
        /// 方案 ID，与 Zone.SchemeId 关联
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 方案名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 方案描述
        /// </summary>
        public string? Description { get; set; }

        // TODO: 未来扩展字段
        // - 风格偏好
        // - 布局策略
        // - 关联的 Zone ID 列表
    }
}
