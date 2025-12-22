using Autodesk.Revit.DB;
using BIMCanvas.Core.Converters;
using BIMCanvas.Revit.Converters;
using BIMCanvas.Revit.Models;
using BIMCanvas.Revit.Services;
using BIMCanvas.Revit.Utilities;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BIMCanvas.Revit.Adapters
{
    /// <summary>
    /// 建筑边界构件轮廓提取适配器
    /// - 墙体：Solid 切割，支持门洞分割成多段
    /// - 柱子：BoundingBox 生成矩形
    /// </summary>
    public class BoundaryAdapter
    {
        /// <summary>
        /// 切割高度（毫米）
        /// </summary>
        private readonly double _cutHeightMm;

        /// <summary>
        /// 创建边界适配器
        /// </summary>
        /// <param name="options">画布导出配置选项</param>
        public BoundaryAdapter(ExportOptions options)
        {
            _cutHeightMm = options.BoundaryCutHeightMm;
        }

        /// <summary>
        /// 提取建筑边界构件轮廓（墙体和柱子）
        /// </summary>
        /// <param name="view">Revit 平面视图</param>
        /// <returns>元组：(墙体列表, 柱子列表)</returns>
        public (List<RevitWall> Walls, List<RevitColumn> Columns) ExtractBoundaries(View view)
        {
            if (view == null)
                throw new ArgumentNullException(nameof(view));

            var doc = view.Document;
            var walls = new List<RevitWall>();
            var columns = new List<RevitColumn>();

            // 切割高度
            double cutHeightFeet = UnitConverter.ToFeet(_cutHeightMm);

            // 收集并处理墙体（使用 Solid 切割，支持门洞分割）
            PrefixId.Reset("w_");
            var wallElements = CollectElements(doc, view, BuiltInCategory.OST_Walls);
            foreach (var wall in wallElements)
            {
                var wallOutlines = ExtractWallOutlines(wall, cutHeightFeet, "w_");
                walls.AddRange(wallOutlines);
            }

            // 收集并处理建筑柱和结构柱（统一使用 c_ 前缀）
            PrefixId.Reset("c_");
            var columnElements = CollectElements(doc, view, BuiltInCategory.OST_Columns);
            foreach (var column in columnElements)
            {
                var col = ExtractColumnOutline(column, "c_", isStructural: false);
                if (col != null)
                    columns.Add(col);
            }

            // 收集并处理结构柱（使用 BoundingBox，与建筑柱共享 c_ 前缀）
            var structuralColumns = CollectElements(doc, view, BuiltInCategory.OST_StructuralColumns);
            foreach (var column in structuralColumns)
            {
                var col = ExtractColumnOutline(column, "c_", isStructural: true);
                if (col != null)
                    columns.Add(col);
            }

            return (walls, columns);
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

        #region 墙体提取（Solid 切割）

        /// <summary>
        /// 提取墙体轮廓（可能产生多段，如被门洞分割）
        /// </summary>
        private List<RevitWall> ExtractWallOutlines(Element wall, double cutHeight, string idPrefix)
        {
            var result = new List<RevitWall>();

            try
            {
                var solid = GetElementSolid(wall);
                if (solid == null || solid.Volume <= 0)
                    return result;

                var loops = CutAtHeight(solid, cutHeight);
                if (loops == null || loops.Count == 0)
                    return result;

                // 每个外环都是独立的墙段（门洞分割后的结果）
                foreach (var loop in loops)
                {
                    var polygon = loop.ToPolygon();
                    if (polygon == null)
                        continue;

                    result.Add(new RevitWall
                    {
                        Id = PrefixId.NewId(idPrefix),
                        ElementId = wall.Id.IntegerValue,  // 同一面墙的多段共享 ElementId
                        Boundary = polygon
                    });
                }
            }
            catch
            {
                // 提取失败，返回空列表
            }

            return result;
        }

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

        /// <summary>
        /// 在指定高度切割 Solid 并提取切割面轮廓
        /// </summary>
        private List<(CurveLoop Shell, List<CurveLoop> Holes)> CutAtHeight(Solid solid, double height)
        {
            var result = new List<(CurveLoop Shell, List<CurveLoop> Holes)>();

            try
            {
                Plane cutPlane = Plane.CreateByNormalAndOrigin(
                    new XYZ(0, 0, -1),
                    new XYZ(0, 0, height)
                );

                Solid cutSolid = BooleanOperationsUtils.CutWithHalfSpace(solid, cutPlane);

                if (cutSolid == null || cutSolid.Volume <= 0)
                    return result;

                result = ExtractTopFaceLoops(cutSolid, height);
            }
            catch
            {
                // 切割失败
            }

            return result;
        }

        /// <summary>
        /// 从 Solid 提取指定高度的顶面轮廓
        /// </summary>
        private List<(CurveLoop Shell, List<CurveLoop> Holes)> ExtractTopFaceLoops(Solid solid, double targetHeight)
        {
            var result = new List<(CurveLoop Shell, List<CurveLoop> Holes)>();
            double tolerance = 0.01;

            foreach (Face face in solid.Faces)
            {
                if (!(face is PlanarFace planarFace)) continue;

                XYZ normal = planarFace.FaceNormal;

                if (normal.Z > 0.9)
                {
                    XYZ origin = planarFace.Origin;
                    if (Math.Abs(origin.Z - targetHeight) < tolerance)
                    {
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
                                // CurveLoop 创建失败
                            }
                        }

                        var shells = loopsWithArea.Where(x => x.SignedArea > 0).Select(x => x.Loop).ToList();
                        var holes = loopsWithArea.Where(x => x.SignedArea < 0).Select(x => x.Loop).ToList();

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
                    break;
            }

            return sorted;
        }

        #endregion

        #region 柱子提取（BoundingBox）

        /// <summary>
        /// 提取柱子轮廓（使用 BoundingBox）
        /// </summary>
        private RevitColumn ExtractColumnOutline(Element column, string idPrefix, bool isStructural)
        {
            try
            {
                var polygon = ExtractBoundingBoxOutline(column);
                if (polygon == null)
                    return null;

                return new RevitColumn
                {
                    Id = PrefixId.NewId(idPrefix),
                    ElementId = column.Id.IntegerValue,
                    IsStructural = isStructural,
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
        private Polygon ExtractBoundingBoxOutline(Element element)
        {
            var bbox = element.get_BoundingBox(null);
            if (bbox == null)
                return null;

            double minX = bbox.Min.X;
            double minY = bbox.Min.Y;
            double maxX = bbox.Max.X;
            double maxY = bbox.Max.Y;

            var coordinates = new[]
            {
                new Coordinate(minX, minY),
                new Coordinate(maxX, minY),
                new Coordinate(maxX, maxY),
                new Coordinate(minX, maxY),
                new Coordinate(minX, minY)
            };

            return new Polygon(new LinearRing(coordinates));
        }

        #endregion
    }
}
