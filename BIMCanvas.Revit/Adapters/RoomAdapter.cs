using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using BIMCanvas.Core.Models.Document;

namespace BIMCanvas.Revit.Adapters
{
    /// <summary>
    /// 房间边界提取适配器
    /// </summary>
    public class RoomAdapter
    {
        private readonly CoordinateAdapter _coordAdapter;

        /// <summary>
        /// 创建房间适配器
        /// </summary>
        /// <param name="coordAdapter">坐标转换器</param>
        public RoomAdapter(CoordinateAdapter coordAdapter)
        {
            _coordAdapter = coordAdapter ?? throw new ArgumentNullException(nameof(coordAdapter));
        }

        /// <summary>
        /// 提取视图中所有房间
        /// </summary>
        /// <param name="view">Revit 平面视图</param>
        /// <returns>Room 列表，每个包含 Id、Name、Boundary</returns>
        /// <remarks>
        /// TODO: 用户填充实现
        ///
        /// 实现提示：
        /// 1. 收集当前标高的房间
        ///    var collector = new FilteredElementCollector(doc)
        ///        .OfCategory(BuiltInCategory.OST_Rooms)
        ///        .OfClass(typeof(SpatialElement));
        ///
        /// 2. 检查房间是否在当前标高
        ///    if (room.Level?.Id != levelId) continue;
        ///
        /// 3. 获取房间边界
        ///    var options = new SpatialElementBoundaryOptions
        ///    {
        ///        SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
        ///    };
        ///    var boundaries = room.GetBoundarySegments(options);
        ///
        /// 4. 使用 _coordAdapter.ToPoint2D() 转换坐标
        ///
        /// 5. 提取房间名称
        ///    var name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString();
        ///
        /// 注意：Room.Type 留空或设为默认值，后续由 RoomTypeInferrer 自动推断
        /// </remarks>
        public List<Room> ExtractRooms(View view)
        {
            // TODO: 用户填充实现
            throw new NotImplementedException("请实现 RoomAdapter.ExtractRooms 方法");
        }

        /// <summary>
        /// 坐标转换器（供子类使用）
        /// </summary>
        protected CoordinateAdapter CoordAdapter => _coordAdapter;
    }
}
