using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using BIMCanvas.Core.Models.Revit;
using BIMCanvas.Revit.Converters;
using BIMCanvas.Revit.Utilities;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BIMCanvas.Core.Algorithms.Geometries;

namespace BIMCanvas.Revit.Test
{
    [Transaction(TransactionMode.Manual)]
    public class GetRoomBoundaryTest : IExternalCommand
    {
        private UIApplication uiApp;
        private UIDocument uiDoc => uiApp.ActiveUIDocument;
        private Document doc => uiDoc.Document;
        private Autodesk.Revit.ApplicationServices.Application app => uiApp.Application;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            uiApp = commandData.Application;

            // 识别项目中的房间
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            var rooms = collector.OfCategory(BuiltInCategory.OST_Rooms)
                                                            .OfClass(typeof(SpatialElement))
                                                            .ToElements()
                                                            .Cast<Autodesk.Revit.DB.Architecture.Room>();

            return Result.Succeeded;
        }

        /// <summary>
        /// 获取Room房间的边线集合
        /// </summary>
        /// <param name="room">Room房间元素</param>
        /// <param name="columnAsRoomBounding">是否将柱子作为房间边界</param>
        /// <returns>边线集合 (List<Curve>)</returns>
        public static Polygon GetRoomBoundary(Document doc, Autodesk.Revit.DB.Architecture.Room room)
        {
            List<Curve> curves = new List<Curve>();

            // 获取房间的边界段
            SpatialElementBoundaryOptions options = new SpatialElementBoundaryOptions();
            IList<IList<BoundarySegment>> boundarySegments = room.GetBoundarySegments(options);

            if (boundarySegments != null)
            {
                // 遍历每个边界环路
                foreach (IList<BoundarySegment> segmentList in boundarySegments)
                {
                    // 遍历环路中的每个边界段
                    foreach (BoundarySegment segment in segmentList)
                    {
                        Curve curve = segment.GetCurve();
                        if (curve != null)
                        {
                            curves.Add(curve);
                        }
                    }
                }
            }
            if (!curves.Any() || curves.Count < 3)
                return null;
            var polygon = curves.Select(c => (c as Line).ToLineSegment()).ToList().GeneratePolygon();
            return polygon;
        }

        /// <summary>
        /// 设置柱子的房间边界属性
        /// </summary>
        /// <param name="doc">当前文档</param>
        /// <param name="enable">是否设置柱子作为房间边界</param>
        /// <param name="enableTransaction">是否启用事务</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public static void SetColumnRoomBounding(Document doc, bool enable, bool enableTransaction = true)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc), "文档不能为空");
            try
            {
                // 获取所有柱子元素
                ICollection<Element> columns = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralColumns)
                    .WhereElementIsNotElementType()
                    .ToElements()
                    .Union(new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Columns)
                        .WhereElementIsNotElementType()
                        .ToElements())
                    .ToList();

                if (enableTransaction)
                {
                    // 使用事务修改柱子的房间边界属性
                    using (Transaction trans = new Transaction(doc, "设置房间边界属性"))
                    {
                        trans.Start();
                        foreach (Element column in columns)
                        {
                            // 获取并修改房间边界参数
                            Parameter roomBoundingParam = column.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING);
                            if (roomBoundingParam != null && roomBoundingParam.HasValue)
                            {
                                if (enable)
                                {
                                    // 检查当前值，如果为启用状态则禁用
                                    if (roomBoundingParam.AsInteger() == 0)
                                    {
                                        roomBoundingParam.Set(1); // 1 表示作为房间边界
                                    }
                                }
                                else
                                {
                                    // 检查当前值，如果为启用状态则禁用
                                    if (roomBoundingParam.AsInteger() == 1)
                                    {
                                        roomBoundingParam.Set(0); // 0 表示不作为房间边界
                                    }
                                }
                            }
                        }
                        trans.Commit();
                    }
                }
                else
                {
                    foreach (Element column in columns)
                    {
                        // 获取并修改房间边界参数
                        Parameter roomBoundingParam = column.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING);
                        if (roomBoundingParam != null && roomBoundingParam.HasValue)
                        {
                            if (enable)
                            {
                                // 检查当前值，如果为启用状态则禁用
                                if (roomBoundingParam.AsInteger() == 0)
                                {
                                    roomBoundingParam.Set(1); // 1 表示作为房间边界
                                }
                            }
                            else
                            {
                                // 检查当前值，如果为启用状态则禁用
                                if (roomBoundingParam.AsInteger() == 1)
                                {
                                    roomBoundingParam.Set(0); // 0 表示不作为房间边界
                                }
                            }
                        }
                    }
                }
                return;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("设置柱子房间边界属性失败", ex);
            }
        }

    }
}
