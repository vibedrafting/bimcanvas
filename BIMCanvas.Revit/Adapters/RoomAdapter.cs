using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using BIMCanvas.Revit.Models;

namespace BIMCanvas.Revit.Adapters
{
    /// <summary>
    /// 房间边界提取适配器
    /// </summary>
    public class RoomAdapter
    {
        /// <summary>
        /// 提取视图中所有房间（返回 Revit 原生数据）
        /// </summary>
        /// <param name="view">Revit 平面视图</param>
        /// <returns>RawRoom 列表（未转换坐标）</returns>
        public List<RawRoom> ExtractRooms(View view)
        {
            if (view == null)
                throw new ArgumentNullException(nameof(view));

            var doc = view.Document;
            var result = new List<RawRoom>();

            // 1. 收集所有房间
            var rooms = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .ToList();

            // 2. 提取每个房间的数据
            foreach (var room in rooms)
            {
                try
                {
                    // 2.1 跳过未放置的房间
                    if (room.Area <= 0 || room.Location == null)
                        continue;

                    // 2.2 获取房间名称
                    var name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "未命名房间";

                    // 2.3 获取房间边界
                    var options = new SpatialElementBoundaryOptions
                    {
                        SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
                    };

                    var boundarySegments = room.GetBoundarySegments(options);
                    if (boundarySegments == null || boundarySegments.Count == 0)
                        continue;

                    // 2.4 转换边界为 CurveLoop
                    var loops = new List<CurveLoop>();
                    foreach (var segments in boundarySegments)
                    {
                        var curves = new List<Curve>();
                        foreach (BoundarySegment segment in segments)
                        {
                            curves.Add(segment.GetCurve());
                        }

                        if (curves.Count > 0)
                        {
                            try
                            {
                                var loop = CurveLoop.Create(curves);
                                loops.Add(loop);
                            }
                            catch
                            {
                                // 创建 CurveLoop 失败，跳过
                                continue;
                            }
                        }
                    }

                    if (loops.Count == 0)
                        continue;

                    // 2.5 创建 RawRoom
                    result.Add(new RawRoom
                    {
                        Id = $"room_{Guid.NewGuid():N}",
                        Name = name,
                        Loops = loops.ToArray()
                    });
                }
                catch
                {
                    // 提取失败，跳过该房间
                    continue;
                }
            }

            return result;
        }
    }
}
