using System.Collections.Generic;
using BIMCanvas.Core.Models.Computed;
using BIMCanvas.Core.Models.Layout;
using BIMCanvas.Core.Models.Project;
using BIMCanvas.Core.Models.Revit;

namespace BIMCanvas.Server.Dtos
{
    /// <summary>
    /// 项目数据聚合根 - 用于 Web 端数据传输
    /// 聚合 project.json + baseline/* + schemes/{s}/* + computed/*
    /// </summary>
    public class ProjectData
    {
        /// <summary>
        /// 项目元数据（来自 project.json）
        /// </summary>
        public Project Project { get; set; } = new Project();

        /// <summary>
        /// Baseline 层数据（来自 baseline/*.json）
        /// </summary>
        public BaselineData Baseline { get; set; } = new BaselineData();

        /// <summary>
        /// 当前激活策略数据（来自 schemes/{activeSchemeId}/*.json）
        /// </summary>
        public SchemeData ActiveScheme { get; set; } = new SchemeData();

        /// <summary>
        /// 计算派生数据（来自 computed/*.json）
        /// </summary>
        public ComputedDataDto Computed { get; set; } = new ComputedDataDto();
    }

    /// <summary>
    /// Baseline 层数据
    /// </summary>
    public class BaselineData
    {
        /// <summary>
        /// 元数据（来自 baseline/metadata.json）
        /// </summary>
        public BaselineManifest Metadata { get; set; } = new BaselineManifest();

        /// <summary>
        /// 墙体列表（来自 baseline/architecture.json）
        /// </summary>
        public List<Wall> Walls { get; set; } = new List<Wall>();

        /// <summary>
        /// 柱子列表（来自 baseline/architecture.json）
        /// </summary>
        public List<Column> Columns { get; set; } = new List<Column>();

        /// <summary>
        /// 门窗列表（来自 baseline/openings.json）
        /// </summary>
        public List<Opening> Openings { get; set; } = new List<Opening>();

        /// <summary>
        /// 房间列表（来自 baseline/rooms.json）
        /// </summary>
        public List<Room> Rooms { get; set; } = new List<Room>();

        /// <summary>
        /// 定位线列表（来自 baseline/location_lines.json）
        /// </summary>
        public List<LocationLine> LocationLines { get; set; } = new List<LocationLine>();
    }

    /// <summary>
    /// 策略层数据
    /// </summary>
    public class SchemeData
    {
        /// <summary>
        /// 策略元数据（来自 schemes/{s}/strategy.json）
        /// </summary>
        public Strategy Strategy { get; set; } = new Strategy();

        /// <summary>
        /// 功能分区（来自 schemes/{s}/zones.json）
        /// </summary>
        public List<Zone> Zones { get; set; } = new List<Zone>();

        /// <summary>
        /// 完成面（来自 schemes/{s}/finishes.json）
        /// </summary>
        public List<FinishSegment> Finishes { get; set; } = new List<FinishSegment>();

        /// <summary>
        /// 家具模块（来自 schemes/{s}/modules.json）
        /// </summary>
        public List<Module> Modules { get; set; } = new List<Module>();
    }

    /// <summary>
    /// 计算派生数据
    /// </summary>
    public class ComputedDataDto
    {
        /// <summary>
        /// 禁区列表（来自 computed/exclusions.json）
        /// </summary>
        public List<ExclusionArea> Exclusions { get; set; } = new List<ExclusionArea>();
    }
}
