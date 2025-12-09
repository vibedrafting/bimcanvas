using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIMCanvas.Revit.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BIMCanvas.Revit.Test
{
    /// <summary>
    /// 门信息数据类
    /// </summary>
    public class DoorInfo
    {
        /// <summary>
        /// JSON 数据唯一标识
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Revit 构件 ElementId
        /// </summary>
        public int ElementId { get; set; }

        /// <summary>
        /// 定位点（毫米）
        /// </summary>
        public XYZ LocationPoint { get; set; }

        /// <summary>
        /// 定位线起点（毫米）
        /// </summary>
        public XYZ LocationLineStart { get; set; }

        /// <summary>
        /// 定位线终点（毫米）
        /// </summary>
        public XYZ LocationLineEnd { get; set; }

        /// <summary>
        /// 内外开启方向（面向方向）
        /// </summary>
        public XYZ FacingDirection { get; set; }

        /// <summary>
        /// 左右开启方向列表
        /// </summary>
        public List<XYZ> HandDirections { get; set; }

        /// <summary>
        /// 宽度（毫米）
        /// </summary>
        public double Width { get; set; }

        /// <summary>
        /// 高度（毫米）
        /// </summary>
        public double Height { get; set; }
    }

    /// <summary>
    /// 窗信息数据类
    /// </summary>
    public class WindowInfo
    {
        /// <summary>
        /// JSON 数据唯一标识
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Revit 构件 ElementId
        /// </summary>
        public int ElementId { get; set; }

        /// <summary>
        /// 定位点（毫米）
        /// </summary>
        public XYZ LocationPoint { get; set; }

        /// <summary>
        /// 定位线起点（毫米）
        /// </summary>
        public XYZ LocationLineStart { get; set; }

        /// <summary>
        /// 定位线终点（毫米）
        /// </summary>
        public XYZ LocationLineEnd { get; set; }

        /// <summary>
        /// 内外开启方向（面向方向）
        /// </summary>
        public XYZ FacingDirection { get; set; }

        /// <summary>
        /// 窗台高度（毫米）
        /// </summary>
        public double SillHeight { get; set; }

        /// <summary>
        /// 宽度（毫米）
        /// </summary>
        public double Width { get; set; }

        /// <summary>
        /// 高度（毫米）
        /// </summary>
        public double Height { get; set; }
    }

    /// <summary>
    /// 测试批量获取门窗信息
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class OpeningInfoTest : IExternalCommand
    {
        private UIApplication uiApp;
        private UIDocument uiDoc => uiApp.ActiveUIDocument;
        private Document doc => uiDoc.Document;

        // 英尺转毫米系数
        private const double FeetToMm = 304.8;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            uiApp = commandData.Application;

            try
            {
                // 获取所有门信息
                List<DoorInfo> doorInfos = new List<DoorInfo>();

                // 获取所有窗信息
                List<WindowInfo> windowInfos = new List<WindowInfo>();

                // 获取所有门
                var doors = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Doors)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .ToList();

                // 获取所有窗
                var windows = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Windows)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .ToList();

                // 处理门
                foreach (var door in doors)
                {
                    var info = GetDoorInfo(door);
                    if (info != null)
                    {
                        doorInfos.Add(info);
                    }
                }

                // 处理窗
                foreach (var window in windows)
                {
                    var info = GetWindowInfo(window);
                    if (info != null)
                    {
                        windowInfos.Add(info);
                    }
                }

                // 输出结果
                ShowResults(doorInfos, windowInfos);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// 获取门信息
        /// </summary>
        private DoorInfo GetDoorInfo(FamilyInstance door)
        {
            try
            {
                var info = new DoorInfo
                {
                    Id = Guid.NewGuid(),
                    ElementId = door.Id.IntegerValue
                };

                // 获取定位点
                var locationPoint = door.Location as LocationPoint;
                if (locationPoint != null)
                {
                    info.LocationPoint = ConvertToMm(locationPoint.Point);
                }

                // 获取宽度和高度
                info.Width = GetParameterValueInMm(door, BuiltInParameter.DOOR_WIDTH)
                             ?? GetParameterValueInMm(door, BuiltInParameter.FAMILY_WIDTH_PARAM)
                             ?? GetSymbolParameterValueInMm(door, "Width")
                             ?? 0;

                info.Height = GetParameterValueInMm(door, BuiltInParameter.DOOR_HEIGHT)
                              ?? GetParameterValueInMm(door, BuiltInParameter.FAMILY_HEIGHT_PARAM)
                              ?? GetSymbolParameterValueInMm(door, "Height")
                              ?? 0;

                // 获取开启方向
                var directions = OpeningDirectionAnalyzer.CalculateOpeningDirections(door);
                info.FacingDirection = directions.FacingDirection;
                info.HandDirections = directions.OpeningDirections;

                // 计算定位线
                CalculateDoorLocationLine(info);

                return info;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 获取窗信息
        /// </summary>
        private WindowInfo GetWindowInfo(FamilyInstance window)
        {
            try
            {
                var info = new WindowInfo
                {
                    Id = Guid.NewGuid(),
                    ElementId = window.Id.IntegerValue
                };

                // 获取定位点
                var locationPoint = window.Location as LocationPoint;
                if (locationPoint != null)
                {
                    info.LocationPoint = ConvertToMm(locationPoint.Point);
                }

                // 获取宽度和高度
                info.Width = GetParameterValueInMm(window, BuiltInParameter.WINDOW_WIDTH)
                             ?? GetParameterValueInMm(window, BuiltInParameter.FAMILY_WIDTH_PARAM)
                             ?? GetSymbolParameterValueInMm(window, "Width")
                             ?? 0;

                info.Height = GetParameterValueInMm(window, BuiltInParameter.WINDOW_HEIGHT)
                              ?? GetParameterValueInMm(window, BuiltInParameter.FAMILY_HEIGHT_PARAM)
                              ?? GetSymbolParameterValueInMm(window, "Height")
                              ?? 0;

                // 获取窗台高度
                info.SillHeight = GetParameterValueInMm(window, BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM) ?? 0;

                // 获取开启方向
                info.FacingDirection = OpeningDirectionAnalyzer.GetWindowFacingDirection(window);

                // 计算定位线
                CalculateWindowLocationLine(info);

                return info;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 计算门定位线
        /// </summary>
        private void CalculateDoorLocationLine(DoorInfo info)
        {
            if (info.LocationPoint == null || info.FacingDirection == null || info.Width <= 0)
            {
                return;
            }

            // 定位线方向 = 面向方向垂直方向（叉乘 Z 轴）
            XYZ lineDirection = info.FacingDirection.CrossProduct(XYZ.BasisZ).Normalize();

            // 半宽（已经是毫米单位）
            double halfWidth = info.Width / 2.0;

            // 定位线起点和终点（注意：LocationPoint 已经是毫米单位，lineDirection 是无量纲单位向量）
            info.LocationLineStart = new XYZ(
                info.LocationPoint.X - halfWidth * lineDirection.X,
                info.LocationPoint.Y - halfWidth * lineDirection.Y,
                info.LocationPoint.Z
            );

            info.LocationLineEnd = new XYZ(
                info.LocationPoint.X + halfWidth * lineDirection.X,
                info.LocationPoint.Y + halfWidth * lineDirection.Y,
                info.LocationPoint.Z
            );
        }

        /// <summary>
        /// 计算窗定位线
        /// </summary>
        private void CalculateWindowLocationLine(WindowInfo info)
        {
            if (info.LocationPoint == null || info.FacingDirection == null || info.Width <= 0)
            {
                return;
            }

            // 定位线方向 = 面向方向垂直方向（叉乘 Z 轴）
            XYZ lineDirection = info.FacingDirection.CrossProduct(XYZ.BasisZ).Normalize();

            // 半宽（已经是毫米单位）
            double halfWidth = info.Width / 2.0;

            // 定位线起点和终点（注意：LocationPoint 已经是毫米单位，lineDirection 是无量纲单位向量）
            info.LocationLineStart = new XYZ(
                info.LocationPoint.X - halfWidth * lineDirection.X,
                info.LocationPoint.Y - halfWidth * lineDirection.Y,
                info.LocationPoint.Z
            );

            info.LocationLineEnd = new XYZ(
                info.LocationPoint.X + halfWidth * lineDirection.X,
                info.LocationPoint.Y + halfWidth * lineDirection.Y,
                info.LocationPoint.Z
            );
        }

        /// <summary>
        /// 获取参数值（转换为毫米）
        /// </summary>
        private double? GetParameterValueInMm(FamilyInstance fi, BuiltInParameter param)
        {
            var p = fi.get_Parameter(param);
            if (p != null && p.HasValue && p.StorageType == StorageType.Double)
            {
                return p.AsDouble() * FeetToMm;
            }
            return null;
        }

        /// <summary>
        /// 获取族类型参数值（转换为毫米）
        /// </summary>
        private double? GetSymbolParameterValueInMm(FamilyInstance fi, string paramName)
        {
            var symbol = fi.Symbol;
            var p = symbol.LookupParameter(paramName);
            if (p != null && p.HasValue && p.StorageType == StorageType.Double)
            {
                return p.AsDouble() * FeetToMm;
            }
            return null;
        }

        /// <summary>
        /// 将 XYZ 坐标从英尺转换为毫米
        /// </summary>
        private XYZ ConvertToMm(XYZ point)
        {
            return new XYZ(point.X * FeetToMm, point.Y * FeetToMm, point.Z * FeetToMm);
        }

        /// <summary>
        /// 显示结果
        /// </summary>
        private void ShowResults(List<DoorInfo> doorInfos, List<WindowInfo> windowInfos)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"共获取 {doorInfos.Count + windowInfos.Count} 个门窗信息");
            sb.AppendLine($"门: {doorInfos.Count} 个");
            sb.AppendLine($"窗: {windowInfos.Count} 个");
            sb.AppendLine();

            // 显示门信息
            sb.AppendLine("===== 门信息 =====");
            foreach (var info in doorInfos.Take(5))
            {
                sb.AppendLine($"--- Door (ElementId: {info.ElementId}) ---");
                sb.AppendLine($"  GUID: {info.Id}");
                sb.AppendLine($"  定位点: ({info.LocationPoint?.X:F0}, {info.LocationPoint?.Y:F0}, {info.LocationPoint?.Z:F0}) mm");
                sb.AppendLine($"  定位线: ({info.LocationLineStart?.X:F0}, {info.LocationLineStart?.Y:F0}) -> ({info.LocationLineEnd?.X:F0}, {info.LocationLineEnd?.Y:F0}) mm");
                sb.AppendLine($"  面向方向: ({info.FacingDirection?.X:F2}, {info.FacingDirection?.Y:F2})");

                if (info.HandDirections?.Count > 0)
                {
                    var handStr = string.Join("; ", info.HandDirections.Select(h => $"({h.X:F2}, {h.Y:F2})"));
                    sb.AppendLine($"  开启方向: {handStr}");
                }

                sb.AppendLine($"  尺寸: {info.Width:F0} x {info.Height:F0} mm");
                sb.AppendLine();
            }

            if (doorInfos.Count > 5)
            {
                sb.AppendLine($"... 还有 {doorInfos.Count - 5} 个门未显示");
                sb.AppendLine();
            }

            // 显示窗信息
            sb.AppendLine("===== 窗信息 =====");
            foreach (var info in windowInfos.Take(5))
            {
                sb.AppendLine($"--- Window (ElementId: {info.ElementId}) ---");
                sb.AppendLine($"  GUID: {info.Id}");
                sb.AppendLine($"  定位点: ({info.LocationPoint?.X:F0}, {info.LocationPoint?.Y:F0}, {info.LocationPoint?.Z:F0}) mm");
                sb.AppendLine($"  定位线: ({info.LocationLineStart?.X:F0}, {info.LocationLineStart?.Y:F0}) -> ({info.LocationLineEnd?.X:F0}, {info.LocationLineEnd?.Y:F0}) mm");
                sb.AppendLine($"  面向方向: ({info.FacingDirection?.X:F2}, {info.FacingDirection?.Y:F2})");
                sb.AppendLine($"  窗台高度: {info.SillHeight:F0} mm");
                sb.AppendLine($"  尺寸: {info.Width:F0} x {info.Height:F0} mm");
                sb.AppendLine();
            }

            if (windowInfos.Count > 5)
            {
                sb.AppendLine($"... 还有 {windowInfos.Count - 5} 个窗未显示");
            }

            TaskDialog.Show("门窗信息", sb.ToString());
        }
    }
}
