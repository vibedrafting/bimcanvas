using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using BIMCanvas.Core.Models.Document;

namespace BIMCanvas.Revit.Adapters
{
    /// <summary>
    /// 墙体轮廓提取适配器
    /// </summary>
    public class WallAdapter
    {
        private readonly CoordinateAdapter _coordAdapter;

        /// <summary>
        /// 创建墙体适配器
        /// </summary>
        /// <param name="coordAdapter">坐标转换器</param>
        public WallAdapter(CoordinateAdapter coordAdapter)
        {
            _coordAdapter = coordAdapter ?? throw new ArgumentNullException(nameof(coordAdapter));
        }

        /// <summary>
        /// 提取视图中所有墙体的轮廓
        /// </summary>
        /// <param name="view">Revit 平面视图</param>
        /// <returns>Wall 列表，每个 Wall 包含 Id 和 Polygon</returns>
        /// <remarks>
        /// TODO: 用户填充实现
        ///
        /// 实现提示：
        /// 1. 使用 FilteredElementCollector 收集视图中的墙体
        ///    var collector = new FilteredElementCollector(doc, view.Id)
        ///        .OfClass(typeof(Autodesk.Revit.DB.Wall));
        ///
        /// 2. 对每个墙体，获取几何并提取底面轮廓
        ///    var geometry = wall.get_Geometry(new Options { View = view });
        ///
        /// 3. 使用 _coordAdapter.ToPoint2D() 转换坐标
        ///
        /// 4. 创建 Polygon2D 并封装为 Wall 对象
        /// </remarks>
        public List<Core.Models.Document.Wall> ExtractWalls(View view)
        {
            // TODO: 用户填充实现
            throw new NotImplementedException("请实现 WallAdapter.ExtractWalls 方法");
        }

        /// <summary>
        /// 坐标转换器（供子类使用）
        /// </summary>
        protected CoordinateAdapter CoordAdapter => _coordAdapter;
    }
}
