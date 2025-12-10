using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BIMCanvas.Core.Models.Document;
using BIMCanvas.Revit.Models;
using BIMCanvas.Revit.Utilities;
using NetTopologySuite.Geometries;
using NetTopologySuite.Mathematics;

namespace BIMCanvas.Revit.Adapters
{
    /// <summary>
    /// 门窗数据提取适配器
    /// </summary>
    public class OpeningAdapter
    {
        /// <summary>
        /// 提取视图中所有门窗
        /// </summary>
        /// <param name="view">Revit 平面视图</param>
        /// <returns>RevitOpening 列表（保留 Revit 原生坐标，feet 单位）</returns>
        public List<RevitOpening> ExtractOpenings(View view)
        {
            if (view == null)
                throw new ArgumentNullException(nameof(view));

            var doc = view.Document;
            var result = new List<RevitOpening>();

            // 1. 收集门元素
            var doors = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Doors)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .ToList();

            // 2. 收集窗元素
            var windows = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Windows)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .ToList();

            // 3. 处理门
            PrefixId.Reset("d");
            foreach (var door in doors)
            {
                var opening = ExtractDoorOpening(door);
                if (opening != null)
                    result.Add(opening);
            }

            // 4. 处理窗
            PrefixId.Reset("win");
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
        /// <param name="door">门族实例</param>
        /// <returns>RevitOpening 或 null（提取失败时）</returns>
        private RevitOpening ExtractDoorOpening(FamilyInstance door)
        {
            try
            {
                // 1. 获取定位点（英尺）
                var locationPoint = (door.Location as LocationPoint)?.Point;
                if (locationPoint == null)
                    return null;

                // 2. 获取宽度（英尺）- 从族类型获取
                var widthParam = door.Symbol.get_Parameter(BuiltInParameter.DOOR_WIDTH);
                var width = widthParam?.AsDouble() ?? 0.0;
                if (width <= 0) width = 1000 / 304.8; // 默认1000mm转换为英尺

                // 3. 获取方向信息
                var directions = OpeningDirectionAnalyzer.CalculateOpeningDirections(door);
                var facingDirection = directions.FacingDirection;
                var handDirections = directions.OpeningDirections ?? new List<XYZ>();

                // 4. 计算定位线起终点（英尺）
                var (start, end) = CalculateLocationLine(locationPoint, width, facingDirection);

                // 5. 创建 RevitOpening 对象
                return new RevitOpening
                {
                    Id = PrefixId.NewId("d"),
                    ElementId = door.Id.IntegerValue,
                    Type = OpeningType.Door,
                    LocationPoint = locationPoint.ToCoordinate(),
                    LocationLine = CreateLineSegment(start, end),
                    FacingDirection = facingDirection.ToVector2D(),
                    HandDirections = handDirections.Select(d => d.ToVector2D()).ToList()
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 提取单个窗的信息
        /// </summary>
        /// <param name="window">窗族实例</param>
        /// <returns>RevitOpening 或 null（提取失败时）</returns>
        private RevitOpening ExtractWindowOpening(FamilyInstance window)
        {
            try
            {
                // 1. 获取定位点（英尺）
                var locationPoint = (window.Location as LocationPoint)?.Point;
                if (locationPoint == null)
                    return null;

                // 2. 获取宽度（英尺）- 从族类型获取
                var widthParam = window.Symbol.get_Parameter(BuiltInParameter.WINDOW_WIDTH);
                var width = widthParam?.AsDouble() ?? 0.0;
                if (width <= 0) width = 1000 / 304.8; // 默认1000mm转换为英尺

                // 3. 获取面向方向
                var facingDirection = OpeningDirectionAnalyzer.GetWindowFacingDirection(window);

                // 4. 计算定位线起终点（英尺）
                var (start, end) = CalculateLocationLine(locationPoint, width, facingDirection);

                // 5. 创建 RevitOpening 对象
                return new RevitOpening
                {
                    Id = PrefixId.NewId("win"),
                    ElementId = window.Id.IntegerValue,
                    Type = OpeningType.Window,
                    LocationPoint = locationPoint.ToCoordinate(),
                    LocationLine = CreateLineSegment(start, end),
                    FacingDirection = facingDirection.ToVector2D(),
                    HandDirections = new List<Vector2D>()  // 窗户无左右开启方向
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 计算定位线（保持 Revit 原生单位：英尺）
        /// </summary>
        /// <param name="locationPoint">定位点</param>
        /// <param name="width">门窗宽度（英尺）</param>
        /// <param name="facingDirection">面向方向</param>
        /// <returns>定位线起终点元组</returns>
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
        /// 创建 NTS LineSegment
        /// </summary>
        /// <param name="start">起点（Revit XYZ）</param>
        /// <param name="end">终点（Revit XYZ）</param>
        /// <returns>NTS LineSegment</returns>
        private NetTopologySuite.Geometries.LineSegment CreateLineSegment(XYZ start, XYZ end)
        {
            return new NetTopologySuite.Geometries.LineSegment(
                new Coordinate(start.X, start.Y),
                new Coordinate(end.X, end.Y)
            );
        }
    }
}
