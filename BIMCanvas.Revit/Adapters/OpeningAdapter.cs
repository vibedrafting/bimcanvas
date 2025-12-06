using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using BIMCanvas.Core.Models.Document;

namespace BIMCanvas.Revit.Adapters
{
    /// <summary>
    /// 门窗线段提取适配器
    /// </summary>
    public class OpeningAdapter
    {
        private readonly CoordinateAdapter _coordAdapter;

        /// <summary>
        /// 创建门窗适配器
        /// </summary>
        /// <param name="coordAdapter">坐标转换器</param>
        public OpeningAdapter(CoordinateAdapter coordAdapter)
        {
            _coordAdapter = coordAdapter ?? throw new ArgumentNullException(nameof(coordAdapter));
        }

        /// <summary>
        /// 提取视图中所有门窗的定位线段
        /// </summary>
        /// <param name="view">Revit 平面视图</param>
        /// <returns>Opening 列表，每个包含 Id、Type、Line</returns>
        /// <remarks>
        /// TODO: 用户填充实现
        ///
        /// 实现提示：
        /// 1. 分别收集门和窗
        ///    var doors = new FilteredElementCollector(doc, view.Id)
        ///        .OfCategory(BuiltInCategory.OST_Doors)
        ///        .OfClass(typeof(FamilyInstance));
        ///
        ///    var windows = new FilteredElementCollector(doc, view.Id)
        ///        .OfCategory(BuiltInCategory.OST_Windows)
        ///        .OfClass(typeof(FamilyInstance));
        ///
        /// 2. 获取门/窗的宿主墙和位置
        ///    var host = instance.Host as Autodesk.Revit.DB.Wall;
        ///    var location = instance.Location as LocationPoint;
        ///
        /// 3. 计算门/窗线段（沿墙方向，宽度为门/窗宽度）
        ///
        /// 4. 使用 _coordAdapter.ToPoint2D() 转换坐标
        ///
        /// 5. 设置正确的 OpeningType (Door/Window)
        /// </remarks>
        public List<Core.Models.Document.Opening> ExtractOpenings(View view)
        {
            // TODO: 用户填充实现
            throw new NotImplementedException("请实现 OpeningAdapter.ExtractOpenings 方法");
        }

        /// <summary>
        /// 坐标转换器（供子类使用）
        /// </summary>
        protected CoordinateAdapter CoordAdapter => _coordAdapter;
    }
}
