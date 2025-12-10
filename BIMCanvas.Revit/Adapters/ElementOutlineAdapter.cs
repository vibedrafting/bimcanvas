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
    /// 单构件轮廓提取适配器（备用）
    /// 按单个构件独立提取轮廓，不做布尔运算合并
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

            // 默认切割高度：1200mm（约 4 feet）
            double cutHeightFeet = UnitConverter.ToFeet(1200);

            // 收集并处理墙体
            var walls = CollectElements(doc, view, BuiltInCategory.OST_Walls);
            foreach (var wall in walls)
            {
                var outline = ExtractSingleOutline(wall, cutHeightFeet, OutlineElementType.Wall);
                if (outline != null)
                    result.Add(outline);
            }

            // 收集并处理建筑柱
            var columns = CollectElements(doc, view, BuiltInCategory.OST_Columns);
            foreach (var column in columns)
            {
                var outline = ExtractSingleOutline(column, cutHeightFeet, OutlineElementType.Column);
                if (outline != null)
                    result.Add(outline);
            }

            // 收集并处理结构柱
            var structuralColumns = CollectElements(doc, view, BuiltInCategory.OST_StructuralColumns);
            foreach (var column in structuralColumns)
            {
                var outline = ExtractSingleOutline(column, cutHeightFeet, OutlineElementType.StructuralColumn);
                if (outline != null)
                    result.Add(outline);
            }

            // 重新分配 ID
            PrefixId.Reset("outline_");
            foreach (var outline in result)
            {
                outline.Id = PrefixId.NewId("outline_", 3);
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
        /// 提取单个构件的轮廓
        /// </summary>
        private ElementOutline ExtractSingleOutline(Element element, double cutHeight, OutlineElementType elementType)
        {
            try
            {
                // 获取元素的 Solid
                var solid = GetElementSolid(element);
                if (solid == null || solid.Volume <= 0)
                    return null;

                // 在指定高度切割并提取轮廓
                var loops = CutAtHeight(solid, cutHeight);
                if (loops == null || loops.Count == 0)
                    return null;

                // 取第一个轮廓（大多数情况单构件只有一个外环）
                var firstLoop = loops[0];
                var polygon = firstLoop.ToPolygon();
                if (polygon == null)
                    return null;

                return new ElementOutline
                {
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

        #region Solid 操作

        /// <summary>
        /// 获取元素的第一个有效 Solid
        /// </summary>
        private Solid GetElementSolid(Element element)
        {
            var options = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine
            };

            var geometry = element.get_Geometry(options);
            if (geometry == null) return null;

            return GetSolidFromGeometry(geometry);
        }

        /// <summary>
        /// 递归从 GeometryElement 中提取 Solid
        /// </summary>
        private Solid GetSolidFromGeometry(GeometryElement geometry)
        {
            foreach (GeometryObject geoObj in geometry)
            {
                if (geoObj is Solid solid && solid.Volume > 0)
                {
                    return solid;
                }

                if (geoObj is GeometryInstance instance)
                {
                    var instSolid = GetSolidFromGeometry(instance.GetInstanceGeometry());
                    if (instSolid != null)
                    {
                        return instSolid;
                    }
                }
            }
            return null;
        }

        #endregion

        #region 切割与轮廓提取

        /// <summary>
        /// 在指定高度切割 Solid 并提取切割面轮廓（支持外环 + 内环）
        /// </summary>
        private List<(CurveLoop Shell, List<CurveLoop> Holes)> CutAtHeight(Solid solid, double height)
        {
            var result = new List<(CurveLoop Shell, List<CurveLoop> Holes)>();

            try
            {
                // 创建切割平面：法向量朝下，保留下方部分
                Plane cutPlane = Plane.CreateByNormalAndOrigin(
                    new XYZ(0, 0, -1),
                    new XYZ(0, 0, height)
                );

                // 执行切割
                Solid cutSolid = BooleanOperationsUtils.CutWithHalfSpace(solid, cutPlane);

                if (cutSolid == null || cutSolid.Volume <= 0)
                {
                    return result;
                }

                // 提取顶面轮廓
                result = ExtractTopFaceLoops(cutSolid, height);
            }
            catch
            {
                // 切割失败，返回空列表
            }

            return result;
        }

        /// <summary>
        /// 从 Solid 提取指定高度的顶面轮廓（支持外环 + 内环）
        /// </summary>
        private List<(CurveLoop Shell, List<CurveLoop> Holes)> ExtractTopFaceLoops(Solid solid, double targetHeight)
        {
            var result = new List<(CurveLoop Shell, List<CurveLoop> Holes)>();
            double tolerance = 0.01; // 高度容差（英尺）

            foreach (Face face in solid.Faces)
            {
                if (!(face is PlanarFace planarFace)) continue;

                XYZ normal = planarFace.FaceNormal;

                // 检查是否是朝上的水平面（法向量 Z > 0.9）
                if (normal.Z > 0.9)
                {
                    XYZ origin = planarFace.Origin;
                    if (Math.Abs(origin.Z - targetHeight) < tolerance)
                    {
                        // 收集所有 CurveLoop 并计算有符号面积
                        var loopsWithArea = new List<(CurveLoop Loop, double SignedArea)>();

                        foreach (EdgeArray edgeArray in face.EdgeLoops)
                        {
                            var curves = new List<Curve>();
                            foreach (Edge edge in edgeArray)
                            {
                                curves.Add(edge.AsCurve());
                            }

                            try
                            {
                                var sortedCurves = SortCurvesContiguous(curves);
                                if (sortedCurves != null && sortedCurves.Count > 0)
                                {
                                    var curveLoop = CurveLoop.Create(sortedCurves);
                                    double signedArea = CalculateSignedArea(curveLoop);
                                    loopsWithArea.Add((curveLoop, signedArea));
                                }
                            }
                            catch
                            {
                                // CurveLoop 创建失败，跳过
                            }
                        }

                        // 分离外环（正面积/CCW）和内环（负面积/CW）
                        var shells = loopsWithArea.Where(x => x.SignedArea > 0).Select(x => x.Loop).ToList();
                        var holes = loopsWithArea.Where(x => x.SignedArea < 0).Select(x => x.Loop).ToList();

                        // 每个外环配对内环
                        if (shells.Count == 1)
                        {
                            result.Add((shells[0], holes));
                        }
                        else if (shells.Count > 1)
                        {
                            foreach (var shell in shells)
                            {
                                var containedHoles = new List<CurveLoop>();
                                foreach (var hole in holes)
                                {
                                    if (IsLoopInsideLoop(hole, shell))
                                    {
                                        containedHoles.Add(hole);
                                    }
                                }
                                result.Add((shell, containedHoles));
                            }
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 计算 CurveLoop 的有符号面积（Shoelace 公式）
        /// </summary>
        private double CalculateSignedArea(CurveLoop curveLoop)
        {
            double area = 0;
            foreach (Curve curve in curveLoop)
            {
                XYZ p0 = curve.GetEndPoint(0);
                XYZ p1 = curve.GetEndPoint(1);
                area += (p0.X * p1.Y - p1.X * p0.Y);
            }
            return area / 2.0;
        }

        /// <summary>
        /// 判断内环是否在外环内部
        /// </summary>
        private bool IsLoopInsideLoop(CurveLoop inner, CurveLoop outer)
        {
            double cx = 0, cy = 0;
            int count = 0;
            foreach (Curve curve in inner)
            {
                var p = curve.GetEndPoint(0);
                cx += p.X;
                cy += p.Y;
                count++;
            }
            cx /= count;
            cy /= count;

            return IsPointInsideLoop(cx, cy, outer);
        }

        /// <summary>
        /// 射线法判断点是否在多边形内
        /// </summary>
        private bool IsPointInsideLoop(double px, double py, CurveLoop loop)
        {
            int crossings = 0;
            foreach (Curve curve in loop)
            {
                var p0 = curve.GetEndPoint(0);
                var p1 = curve.GetEndPoint(1);

                double y0 = p0.Y, y1 = p1.Y;
                double x0 = p0.X, x1 = p1.X;

                if ((y0 <= py && y1 > py) || (y1 <= py && y0 > py))
                {
                    double t = (py - y0) / (y1 - y0);
                    double xIntersect = x0 + t * (x1 - x0);
                    if (px < xIntersect)
                    {
                        crossings++;
                    }
                }
            }
            return (crossings % 2) == 1;
        }

        /// <summary>
        /// 将曲线列表排序为连续顺序
        /// </summary>
        private List<Curve> SortCurvesContiguous(List<Curve> curves)
        {
            if (curves == null || curves.Count == 0) return null;
            if (curves.Count == 1) return curves;

            var sorted = new List<Curve>();
            var remaining = new List<Curve>(curves);

            sorted.Add(remaining[0]);
            remaining.RemoveAt(0);

            double tolerance = 0.001;

            while (remaining.Count > 0)
            {
                var lastCurve = sorted[sorted.Count - 1];
                var endPoint = lastCurve.GetEndPoint(1);

                bool found = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    var curve = remaining[i];
                    var start = curve.GetEndPoint(0);
                    var end = curve.GetEndPoint(1);

                    if (start.DistanceTo(endPoint) < tolerance)
                    {
                        sorted.Add(curve);
                        remaining.RemoveAt(i);
                        found = true;
                        break;
                    }
                    else if (end.DistanceTo(endPoint) < tolerance)
                    {
                        sorted.Add(curve.CreateReversed());
                        remaining.RemoveAt(i);
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    break;
                }
            }

            return sorted;
        }

        #endregion
    }
}
