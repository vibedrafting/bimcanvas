using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using NetTopologySuite.Geometries;
using SharedLibrary.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BIMCanvas.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class Test : IExternalCommand
    {
        private UIApplication uiApp;
        private UIDocument uiDoc => uiApp.ActiveUIDocument;
        private Document doc => uiDoc.Document;
        private Autodesk.Revit.ApplicationServices.Application app => uiApp.Application;

        public double _minRoomArea = 1.0; // 最小房间面积，单位平方米

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            uiApp = commandData.Application;

            var regions = GenerateClosedRegions(doc, 0, false);

            foreach (var item in regions)
            {
                foreach (var l in item.GetPolygonLines())
                {
                    if (l.Length>1000/304.8)
                    {
                        doc.DisplayLine(item, SharedLibrary.Class.Enum.ColorType.Red);
                    }
                }
            }

            System.Windows.MessageBox.Show($"成功获取{regions.Count}个封闭曲线，Success!");

            return Result.Succeeded;
        }

        #region # 获取闭合曲线方法
        /// <summary>
        /// 获取指定标高下的所有闭合区域
        /// </summary>
        /// <param name="doc">当前文档</param>
        /// <param name="levelHeight">指定标高高度</param>
        /// <param name="skipSmallArea">是否跳过小于指定面积的区域</param>
        /// <returns>包含创建结果的元组(成功创建的房间数量, 出错的房间数量)</returns>
        public List<Polygon> GenerateClosedRegions(Document doc, double levelHeight = 0, bool skipSmallArea = true)
        {
            List<Polygon> closedRegions = new List<Polygon>();
            Level level = GetNearestLevel(doc, levelHeight);
            // 验证输入参数
            if (doc == null || level == null)
            {
                throw new ArgumentNullException("必要的参数不能为空");
            }

            // 获取该标高下的平面拓扑信息
            PlanTopology planTopology = null;
            using (Transaction transaction = new Transaction(doc, "获取平面拓扑信息"))
            {
                transaction.Start();
                planTopology = doc.get_PlanTopology(level);
                transaction.Commit();
            }
            if (planTopology == null)
            {
                throw new InvalidOperationException("无法获取平面拓扑信息");
            }

            // 遍历所有闭合区域
            foreach (PlanCircuit circuit in planTopology.Circuits)
            {
                if (skipSmallArea)
                {
                    // 检查该区域面积是否大于最小面积
                    if (circuit.Area / 10.7639 > _minRoomArea)
                    {
                        var closedRegion = ConvertPlanCircuitToPolygon(doc, circuit);
                        if (closedRegion != null)
                        {
                            closedRegions.Add(closedRegion);
                        }
                    }
                }
                else
                {
                    var closedRegion = ConvertPlanCircuitToPolygon(doc, circuit);
                    if (closedRegion != null)
                    {
                        closedRegions.Add(closedRegion);
                    }
                }

            }
            return closedRegions;
        }

        /// <summary>
        /// 从 PlanCircuit 转换为 Polygon
        /// </summary>
        /// <param name="doc">当前文档</param>
        /// <param name="circuit">目标的 PlanCircuit</param>
        /// <returns>转换后的 Polygon</returns>
        public Polygon ConvertPlanCircuitToPolygon(Document doc, PlanCircuit circuit)
        {
            // 验证输入参数
            if (doc == null || circuit == null)
            {
                throw new ArgumentNullException("必要的参数不能为空");
            }

            // 创建房间并获取其边界
            Room room = null;
            using (Transaction trans = new Transaction(doc, "临时创建房间"))
            {
                trans.Start();

                // 创建临时房间对象，仅用于获取边界
                if (!circuit.IsRoomLocated)
                {
                    room = doc.Create.NewRoom(null, circuit); // 创建房间对象
                }

                // 如果房间成功创建，则继续处理
                if (room != null)
                {
                    // 获取房间的边界
                    IList<IList<BoundarySegment>> boundarySegments = room.GetBoundarySegments(new SpatialElementBoundaryOptions());
                    List<Coordinate> coordinates = new List<Coordinate>();

                    // 遍历所有边界段并提取坐标
                    foreach (var boundaryList in boundarySegments)
                    {
                        foreach (var boundary in boundaryList)
                        {
                            XYZ start = boundary.GetCurve().GetEndPoint(0);
                            XYZ end = boundary.GetCurve().GetEndPoint(1);

                            // 将 Revit XYZ 转换为 NetTopologySuite Coordinate
                            coordinates.Add(new Coordinate(start.X, start.Y));
                            coordinates.Add(new Coordinate(end.X, end.Y));
                        }
                    }

                    // 创建 Polygon 对象
                    if (coordinates.Count > 2)
                    {
                        // 确保多边形封闭
                        if (!coordinates[0].Equals(coordinates[coordinates.Count - 1]))
                        {
                            coordinates.Add(coordinates[0]); // 闭合多边形
                        }

                        var ring = new LinearRing(coordinates.ToArray());
                        Polygon polygon = new Polygon(ring);

                        // 直接回滚事务，取消房间的创建操作
                        trans.RollBack(); // 直接回滚事务，房间不会被提交

                        return polygon;
                    }
                }

                // 如果无法创建房间或者没有有效的边界，回滚事务
                trans.RollBack();
            }

            return null;
        }

        /// <summary>
        /// 查找距离给定高度最近的标高
        /// </summary>
        /// <param name="doc">当前Revit文档</param>
        /// <param name="height">目标高度（Revit内部单位）</param>
        /// <returns>距离目标高度最近的标高，若文档中没有标高则返回null</returns>
        public Level GetNearestLevel(Document doc, double height)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc), "文档不能为空");

            // 直接使用LINQ查询获取距离最近的标高
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(level => Math.Abs(level.Elevation - height))
                .FirstOrDefault();
        }

        #endregion

    }
}
