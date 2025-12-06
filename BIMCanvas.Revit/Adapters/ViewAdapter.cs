using System;
using Autodesk.Revit.DB;
using BIMCanvas.Core.Models.Document;

namespace BIMCanvas.Revit.Adapters
{
    /// <summary>
    /// 视图元数据提取适配器
    /// </summary>
    public class ViewAdapter
    {
        private readonly CoordinateAdapter _coordAdapter;

        /// <summary>
        /// 创建视图适配器
        /// </summary>
        /// <param name="coordAdapter">坐标转换器</param>
        public ViewAdapter(CoordinateAdapter coordAdapter)
        {
            _coordAdapter = coordAdapter ?? throw new ArgumentNullException(nameof(coordAdapter));
        }

        /// <summary>
        /// 提取视图元数据
        /// </summary>
        /// <param name="view">Revit 视图</param>
        /// <returns>元数据对象</returns>
        /// <remarks>
        /// TODO: 用户填充实现
        /// 需要提取：
        /// - RevitViewId: view.Id.IntegerValue
        /// - LevelId: 视图关联的标高 ID
        /// - GridSize: 默认 500mm
        /// </remarks>
        public Metadata ExtractMetadata(View view)
        {
            // TODO: 用户填充实现
            throw new NotImplementedException("请实现 ViewAdapter.ExtractMetadata 方法");
        }

        /// <summary>
        /// 获取视图关联的标高 ID
        /// </summary>
        /// <param name="view">Revit 视图</param>
        /// <returns>标高元素 ID</returns>
        /// <remarks>
        /// TODO: 用户填充实现
        /// 提示：ViewPlan 有 GenLevel 属性
        /// </remarks>
        public ElementId GetLevelId(View view)
        {
            // TODO: 用户填充实现
            throw new NotImplementedException("请实现 ViewAdapter.GetLevelId 方法");
        }

        /// <summary>
        /// 坐标转换器（供子类使用）
        /// </summary>
        protected CoordinateAdapter CoordAdapter => _coordAdapter;
    }
}
