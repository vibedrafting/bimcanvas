using System;
using System.Collections.Generic;

namespace BIMCanvas.Core.Models.Project
{
    /// <summary>
    /// 项目入口
    /// 对应 project.json
    /// </summary>
    public class Project
    {
        /// <summary>
        /// 项目唯一标识，格式：proj_{名称}_{序号}
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 项目名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Schema 版本，固定 "3.0"
        /// </summary>
        public string Version { get; set; } = "3.0";

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// 坐标系标识，固定 "cartesian_mm_yUp"
        /// </summary>
        public string CoordinateSystem { get; set; } = "cartesian_mm_yUp";

        /// <summary>
        /// 当前激活的策略 ID（可选）
        /// </summary>
        public string? ActiveSchemeId { get; set; }

        /// <summary>
        /// 策略列表
        /// </summary>
        public List<SchemeRef> Schemes { get; set; } = new List<SchemeRef>();
    }
}
