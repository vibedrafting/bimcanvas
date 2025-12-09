using System;
using System.Reflection;
using Autodesk.Revit.UI;

namespace BIMCanvas.Revit.Commands
{
    /// <summary>
    /// Revit 外部应用程序入口
    /// 负责创建 Ribbon 面板和按钮
    /// </summary>
    public class App : IExternalApplication
    {
        /// <summary>
        /// Revit 启动时调用
        /// </summary>
        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                // 创建 Ribbon 面板
                var panel = application.CreateRibbonPanel("BIMCanvas");

                // 获取当前程序集路径
                var assemblyPath = Assembly.GetExecutingAssembly().Location;

                // 添加导出按钮
                var exportButtonData = new PushButtonData(
                    "ExportCanvas",
                    "导出画布",
                    assemblyPath,
                    typeof(ExportCanvasCommand).FullName
                );

                exportButtonData.ToolTip = "将当前平面视图导出为 BIMCanvas JSON 文件";
                exportButtonData.LongDescription =
                    "从当前活动的平面视图中提取边界、门窗、房间信息，\n" +
                    "导出为 BIMCanvas 格式的 JSON 文件。\n\n" +
                    "导出前会弹出窗口让您确认房间类型。";

                // 设置图标（如果有的话）
                // exportButtonData.LargeImage = new BitmapImage(new Uri("pack://application:,,,/BIMCanvas.Revit;component/Resources/export32.png"));
                // exportButtonData.Image = new BitmapImage(new Uri("pack://application:,,,/BIMCanvas.Revit;component/Resources/export16.png"));

                panel.AddItem(exportButtonData);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("BIMCanvas 错误", $"插件启动失败：\n{ex.Message}");
                return Result.Failed;
            }
        }

        /// <summary>
        /// Revit 关闭时调用
        /// </summary>
        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
