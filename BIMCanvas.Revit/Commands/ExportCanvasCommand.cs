using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIMCanvas.Revit.Services;
using Microsoft.Win32;

namespace BIMCanvas.Revit.Commands
{
    /// <summary>
    /// 导出画布命令 (v3.0)
    /// 从当前平面视图导出 .bcp 项目包
    /// </summary>
    [Transaction(TransactionMode.Manual)]
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
                var options = ExportOptions.Load();

                // 1. 先执行导出（包含房间类型确认）
                CanvasExportService.ExportResult exportResult;
                try
                {
                    exportResult = exportService.ExportFromView(view, options);
                }
                catch (OperationCanceledException)
                {
                    // 用户在房间类型确认时取消
                    return Result.Cancelled;
                }
                catch (NotImplementedException ex)
                {
                    // Adapter 未实现
                    TaskDialog.Show("BIMCanvas",
                        "部分功能尚未实现，请完成 Adapter 代码：\n\n" + ex.Message);
                    return Result.Failed;
                }

                // 2. 再弹出保存对话框选择路径
                var saveDialog = new SaveFileDialog
                {
                    Filter = "BIMCanvas 项目 (*.bcp)|*.bcp|所有文件 (*.*)|*.*",
                    Title = "保存 BIMCanvas 项目",
                    FileName = $"{doc.Title}_{view.Name}",
                    DefaultExt = ".bcp",
                    AddExtension = true
                };

                if (saveDialog.ShowDialog() != true)
                {
                    return Result.Cancelled;
                }

                // 移除扩展名（BcpExporter 会自动添加）
                var outputPath = saveDialog.FileName;
                if (outputPath.EndsWith(".bcp", StringComparison.OrdinalIgnoreCase))
                {
                    outputPath = outputPath.Substring(0, outputPath.Length - 4);
                }

                // 3. 保存到 .bcp 文件
                string bcpPath;
                var exporter = new BcpExporter();
                bcpPath = exporter.ExportToBcp(
                    outputPath,
                    exportResult.ProjectName,
                    exportResult.Manifest,
                    exportResult.Architecture,
                    exportResult.Openings,
                    exportResult.Rooms,
                    exportResult.LocationLines);

                // 显示导出成功信息
                TaskDialog.Show("BIMCanvas",
                    $"导出成功！\n\n" +
                    $"文件位置：\n{bcpPath}\n\n" +
                    $"导出统计：\n" +
                    $"- 墙体：{exportResult.Architecture?.Walls?.Count ?? 0} 个\n" +
                    $"- 柱子：{exportResult.Architecture?.Columns?.Count ?? 0} 个\n" +
                    $"- 门窗：{exportResult.Openings?.Count ?? 0} 个\n" +
                    $"- 定位线：{exportResult.LocationLines?.Count ?? 0} 条\n" +
                    $"- 房间：{exportResult.Rooms?.Count ?? 0} 个\n\n" +
                    $"项目格式：v3.0 Multi-Repo Collection");

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
