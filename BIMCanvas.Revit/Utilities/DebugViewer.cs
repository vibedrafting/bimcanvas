using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BIMCanvas.Revit.Utilities;
using BIMCanvas.Core.Algorithms.Geometries;
using LineSegment = NetTopologySuite.Geometries.LineSegment;

namespace BIMCanvas.Revit.Utilities
{
    /// <summary>
    /// 线条颜色枚举
    /// </summary>
    public enum ColorType
    {
        // 基础颜色
        /// <summary>
        /// 黑色
        /// </summary>
        Black,
        /// <summary>
        /// 白色
        /// </summary>
        White,
        /// <summary>
        /// 红色
        /// </summary>
        Red,
        /// <summary>
        /// 绿色
        /// </summary>
        Green,
        /// <summary>
        /// 蓝色
        /// </summary>
        Blue,

        // 原有扩展颜色
        /// <summary>
        /// 橙色
        /// </summary>
        Orange,
        /// <summary>
        /// 粉色
        /// </summary>
        Pink,
        /// <summary>
        /// 紫色
        /// </summary>
        Purple,
        /// <summary>
        /// 棕色
        /// </summary>
        Brown,
        /// <summary>
        /// 黄色
        /// </summary>
        Yellow,

        // 新增基础颜色
        /// <summary>
        /// 灰色
        /// </summary>
        Gray,
        /// <summary>
        /// 青色
        /// </summary>
        Cyan,
        /// <summary>
        /// 品红
        /// </summary>
        Magenta,
    }
    public static class DebugViewer
    {
        /// <summary>
        ///Revit常规主色的颜色字典映射
        /// </summary>
        public static readonly Dictionary<ColorType, Color> ColorMap = new Dictionary<ColorType, Color>
        {
            // 黑色
            { ColorType.Black,    new Color(0, 0, 0) },
            // 白色
            { ColorType.White,    new Color(255, 255, 255) },
            // 红色
            { ColorType.Red,      new Color(255, 0, 0) },
            // 绿色
            { ColorType.Green,    new Color(0, 255, 0) },
            // 蓝色
            { ColorType.Blue,     new Color(0, 0, 255) },
            // 黄色
            { ColorType.Yellow,   new Color(255, 255, 0) },
            // 灰色
            { ColorType.Gray,     new Color(128, 128, 128) },
            // 青色（蓝绿色）
            { ColorType.Cyan,     new Color(0, 255, 255) },
            // 洋红色（品红色）
            { ColorType.Magenta,  new Color(255, 0, 255) },
        };

        #region ## 显示轮廓线方法
        #region 公共方法
        /// <summary>
        /// 创建带颜色的模型线（不处理草图平面检查和创建逻辑）
        /// </summary>
        /// <param name="doc">Revit 文档对象</param>
        /// <param name="curve">要创建的模型线曲线</param>
        /// <param name="origin">曲线的起始点</param>
        /// <param name="colorType">颜色类型</param>
        /// <returns>创建的模型线的 ElementId</returns>
        public static ElementId CreateColoredModelCurve(Document doc, Curve curve, ColorType? colorType = null)
        {
            if (curve.Length < 0.01 / 304.8)
            {
                return null;
            }
            else
            {
                try
                {
                    ModelCurve modelCurve = doc.Create.NewModelCurve(curve, doc.ActiveView.SketchPlane);
                    // 如果指定了颜色，则设置模型曲线颜色
                    if (colorType.HasValue)
                    {
                        OverrideGraphicSettings overrideSettings = new OverrideGraphicSettings();
                        overrideSettings.SetProjectionLineColor(colorType.Value.ToColor());
                        doc.ActiveView.SetElementOverrides(modelCurve.Id, overrideSettings);
                    }
                    return modelCurve.Id;
                }
                catch (Exception ex)
                {
                    return null;
                }
            }

        }

        /// <summary>
        /// 将 ColorType 转换为 Revit API 中的 Color
        /// </summary>
        /// <param name="colorType">ColorType 枚举</param>
        /// <returns>Revit 的 Color 对象</returns>
        public static Color ToColor(this ColorType colorType)
        {
            switch (colorType)
            {
                case ColorType.Red:
                    return new Color(255, 0, 0);  // 红色
                case ColorType.Green:
                    return new Color(0, 255, 0);  // 绿色
                case ColorType.Blue:
                    return new Color(0, 0, 255);  // 蓝色
                case ColorType.Yellow:
                    return new Color(255, 255, 0);  // 黄色
                default:
                    return new Color(0, 0, 0);  // 默认黑色
            }
        }

        /// <summary>
        /// 获取适合绘制详图线的目标视图
        /// </summary>
        public static View GetTargetViewForDetailCurve(Document doc, double elevation)
        {
            View activeView = doc.ActiveView;

            // 判断当前视图是否为平面视图
            if (activeView.ViewType == ViewType.FloorPlan || activeView.ViewType == ViewType.CeilingPlan || activeView.ViewType == ViewType.EngineeringPlan)
                return activeView;

            // 查找所有平面视图
            List<View> planViews = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan)
                .ToList();

            if (!planViews.Any()) return null;

            // 找到距离指定高度最近的平面视图
            View nearestView = planViews.First();
            double minDiff = Math.Abs(nearestView.Origin.Z - elevation);

            foreach (View view in planViews)
            {
                double diff = Math.Abs(view.Origin.Z - elevation);
                if (diff < minDiff)
                {
                    minDiff = diff;
                    nearestView = view;
                }
            }

            return nearestView;
        }

        /// <summary>
        /// 获取指定颜色类型的Color对象
        /// </summary>
        public static Color GetColorByType(ColorType colorType)
        {
            return ColorMap.ContainsKey(colorType) ? ColorMap[colorType] : new Color(0, 0, 0); // 默认返回黑色
        }

        /// <summary>
        /// 设置元素投影线的颜色覆盖
        /// </summary>
        public static void SetElementLineColor(this Document doc, Element element, View view, ColorType colorType)
        {
            OverrideGraphicSettings overrideSettings = new OverrideGraphicSettings();
            Color color = GetColorByType(colorType);
            overrideSettings.SetProjectionLineColor(color);
            view.SetElementOverrides(element.Id, overrideSettings);
        }

        /// <summary>
        /// 设置元素曲面前景图、截面的颜色和填充模式覆盖（带透明度）
        /// </summary>
        public static void SetElementSurfaceColor(this Document doc, Element element, View view, ColorType colorType, int transparency = 0)
        {
            OverrideGraphicSettings overrideSettings = new OverrideGraphicSettings();
            Color color = GetColorByType(colorType);

            // 获取默认的实心填充模式
            FillPatternElement solidFill = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(x => x.GetFillPattern().IsSolidFill);

            if (solidFill != null)
            {
                overrideSettings.SetSurfaceForegroundPatternId(solidFill.Id);
                overrideSettings.SetSurfaceForegroundPatternColor(color);
                overrideSettings.SetCutForegroundPatternId(solidFill.Id);
                overrideSettings.SetCutForegroundPatternColor(color);
                overrideSettings.SetSurfaceTransparency(transparency);
            }

            view.SetElementOverrides(element.Id, overrideSettings);
        }

        /// <summary>
        /// 执行带事务的方法封装，确保操作在事务内执行
        /// </summary>
        /// <param name="doc">Revit文档</param>
        /// <param name="transactionName">事务名称</param>
        /// <param name="action">要执行的操作</param>
        /// <returns>事务操作结果</returns>
        public static T ExecuteInTransaction<T>(Document doc, string transactionName, Func<T> action, FailureLevel failureLevel = FailureLevel.IgnoreWarningsAndErrors)
        {
            // 开始事务
            using (Transaction trans = new Transaction(doc, transactionName))
            {
                trans.Start();
                trans.IgnoreFailure(failureLevel);

                try
                {
                    // 执行操作
                    T result = action();
                    // 提交事务
                    trans.Commit();
                    return result;
                }
                catch (Exception ex)
                {
                    // 回滚事务
                    trans.RollBack();
                    throw new InvalidOperationException("事务操作失败", ex);
                }
            }
        }

        /// <summary>
        /// 获取 BoundingBoxXYZ 的底部边界的所有曲线
        /// </summary>
        /// <param name="boundingBox">BoundingBoxXYZ对象</param>
        /// <returns>底部边界曲线集合</returns>
        public static IEnumerable<Curve> GetBottomBoundaryCurves(BoundingBoxXYZ boundingBox)
        {
            if (boundingBox == null)
                yield break;

            XYZ min = boundingBox.Min;
            XYZ max = boundingBox.Max;

            // 只获取底部的四条边界曲线，底部的边界是z轴的最小值（min.Z）
            yield return Line.CreateBound(new XYZ(min.X, min.Y, min.Z), new XYZ(max.X, min.Y, min.Z));  // Bottom-Left to Bottom-Right
            yield return Line.CreateBound(new XYZ(max.X, min.Y, min.Z), new XYZ(max.X, max.Y, min.Z));  // Bottom-Right to Top-Right
            yield return Line.CreateBound(new XYZ(max.X, max.Y, min.Z), new XYZ(min.X, max.Y, min.Z));  // Top-Right to Top-Left
            yield return Line.CreateBound(new XYZ(min.X, max.Y, min.Z), new XYZ(min.X, min.Y, min.Z));  // Top-Left to Bottom-Left
        }

        /// <summary>
        /// 获取草图平面相对于XY平面的高度
        /// </summary>
        /// <param name="sketchPlane">草图平面</param>
        /// <returns>相对于XY平面的Z高度值，以Revit内部单位表示</returns>
        public static double GetSketchPlaneHeight(this SketchPlane sketchPlane)
        {
            if (sketchPlane == null)
                return 0;

            // 获取平面
            Plane plane = sketchPlane.GetPlane();
            XYZ normal = plane.Normal;
            XYZ origin = plane.Origin;

            // 检查平面是否平行于XY平面
            if (Math.Abs(normal.Z) > 0.999) // 近似为1，考虑浮点误差
            {
                // 如果平面平行于XY平面，直接返回原点的Z值
                return origin.Z;
            }
            else
            {
                // 如果平面不平行于XY平面，计算从原点到XY平面的垂直距离
                // 使用点到平面距离公式: d = |ax₀ + by₀ + cz₀ + d| / √(a² + b² + c²)
                // 其中(a,b,c)是XY平面的法向量(0,0,1)，d是XY平面方程的常数项(0)
                // 简化后，点到XY平面的距离就是点的Z坐标
                return origin.Z;
            }
        }
        #endregion

        #region 创建模型线方法

        /// <summary>
        /// 显示线段（支持是否开启事务，UI延迟支持）
        /// </summary>
        /// <returns>创建的模型线的ElementId</returns>
        public static ElementId DisplayLine(this Document doc, LineSegment line, int delayMilliseconds, ColorType? colorType = null, bool enableTransaction = true, FailureLevel failureLevel = FailureLevel.IgnoreWarningsAndErrors)
        {
            if (line == null) return null;

            UIApplication uiApp = new UIApplication(doc.Application);
            UIDocument uiDoc = uiApp.ActiveUIDocument;

            if (enableTransaction)
            {
                return ExecuteInTransaction(uiDoc.Document, "显示模型线", () =>
                {
                    var elementId = CreateColoredModelCurve(uiDoc.Document, line.ToLine(doc.ActiveView.SketchPlane.GetSketchPlaneHeight()), colorType);

                    // 刷新界面
                    uiDoc.Document.Regenerate();
                    uiDoc.RefreshActiveView();

                    // 添加延时
                    Thread.Sleep(delayMilliseconds); // 暂停0.15秒

                    return elementId;
                }, failureLevel);
            }
            else
            {
                var elementId = CreateColoredModelCurve(uiDoc.Document, line.ToLine( doc.ActiveView.SketchPlane.GetSketchPlaneHeight()), colorType);

                // 刷新界面
                uiDoc.Document.Regenerate();
                uiDoc.RefreshActiveView();

                return elementId;
            }
        }


        /// <summary>
        /// 显示坐标点的模型线（支持是否开启事务）
        /// </summary>
        /// <returns>创建的模型线的ElementId</returns>
        public static ElementId DisplayLine(this Document doc, XYZ xyz, ColorType? colorType = null, bool enableTransaction = true, FailureLevel failureLevel = FailureLevel.IgnoreWarningsAndErrors)
        {
            if (enableTransaction)
            {
                return ExecuteInTransaction(doc, "显示模型线", () =>
                {
                    Line line = Line.CreateBound(new XYZ(0, 0, doc.ActiveView.SketchPlane.GetSketchPlaneHeight()), new XYZ(xyz.X, xyz.Y, doc.ActiveView.SketchPlane.GetSketchPlaneHeight()));
                    return CreateColoredModelCurve(doc, line, colorType);
                }, failureLevel);
            }
            else
            {
                Line line = Line.CreateBound(new XYZ(0, 0, doc.ActiveView.SketchPlane.GetSketchPlaneHeight()), new XYZ(xyz.X, xyz.Y, doc.ActiveView.SketchPlane.GetSketchPlaneHeight()));
                return CreateColoredModelCurve(doc, line, colorType);
            }
        }

        /// <summary>
        /// 显示坐标点的模型线（支持是否开启事务）
        /// </summary>
        /// <returns>创建的模型线的ElementId</returns>
        public static ElementId DisplayLine(this Document doc, Coordinate coord, ColorType? colorType = null, bool enableTransaction = true, FailureLevel failureLevel = FailureLevel.IgnoreWarningsAndErrors)
        {
            if (enableTransaction)
            {
                return ExecuteInTransaction(doc, "显示模型线", () =>
                {
                    Line line = Line.CreateBound(new XYZ(0, 0, doc.ActiveView.SketchPlane.GetSketchPlaneHeight()), new XYZ(coord.X, coord.Y, doc.ActiveView.SketchPlane.GetSketchPlaneHeight()));
                    return CreateColoredModelCurve(doc, line, colorType);
                }, failureLevel);
            }
            else
            {
                Line line = Line.CreateBound(new XYZ(0, 0, doc.ActiveView.SketchPlane.GetSketchPlaneHeight()), new XYZ(coord.X, coord.Y, doc.ActiveView.SketchPlane.GetSketchPlaneHeight()));
                return CreateColoredModelCurve(doc, line, colorType);
            }
        }


        /// <summary>
        /// 显示曲线（支持是否开启事务）
        /// </summary>
        /// <returns>创建的模型线的ElementId</returns>
        public static ElementId DisplayLine(this Document doc, Line line, ColorType? colorType = null, bool enableTransaction = true, FailureLevel failureLevel = FailureLevel.IgnoreWarningsAndErrors)
        {
            if (line == null) return null;

            if (enableTransaction)
            {
                return ExecuteInTransaction(doc, "显示模型线", () =>
                {
                    return CreateColoredModelCurve(doc, line, colorType);
                }, failureLevel);
            }
            else
            {
                return CreateColoredModelCurve(doc, line, colorType);
            }
        }

        /// <summary>
        /// 显示线段（支持是否开启事务）
        /// </summary>
        /// <returns>创建的模型线的ElementId</returns>
        public static ElementId DisplayLine(this Document doc, LineSegment line, ColorType? colorType = null, bool enableTransaction = true, FailureLevel failureLevel = FailureLevel.IgnoreWarningsAndErrors)
        {
            if (line == null) return null;

            if (enableTransaction)
            {
                return ExecuteInTransaction(doc, "显示模型线", () =>
                {
                    return CreateColoredModelCurve(doc, line.ToLine( doc.ActiveView.SketchPlane.GetSketchPlaneHeight()), colorType);
                }, failureLevel);
            }
            else
            {
                return CreateColoredModelCurve(doc, line.ToLine( doc.ActiveView.SketchPlane.GetSketchPlaneHeight()), colorType);
            }
        }


        /// <summary>
        /// 显示线段,可选是否显示中点（支持是否开启事务）
        /// </summary>
        /// <returns>创建的模型线或中点线的ElementId</returns>
        public static ElementId DisplayLine(this Document doc, LineSegment line, bool mid, ColorType? colorType = null, bool enableTransaction = true, FailureLevel failureLevel = FailureLevel.IgnoreWarningsAndErrors)
        {
            if (line == null) return null;

            if (mid)
            {
                return doc.DisplayLine(line.MidPoint, colorType, enableTransaction, failureLevel);
            }

            if (enableTransaction)
            {
                return ExecuteInTransaction(doc, "显示模型线", () =>
                {
                    return CreateColoredModelCurve(doc, line.ToLine( doc.ActiveView.SketchPlane.GetSketchPlaneHeight()), colorType);
                }, failureLevel);
            }
            else
            {
                return CreateColoredModelCurve(doc, line.ToLine( doc.ActiveView.SketchPlane.GetSketchPlaneHeight()), colorType);
            }
        }


        /// <summary>
        /// 显示 Polygon 类型的轮廓线（支持是否开启事务）
        /// </summary>
        /// <param name="doc">Revit 文档</param>
        /// <param name="polygon">Polygon 对象</param>
        /// <param name="colorType">颜色类型</param>
        /// <param name="enableTransaction">是否启用事务</param>
        /// <returns>创建的模型线的 ElementId 集合</returns>
        public static IList<ElementId> DisplayLine(this Document doc, Polygon polygon, ColorType? colorType = null, bool enableTransaction = true, FailureLevel failureLevel = FailureLevel.IgnoreWarningsAndErrors)
        {
            if (polygon == null || polygon.Coordinates == null || polygon.Coordinates.Length < 2) return new List<ElementId>();

            if (enableTransaction)
            {
                return ExecuteInTransaction(doc, "显示 Polygon 轮廓线", () =>
                {
                    List<ElementId> modelLineIds = new List<ElementId>();

                    // 绘制外环
                    var polygonLines = polygon.GetPolygonLines();
                    foreach (var line in polygonLines)
                    {
                        modelLineIds.Add(DisplayLine(doc, line, colorType, false, failureLevel));
                    }

                    // 绘制内环（如果有）
                    if (polygon.Holes != null && polygon.Holes.Any())
                    {
                        foreach (var hole in polygon.Holes)
                        {
                            Polygon holePolygon = new Polygon(hole);  // 将内环作为 Polygon 处理
                            modelLineIds.AddRange(DisplayLine(doc, holePolygon, colorType, false, failureLevel)); // 递归调用自身处理内环
                        }
                    }

                    return modelLineIds;
                }, failureLevel);
            }
            else
            {
                List<ElementId> modelLineIds = new List<ElementId>();

                // 绘制外环
                var polygonLines = polygon.GetPolygonLines();
                foreach (var line in polygonLines)
                {
                    modelLineIds.Add(DisplayLine(doc, line, colorType, enableTransaction, failureLevel));
                }

                // 绘制内环（如果有）
                if (polygon.Holes != null && polygon.Holes.Any())
                {
                    foreach (var hole in polygon.Holes)
                    {
                        Polygon holePolygon = new Polygon(hole);  // 将内环作为 Polygon 处理
                        modelLineIds.AddRange(DisplayLine(doc, holePolygon, colorType, enableTransaction, failureLevel)); // 递归调用自身处理内环
                    }
                }

                return modelLineIds;
            }
        }


        /// <summary>
        /// 显示线段（通过BoundingBoxXYZ显示，默认在事务中执行）
        /// </summary>
        /// <param name="doc">Revit文档对象</param>
        /// <param name="boundingBox">BoundingBoxXYZ对象</param>
        /// <param name="colorType">颜色类型（可选）</param>
        /// <returns>创建的模型线的ElementId集合</returns>
        public static IList<ElementId> DisplayLine(this Document doc, BoundingBoxXYZ boundingBox, ColorType? colorType = null, FailureLevel failureLevel = FailureLevel.IgnoreWarningsAndErrors)
        {
            if (boundingBox == null) return null;

            List<ElementId> modelLineIds = new List<ElementId>();

            return ExecuteInTransaction(doc, "显示模型线", () =>
            {
                // 获取底部边界曲线
                IEnumerable<Curve> bottomCurves = GetBottomBoundaryCurves(boundingBox);

                foreach (Curve curve in bottomCurves)
                {
                    // 调用 CreateColoredModelCurveWithSketchPlane 方法创建模型线
                    modelLineIds.Add(CreateColoredModelCurveWithSketchPlane(doc, curve, boundingBox.Min, colorType));
                }

                return modelLineIds;
            }, failureLevel);
        }

        /// <summary>
        /// 创建带草图平面检查的模型线
        /// </summary>
        /// <param name="doc">Revit 文档对象</param>
        /// <param name="curve">要创建的模型线曲线</param>
        /// <param name="origin">曲线的起始点</param>
        /// <param name="colorType">颜色类型</param>
        /// <returns>创建的模型线的 ElementId</returns>
        public static ElementId CreateColoredModelCurveWithSketchPlane(Document doc, Curve curve, XYZ origin, ColorType? colorType = null, FailureLevel failureLevel = FailureLevel.IgnoreWarningsAndErrors)
        {
            // 检查当前激活视图是否已经有合适的草图平面
            SketchPlane activeSketchPlane = doc.ActiveView.SketchPlane;
            if (activeSketchPlane != null)
            {
                // 获取草图平面的法向量和原点
                Plane activePlane = activeSketchPlane.GetPlane();
                XYZ planeNormal = activePlane.Normal;
                XYZ planeOrigin = activePlane.Origin;

                // 检查草图平面是否与目标平面（Z = origin.Z）匹配
                if (planeNormal.IsAlmostEqualTo(XYZ.BasisZ) && Math.Abs(planeOrigin.Z - origin.Z) < 1e-6)
                {
                    // 如果当前草图平面合适，则直接调用 CreateColoredModelCurve
                    return CreateColoredModelCurve(doc, curve, colorType);
                }
            }

            // 如果没有合适的草图平面，则创建一个新的草图平面
            using (Transaction transaction = new Transaction(doc, "创建草图平面"))
            {
                transaction.Start();
                transaction.IgnoreFailure(failureLevel);
                Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, origin);
                SketchPlane newSketchPlane = SketchPlane.Create(doc, plane);

                // 将草图平面设置为当前视图的草图平面
                doc.ActiveView.SketchPlane = newSketchPlane;

                transaction.Commit();
            }

            // 再次调用 CreateColoredModelCurve 创建模型线
            return CreateColoredModelCurve(doc, curve, colorType);
        }

        #endregion

        #region 创建详图曲线方法

        /// <summary>
        /// 创建详图曲线（支持是否开启事务）
        /// </summary>
        public static void DisplayDetailCurve(this Document doc, XYZ start, XYZ end, ColorType colorType, bool enableTransaction = true, FailureLevel failureLevel = FailureLevel.IgnoreWarningsAndErrors)
        {
            // 获取合适的视图
            View targetView = GetTargetViewForDetailCurve(doc, start.Z);
            if (targetView == null)
            {
                TaskDialog.Show("错误", "未找到合适的平面视图");
                return;
            }

            if (enableTransaction)
            {
                using (Transaction tran = new Transaction(doc, "显示详图曲线"))
                {
                    tran.Start();
                    tran.IgnoreFailure(failureLevel);
                    // 在指定的两个点之间创建一条线段
                    Line connectionLine = Line.CreateBound(start, end);
                    // 在目标视图中绘制线段
                    DetailCurve detailCurve = doc.Create.NewDetailCurve(targetView, connectionLine);
                    // 设置颜色
                    SetElementLineColor(doc, detailCurve, targetView, colorType);
                    tran.Commit();
                }
            }
            else
            {
                // 在指定的两个点之间创建一条线段
                Line connectionLine = Line.CreateBound(start, end);
                // 在目标视图中绘制线段
                DetailCurve detailCurve = doc.Create.NewDetailCurve(targetView, connectionLine);
                // 设置颜色
                SetElementLineColor(doc, detailCurve, targetView, colorType);
            }
        }

        /// <summary>
        /// 创建详图曲线（支持是否开启事务）
        /// </summary>
        public static void DisplayDetailCurve(this Document doc, LineSegment lineSegment, ColorType colorType, bool enableTransaction = true, FailureLevel failureLevel = FailureLevel.IgnoreWarningsAndErrors)
        {
            // 获取合适的视图
            View targetView = GetTargetViewForDetailCurve(doc, lineSegment.P0.Z);
            if (targetView == null)
            {
                TaskDialog.Show("错误", "未找到合适的平面视图");
                return;
            }

            if (enableTransaction)
            {
                using (Transaction tran = new Transaction(doc, "显示详图曲线"))
                {
                    tran.Start();
                    tran.IgnoreFailure(failureLevel);
                    // 创建并绘制线段
                    Line connectionLine = lineSegment.ToLine(0);
                    DetailCurve detailCurve = doc.Create.NewDetailCurve(targetView, connectionLine);
                    // 设置颜色
                    SetElementLineColor(doc, detailCurve, targetView, colorType);
                    tran.Commit();
                }
            }
            else
            {
                // 创建并绘制线段
                Line connectionLine = lineSegment.ToLine(0);
                DetailCurve detailCurve = doc.Create.NewDetailCurve(targetView, connectionLine);
                // 设置颜色
                SetElementLineColor(doc, detailCurve, targetView, colorType);
            }
        }

        /// <summary>
        /// 创建详图曲线（支持是否开启事务）
        /// </summary>
        public static void DisplayDetailCurve(this Document doc, Coordinate coordinate, ColorType colorType, bool enableTransaction = true, FailureLevel failureLevel = FailureLevel.IgnoreWarningsAndErrors)
        {
            // 获取合适的视图
            View targetView = GetTargetViewForDetailCurve(doc, coordinate.Z);
            if (targetView == null)
            {
                TaskDialog.Show("错误", "未找到合适的平面视图");
                return;
            }

            if (enableTransaction)
            {
                using (Transaction tran = new Transaction(doc, "显示详图曲线"))
                {
                    tran.Start();
                    tran.IgnoreFailure(failureLevel);
                    // 创建并绘制线段
                    Line connectionLine = Line.CreateBound(new XYZ(), coordinate.ToXYZ());
                    DetailCurve detailCurve = doc.Create.NewDetailCurve(targetView, connectionLine);
                    // 设置颜色
                    SetElementLineColor(doc, detailCurve, targetView, colorType);
                    tran.Commit();
                }
            }
            else
            {
                // 创建并绘制线段
                Line connectionLine = Line.CreateBound(new XYZ(), coordinate.ToXYZ());
                DetailCurve detailCurve = doc.Create.NewDetailCurve(targetView, connectionLine);
                // 设置颜色
                SetElementLineColor(doc, detailCurve, targetView, colorType);
            }
        }

        #endregion

        #region 创建 DirectShape 方法

        /// <summary>
        /// 使用 DirectShape 显示 NTS Polygon 轮廓（包括外环和内环）
        /// </summary>
        /// <param name="doc">Revit 文档</param>
        /// <param name="polygon">NTS Polygon 对象</param>
        /// <param name="colorType">颜色类型（可选）</param>
        /// <param name="enableTransaction">是否启用事务</param>
        /// <param name="failureLevel">失败处理级别</param>
        /// <returns>创建的 DirectShape 的 ElementId 集合</returns>
        public static IList<ElementId> DisplayDirectShape(this Document doc, Polygon polygon, ColorType? colorType = null, bool enableTransaction = true, FailureLevel failureLevel = FailureLevel.IgnoreWarningsAndErrors)
        {
            if (polygon == null || polygon.IsEmpty) return new List<ElementId>();

            Func<IList<ElementId>> createAction = () =>
            {
                var elementIds = new List<ElementId>();
                var categoryId = new ElementId(BuiltInCategory.OST_GenericModel);
                double height = doc.ActiveView.SketchPlane?.GetSketchPlaneHeight() ?? 0;

                // 收集所有环（外环 + 内环）
                var rings = new List<NetTopologySuite.Geometries.LineString> { polygon.ExteriorRing };
                if (polygon.Holes != null)
                {
                    rings.AddRange(polygon.Holes);
                }

                // 遍历每个环创建 DirectShape
                foreach (var ring in rings)
                {
                    if (ring == null || ring.NumPoints < 2) continue;

                    try
                    {
                        var curves = new List<GeometryObject>();
                        var coords = ring.Coordinates;

                        for (int i = 0; i < coords.Length - 1; i++)
                        {
                            var start = new XYZ(coords[i].X, coords[i].Y, height);
                            var end = new XYZ(coords[i + 1].X, coords[i + 1].Y, height);

                            if (start.DistanceTo(end) > 0.01 / 304.8)
                            {
                                curves.Add(Line.CreateBound(start, end));
                            }
                        }

                        if (curves.Count > 0)
                        {
                            var ds = DirectShape.CreateElement(doc, categoryId);
                            ds.SetShape(curves);
                            ds.SetName("Polygon轮廓");

                            if (colorType.HasValue)
                            {
                                SetElementLineColor(doc, ds, doc.ActiveView, colorType.Value);
                            }
                            elementIds.Add(ds.Id);
                        }
                    }
                    catch
                    {
                        // 创建失败，跳过
                    }
                }

                return elementIds;
            };

            if (enableTransaction)
            {
                return ExecuteInTransaction(doc, "显示 Polygon 轮廓", createAction, failureLevel);
            }
            else
            {
                return createAction();
            }
        }

        #endregion

        #endregion

    }
}
