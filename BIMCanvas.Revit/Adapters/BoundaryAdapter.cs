using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using BIMCanvas.Core.Models.Document;

namespace BIMCanvas.Revit.Adapters
{
    /// <summary>
    /// 边界轮廓提取适配器
    /// </summary>
    public class BoundaryAdapter
    {
        private readonly CoordinateAdapter _coordAdapter;

        /// <summary>
        /// 创建边界适配器
        /// </summary>
        /// <param name="coordAdapter">坐标转换器</param>
        public BoundaryAdapter(CoordinateAdapter coordAdapter)
        {
            _coordAdapter = coordAdapter ?? throw new ArgumentNullException(nameof(coordAdapter));
        }

        /// <summary>
        /// 提取视图中所有边界的轮廓
        /// </summary>
        /// <param name="view">Revit 平面视图</param>
        /// <returns>Boundary 列表，每个 Boundary 包含 Id 和 Polygon</returns>
        /// <remarks>
        /// TODO: 用户填充实现
        ///
        /// 实现提示：
        /// 1. 使用 FilteredElementCollector 收集视图中的边界
        ///    var collector = new FilteredElementCollector(doc, view.Id)
        ///        .OfClass(typeof(Autodesk.Revit.DB.Boundary));
        ///
        /// 2. 对每个边界，获取几何并提取底面轮廓
        ///    var geometry = wall.get_Geometry(new Options { View = view });
        ///
        /// 3. 使用 _coordAdapter.ToPoint2D() 转换坐标
        ///
        /// 4. 创建 Polygon2D 并封装为 Boundary 对象
        /// </remarks>
        public List<Boundary> ExtractBoundarys(View view)
        {
            // TODO: 用户填充实现
            throw new NotImplementedException("请实现 BoundaryAdapter.ExtractBoundarys 方法");
        }

        /// <summary>
        /// 坐标转换器（供子类使用）
        /// </summary>
        protected CoordinateAdapter CoordAdapter => _coordAdapter;
    }
}
