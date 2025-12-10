using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using BIMCanvas.Core.Algorithms.Geometries;
using BIMCanvas.Revit.Models;
using BIMCanvas.Revit.Converters;
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


            //foreach (var item in result)
            //{
            //    doc.DisplayDirectShape(item.Boundary, ColorType.Red);
            //}

            return result;
        }

        /// <summary>
        /// 获取房间的边界多边形（支持外环 + 内环）
        /// </summary>
        /// <param name="room">房间元素</param>
        /// <returns>NTS Polygon（保留 Revit 原生坐标，feet 单位，支持内环）</returns>
        private static Polygon GetRoomBoundary(Room room)
        {
            // 获取房间的边界段
            var options = new SpatialElementBoundaryOptions();
            var boundarySegments = room.GetBoundarySegments(options);

            if (boundarySegments == null || boundarySegments.Count == 0)
                return null;

            // 1. 处理外环（第一个边界环路）
            var outerLoop = boundarySegments[0];
            if (outerLoop == null || outerLoop.Count < 3)
                return null;

            var shellRing = ConvertBoundaryLoopToLinearRing(outerLoop);
            if (shellRing == null)
                return null;

            // 2. 处理内环（第二个及之后的环路）
            var holes = new List<LinearRing>();
            for (int i = 1; i < boundarySegments.Count; i++)
            {
                var innerLoop = boundarySegments[i];
                if (innerLoop == null || innerLoop.Count < 3)
                    continue;

                var holeRing = ConvertBoundaryLoopToLinearRing(innerLoop);
                if (holeRing != null)
                    holes.Add(holeRing);
            }

            // 3. 创建带孔多边形
            return new Polygon(shellRing, holes.Count > 0 ? holes.ToArray() : null);
        }

        /// <summary>
        /// 将 Revit 边界环路转换为 NTS LinearRing
        /// </summary>
        /// <param name="loop">Revit 边界环路</param>
        /// <returns>NTS LinearRing（保留 Revit 原生坐标，feet 单位）</returns>
        private static LinearRing ConvertBoundaryLoopToLinearRing(IList<BoundarySegment> loop)
        {
            var coordinates = new List<Coordinate>();

            foreach (var segment in loop)
            {
                var curve = segment.GetCurve();
                if (curve == null)
                    continue;

                // 获取起点坐标（环路中每段曲线的起点构成完整环）
                var startPoint = curve.GetEndPoint(0);
                coordinates.Add(new Coordinate(startPoint.X, startPoint.Y));
            }

            if (coordinates.Count < 3)
                return null;

            // NTS LinearRing 需要首尾闭合
            coordinates.Add(coordinates[0]);

            return new LinearRing(coordinates.ToArray());
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
