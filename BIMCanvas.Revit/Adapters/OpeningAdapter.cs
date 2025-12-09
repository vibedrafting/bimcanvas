using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BIMCanvas.Core.Models.Document;
using BIMCanvas.Core.Models.Primitives;
using BIMCanvas.Revit.Models;
using BIMCanvas.Revit.Utilities;
using NetTopologySuite.Geometries;

namespace BIMCanvas.Revit.Adapters
{
    /// <summary>
    /// 门窗线段提取适配器
    /// </summary>
    public class OpeningAdapter
    {
        /// <summary>
        /// 提取视图中所有门窗的定位线段（返回 NTS/Core 格式数据）
        /// </summary>
        /// <param name="view">Revit 平面视图</param>
        /// <returns>RevitOpening 列表（使用 NTS/Core 几何类型）</returns>
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

                // 3. 获取方向信息
                var directions = OpeningDirectionAnalyzer.CalculateOpeningDirections(door);
                var facingDirection = directions.FacingDirection;
                var handDirections = directions.OpeningDirections; // List<XYZ>

                // 4. 计算定位线起终点（英尺）
                var (start, end) = CalculateLocationLine(locationPoint, width.Value, facingDirection);

                // 5. 转换为 NTS/Core 类型
                return new RevitOpening
                {
                    Id = DataId.NewId("d"),
                    Type = OpeningType.Door,
                    LocationPoint = ToCoordinate(locationPoint),
                    LocationLine = CreateLineSegment(start, end),
                    FacingDirection = ToVec2D(facingDirection),
                    HandDirections = ToVec2DArray(handDirections)
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

                // 4. 计算定位线起终点（英尺）
                var (start, end) = CalculateLocationLine(locationPoint, width.Value, facingDirection);

                // 5. 转换为 NTS/Core 类型
                return new RevitOpening
                {
                    Id = DataId.NewId("win"),
                    Type = OpeningType.Window,
                    LocationPoint = ToCoordinate(locationPoint),
                    LocationLine = CreateLineSegment(start, end),
                    FacingDirection = ToVec2D(facingDirection),
                    HandDirections = new Vec2D[0]  // 窗没有手柄方向
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
        /// Revit XYZ → NTS Coordinate（仅 X, Y，忽略 Z）
        /// </summary>
        private Coordinate ToCoordinate(XYZ xyz)
        {
            return new Coordinate(xyz.X, xyz.Y);
        }

        /// <summary>
        /// Revit XYZ → Core Vec2D（归一化为单位向量）
        /// </summary>
        private Vec2D ToVec2D(XYZ xyz)
        {
            var vec = new Vec2D(xyz.X, xyz.Y);
            return vec.Normalize();
        }

        /// <summary>
        /// Revit XYZ 列表 → Core Vec2D 数组
        /// </summary>
        private Vec2D[] ToVec2DArray(List<XYZ> xyzList)
        {
            if (xyzList == null || xyzList.Count == 0)
                return new Vec2D[0];

            return xyzList.Select(ToVec2D).ToArray();
        }

        /// <summary>
        /// 创建 NTS LineSegment
        /// </summary>
        private NetTopologySuite.Geometries.LineSegment CreateLineSegment(XYZ start, XYZ end)
        {
            return new NetTopologySuite.Geometries.LineSegment(
                new Coordinate(start.X, start.Y),
                new Coordinate(end.X, end.Y)
            );
        }
    }
}
