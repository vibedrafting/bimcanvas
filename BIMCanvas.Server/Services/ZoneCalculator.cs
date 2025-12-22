using BIMCanvas.Core.Models.CanvasData;
using BIMCanvas.Core.Models.Primitives;
using BIMCanvas.Core.Models.RevitSource;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// Zone 计算服务
    /// 负责计算门扇禁区、完成面禁区等
    /// </summary>
    public class ZoneCalculator
    {
        private readonly ILogger<ZoneCalculator> _logger;
        private int _exclusionIdCounter = 0;

        public ZoneCalculator(ILogger<ZoneCalculator> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 处理 CanvasDocument，计算并填充禁区数据
        /// </summary>
        public CanvasDocument Process(CanvasDocument document)
        {
            _exclusionIdCounter = 0;

            // 1. 如果 zones 为空但 rooms 存在，自动从 rooms 创建 zones
            if ((document.Zones == null || document.Zones.Count == 0) &&
                document.Rooms != null && document.Rooms.Count > 0)
            {
                document.Zones = CreateZonesFromRooms(document.Rooms);
                _logger.LogInformation("从 Rooms 创建 Zones: {Count} 个", document.Zones.Count);
            }

            // 2. 清空现有禁区（始终重新计算）
            ClearExclusionAreas(document);

            // 3. 计算门扇禁区
            var doorSwingAreas = CalculateDoorSwingAreas(document.Openings);
            _logger.LogInformation("计算门扇禁区: {Count} 个", doorSwingAreas.Count);

            // 4. 将禁区分配到对应的 Zone
            AssignExclusionAreasToZones(document.Zones, doorSwingAreas);

            return document;
        }

        /// <summary>
        /// 从 Rooms 创建 Zones（1:1 映射）
        /// </summary>
        private List<Zone> CreateZonesFromRooms(List<Room> rooms)
        {
            var zones = new List<Zone>();
            int index = 1;

            foreach (var room in rooms)
            {
                var zone = new Zone
                {
                    Id = $"z{index++}",
                    Name = room.Name ?? $"Zone {index}",
                    RoomId = room.Id,
                    RawBoundary = room.Boundary,
                    InnerBoundary = room.Boundary // 暂时直接使用边界，后续可扣除完成面
                };

                zones.Add(zone);
            }

            return zones;
        }

        /// <summary>
        /// 清空所有 Zone 的禁区
        /// </summary>
        private void ClearExclusionAreas(CanvasDocument document)
        {
            foreach (var zone in document.Zones)
            {
                zone.ExclusionAreas.Clear();
            }
        }

        /// <summary>
        /// 计算所有门的门扇禁区
        /// </summary>
        private List<ExclusionArea> CalculateDoorSwingAreas(List<Opening> openings)
        {
            var result = new List<ExclusionArea>();

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

                var exclusionArea = CalculateDoorSwingArea(opening);
                if (exclusionArea != null)
                {
                    result.Add(exclusionArea);
                }
            }

            return result;
        }

        /// <summary>
        /// 计算单个门的门扇禁区
        /// </summary>
        private ExclusionArea? CalculateDoorSwingArea(Opening door)
        {
            var line = door.Line!;
            var facing = door.FacingDirection!.Value;

            // 门宽 = 线段长度
            var doorWidth = line.Length;
            if (doorWidth < 1) // 门宽太小，忽略
                return null;

            // 计算禁区矩形的四个顶点
            // 沿 facing 方向扩展 doorWidth 距离
            var boundary = CalculateSwingBoundary(line.Start, line.End, facing, doorWidth);

            return new ExclusionArea
            {
                Id = $"ex_{++_exclusionIdCounter}",
                Type = ExclusionType.DoorSwing,
                Boundary = boundary
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

        /// <summary>
        /// 将禁区分配到对应的 Zone
        /// 基于禁区中心点判断归属
        /// </summary>
        private void AssignExclusionAreasToZones(List<Zone> zones, List<ExclusionArea> exclusionAreas)
        {
            foreach (var exclusion in exclusionAreas)
            {
                if (exclusion.Boundary == null)
                    continue;

                // 计算禁区中心点
                var center = exclusion.Boundary.ComputeCenter();

                // 查找包含该中心点的 Zone
                var targetZone = FindZoneContainingPoint(zones, center);
                if (targetZone != null)
                {
                    targetZone.ExclusionAreas.Add(exclusion);
                    _logger.LogDebug("禁区 {Id} 分配到 Zone {ZoneId}", exclusion.Id, targetZone.Id);
                }
                else
                {
                    // 如果没有找到包含该点的 Zone，分配到第一个 Zone（兜底）
                    if (zones.Count > 0)
                    {
                        zones[0].ExclusionAreas.Add(exclusion);
                        _logger.LogWarning("禁区 {Id} 未找到对应 Zone，分配到 Zone {ZoneId}", exclusion.Id, zones[0].Id);
                    }
                }
            }
        }

        /// <summary>
        /// 查找包含指定点的 Zone
        /// </summary>
        private Zone? FindZoneContainingPoint(List<Zone> zones, Point2D point)
        {
            foreach (var zone in zones)
            {
                // 优先使用 RawBoundary，其次使用 InnerBoundary
                var boundary = zone.RawBoundary ?? zone.InnerBoundary;
                if (boundary == null)
                    continue;

                if (IsPointInPolygon(point, boundary))
                {
                    return zone;
                }
            }
            return null;
        }

        /// <summary>
        /// 判断点是否在多边形内（射线法）
        /// </summary>
        private bool IsPointInPolygon(Point2D point, Polygon2D polygon)
        {
            var vertices = polygon.Vertices;
            if (vertices.Length < 3)
                return false;

            int n = vertices.Length;
            bool inside = false;

            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var vi = vertices[i];
                var vj = vertices[j];

                if ((vi.Y > point.Y) != (vj.Y > point.Y) &&
                    point.X < (vj.X - vi.X) * (point.Y - vi.Y) / (vj.Y - vi.Y) + vi.X)
                {
                    inside = !inside;
                }
            }

            return inside;
        }
    }
}
