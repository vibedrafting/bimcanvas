using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using BIMCanvas.Core.Algorithms.Geometries;
using BIMCanvas.Revit.Models;
using BIMCanvas.Revit.Utilities;
using NetTopologySuite.Geometries;
using NtsLineSegment = NetTopologySuite.Geometries.LineSegment;

namespace BIMCanvas.Revit.Adapters
{
    /// <summary>
    /// 房间边界提取适配器
    /// </summary>
    public class RoomAdapter
    {
        /// <summary>
        /// 提取视图中所有房间
        /// </summary>
        /// <param name="view">Revit 平面视图</param>
        /// <returns>RevitRoom 列表（保留 Revit 原生坐标，feet 单位）</returns>
        public List<RevitRoom> ExtractRooms(View view)
        {
            if (view == null)
                throw new ArgumentNullException(nameof(view));

            var doc = view.Document;
            var result = new List<RevitRoom>();

            // 1. 设置柱子作为房间边界
            SetColumnRoomBounding(doc, true);

            // 2. 重置 DataId 计数器
            PrefixId.Reset("room_");

            // 3. 获取视图标高
            var levelId = view.GenLevel?.Id;
            if (levelId == null)
                throw new InvalidOperationException("视图缺少关联标高");

            // 4. 收集房间
            var collector = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .OfClass(typeof(SpatialElement));

            foreach (var element in collector)
            {
                var room = element as Room;
                if (room == null)
                    continue;

                // 5. 过滤条件
                // 5.1 检查标高
                if (room.Level?.Id != levelId)
                    continue;

                // 5.2 检查面积（过滤未放置的房间）
                if (room.Area <= 0)
                    continue;

                // 6. 提取边界
                var boundary = GetRoomBoundary(room);
                if (boundary == null)
                    continue; // 跳过边界为空的房间

                // 7. 提取房间名称
                var name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString()
                           ?? "未命名房间";

                // 8. 创建 RevitRoom 对象
                var revitRoom = new RevitRoom
                {
                    Id = PrefixId.NewId("room_", 3),
                    ElementId = room.Id.IntegerValue,
                    Name = name,
                    Boundary = boundary
                };

                result.Add(revitRoom);
            }

            return result;
        }

        /// <summary>
        /// 获取房间的边界多边形
        /// </summary>
        /// <param name="room">房间元素</param>
        /// <returns>NTS Polygon（保留 Revit 原生坐标，feet 单位）</returns>
        private static Polygon GetRoomBoundary(Room room)
        {
            // 获取房间的边界段
            var options = new SpatialElementBoundaryOptions();
            var boundarySegments = room.GetBoundarySegments(options);

            if (boundarySegments == null || boundarySegments.Count == 0)
                return null;

            // 只处理外环（第一个边界环路）
            var outerLoop = boundarySegments[0];
            if (outerLoop == null || outerLoop.Count < 3)
                return null;

            var lineSegments = new List<NtsLineSegment>();

            foreach (var segment in outerLoop)
            {
                var curve = segment.GetCurve();
                if (curve == null)
                    continue;

                // 只处理直线类型，跳过弧线等
                if (curve is Line line)
                {
                    lineSegments.Add(line.ToLineSegment());
                }
                else
                {
                    // 对于非直线曲线，用首尾点连线近似
                    var startPoint = curve.GetEndPoint(0);
                    var endPoint = curve.GetEndPoint(1);
                    lineSegments.Add(new NtsLineSegment(
                        new Coordinate(startPoint.X, startPoint.Y),
                        new Coordinate(endPoint.X, endPoint.Y)));
                }
            }

            if (lineSegments.Count < 3)
                return null;

            // 生成多边形
            return lineSegments.GeneratePolygon();
        }

        /// <summary>
        /// 设置柱子的房间边界属性
        /// </summary>
        /// <param name="doc">Revit 文档</param>
        /// <param name="enable">是否启用房间边界</param>
        private static void SetColumnRoomBounding(Document doc, bool enable)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc), "文档不能为空");

            try
            {
                // 获取所有柱子元素
                var columns = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralColumns)
                    .WhereElementIsNotElementType()
                    .ToElements()
                    .Union(new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Columns)
                        .WhereElementIsNotElementType()
                        .ToElements())
                    .ToList();

                // 使用事务修改柱子的房间边界属性
                using (var trans = new Transaction(doc, "设置房间边界属性"))
                {
                    trans.Start();
                    foreach (var column in columns)
                    {
                        // 获取并修改房间边界参数
                        var roomBoundingParam = column.get_Parameter(
                            BuiltInParameter.WALL_ATTR_ROOM_BOUNDING);

                        if (roomBoundingParam != null && roomBoundingParam.HasValue)
                        {
                            if (enable)
                            {
                                // 检查当前值，如果为禁用状态则启用
                                if (roomBoundingParam.AsInteger() == 0)
                                {
                                    roomBoundingParam.Set(1); // 1 表示作为房间边界
                                }
                            }
                            else
                            {
                                // 检查当前值，如果为启用状态则禁用
                                if (roomBoundingParam.AsInteger() == 1)
                                {
                                    roomBoundingParam.Set(0); // 0 表示不作为房间边界
                                }
                            }
                        }
                    }
                    trans.Commit();
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("设置柱子房间边界属性失败", ex);
            }
        }
    }
}
