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
        /// <summary>
        /// 提取边界轮廓（返回 Revit 原生数据）
        /// </summary>
        /// <param name="view">Revit 平面视图</param>
        /// <returns>RawBoundary 列表（未转换坐标）</returns>
        public List<RevitBoundary> ExtractBoundaries(View view)
        {
            if (view == null)
                throw new ArgumentNullException(nameof(view));

            var doc = view.Document;
            var result = new List<RevitBoundary>();

            // 默认切割高度：1200mm（约 4 feet）
            double cutHeightFeet = UnitConverter.ToFeet(1200);

            // 定义目标构件类别
            var categories = new[]
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Columns,
                BuiltInCategory.OST_StructuralColumns
            };

            // 使用 OutlineExtractor 提取轮廓
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

            // 空轮廓检查
            if (outlines == null || outlines.Count == 0)
                return result;

            // 重置 ID 计数器
            DataId.Reset("boundary");

            // 转换每个 CurveLoop 为 RawBoundary
            foreach (var curveLoop in outlines)
            {
                try
                {
                    if (curveLoop != null)
                    {
                        result.Add(new RevitBoundary
                        {
                            Id = DataId.NewId("boundary", 1),
                            Boundary = curveLoop.ToPolygon()
                        });
                    }
                }
                catch
                {
                    // 转换失败，跳过该轮廓
                    continue;
                }
            }

            return result;
        }
    }
}
