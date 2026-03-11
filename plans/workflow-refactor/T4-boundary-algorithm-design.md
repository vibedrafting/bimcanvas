# T4: boundarySegments 算法详细设计

> 基于 T4-zone-boundary-segments.md 的需求，详细描述 `ZoneBoundaryService` 的算法实现方案。
> 融合了 AIInteriorPlanner EdgeLoopExtractor 的"预归属 + 精确切割"思路。

**前置**：T4-zone-boundary-segments.md（需求与数据模型定义）
**参考**：AIInteriorPlanner EdgeLoopExtractor（门窗两阶段分离设计）
**状态**：算法设计完成，待实现

---

## 一、数据结构（不变，来自 T4 需求文档）

```csharp
public class BoundarySegment
{
    public string? Id { get; set; }    // 门窗 ID（d_5/wi_5）或相邻 zone ID（dz_1），wall 时为 null
    public string Type { get; set; }   // "wall" | "passage" | "door" | "window"
    public Point2D Start { get; set; }
    public Point2D End { get; set; }
}

public class ZoneBoundaryData
{
    public string ZoneId { get; set; }
    public List<BoundarySegment> Segments { get; set; } = new();
}
```

---

## 二、算法总览

```
输入：allZones（含 subZones）, openings
输出：List<ZoneBoundaryData>

对每个叶子 zone Z：
  parent = Z 的父 zone（room zone）
  siblings = 同父的兄弟叶子 zone

  Phase A: Opening 预归属 — 每个 opening 绑定到最近的 zone 边
  Phase B: Wall/Passage 分类 — 每条边和 parent 比对
  Phase C: 门窗精确切割 — 只切割预归属到该边的 opening
  Phase D: 后处理 — 极短段合并 + 短 passage 修正 + 闭合验证
```

### 分类策略："非 wall 即 passage"

先用 parent 匹配确定 wall，剩余部分全部标为 passage，再用 sibling 匹配确定 passage.Id。

**理由**：子 zone 之间边界不一定完全对齐（如 dz_2 东边 y=5350→5800 与 dz_1 存在 450mm 间隙），这种间隙物理上不是墙。"非 wall 即 passage" 正确处理了这种间隙，通过短段修正规则处理坐标偏移导致的伪 passage。

---

## 三、Phase A：Opening 预归属

> 来自 EdgeLoopExtractor 的"预投影"思路：先把 opening 分配到 zone 边上，后续切割只用小容差。

### `PreMatchOpeningsToEdges(boundary, openings, tolerance=150mm)`

```
输入：zone 边界多边形, 所有 openings
输出：Dict<edgeIndex, List<(Opening, tMin, tMax)>>

for each opening O:
  bestEdgeIndex = -1
  bestPerpDist = +∞

  for each edge E (index i) of zone boundary:
    // 平行检测：opening 方向和边方向必须接近平行
    if not IsParallel(O.Line, E): continue

    // 垂直距离：opening 中点到边所在直线的垂距
    perpDist = PerpendicularDistanceToInfiniteLine(O.Line.Midpoint, E)
    if perpDist > tolerance: continue

    // 取距离最近的边
    if perpDist < bestPerpDist:
      bestPerpDist = perpDist
      bestEdgeIndex = i

  if bestEdgeIndex >= 0:
    E = edges[bestEdgeIndex]
    (tMin, tMax) = ProjectOpeningOntoEdge(O.Line, E)
    openingsByEdge[bestEdgeIndex].Add((O, tMin, tMax))
```

**关键设计**：
- **每个 opening 只归属一条边**（取垂距最小的），消除了 150mm 大容差下的重复匹配风险
- 这一步完成后，后续切割阶段不再需要大容差搜索

---

## 四、Phase B：Wall/Passage 分类

### 核心子算法：`FindOverlapRange(E, R, tolerance=150mm)`

给定叶子 zone 的边 E（参数化为 [0,1]）和参考多边形的边 R，返回 E 上与 R 近共线重叠的参数区间。

```csharp
static (double tMin, double tMax)? FindOverlapRange(
    Point2D edgeStart, Point2D edgeEnd,
    Point2D refStart, Point2D refEnd,
    double tolerance = 150.0)
{
    // --- Step 1: 平行检测 ---
    double edgeDx = edgeEnd.X - edgeStart.X;
    double edgeDy = edgeEnd.Y - edgeStart.Y;
    double edgeLen = Math.Sqrt(edgeDx * edgeDx + edgeDy * edgeDy);
    if (edgeLen < 1e-6) return null;

    double refDx = refEnd.X - refStart.X;
    double refDy = refEnd.Y - refStart.Y;
    double refLen = Math.Sqrt(refDx * refDx + refDy * refDy);
    if (refLen < 1e-6) return null;

    double eDirX = edgeDx / edgeLen, eDirY = edgeDy / edgeLen;
    double rDirX = refDx / refLen, rDirY = refDy / refLen;

    // |sin(θ)| < 0.05 ≈ 3°
    double cross = Math.Abs(eDirX * rDirY - eDirY * rDirX);
    if (cross > 0.05) return null;

    // --- Step 2: 垂直距离（到 E 无限延长线的距离） ---
    double dx = refStart.X - edgeStart.X;
    double dy = refStart.Y - edgeStart.Y;
    double perpDist = Math.Abs(eDirX * dy - eDirY * dx);
    if (perpDist > tolerance) return null;

    // --- Step 3: 参数化投影 ---
    double edgeLenSq = edgeDx * edgeDx + edgeDy * edgeDy;
    double t0 = ((refStart.X - edgeStart.X) * edgeDx +
                 (refStart.Y - edgeStart.Y) * edgeDy) / edgeLenSq;
    double t1 = ((refEnd.X - edgeStart.X) * edgeDx +
                 (refEnd.Y - edgeStart.Y) * edgeDy) / edgeLenSq;

    double tMin = Math.Max(0, Math.Min(t0, t1));
    double tMax = Math.Min(1, Math.Max(t0, t1));

    if ((tMax - tMin) * edgeLen < 10.0) return null; // 最小有效重叠 10mm

    return (tMin, tMax);
}
```

### 设计要点

| 要点 | 说明 |
|------|------|
| **垂直距离用无限延长线** | 避免 NTS `LineSegment.Distance` 的端点 clamp，否则不重叠时距离虚高 |
| **150mm 容差** | 覆盖 Revit 导出偏移（~50-100mm）+ Agent 四舍五入误差 |
| **平行检测先行** | 叉积 < 0.05（约 3°）快速排除不平行边 |
| **点积投影法** | 比主轴分量法更通用，支持斜墙和偏移投影 |

### Wall/Passage 分类流程：`ClassifyEdge`

```
对 Z.rawBoundary 的每条边 E：

  // Step 1: 收集 wall 区间
  wallIntervals = []
  for each parentEdge in parent.RawBoundary:
    range = FindOverlapRange(E, parentEdge, tolerance=150)
    if range != null: wallIntervals.Add(range)

  wallIntervals = MergeOverlappingIntervals(wallIntervals)

  // Step 2: 补集 = passage 区间
  passageIntervals = ComputeComplement([0,1], wallIntervals)

  // Step 3: 为每个 passage 区间匹配最近 sibling
  for each passageInterval:
    bestSiblingId = null, bestOverlap = 0
    for each (siblingId, siblingEdges) in siblings:
      for each siblingEdge:
        range = FindOverlapRange(E, siblingEdge, tolerance=150)
        if range != null:
          交集 = range ∩ passageInterval
          if 交集长度 > bestOverlap:
            bestOverlap = 交集长度
            bestSiblingId = siblingId
    passageInterval.Id = bestSiblingId  // 可能为 null
```

### 区间工具函数

```csharp
/// 合并重叠区间
static List<(double start, double end)> MergeOverlappingIntervals(
    List<(double start, double end)> intervals)
{
    if (intervals.Count == 0) return new();
    var sorted = intervals.OrderBy(i => i.start).ToList();
    var merged = new List<(double, double)>();
    var current = sorted[0];
    for (int i = 1; i < sorted.Count; i++)
    {
        if (sorted[i].start <= current.end + 1e-6)
            current.end = Math.Max(current.end, sorted[i].end);
        else { merged.Add(current); current = sorted[i]; }
    }
    merged.Add(current);
    return merged;
}

/// 计算 [0,1] 的补集
static List<(double start, double end)> ComputeComplement(
    List<(double start, double end)> intervals)
{
    var result = new List<(double, double)>();
    double cursor = 0.0;
    foreach (var (start, end) in intervals)
    {
        if (start > cursor + 1e-6) result.Add((cursor, start));
        cursor = Math.Max(cursor, end);
    }
    if (cursor < 1.0 - 1e-6) result.Add((cursor, 1.0));
    return result;
}
```

---

## 五、Phase C：门窗精确切割

> 融合了 EdgeLoopExtractor 的两阶段思路：Phase A 已完成预归属，这里只做精确切割。

### `SplitWallsByOpenings(segments, openingsByEdge)`

```
对 segments 中每个 type="wall" 的段 W（已知它在第 i 条边上）：
  matchedOpenings = openingsByEdge[i]  // Phase A 预归属的结果
  if matchedOpenings 为空: 跳过

  // 筛选落在当前 wall 段参数范围内的 openings
  candidates = []
  for each (opening, tMin, tMax) in matchedOpenings:
    // tMin/tMax 是相对于整条边 E 的参数
    // 需要转换为相对于 wall 段 W 的参数
    wStart_t, wEnd_t = W 在 E 上的参数范围
    localTMin = (tMin - wStart_t) / (wEnd_t - wStart_t)
    localTMax = (tMax - wStart_t) / (wEnd_t - wStart_t)
    localTMin = Clamp(localTMin, 0, 1)
    localTMax = Clamp(localTMax, 0, 1)
    if (localTMax - localTMin) * W.Length < 10mm: continue
    candidates.Add((opening, localTMin, localTMax))

  sort candidates by localTMin

  // 线性游标切割
  result = [], cursor = 0
  for each (O, localTMin, localTMax) in candidates:
    if localTMin > cursor + ε:
      result.Add(wall [cursor, localTMin])
    openingType = O.Type == Door ? "door" : "window"
    result.Add({ type: openingType, id: O.Id, [localTMin, localTMax] })
    cursor = localTMax

  if cursor < 1 - ε:
    result.Add(wall [cursor, 1])

  用 result 替换原 wall 段 W
```

### Opening ID 映射

| Opening.Type | BoundarySegment.Type | BoundarySegment.Id |
|-------------|---------------------|-------------------|
| Door | `"door"` | Opening.Id（如 `"d_5"`） |
| Window | `"window"` | Opening.Id（如 `"wi_5"`） |

---

## 六、Phase D：后处理

在所有边的 segments 收集完成后，顺序执行：

### 规则 1：极短段合并（< 50mm）

浮点精度导致的微小段，合并入相邻同类型段。如果左右类型不同，合入较长的一侧。

### 规则 2：短 passage 修正（< 200mm）

如果一个 passage 段长度 < 200mm，且至少一侧相邻 wall 段 → 合并为 wall。

**原因**：parent 坐标偏移（如 x=12000 vs x=12100）可能导致边界末端 ~100mm 不匹配，被"非 wall 即 passage"错误标为 passage。200mm 阈值安全覆盖这种偏移。

### 规则 3：首尾闭合验证

最后一段的 End 应与第一段的 Start 距离 < 1mm。如果不满足，调整最后一段的 End 强制闭合。

---

## 七、金凤143 完整验证

### dz_1 `[[12100,5800],[15500,5800],[15500,10700],[12100,10700]]`

#### Phase A：Opening 预归属

| Opening | 最近边 | 垂距 | 投影参数 |
|---------|--------|------|---------|
| wi_5 ≈ (14800,10700)→(12800,10700) | 边3: (15500,10700)→(12100,10700) | 0mm | t=[0.206, 0.794] |

其他 opening（d_5, d_6）离 dz_1 边界太远（>150mm），不归属。

#### Phase B + C：逐边处理

**边1 南边 (12100,5800)→(15500,5800)**

| 步骤 | 操作 | 结果 |
|------|------|------|
| B | parent 两段南墙均匹配，合并 wall [0, 1.0] | **wall** ✅ |

**边2 东边 (15500,5800)→(15500,10700)**

| 步骤 | 操作 | 结果 |
|------|------|------|
| B | parent 边完全匹配 [0, 1.0] | **wall** ✅ |

**边3 北边 (15500,10700)→(12100,10700)**

| 步骤 | 操作 | 结果 |
|------|------|------|
| B | parent 边完全匹配 [0, 1.0] | wall |
| C | wi_5 预归属到此边，投影 t=[0.206, 0.794] | 拆分 |
| 输出 | wall[0,0.206] + window[0.206,0.794] + wall[0.794,1.0] | **wall + wi_5(window) + wall** ✅ |

**边4 西边 (12100,10700)→(12100,5800)**

| 步骤 | 操作 | 结果 |
|------|------|------|
| B | parent 边匹配 [0, 0.592]，补集 [0.592, 1.0] = passage | |
| B | sibling dz_2 匹配 passage 区间 → Id="dz_2" | |
| C | 无 opening 归属到此边 | 不拆分 |
| 输出 | wall(10700→7800) + passage(7800→5800, →dz_2) | ✅ |

### dz_2 `[[9500,5350],[12100,5350],[12100,7800],[9500,7800]]`

#### Phase A：Opening 预归属

| Opening | 最近边 | 垂距 | 投影参数 |
|---------|--------|------|---------|
| d_6 ≈ (11070,5350)→(11820,5350) | 边1: (9500,5350)→(12100,5350) | 0mm | t=[0.604, 0.892] |
| d_5 ≈ (10960,7800)→(10260,7800) | 边3: (12100,7800)→(9500,7800) | 0mm | t=[0.438, 0.708] |

#### Phase B + C：逐边处理

**边1 南边 (9500,5350)→(12100,5350)**

| 步骤 | 操作 | 结果 |
|------|------|------|
| B | parent 匹配 [0, 0.962]，补集 [0.962, 1.0]=100mm | passage |
| D | 后处理：100mm < 200mm 且相邻 wall → 合并为 wall | wall [0, 1.0] |
| C | d_6 预归属到此边，投影 t=[0.604, 0.892] | 拆分 |
| 输出 | wall + d_6(door) + wall | ✅ |

**边2 东边 (12100,5350)→(12100,7800)**

| 步骤 | 操作 | 结果 |
|------|------|------|
| B | parent 无匹配，补集 = 全部 passage | |
| B | sibling dz_1 匹配 [0.184, 1.0] → Id="dz_1" | |
| C | 无 opening 归属到此边 | 不拆分 |
| 输出 | passage(→dz_1) | ✅ |

**边3 北边 (12100,7800)→(9500,7800)**

| 步骤 | 操作 | 结果 |
|------|------|------|
| B | parent 完全匹配 [0, 1.0] | wall |
| C | d_5 预归属到此边，投影 t=[0.438, 0.708] | 拆分 |
| 输出 | wall + d_5(door) + wall | ✅ |

**边4 西边 (9500,7800)→(9500,5350)**

| 步骤 | 操作 | 结果 |
|------|------|------|
| B | parent 完全匹配 [0, 1.0] | wall |
| C | 无 opening 归属 | 不拆分 |
| 输出 | wall | ✅ |

---

## 八、边界情况与容错

| 场景 | 处理 |
|------|------|
| 退化边（长度 ≈ 0） | FindOverlapRange 返回 null，跳过 |
| Zone 没有 parent（room zone 自身） | 所有边默认 wall，只做门窗切割 |
| Zone 没有 sibling | 非 parent 部分标 passage(id=null) |
| 同一 opening 匹配到多个 zone | Phase A 只为当前 zone 匹配，不冲突 |
| parent 边与子 zone 边方向相反 | FindOverlapRange 对方向不敏感（排序 tMin/tMax） |
| 浮点精度导致微小间隙 | 后处理规则 1 合并 < 50mm 段 |

---

## 九、可复用的已有代码

| 需求 | 方法 | 位置 |
|------|------|------|
| NTS 类型转换 | `NtsConverter.ToNtsLineSegment()` | `Core/Converters/NtsConverter.cs` |
| 平行检测 | `NtsGeometryHelper.IsParallel()` | `Core/Algorithms/Geometries/NtsGeometryHelper.cs` |
| 多边形边遍历 | `NtsGeometryHelper.GetPolygonLines()` | 同上 |
| 投影因子 | `LineSegment.ProjectionFactor()` | NTS 内置 |
| 点沿线段插值 | `LineSegment.PointAlong()` | NTS 内置 |

---

## 十、实施文件清单

| 层 | 文件 | 操作 | 内容 |
|----|------|------|------|
| **Core** | `Models/Computed/BoundarySegment.cs` | 新建 | BoundarySegment 数据类（4 字段） |
| **Core** | `Models/Computed/ZoneBoundaryData.cs` | 新建 | ZoneBoundaryData 包装类 |
| **Core** | `BIMCanvas.Core.csproj` | 修改 | 添加新文件引用 |
| **Server** | `Services/ZoneBoundaryService.cs` | 新建 | 核心算法实现 |
| **Server** | MCP Controller | 修改 | get_zone_boundaries 端点 |

### 算法方法清单（ZoneBoundaryService.cs）

```
public:
  List<ZoneBoundaryData> CalculateZoneBoundarySegments(List<Zone> allZones, List<Opening> openings)

private:
  // Phase A: Opening 预归属
  Dictionary<int, List<OpeningMatch>> PreMatchOpeningsToEdges(
      Point2D[] vertices, List<Opening> openings, double tolerance)

  // Phase B: Wall/Passage 分类
  List<BoundarySegment> ProcessLeafZone(Zone leaf, Zone parent, List<Zone> siblings,
      Dictionary<int, List<OpeningMatch>> openingsByEdge)
  List<TaggedInterval> ClassifyEdge(Point2D edgeStart, Point2D edgeEnd,
      Point2D[] parentVertices, Dictionary<string, Point2D[]> siblingVerticesMap)
  (double tMin, double tMax)? FindOverlapRange(Point2D eStart, Point2D eEnd,
      Point2D rStart, Point2D rEnd, double tolerance)
  List<(double, double)> MergeOverlappingIntervals(List<(double, double)> intervals)
  List<(double, double)> ComputeComplement(List<(double, double)> wallIntervals)

  // Phase C: 门窗切割
  List<BoundarySegment> SplitWallsByOpenings(List<BoundarySegment> segments,
      int edgeIndex, Dictionary<int, List<OpeningMatch>> openingsByEdge)

  // Phase D: 后处理
  List<BoundarySegment> PostProcess(List<BoundarySegment> segments)

  // 工具
  BoundarySegment IntervalToSegment(Point2D edgeStart, Point2D edgeEnd,
      double tStart, double tEnd, string type, string? id)
```

---

## 十一、验证方案

1. **编译**：`dotnet build BIMCanvas.Core --no-restore` + `dotnet build BIMCanvas.Server --no-restore`
2. **单元验证**：用金凤143 数据调用 `CalculateZoneBoundarySegments()`，对比第七节的期望输出
3. **MCP 端到端**：通过 `get_zone_boundaries` 工具调用，验证 JSON 输出格式
4. **关键检查点**：
   - dz_1 西边 → wall(10700→7800) + passage(7800→5800, →dz_2)
   - dz_2 东边 → 整体 passage(→dz_1)
   - dz_2 南墙 → wall + d_6(door) + wall
   - dz_2 北墙 → wall + d_5(door) + wall
   - dz_1 北墙 → wall + wi_5(window) + wall

---

## 附录：算法来源说明

| 模块 | 来源 | 说明 |
|------|------|------|
| Phase A: Opening 预归属 | EdgeLoopExtractor 启发 | 借鉴"预投影 + 精确切割"的两阶段分离思想 |
| Phase B: Wall/Passage 分类 | T4 原创 | EdgeLoopExtractor 无此能力 |
| Phase B: FindOverlapRange | T4 原创 | 近共线重叠检测（平行+垂距+参数化投影） |
| Phase B: 点积投影法 | T4 原创 | 比 EdgeLoopExtractor 的主轴分量法更通用 |
| Phase C: 线性游标切割 | 两者共有 | T4 用区间法，EdgeLoopExtractor 用切割点法，实质等价 |
| Phase D: 后处理 | T4 原创 | EdgeLoopExtractor 输入精确无需后处理 |
