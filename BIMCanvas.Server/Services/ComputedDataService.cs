using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BIMCanvas.Core.Models.Computed;
using BIMCanvas.Core.Models.Geometry;
using BIMCanvas.Core.Models.Layout;
using BIMCanvas.Core.Models.Revit;
using BIMCanvas.Core.Models.Shared;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// 计算数据管理服务
    /// 负责 computed/ 目录下数据的生成和验证
    /// </summary>
    public class ComputedDataService
    {
        private readonly ILogger<ComputedDataService> _logger;
        private readonly ManifestService _manifestService;

        public ComputedDataService(
            ILogger<ComputedDataService> logger,
            ManifestService manifestService)
        {
            _logger = logger;
            _manifestService = manifestService;
        }

        /// <summary>
        /// 验证 computed 数据是否有效
        /// </summary>
        /// <param name="projectPath">项目文件夹路径</param>
        /// <returns>true = 有效，无需重新计算</returns>
        public bool ValidateComputedData(string projectPath)
        {
            var baselinePath = Path.Combine(projectPath, "baseline");
            var computedPath = Path.Combine(projectPath, "computed");

            // 1. 检查 computed/ 目录是否存在
            if (!Directory.Exists(computedPath))
            {
                _logger.LogInformation("computed/ 目录不存在，需要生成");
                return false;
            }

            // 2. 检查 exclusions.json 是否存在
            var exclusionsPath = Path.Combine(computedPath, "exclusions.json");
            if (!File.Exists(exclusionsPath))
            {
                _logger.LogInformation("exclusions.json 不存在，需要生成");
                return false;
            }

            // 3. 检查 zones.json 是否存在
            var zonesPath = Path.Combine(computedPath, "zones.json");
            if (!File.Exists(zonesPath))
            {
                _logger.LogInformation("zones.json 不存在，需要生成");
                return false;
            }

            // 3. 获取 baseline 和 computed 的哈希值
            var baselineHash = _manifestService.GetBaselineHash(baselinePath);
            var computedBaselineHash = _manifestService.GetComputedBaselineHash(computedPath);

            if (string.IsNullOrEmpty(baselineHash))
            {
                _logger.LogInformation("baseline.manifest 不存在或无 baselineHash，需要重新计算");
                return false;
            }

            if (string.IsNullOrEmpty(computedBaselineHash))
            {
                _logger.LogInformation("computed.manifest 不存在或无 baselineHash，需要重新计算");
                return false;
            }

            // 4. 比较 baselineHash
            if (!string.Equals(baselineHash, computedBaselineHash, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("baselineHash 不一致，需要重新计算");
                _logger.LogDebug("  baseline.manifest.baselineHash = {Hash1}", baselineHash);
                _logger.LogDebug("  computed.manifest.baselineHash = {Hash2}", computedBaselineHash);
                return false;
            }

            _logger.LogInformation("computed 数据验证通过，无需重新计算");
            return true;
        }

        /// <summary>
        /// 生成计算数据
        /// </summary>
        /// <param name="projectPath">项目文件夹路径</param>
        public void GenerateComputedData(string projectPath)
        {
            var baselinePath = Path.Combine(projectPath, "baseline");
            var computedPath = Path.Combine(projectPath, "computed");

            _logger.LogInformation("开始生成计算数据...");

            // 1. 确保 computed/ 目录存在
            if (!Directory.Exists(computedPath))
            {
                Directory.CreateDirectory(computedPath);
                _logger.LogInformation("创建 computed/ 目录");
            }

            // 2. 读取 openings.json 并计算门扇禁区
            var openingsPath = Path.Combine(baselinePath, "openings.json");
            var openings = LoadOpenings(openingsPath);
            _logger.LogInformation("读取到 {Count} 个门窗", openings.Count);

            var exclusions = CalculateDoorSwingExclusions(openings);
            _logger.LogInformation("计算出 {Count} 个门扇禁区", exclusions.Count);

            // 3. 写入 exclusions.json
            var exclusionsPath = Path.Combine(computedPath, "exclusions.json");
            var exclusionsJson = JsonConvert.SerializeObject(exclusions, Formatting.Indented);
            File.WriteAllText(exclusionsPath, exclusionsJson, Encoding.UTF8);
            _logger.LogInformation("写入 exclusions.json");

            // 4. 读取 rooms.json 并转换为 Zone
            var roomsPath = Path.Combine(baselinePath, "rooms.json");
            var rooms = LoadRooms(roomsPath);
            _logger.LogInformation("读取到 {Count} 个房间", rooms.Count);

            var roomZones = CalculateRoomZones(rooms);
            _logger.LogInformation("计算出 {Count} 个房间区域", roomZones.Count);

            // 5. 写入 zones.json
            var zonesPath = Path.Combine(computedPath, "zones.json");
            var zonesJson = JsonConvert.SerializeObject(roomZones, Formatting.Indented);
            File.WriteAllText(zonesPath, zonesJson, Encoding.UTF8);
            _logger.LogInformation("写入 zones.json");

            // 6. 读取当前 baseline hash 并写入 computed.manifest
            var baselineHash = _manifestService.GetBaselineHash(baselinePath);
            if (!string.IsNullOrEmpty(baselineHash))
            {
                _manifestService.WriteComputedManifest(computedPath, baselineHash);
            }
            else
            {
                _logger.LogWarning("无法获取 baselineHash，computed.manifest 可能不完整");
            }

            _logger.LogInformation("计算数据生成完成");
        }

        /// <summary>
        /// 加载 openings.json
        /// </summary>
        private List<Opening> LoadOpenings(string openingsPath)
        {
            if (!File.Exists(openingsPath))
            {
                _logger.LogWarning("openings.json 不存在: {Path}", openingsPath);
                return new List<Opening>();
            }

            try
            {
                var json = File.ReadAllText(openingsPath, Encoding.UTF8);
                return JsonConvert.DeserializeObject<List<Opening>>(json) ?? new List<Opening>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "解析 openings.json 失败");
                return new List<Opening>();
            }
        }

        /// <summary>
        /// 计算门扇禁区
        /// 返回 Zone 类型，Type = ZoneType.Exclusion
        /// reason 字段格式: {subType}:{description}
        /// </summary>
        private List<Zone> CalculateDoorSwingExclusions(List<Opening> openings)
        {
            var result = new List<Zone>();

            foreach (var opening in openings)
            {
                // 只处理门
                if (opening.Type != OpeningType.Door)
                    continue;

                // 必须有线段和面向方向
                if (opening.Line == null || opening.FacingDirection == null)
                {
                    _logger.LogWarning("门 {Id} 缺少线段或面向方向数据，跳过", opening.Id);
                    continue;
                }

                var line = opening.Line;
                var facing = opening.FacingDirection.Value;
                var doorWidth = line.Length;

                if (doorWidth < 1) // 门宽太小，忽略
                {
                    _logger.LogDebug("门 {Id} 宽度过小 ({Width}mm)，跳过", opening.Id, doorWidth);
                    continue;
                }

                // 计算禁区矩形边界
                // 向房间内扩展 doorWidth 的距离
                var offset = facing * doorWidth;
                var vertices = new[]
                {
                    line.Start,
                    line.End,
                    line.End + offset,
                    line.Start + offset
                };

                var exclusion = new Zone
                {
                    Id = $"excl_door_{opening.Id}",
                    Name = "门扇禁区",
                    RoomId = string.Empty,
                    Type = ZoneType.Exclusion,
                    Reason = $"door_swing:门 {opening.Id} 的开启扫过区域",
                    RawBoundary = new Polygon2D(vertices),
                    ComputedBoundary = null,
                    Tags = new List<ZoneTag>(),
                    FinishRequirements = new List<FinishRequirement>(),
                    SchemeId = null
                };

                result.Add(exclusion);
                _logger.LogDebug("生成门扇禁区: {Id}, 宽度={Width}mm", exclusion.Id, doorWidth);
            }

            return result;
        }

        /// <summary>
        /// 加载 rooms.json
        /// </summary>
        private List<Room> LoadRooms(string roomsPath)
        {
            if (!File.Exists(roomsPath))
            {
                _logger.LogWarning("rooms.json 不存在: {Path}", roomsPath);
                return new List<Room>();
            }

            try
            {
                var json = File.ReadAllText(roomsPath, Encoding.UTF8);
                return JsonConvert.DeserializeObject<List<Room>>(json) ?? new List<Room>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "解析 rooms.json 失败");
                return new List<Room>();
            }
        }

        /// <summary>
        /// 将物理房间转换为 Zone
        /// 返回 Zone 类型，Type = ZoneType.Room
        /// reason 字段格式: {subType}:{description}
        /// </summary>
        private List<Zone> CalculateRoomZones(List<Room> rooms)
        {
            var result = new List<Zone>();

            foreach (var room in rooms)
            {
                // 必须有边界
                if (room.Boundary == null || room.Boundary.Vertices.Length < 3)
                {
                    _logger.LogWarning("房间 {Id} 缺少有效边界数据，跳过", room.Id);
                    continue;
                }

                var zone = new Zone
                {
                    Id = $"zone_room_{room.Id}",
                    Name = room.Name,
                    RoomId = room.Id,
                    Type = ZoneType.Room,
                    Reason = $"room:{room.Type}",
                    RawBoundary = room.Boundary,
                    ComputedBoundary = null, // Room 类型暂不计算内缩边界
                    Tags = new List<ZoneTag>(),
                    FinishRequirements = new List<FinishRequirement>(),
                    SchemeId = null
                };

                result.Add(zone);
                _logger.LogDebug("生成房间区域: {Id}, 名称={Name}, 类型={Type}",
                    zone.Id, room.Name, room.Type);
            }

            return result;
        }
    }
}
