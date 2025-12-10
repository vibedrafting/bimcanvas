using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BIMCanvas.Revit.Utilities
{
    /// <summary>
    /// 构件轮廓提取工具类
    /// 提供获取指定类型构件在指定高度平面上轮廓的功能
    /// </summary>
    public static class OutlineExtractor
    {
        #region 公开 API

        /// <summary>
        /// 获取指定类型构件在指定高度的平面轮廓（支持外环 + 内环）
        /// </summary>
        /// <param name="doc">Revit 文档</param>
        /// <param name="categories">构件类别列表</param>
        /// <param name="height">绝对高度（英尺），与 level 二选一</param>
        /// <param name="level">标高，与 height 二选一</param>
        /// <param name="heightOffset">相对于标高或 Solid 底部的偏移（英尺），默认 1 英尺</param>
        /// <param name="view">可选的视图过滤（仅收集视图中可见的构件）</param>
        /// <returns>轮廓列表，每个元组包含外环 Shell 和内环列表 Holes</returns>
        /// <exception cref="ArgumentNullException">doc 或 categories 为空</exception>
        /// <exception cref="ArgumentException">height 和 level 都未指定</exception>
        public static List<(CurveLoop Shell, List<CurveLoop> Holes)> GetOutlines(
            Document doc,
            IEnumerable<BuiltInCategory> categories,
            double? height = null,
            Level level = null,
            double heightOffset = 1.0,
            View view = null)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));
            if (categories == null || !categories.Any())
                throw new ArgumentNullException(nameof(categories));

            // Step 1: 收集构件
            var elements = CollectElements(doc, categories, view);
            if (elements.Count == 0)
                return new List<(CurveLoop Shell, List<CurveLoop> Holes)>();

            // Step 2: 获取所有 Solid
            var solids = new List<Solid>();
            foreach (var element in elements)
            {
                var solid = GetElementSolid(element);
                if (solid != null && solid.Volume > 0)
                {
                    solids.Add(solid);
                }
            }

            if (solids.Count == 0)
                return new List<(CurveLoop Shell, List<CurveLoop> Holes)>();

            // Step 3: 合并所有 Solid
            Solid mergedSolid = UnionSolids(solids);
            if (mergedSolid == null)
                return new List<(CurveLoop Shell, List<CurveLoop> Holes)>();

            // Step 4: 确定切割高度
            double cutHeight;
            if (height.HasValue)
            {
                cutHeight = height.Value;
            }
            else if (level != null)
            {
                cutHeight = level.Elevation + heightOffset;
            }
            else
            {
                // 默认使用 Solid 底部 + 偏移
                cutHeight = GetSolidBottomZ(mergedSolid) + heightOffset;
            }

            // Step 5: 在指定高度切割并提取轮廓
            return CutAtHeight(mergedSolid, cutHeight);
        }

        #endregion

        #region 构件收集

        /// <summary>
        /// 按类别收集构件
        /// </summary>
        private static List<Element> CollectElements(
            Document doc,
            IEnumerable<BuiltInCategory> categories,
            View view)
        {
            var elements = new List<Element>();

            foreach (var category in categories)
            {
                FilteredElementCollector collector;

                if (view != null)
                {
                    collector = new FilteredElementCollector(doc, view.Id);
                }
                else
                {
                    collector = new FilteredElementCollector(doc);
                }

                var categoryElements = collector
                    .OfCategory(category)
                    .WhereElementIsNotElementType()
                    .ToElements();

                elements.AddRange(categoryElements);
            }

            return elements;
        }

        #endregion

        #region Solid 操作

        /// <summary>
        /// 获取元素的第一个有效 Solid
        /// </summary>
        private static Solid GetElementSolid(Element element)
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
        private static Solid GetSolidFromGeometry(GeometryElement geometry)
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
        /// 合并所有 Solid
        /// </summary>
        private static Solid UnionSolids(List<Solid> solids)
        {
            if (solids == null || solids.Count == 0) return null;
            if (solids.Count == 1) return solids[0];

            Solid mergedSolid = solids[0];

            for (int i = 1; i < solids.Count; i++)
            {
                try
                {
                    var newMerged = BooleanOperationsUtils.ExecuteBooleanOperation(
                        mergedSolid,
                        solids[i],
                        BooleanOperationsType.Union
                    );

                    if (newMerged != null && newMerged.Volume > 0)
                    {
                        mergedSolid = newMerged;
                    }
                    // 合并失败时保持原有 mergedSolid，继续处理下一个
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                {
                    // Union 失败，跳过这个 Solid
                }
            }

            return mergedSolid;
        }

        /// <summary>
        /// 获取 Solid 的最低 Z 坐标
        /// </summary>
        private static double GetSolidBottomZ(Solid solid)
        {
            double minZ = double.MaxValue;
            foreach (Edge edge in solid.Edges)
            {
                var curve = edge.AsCurve();
                var p0 = curve.GetEndPoint(0);
                var p1 = curve.GetEndPoint(1);
                minZ = Math.Min(minZ, Math.Min(p0.Z, p1.Z));
            }
            return minZ;
        }

        #endregion

        #region 切割与轮廓提取

        /// <summary>
        /// 在指定高度切割 Solid 并提取切割面轮廓（支持外环 + 内环）
        /// </summary>
        private static List<(CurveLoop Shell, List<CurveLoop> Holes)> CutAtHeight(Solid solid, double height)
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
        /// 使用绕向（有符号面积）判断：CCW = 外环，CW = 内环
        /// </summary>
        /// <param name="solid">切割后的 Solid</param>
        /// <param name="targetHeight">目标高度（英尺）</param>
        /// <returns>轮廓列表，每个元组包含外环 Shell 和内环列表 Holes</returns>
        private static List<(CurveLoop Shell, List<CurveLoop> Holes)> ExtractTopFaceLoops(Solid solid, double targetHeight)
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
                        // 1. 收集所有 CurveLoop 并计算有符号面积
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

                        // 2. 分离外环（正面积/CCW）和内环（负面积/CW）
                        var shells = loopsWithArea.Where(x => x.SignedArea > 0).Select(x => x.Loop).ToList();
                        var holes = loopsWithArea.Where(x => x.SignedArea < 0).Select(x => x.Loop).ToList();

                        // 3. 每个外环配对内环
                        if (shells.Count == 1)
                        {
                            // 单外环：所有内环归属该外环
                            result.Add((shells[0], holes));
                        }
                        else if (shells.Count > 1)
                        {
                            // 多外环：根据包含关系分配内环
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
                        // shells.Count == 0 时不添加结果
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 计算 CurveLoop 的有符号面积（Shoelace 公式）
        /// 正值 = CCW（逆时针）= 外环
        /// 负值 = CW（顺时针）= 内环
        /// </summary>
        private static double CalculateSignedArea(CurveLoop curveLoop)
        {
            double area = 0;
            foreach (Curve curve in curveLoop)
            {
                XYZ p0 = curve.GetEndPoint(0);
                XYZ p1 = curve.GetEndPoint(1);
                // Shoelace formula: 0.5 * Σ(x_i * y_{i+1} - x_{i+1} * y_i)
                area += (p0.X * p1.Y - p1.X * p0.Y);
            }
            return area / 2.0;
        }

        /// <summary>
        /// 判断内环是否在外环内部（使用质心点测试）
        /// </summary>
        private static bool IsLoopInsideLoop(CurveLoop inner, CurveLoop outer)
        {
            // 计算内环的质心
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

            // 使用射线法判断点是否在外环内
            return IsPointInsideLoop(cx, cy, outer);
        }

        /// <summary>
        /// 射线法判断点是否在多边形内
        /// </summary>
        private static bool IsPointInsideLoop(double px, double py, CurveLoop loop)
        {
            int crossings = 0;
            foreach (Curve curve in loop)
            {
                var p0 = curve.GetEndPoint(0);
                var p1 = curve.GetEndPoint(1);

                double y0 = p0.Y, y1 = p1.Y;
                double x0 = p0.X, x1 = p1.X;

                // 检查射线（向右）与边的交点
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
        private static List<Curve> SortCurvesContiguous(List<Curve> curves)
        {
            if (curves == null || curves.Count == 0) return null;
            if (curves.Count == 1) return curves;

            var sorted = new List<Curve>();
            var remaining = new List<Curve>(curves);

            sorted.Add(remaining[0]);
            remaining.RemoveAt(0);

            double tolerance = 0.001; // 容差（英尺）

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
