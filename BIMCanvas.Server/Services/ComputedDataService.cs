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
using Newtonsoft.Json.Serialization;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// 计算数据管理服务
    /// 负责 computed/ 目录下数据的生成和验证
    /// </summary>
    public class ComputedDataService
    {
        internal sealed class ComputedDataValidationResult
        {
            public bool IsValid { get; init; }
            public bool BaselineHashChanged { get; init; }
        }

        private readonly ILogger<ComputedDataService> _logger;
        private readonly ManifestService _manifestService;
        private readonly RoomTypeTagMappingService _tagMappingService;

        /// <summary>
        /// 统一的 JSON 序列化配置：camelCase 命名 + 枚举字符串化
        /// </summary>
        private static readonly JsonSerializerSettings CamelCaseSettings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
            Formatting = Formatting.Indented,
            Converters = new List<JsonConverter>
            {
                new Newtonsoft.Json.Converters.StringEnumConverter(new CamelCaseNamingStrategy())
            }
        };

        public ComputedDataService(
            ILogger<ComputedDataService> logger,
            ManifestService manifestService,
            RoomTypeTagMappingService tagMappingService)
        {
            _logger = logger;
            _manifestService = manifestService;
            _tagMappingService = tagMappingService;
        }

        /// <summary>
        /// 验证 computed 数据是否有效
        /// </summary>
        /// <param name="projectPath">项目文件夹路径</param>
        /// <returns>true = 有效，无需重新计算</returns>
        public bool ValidateComputedData(string projectPath)
        {
            return AnalyzeComputedData(projectPath).IsValid;
        }

        /// <summary>
        /// 分析 computed 数据状态，判断是否需要重新生成。
        /// </summary>
        internal ComputedDataValidationResult AnalyzeComputedData(string projectPath)
        {
            var baselinePath = Path.Combine(projectPath, "baseline");
            var computedPath = Path.Combine(projectPath, "computed");

            // 1. 检查 computed/ 目录是否存在
            if (!Directory.Exists(computedPath))
            {
                _logger.LogInformation("computed/ 目录不存在，需要生成");
                return new ComputedDataValidationResult { IsValid = false };
            }

            // 2. 检查 exclusions.json 是否存在
            var exclusionsPath = Path.Combine(computedPath, "exclusions.json");
            if (!File.Exists(exclusionsPath))
            {
                _logger.LogInformation("exclusions.json 不存在，需要生成");
                return new ComputedDataValidationResult { IsValid = false };
            }

            // 3. 检查 room_zones.json 是否存在
            var zonesPath = Path.Combine(computedPath, "room_zones.json");
            if (!File.Exists(zonesPath))
            {
                _logger.LogInformation("room_zones.json 不存在，需要生成");
                return new ComputedDataValidationResult { IsValid = false };
            }

            // 3. 获取 baseline 和 computed 的哈希值
            var baselineHash = _manifestService.GetBaselineHash(baselinePath);
            var computedBaselineHash = _manifestService.GetComputedBaselineHash(computedPath);

            if (string.IsNullOrEmpty(baselineHash))
            {
                _logger.LogInformation("baseline.manifest 不存在或无 baselineHash，需要重新计算");
                return new ComputedDataValidationResult { IsValid = false };
            }

            if (string.IsNullOrEmpty(computedBaselineHash))
            {
                _logger.LogInformation("computed.manifest 不存在或无 baselineHash，需要重新计算");
                return new ComputedDataValidationResult { IsValid = false };
            }

            // 4. 比较 baselineHash
            if (!string.Equals(baselineHash, computedBaselineHash, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("baselineHash 不一致，需要重新计算");
                _logger.LogDebug("  baseline.manifest.baselineHash = {Hash1}", baselineHash);
                _logger.LogDebug("  computed.manifest.baselineHash = {Hash2}", computedBaselineHash);
                return new ComputedDataValidationResult
                {
                    IsValid = false,
                    BaselineHashChanged = true
                };
            }

            _logger.LogInformation("computed 数据验证通过，无需重新计算");
            return new ComputedDataValidationResult { IsValid = true };
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

            // 3. 写入 exclusions.json（使用 camelCase）
            var exclusionsPath = Path.Combine(computedPath, "exclusions.json");
            var exclusionsJson = JsonConvert.SerializeObject(exclusions, CamelCaseSettings);
            File.WriteAllText(exclusionsPath, exclusionsJson, Encoding.UTF8);
            _logger.LogInformation("写入 exclusions.json");

            // 4. 读取 rooms.json 并转换为 Zone
            var roomsPath = Path.Combine(baselinePath, "rooms.json");
            var rooms = LoadRooms(roomsPath);
            _logger.LogInformation("读取到 {Count} 个房间", rooms.Count);

            var roomZones = CalculateRoomZones(rooms);
            _logger.LogInformation("计算出 {Count} 个房间区域", roomZones.Count);

            // 5. 写入 room_zones.json（使用 camelCase）
            var zonesPath = Path.Combine(computedPath, "room_zones.json");
            var zonesJson = JsonConvert.SerializeObject(roomZones, CamelCaseSettings);
            File.WriteAllText(zonesPath, zonesJson, Encoding.UTF8);
            _logger.LogInformation("写入 room_zones.json");

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
        /// ID 格式: ez_{序号}
        /// </summary>
        private List<Zone> CalculateDoorSwingExclusions(List<Opening> openings)
        {
            var result = new List<Zone>();
            var exclusionIndex = 0;

            foreach (var opening in openings)
            {
                // 只处理门
                if (opening.Type != OpeningType.Door)
                    continue;

                // 必须有线段
                if (opening.Line == null)
                {
                    _logger.LogWarning("门 {Id} 缺少线段数据，跳过", opening.Id);
                    continue;
                }

                var line = opening.Line;
                var facing = opening.FacingDirection?.Normalize();
                if (facing == null || facing.Value.Length < 1e-6)
                {
                    // 兼容：缺失面向方向时，使用线段朝向的正交方向
                    var dir = line.Direction;
                    if (dir.Length < 1e-6)
                    {
                        _logger.LogWarning("门 {Id} 线段长度过小，无法推断面向方向，跳过", opening.Id);
                        continue;
                    }
                    facing = new Vec2D(-dir.Y, dir.X);
                    _logger.LogDebug("门 {Id} 缺少面向方向，已根据线段推断为 {Facing}", opening.Id, facing.Value);
                }
                var facingVec = facing.Value;
                var doorWidth = line.Length;

                if (doorWidth < 1) // 门宽太小，忽略
                {
                    _logger.LogDebug("门 {Id} 宽度过小 ({Width}mm)，跳过", opening.Id, doorWidth);
                    continue;
                }

                // 根据门操作方式和门扇数决定禁区扩展距离
                var isSliding = opening.DoorOperation == DoorOperationType.Sliding;
                var isDoubleDoor = opening.HandDirections?.Count >= 2;
                var extensionDistance = isSliding ? 300.0
                    : isDoubleDoor ? doorWidth / 2.0  // 双开门：每扇宽 doorWidth/2
                    : doorWidth;                       // 单开门：整个门宽
                var swingName = isSliding ? "通行禁区" : "门扇禁区";
                var swingReasonPrefix = isSliding ? "door_passage" : "door_swing";

                // 计算单向禁区矩形边界
                var offset = facingVec * extensionDistance;
                var reverseOffset = facingVec * (-extensionDistance);

                // Zone A：面向方向（门扇开启侧），前端可见
                var swingVertices = new[]
                {
                    line.Start,
                    line.End,
                    line.End + offset,
                    line.Start + offset
                };

                // Zone B：反向（门前净空侧），前端不可见
                var clearanceVertices = new[]
                {
                    line.Start + reverseOffset,
                    line.End + reverseOffset,
                    line.End,
                    line.Start
                };

                exclusionIndex++;

                var swingExclusion = new Zone
                {
                    Id = $"ez_{exclusionIndex}a",
                    Name = swingName,
                    RoomId = string.Empty,
                    Type = ZoneType.Exclusion,
                    Reason = $"{swingReasonPrefix}:门 {opening.Id} 的门扇开启区域",
                    RawBoundary = new Polygon2D(swingVertices),
                    ComputedBoundary = null,
                    Visible = true,
                    Tags = new List<ZoneTag>(),
                    OptionalTags = new List<ZoneTag>(),
                    FinishRequirements = new List<FinishRequirement>(),
                    SchemeId = null
                };

                var clearanceExclusion = new Zone
                {
                    Id = $"ez_{exclusionIndex}b",
                    Name = "门前净空区",
                    RoomId = string.Empty,
                    Type = ZoneType.Exclusion,
                    Reason = $"door_clearance:门 {opening.Id} 的门前净空区域",
                    RawBoundary = new Polygon2D(clearanceVertices),
                    ComputedBoundary = null,
                    Visible = false,
                    Tags = new List<ZoneTag>(),
                    OptionalTags = new List<ZoneTag>(),
                    FinishRequirements = new List<FinishRequirement>(),
                    SchemeId = null
                };

                result.Add(swingExclusion);
                result.Add(clearanceExclusion);
                _logger.LogDebug("生成{SwingName}: {SwingId} + 门前净空区: {ClearanceId} (源门: {DoorId}), 扩展距离={Dist}mm",
                    swingName, swingExclusion.Id, clearanceExclusion.Id, opening.Id, extensionDistance);
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
        /// ID 格式: rz_{序号}
        /// </summary>
        private List<Zone> CalculateRoomZones(List<Room> rooms)
        {
            var result = new List<Zone>();
            var roomZoneIndex = 0;

            foreach (var room in rooms)
            {
                // 必须有边界
                if (room.Boundary == null || room.Boundary.Vertices.Length < 3)
                {
                    _logger.LogWarning("房间 {Id} 缺少有效边界数据，跳过", room.Id);
                    continue;
                }

                roomZoneIndex++;
                var zone = new Zone
                {
                    Id = $"rz_{roomZoneIndex}",
                    Name = room.Name,
                    RoomId = room.Id,
                    Type = ZoneType.Room,
                    Reason = $"room:{room.Type}",
                    RawBoundary = room.Boundary,
                    ComputedBoundary = null, // Room 类型暂不计算内缩边界
                    Visible = true,
                    Tags = _tagMappingService.GetTagsForRoomType(room.Type),
                    OptionalTags = _tagMappingService.GetOptionalTagsForRoomType(room.Type),
                    FinishRequirements = new List<FinishRequirement>(),
                    SchemeId = null
                };

                result.Add(zone);
                _logger.LogDebug("生成房间区域: {Id} (源房间: {RoomId}), 名称={Name}, 类型={Type}",
                    zone.Id, room.Id, room.Name, room.Type);
            }

            return result;
        }
    }
}
