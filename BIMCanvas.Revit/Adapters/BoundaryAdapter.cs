using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BIMCanvas.Core.Converters;
using BIMCanvas.Revit.Models;
using BIMCanvas.Revit.Converters;
using BIMCanvas.Revit.Utilities;
using NetTopologySuite.Geometries;

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
        /// <returns>RevitBoundary 列表（保留 Revit 原生坐标，feet 单位）</returns>
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

            // 使用 OutlineExtractor 提取轮廓（支持外环 + 内环）
            List<(CurveLoop Shell, List<CurveLoop> Holes)> outlines;
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

            // 收集所有墙和柱子元素（用于后续 ElementIds 匹配）
            var wallAndColumnElements = CollectWallAndColumnElements(doc, view, categories);

            // 重置 ID 计数器
            PrefixId.Reset("boundary_");

            // 转换每个轮廓为 RevitBoundary（支持内环）
            foreach (var outline in outlines)
            {
                try
                {
                    if (outline.Shell == null)
                        continue;

                    var polygon = outline.ToPolygon();
                    if (polygon == null)
                        continue;

                    // 查找与该轮廓相交的元素 ID
                    var elementIds = FindIntersectingElementIds(
                        wallAndColumnElements,
                        polygon,
                        cutHeightFeet);

                    result.Add(new RevitBoundary
                    {
                        Id = PrefixId.NewId("boundary_", 3),
                        ElementIds = elementIds,
                        Boundary = polygon
                    });
                }
                catch
                {
                    // 转换失败，跳过该轮廓
                    continue;
                }
            }


            foreach (var item in result)
            {
                doc.DisplayDirectShape(new Polygon(new LinearRing(item.Boundary.ExteriorRing.Coordinates)), ColorType.Red);
                foreach (var ring in item.Boundary.InteriorRings)
                {
                    doc.DisplayLine(new Polygon(new LinearRing(ring.Coordinates)), ColorType.Blue);
                    doc.DisplayLine(new Polygon(new LinearRing(ring.Coordinates)).Centroid.Coordinate, ColorType.Blue);
                }

                System.Windows.MessageBox.Show($"InteriorRings:{item.Boundary.InteriorRings.Count()}");
            }
            System.Windows.MessageBox.Show($"result:{result.Count}");


            return result;
        }

        /// <summary>
        /// 收集视图中所有墙和柱子元素
        /// </summary>
        /// <param name="doc">Revit 文档</param>
        /// <param name="view">平面视图</param>
        /// <param name="categories">构件类别数组</param>
        /// <returns>元素列表</returns>
        private List<Element> CollectWallAndColumnElements(
            Document doc,
            View view,
            BuiltInCategory[] categories)
        {
            var elements = new List<Element>();

            foreach (var category in categories)
            {
                var collector = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(category)
                    .WhereElementIsNotElementType()
                    .ToElements();

                elements.AddRange(collector);
            }

            return elements;
        }

        /// <summary>
        /// 查找与给定多边形轮廓相交的元素 ID 列表
        /// </summary>
        /// <param name="elements">候选元素列表</param>
        /// <param name="boundaryPolygon">边界多边形（NTS Polygon）</param>
        /// <param name="cutHeight">切割高度（英尺）</param>
        /// <returns>相交元素的 ElementId 列表</returns>
        private List<int> FindIntersectingElementIds(
            List<Element> elements,
            Polygon boundaryPolygon,
            double cutHeight)
        {
            var result = new List<int>();

            // 将边界多边形稍微扩展，避免边界精度问题
            var tolerance = 0.01; // 英尺

            foreach (var element in elements)
            {
                try
                {
                    // 获取元素的位置点或边界框中心
                    var elementPoint = GetElementLocationPoint(element, cutHeight);
                    if (elementPoint == null)
                        continue;

                    // 创建 NTS Point
                    var ntsPoint = new NetTopologySuite.Geometries.Point(
                        new Coordinate(elementPoint.X, elementPoint.Y));

                    // 检查点是否在边界多边形内或边界上
                    if (boundaryPolygon.Contains(ntsPoint) ||
                        boundaryPolygon.Distance(ntsPoint) < tolerance)
                    {
                        result.Add(element.Id.IntegerValue);
                    }
                }
                catch
                {
                    // 跳过无法处理的元素
                    continue;
                }
            }

            return result;
        }

        /// <summary>
        /// 获取元素在指定高度的定位点
        /// </summary>
        /// <param name="element">Revit 元素</param>
        /// <param name="cutHeight">切割高度（英尺）</param>
        /// <returns>定位点 XYZ 或 null</returns>
        private XYZ GetElementLocationPoint(Element element, double cutHeight)
        {
            // 尝试从 Location 获取
            if (element.Location is LocationPoint locPoint)
            {
                return new XYZ(locPoint.Point.X, locPoint.Point.Y, cutHeight);
            }

            if (element.Location is LocationCurve locCurve)
            {
                // 对于墙等线性元素，取中点
                var curve = locCurve.Curve;
                var midPoint = curve.Evaluate(0.5, true);
                return new XYZ(midPoint.X, midPoint.Y, cutHeight);
            }

            // 使用边界框中心
            var bbox = element.get_BoundingBox(null);
            if (bbox != null)
            {
                var center = (bbox.Min + bbox.Max) / 2;
                return new XYZ(center.X, center.Y, cutHeight);
            }

            return null;
        }
    }
}
