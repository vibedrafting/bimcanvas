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

        /// <summary>
        /// 加载时发现的分区数据质检错误（Load 质检闸门）
        /// 存在此字段说明部分分区的模块已被隔离，文件未被修改
        /// </summary>
        public List<ZoneLoadError> ZoneErrors { get; set; } = new List<ZoneLoadError>();
    }

    /// <summary>
    /// 计算派生数据
    /// </summary>
    public class ComputedDataDto
    {
        /// <summary>
        /// 房间区域列表（来自 computed/room_zones.json）
        /// 使用 Zone 类型，Type = ZoneType.Room
        /// 由 baseline/rooms.json 转换生成
        /// </summary>
        public List<Zone> RoomZones { get; set; } = new List<Zone>();

        /// <summary>
        /// 禁区列表（来自 computed/exclusions.json）
        /// 使用 Zone 类型，Type = ZoneType.Exclusion
        /// </summary>
        public List<Zone> Exclusions { get; set; } = new List<Zone>();
    }

    /// <summary>
    /// Load 质检闸门：分区数据加载错误描述符
    /// Server 加载 modules.json 时发现不可渲染的坏数据，生成此描述符并隔离问题模块
    /// </summary>
    public class ZoneLoadError
    {
        /// <summary>发生错误的分区 ID</summary>
        public string ZoneId { get; set; } = string.Empty;

        /// <summary>错误类型：ParseError（文件无法解析）| StructureError（字段结构不合法）</summary>
        public string ErrorType { get; set; } = string.Empty;

        /// <summary>错误描述</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>被隔离的模块 ID 列表（ParseError 时为空）</summary>
        public List<string> FailedModuleIds { get; set; } = new List<string>();
    }
}
