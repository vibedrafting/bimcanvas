using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using BIMCanvas.Core.Converters;
using BIMCanvas.Revit.Models;
using BIMCanvas.Revit.Utilities;

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
        /// 提取视图中所有边界的轮廓（Revit 原生坐标）
        /// </summary>
        /// <param name="view">Revit 平面视图</param>
        /// <param name="cutHeightMm">切割高度（毫米）</param>
        /// <returns>RevitBoundary 列表，包含 Revit 原生 XYZ 坐标</returns>
        /// <exception cref="ArgumentNullException">view 为空</exception>
        public List<RevitBoundary> ExtractBoundarys(View view, double cutHeightMm)
        {
            if (view == null)
                throw new ArgumentNullException(nameof(view));

            var doc = view.Document;
            var result = new List<RevitBoundary>();

            // Step 1: 单位转换 (mm → feet)
            double cutHeightFeet = UnitConverter.ToFeet(cutHeightMm);

            // Step 2: 定义目标构件类别
            var categories = new[]
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Columns,
                BuiltInCategory.OST_StructuralColumns
            };

            // Step 3: 使用 OutlineExtractor 提取轮廓
            List<CurveLoop> outlines;
            try
            {
                outlines = OutlineExtractor.GetOutlines(
                    doc,
                    categories,
                    height: cutHeightFeet,
                    view: view
                );
            }
            catch
            {
                // 提取失败，返回空列表
                return result;
            }

            // Step 4: 空轮廓检查
            if (outlines == null || outlines.Count == 0)
                return result;

            // Step 5: 重置 ID 计数器
            DataId.Reset("b");

            // Step 6: 转换每个 CurveLoop 为 RevitBoundary
            foreach (var curveLoop in outlines)
            {
                try
                {
                    // 6.1 提取顶点（Revit 原生 XYZ 坐标）
                    var vertices = new List<XYZ>();
                    foreach (Curve curve in curveLoop)
                    {
                        // 只取起点，形成闭合多边形（终点 = 下一条曲线的起点）
                        var revitPoint = curve.GetEndPoint(0);
                        vertices.Add(revitPoint);
                    }

                    // 6.2 验证顶点数量（少于 3 个无法形成有效多边形）
                    if (vertices.Count < 3)
                        continue;

                    // 6.3 创建 RevitBoundary
                    result.Add(new RevitBoundary
                    {
                        Id = DataId.NewId("b", 1),  // 生成 b1, b2, b3...
                        Vertices = vertices
                    });
                }
                catch
                {
                    // 转换失败，跳过该轮廓，不影响其他轮廓
                    continue;
                }
            }

            return result;
        }

        /// <summary>
        /// 坐标转换器（供子类使用）
        /// </summary>
        protected CoordinateAdapter CoordAdapter => _coordAdapter;
    }
}
