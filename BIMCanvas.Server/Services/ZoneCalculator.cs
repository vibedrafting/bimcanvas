using BIMCanvas.Core.Models.CanvasData;
using BIMCanvas.Core.Models.Document;
using BIMCanvas.Core.Models.Primitives;
using BIMCanvas.Core.Models.RevitSource;
using BIMCanvas.Core.Models.Shared;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// Zone 计算服务
    /// 负责从 Rooms 创建 Zone、计算门扇禁区等
    /// </summary>
    public class ZoneCalculator
    {
        private readonly ILogger<ZoneCalculator> _logger;
        private int _zoneIdCounter = 0;

        public ZoneCalculator(ILogger<ZoneCalculator> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 处理 DesignDocument，创建 Zone 并计算禁区
        /// </summary>
        public DesignDocument Process(DesignDocument document)
        {
            _zoneIdCounter = 0;

            // 确保 Computed 子结构存在
            document.Computed ??= new ComputedData();
            document.Computed.Zones ??= new List<Zone>();

            var zones = document.Computed.Zones;
            var rooms = document.Revit?.Rooms;
            var openings = document.Revit?.Openings;

            // 1. 从 Rooms 创建 Room 类型的 Zone（如果还没有）
            if (!zones.Any(z => z.Type == ZoneType.Room) &&
                rooms != null && rooms.Count > 0)
            {
                var roomZones = CreateZonesFromRooms(rooms);
                zones.AddRange(roomZones);
                _logger.LogInformation("从 Rooms 创建 Room Zone: {Count} 个", roomZones.Count);
            }

            // 2. 移除现有禁区（重新计算）
            zones.RemoveAll(z => z.Type == ZoneType.Exclusion);

            // 3. 计算门扇禁区
            if (openings != null)
            {
                var doorSwingZones = CalculateDoorSwingZones(openings);
                zones.AddRange(doorSwingZones);
                _logger.LogInformation("计算门扇禁区: {Count} 个", doorSwingZones.Count);
            }

            return document;
        }

        /// <summary>
        /// 从 Rooms 创建 Room 类型的 Zone
        /// </summary>
        private List<Zone> CreateZonesFromRooms(List<Room> rooms)
        {
            var zones = new List<Zone>();

            foreach (var room in rooms)
            {
                var zone = new Zone
                {
                    Id = $"z{++_zoneIdCounter}",
                    Name = room.Name ?? $"房间 {_zoneIdCounter}",
                    Type = ZoneType.Room,
                    Reason = "从 Revit Room 自动转换",
                    RawBoundary = room.Boundary,
                    ComputedBoundary = room.Boundary // 暂时相同，后续可扣除完成面
                };

                zones.Add(zone);
            }

            return zones;
        }

        /// <summary>
        /// 计算所有门的门扇禁区
        /// </summary>
        private List<Zone> CalculateDoorSwingZones(List<Opening> openings)
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
                    _logger.LogWarning("门 {Id} 缺少线段或面向方向数据", opening.Id);
                    continue;
                }

                var exclusionZone = CreateDoorSwingZone(opening);
                if (exclusionZone != null)
                {
                    result.Add(exclusionZone);
                }
            }

            return result;
        }

        /// <summary>
        /// 为单个门创建门扇禁区 Zone
        /// </summary>
        private Zone? CreateDoorSwingZone(Opening door)
        {
            var line = door.Line!;
            var facing = door.FacingDirection!.Value;

            // 门宽 = 线段长度
            var doorWidth = line.Length;
            if (doorWidth < 1) // 门宽太小，忽略
                return null;

            // 计算禁区矩形
            var boundary = CalculateSwingBoundary(line.Start, line.End, facing, doorWidth);

            return new Zone
            {
                Id = $"z{++_zoneIdCounter}",
                Name = $"门扇禁区 ({door.Id})",
                Type = ZoneType.Exclusion,
                Reason = $"门 {door.Id} 的开启扫过区域，禁止布置家具",
                RawBoundary = boundary,
                ComputedBoundary = null // 禁区不需要计算轮廓
            };
        }

        /// <summary>
        /// 计算门扇禁区边界
        /// </summary>
        /// <param name="p0">门洞起点</param>
        /// <param name="p1">门洞终点</param>
        /// <param name="facing">面向方向（单位向量）</param>
        /// <param name="doorWidth">门宽</param>
        /// <returns>禁区多边形（4顶点矩形）</returns>
        private Polygon2D CalculateSwingBoundary(Point2D p0, Point2D p1, Vec2D facing, double doorWidth)
        {
            // 计算偏移向量
            var offset = facing * doorWidth;

            // 四个顶点（按逆时针顺序）
            var vertices = new Point2D[]
            {
                p0,                     // 门洞起点
                p1,                     // 门洞终点
                p1 + offset,            // 偏移后终点
                p0 + offset             // 偏移后起点
            };

            return new Polygon2D(vertices);
        }
    }
}
