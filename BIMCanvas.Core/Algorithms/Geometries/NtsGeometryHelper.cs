using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Mathematics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BIMCanvas.Core.Algorithms.Geometries
{
    public static class NtsGeometryHelper
    {
        #region # 平面几何方法集
        #region ## NetTopologySuite方法补充
        /// <summary>
        /// 顺时针多边形
        /// </summary>
        /// <param name="plg"></param>
        /// <returns></returns>
        public static Polygon ClockwisePolygon(this Polygon plg)
        {
            if (!plg.Shell.IsCCW)
            {
                return plg;
            }
            else
            {
                var coords = GetPolygonCoords(plg);
                var newCoords = coords.ToArray().Reverse().ToList();
                List<Coordinate> newLinearRing = new List<Coordinate>();
                newLinearRing.AddRange(newCoords);
                newLinearRing.Add(newCoords[0]);

                LinearRing linearRing = new LinearRing(newLinearRing.ToArray());        //生成多边形需要的环
                Polygon polygon = new Polygon(linearRing);                  //生成多边形
                return polygon;
            }
        }

        /// <summary>
        /// 获取顺时针旋转90度的垂直向量
        /// </summary>
        /// <param name="vector">原始向量</param>
        /// <returns>顺时针旋转90度后的垂直向量</returns>
        public static Vector2D GetPerpVector(this Vector2D vector)
        {
            if (vector == null)
            {
                return null;
            }

            // 顺时针旋转90度后，新的X为原Y，新的Y为原X的负值
            double perpX = vector.Y;
            double perpY = -vector.X;

            return new Vector2D(perpX, perpY);
        }

        /// <summary>
        /// 生成多边形
        /// </summary>
        /// <param name="segments">想要生成多边形的轮廓线哈希集</param>
        /// <returns>生成的多边形</returns>
        public static Polygon GeneratePolygon(this ICollection<LineSegment> segments, bool autoIntersection = false)
        {
            List<Coordinate> coordinates = new List<Coordinate>();      //环中的第一个和最后一个坐标必须相等
            // 直接生成
            if (!autoIntersection)
            {
                //生成环所需的点、多边形的点、多边形的线
                for (int i = 0; i < segments.Count; i++)
                {
                    coordinates.Add(new Coordinate(segments.ElementAt(i).P0.X, segments.ElementAt(i).P0.Y));
                }
            }
            // 自动计算交点
            else
            {
                for (int i = 0; i < segments.Count; i++)
                {
                    var currentSegment = segments.ElementAt(i);
                    var nextSegment = segments.Next(segments.ElementAt(i));
                    if (currentSegment.IsCollinear(nextSegment))
                    {
                        continue;
                    }
                    var intersection = currentSegment.GetIntersectionPoint(nextSegment);
                    if (intersection != null)
                    {
                        coordinates.Add(intersection);
                    }
                }
            }
            return coordinates.GeneratePolygon();
        }

        /// <summary>
        /// 生成多边形
        /// </summary>
        /// <param name="segment">初始偏移线</param>
        /// <param name="vec">偏移方向</param>
        /// <param name="distance">偏移 距离</param>
        /// <returns></returns>
        public static Polygon GeneratePolygon(this LineSegment segment, Vector2D vec, double distance)
        {
            List<Coordinate> coordinates = new List<Coordinate>();
            var newSegment = segment.Translate(vec, distance);
            coordinates = new List<Coordinate>
            {
                segment.P0,
                segment.P1,
                newSegment.P1,
                newSegment.P0
            };
            return coordinates.GeneratePolygon();
        }

        /// <summary>
        /// 生成多边形
        /// </summary>
        /// <param name="coordinates">要生成多边形的坐标集合</param>
        /// <returns>生成的多边形</returns>
        public static Polygon GeneratePolygon(this ICollection<Coordinate> coordinates)
        {
            Coordinate[] newCoordinates = new Coordinate[coordinates.Count + 1];
            for (int i = 0; i < coordinates.Count; i++)
            {
                newCoordinates[i] = coordinates.ElementAt(i);
            }
            newCoordinates[newCoordinates.Length - 1] = newCoordinates[0];
            Polygon polygon = new Polygon(new LinearRing(newCoordinates));
            return polygon;
        }

        #endregion

        #region ## 角度计算方法
        /// <summary>
        /// 计算两个向量之间的夹角，并区分顺时针和逆时针（逆时针为正）
        /// </summary>
        /// <param name="v1">第一个向量（Vector2D）</param>
        /// <param name="v2">第二个向量（Vector2D）</param>
        /// <returns>夹角（弧度制）</returns>
        public static double RealAngleTo(this Vector2D v1, Vector2D v2)
        {
            // 计算点积
            double dot = v1.X * v2.X + v1.Y * v2.Y;

            // 计算向量模长
            double magV1 = Math.Sqrt(v1.X * v1.X + v1.Y * v1.Y);
            double magV2 = Math.Sqrt(v2.X * v2.X + v2.Y * v2.Y);

            // 计算夹角的余弦值
            double cosTheta = dot / (magV1 * magV2);

            // 限制余弦值在[-1, 1]范围内，以防止浮点运算误差
            cosTheta = Math.Max(-1, Math.Min(1, cosTheta));

            // 计算未定向的夹角
            double angleRadians = Math.Acos(cosTheta);
            //double angleDegrees = angleRadians * (180 / Math.PI);

            // 计算叉积
            double cross = v1.X * v2.Y - v1.Y * v2.X;

            // 根据叉积的符号来判断方向
            if (cross < 0)
            {
                // 如果叉积小于 0，说明 v2 在 v1 的顺时针方向
                // 将夹角转换为大于 π 的角度，表示顺时针
                angleRadians = 2 * Math.PI - angleRadians;
                //angleDegrees = 360 - angleDegrees; // 如果需要角度制，可以转换
            }

            // 返回夹角，范围为 [0, 2*PI]
            return angleRadians;
        }

        /// <summary>
        /// 根据输入夹角和旋转角度计算旋转后的夹角，并修正为 [0, 2*PI] 范围内
        /// </summary>
        /// <param name="currentAngleRadians">当前夹角（弧度制）</param>
        /// <param name="rotationAngleDegrees">旋转角度（角度制）</param>
        /// <returns>旋转且修正后的夹角（弧度制），范围 [0, 2*PI]</returns>
        public static double RotateAndNormalizeAngle(double currentAngleRadians, double rotationAngleDegrees)
        {
            // 将旋转角度从角度制转换为弧度制
            double rotationAngleRadians = rotationAngleDegrees * (Math.PI / 180.0);

            // 计算旋转后的夹角
            double rotatedAngle = currentAngleRadians + rotationAngleRadians;

            // 保证结果在 [0, 2*PI] 范围内
            rotatedAngle = rotatedAngle % (2 * Math.PI);

            // 如果结果是负数，则加上 2*PI 确保在正的范围内
            if (rotatedAngle < 0)
            {
                rotatedAngle += 2 * Math.PI;
            }

            return rotatedAngle;
        }

        /// <summary>
        /// 将角度转换为弧度
        /// </summary>
        /// <param name="degrees">角度值</param>
        /// <returns>对应的弧度值</returns>
        public static double ToRadians(this double degrees)
        {
            return degrees * (Math.PI / 180);
        }

        /// <summary>
        /// 将弧度转换为角度
        /// </summary>
        /// <param name="radians">弧度值</param>
        /// <returns>对应的角度值</returns>
        public static double ToDegrees(this double radians)
        {
            return radians * (180 / Math.PI);
        }

        #endregion

        #region ## 多边形规范方法
        #region ### 规范化多边形
        /// <summary>
        /// 规范化多边形
        /// </summary>
        /// <param name="inputPolygon">输入多边形</param>
        /// <param name="mergeCollinearSegments">是否合并共线线段</param>
        /// <param name="ignoreSmallEdges">是否忽略无效和过小的边线</param>
        /// <param name="normalizeDirections">是否规范化边线方向</param>
        /// <param name="minLineLength">最小边线长度</param>
        /// <param name="angleTolerance">角度误差容忍度（度）</param>
        /// <returns>返回规范化后的多边形</returns>
        public static Polygon NormalizePolygon(
            this Polygon inputPolygon,
            bool mergeCollinearSegments = true,
            bool ignoreSmallEdges = true,
            bool normalizeDirections = true,
            double minLineLength = 0.01 / 304.8,
            double angleTolerance = 1)
        {
            // 0. 顺时针存储边线
            inputPolygon = inputPolygon.ClockwisePolygon();
            Polygon validPolygon = inputPolygon;

            // 1. 合并共线线段
            if (mergeCollinearSegments)
            {
                validPolygon = MergeCollinearSegments(validPolygon);
            }

            // 2. 过滤无效和过小的垂直边线
            if (ignoreSmallEdges)
            {
                validPolygon = RemoveSmallEdges(validPolygon, minLineLength); // 调用过滤小边线的方法
            }

            //// 3. 调整边线角度
            //if (normalizeDirections)
            //{
            //    validCoordinates = NormalizeDirections(validCoordinates, angleTolerance); // 规范化边线方向
            //}

            // 如果有效坐标点数不足3个，则认为不构成多边形
            if (validPolygon.GetPolygonCoords().Count < 3)
            {
                return null; // 返回 null 或空的多边形
            }
            // 返回规范化后的多边形
            return validPolygon;
        }

        /// <summary>
        /// 过滤无效和过小的边线
        /// </summary>
        /// <param name="polygon"></param>
        /// <param name="minLineLength"></param>
        /// <returns></returns>
        private static Polygon RemoveSmallEdges(Polygon polygon, double minLineLength)
        {
            Polygon validPolygon = polygon;
            var segments = polygon.GetPolygonLines(); // 获取多边形的边线
            var centroid = polygon.Centroid;    // 形心

            var validSegments = new List<LineSegment>(); // 存储有效的边线
            var validCoords = new List<Coordinate>(); // 存储有效的坐标

            var invalidSegments = new HashSet<LineSegment>(); // 存储无效的边线
            // 排除如下情况的过小边线
            foreach (var segment in segments)
            {
                // 如果边线长度小于指定的最小长度
                if (segment.Length < minLineLength)
                {
                    invalidSegments.Add(segment);
                    var previousSegment = segments.Previous(segment);
                    var nextSegment = segments.Next(segment);
                    while (previousSegment.Length < minLineLength)
                    {
                        invalidSegments.Add(previousSegment);
                        previousSegment = segments.Previous(previousSegment);
                    }

                    while (nextSegment.Length < minLineLength)
                    {
                        invalidSegments.Add(previousSegment);
                        nextSegment = segments.Next(nextSegment);
                    }
                }
            }

            if (!invalidSegments.Any())
                return validPolygon;

            // 过滤出有效边线（含相邻的平行线段）
            validSegments = segments.Where(s => !invalidSegments.Contains(s)).ToList();

            // 过滤掉相邻的平行的线段
            var parallelSegementGroupList = new List<List<LineSegment>>();
            foreach (var segment in validSegments)
            {
                // 不在已找到的相邻平行组中
                if (parallelSegementGroupList.SelectMany(s => s).Contains(segment))
                    continue;

                var previousSegment = validSegments.Previous(segment);
                var nextSegment = validSegments.Next(segment);

                var parallelSegementGroup = new List<LineSegment>();
                if (previousSegment.IsParallel(segment) || nextSegment.IsParallel(segment))
                {
                    parallelSegementGroup.Add(segment);
                }
                else
                {
                    continue;
                }

                // 向前
                while (previousSegment.IsParallel(segment))
                {
                    parallelSegementGroup.Add(previousSegment);
                    previousSegment = validSegments.Previous(previousSegment);
                }

                // 向后
                while (nextSegment.IsParallel(segment))
                {
                    parallelSegementGroup.Add(nextSegment);
                    nextSegment = validSegments.Next(nextSegment);
                }

                if (parallelSegementGroup.Any())
                    parallelSegementGroupList.Add(parallelSegementGroup);
            }

            if (parallelSegementGroupList.Any())
            {
                // 根据距离形心距离挑选一个代表相邻平行组的线段
                for (int i = 0; i < parallelSegementGroupList.Count; i++)
                {
                    var parallelSegementGroup = parallelSegementGroupList[i];
                    parallelSegementGroupList[i] = parallelSegementGroup.OrderBy(p => p.Distance(centroid.Coordinate)).Skip(1).ToList();
                }

                // 过滤出相邻的不平行的线段
                var parallelSegements = parallelSegementGroupList.SelectMany(p => p);
                validSegments = validSegments.Where(s => !parallelSegements.Contains(s)).ToList();
            }

            //获取交点
            foreach (var segment in validSegments)
            {

                var nextSegment = validSegments.Next(segment);
                if (segment.IsCollinear(nextSegment))
                {
                    continue;
                }
                var coord = segment.IntersectPoint(nextSegment);

                if (coord != null)
                {
                    validCoords.Add(coord);
                }
            }
            if (validCoords.Count <= 3)
            {
                return validPolygon;
            }
            validPolygon = validCoords.GeneratePolygon();
            return validPolygon;
        }

        /// <summary>
        /// 合并共线线段
        /// </summary>
        /// <param name="coordinates">输入坐标列表</param>
        /// <returns>返回合并后的坐标列表</returns>
        private static Polygon MergeCollinearSegments(Polygon polygon)
        {
            List<Coordinate> coordinates = polygon.GetPolygonCoords();
            var mergedCoordinates = new List<Coordinate>();
            for (int i = 0; i < coordinates.Count; i++)
            {
                var curr = coordinates[i];
                var prev = coordinates[(i - 1 + coordinates.Count) % coordinates.Count];
                var next = coordinates[(i + 1) % coordinates.Count];

                // 如果当前点与前后两点共线，则跳过当前点
                if (prev.IsCollinear(curr, next, 0.001) || next.IsCollinear(prev, curr, 0.001) || curr.IsCollinear(next, prev, 0.001))
                {
                    continue;
                }
                // 否则，将当前点加入结果列表
                mergedCoordinates.Add(curr);
            }

            // 移除掉近乎重合的点
            for (int i = 0; i < mergedCoordinates.Count; i++)
            {
                var curr = mergedCoordinates[i];
                var next = mergedCoordinates[(i + 1) % mergedCoordinates.Count];
                if (curr.Distance(next) < 1e-10)
                {
                    mergedCoordinates.RemoveAt(i);
                    i--;
                }
            }

            if (mergedCoordinates.Count <= 3)
            {
                return polygon;
            }

            return mergedCoordinates.GeneratePolygon();
        }

        /// <summary>
        /// 合并两个共线且端点相连的线段，返回合并后的线段，否则返回 null。
        /// </summary>
        /// <param name="segment1">第一个线段</param>
        /// <param name="segment2">第二个线段</param>
        /// <returns>合并后的线段或 null。</returns>
        public static LineSegment MergeCollinearSegments(LineSegment segment1, LineSegment segment2)
        {
            // 判断两个线段是否共线且端点相连
            if (!IsCollinearAndConnected(segment1, segment2))
            {
                return null; // 如果不共线或端点不连接，则返回 null
            }

            // 获取两个线段的端点坐标
            Coordinate p1Start = segment1.P0;
            Coordinate p1End = segment1.P1;
            Coordinate p2Start = segment2.P0;
            Coordinate p2End = segment2.P1;

            // 计算合并后的线段的起点和终点，取最小值和最大值
            double minX = Math.Min(Math.Min(p1Start.X, p1End.X), Math.Min(p2Start.X, p2End.X));
            double maxX = Math.Max(Math.Max(p1Start.X, p1End.X), Math.Max(p2Start.X, p2End.X));
            double minY = Math.Min(Math.Min(p1Start.Y, p1End.Y), Math.Min(p2Start.Y, p2End.Y));
            double maxY = Math.Max(Math.Max(p1Start.Y, p1End.Y), Math.Max(p2Start.Y, p2End.Y));

            // 返回一个新的合并后的线段
            Coordinate mergedStart = new Coordinate(minX, minY);
            Coordinate mergedEnd = new Coordinate(maxX, maxY);

            return new LineSegment(mergedStart, mergedEnd); // 返回合并后的线段
        }
        #endregion

        /// <summary>
        /// 处理多边形顶点，在内凹角执行填充，在外凸角执行扣减操作（由 isConcave 控制）。
        /// </summary>
        /// <param name="polygon">要处理的多边形</param>
        /// <param name="isFill">true: 内凹角填充 (currentAngle≈-90); false: 外凸角扣减 (currentAngle≈90)</param>
        /// <param name="minSize">最小边长(ft)</param>
        /// <param name="angleTolerance">角度容差(度)</param>
        /// <param name="maxIterations">最大迭代次数</param>
        /// <returns>命名元组: OutputPolygon 操作后的多边形, OperatedAreas 所有处理过的小区域</returns>
        public static (Polygon OutputPolygon, List<Polygon> OperatedAreas) ProcessPolygon(
            this Polygon polygon,
            bool isFill = true,
            double minSize = 500 / 304.8,
            double angleTolerance = 0.1,
            int maxIterations = 100)
        {
            if (polygon == null)
                return (null, null);

            Polygon currentPolygon = polygon;
            var operatedAreas = new List<Polygon>();
            int iterations = 0;
            bool continueProcessing = true;

            try
            {
                while (continueProcessing && iterations < maxIterations)
                {
                    iterations++;
                    continueProcessing = false;

                    var lines = currentPolygon.GetPolygonLines();
                    var angles = currentPolygon.GetPolygonAngles();

                    for (int i = 0; i < lines.Count; i++)
                    {
                        var currentAngle = angles[i];
                        var currentLine = lines[i];
                        var previousLine = lines.Previous(currentLine);

                        bool isTargetCorner = isFill
                            ? Math.Abs(currentAngle + 90) < angleTolerance // 内凹角
                            : Math.Abs(currentAngle - 90) < angleTolerance; // 外凸角

                        if (isTargetCorner)
                        {
                            if (currentLine.Length < minSize && previousLine.Length < minSize)
                            {
                                var newLine = previousLine.Translate(currentLine.Direction(), currentLine.Length);
                                var operatedArea = new Coordinate[]
                                {
                                    previousLine.P0,
                                    previousLine.P1,
                                    newLine.P1,
                                    newLine.P0
                                }.GeneratePolygon();

                                if (operatedArea != null && operatedArea.IsValid)
                                {
                                    operatedAreas.Add(operatedArea);

                                    if (isFill)
                                    {
                                        // 填充（Union）
                                        var union = currentPolygon.Union(operatedArea);
                                        if (union is Polygon resultPolygon && resultPolygon.IsValid)
                                        {
                                            currentPolygon = resultPolygon.NormalizePolygon();
                                            continueProcessing = true;
                                            break;
                                        }
                                    }
                                    else
                                    {
                                        // 扣减（Difference）
                                        var diff = currentPolygon.Difference(operatedArea);
                                        Polygon resultPoly = null;
                                        if (diff is Polygon singlePolygon)
                                        {
                                            resultPoly = singlePolygon.NormalizePolygon();
                                        }
                                        else if (diff is MultiPolygon multiPolygon && multiPolygon.NumGeometries > 0)
                                        {
                                            resultPoly = multiPolygon.GetGeometryN(0) as Polygon;
                                        }
                                        if (resultPoly != null && resultPoly.IsValid)
                                        {
                                            currentPolygon = resultPoly;
                                            continueProcessing = true;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                // 达到最大迭代亦返回结果，不抛异常
            }
            catch (Exception ex)
            {
                // 返回原多边形，不产生操作区（健壮性重于抛出）
                return (polygon, new List<Polygon>());
            }

            return (currentPolygon, operatedAreas);
        }

        /// <summary>
        /// 在多边形内部查找最大内接矩形（偏移边法）
        /// </summary>
        public static Polygon FindLargestInscribedRectangle(this Polygon polygon, double distanceTolerance = 0.001)
        {
            // 确保多边形是顺时针方向
            polygon = polygon.ClockwisePolygon();

            // 获取多边形的所有边和顶点
            List<LineSegment> edges = polygon.GetPolygonLines();
            List<Coordinate> vertices = polygon.GetPolygonCoords();

            // 存储所有候选矩形
            List<Polygon> candidates = new List<Polygon>();

            // 对每条边
            for (int edgeIndex = 0; edgeIndex < edges.Count; edgeIndex++)
            {
                LineSegment baseEdge = edges[edgeIndex];

                // 跳过太短的边
                if (baseEdge.Length < distanceTolerance)
                {
                    continue;
                }

                // 计算边的方向向量
                Vector2D edgeVector = baseEdge.Direction().Normalize();

                // 计算法向量 (垂直于边的向量)
                Vector2D normalVector = edgeVector.GetPerpVector();

                // 查找所有非基准边端点的顶点
                Coordinate edgeP0 = baseEdge.P0;
                Coordinate edgeP1 = baseEdge.P1;

                for (int vertexIndex = 0; vertexIndex < vertices.Count; vertexIndex++)
                {
                    Coordinate vertex = vertices[vertexIndex];

                    // 如果这个顶点是基准边的端点则跳过
                    if (vertex.IsEquals(edgeP0, 0.001) ||
                        vertex.IsEquals(edgeP1, 0.001))
                    {
                        continue;
                    }

                    // 计算顶点到边的距离
                    double distance = baseEdge.Distance(vertex);

                    // 如果距离太小则跳过
                    if (distance < distanceTolerance)
                    {
                        continue;
                    }

                    // 使用辅助方法基于基准边和法向量生成矩形
                    Polygon rect = baseEdge.GeneratePolygon(normalVector, distance);

                    // 确认矩形完全在原多边形内部
                    if (rect != null)
                    {
                        if (rect.SetOffset(distanceTolerance / 304.8).Within(polygon))
                        {
                            candidates.Add(rect);
                        }
                        else
                        {
                        }
                    }
                    else if (rect != null)
                    {
                    }
                }
            }

            // 返回面积最大的矩形
            if (candidates.Count > 0)
            {
                Polygon largest = candidates.OrderByDescending(r => r.Area).First();
                return largest;
            }

            return null;
        }

        #endregion

        #region ## 获取图形信息方法
        /// <summary>
        /// 返回线段的方向
        /// </summary>
        /// <param name="segment">要计算线段</param>
        /// <returns>返回的向量</returns>
        public static Vector2D Direction(this LineSegment segment)
        {
            return new Vector2D(segment.P0, segment.P1);
        }

        /// <summary>
        /// 获取多边形的内角集合
        /// </summary>
        /// <param name="polygon">要获取内角的多边形</param>
        /// <returns>内角集合</returns>
        public static List<double> GetPolygonAngles(this Polygon polygon)
        {
            //弧度/π=角度/180
            List<double> angles = new List<double>();

            //生成多边形的内角
            for (int i = 0; i <= polygon.Coordinates.Length - 2; i++)
            {
                int m = i - 1;
                int n = i + 1;
                if (i == 0)
                {
                    m = polygon.Coordinates.Length - 2;
                }
                double radian = AngleUtility.AngleBetweenOriented(polygon.Coordinates[m], polygon.Coordinates[i], polygon.Coordinates[n]);
                int angle = Convert.ToInt32(radian * 180 / Math.PI);
                angles.Add(angle);
            }
            return angles;
        }

        /// <summary>
        /// 获取多边形的外边线集合
        /// </summary>
        /// <param name="polygon">要获取边线的多边形</param>
        /// <returns>外边线集合</returns>
        public static List<LineSegment> GetPolygonLines(this Polygon polygon)
        {
            if (polygon == null || polygon.ExteriorRing == null)
            {
                return new List<LineSegment>(); // 返回空集合
            }

            // 获取外环的坐标
            List<LineSegment> lineSegments = polygon.ExteriorRing.ToLineSegments();

            return lineSegments;
        }

        /// <summary>
        /// 获取多边形的坐标集合
        /// </summary>
        /// <param name="polygon">要获取坐标的多边形</param>
        /// <returns>坐标集合</returns>
        public static List<Coordinate> GetPolygonCoords(this Polygon polygon)
        {
            List<Coordinate> coordinates = new List<Coordinate>();
            for (int i = 0; i < polygon.Coordinates.Length - 1; i++)
            {
                coordinates.Add(polygon.Coordinates[i]);
            }
            return coordinates;
        }

        /// <summary>
        /// 提取多边形的两个主要方向向量
        /// </summary>
        /// <param name="polygon">输入多边形</param>
        /// <returns>包含主方向和次方向的元组</returns>
        /// <exception cref="ArgumentException">当多边形顶点不足以形成四边形时抛出</exception>
        /// <exception cref="InvalidOperationException">当无法提取有效方向时抛出</exception>
        public static (Vector2D PrimaryDirection, Vector2D SecondaryDirection) GetMainDirections(this Polygon polygon)
        {
            // 1. 参数验证
            if (polygon == null)
                return (null, null); // 如果多边形为空，返回空元组

            // 获取多边形的坐标序列
            Coordinate[] coordinates = polygon.ExteriorRing.Coordinates;

            if (coordinates.Length < 5) // 确保至少有4条边（首尾点相同）
            {
                return (null, null);
            }

            // 2. 提取所有边的方向向量和长度
            List<Vector2D> edgeDirections = new List<Vector2D>();  // 存储每条边的单位向量方向
            List<double> edgeLengths = new List<double>();         // 存储每条边的长度

            for (int i = 0; i < coordinates.Length - 1; i++)
            {
                Coordinate p1 = coordinates[i];
                Coordinate p2 = coordinates[i + 1];

                // 计算从p1到p2的向量
                Vector2D direction = new Vector2D(p2.X - p1.X, p2.Y - p1.Y);
                double length = direction.Length();

                // 忽略太短的边（可能是由于顶点重合或数值精度问题）
                if (length > 1e-10)
                {
                    edgeDirections.Add(direction.Normalize());  // 存储归一化后的方向向量
                    edgeLengths.Add(length);                    // 存储边长
                }
            }

            // 3. 将相似方向的边分组
            List<List<int>> directionGroups = new List<List<int>>();  // 存储方向组，每组包含边的索引

            for (int i = 0; i < edgeDirections.Count; i++)
            {
                bool foundGroup = false;
                Vector2D currentDirection = edgeDirections[i];

                // 尝试将当前边加入已有的方向组
                for (int j = 0; j < directionGroups.Count; j++)
                {
                    var group = directionGroups[j];
                    // 计算当前组的加权平均方向
                    Vector2D groupAvgDir = CalculateWeightedAverageDirection(group, edgeDirections, edgeLengths);

                    // 检查当前方向是否与组的平均方向相似（包括正反方向）
                    // 使用点积的绝对值，接近1表示方向相似或相反
                    double dotProduct = Math.Abs(groupAvgDir.Dot(currentDirection));
                    if (dotProduct > 0.8) // 约30度内的偏差视为相似方向
                    {
                        group.Add(i);  // 将当前边的索引添加到该方向组
                        foundGroup = true;
                        break;
                    }
                }

                // 如果当前边与所有已有组都不相似，创建新组
                if (!foundGroup)
                {
                    List<int> newGroup = new List<int> { i };
                    directionGroups.Add(newGroup);
                }
            }

            // 4. 计算每个方向组的总权重（边长和）和平均方向
            List<double> groupWeights = new List<double>();
            List<Vector2D> groupDirections = new List<Vector2D>();

            foreach (var group in directionGroups)
            {
                // 计算该组所有边的长度总和
                double totalLength = 0;
                foreach (int edgeIndex in group)
                {
                    totalLength += edgeLengths[edgeIndex];
                }
                groupWeights.Add(totalLength);

                // 计算该组的加权平均方向
                groupDirections.Add(CalculateWeightedAverageDirection(group, edgeDirections, edgeLengths));
            }

            // 5. 按照权重排序，提取主要方向
            // 使用索引排序以保持权重和方向的对应关系
            int[] sortedIndices = Enumerable.Range(0, groupDirections.Count)
                .OrderByDescending(i => groupWeights[i])
                .ToArray();

            // 6. 构造结果
            if (groupDirections.Count >= 2)
            {
                // 有至少两个方向组，取权重最大的两组作为主方向和次方向
                int primaryIndex = sortedIndices[0];
                int secondaryIndex = sortedIndices[1];

                return (
                    PrimaryDirection: groupDirections[primaryIndex],      // 主方向
                    SecondaryDirection: groupDirections[secondaryIndex]   // 次方向
                );
            }
            else if (groupDirections.Count == 1)
            {
                // 只有一个方向组，手动创建与主方向垂直的次方向
                Vector2D primaryDirection = groupDirections[0];
                // 逆时针旋转90度创建垂直向量
                Vector2D perpendicularDirection = new Vector2D(-primaryDirection.Y, primaryDirection.X).Normalize();

                return (
                    PrimaryDirection: primaryDirection,         // 主方向 
                    SecondaryDirection: perpendicularDirection  // 人工构造的垂直次方向
                );
            }
            else
            {
                // 无法提取有效的方向（极少发生）
                return (null, null);
            }
        }

        /// <summary>
        /// 根据边长计算一组边的加权平均方向
        /// </summary>
        /// <param name="edgeIndices">边的索引列表</param>
        /// <param name="directions">所有边的方向向量</param>
        /// <param name="lengths">所有边的长度</param>
        /// <returns>归一化的加权平均方向向量</returns>
        public static Vector2D CalculateWeightedAverageDirection(List<int> edgeIndices, List<Vector2D> directions, List<double> lengths)
        {
            if (edgeIndices.Count == 0)
                return null;

            double sumX = 0, sumY = 0;
            Vector2D referenceDir = directions[edgeIndices[0]];  // 使用组内第一个方向作为参考

            foreach (int i in edgeIndices)
            {
                Vector2D currentDir = directions[i];

                // 计算当前方向与参考方向的点积，判断是否需要反转
                double dot = referenceDir.Dot(currentDir);

                // 如果方向相反（点积为负），反转该向量再计算贡献
                // 这确保同一组内的向量指向一致，避免相互抵消
                double factor = (dot >= 0) ? lengths[i] : -lengths[i];

                // 累加向量分量（带权重）
                sumX += currentDir.X * factor;
                sumY += currentDir.Y * factor;
            }

            // 创建合成向量并归一化
            Vector2D avgDir = new Vector2D(sumX, sumY);
            double magnitude = avgDir.Length();

            // 防止零向量
            if (magnitude < 1e-10)
                return referenceDir;  // 回退至参考方向

            return avgDir.Normalize();  // 返回归一化后的方向向量
        }

        #endregion

        #region ## 图形平移旋转方法
        /// <summary>
        /// 坐标平移
        /// </summary>
        /// <param name="coordinate">要平移的坐标</param>
        /// <param name="vec">要平移的方向</param>
        /// <param name="distance">要平移的距离</param>
        /// <returns>平移后的坐标</returns>
        public static Coordinate Translate(this Coordinate coordinate, Vector2D vec, double distance)
        {
            Vector2D vecCoordinate = new Vector2D(coordinate);
            Vector2D vecNewCoordinate = vecCoordinate + (vec.Normalize()) * distance;
            Coordinate newCoordinate = new Coordinate(vecNewCoordinate.X, vecNewCoordinate.Y);
            return newCoordinate;
        }

        /// <summary>
        /// 线段平移
        /// </summary>
        /// <param name="line">要平移的线段</param>
        /// <param name="vec">要平移的方向</param>
        /// <param name="distance">要平移的距离</param>
        /// <returns>平移后的线段</returns>
        public static LineSegment Translate(this LineSegment line, Vector2D vec, double distance)
        {
            Coordinate newP0 = Translate(line.P0, vec, distance);
            Coordinate newP1 = Translate(line.P1, vec, distance);
            LineSegment newLine = new LineSegment(newP0, newP1);
            return newLine;
        }

        /// <summary>
        /// 多边形平移
        /// </summary>
        /// <param name="polygon">要平移的多边形</param>
        /// <param name="vec">要平移的方向</param>
        /// <param name="distance">要平移的距离</param>
        /// <returns></returns>
        public static Polygon Translate(this Polygon polygon, Vector2D vec, double distance)
        {
            List<Coordinate> newCoordinates = new List<Coordinate>();
            foreach (var coordinate in polygon.Coordinates)
            {
                newCoordinates.Add(Translate(coordinate, vec, distance));
            }
            LinearRing linearRing = new LinearRing(newCoordinates.ToArray());
            Polygon newPolygon = new Polygon(linearRing);
            return newPolygon;
        }

        /// <summary>
        /// 将局部坐标转换为全局坐标，先旋转后平移（角度制）
        /// </summary>
        /// <param name="localPoint">局部坐标系中的点</param>
        /// <param name="translation">平移向量</param>
        /// <param name="rotationRadian">旋转角度（弧度制）</param>
        /// <returns>转换后的全局坐标点</returns>
        public static Coordinate ConvertRelativeToAbsolute(this Coordinate localPoint, Coordinate translation, double rotationRadian)
        {
            // 1. 将 Coordinate 转换为 Point（Geometry）
            NetTopologySuite.Geometries.Point point = new NetTopologySuite.Geometries.Point(localPoint);

            // 2. 创建旋转矩阵，绕指定轴旋转（假设是Z轴，角度为弧度制）
            AffineTransformation rotateTransform = AffineTransformation.RotationInstance(rotationRadian, 0, 0);

            // 3. 将点按照旋转矩阵进行旋转
            NetTopologySuite.Geometries.Point rotatedPoint = (NetTopologySuite.Geometries.Point)rotateTransform.Transform(point);

            // 4. 创建平移矩阵
            AffineTransformation translateTransform = AffineTransformation.TranslationInstance(translation.X, translation.Y);

            // 5. 将旋转后的点按照平移矩阵进行平移
            NetTopologySuite.Geometries.Point transformedPoint = (NetTopologySuite.Geometries.Point)translateTransform.Transform(rotatedPoint);

            // 6. 返回转换后的坐标点
            return transformedPoint.Coordinate;
        }

        /// <summary>
        /// 将坐标以坐标原点为旋转中心旋转指定弧度
        /// </summary>
        /// <param name="coord"></param>
        /// <param name="rotationRadian"></param>
        /// <returns></returns>
        public static Coordinate Rotate(this Coordinate coord, double rotationRadian)
        {
            // 1. 将 Coordinate 转换为 Point（Geometry）
            NetTopologySuite.Geometries.Point point = new NetTopologySuite.Geometries.Point(coord);

            // 2. 创建旋转矩阵，绕指定轴旋转（假设是Z轴，角度为弧度制）
            AffineTransformation rotateTransform = AffineTransformation.RotationInstance(rotationRadian, 0, 0);

            // 3. 将点按照旋转矩阵进行旋转
            NetTopologySuite.Geometries.Point rotatedPoint = (NetTopologySuite.Geometries.Point)rotateTransform.Transform(point);
            // 6. 返回转换后的坐标点
            return rotatedPoint.Coordinate;
        }

        #endregion

        #region ## 图像偏移方法
        /// <summary>
        /// 获取偏移后的闭合多边形
        /// </summary>
        /// <param name="polygon">原始闭合多边形</param>
        /// <param name="distance">偏移距离</param>
        /// <param name="isIntro">是否向内偏移</param>
        /// <returns>偏移后的闭合多边形</returns>
        public static Polygon SetOffset(this Polygon polygon, double distance, bool isIntro = true, double epsilon = 1e-6)
        {
            if (polygon == null || polygon.Coordinates.Length < 4) // 多边形至少需要4个点（包括重复的起点）
            {
                //Trace.WriteLine("输入的多边形为空或点数不足以构成闭合多边形。");
                return polygon;
            }

            // 逆时针
            if (polygon.Shell.IsCCW)
            {
            }
            // 顺时针
            else
            {
                isIntro = isIntro ? false : true;
            }

            // 计算偏移距离（考虑偏移方向）
            double offset = Math.Abs(distance) * (isIntro ? 1 : -1);

            // 获取原始多边形的边线
            var segments = polygon.GetPolygonLines();

            // 存储所有偏移后的边线
            List<LineSegment> offsetSegments = new List<LineSegment>();

            foreach (var segment in segments)
            {
                double dx = segment.P1.X - segment.P0.X;
                double dy = segment.P1.Y - segment.P0.Y;
                double length = Math.Sqrt(dx * dx + dy * dy);

                if (length == 0)
                {
                    //Trace.WriteLine("跳过长度为零的边线。");
                    continue; // 跳过零长度的边线
                }

                // 计算单位法向量
                double normalX = -dy / length;
                double normalY = dx / length;

                // 创建偏移后的起点和终点
                var offsetP0 = new Coordinate(segment.P0.X + normalX * offset, segment.P0.Y + normalY * offset);
                var offsetP1 = new Coordinate(segment.P1.X + normalX * offset, segment.P1.Y + normalY * offset);

                // 添加偏移后的边线
                offsetSegments.Add(new LineSegment(offsetP0, offsetP1));
            }

            // 存储偏移后的多边形顶点
            List<Coordinate> resultCoordinates = new List<Coordinate>();

            for (int i = 0; i < offsetSegments.Count; i++)
            {
                var currentOffsetLine = offsetSegments[i];
                var nextOffsetLine = offsetSegments[(i + 1) % offsetSegments.Count];
                var currentLine = segments[i];
                var nextLine = segments[(i + 1) % segments.Count];

                // 获取当前边线和下一边线的单位方向向量
                (double currDirX, double currDirY) = GetNormalizedDirection(currentLine);
                (double nextDirX, double nextDirY) = GetNormalizedDirection(nextLine);

                // 计算叉积以判断角点类型
                double cross = currDirX * nextDirY - currDirY * nextDirX;
                bool isConcave = (cross < -epsilon && isIntro) || (cross > epsilon && !isIntro);

                Coordinate intersection;

                if (isConcave)
                {
                    // 对于内凹角，延长偏移后的边线
                    var extendedCurrent = new LineSegment(
                        currentOffsetLine.P1,
                        new Coordinate(currentOffsetLine.P1.X + (currentLine.P1.X - currentLine.P0.X),
                                       currentOffsetLine.P1.Y + (currentLine.P1.Y - currentLine.P0.Y)));

                    var extendedNext = new LineSegment(
                        nextOffsetLine.P0,
                        new Coordinate(nextOffsetLine.P0.X - (nextLine.P1.X - nextLine.P0.X),
                                       nextOffsetLine.P0.Y - (nextLine.P1.Y - nextLine.P0.Y)));

                    // 计算延长线的交点
                    intersection = GetIntersectionPoint(extendedCurrent, extendedNext);
                }
                else
                {
                    // 对于凸角，直接计算偏移后的边线交点
                    intersection = GetIntersectionPoint(currentOffsetLine, nextOffsetLine);
                }

                if (intersection != null)
                {
                    // 检查是否需要添加新点（避免重复或近似重复点）
                    if (resultCoordinates.Count > 0)
                    {
                        var lastAdded = resultCoordinates[resultCoordinates.Count - 1];
                        bool areApproximatelyEqual = Math.Abs(lastAdded.X - intersection.X) < epsilon &&
                                                     Math.Abs(lastAdded.Y - intersection.Y) < epsilon &&
                                                     Math.Abs(lastAdded.Z - intersection.Z) < epsilon;

                        if (!areApproximatelyEqual)
                        {
                            resultCoordinates.Add(intersection);
                            //Trace.WriteLine($"添加交点: ({intersection.X}, {intersection.Y}, {intersection.Z})");
                        }
                        else
                        {
                            //Trace.WriteLine("跳过添加近似重复的交点。");
                        }
                    }
                    else
                    {
                        // 添加第一个交点
                        resultCoordinates.Add(intersection);
                        //Trace.WriteLine($"添加第一个交点: ({intersection.X}, {intersection.Y}, {intersection.Z})");
                    }
                }
                else
                {
                    //Trace.WriteLine("无法找到交点，可能的几何问题。");
                }
            }

            // 确保多边形闭合
            if (resultCoordinates.Count > 0 && !(Math.Abs(resultCoordinates[0].X - resultCoordinates[resultCoordinates.Count - 1].X) < epsilon && Math.Abs(resultCoordinates[0].Y - resultCoordinates[resultCoordinates.Count - 1].Y) < epsilon))
            {
                resultCoordinates.Add(resultCoordinates[0]);
                //Trace.WriteLine("闭合多边形，添加起点作为终点。");
            }

            // 检查结果坐标点数
            if (resultCoordinates.Count < 4)
            {
                //Trace.WriteLine("偏移后的多边形点数不足以构成闭合多边形。");
                return polygon;
            }

            // 创建偏移后的多边形
            var newPolygon = new Polygon(new LinearRing(resultCoordinates.ToArray()));
            //Trace.WriteLine("成功创建偏移后的多边形。");
            return newPolygon;
        }

        /// <summary>
        /// 获取偏移后的闭合多边形
        /// </summary>
        /// <param name="segement">原始闭合多边形</param>
        /// <param name="offset">偏移距离</param>
        /// <param name="isIntro">是否向内偏移</param>
        /// <returns>偏移后的闭合多边形</returns>
        public static LineSegment SetOffset(this LineSegment segement, double offset, bool isIntro = false)
        {
            if (segement == null)
            {
                return segement;
            }
            if (isIntro)
            {
                segement = new LineSegment(segement.P0.Translate(segement.Direction(), offset), segement.P1.Translate(-segement.Direction(), offset));
            }
            else
            {
                segement = new LineSegment(segement.P0.Translate(-segement.Direction(), offset), segement.P1.Translate(segement.Direction(), offset));
            }
            return segement;
        }

        /// <summary>
        /// 获取线段的单位方向向量
        /// </summary>
        public static (double x, double y) GetNormalizedDirection(LineSegment line)
        {
            double dx = line.P1.X - line.P0.X;
            double dy = line.P1.Y - line.P0.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            return (dx / length, dy / length);
        }

        /// <summary>
        /// 计算两条线段的交点
        /// </summary>
        public static Coordinate GetIntersectionPoint(this LineSegment line1, LineSegment line2)
        {
            // 获取直线1的参数
            double A1 = line1.P0.Y - line1.P1.Y;
            double B1 = line1.P1.X - line1.P0.X;
            double C1 = A1 * line1.P1.X + B1 * line1.P1.Y;

            // 获取直线2的参数
            double A2 = line2.P0.Y - line2.P1.Y;
            double B2 = line2.P1.X - line2.P0.X;
            double C2 = A2 * line2.P1.X + B2 * line2.P1.Y;

            // 计算两条直线的行列式 D
            double D = A1 * B2 - A2 * B1;

            // 如果 D == 0，说明两直线平行或者重合，返回 null
            if (Math.Abs(D) < 1e-6)
            {
                return null; // 无交点
            }

            // 计算交点的 x 和 y 坐标
            double Dx = C1 * B2 - C2 * B1;
            double Dy = A1 * C2 - A2 * C1;

            double x = Dx / D;
            double y = Dy / D;

            return new Coordinate(x, y);
        }

        #endregion

        #region ## 布尔运算方法
        #region ### 判断
        /// <summary>
        /// 检查两点是否非常接近
        /// </summary>
        public static bool IsEquals(this Coordinate p1, Coordinate p2, double tolerance = 0.001)
        {
            double dx = p1.X - p2.X;
            double dy = p1.Y - p2.Y;
            return (dx * dx + dy * dy) <= tolerance * tolerance;
        }

        /// <summary>
        /// 判断两个Vector2D是否大致相同，考虑指定的误差容差
        /// </summary>
        /// <param name="vector1">第一个二维向量</param>
        /// <param name="vector2">第二个二维向量</param>
        /// <param name="tolerance">容差值，默认为0.001</param>
        /// <returns>如果两个向量在容差范围内相同则返回true，否则返回false</returns>
        public static bool IsEquals(this Vector2D vector1, Vector2D vector2, double tolerance = 0.001)
        {
            try
            {
                // 检查输入参数是否为空
                if (vector1 == null || vector2 == null)
                {
                    return false;
                }

                // 检查容差值是否有效
                if (tolerance < 0)
                {
                    return false; // 容差不能为负数
                }

                // 计算两个向量各分量的差值
                double deltaX = System.Math.Abs(vector1.X - vector2.X);
                double deltaY = System.Math.Abs(vector1.Y - vector2.Y);

                // 方法1：使用欧几里得距离判断
                double distance = System.Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
                bool isEqualByDistance = distance <= tolerance;

                // 方法2：分别比较X和Y分量（可选的更严格判断）
                bool isEqualByComponents = deltaX <= tolerance && deltaY <= tolerance;

                // 使用欧几里得距离方法
                return isEqualByDistance;
            }
            catch (System.Exception ex)
            {
                return false; // 容差不能为负数
            }
        }

        /// <summary>
        /// 判断两个线段是否平行
        /// </summary>
        /// <param name="line1">第一个线段</param>
        /// <param name="line2">第二个线段</param>
        /// <param name="tolerance">容许的误差范围</param>
        /// <returns>如果两个线段平行则返回true，否则返回false</returns>
        public static bool IsParallel(this LineSegment line1, LineSegment line2, double tolerance = 0.01)
        {
            // 获取第一个线段的方向向量
            var vector1 = line1.Direction().Normalize();

            // 获取第二个线段的方向向量
            var vector2 = line2.Direction().Normalize();

            // 计算两个向量的叉积
            double crossProduct = vector1.X * vector2.Y - vector1.Y * vector2.X;

            // 如果叉积接近零，则认为两个向量平行
            return Math.Abs(crossProduct) < tolerance;
        }

        /// <summary>
        /// 判断三点是否共线，使用叉积判断法。
        /// </summary>
        /// <param name="p1">第一个点</param>
        /// <param name="p2">第二个点</param>
        /// <param name="p3">第三个点</param>
        /// <returns>如果三点共线返回 true，否则返回 false。</returns>
        public static bool IsCollinear(this Coordinate p1, Coordinate p2, Coordinate p3, double tolerance = 1e-6)
        {
            Vector2D vector12 = new Vector2D(p1, p2).Normalize();
            Vector2D vector23 = new Vector2D(p2, p3).Normalize();

            // 计算两个向量的差
            double deltaX = vector12.X - vector23.X;
            double deltaY = vector12.Y - vector23.Y;

            // 计算差的长度（模）
            double distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);

            // 如果差的长度小于容差，则认为两个向量近似相等
            return distance < tolerance;
        }

        /// <summary>
        /// 判断两个Vector2D向量是否共线
        /// </summary>
        /// <param name="vector1">第一个向量</param>
        /// <param name="vector2">第二个向量</param>
        /// <param name="tolerance">误差容忍度，默认为1e-9</param>
        /// <returns>若两向量共线则返回true，否则返回false</returns>
        public static bool IsCollinear(this Vector2D vector1, Vector2D vector2, double tolerance = 0.01)
        {
            // 处理零向量的情况
            double v1Length = Math.Sqrt(vector1.X * vector1.X + vector1.Y * vector1.Y);
            double v2Length = Math.Sqrt(vector2.X * vector2.X + vector2.Y * vector2.Y);

            if (v1Length < tolerance || v2Length < tolerance)
            {
                // 任一向量为零向量时，视为共线
                return true;
            }

            // 计算叉积: v1.X * v2.Y - v1.Y * v2.X
            double crossProduct = vector1.X * vector2.Y - vector1.Y * vector2.X;

            // 归一化叉积，消除向量长度影响
            double normalizedCross = Math.Abs(crossProduct) / (v1Length * v2Length);

            // 如果归一化叉积接近零，则向量共线
            return normalizedCross < tolerance;
        }

        /// <summary>
        /// 判断直线是否共线，使用叉积判断法。
        /// </summary>
        /// <returns>如果三点共线返回 true，否则返回 false。</returns>
        public static bool IsCollinear(this LineSegment line1, LineSegment line2, double tolerance = 1e-6)
        {
            // 过滤掉不平行的情况（两直线共线必须平行）
            if (!IsParallel(line1, line2))
                return false;

            Coordinate p1 = line1.P0;
            Coordinate p2 = line1.P1;
            Coordinate p3 = line2.P1;
            if (line2.P1 == line1.P0 || line2.P1 == line1.P1)
            {
                p3 = line2.P1;
            }

            Vector2D vector12 = new Vector2D(p1, p2).Normalize();
            Vector2D vector23 = new Vector2D(p2, p3).Normalize();

            // 计算两个向量的差
            double deltaX = vector12.X - vector23.X;
            double deltaY = vector12.Y - vector23.Y;

            // 计算差的长度（模）
            double distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);

            // 如果差的长度小于容差，则认为两个向量近似相等
            return distance < tolerance;
        }

        /// <summary>
        /// 检查点是否在线段上
        /// </summary>
        /// <param name="point">待检查的点</param>
        /// <param name="lineReference">参考线段（容器线段）</param>
        /// <param name="tolerance">容差值(默认为1e-10)</param>
        /// <returns>如果点在线段上返回true，否则返回false</returns>
        public static bool IsInsideLineSegment(this Coordinate point, LineSegment lineReference, double tolerance = 1e-10)
        {
            return lineReference.Distance(point) < tolerance;
        }

        /// <summary>
        /// 判断一个线段是否完全在另一个线段内部(要求两线段共线)
        /// </summary>
        /// <param name="lineToCheck">需要检查是否在参考线段内部的线段</param>
        /// <param name="lineReference">参考线段（容器线段）</param>
        /// <param name="tolerance">误差值，用于共线判断和投影计算</param>
        /// <returns>如果lineToCheck完全在lineReference内部且共线，返回true；否则返回false</returns>
        public static bool IsInsideLineSegment(this LineSegment lineToCheck, LineSegment lineReference, double tolerance = 1e-6)
        {
            if (lineToCheck == null || lineReference == null)
                return false;

            // 首先检查两条线段是否共线
            if (!lineToCheck.IsCollinear(lineReference, tolerance))
            {
                return false;
            }

            // 获取待检查线段的两个端点
            Coordinate p0 = lineToCheck.P0;
            Coordinate p1 = lineToCheck.P1;

            // 计算两个端点在参考线段上的投影因子
            double factorP0 = lineReference.ProjectionFactor(p0);
            double factorP1 = lineReference.ProjectionFactor(p1);

            // 考虑误差，检查投影因子是否在[0,1]范围内
            // 这表示lineToCheck的端点都位于lineReference的范围内
            if (factorP0 >= -tolerance && factorP0 <= 1 + tolerance &&
                factorP1 >= -tolerance && factorP1 <= 1 + tolerance)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// 判断一个多边形是否完全在另一个多边形内部
        /// </summary>
        /// <param name="polygonToCheck">需要检查是否在参考多边形内部的多边形</param>
        /// <param name="polygonReference">参考多边形（容器多边形）</param>
        /// <param name="tolerance">误差值，当差集是多边形时，若其面积小于该误差值，视为 polygonToCheck 完全在 polygonReference 内部</param>
        /// <returns></returns>
        public static bool IsInsidePolygon(this Polygon polygonToCheck, Polygon polygonReference, double tolerance = 0.01)
        {
            try
            {
                // 计算 polygonReference 和 polygonToCheck 的差集
                NetTopologySuite.Geometries.Geometry difference = polygonToCheck.Difference(polygonReference);

                // 判断差集类型
                if (difference.IsEmpty || difference is NetTopologySuite.Geometries.Point || difference is LineString)
                {
                    // 如果差集为空、点或线段，说明 polygonToCheck 完全在 polygonReference 内部
                    return true;
                }
                // 如果差集是多边形，判断其面积是否小于误差值
                else if (difference is Polygon diffPolygon)
                {
                    // 获取差集多边形的面积
                    double differenceArea = diffPolygon.Area;

                    // 如果差集的面积小于误差值，视为 polygonToCheck 完全在 polygonReference 内部
                    if (differenceArea < tolerance)
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    // 如果差集是其他类型，返回 false
                    return false;
                }
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        /// <summary>
        /// 判断两个 LineSegment 是否共线
        /// </summary>
        /// <param name="segment1">第一个线段</param>
        /// <param name="segment2">第二个线段</param>
        /// <returns>如果共线且端点连接返回 true，否则返回 false。</returns>
        public static bool IsCollinearAndConnected(LineSegment segment1, LineSegment segment2)
        {
            Coordinate p1Start = segment1.P0;
            Coordinate p1End = segment1.P1;
            Coordinate p2Start = segment2.P0;
            Coordinate p2End = segment2.P1;

            bool endPointsConnected = false;

            // 1. 检查端点是否相连 (四种连接情况)
            if (p1End.Equals2D(p2Start) || p1Start.Equals2D(p2End) || p1End.Equals2D(p2End) || p1Start.Equals2D(p2Start))
            {
                endPointsConnected = true;
            }

            if (!endPointsConnected)
            {
                return false; // 如果没有端点连接，直接返回 false
            }

            // 2. 选取三个不重叠的点进行共线判断
            Coordinate p1 = p1Start;
            Coordinate p2 = p1End;
            Coordinate p3 = p2End; // 默认选择 p1Start, p1End, p2End

            // 确保选取的三个点中没有重叠的点。 优先选择 segment2 的终点 p2End
            if (p1.Equals2D(p2End) || p2.Equals2D(p2End))
            {
                p3 = p2Start; // 如果 p2End 与 p1Start 或 p1End 重叠，则选择 p2Start
            }

            // 再次检查，如果 p2Start 也与 p1Start 或 p1End 重叠 (理论上不应该发生，除非所有点都重叠，线段退化为点)
            if (p1.Equals2D(p3) || p2.Equals2D(p3))
            {
                return true; // 如果无法选出三个不重叠的点，且端点已连接，则认为共线 (例如，线段退化为点的情况)
            }

            // 3. 使用 ProjectUtils.IsCollinear 判断这三个点是否共线
            return p1.IsCollinear(p2, p3); // 使用 ProjectUtils.IsCollinear 进行共线判断
        }

        #endregion

        #region ### Difference
        /// <summary>
        /// 获取两条边不相交部分的边
        /// </summary>
        /// <param name="segment1">第一条边</param>
        /// <param name="segment2">第二条边</param>
        /// <returns>不相交部分的几何元素</returns>
        public static List<LineSegment> DifferenceLine(this LineSegment segment1, LineSegment segment2, double tolerance = 1e-5)
        {
            // 检查输入多边形是否为空
            if (segment1 == null || segment2 == null)
            {
                return new List<LineSegment> { segment1 };
            }
            if (!segment1.IsParallel(segment2))
                return new List<LineSegment> { segment1 };

            Polygon polygon = segment2.Translate(segment2.Direction().GetPerpVector(), 0.1).GeneratePolygon(-segment2.Direction().GetPerpVector(), 0.2);

            // 计算两个多边形的对称差集（不相交部分）
            NetTopologySuite.Geometries.Geometry diff = segment1.ToLineString().Difference(polygon);

            // 如果不相交部分为空，则返回null
            if (diff == null || diff.IsEmpty)
                return new List<LineSegment> { segment1 };

            else if (diff is LineString line)
            {
                if (line.Length < tolerance)
                    return new List<LineSegment> { segment1 };
                return new List<LineSegment> { line.ToLineSegment() };
            }
            else if (diff is MultiLineString mtLine)
            {
                var result = new List<LineSegment>();

                foreach (LineString item in mtLine)
                {
                    if (item.Length < tolerance)
                        continue;
                    result.Add(item.ToLineSegment());
                }
                if (!result.Any())
                {
                    return new List<LineSegment> { segment1 };
                }
                return result;
            }
            else
            {
                return new List<LineSegment> { segment1 };
            }
        }

        /// <summary>
        /// 获取一条线段去除与多条线段相交部分后剩余的线段列表
        /// </summary>
        /// <param name="segment1">主线段</param>
        /// <param name="segments2">待剔除的线段列表</param>
        /// <param name="tolerance">长度容差（小于该值视为忽略）</param>
        /// <returns>主线段去除与segments2重叠/相交部分后剩余的LineSegment列表</returns>
        public static List<LineSegment> DifferenceLines(this LineSegment segment1, IEnumerable<LineSegment> segments2, double tolerance = 1e-5)
        {
            #region Step1 参数有效性校验
            if (segment1 == null || segments2 == null || !segments2.Any())
            {
                return new List<LineSegment> { segment1 };
            }
            #endregion

            #region Step2 将 segment1/segments2 转换成 NTS 几何对象
            var lineString = segment1.ToLineString();

            var factory = lineString.Factory;

            // 将所有segments2转为LineString，再Union为一个几何对象（GeometryCollection/MultiLineString）
            var others = segments2
                .Where(ls => ls != null)
                .Select(ls => ls.ToLineString())
                .ToArray();

            if (others.Length == 0)
            {
                return new List<LineSegment> { segment1 };
            }

            NetTopologySuite.Geometries.Geometry otherGeo = factory.BuildGeometry(others).Union();
            #endregion

            #region Step3 求差集
            NetTopologySuite.Geometries.Geometry diff = lineString.Difference(otherGeo);
            if (diff == null || diff.IsEmpty)
            {
                return new List<LineSegment>();
            }
            #endregion

            #region Step4 转换几何差集为LineSegment列表
            var result = new List<LineSegment>();

            if (diff is LineString singleLine)
            {
                if (singleLine.Length >= tolerance)
                {
                    result.Add(singleLine.ToLineSegment());
                }
            }
            else if (diff is MultiLineString multiLine)
            {
                foreach (LineString item in multiLine.Geometries)
                {
                    if (item.Length >= tolerance)
                    {
                        result.Add(item.ToLineSegment());
                    }
                }
            }
            else if (diff is GeometryCollection gc)
            {
                for (int i = 0; i < gc.NumGeometries; i++)
                {
                    var geom = gc.GetGeometryN(i);
                    if (geom is LineString ls && ls.Length >= tolerance)
                    {
                        result.Add(ls.ToLineSegment());
                    }
                }
            }

            // 如果没有有效结果，直接返回空
            if (!result.Any())
                return new List<LineSegment>();

            return result;
            #endregion
        }

        /// <summary>
        /// 获取一条线段去除与多边形相交部分后剩余的线段列表
        /// </summary>
        /// <param name="segment1">主线段</param>
        /// <param name="polygons">待剔除的多边形列表</param>
        /// <param name="tolerance">长度容差（小于该值视为忽略）</param>
        /// <returns>主线段去除与polygons重叠/相交部分后剩余的LineSegment列表</returns>
        public static List<LineSegment> DifferenceLinesWithPolygons(this LineSegment segment1, IEnumerable<Polygon> polygons, double tolerance = 1e-5)
        {
            #region Step1 参数有效性校验
            if (segment1 == null || polygons == null || !polygons.Any())
            {
                return new List<LineSegment> { segment1 };
            }
            #endregion

            #region Step2 将 segment1 转换成 NTS 几何对象
            var lineString = segment1.ToLineString();
            var factory = lineString.Factory;

            // 合并 polygons 为一个几何对象（GeometryCollection/Union）
            NetTopologySuite.Geometries.Geometry combinedPolygons = factory.BuildGeometry(polygons).Union();
            #endregion

            #region Step3 求差集
            NetTopologySuite.Geometries.Geometry diff = lineString.Difference(combinedPolygons);

            if (diff == null || diff.IsEmpty)
            {
                return new List<LineSegment>();
            }
            #endregion

            #region Step4 转换几何差集为LineSegment列表
            var result = new List<LineSegment>();

            if (diff is LineString singleLine)
            {
                if (singleLine.Length >= tolerance)
                {
                    result.Add(singleLine.ToLineSegment());
                }
            }
            else if (diff is MultiLineString multiLine)
            {
                foreach (LineString item in multiLine.Geometries)
                {
                    if (item.Length >= tolerance)
                    {
                        result.Add(item.ToLineSegment());
                    }
                }
            }
            else if (diff is GeometryCollection gc)
            {
                for (int i = 0; i < gc.NumGeometries; i++)
                {
                    var geom = gc.GetGeometryN(i);
                    if (geom is LineString ls && ls.Length >= tolerance)
                    {
                        result.Add(ls.ToLineSegment());
                    }
                }
            }

            // 如果没有有效结果，直接返回空
            if (!result.Any())
                return new List<LineSegment>();

            return result;
            #endregion
        }

        /// <summary>
        /// 获取两个Polygon不相交部分的面
        /// </summary>
        /// <param name="plg">第一个多边形</param>
        /// <param name="geometry">第二个多边形</param>
        /// <returns>不相交部分的几何元素</returns>
        public static List<Polygon> DifferencePolygons(this Polygon plg, NetTopologySuite.Geometries.Geometry geometry, double tolerance = 0.01)
        {
            List<Polygon> diffPlgs = new List<Polygon>();
            // 检查输入多边形是否为空
            if (plg == null || geometry == null)
            {
                return null;
            }

            // 计算两个多边形的对称差集（不相交部分）
            NetTopologySuite.Geometries.Geometry nonIntersecting = plg.Difference(geometry);

            // 如果不相交部分为空，则返回null
            if (nonIntersecting == null || nonIntersecting.IsEmpty)
            {
                return null;
            }
            else if (nonIntersecting is Polygon polygon)
            {
                var diff = polygon;
                if (diff.Area >= tolerance)
                    diffPlgs.Add(diff);
                return diffPlgs;
            }
            else if (nonIntersecting is MultiPolygon mtPolygon)
            {
                foreach (Polygon diff in mtPolygon)
                {
                    if (diff.Area >= tolerance)
                        diffPlgs.Add(diff);
                }
                return diffPlgs;
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// 获取两个Polygon不相交部分的面
        /// </summary>
        /// <param name="plg">第一个多边形</param>
        /// <param name="segment">第二个多边形</param>
        /// <returns>不相交部分的几何元素</returns>
        public static List<Polygon> DifferencePolygons(this Polygon plg, LineSegment segment, double offset = 10000 / 304.8, double tolerance = 0.01)
        {
            List<Polygon> diffPlgs = new List<Polygon>();
            // 检查输入多边形是否为空
            if (plg == null || segment == null)
            {
                return null;
            }

            var segmentPlg = segment.SetOffset(offset).GeneratePolygon(segment.Direction().GetPerpVector(), offset);
            var intersectPlgs = segmentPlg.IntersectPolygons(plg);
            if (intersectPlgs == null || !intersectPlgs.Any() || intersectPlgs.Count > 1)
            {
                segmentPlg = segment.SetOffset(offset).GeneratePolygon(-segment.Direction().GetPerpVector(), offset);
                intersectPlgs = segmentPlg.IntersectPolygons(plg);
            }
            if (intersectPlgs.FirstOrDefault() != null && intersectPlgs.FirstOrDefault().Area >= tolerance)
            {
                diffPlgs.Add(intersectPlgs.FirstOrDefault());
            }
            var diffPlg = plg.DifferencePolygon(intersectPlgs.FirstOrDefault());
            if (diffPlg != null && diffPlg.Area >= tolerance)
            {
                diffPlgs.Add(diffPlg);
            }
            return diffPlgs;
        }

        /// <summary>
        /// 计算两个多边形的差集
        /// </summary>
        /// <param name="polygon1"></param>
        /// <param name="polygon2"></param>
        /// <returns></returns>
        public static List<Polygon> DifferenceRegions(this Polygon polygon1, Polygon polygon2)
        {
            List<Polygon> result = new List<Polygon>();

            try
            {
                // 使用Difference方法获取polygon1中与polygon2不相交的部分
                NetTopologySuite.Geometries.Geometry diff = polygon1.Difference(polygon2);

                // 如果difference是一个多边形，加入到结果中
                if (diff is Polygon diffPolygon)
                {
                    result.Add(diffPolygon);
                }
                // 如果difference是多多边形对象，可以将其拆分成多个多边形
                else if (diff is MultiPolygon multiDiff)
                {
                    foreach (Polygon p in multiDiff)
                    {
                        result.Add(p);
                    }
                }
            }
            catch (System.Exception ex)
            {
            }

            return result; // 如果没有交集，返回空的List
        }

        /// <summary>
        /// 计算一个多边形与多个多边形的差集
        /// </summary>
        /// <param name="polygon">主多边形</param>
        /// <param name="polygons">要排除的多边形列表</param>
        /// <returns>与所有给定多边形不相交的区域列表</returns>
        public static List<Polygon> DifferenceRegions(this Polygon polygon, List<Polygon> polygons)
        {
            // 基本情况：如果没有更多的多边形需要处理，返回polygon1本身
            if (polygons == null || polygons.Count == 0)
            {
                return new List<Polygon> { polygon };
            }

            // 处理第一个多边形
            Polygon firstPolygon = polygons[0];
            List<Polygon> intermediateResult = DifferenceRegions(polygon, firstPolygon);

            // 如果没有差集，直接返回空列表
            if (intermediateResult.Count == 0)
            {
                return new List<Polygon>();
            }

            // 递归处理剩余的多边形
            List<Polygon> remainingPolygons = polygons.GetRange(1, polygons.Count - 1);
            List<Polygon> finalResult = new List<Polygon>();

            foreach (Polygon p in intermediateResult)
            {
                List<Polygon> nonIntersecting = DifferenceRegions(p, remainingPolygons);
                finalResult.AddRange(nonIntersecting);
            }

            return finalResult;
        }

        /// <summary>
        /// 获取两个Polygon不相交部分的面
        /// </summary>
        /// <param name="polygon1">第一个多边形</param>
        /// <param name="polygon2">第二个多边形</param>
        /// <returns>不相交部分的几何元素</returns>
        public static Polygon DifferencePolygon(this Polygon polygon1, Polygon polygon2)
        {
            // 检查输入多边形是否为空
            if (polygon1 == null || polygon2 == null)
            {
                return null;
            }

            // 计算两个多边形的对称差集（不相交部分）
            NetTopologySuite.Geometries.Geometry nonIntersecting = polygon1.Difference(polygon2);

            // 如果不相交部分为空，则返回null
            if (nonIntersecting == null || nonIntersecting.IsEmpty)
            {
                return null;
            }
            else if (nonIntersecting is Polygon polygon)
            {
                var diff = polygon;
                if (diff.Area < 0.01)
                    return null;
                return diff;
            }
            else if (nonIntersecting is MultiPolygon mtPolygon)
            {
                var diff = mtPolygon.OrderByDescending(p => p.Area).Cast<Polygon>().FirstOrDefault();
                if (diff.Area < 0.01)
                    return null;
                return diff;
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// 获取两条边不相交部分的边
        /// </summary>
        /// <param name="segment1">第一条边</param>
        /// <param name="segment2">第二条边</param>
        /// <returns>不相交部分的几何元素</returns>
        public static List<LineSegment> SlpitLine(this LineSegment segment1, LineSegment segment2, double tolerance = 1e-5)
        {
            // 检查输入多边形是否为空
            if (segment1 == null || segment2 == null)
            {
                return new List<LineSegment> { segment1 };
            }

            // 计算两个多边形的对称差集（不相交部分）
            NetTopologySuite.Geometries.Geometry diff = segment1.ToLineString().Difference(segment2.SetOffset(1 / 304.8).ToLineString());

            // 如果不相交部分为空，则返回null
            if (diff == null || diff.IsEmpty)
                return new List<LineSegment> { segment1 };

            else if (diff is LineString line)
            {
                if (line.Length < tolerance)
                    return new List<LineSegment> { segment1 };
                return new List<LineSegment> { line.ToLineSegment() };
            }
            else if (diff is MultiLineString mtLine)
            {
                var result = new List<LineSegment>();

                foreach (LineString item in mtLine)
                {
                    if (item.Length < tolerance)
                        continue;
                    result.Add(item.ToLineSegment());
                }
                if (!result.Any())
                {
                    return new List<LineSegment> { segment1 };
                }
                return result;
            }
            else
            {
                return new List<LineSegment> { segment1 };
            }
        }

        /// <summary>
        /// 拆分多边形的方法，根据输入的切割线拆分多边形
        /// </summary>
        /// <param name="polygon">要拆分的多边形</param>
        /// <param name="lineSegment">用来切割多边形的线段</param> 
        /// <returns>拆分后的多边形列表</returns>
        public static List<Polygon> SplitPolygon(this Polygon polygon, LineSegment lineSegment)
        {
            // 确保传入的参数是有效的
            if (polygon == null || lineSegment == null)
            {
                return null;
            }

            // 创建一个几何工厂来生成几何对象
            GeometryFactory geometryFactory = new GeometryFactory();

            // 获取线段的起点和终点
            Coordinate p1 = lineSegment.P0;
            Coordinate p2 = lineSegment.P1;

            // 获取目标多边形的边界范围
            Envelope polygonEnvelope = polygon.EnvelopeInternal;

            // 上下区域的扩展范围
            double minX = polygonEnvelope.MinX - 10;  // 微调扩展范围，避免精度问题
            double maxX = polygonEnvelope.MaxX + 10;
            double minY = polygonEnvelope.MinY - 10;
            double maxY = polygonEnvelope.MaxY + 10;

            // 构造上区域的多边形
            Coordinate upperLeft = new Coordinate(minX, p1.Y);
            Coordinate upperRight = new Coordinate(maxX, p1.Y);
            Coordinate upperBottomLeft = new Coordinate(minX, maxY);
            Coordinate upperBottomRight = new Coordinate(maxX, maxY);
            Polygon upperPolygon = geometryFactory.CreatePolygon(new Coordinate[]
            {
        upperBottomLeft, upperBottomRight, upperRight, upperLeft, upperBottomLeft
            });

            // 构造下区域的多边形
            Coordinate lowerTopLeft = new Coordinate(minX, p1.Y);
            Coordinate lowerTopRight = new Coordinate(maxX, p1.Y);
            Coordinate lowerBottomLeft = new Coordinate(minX, minY);
            Coordinate lowerBottomRight = new Coordinate(maxX, minY);
            Polygon lowerPolygon = geometryFactory.CreatePolygon(new Coordinate[]
            {
        lowerTopLeft, lowerTopRight, lowerBottomRight, lowerBottomLeft, lowerTopLeft
            });

            // 检查区域多边形是否有效
            if (!upperPolygon.IsValid || !lowerPolygon.IsValid)
            {
                return null;
            }

            // 分别计算上部分和下部分多边形
            NetTopologySuite.Geometries.Geometry upperResult = polygon.Intersection(upperPolygon);  // 上部分
            NetTopologySuite.Geometries.Geometry lowerResult = polygon.Intersection(lowerPolygon);  // 下部分

            // 如果运算结果为空，则没有交集，返回一个空列表
            List<Polygon> result = new List<Polygon>();

            // 处理上部分结果
            if (upperResult != null && !upperResult.IsEmpty)
            {
                if (upperResult is Polygon polygonResult)
                {
                    result.Add(polygonResult);
                }
                else if (upperResult is GeometryCollection collection)
                {
                    foreach (NetTopologySuite.Geometries.Geometry geometry in collection)
                    {
                        if (geometry is Polygon poly)
                        {
                            result.Add(poly);
                        }
                    }
                }
            }

            // 处理下部分结果
            if (lowerResult != null && !lowerResult.IsEmpty)
            {
                if (lowerResult is Polygon polygonResult)
                {
                    result.Add(polygonResult);
                }
                else if (lowerResult is GeometryCollection collection)
                {
                    foreach (NetTopologySuite.Geometries.Geometry geometry in collection)
                    {
                        if (geometry is Polygon poly)
                        {
                            result.Add(poly);
                        }
                    }
                }
            }

            // 如果结果为空，打印相应信息
            if (result.Count == 0)
            {
            }

            // 返回拆分后的多边形列表
            return result;
        }

        #endregion

        #region ### Intersect
        /// <summary>
        /// 计算两个直线的交点
        /// </summary>
        /// <param name="seg1"></param>
        /// <param name="seg2"></param>
        /// <returns></returns>
        public static Coordinate IntersectPoint(this LineSegment seg1, LineSegment seg2)
        {
            // 获取两个线段的端点
            Coordinate p1 = seg1.P0;
            Coordinate p2 = seg1.P1;
            Coordinate p3 = seg2.P0;
            Coordinate p4 = seg2.P1;

            // 计算两个线段所在的直线的参数
            double denom = (p1.X - p2.X) * (p3.Y - p4.Y) - (p1.Y - p2.Y) * (p3.X - p4.X);

            // 如果分母为零，则表示两条线段平行或重合
            if (denom == 0)
            {
                return null;  // 没有交点
            }

            // 计算交点的 X 和 Y 坐标
            double x = ((p1.X * p2.Y - p1.Y * p2.X) * (p3.X - p4.X) - (p1.X - p2.X) * (p3.X * p4.Y - p3.Y * p4.X)) / denom;
            double y = ((p1.X * p2.Y - p1.Y * p2.X) * (p3.Y - p4.Y) - (p1.Y - p2.Y) * (p3.X * p4.Y - p3.Y * p4.X)) / denom;

            // 创建交点坐标
            Coordinate intersection = new Coordinate(x, y);

            return intersection;
        }

        /// <summary>
        /// 获取两个Polygon相交部分的面
        /// </summary>
        /// <param name="polygon1">第一个多边形</param>
        /// <param name="polygon2">第二个多边形</param>
        /// <returns>相交部分的几何元素</returns>
        public static Polygon IntersectPolygon(this Polygon polygon1, Polygon polygon2)
        {
            // 检查输入多边形是否为空
            if (polygon1 == null || polygon2 == null)
            {
                return null;
            }

            // 计算两个多边形的相交部分
            NetTopologySuite.Geometries.Geometry intersection = null;
            try
            {
                intersection = polygon1.Intersection(polygon2);
            }
            catch (Exception ex)
            {
                return null;
            }

            // 如果相交部分为空，则返回null
            if (intersection == null || intersection.IsEmpty)
            {
                return null;
            }
            else if (intersection is Polygon polygon)
            {
                var intersect = polygon;
                if (intersect.Area < 0.01)
                    return null;
                return intersect;
            }
            else if (intersection is MultiPolygon mtPolygon)
            {
                var intersect = mtPolygon.OrderByDescending(p => p.Area).Cast<Polygon>().FirstOrDefault();
                if (intersect.Area < 0.01)
                    return null;
                return intersect;
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// 获取两个Polygon相交部分的面
        /// </summary>
        /// <param name="polygon1">第一个多边形</param>
        /// <param name="polygon2">第二个多边形</param>
        /// <returns>相交部分的几何元素</returns>
        public static List<Polygon> IntersectPolygons(this Polygon polygon1, Polygon polygon2, double tolerance = 0.01)
        {
            var intersectPlgs = new List<Polygon>();

            // 检查输入多边形是否为空
            if (polygon1 == null || polygon2 == null)
            {
                return null;
            }

            // 计算两个多边形的相交部分
            NetTopologySuite.Geometries.Geometry intersection = null;
            try
            {
                intersection = polygon1.Intersection(polygon2);
            }
            catch (Exception ex)
            {
                return null;
            }

            // 如果相交部分为空，则返回null
            if (intersection == null || intersection.IsEmpty)
            {
                return null;
            }
            else if (intersection is Polygon polygon)
            {
                var intersect = polygon;
                if (intersect.Area >= tolerance)
                {
                    intersectPlgs.Add(intersect);
                }
                return intersectPlgs;
            }
            else if (intersection is MultiPolygon mtPolygon)
            {
                foreach (Polygon intersect in mtPolygon)
                {
                    if (intersect.Area >= tolerance)
                    {
                        intersectPlgs.Add(intersect);
                    }
                }
                return intersectPlgs;
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// 获取LineSegment和Polygon相交部分的线
        /// </summary>
        /// <param name="segment">第一个多边形</param>
        /// <param name="plg">第二个多边形</param>
        /// <returns>相交部分的几何元素</returns>
        public static LineSegment IntersectLine(this LineSegment segment, Polygon plg)
        {
            // 检查输入多边形是否为空
            if (segment == null || plg == null)
            {
                return segment;
            }

            // 计算两个多边形的相交部分
            NetTopologySuite.Geometries.Geometry intersection = null;
            try
            {
                intersection = segment.ToLineString().Intersection(plg.SetOffset(0.01 / 304.8, false));
            }
            catch (Exception ex)
            {
                return segment;
            }

            // 如果相交部分为空，则返回null
            if (intersection == null || intersection.IsEmpty)
            {
                return segment;
            }
            else if (intersection is LineString line)
            {
                return line.ToLineSegment(); // 返回面
            }
            else if (intersection is MultiLineString multiLine)
            {
                return multiLine.Cast<LineString>().OrderByDescending(l => l.Length).FirstOrDefault().ToLineSegment();
            }
            else
            {
                return segment;
            }
        }

        #endregion

        #region ### Union
        /// <summary>
        /// 获取两个Polygon合并的面
        /// </summary>
        /// <param name="polygon1">第一个多边形</param>
        /// <param name="polygon2">第二个多边形</param>
        /// <returns>相交部分的几何元素</returns>
        public static Polygon UnionPolygon(this Polygon polygon1, Polygon polygon2, double tolerance = 0.01)
        {
            // 检查输入数组是否为null或包含null元素
            if (polygon1 == null || polygon2 == null)
            {
                return null;
            }

            // 计算两个多边形的并集
            NetTopologySuite.Geometries.Geometry nonIntersecting = polygon1.SetOffset(tolerance / 304.8, false).Union(polygon2.SetOffset(tolerance / 304.8, false));

            if (nonIntersecting == null || nonIntersecting.IsEmpty)
            {
                return null;
            }
            else if (nonIntersecting is Polygon polygon)
            {
                var union = polygon;
                return union.SetOffset(tolerance / 304.8);
            }
            else if (nonIntersecting is MultiPolygon mtPolygon)
            {
                return null;
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// 将多个多边形合并成一个新的多边形
        /// </summary>
        /// <param name="polygons">要合并的多边形数组</param>
        /// <returns>合并后的多边形，如果无法合并成单个多边形则返回null</returns>
        public static Polygon MergePolygon(this ICollection<Polygon> polygons, double tolerance = 10)
        {
            // 检查输入数组是否为null或包含null元素
            if (polygons == null || polygons.Any(p => p == null))
            {
                return null;
            }

            // 检查数组是否为空
            if (polygons.Count == 0)
            {
                return null;
            }
            polygons = polygons.Where(p => p != null).ToList();

            // 初始化合并结果为第一个多边形
            NetTopologySuite.Geometries.Geometry merged = polygons.ElementAt(0).SetOffset(tolerance / 304.8, false);

            // 逐个合并剩余多边形
            for (int i = 1; i < polygons.Count; i++)
            {
                try
                {
                    NetTopologySuite.Geometries.Geometry unionResult = merged.Union(polygons.ElementAt(i).SetOffset(tolerance / 304.8, false));

                    // 检查合并结果是否为空或无效
                    if (unionResult == null || unionResult.IsEmpty)
                    {
                        return null;
                    }

                    merged = unionResult;
                }
                catch (Exception ex)
                {
                    continue;
                }
            }
            // 检查最终结果是否为Polygon类型
            if (merged is Polygon mergedPolygon)
            {
                return mergedPolygon.SetOffset(tolerance / 304.8, true);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// 将一组共线且首尾相连的线段合并成一条最长的线段
        /// </summary>
        /// <param name="lines">共线且首尾相连的线段集合</param>
        /// <returns>合并后的最长线段</returns>
        public static LineSegment MergeColinearLines(this IList<LineSegment> lines)
        {
            const double EPSILON = 1e-10;

            if (lines == null || !lines.Any())
                return null;
            if (lines.Count() == 1)
                return lines.First();
            // 收集所有端点
            var allPoints = lines.SelectMany(seg => new[] { seg.P0, seg.P1 }).ToList();
            // 创建一个包含唯一点和对应计数的字典
            var pointCounts = new Dictionary<Coordinate, int>();
            foreach (var point in allPoints)
            {
                bool found = false;
                foreach (var key in pointCounts.Keys.ToList())
                {
                    if (point.IsEquals(key, EPSILON))
                    {
                        pointCounts[key]++;
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    pointCounts[point] = 1;
                }
            }
            // 找出只出现一次的点（线段链的端点）
            var endpoints = pointCounts
                .Where(pair => pair.Value == 1)
                .Select(pair => pair.Key)
                .ToList();
            // 如果找到了两个端点，构建结果线段
            if (endpoints.Count == 2)
                return new LineSegment(endpoints[0], endpoints[1]);
            // 如果是闭合环，找出最远的两点作为端点
            double maxDistance = 0;
            LineSegment result = null;
            var uniquePoints = pointCounts.Keys.ToList();
            for (int i = 0; i < uniquePoints.Count; i++)
            {
                for (int j = i + 1; j < uniquePoints.Count; j++)
                {
                    // 直接计算两点之间的距离
                    double dx = uniquePoints[j].X - uniquePoints[i].X;
                    double dy = uniquePoints[j].Y - uniquePoints[i].Y;
                    double dz = uniquePoints[j].Z - uniquePoints[i].Z;
                    double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (dist > maxDistance)
                    {
                        maxDistance = dist;
                        result = new LineSegment(uniquePoints[i], uniquePoints[j]);
                    }
                }
            }
            // 检查方向与第一条线段是否一致
            if (result.Direction() != lines[0].Direction())
            {
                result = new LineSegment(result.P1, result.P0);
            }

            return result;
        }
        #endregion

        #region ### Project
        /// <summary>
        /// 计算多边形在直线上的投影
        /// </summary>
        /// <param name="polygon">需要投影的多边形（四边形）</param>
        /// <param name="segment">投影的基准直线</param>
        /// <returns>表示投影的线段</returns>
        public static LineSegment ProjectionOnLine(this Polygon polygon, LineSegment segment)
        {
            if (polygon == null)
                return null;

            if (segment == null)
                return null;

            // 获取直线的起点和终点
            Coordinate lineStart = segment.P0;
            Coordinate lineEnd = segment.P1;

            // 创建表示直线的线段
            LineSegment lineSegment = new LineSegment(lineStart, lineEnd);

            // 获取多边形的所有坐标点
            Coordinate[] polygonCoords = polygon.Coordinates;

            // 存储投影点在直线上的参数值
            List<double> projectionParams = new List<double>();

            // 计算每个点在直线上的投影参数
            foreach (Coordinate point in polygonCoords)
            {
                double param = lineSegment.ProjectionFactor(point);
                projectionParams.Add(param);
            }

            // 获取参数的最小值和最大值
            double minParam = projectionParams.Min();
            double maxParam = projectionParams.Max();

            // 处理投影超出线段范围的情况
            minParam = Math.Max(0, minParam);
            maxParam = Math.Min(1, maxParam);

            // 如果最小值大于最大值，说明没有有效投影
            if (minParam > maxParam)
            {
                // 返回一个长度为0的线段
                return new LineSegment(lineStart, lineStart);
            }

            // 计算投影线段的起点和终点
            Coordinate projectionStart = lineSegment.PointAlong(minParam);
            Coordinate projectionEnd = lineSegment.PointAlong(maxParam);

            // 创建并返回投影线段
            return new LineSegment(projectionStart, projectionEnd);
        }

        /// <summary>
        /// 将一个线段投影到另一个线段上，并裁剪超出范围的部分
        /// </summary>
        /// <param name="sourceSegment">要投影的源线段</param>
        /// <param name="targetSegment">投影的目标线段</param>
        /// <returns>投影后的线段，如果投影结果无效则返回null</returns>
        public static LineSegment ProjectSegment(this LineSegment sourceSegment, LineSegment targetSegment)
        {
            // 验证输入参数
            if (sourceSegment == null || targetSegment == null)
                return null;

            // 检查目标线段长度是否过小
            if (targetSegment.Length < 1e-10)
                return null;

            // 计算源线段两端点在目标线段上的投影因子
            // 投影因子0表示在起点，1表示在终点
            double factor0 = targetSegment.ProjectionFactor(sourceSegment.P0);
            double factor1 = targetSegment.ProjectionFactor(sourceSegment.P1);

            // 裁剪投影因子到[0,1]范围内
            double clippedFactor0 = Math.Max(0, Math.Min(1, factor0));
            double clippedFactor1 = Math.Max(0, Math.Min(1, factor1));

            // 根据裁剪后的投影因子计算投影点坐标
            Coordinate proj0 = new Coordinate(
                targetSegment.P0.X + clippedFactor0 * (targetSegment.P1.X - targetSegment.P0.X),
                targetSegment.P0.Y + clippedFactor0 * (targetSegment.P1.Y - targetSegment.P0.Y)
            );

            Coordinate proj1 = new Coordinate(
                targetSegment.P0.X + clippedFactor1 * (targetSegment.P1.X - targetSegment.P0.X),
                targetSegment.P0.Y + clippedFactor1 * (targetSegment.P1.Y - targetSegment.P0.Y)
            );

            // 检查投影后的线段是否有效（长度不为零）
            if (proj0.Distance(proj1) < 1e-10)
                return null;

            return new LineSegment(proj0, proj1);
        }
        #endregion

        #region ### Overlap
        /// <summary>
        /// 查找所有重合的线
        /// </summary>
        /// <param name="function"></param>
        /// <param name="space"></param>
        /// <param name="tolerance"></param>
        /// <returns></returns>
        public static List<LineSegment> FindOverlappingLines(this Polygon function, Polygon space, double tolerance = 10 / 304.8)
        {
            var result = new List<LineSegment>();
            var functionProfile = function.GetPolygonLines();
            var spaceProfile = space.GetPolygonLines();
            foreach (var functionLine in functionProfile)
            {
                foreach (var spaceLine in spaceProfile)
                {
                    if (functionLine.IsParallel(spaceLine) && spaceLine.Distance(functionLine.MidPoint) <= tolerance)
                    {
                        result.Add(functionLine);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// 查找所有重合的线
        /// </summary>
        /// <param name="function"></param>
        /// <param name="space"></param>
        /// <param name="tolerance"></param>
        /// <returns></returns>
        public static List<LineSegment> FindOverlappingLines(this Polygon function, List<LineSegment> profileLines, double tolerance = 10 / 304.8)
        {
            var result = new List<LineSegment>();
            var functionProfile = function.GetPolygonLines();
            foreach (var functionLine in functionProfile)
            {
                foreach (var spaceLine in profileLines)
                {
                    if (functionLine.IsParallel(spaceLine) && spaceLine.Distance(functionLine.MidPoint) <= tolerance)
                    {
                        result.Add(functionLine);
                    }
                }
            }
            return result;
        }

        #endregion

        #endregion

        #region ## 射线法
        /// <summary>
        /// 检测射线与几何对象列表的碰撞，返回元组形式的结果
        /// </summary>
        /// <param name="origin">射线原点</param>
        /// <param name="direction">射线方向</param>
        /// <param name="geometries">待检测的几何对象列表</param>
        /// <param name="maxDistance">最大检测距离，默认为一个较大但合理的值</param>
        /// <returns>元组(碰撞几何体, 距离, 碰撞点)，如果没有碰撞则返回(null, -1, null)</returns>
        public static (NetTopologySuite.Geometries.Geometry HitGeometry, double Distance, Coordinate HitPoint) FindRayIntersection(
            this Coordinate origin,
            Vector2D direction,
            List<NetTopologySuite.Geometries.Geometry> geometries,
            double maxDistance = 1.0E+10)
        {
            // 标准化方向向量
            direction = direction.Normalize();

            // 创建几何工厂
            var factory = new GeometryFactory();

            // 计算射线终点 - 使用安全的最大距离
            Coordinate endPoint = new Coordinate(
                origin.X + direction.X * maxDistance,
                origin.Y + direction.Y * maxDistance
            );

            // 创建射线线段
            LineString ray = factory.CreateLineString(new[] { origin, endPoint });

            // 用于保存最近的碰撞结果
            NetTopologySuite.Geometries.Geometry closestGeometry = null;
            Coordinate closestPoint = null;
            double minDistance = double.MaxValue;

            // 检测与每个几何对象的碰撞
            foreach (var geometry in geometries)
            {
                try
                {
                    // 包络盒快速检测
                    if (!ray.EnvelopeInternal.Intersects(geometry.EnvelopeInternal))
                        continue;

                    // 计算射线与几何对象的交点
                    NetTopologySuite.Geometries.Geometry intersection;

                    // 根据几何对象类型选择适当的相交计算方法
                    if (geometry is Polygon polygon)
                    {
                        // 对于多边形，检查与边界的交点
                        intersection = ray.Intersection(polygon.Boundary);
                    }
                    else
                    {
                        // 对于其他类型的几何对象，直接计算交点
                        intersection = ray.Intersection(geometry);
                    }

                    // 无交点情况
                    if (intersection == null || intersection.IsEmpty)
                        continue;

                    // 处理所有交点，找出最近的一个
                    for (int i = 0; i < intersection.NumGeometries; i++)
                    {
                        Coordinate hitPoint = intersection.GetGeometryN(i).Coordinate;

                        // 计算从原点到交点的向量
                        Vector2D toIntersection = new Vector2D(origin, hitPoint);

                        // 确保交点在射线前方(点积大于0表示方向一致)
                        if (direction.Dot(toIntersection) > 0)
                        {
                            double distance = hitPoint.Distance(origin);

                            // 确保距离在最大检测范围内
                            if (distance <= maxDistance &&
                                distance < minDistance &&
                                distance > 1e-10)  // 排除起点附近的点(避免数值精度问题)
                            {
                                minDistance = distance;
                                closestPoint = hitPoint;
                                closestGeometry = geometry;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    continue;
                }
            }

            // 如果找到碰撞，返回元组结果
            if (closestGeometry != null)
            {
                return (closestGeometry, minDistance, closestPoint);
            }

            // 没有找到碰撞，返回(null, -1, null)
            return (null, -1, null);
        }

        #endregion

        #endregion

        #region ## NetTopologySuite内部类型转换
        /// <summary>
        /// 线段转多段线
        /// </summary>
        /// <param name="segment">要转换的线段</param>
        /// <returns>多段线</returns>
        public static LineString ToLineString(this LineSegment segment)
        {
            LineString lineString = new LineString(new Coordinate[] { new Coordinate(segment.P0), new Coordinate(segment.P1) });
            return lineString;
        }

        /// <summary>
        /// 多段线转线段
        /// </summary>
        /// <param name="lineString">要转换的线段</param>
        /// <returns>多段线</returns>
        public static LineSegment ToLineSegment(this LineString lineString)
        {
            LineSegment lineSegment = new LineSegment(lineString.StartPoint.Coordinate, lineString.EndPoint.Coordinate);
            return lineSegment;
        }

        /// <summary>
        /// 从 LineString 获取 LineSegment集合
        /// </summary>
        /// <param name="lineString">LineString 对象</param>
        /// <returns>LineSegment 集合</returns>
        public static List<LineSegment> ToLineSegments(this LineString lineString)
        {
            // 获取 LineString 中的点集合
            var points = lineString.Coordinates;

            // 如果没有点，直接返回空集合
            if (points == null || points.Length < 2)
            {
                return new List<LineSegment>();
            }

            // 创建一个 LineSegment 集合来存储每两个相邻的点构成的线段
            List<LineSegment> segments = new List<LineSegment>();

            // 遍历每两个连续的点
            for (int i = 0; i < points.Length - 1; i++)
            {
                // 创建 LineSegment 并添加到列表中
                segments.Add(new LineSegment(points[i], points[i + 1]));
            }

            // 返回转换为集合的 LineSegment 列表
            return segments.ToList();
        }

        #endregion


        #region 其他方法
        /// <summary>
        /// 获取上一个元素
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="collection"></param>
        /// <param name="element"></param>
        /// <returns></returns>
        public static T Previous<T>(this IList<T> collection, T element)
        {
            if (element == null)
                throw new Exception($"输入{nameof(element)}元素为空!");
            int index = collection.ToList().IndexOf(element);
            int previousIndex = (index - 1 + collection.Count) % collection.Count;
            return collection[previousIndex];
        }
        /// <summary>
        /// 获取下一个元素
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="collection"></param>
        /// <param name="element"></param>
        /// <returns></returns>
        public static T Next<T>(this IList<T> collection, T element)
        {
            if (element == null)
                throw new Exception($"输入{nameof(element)}元素为空!");
            int index = collection.ToList().IndexOf(element);
            int nextIndex = (index + 1) % collection.Count;
            return collection[nextIndex];
        }
        /// <summary>
        /// 获取上一个元素
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="collection"></param>
        /// <param name="element"></param>
        /// <returns></returns>
        public static T Previous<T>(this ICollection<T> collection, T element)
        {
            if (element == null)
                throw new Exception($"输入{nameof(element)}元素为空!");
            int index = collection.ToList().IndexOf(element);
            int previousIndex = (index - 1 + collection.Count) % collection.Count;
            return collection.ElementAt(previousIndex);
        }
        /// <summary>
        /// 获取下一个元素
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="collection"></param>
        /// <param name="element"></param>
        /// <returns></returns>
        public static T Next<T>(this ICollection<T> collection, T element)
        {
            if (element == null)
                throw new Exception($"输入{nameof(element)}元素为空!");
            int index = collection.ToList().IndexOf(element);
            int nextIndex = (index + 1) % collection.Count;
            return collection.ElementAt(nextIndex);
        }
        /// <summary>
        /// 获取上一个元素
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="collection"></param>
        /// <param name="element"></param>
        /// <returns></returns>
        public static T Previous<T>(this IEnumerable<T> collection, T element)
        {
            if (element == null)
                throw new Exception($"输入{nameof(element)}元素为空!");
            int index = collection.ToList().IndexOf(element);
            int previousIndex = (index - 1 + collection.Count()) % collection.Count();
            return collection.ElementAt(previousIndex);
        }
        /// <summary>
        /// 获取下一个元素
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="collection"></param>
        /// <param name="element"></param>
        /// <returns></returns>
        public static T Next<T>(this IEnumerable<T> collection, T element)
        {
            if (element == null)
                throw new Exception($"输入{nameof(element)}元素为空!");
            int index = collection.ToList().IndexOf(element);
            int nextIndex = (index + 1) % collection.Count();
            return collection.ElementAt(nextIndex);
        }

        #endregion

    }
}
