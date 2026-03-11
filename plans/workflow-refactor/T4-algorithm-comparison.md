# T4 算法对比分析：T4-boundarySegments vs EdgeLoopExtractor

> 对比 BIMCanvas T4 算法与 AIInteriorPlanner EdgeLoopExtractor 的设计差异，取长补短。

---

## 一、问题域差异

两个算法解决的核心问题**不同**，但在"将多边形边切割为语义段"这一步骤上**高度相似**。

| 维度 | EdgeLoopExtractor | T4 boundarySegments |
|------|-------------------|---------------------|
| **作用层级** | 房间级（单层多边形） | 子分区级（父子多边形关系） |
| **输入** | 房间轮廓 + 已投影的构件 | Zone 树 + 未投影的 openings |
| **核心问题** | 墙段 vs 门窗的切割 | **墙 vs 通道**的分类（门窗是次要问题） |
| **坐标精度** | 高（Revit 精确导出） | 低（子 zone 是 Agent 四舍五入的坐标） |
| **构件位置** | 已投影到轮廓边上（精确） | 在墙中线，离 zone 边有 50-100mm 偏移 |
| **额外输出** | Corner（凸/凹角）、Group | 无 |

**结论**：T4 的 wall/passage 分类是 EdgeLoopExtractor 完全不涉及的独有难题。而门窗切割部分，两者算法思路趋同。

---

## 二、逐环节对比

### 2.1 参数化定位

**EdgeLoopExtractor** — 主轴分量法：

```csharp
// 选择变化量大的轴，避免除以接近零的值
if (|dx| > |dy|) t = (p.X - P0.X) / dx;
else             t = (p.Y - P0.Y) / dy;
return clamp(t, 0, 1);
```

**T4** — 点积投影法：

```csharp
t = ((p.X - E.P0.X) * edgeDx + (p.Y - E.P0.Y) * edgeDy) / edgeLenSq;
```

| | 主轴分量法 | 点积投影法 |
|--|-----------|-----------|
| 正交边（水平/垂直） | ✅ 精确 | ✅ 精确 |
| 非正交边（斜边） | ❌ 投影偏差 | ✅ 精确（向量投影） |
| 点不在直线上时 | ❌ 结果无意义 | ✅ 给出正交投影参数 |
| 实现复杂度 | 更简单 | 略复杂 |

**结论**：T4 的点积法更通用。虽然 BIM 数据多为正交墙，但斜墙确实存在。此外，T4 场景中 opening 不在 zone 边界上（有偏移），需要正交投影到直线上，主轴分量法在这种场景下结果不正确。

**✅ 保留 T4 方案**。

---

### 2.2 构件匹配策略

**EdgeLoopExtractor** — 预投影 + 精确匹配（两阶段分离）：

```
阶段 1（上游，平台特有）：
  每个构件 → 投影到最近的轮廓边 → ComponentInfo.Location 精确在边上

阶段 2（EdgeLoopExtractor 内部）：
  对每条轮廓边，用 IsPointOnSegment(容差 10mm) 找到在该边上的构件
  → 无需距离检测，因为构件已经在边上
```

**T4** — 容差匹配 + 内联投影（单步完成）：

```
对每个 wall 段 W：
  for each opening O:
    垂直距离检测（O.Line 中点到 W 直线 < 150mm）
    投影 O 端点到 W
    → 距离检测 + 投影 在同一步完成
```

**优劣分析**：

| | EdgeLoopExtractor | T4 |
|--|-------------------|-----|
| 架构清晰度 | ✅ 关注点分离（投影是独立步骤） | ⚠️ 匹配与投影耦合在一起 |
| 适用场景 | 构件位置精确，只需小容差匹配 | 构件位置有偏移，需要大容差 |
| 错误匹配风险 | 低（10mm 容差） | 中等（150mm 容差可能匹配到相邻平行墙上的开口） |

**T4 的隐患**：150mm 容差在门窗匹配时可能过大。如果两面平行墙间距 < 300mm（如走廊墙），一个门可能同时匹配到两面墙。

**改进方案**：借鉴 EdgeLoopExtractor 的**两阶段思路**，但适配 T4 场景：

```
阶段 1（预匹配）：
  对每个 opening O，找到垂直距离最近的 zone 边 E
  将 O 投影到 E 上 → 得到 projectedLocation
  → 每个 opening 只归属一条边（最近的那条）

阶段 2（切割）：
  对每条边，用已归属的 openings 做精确切割（容差降到 10mm）
```

**优势**：
- 每个 opening 只匹配一条边（最近原则），消除重复匹配
- 切割阶段容差极小，精度高
- 架构更清晰

**✅ 采纳 EdgeLoopExtractor 的两阶段分离思路**。

---

### 2.3 切割算法

**EdgeLoopExtractor** — 切割点法：

```
1. 收集切割点（每个构件的两个端点，标记 IsStart/IsEnd + 归属构件）
2. 按参数排序
3. 遍历相邻切割点对：
   - 同一构件的 Start→End → 标记为该构件类型
   - 不同构件之间的间隙 → 标记为 Wall
```

**T4** — 区间法：

```
1. 每个 opening → 一个区间 [tMin, tMax]
2. 按 tMin 排序
3. 线性游标遍历：
   - 游标到区间开始 → Wall 段
   - 区间 → Opening 段
   - 区间结束更新游标
```

**对比**：

| | 切割点法 | 区间法 |
|--|---------|--------|
| 概念模型 | 切割点 + 配对 | 区间 + 游标 |
| 处理重叠构件 | 需要额外的配对逻辑 | 天然处理（区间覆盖） |
| 代码复杂度 | 中等（需要 IsStart/IsEnd 跟踪） | 较低（游标即可） |
| 可读性 | 高（切割直观） | 高（区间直观） |

**实质等价**：两种方法产出完全相同的结果。在 T4 场景下，由于 opening 已经以区间形式存在（来自 FindOverlapRange），区间法更自然，无需额外转换为切割点。

**✅ 保留 T4 的区间法**（与上游 FindOverlapRange 一致）。

---

### 2.4 特有功能

#### EdgeLoopExtractor 的 Corner 信息

在每两条边段之间插入 Corner 段（标记凸角/凹角）。

```
Wall → [Corner:Convex] → Wall → [Corner:Convex] → Wall+Door+Wall → [Corner:Convex] → Wall
```

**对 T4 的价值**：
- Agent 理解 L 形/U 形房间时，凹角位置是有用的空间信息
- 但 T4 的核心目标是区分 wall/passage，Corner 是**锦上添花**
- BoundarySegment 已定义为 4 种类型（wall/passage/door/window），增加 corner 需要扩展类型

**决策**：暂不采纳。可作为未来增强。理由：
1. T4 当前目标明确（解决 passage 误判），不宜扩大范围
2. Corner 信息可以由 Agent 从 rawBoundary 顶点自行推断
3. 引入 Corner 会使 segments 数量翻倍，增加 Agent 的注意力负担

#### EdgeLoopExtractor 的 Group 合并

将 `[短墙][门][短墙]` 合并为 `[Group(type=Door)]`。

**对 T4 的价值**：无。T4 需要精确的边界段信息供 Agent 做家具布置决策，Group 反而丢失了精度。

---

### 2.5 输入验证

**EdgeLoopExtractor**：

```csharp
// 入口处验证轮廓闭合性
IsLoopValid(outerLoop);  // 检查首尾相连
```

**T4**：仅在后处理中验证首尾闭合。

**改进**：应在入口处增加验证：

```
1. Zone.RawBoundary 顶点数 ≥ 3
2. 首尾顶点距离 < 1mm（或 Polygon2D 隐式闭合则跳过）
3. 无退化边（连续两顶点距离 > 1mm）
```

**✅ 采纳 EdgeLoopExtractor 的前置验证思路**。

---

## 三、总结：取长补短

### 从 EdgeLoopExtractor 采纳的改进

| 改进 | 具体内容 | 价值 |
|------|---------|------|
| **Opening 预归属** | 每个 opening 先匹配到最近的 zone 边（150mm 容差），再精确切割（10mm 容差） | 消除重复匹配、提高精度 |
| **前置输入验证** | 在算法入口处验证多边形闭合性、退化边 | 提早暴露数据问题 |

### T4 算法保持不变的部分

| 部分 | 理由 |
|------|------|
| **FindOverlapRange** 近共线重叠检测 | EdgeLoopExtractor 无此需求，T4 独有 |
| **"非 wall 即 passage"** 分类策略 | EdgeLoopExtractor 无此需求，T4 独有 |
| **点积投影法** | 比主轴分量法更通用，适用于斜墙和偏移投影 |
| **区间法切割** | 与上游区间计算一致，无需转换为切割点 |
| **后处理规则** | EdgeLoopExtractor 不需要（数据精确），T4 必须有（数据近似） |

### 不采纳的部分

| 部分 | 理由 |
|------|------|
| Corner 信息 | 增加复杂度，偏离 T4 核心目标，Agent 可自行推断 |
| Group 合并 | 丢失精度，与 T4 目标冲突 |
| 主轴分量法 | 不如点积法通用 |

---

## 四、改进后的 T4 算法流程

```
输入：allZones（含 subZones）, openings
输出：List<ZoneBoundaryData>

对每个叶子 zone Z：
  parent, siblings = 解析层级关系

  // ===== 前置验证（来自 EdgeLoopExtractor）=====
  ValidatePolygon(Z.RawBoundary)

  // ===== Phase A: Opening 预归属（来自 EdgeLoopExtractor 的分离思路）=====
  openingsByEdge = PreMatchOpeningsToEdges(Z.RawBoundary, openings, tolerance=150mm)
  // 每个 opening 只归属距离最近的一条边

  // ===== Phase B: Wall/Passage 分类（T4 独有）=====
  对 Z.rawBoundary 的每条边 E：
    wallIntervals = FindOverlapRanges(E, parent.RawBoundary)
    passageIntervals = Complement([0,1], wallIntervals)
    AssignPassageIds(passageIntervals, siblings)
    segments += ToSegments(E, wallIntervals, passageIntervals)

  // ===== Phase C: 门窗精确切割（融合两者优点）=====
  对 segments 中每个 wall 段 W：
    matchedOpenings = openingsByEdge[W.所在边]  // 预归属的结果
    SplitByProjectedOpenings(W, matchedOpenings, tolerance=10mm)
    // ↑ 容差降到 10mm，因为已经确认了归属关系

  // ===== Phase D: 后处理（T4 独有）=====
  PostProcess(segments)  // 极短段合并、短 passage 修正、闭合验证
```

### PreMatchOpeningsToEdges 详解

```
输入：polygon（zone 边界）, openings（所有门窗）
输出：Dict<edgeIndex, List<(Opening, projectedTMin, projectedTMax)>>

for each opening O:
  bestEdgeIndex = -1
  bestPerpDist = +∞

  for each edge E (index i) of polygon:
    // 平行检测
    if not IsParallel(O.Line, E): continue

    // 垂直距离
    perpDist = PerpendicularDistance(O.Line.Midpoint, E)
    if perpDist > 150mm: continue

    if perpDist < bestPerpDist:
      bestPerpDist = perpDist
      bestEdgeIndex = i

  if bestEdgeIndex >= 0:
    // 投影 opening 端点到最佳边
    E = edges[bestEdgeIndex]
    tMin, tMax = ProjectAndClamp(O.Line, E)
    openingsByEdge[bestEdgeIndex].Add((O, tMin, tMax))
```

**关键改进**：每个 opening 只归属一条边（最近的平行边），消除了 150mm 大容差下的重复匹配风险。切割阶段用 10mm 小容差确保精度。

---

## 五、方法清单（更新后）

```
public:
  List<ZoneBoundaryData> CalculateZoneBoundarySegments(List<Zone> allZones, List<Opening> openings)

private:
  // 前置验证
  void ValidatePolygon(Polygon2D boundary)

  // Phase A: Opening 预归属
  Dictionary<int, List<OpeningMatch>> PreMatchOpeningsToEdges(
      Polygon2D boundary, List<Opening> openings, double tolerance)

  // Phase B: Wall/Passage 分类
  List<BoundarySegment> ProcessLeafZone(Zone leaf, Zone parent, List<Zone> siblings,
      Dictionary<int, List<OpeningMatch>> openingsByEdge)
  List<BoundarySegment> ClassifyEdge(Point2D edgeStart, Point2D edgeEnd,
      Point2D[] parentVertices, Dictionary<string, Point2D[]> siblingVerticesMap)
  (double tMin, double tMax)? FindOverlapRange(Point2D eStart, Point2D eEnd,
      Point2D rStart, Point2D rEnd, double tolerance)
  List<(double, double)> MergeOverlappingIntervals(List<(double, double)> intervals)
  List<(double, double)> ComputeComplement(List<(double, double)> wallIntervals)

  // Phase C: 门窗切割
  List<BoundarySegment> SplitWallsByOpenings(List<BoundarySegment> segments,
      Dictionary<int, List<OpeningMatch>> openingsByEdge)

  // Phase D: 后处理
  List<BoundarySegment> PostProcess(List<BoundarySegment> segments)
```
