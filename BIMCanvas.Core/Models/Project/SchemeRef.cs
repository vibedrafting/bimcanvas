namespace BIMCanvas.Core.Models.Project
{
    /// <summary>
    /// 策略引用（轻量级）
    /// 用于 Project.Schemes 列表
    /// </summary>
    public class SchemeRef
    {
        /// <summary>
        /// 策略 ID，格式：s{序号}_{名称}
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 策略相对路径，如 "./schemes/s1_Flow"
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// 策略显示名称
        /// </summary>
        public string Name { get; set; } = string.Empty;
    }
}
