using System.Collections.Generic;

namespace BIMCanvas.Core.Validation
{
    /// <summary>
    /// 方案级验证报告（类比编译器输出）
    /// </summary>
    public class SchemeValidationReport
    {
        /// <summary>
        /// 是否通过验证（无任何 Error 级别诊断）
        /// </summary>
        public bool IsValid => ErrorCount == 0;

        /// <summary>
        /// 参与验证的模块总数
        /// </summary>
        public int TotalModules { get; set; }

        /// <summary>
        /// 错误总数（severity = "error"）
        /// </summary>
        public int ErrorCount { get; set; }

        /// <summary>
        /// 警告总数（severity = "warning"）
        /// </summary>
        public int WarningCount { get; set; }

        /// <summary>
        /// 诊断列表
        /// </summary>
        public List<Diagnostic> Diagnostics { get; set; } = new List<Diagnostic>();

        /// <summary>
        /// 验证耗时（毫秒）
        /// </summary>
        public long ElapsedMs { get; set; }
    }
}
