using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIMCanvas.Revit.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace BIMCanvas.Revit.Test
{
    /// <summary>
    /// 测试 OutlineExtractor 工具类的功能
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class WallSolidUnionTest : IExternalCommand
    {
        private UIApplication uiApp;
        private UIDocument uiDoc => uiApp.ActiveUIDocument;
        private Document doc => uiDoc.Document;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            uiApp = commandData.Application;

            try
            {
                // Step 1: 获取用户指定的切割高度
                double? cutHeight = 1000 / 304.8;

                // Step 2: 使用 OutlineExtractor 获取轮廓
                var categories = new[]
                {
                    BuiltInCategory.OST_Walls,
                    BuiltInCategory.OST_Columns,
                    BuiltInCategory.OST_StructuralColumns
                };

                var outlines = OutlineExtractor.GetOutlines(
                    doc,
                    categories,
                    height: cutHeight.Value,
                    view: uiDoc.ActiveView
                );

                // Step 3: 可视化轮廓
                if (outlines.Count > 0)
                {
                    VisualizeLoops(outlines);
                }

                // Step 4: 显示结果
                TaskDialog.Show("轮廓提取结果",
                    $"切割高度: {cutHeight.Value * 304.8:F0} mm\n" +
                    $"找到轮廓: {outlines.Count} 个\n");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("错误", $"{ex.Message}\n\n{ex.StackTrace}");
                return Result.Failed;
            }
        }

        #region 可视化
        /// <summary>
        /// 在视图中绘制轮廓
        /// </summary>
        private void VisualizeLoops(List<CurveLoop> loops)
        {
            using (Transaction trans = new Transaction(doc, "显示合并轮廓"))
            {
                trans.Start();

                var categoryId = new ElementId(BuiltInCategory.OST_GenericModel);

                foreach (var loop in loops)
                {
                    try
                    {
                        var ds = DirectShape.CreateElement(doc, categoryId);
                        var curves = new List<GeometryObject>();
                        foreach (Curve curve in loop)
                        {
                            curves.Add(curve);
                        }
                        ds.SetShape(curves);
                        ds.SetName("合并轮廓");
                    }
                    catch
                    {
                        // 绘制失败，跳过
                    }
                }

                trans.Commit();
            }
        }

        #endregion
    }
}
