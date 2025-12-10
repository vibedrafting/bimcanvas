using System.Collections.Generic;
using BIMCanvas.Core.Models.CanvasData;
using BIMCanvas.Core.Models.RevitWriteback;

namespace BIMCanvas.Core.Models.RevitSource
{
    /// <summary>
    /// 画布文档（根对象）
    /// </summary>
    public class CanvasDocument
    {
        // ===== 元数据 =====

        /// <summary>
        /// 画布唯一标识，格式：canvas_{uuid}
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 版本号，每次修改递增，用于乐观锁
        /// </summary>
        public int Version { get; set; }

        /// <summary>
        /// 坐标系统，固定值：cartesian_mm_yUp
        /// </summary>
        public string CoordinateSystem { get; set; } = "cartesian_mm_yUp";

        /// <summary>
        /// 元数据
        /// </summary>
        public Metadata? Metadata { get; set; }

        // ===== 建筑构件（原 Outline 内容，提升到顶层）=====

        /// <summary>
        /// 单独墙体轮廓列表
        /// </summary>
        public List<Wall> Walls { get; set; } = new List<Wall>();

        /// <summary>
        /// 单独柱子轮廓列表
        /// </summary>
        public List<Column> Columns { get; set; } = new List<Column>();

        /// <summary>
        /// 门窗开口列表
        /// </summary>
        public List<Opening> Openings { get; set; } = new List<Opening>();

        /// <summary>
        /// 完成面定位边界列表（墙柱连续组合轮廓，已过滤外墙）
        /// </summary>
        public List<FinishLocationBoundary> FinishLocationBoundaries { get; set; } = new List<FinishLocationBoundary>();

        // ===== 空间数据 =====

        /// <summary>
        /// 物理房间列表
        /// </summary>
        public List<Room> Rooms { get; set; } = new List<Room>();

        /// <summary>
        /// 设计区域列表
        /// </summary>
        public List<Zone> Zones { get; set; } = new List<Zone>();

        /// <summary>
        /// 墙面完成面列表
        /// </summary>
        public List<WallFinish> WallFinishes { get; set; } = new List<WallFinish>();

        /// <summary>
        /// 布置模块列表
        /// </summary>
        public List<Module> Modules { get; set; } = new List<Module>();
    }
}
