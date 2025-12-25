using System;
using System.Collections.Generic;
using System.Linq;
using BIMCanvas.Core.Converters;
using BIMCanvas.Core.Models.Geometry;
using BIMCanvas.Core.Models.Revit;
using BIMCanvas.Revit.Models;
using BIMCanvas.Revit.Services;
using NetTopologySuite.Geometries;

namespace BIMCanvas.Revit.Adapters
{
    /// <summary>
    /// 完成面定位线提取适配器
    /// 从墙面完成面边界中提取定位线，关联墙体和房间
    /// </summary>
    public class LocationLineAdapter
    {
        private readonly CoordinateTransformer _transformer;

        /// <summary>
        /// 创建定位线适配器
        /// </summary>
        /// <param name="transformer">坐标转换器</param>
        public LocationLineAdapter(CoordinateTransformer transformer)
        {
            _transformer = transformer ?? throw new ArgumentNullException(nameof(transformer));
        }

        /// <summary>
        /// 从过滤后的墙面完成面边界提取定位线
        /// </summary>
        /// <param name="filteredWallFinishes">过滤后的墙面完成面（仅内墙边）</param>
        /// <param name="revitRooms">房间列表</param>
        /// <param name="revitWalls">墙体列表</param>
        /// <returns>定位线列表</returns>
        public List<LocationLine> ExtractLocationLines(
            List<RevitWallFinish> filteredWallFinishes,
            List<RevitRoom> revitRooms,
            List<RevitWall> revitWalls)
        {
            var result = new List<LocationLine>();
            var idCounter = 1;

            foreach (var wallFinish in filteredWallFinishes)
            {
                if (wallFinish.Boundary == null)
                    continue;

                var shell = wallFinish.Boundary.Shell;

                // 遍历轮廓的每条边
                for (int i = 0; i < shell.NumPoints - 1; i++)
                {
                    var p0 = shell.GetCoordinateN(i);
                    var p1 = shell.GetCoordinateN(i + 1);

                    // 计算边长
                    var length = Math.Sqrt(
                        Math.Pow(p1.X - p0.X, 2) +
                        Math.Pow(p1.Y - p0.Y, 2));

                    // 忽略零长度边
                    if (length < 0.001)
                        continue;

                    // 查找所属房间
                    var roomId = FindAssociatedRoom(p0, p1, revitRooms);

                    // 查找所属墙体
                    var wallId = FindAssociatedWall(p0, p1, revitWalls, wallFinish.ElementIds);

                    // 转换坐标到 BIMCanvas 坐标系
                    var transformedLine = _transformer.TransformLineSegment(
                        new LineSegment(p0, p1));
                    var line2D = NtsConverter.FromNtsLineSegment(transformedLine);

                    // 计算转换后的长度（毫米）
                    var lengthMm = UnitConverter.ToMillimeters(length);

                    result.Add(new LocationLine
                    {
                        Id = $"ll{idCounter++:D3}",
                        WallId = wallId,
                        RoomId = roomId,
                        Side = "interior",
                        Line = line2D,
                        Length = lengthMm
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// 查找边所属的房间
        /// </summary>
        private string FindAssociatedRoom(Coordinate p0, Coordinate p1, List<RevitRoom> rooms)
        {
            // 计算边的中点
            var midX = (p0.X + p1.X) / 2;
            var midY = (p0.Y + p1.Y) / 2;

            // 计算边的内侧法向（逆时针轮廓的右侧）
            var dx = p1.X - p0.X;
            var dy = p1.Y - p0.Y;
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6)
                return string.Empty;

            // 垂直于边的方向（右侧）
            var normalX = dy / len;
            var normalY = -dx / len;

            // 在中点内侧方向偏移一小段距离（0.1 feet ≈ 30mm）
            var testPoint = new Point(new Coordinate(
                midX + normalX * 0.1,
                midY + normalY * 0.1));

            // 检查测试点在哪个房间内
            foreach (var room in rooms)
            {
                if (room.Boundary != null && room.Boundary.Contains(testPoint))
                {
                    return room.Id;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// 查找边所属的墙体
        /// </summary>
        private string FindAssociatedWall(
            Coordinate p0,
            Coordinate p1,
            List<RevitWall> walls,
            List<int> wallFinishElementIds)
        {
            // 首先尝试根据 ElementIds 匹配
            if (wallFinishElementIds != null && wallFinishElementIds.Count > 0)
            {
                foreach (var wall in walls)
                {
                    if (wallFinishElementIds.Contains(wall.ElementId))
                    {
                        // 进一步验证边是否在墙体轮廓附近
                        if (IsEdgeNearWall(p0, p1, wall))
                        {
                            return wall.Id;
                        }
                    }
                }
            }

            // 降级：通过几何距离查找最近的墙体
            return FindNearestWall(p0, p1, walls);
        }

        /// <summary>
        /// 判断边是否在墙体轮廓附近
        /// </summary>
        private bool IsEdgeNearWall(Coordinate p0, Coordinate p1, RevitWall wall)
        {
            if (wall.Boundary == null)
                return false;

            var edgeLine = new LineString(new[] { p0, p1 });
            var distance = wall.Boundary.Distance(edgeLine);

            // 容差：0.5 feet ≈ 150mm
            return distance < 0.5;
        }

        /// <summary>
        /// 查找最近的墙体
        /// </summary>
        private string FindNearestWall(Coordinate p0, Coordinate p1, List<RevitWall> walls)
        {
            var edgeLine = new LineString(new[] { p0, p1 });
            string nearestWallId = string.Empty;
            double minDistance = double.MaxValue;

            foreach (var wall in walls)
            {
                if (wall.Boundary == null)
                    continue;

                var distance = wall.Boundary.Distance(edgeLine);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearestWallId = wall.Id;
                }
            }

            // 距离阈值：2 feet ≈ 600mm
            if (minDistance < 2.0)
            {
                return nearestWallId;
            }

            return string.Empty;
        }
    }
}
