using System;
using System.Collections.Generic;
using System.Linq;
using BIMCanvas.Core.Models.Computed;
using BIMCanvas.Core.Models.Geometry;
using BIMCanvas.Core.Models.Revit;
using Microsoft.Extensions.Logging;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// Zone 边界段语义计算服务
    /// 将 zone 的多边形边界拆分为 wall/passage/door/window 段
    /// </summary>
    public class ZoneBoundaryService
    {
        private readonly ILogger<ZoneBoundaryService> _logger;

        // 算法常量
        private const double ParallelTolerance = 0.05;       // |sin(θ)| < 0.05 ≈ 3°
        private const double DistanceTolerance = 150.0;      // mm，覆盖 Revit 偏移 + Agent 四舍五入
        private const double MinOverlapLength = 10.0;        // mm，最小有效重叠
        private const double MergeThreshold = 50.0;          // mm，极短段合并阈值
        private const double ShortPassageThreshold = 200.0;  // mm，短 passage 修正阈值
        private const double ClosureTolerance = 1.0;         // mm，首尾闭合容差

        public ZoneBoundaryService(ILogger<ZoneBoundaryService> logger)
        {
            _logger = logger;
        }

        #region 内部数据结构

        /// <summary>叶子 zone 的上下文信息</summary>
        private class ZoneContext
        {
            public Zone Leaf;
            public Zone Parent;       // null 表示顶层 room zone（无 parent）
            public List<Zone> Siblings = new List<Zone>();  // 同 parent 的其他叶子 zone
        }

        /// <summary>Opening 在某条边上的预归属匹配</summary>
        private class OpeningMatch
        {
            public Opening Opening;
            public double TMin;  // 相对于整条边的参数化起点 [0,1]
            public double TMax;  // 相对于整条边的参数化终点 [0,1]
        }

        /// <summary>带类型标记的参数区间</summary>
        private class TaggedInterval
        {
            public double Start;
            public double End;
            public string Type;     // "wall" | "passage"
            public string Id;       // passage 时为 sibling zone id，可能为 null
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 计算指定 zone 的边界段语义
        /// </summary>
        public List<ZoneBoundaryData> CalculateBoundarySegments(
            List<Zone> allZones,
            List<Opening> openings,
            List<string> zoneIds = null)
        {
            var results = new List<ZoneBoundaryData>();

            // 1. 构建 leaf → (parent, siblings) 关系索引
            var index = BuildZoneIndex(allZones);

            // 2. 确定要处理的 zone
            IEnumerable<ZoneContext> targets;
            if (zoneIds != null && zoneIds.Count > 0)
            {
                var filterSet = new HashSet<string>(zoneIds);
                targets = index.Values.Where(ctx => filterSet.Contains(ctx.Leaf.Id));
            }
            else
            {
                targets = index.Values;
            }

            // 3. 对每个叶子 zone 计算边界段
            foreach (var ctx in targets)
            {
                var data = ProcessLeafZone(ctx, openings);
                if (data != null)
                    results.Add(data);
            }

            _logger.LogDebug("[ZoneBoundary] 计算完成: {Count} 个 zone", results.Count);
            return results;
        }

        #endregion

        #region 索引构建

        /// <summary>递归遍历 zone 树，构建叶子 zone 索引</summary>
        private Dictionary<string, ZoneContext> BuildZoneIndex(List<Zone> allZones)
        {
            var index = new Dictionary<string, ZoneContext>();

            foreach (var zone in allZones)
            {
                CollectLeaves(zone, null, index);
            }

            return index;
        }

        /// <summary>递归收集叶子 zone</summary>
        private void CollectLeaves(Zone zone, Zone parent, Dictionary<string, ZoneContext> index)
        {
            if (zone.SubZones != null && zone.SubZones.Count > 0)
            {
                // 当前 zone 有子分区，递归处理
                // 收集所有直接子叶子 zone
                var directLeaves = new List<Zone>();
                foreach (var sub in zone.SubZones)
                {
                    if (sub.SubZones == null || sub.SubZones.Count == 0)
                        directLeaves.Add(sub);
                }

                foreach (var sub in zone.SubZones)
                {
                    if (sub.SubZones != null && sub.SubZones.Count > 0)
                    {
                        // 递归（sub 自己有子分区）
                        CollectLeaves(sub, zone, index);
                    }
                    else
                    {
                        // sub 是叶子，parent = zone，siblings = 同级其他叶子
                        var siblings = directLeaves.Where(s => s.Id != sub.Id).ToList();
                        index[sub.Id] = new ZoneContext
                        {
                            Leaf = sub,
                            Parent = zone,
                            Siblings = siblings
                        };
                    }
                }
            }
            else
            {
                // 当前 zone 是叶子（顶层 room zone，无 parent）
                index[zone.Id] = new ZoneContext
                {
                    Leaf = zone,
                    Parent = parent,
                    Siblings = new List<Zone>()
                };
            }
        }

        #endregion

        #region 主处理流程

        /// <summary>处理单个叶子 zone</summary>
        private ZoneBoundaryData ProcessLeafZone(ZoneContext ctx, List<Opening> openings)
        {
            var leaf = ctx.Leaf;
            if (leaf.RawBoundary == null || leaf.RawBoundary.Vertices.Length < 3)
            {
                _logger.LogDebug("[ZoneBoundary] 跳过 {ZoneId}: RawBoundary 为空或不足 3 个顶点", leaf.Id);
                return null;
            }

            var vertices = leaf.RawBoundary.Vertices;
            var edgeCount = vertices.Length;

            // Phase A: Opening 预归属
            var openingsByEdge = PreMatchOpeningsToEdges(vertices, openings);

            // Phase B + C: 逐边处理
            var allSegments = new List<BoundarySegment>();

            for (int i = 0; i < edgeCount; i++)
            {
                var edgeStart = vertices[i];
                var edgeEnd = vertices[(i + 1) % edgeCount];  // 隐式闭合

                List<TaggedInterval> intervals;

                if (ctx.Parent == null)
                {
                    // 顶层 room zone：所有边默认 wall
                    intervals = new List<TaggedInterval>
                    {
                        new TaggedInterval { Start = 0, End = 1, Type = "wall", Id = null }
                    };
                }
                else
                {
                    // 子 zone：用 parent 匹配分类
                    intervals = ClassifyEdge(edgeStart, edgeEnd, ctx.Parent, ctx.Siblings);
                }

                // 区间转 BoundarySegment
                var edgeSegments = new List<BoundarySegment>();
                foreach (var interval in intervals)
                {
                    edgeSegments.Add(IntervalToSegment(
                        edgeStart, edgeEnd,
                        interval.Start, interval.End,
                        interval.Type, interval.Id));
                }

                // Phase C: 门窗切割（只切 wall 段）
                edgeSegments = SplitWallsByOpenings(
                    edgeSegments, i, edgeStart, edgeEnd, openingsByEdge);

                allSegments.AddRange(edgeSegments);
            }

            // Phase D: 后处理
            allSegments = PostProcess(allSegments);

            return new ZoneBoundaryData
            {
                ZoneId = leaf.Id,
                Segments = allSegments
            };
        }

        #endregion

        #region Phase A: Opening 预归属

        /// <summary>
        /// 将每个 opening 绑定到 zone 边界上垂距最小的边
        /// </summary>
        private Dictionary<int, List<OpeningMatch>> PreMatchOpeningsToEdges(
            Point2D[] vertices, List<Opening> openings)
        {
            var result = new Dictionary<int, List<OpeningMatch>>();
            var edgeCount = vertices.Length;

            foreach (var opening in openings)
            {
                if (opening.Line == null) continue;

                var openingDir = opening.Line.Direction;
                var openingMid = opening.Line.Midpoint;

                int bestEdgeIndex = -1;
                double bestPerpDist = double.MaxValue;

                for (int i = 0; i < edgeCount; i++)
                {
                    var eStart = vertices[i];
                    var eEnd = vertices[(i + 1) % edgeCount];

                    var edgeDx = eEnd.X - eStart.X;
                    var edgeDy = eEnd.Y - eStart.Y;
                    var edgeLen = Math.Sqrt(edgeDx * edgeDx + edgeDy * edgeDy);
                    if (edgeLen < 1e-6) continue;

                    var eDirX = edgeDx / edgeLen;
                    var eDirY = edgeDy / edgeLen;

                    // 平行检测：opening 方向和边方向必须接近平行
                    var cross = Math.Abs(eDirX * openingDir.Y - eDirY * openingDir.X);
                    if (cross > ParallelTolerance) continue;

                    // 垂直距离：opening 中点到边所在无限延长线的垂距
                    var dx = openingMid.X - eStart.X;
                    var dy = openingMid.Y - eStart.Y;
                    var perpDist = Math.Abs(eDirX * dy - eDirY * dx);
                    if (perpDist > DistanceTolerance) continue;

                    if (perpDist < bestPerpDist)
                    {
                        bestPerpDist = perpDist;
                        bestEdgeIndex = i;
                    }
                }

                if (bestEdgeIndex >= 0)
                {
                    // 投影 opening 两端点到边上
                    var eStart = vertices[bestEdgeIndex];
                    var eEnd = vertices[(bestEdgeIndex + 1) % edgeCount];
                    var (tMin, tMax) = ProjectOpeningOntoEdge(opening.Line, eStart, eEnd);

                    if (!result.ContainsKey(bestEdgeIndex))
                        result[bestEdgeIndex] = new List<OpeningMatch>();

                    result[bestEdgeIndex].Add(new OpeningMatch
                    {
                        Opening = opening,
                        TMin = tMin,
                        TMax = tMax
                    });
                }
            }

            return result;
        }

        /// <summary>将 opening 的两端点投影到边上，返回 (tMin, tMax) 参数</summary>
        private (double tMin, double tMax) ProjectOpeningOntoEdge(
            Line2D openingLine, Point2D edgeStart, Point2D edgeEnd)
        {
            var edgeDx = edgeEnd.X - edgeStart.X;
            var edgeDy = edgeEnd.Y - edgeStart.Y;
            var edgeLenSq = edgeDx * edgeDx + edgeDy * edgeDy;

            double ProjectPoint(Point2D p)
            {
                return ((p.X - edgeStart.X) * edgeDx +
                        (p.Y - edgeStart.Y) * edgeDy) / edgeLenSq;
            }

            var t0 = ProjectPoint(openingLine.Start);
            var t1 = ProjectPoint(openingLine.End);

            var tMin = Math.Max(0, Math.Min(t0, t1));
            var tMax = Math.Min(1, Math.Max(t0, t1));

            return (tMin, tMax);
        }

        #endregion

        #region Phase B: Wall/Passage 分类

        /// <summary>
        /// 对一条边进行 wall/passage 分类
        /// 策略："非 wall 即 passage"
        /// </summary>
        private List<TaggedInterval> ClassifyEdge(
            Point2D edgeStart, Point2D edgeEnd,
            Zone parent, List<Zone> siblings)
        {
            // Step 1: 收集 wall 区间（与 parent 边界重叠的部分）
            var wallIntervals = new List<(double start, double end)>();

            if (parent.RawBoundary != null)
            {
                var parentVerts = parent.RawBoundary.Vertices;
                for (int j = 0; j < parentVerts.Length; j++)
                {
                    var rStart = parentVerts[j];
                    var rEnd = parentVerts[(j + 1) % parentVerts.Length];

                    var range = FindOverlapRange(edgeStart, edgeEnd, rStart, rEnd);
                    if (range.HasValue)
                        wallIntervals.Add(range.Value);
                }
            }

            wallIntervals = MergeOverlappingIntervals(wallIntervals);

            // Step 2: 补集 = passage 区间
            var passageIntervals = ComputeComplement(wallIntervals);

            // Step 3: 如果没有 passage，全部 wall
            if (passageIntervals.Count == 0)
            {
                return new List<TaggedInterval>
                {
                    new TaggedInterval { Start = 0, End = 1, Type = "wall", Id = null }
                };
            }

            // Step 4: 为每个 passage 区间匹配最近 sibling
            var result = new List<TaggedInterval>();
            double cursor = 0;

            // 合并 wall 和 passage 区间，按起点排序
            int wi = 0, pi = 0;
            while (wi < wallIntervals.Count || pi < passageIntervals.Count)
            {
                double wallStart = wi < wallIntervals.Count ? wallIntervals[wi].start : 2;
                double passStart = pi < passageIntervals.Count ? passageIntervals[pi].start : 2;

                if (wallStart <= passStart && wi < wallIntervals.Count)
                {
                    result.Add(new TaggedInterval
                    {
                        Start = wallIntervals[wi].start,
                        End = wallIntervals[wi].end,
                        Type = "wall",
                        Id = null
                    });
                    wi++;
                }
                else if (pi < passageIntervals.Count)
                {
                    var pInterval = passageIntervals[pi];
                    string siblingId = FindBestSibling(
                        edgeStart, edgeEnd, pInterval.start, pInterval.end, siblings);

                    result.Add(new TaggedInterval
                    {
                        Start = pInterval.start,
                        End = pInterval.end,
                        Type = "passage",
                        Id = siblingId
                    });
                    pi++;
                }
            }

            return result;
        }

        /// <summary>为 passage 区间匹配最佳 sibling</summary>
        private string FindBestSibling(
            Point2D edgeStart, Point2D edgeEnd,
            double passStart, double passEnd,
            List<Zone> siblings)
        {
            string bestId = null;
            double bestOverlap = 0;

            foreach (var sibling in siblings)
            {
                if (sibling.RawBoundary == null) continue;
                var sibVerts = sibling.RawBoundary.Vertices;

                for (int j = 0; j < sibVerts.Length; j++)
                {
                    var rStart = sibVerts[j];
                    var rEnd = sibVerts[(j + 1) % sibVerts.Length];

                    var range = FindOverlapRange(edgeStart, edgeEnd, rStart, rEnd);
                    if (!range.HasValue) continue;

                    // 计算与 passage 区间的交集
                    var iStart = Math.Max(range.Value.tMin, passStart);
                    var iEnd = Math.Min(range.Value.tMax, passEnd);
                    var overlap = iEnd - iStart;

                    if (overlap > bestOverlap)
                    {
                        bestOverlap = overlap;
                        bestId = sibling.Id;
                    }
                }
            }

            return bestId;
        }

        #endregion

        #region Phase B 核心: FindOverlapRange

        /// <summary>
        /// 给定叶子 zone 的边 E 和参考多边形的边 R，
        /// 返回 E 上与 R 近共线重叠的参数区间 [tMin, tMax]
        /// </summary>
        private (double tMin, double tMax)? FindOverlapRange(
            Point2D edgeStart, Point2D edgeEnd,
            Point2D refStart, Point2D refEnd)
        {
            // Step 1: 边长检测
            double edgeDx = edgeEnd.X - edgeStart.X;
            double edgeDy = edgeEnd.Y - edgeStart.Y;
            double edgeLenSq = edgeDx * edgeDx + edgeDy * edgeDy;
            double edgeLen = Math.Sqrt(edgeLenSq);
            if (edgeLen < 1e-6) return null;

            double refDx = refEnd.X - refStart.X;
            double refDy = refEnd.Y - refStart.Y;
            double refLen = Math.Sqrt(refDx * refDx + refDy * refDy);
            if (refLen < 1e-6) return null;

            // Step 2: 平行检测（叉积 |sin(θ)| < 0.05 ≈ 3°）
            double eDirX = edgeDx / edgeLen;
            double eDirY = edgeDy / edgeLen;
            double rDirX = refDx / refLen;
            double rDirY = refDy / refLen;

            double cross = Math.Abs(eDirX * rDirY - eDirY * rDirX);
            if (cross > ParallelTolerance) return null;

            // Step 3: 垂直距离（refStart 到 E 无限延长线的距离）
            double dx = refStart.X - edgeStart.X;
            double dy = refStart.Y - edgeStart.Y;
            double perpDist = Math.Abs(eDirX * dy - eDirY * dx);
            if (perpDist > DistanceTolerance) return null;

            // Step 4: 参数化投影（ref 两端点投影到 E 上）
            double t0 = ((refStart.X - edgeStart.X) * edgeDx +
                         (refStart.Y - edgeStart.Y) * edgeDy) / edgeLenSq;
            double t1 = ((refEnd.X - edgeStart.X) * edgeDx +
                         (refEnd.Y - edgeStart.Y) * edgeDy) / edgeLenSq;

            double tMin = Math.Max(0, Math.Min(t0, t1));
            double tMax = Math.Min(1, Math.Max(t0, t1));

            // 最小有效重叠检测
            if ((tMax - tMin) * edgeLen < MinOverlapLength) return null;

            return (tMin, tMax);
        }

        #endregion

        #region Phase B 工具: 区间运算

        /// <summary>合并重叠区间</summary>
        private List<(double start, double end)> MergeOverlappingIntervals(
            List<(double start, double end)> intervals)
        {
            if (intervals.Count == 0) return new List<(double, double)>();

            var sorted = intervals.OrderBy(i => i.start).ToList();
            var merged = new List<(double start, double end)>();
            var current = sorted[0];

            for (int i = 1; i < sorted.Count; i++)
            {
                if (sorted[i].start <= current.end + 1e-6)
                    current.end = Math.Max(current.end, sorted[i].end);
                else
                {
                    merged.Add(current);
                    current = sorted[i];
                }
            }
            merged.Add(current);
            return merged;
        }

        /// <summary>计算 [0,1] 中 wallIntervals 的补集</summary>
        private List<(double start, double end)> ComputeComplement(
            List<(double start, double end)> wallIntervals)
        {
            var result = new List<(double start, double end)>();
            double cursor = 0.0;

            foreach (var (start, end) in wallIntervals)
            {
                if (start > cursor + 1e-6)
                    result.Add((cursor, start));
                cursor = Math.Max(cursor, end);
            }

            if (cursor < 1.0 - 1e-6)
                result.Add((cursor, 1.0));

            return result;
        }

        #endregion

        #region Phase C: 门窗切割

        /// <summary>
        /// 将 wall 段中预归属的 opening 切割为 wall + door/window + wall
        /// </summary>
        private List<BoundarySegment> SplitWallsByOpenings(
            List<BoundarySegment> segments,
            int edgeIndex,
            Point2D edgeStart, Point2D edgeEnd,
            Dictionary<int, List<OpeningMatch>> openingsByEdge)
        {
            if (!openingsByEdge.ContainsKey(edgeIndex))
                return segments;

            var matches = openingsByEdge[edgeIndex];
            if (matches.Count == 0)
                return segments;

            var edgeDx = edgeEnd.X - edgeStart.X;
            var edgeDy = edgeEnd.Y - edgeStart.Y;
            var edgeLenSq = edgeDx * edgeDx + edgeDy * edgeDy;
            var edgeLen = Math.Sqrt(edgeLenSq);

            var result = new List<BoundarySegment>();

            foreach (var seg in segments)
            {
                if (seg.Type != "wall")
                {
                    result.Add(seg);
                    continue;
                }

                // 计算当前 wall 段在整条边上的参数范围
                double wStartT = ProjectPointOntoEdge(seg.Start, edgeStart, edgeDx, edgeDy, edgeLenSq);
                double wEndT = ProjectPointOntoEdge(seg.End, edgeStart, edgeDx, edgeDy, edgeLenSq);

                // 确保 wStartT < wEndT
                if (wStartT > wEndT)
                {
                    var temp = wStartT;
                    wStartT = wEndT;
                    wEndT = temp;
                }

                double wLen = (wEndT - wStartT) * edgeLen;
                if (wLen < 1e-6)
                {
                    result.Add(seg);
                    continue;
                }

                // 筛选落在当前 wall 段范围内的 openings
                var candidates = new List<(Opening opening, double localTMin, double localTMax)>();

                foreach (var match in matches)
                {
                    // 转换为相对于 wall 段的局部参数
                    double localTMin = (match.TMin - wStartT) / (wEndT - wStartT);
                    double localTMax = (match.TMax - wStartT) / (wEndT - wStartT);

                    localTMin = Math.Max(0, Math.Min(1, localTMin));
                    localTMax = Math.Max(0, Math.Min(1, localTMax));

                    if ((localTMax - localTMin) * wLen < MinOverlapLength) continue;

                    candidates.Add((match.Opening, localTMin, localTMax));
                }

                if (candidates.Count == 0)
                {
                    result.Add(seg);
                    continue;
                }

                // 按 localTMin 排序后线性游标切割
                candidates.Sort((a, b) => a.localTMin.CompareTo(b.localTMin));

                double cursor = 0;
                foreach (var (opening, localTMin, localTMax) in candidates)
                {
                    if (localTMin > cursor + 1e-6)
                    {
                        // cursor → localTMin 是 wall
                        result.Add(IntervalToSegment(seg.Start, seg.End, cursor, localTMin, "wall", null));
                    }

                    // opening 段
                    string openingType = opening.Type == OpeningType.Door ? "door" : "window";
                    result.Add(IntervalToSegment(seg.Start, seg.End, localTMin, localTMax, openingType, opening.Id));

                    cursor = localTMax;
                }

                if (cursor < 1 - 1e-6)
                {
                    result.Add(IntervalToSegment(seg.Start, seg.End, cursor, 1, "wall", null));
                }
            }

            return result;
        }

        /// <summary>将点投影到边上，返回参数 t</summary>
        private double ProjectPointOntoEdge(
            Point2D point, Point2D edgeStart,
            double edgeDx, double edgeDy, double edgeLenSq)
        {
            return ((point.X - edgeStart.X) * edgeDx +
                    (point.Y - edgeStart.Y) * edgeDy) / edgeLenSq;
        }

        #endregion

        #region Phase D: 后处理

        /// <summary>
        /// 后处理：极短段合并 + 短 passage 修正 + 首尾闭合
        /// </summary>
        private List<BoundarySegment> PostProcess(List<BoundarySegment> segments)
        {
            if (segments.Count <= 1) return segments;

            // 规则 1: 极短段合并 (< 50mm)
            segments = MergeShortSegments(segments, MergeThreshold);

            // 规则 2: 短 passage 修正 (< 200mm)
            segments = FixShortPassages(segments, ShortPassageThreshold);

            // 规则 3: 共线同类型段合并
            segments = MergeCollinearSegments(segments);

            // 规则 4: 首尾闭合验证
            EnsureClosure(segments);

            return segments;
        }

        /// <summary>规则 1: 合并极短段</summary>
        private List<BoundarySegment> MergeShortSegments(List<BoundarySegment> segments, double threshold)
        {
            if (segments.Count <= 1) return segments;

            var result = new List<BoundarySegment>(segments);
            bool changed = true;

            while (changed)
            {
                changed = false;
                for (int i = result.Count - 1; i >= 0; i--)
                {
                    var seg = result[i];
                    double len = Distance(seg.Start, seg.End);
                    if (len >= threshold) continue;

                    // 找合并目标：相邻同类型段优先，否则合入较长侧
                    if (result.Count <= 1) break;

                    int mergeTarget;
                    if (i > 0 && i < result.Count - 1)
                    {
                        // 中间段：优先合入同类型邻居
                        var prev = result[i - 1];
                        var next = result[i + 1];
                        if (prev.Type == seg.Type)
                            mergeTarget = i - 1;
                        else if (next.Type == seg.Type)
                            mergeTarget = i + 1;
                        else
                            mergeTarget = Distance(prev.Start, prev.End) >= Distance(next.Start, next.End) ? i - 1 : i + 1;
                    }
                    else if (i == 0)
                    {
                        mergeTarget = 1;
                    }
                    else
                    {
                        mergeTarget = i - 1;
                    }

                    // 执行合并
                    if (mergeTarget < i)
                    {
                        result[mergeTarget] = new BoundarySegment
                        {
                            Id = result[mergeTarget].Id,
                            Type = result[mergeTarget].Type,
                            Start = result[mergeTarget].Start,
                            End = seg.End
                        };
                    }
                    else
                    {
                        result[mergeTarget] = new BoundarySegment
                        {
                            Id = result[mergeTarget].Id,
                            Type = result[mergeTarget].Type,
                            Start = seg.Start,
                            End = result[mergeTarget].End
                        };
                    }

                    result.RemoveAt(i);
                    changed = true;
                    break;  // 重新扫描
                }
            }

            return result;
        }

        /// <summary>规则 2: 修正短 passage 段</summary>
        private List<BoundarySegment> FixShortPassages(List<BoundarySegment> segments, double threshold)
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < segments.Count; i++)
                {
                    var seg = segments[i];
                    if (seg.Type != "passage") continue;

                    double len = Distance(seg.Start, seg.End);
                    if (len >= threshold) continue;

                    // 至少一侧相邻 wall → 合并为 wall
                    bool prevIsWall = i > 0 && segments[i - 1].Type == "wall";
                    bool nextIsWall = i < segments.Count - 1 && segments[i + 1].Type == "wall";

                    if (prevIsWall || nextIsWall)
                    {
                        segments[i] = new BoundarySegment
                        {
                            Id = null,
                            Type = "wall",
                            Start = seg.Start,
                            End = seg.End
                        };
                        changed = true;

                        // 尝试合并相邻 wall 段
                        segments = MergeAdjacentWalls(segments);
                        break;  // 重新扫描
                    }
                }
            }

            return segments;
        }

        /// <summary>合并相邻的共线 wall 段</summary>
        private List<BoundarySegment> MergeAdjacentWalls(List<BoundarySegment> segments)
        {
            var result = new List<BoundarySegment>();
            for (int i = 0; i < segments.Count; i++)
            {
                if (result.Count > 0
                    && result[result.Count - 1].Type == "wall"
                    && segments[i].Type == "wall"
                    && result[result.Count - 1].Id == null
                    && segments[i].Id == null)
                {
                    var prev = result[result.Count - 1];
                    var curr = segments[i];

                    // 共线性检查：防止合并方向不同的 wall 段（如转角处垂直段）
                    var prevDx = prev.End.X - prev.Start.X;
                    var prevDy = prev.End.Y - prev.Start.Y;
                    var currDx = curr.End.X - curr.Start.X;
                    var currDy = curr.End.Y - curr.Start.Y;
                    var prevLen = Math.Sqrt(prevDx * prevDx + prevDy * prevDy);
                    var currLen = Math.Sqrt(currDx * currDx + currDy * currDy);

                    bool isCollinear = false;
                    if (prevLen > 1e-6 && currLen > 1e-6)
                    {
                        var cross = Math.Abs(
                            (prevDx / prevLen) * (currDy / currLen) -
                            (prevDy / prevLen) * (currDx / currLen));
                        isCollinear = cross < ParallelTolerance;
                    }

                    if (isCollinear)
                    {
                        // 共线：合并，扩展前一段的 End
                        result[result.Count - 1] = new BoundarySegment
                        {
                            Id = null,
                            Type = "wall",
                            Start = prev.Start,
                            End = curr.End
                        };
                        continue;
                    }
                }

                result.Add(segments[i]);
            }
            return result;
        }

        /// <summary>规则 3: 合并相邻共线同类型段</summary>
        private List<BoundarySegment> MergeCollinearSegments(List<BoundarySegment> segments)
        {
            if (segments.Count <= 1) return segments;

            var result = new List<BoundarySegment> { segments[0] };

            for (int i = 1; i < segments.Count; i++)
            {
                var prev = result[result.Count - 1];
                var curr = segments[i];

                // 类型相同 + Id 相同 + 前段 End ≈ 后段 Start + 方向共线
                if (prev.Type == curr.Type
                    && prev.Id == curr.Id
                    && Distance(prev.End, curr.Start) < ClosureTolerance)
                {
                    // 共线检测：两段方向向量叉积 ≈ 0
                    var prevDx = prev.End.X - prev.Start.X;
                    var prevDy = prev.End.Y - prev.Start.Y;
                    var currDx = curr.End.X - curr.Start.X;
                    var currDy = curr.End.Y - curr.Start.Y;
                    var prevLen = Math.Sqrt(prevDx * prevDx + prevDy * prevDy);
                    var currLen = Math.Sqrt(currDx * currDx + currDy * currDy);

                    if (prevLen > 1e-6 && currLen > 1e-6)
                    {
                        var cross = Math.Abs(
                            (prevDx / prevLen) * (currDy / currLen) -
                            (prevDy / prevLen) * (currDx / currLen));

                        if (cross < ParallelTolerance)
                        {
                            // 合并：扩展前段的 End
                            result[result.Count - 1] = new BoundarySegment
                            {
                                Id = prev.Id,
                                Type = prev.Type,
                                Start = prev.Start,
                                End = curr.End
                            };
                            continue;
                        }
                    }
                }

                result.Add(curr);
            }

            return result;
        }

        /// <summary>规则 4: 首尾闭合验证</summary>
        private void EnsureClosure(List<BoundarySegment> segments)
        {
            if (segments.Count < 2) return;

            var first = segments[0];
            var last = segments[segments.Count - 1];

            if (Distance(last.End, first.Start) > ClosureTolerance)
            {
                segments[segments.Count - 1] = new BoundarySegment
                {
                    Id = last.Id,
                    Type = last.Type,
                    Start = last.Start,
                    End = first.Start
                };
            }
        }

        #endregion

        #region 工具方法

        /// <summary>参数区间 → BoundarySegment（线性插值坐标）</summary>
        private static BoundarySegment IntervalToSegment(
            Point2D edgeStart, Point2D edgeEnd,
            double tStart, double tEnd,
            string type, string id)
        {
            var dx = edgeEnd.X - edgeStart.X;
            var dy = edgeEnd.Y - edgeStart.Y;

            return new BoundarySegment
            {
                Id = id,
                Type = type,
                Start = new Point2D(
                    Math.Round(edgeStart.X + tStart * dx),
                    Math.Round(edgeStart.Y + tStart * dy)),
                End = new Point2D(
                    Math.Round(edgeStart.X + tEnd * dx),
                    Math.Round(edgeStart.Y + tEnd * dy))
            };
        }

        /// <summary>两点间距离</summary>
        private static double Distance(Point2D a, Point2D b)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        #endregion
    }
}
