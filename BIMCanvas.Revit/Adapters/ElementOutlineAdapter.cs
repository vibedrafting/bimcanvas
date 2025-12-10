using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BIMCanvas.Revit.Models;
using BIMCanvas.Revit.Utilities;
using NetTopologySuite.Geometries;

namespace BIMCanvas.Revit.Adapters
{
    /// <summary>
    /// 单构件轮廓提取适配器（备用）
    /// 按单个构件独立提取轮廓，使用 BoundingBox 生成矩形
    /// </summary>
    public class ElementOutlineAdapter
    {
        /// <summary>
        /// 提取单构件轮廓列表
        /// </summary>
        /// <param name="view">Revit 平面视图</param>
        /// <returns>ElementOutline 列表（每个构件独立）</returns>
        public List<ElementOutline> ExtractOutlines(View view)
        {
            if (view == null)
                throw new ArgumentNullException(nameof(view));

            var doc = view.Document;
            var result = new List<ElementOutline>();

            // 收集并处理墙体
            PrefixId.Reset("wall_");
            var walls = CollectElements(doc, view, BuiltInCategory.OST_Walls);
            foreach (var wall in walls)
            {
                var outline = ExtractSingleOutline(wall, OutlineElementType.Wall, "wall_");
                if (outline != null)
                    result.Add(outline);
            }

            // 收集并处理建筑柱
            PrefixId.Reset("col_");
            var columns = CollectElements(doc, view, BuiltInCategory.OST_Columns);
            foreach (var column in columns)
            {
                var outline = ExtractSingleOutline(column, OutlineElementType.Column, "col_");
                if (outline != null)
                    result.Add(outline);
            }

            // 收集并处理结构柱
            PrefixId.Reset("scol_");
            var structuralColumns = CollectElements(doc, view, BuiltInCategory.OST_StructuralColumns);
            foreach (var column in structuralColumns)
            {
                var outline = ExtractSingleOutline(column, OutlineElementType.StructuralColumn, "scol_");
                if (outline != null)
                    result.Add(outline);
            }

            return result;
        }

        /// <summary>
        /// 收集指定类别的构件
        /// </summary>
        private List<Element> CollectElements(Document doc, View view, BuiltInCategory category)
        {
            return new FilteredElementCollector(doc, view.Id)
                .OfCategory(category)
                .WhereElementIsNotElementType()
                .ToElements()
                .ToList();
        }

        /// <summary>
        /// 提取单个构件的轮廓（使用 BoundingBox）
        /// </summary>
        private ElementOutline ExtractSingleOutline(Element element, OutlineElementType elementType, string idPrefix)
        {
            try
            {
                var polygon = ExtractBoundingBoxOutline(element);
                if (polygon == null)
                    return null;

                return new ElementOutline
                {
                    Id = PrefixId.NewId(idPrefix, 3),
                    ElementId = element.Id.IntegerValue,
                    Type = elementType,
                    Boundary = polygon
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 从 BoundingBox 生成矩形轮廓
        /// </summary>
        /// <param name="element">Revit 元素</param>
        /// <returns>NTS Polygon（矩形，feet 单位）</returns>
        private Polygon ExtractBoundingBoxOutline(Element element)
        {
            var bbox = element.get_BoundingBox(null);
            if (bbox == null)
                return null;

            // 从 BoundingBox 构建矩形（XY 平面投影）
            double minX = bbox.Min.X;
            double minY = bbox.Min.Y;
            double maxX = bbox.Max.X;
            double maxY = bbox.Max.Y;

            // 构建闭合矩形坐标（逆时针）
            var coordinates = new[]
            {
                new Coordinate(minX, minY),
                new Coordinate(maxX, minY),
                new Coordinate(maxX, maxY),
                new Coordinate(minX, maxY),
                new Coordinate(minX, minY)  // 闭合
            };

            return new Polygon(new LinearRing(coordinates));
        }
    }
}
