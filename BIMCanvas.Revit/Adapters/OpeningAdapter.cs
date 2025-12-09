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
        private readonly CoordinateAdapter _coordAdapter;

        /// <summary>
        /// 创建门窗适配器
        /// </summary>
        /// <param name="coordAdapter">坐标转换器（保留接口兼容，实际不使用）</param>
        public OpeningAdapter(CoordinateAdapter coordAdapter)
        {
            _coordAdapter = coordAdapter ?? throw new ArgumentNullException(nameof(coordAdapter));
        }

        /// <summary>
        /// 提取视图中所有门窗的定位线段
        /// </summary>
        /// <param name="view">Revit 平面视图</param>
        /// <returns>RevitOpening 列表（保持 Revit 原生坐标）</returns>
        public List<RevitOpening> ExtractOpenings(View view)
        {
            var result = new List<RevitOpening>();

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
            foreach (var door in doors)
            {
                var opening = ExtractDoorOpening(door);
                if (opening != null)
                    result.Add(opening);
            }

            // 3. 处理窗
            foreach (var window in windows)
            {
                var opening = ExtractWindowOpening(window);
                if (opening != null)
                    result.Add(opening);
            }

            return result;
        }

        /// <summary>
        /// 提取单个门的信息
        /// </summary>
        private RevitOpening? ExtractDoorOpening(FamilyInstance door)
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

                // 5. 创建 RevitOpening
                return new RevitOpening
                {
                    Id = DataId.NewId("d"),
                    Type = OpeningType.Door,
                    LocationLineStart = start,
                    LocationLineEnd = end
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
        private RevitOpening? ExtractWindowOpening(FamilyInstance window)
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

                // 5. 创建 RevitOpening
                return new RevitOpening
                {
                    Id = DataId.NewId("win"),
                    Type = OpeningType.Window,
                    LocationLineStart = start,
                    LocationLineEnd = end
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

        /// <summary>
        /// 坐标转换器（保留接口兼容，实际未使用）
        /// </summary>
        protected CoordinateAdapter CoordAdapter => _coordAdapter;
    }
}
