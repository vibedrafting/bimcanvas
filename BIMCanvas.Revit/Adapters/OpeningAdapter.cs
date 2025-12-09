using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BIMCanvas.Core.Models.Document;
using BIMCanvas.Revit.Models;
using BIMCanvas.Revit.Utilities;

namespace BIMCanvas.Revit.Adapters
{
    /// <summary>
    /// 门窗线段提取适配器
    /// </summary>
    public class OpeningAdapter
    {
        /// <summary>
        /// 提取视图中所有门窗的定位线段（返回 Revit 原生数据）
        /// </summary>
        /// <param name="view">Revit 平面视图</param>
        /// <returns>RawOpening 列表（未转换坐标）</returns>
        public List<RawOpening> ExtractOpenings(View view)
        {
            var result = new List<RawOpening>();

            // 1. 收集门窗元素
            var doors = new FilteredElementCollector(view.Document, view.Id)
                .OfCategory(BuiltInCategory.OST_Doors)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .ToList();

            var windows = new FilteredElementCollector(view.Document, view.Id)
                .OfCategory(BuiltInCategory.OST_Windows)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .ToList();

            // 2. 处理门
            DataId.Reset("d");
            foreach (var door in doors)
            {
                var opening = ExtractDoorOpening(door);
                if (opening != null)
                    result.Add(opening.Value);
            }

            // 3. 处理窗
            DataId.Reset("win");
            foreach (var window in windows)
            {
                var opening = ExtractWindowOpening(window);
                if (opening != null)
                    result.Add(opening.Value);
            }

            return result;
        }

        /// <summary>
        /// 提取单个门的信息
        /// </summary>
        private RawOpening? ExtractDoorOpening(FamilyInstance door)
        {
            try
            {
                // 1. 获取定位点（英尺）
                var locationPoint = (door.Location as LocationPoint)?.Point;
                if (locationPoint == null) return null;

                // 2. 获取宽度（英尺）
                var width = GetParameterValue(door, BuiltInParameter.DOOR_WIDTH)
                         ?? GetParameterValue(door, BuiltInParameter.FAMILY_WIDTH_PARAM)
                         ?? GetSymbolParameterValue(door, "Width");
                if (width == null) return null;

                // 3. 获取面向方向
                var directions = OpeningDirectionAnalyzer.CalculateOpeningDirections(door);
                var facingDirection = directions.FacingDirection;

                // 4. 计算定位线（英尺）
                var (start, end) = CalculateLocationLine(locationPoint, width.Value, facingDirection);

                // 5. 创建 Revit Line 对象
                var line = Line.CreateBound(start, end);

                // 6. 创建 RawOpening
                return new RawOpening
                {
                    Id = DataId.NewId("d"),
                    Type = OpeningType.Door,
                    Line = line
                };
            }
            catch
            {
                return null;  // 提取失败，跳过
            }
        }

        /// <summary>
        /// 提取单个窗的信息
        /// </summary>
        private RawOpening? ExtractWindowOpening(FamilyInstance window)
        {
            try
            {
                // 1. 获取定位点（英尺）
                var locationPoint = (window.Location as LocationPoint)?.Point;
                if (locationPoint == null) return null;

                // 2. 获取宽度（英尺）
                var width = GetParameterValue(window, BuiltInParameter.WINDOW_WIDTH)
                         ?? GetParameterValue(window, BuiltInParameter.FAMILY_WIDTH_PARAM)
                         ?? GetSymbolParameterValue(window, "Width");
                if (width == null) return null;

                // 3. 获取面向方向
                var facingDirection = OpeningDirectionAnalyzer.GetWindowFacingDirection(window);

                // 4. 计算定位线（英尺）
                var (start, end) = CalculateLocationLine(locationPoint, width.Value, facingDirection);

                // 5. 创建 Revit Line 对象
                var line = Line.CreateBound(start, end);

                // 6. 创建 RawOpening
                return new RawOpening
                {
                    Id = DataId.NewId("win"),
                    Type = OpeningType.Window,
                    Line = line
                };
            }
            catch
            {
                return null;  // 提取失败，跳过
            }
        }

        /// <summary>
        /// 计算定位线（保持 Revit 原生单位：英尺）
        /// </summary>
        private (XYZ start, XYZ end) CalculateLocationLine(XYZ locationPoint, double width, XYZ facingDirection)
        {
            // 定位线方向 = 面向方向垂直方向（叉乘 Z 轴）
            var lineDirection = facingDirection.CrossProduct(XYZ.BasisZ).Normalize();
            var halfWidth = width / 2.0;

            // 起终点（英尺）
            var start = new XYZ(
                locationPoint.X - halfWidth * lineDirection.X,
                locationPoint.Y - halfWidth * lineDirection.Y,
                locationPoint.Z
            );

            var end = new XYZ(
                locationPoint.X + halfWidth * lineDirection.X,
                locationPoint.Y + halfWidth * lineDirection.Y,
                locationPoint.Z
            );

            return (start, end);
        }

        /// <summary>
        /// 获取参数值（英尺）
        /// </summary>
        private double? GetParameterValue(FamilyInstance fi, BuiltInParameter param)
        {
            var p = fi.get_Parameter(param);
            if (p != null && p.HasValue && p.StorageType == StorageType.Double)
                return p.AsDouble();
            return null;
        }

        /// <summary>
        /// 获取族类型参数值（英尺）
        /// </summary>
        private double? GetSymbolParameterValue(FamilyInstance fi, string paramName)
        {
            var symbol = fi.Symbol;
            var p = symbol.LookupParameter(paramName);
            if (p != null && p.HasValue && p.StorageType == StorageType.Double)
                return p.AsDouble();
            return null;
        }
    }
}
