using System.Collections.Generic;

namespace BIMCanvas.Core.Models.Document
{
    /// <summary>
    /// 画布文档（根对象）
    /// </summary>
    public class CanvasDocument
    {
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

        /// <summary>
        /// 可视化底图（边界轮廓 + 门窗线段）
        /// </summary>
        public Outline? Outline { get; set; }

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
