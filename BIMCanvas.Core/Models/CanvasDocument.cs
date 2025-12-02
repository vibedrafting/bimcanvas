using System.Collections.Generic;
using Newtonsoft.Json;

namespace BIMCanvas.Core.Models
{
    /// <summary>
    /// 画布文档根对象 - v2.0 极简版
    /// </summary>
    public class CanvasDocument
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("version")]
        public int Version { get; set; }

        [JsonProperty("coordinateSystem")]
        public string CoordinateSystem { get; set; } = "cartesian_mm_yUp";

        [JsonProperty("metadata")]
        public Metadata Metadata { get; set; }

        [JsonProperty("outline")]
        public Outline Outline { get; set; }

        [JsonProperty("zones")]
        public List<Zone> Zones { get; set; } = new List<Zone>();

        [JsonProperty("modules")]
        public List<Module> Modules { get; set; } = new List<Module>();
    }

    /// <summary>
    /// 元数据
    /// </summary>
    public class Metadata
    {
        [JsonProperty("revitViewId")]
        public int RevitViewId { get; set; }

        [JsonProperty("levelId")]
        public int LevelId { get; set; }

        [JsonProperty("gridSize")]
        public int GridSize { get; set; } = 500;
    }

    /// <summary>
    /// 可视化底图 - 用于前端渲染户型图
    /// </summary>
    public class Outline
    {
        [JsonProperty("walls")]
        public List<Wall> Walls { get; set; } = new List<Wall>();

        [JsonProperty("openings")]
        public List<Opening> Openings { get; set; } = new List<Opening>();
    }

    /// <summary>
    /// 墙体轮廓 - 封闭多边形
    /// </summary>
    public class Wall
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        /// <summary>
        /// 墙体轮廓多边形顶点 [[x,y], [x,y], ...]
        /// </summary>
        [JsonProperty("polygon")]
        public List<double[]> Polygon { get; set; } = new List<double[]>();
    }

    /// <summary>
    /// 门窗 - 简化为线段
    /// </summary>
    public class Opening
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        /// <summary>
        /// 类型: "door" | "window"
        /// </summary>
        [JsonProperty("type")]
        public string Type { get; set; }

        /// <summary>
        /// 线段 [[x1,y1], [x2,y2]]
        /// </summary>
        [JsonProperty("line")]
        public double[][] Line { get; set; }
    }

    /// <summary>
    /// 设计区域 - AI 的核心工作区
    /// </summary>
    public class Zone
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>
        /// 功能类型: living, dining, master_bedroom, bedroom, study, kitchen, bathroom, entrance, balcony, corridor, storage
        /// </summary>
        [JsonProperty("function")]
        public string Function { get; set; }

        /// <summary>
        /// 可用空间轮廓（已扣除完成面）[[x,y], [x,y], ...]
        /// </summary>
        [JsonProperty("innerBoundary")]
        public List<double[]> InnerBoundary { get; set; } = new List<double[]>();

        /// <summary>
        /// 禁止布置区（门扇、通道等）
        /// </summary>
        [JsonProperty("exclusionAreas")]
        public List<ExclusionArea> ExclusionAreas { get; set; } = new List<ExclusionArea>();

        /// <summary>
        /// 关联的门窗 ID
        /// </summary>
        [JsonProperty("openings")]
        public List<string> Openings { get; set; } = new List<string>();
    }

    /// <summary>
    /// 禁止布置区 - 预计算为矩形（AABB）
    /// </summary>
    public class ExclusionArea
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        /// <summary>
        /// 类型: "door_swing" | "passage" | "other"
        /// </summary>
        [JsonProperty("type")]
        public string Type { get; set; }

        /// <summary>
        /// 简化矩形 [minX, minY, maxX, maxY]
        /// </summary>
        [JsonProperty("rect")]
        public double[] Rect { get; set; }
    }

    /// <summary>
    /// 布置模块 - 最小布置单元，可以是单一家具或组合
    /// </summary>
    public class Module
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        /// <summary>
        /// 模块库中的模块类型 ID
        /// </summary>
        [JsonProperty("moduleId")]
        public string ModuleId { get; set; }

        /// <summary>
        /// 可读名称（如"主卧睡眠模块"）
        /// </summary>
        [JsonProperty("moduleName")]
        public string ModuleName { get; set; }

        /// <summary>
        /// AABB 包围盒 [minX, minY, maxX, maxY]
        /// </summary>
        [JsonProperty("bounds")]
        public double[] Bounds { get; set; }

        /// <summary>
        /// 语义化朝向: north, south, east, west, northeast, southeast, southwest, northwest
        /// </summary>
        [JsonProperty("facing")]
        public string Facing { get; set; }

        /// <summary>
        /// 所属区域 ID
        /// </summary>
        [JsonProperty("zoneId")]
        public string ZoneId { get; set; }

        /// <summary>
        /// 模块内部家具清单（回写 Revit 用）
        /// </summary>
        [JsonProperty("items")]
        public List<ModuleItem> Items { get; set; } = new List<ModuleItem>();
    }

    /// <summary>
    /// 模块内部家具
    /// </summary>
    public class ModuleItem
    {
        /// <summary>
        /// 族库中的 Family ID
        /// </summary>
        [JsonProperty("familyId")]
        public string FamilyId { get; set; }

        /// <summary>
        /// 相对模块中心的偏移 [dx, dy]
        /// </summary>
        [JsonProperty("offset")]
        public double[] Offset { get; set; }

        /// <summary>
        /// 在模块中的角色（如"主体"、"左床头柜"）
        /// </summary>
        [JsonProperty("role")]
        public string Role { get; set; }
    }
}
