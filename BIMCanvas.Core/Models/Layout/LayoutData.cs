using System.Collections.Generic;

namespace BIMCanvas.Core.Models.Layout
{
    /// <summary>
    /// 方案数据（AI 生成的布置结果）
    /// </summary>
    public class LayoutData
    {
        /// <summary>
        /// 布置模块列表
        /// </summary>
        public List<Module> Modules { get; set; } = new List<Module>();

        /// <summary>
        /// 设计方案列表（预留，与 Zone.SchemeId 关联）
        /// </summary>
        public List<Scheme> Schemes { get; set; } = new List<Scheme>();
    }
}
