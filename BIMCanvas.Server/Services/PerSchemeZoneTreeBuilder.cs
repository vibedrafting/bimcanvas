using System.Collections.Generic;
using System.IO;
using System.Linq;
using BIMCanvas.Core.Converters.Json;
using BIMCanvas.Core.Models.Computed;
using BIMCanvas.Core.Models.Shared;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// P3 §2.8 整合：把 P1 拓扑解析器（<see cref="ModuleFileTopology"/>）算出的 per-scheme 叶子，
    /// 统一塑形为喂给下游几何消费方的 <see cref="Zone"/>。
    ///
    /// validate 的 <c>zoneGeometry</c>（<see cref="PluginSecurity.PluginValidatorOrchestrator"/>）
    /// 与 get_zone_boundaries 的边界喂入树（<see cref="ValidationController"/>）**共用此处**，
    /// 杜绝"两份重建逻辑漂移"。叶子唯一来源 = <see cref="ModuleFileTopology.GetLeafGeometrySource"/>
    /// （per-scheme、adopted-aware），不再走全局 schemes/zones.json 盲展平。
    ///
    /// passage 两期定义（预备-0 核实-1 / 蓝图 §2.7-2）：
    ///   · 空间理解期 = 顶层 room 全 wall（单叶子时叶子=room、几何全等 → ClassifyEdge 判全 wall）；
    ///   · 落地/校验期 = sibling 来自当前 adopted 方案 per-scheme 叶子（由本类重建喂入树落地）。
    /// </summary>
    public static class PerSchemeZoneTreeBuilder
    {
        private static readonly JsonSerializerSettings ZoneJsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
            Converters = { new Polygon2DConverter(), new Point2DConverter() }
        };

        /// <summary>
        /// 读 schemes/zones.json 顶层 room（rz_*，纯 baseline 房间拓扑），仅取 Id+Type+RawBoundary，
        /// **丢弃任何残留 subZones**（递归模型下 subZones 已不在全局树里）。
        /// 顶层 room 是 passage 落地期的 parent 包络 + per-scheme 叶子枚举入口。缺失返回空。
        /// </summary>
        public static List<Zone> ReadSchemeTopRooms(string projectPath)
        {
            var zonesPath = Path.Combine(projectPath, "schemes", "zones.json");
            if (!File.Exists(zonesPath))
                return new List<Zone>();

            var zones = JsonConvert.DeserializeObject<List<Zone>>(File.ReadAllText(zonesPath), ZoneJsonSettings)
                        ?? new List<Zone>();
            return zones
                .Where(z => !string.IsNullOrWhiteSpace(z.Id))
                .Select(z => new Zone { Id = z.Id, Type = z.Type, RawBoundary = z.RawBoundary })
                .ToList();
        }

        /// <summary>
        /// 某顶层 room/设计区节点下当前生效（adopted）的 per-scheme 叶子 → Zone（Id + Designable + RawBoundary）。
        /// 叶子集与几何均来自解析器，杜绝盲展平。未知节点 → 空。
        /// </summary>
        public static List<Zone> LeafZonesUnder(ModuleFileTopology topology, string roomNodeId)
        {
            return topology.GetLeafGeometrySource(roomNodeId)
                .Select(lg => new Zone
                {
                    Id = lg.LeafZoneId,
                    Type = ZoneType.Designable,
                    RawBoundary = lg.RawBoundary,
                })
                .ToList();
        }

        /// <summary>
        /// 重建喂给 <see cref="ZoneBoundaryService.CalculateBoundarySegments"/> 的 per-scheme zone 树：
        /// 顶层 room（schemes/zones.json，带 RawBoundary 作 parent 包络）→ SubZones = 该 room 的 per-scheme 叶子。
        /// ZoneBoundaryService 一行不改，仅换喂入树来源（§2.7-2）。
        /// </summary>
        public static List<Zone> BuildBoundaryFeedTree(string projectPath, ModuleFileTopology topology)
        {
            var rooms = ReadSchemeTopRooms(projectPath);
            foreach (var room in rooms)
            {
                var leaves = LeafZonesUnder(topology, room.Id);
                room.SubZones = leaves.Count > 0 ? leaves : null;
            }
            return rooms;
        }

        /// <summary>
        /// validate 的 zoneGeometry.designZones（Designable 叶子部分）：
        /// 遍历 schemes/zones.json 顶层 room（与边界喂入树同源），各取 per-scheme 叶子。
        /// room 层（Room 类型，带 computedBoundary）由调用方另读 room_zones.json 合并（镜像旧 _load_zone_data）。
        /// </summary>
        public static List<Zone> AllLeafZones(string projectPath, ModuleFileTopology topology)
        {
            return ReadSchemeTopRooms(projectPath)
                .SelectMany(room => LeafZonesUnder(topology, room.Id))
                .ToList();
        }
    }
}
