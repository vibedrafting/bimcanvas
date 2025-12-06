using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIMCanvas.Core.Models.Document;
using BIMCanvas.Revit.Services;
using Microsoft.Win32;

namespace BIMCanvas.Revit.Commands
{
    /// <summary>
    /// 导出画布命令
    /// 从当前平面视图导出 BIMCanvas JSON 文件
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class ExportCanvasCommand : IExternalCommand
    {
        /// <summary>
        /// 执行导出命令
        /// </summary>
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                var uiDoc = commandData.Application.ActiveUIDocument;
                var doc = uiDoc.Document;
                var view = uiDoc.ActiveView;

                // 检查视图类型
                if (!(view is ViewPlan))
                {
                    TaskDialog.Show("BIMCanvas",
                        "请在平面视图中执行此命令。\n\n" +
                        "当前视图类型：" + view.ViewType.ToString());
                    return Result.Cancelled;
                }

                // 创建导出服务和选项
                var exportService = new CanvasExportService();
                var options = new ExportOptions
                {
                    ShowConfigWindow = true,
                    GridSize = 500,
                    ExportWalls = true,
                    ExportOpenings = true,
                    ExportRooms = true
                };

                // 执行导出
                CanvasDocument document;
                try
                {
                    document = exportService.ExportFromView(view, options);
                }
                catch (OperationCanceledException)
                {
                    // 用户取消
                    return Result.Cancelled;
                }
                catch (NotImplementedException ex)
                {
                    // Adapter 未实现
                    TaskDialog.Show("BIMCanvas",
                        "部分功能尚未实现，请完成 Adapter 代码：\n\n" + ex.Message);
                    return Result.Failed;
                }

                // 弹出保存对话框
                var saveDialog = new SaveFileDialog
                {
                    Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                    Title = "保存 BIMCanvas 文件",
                    FileName = $"{doc.Title}_{view.Name}.json",
                    DefaultExt = ".json",
                    AddExtension = true
                };

                if (saveDialog.ShowDialog() == true)
                {
                    exportService.SaveToFile(document, saveDialog.FileName);

                    TaskDialog.Show("BIMCanvas",
                        $"导出成功！\n\n" +
                        $"文件位置：\n{saveDialog.FileName}\n\n" +
                        $"导出统计：\n" +
                        $"- 墙体：{document.Outline?.Walls?.Count ?? 0} 个\n" +
                        $"- 门窗：{document.Outline?.Openings?.Count ?? 0} 个\n" +
                        $"- 房间：{document.Rooms?.Count ?? 0} 个");
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("BIMCanvas 错误",
                    $"导出失败：\n{ex.Message}\n\n" +
                    $"详细信息：\n{ex.StackTrace}");
                return Result.Failed;
            }
        }
    }
}
