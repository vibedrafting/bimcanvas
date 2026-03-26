# T4: Zone 边界语义化 — boundarySegments

> 为 Zone 添加边界段类型描述（wall/passage/door/window），解决 Agent 无法区分墙和通道的问题。

**前置任务**：T3-zoning-architecture（分区架构已完成）
**参考**：AIInteriorPlanner EdgeLoopExtractor（门窗两阶段分离设计）
**状态**：✅ 核心实现完成，已通过金凤143实测验证

---

## 实施进度

| 步骤 | 状态 | 说明 |
|------|------|------|
| Core 层数据模型 | ✅ 完成 | BoundarySegment.cs + ZoneBoundaryData.cs |
| Server 算法 | ✅ 完成 | ZoneBoundaryService.cs（四阶段算法） |
| Server 端点 | ✅ 完成 | `POST /api/validation/zone-boundaries` |
| Agent MCP 工具 | ✅ 完成 | `mcp__canvas__get_zone_boundaries` |
| 金凤143 实测 | ✅ 通过 | rz_2 测试：门窗匹配准确，wall 分类正确 |
| 坐标精度优化 | ✅ 完成 | 输出坐标四舍五入到整数 mm |
| 共线段合并优化 | ✅ 完成 | 相邻共线同类型段自动合并 |
| Web 调试面板可视化 | ✅ 完成 | BoundaryDebugPanel：Zone 绿色填充 + 虚线边框 + 构件外扩 |
| Web 调试面板 Zone 属性 | ✅ 完成 | 选中 Zone 时显示完整属性（ID/name/roomId/type/reason/parentZoneId） |
| Web 调试面板重复打开 | ✅ 完成 | 修复 disposeThree 未置空变量导致 watch 竞态 |
| Web 主属性面板子分区 | ✅ 完成 | findObjectById 增加嵌套 subZones 搜索 + sceneDataCache 兼容 userData |
| Skill 提示词调整 | ✅ 完成 | T5 完成：workflow/bedroom/zoning/livingroom/bathroom 全部集成 boundarySegments |
| MCP 输出格式优化 | ✅ 完成 | T5 完成：_format_zone_boundaries 重构为按墙面分组+方位标签+实墙摘要 |
| dz_1/dz_2 端到端验证 | 🔶 待做 | 验证子分区 passage 分类 + Agent 布置行为 |

---

## Context

### 问题根源

Zone 的 `rawBoundary` 只描述多边形形状，不描述每条边的物理含义。子分区场景下，Agent 无法区分墙和通道。

**金凤143主卧实例**：dz_1 西边 x=12100 被 Agent 当作"西墙"放了 4200mm 衣柜，实际 y=5800→7800 段是通向 dz_2 的开放通道。dz_2 东边同理放了展示柜。

### 用户决策
1. 混合边 → Server 自动拆分
2. 交付方式 → MCP 工具按需计算（不存文件）
3. 范围 → 一步到位（wall/passage + 门窗标注）

---

## 一、数据模型（Core 层）

### `BIMCanvas.Core/Models/Computed/BoundarySegment.cs`

```csharp
public class BoundarySegment
{
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string Id { get; set; }       // door/window: Opening.Id, passage: sibling zone Id, wall: null(省略)

    public string Type { get; set; } = "wall";  // "wall" | "passage" | "door" | "window"
    public Point2D Start { get; set; }
    public Point2D End { get; set; }
}
```

### `BIMCanvas.Core/Models/Computed/ZoneBoundaryData.cs`

```csharp
public class ZoneBoundaryData
{
    public string ZoneId { get; set; } = string.Empty;
    public List<BoundarySegment> Segments { get; set; } = new List<BoundarySegment>();
}
```

**设计要点**：
- 4 字段、每段自包含、有序闭合
- 门窗从 wall 段中拆出成独立段（不是嵌套在 wall 的 openings 字段里）
- 不修改 Zone.cs：boundarySegments 是派生计算数据
- wall 段 Id 为 null，JSON 中省略（`NullValueHandling.Ignore`）
- 坐标四舍五入到整数 mm（消除 Revit 导出浮点噪声）

---

## 二、JSON 示例

### dz_1（主卧睡眠区）— 西边混合边拆分

```json
{ "zoneId": "dz_1", "segments": [
  { "type": "wall",                "start": [12100, 10700], "end": [12100, 7800] },
  { "id": "dz_2", "type": "passage", "start": [12100, 7800], "end": [12100, 5800] },
  { "type": "wall",                "start": [12100, 5800],  "end": [15500, 5800] },
  { "type": "wall",                "start": [15500, 5800],  "end": [15500, 10700] },
  { "type": "wall",                "start": [15500, 10700], "end": [14800, 10700] },
  { "id": "wi_5", "type": "window", "start": [14800, 10700], "end": [12800, 10700] },
  { "type": "wall",                "start": [12800, 10700], "end": [12100, 10700] }
]}
```

### dz_2（主卧梳妆区）— 东边全 passage

```json
{ "zoneId": "dz_2", "segments": [
  { "type": "wall",                "start": [9500, 5350],  "end": [11070, 5350] },
  { "id": "d_6", "type": "door",   "start": [11070, 5350], "end": [11820, 5350] },
  { "type": "wall",                "start": [11820, 5350], "end": [12100, 5350] },
  { "id": "dz_1", "type": "passage", "start": [12100, 5350], "end": [12100, 7800] },
  { "type": "wall",                "start": [12100, 7800], "end": [10960, 7800] },
  { "id": "d_5", "type": "door",   "start": [10960, 7800], "end": [10260, 7800] },
  { "type": "wall",                "start": [10260, 7800], "end": [9500, 7800] },
  { "type": "wall",                "start": [9500, 7800],  "end": [9500, 5350] }
]}
```

---

## 三、计算算法总览

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

### 关键常量

| 常量 | 值 | 用途 |
|------|------|------|
| ParallelTolerance | 0.05 | ~3° 平行检测 |
| DistanceTolerance | 150mm | 覆盖 Revit 偏移 + Agent 四舍五入 |
| MinOverlapLength | 10mm | 最小有效重叠 |
| MergeThreshold | 50mm | 极短段合并 |
| ShortPassageThreshold | 200mm | 短 passage 修正 |

---

## 四、Phase A：Opening 预归属

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

## 五、Phase B：Wall/Passage 分类

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

## 六、Phase C：门窗精确切割

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

## 七、Phase D：后处理

在所有边的 segments 收集完成后，顺序执行：

### 规则 1：极短段合并（< 50mm）

浮点精度导致的微小段，合并入相邻同类型段。如果左右类型不同，合入较长的一侧。

### 规则 2：短 passage 修正（< 200mm）

如果一个 passage 段长度 < 200mm，且至少一侧相邻 wall 段 → 合并为 wall。

**原因**：parent 坐标偏移（如 x=12000 vs x=12100）可能导致边界末端 ~100mm 不匹配，被"非 wall 即 passage"错误标为 passage。200mm 阈值安全覆盖这种偏移。

### 规则 3：共线同类型段合并

相邻段如果同类型且共线（方向一致），合并为一段。

### 规则 4：首尾闭合验证

最后一段的 End 应与第一段的 Start 距离 < 1mm。如果不满足，调整最后一段的 End 强制闭合。

---

## 八、边界情况与容错

| 场景 | 处理 |
|------|------|
| 退化边（长度 ≈ 0） | FindOverlapRange 返回 null，跳过 |
| Zone.RawBoundary 为 null | 跳过，返回空 segments |
| Opening.Line 为 null | 跳过该 opening |
| Zone 没有 parent（room zone 自身） | 所有边默认 wall，只做门窗切割 |
| Zone 没有 sibling | 非 parent 部分标 passage(id=null) |
| 同一 opening 匹配到多个 zone | Phase A 只为当前 zone 匹配，不冲突 |
| parent 边与子 zone 边方向相反 | FindOverlapRange 对方向不敏感（排序 tMin/tMax） |
| 浮点精度导致微小间隙 | 后处理规则 1 合并 < 50mm 段 |
| Polygon2D.Vertices 隐式闭合 | 遍历时处理 Vn→V0 边 |
| Revit 浮点噪声 | 输出坐标 Math.Round 到整数 mm |

---

## 九、金凤143 完整验证

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

### rz_2 实测记录（2026-03-11）

**结果**：✅ 算法逻辑完全正确

| 检查项 | 结果 |
|--------|------|
| wi_6（窗）匹配到北边 | ✅ 垂距 100mm < 150mm，位置准确 |
| d_5（门）匹配到南边 | ✅ 垂距 50mm < 150mm，位置准确 |
| 东侧凹凸结构（7段 wall） | ✅ 与截图吻合 |
| 总段数 | 15段（优化后 14段，西边共线合并） |

**优化项**：
1. ~~坐标浮点噪声~~（已修复：Math.Round 取整）
2. ~~共线 wall 段未合并~~（已修复：PostProcess 新增 MergeCollinearSegments）

### 待验证

- [ ] dz_1/dz_2 子分区 passage 分类
- [ ] Agent 端到端布置行为（避免在 passage 段放家具）

---

## 十、交付方式：MCP 工具（按需计算，不存文件）

### 设计决策

boundary segments 是**派生计算**（由 zones + openings 完全决定），不是持久状态。计算量极小（微秒级），无需缓存。

**不存文件** → 不需要触发/同步机制 → 架构更简单。

### Server 端点

```
POST /api/validation/zone-boundaries
请求体：{ "zoneIds": ["dz_1", "dz_2"] }  // 可选，不传返回所有叶子 zone
响应体：List<ZoneBoundaryData>
```

### Agent MCP 工具：`mcp__canvas__get_zone_boundaries`

```python
@tool("get_zone_boundaries", ...)
async def get_zone_boundaries(args):
    # 调用 Server → 格式化为 AI 友好文本
```

输出格式：
```
=== Zone 边界段数据（1 个 zone）===
--- rz_2 (14 段) ---
  wall: [11675, 10700] → [11188, 10700]
  window (wi_6): [11188, 10700] → [9988, 10700]
  wall: [9988, 10700] → [9500, 10700]
  wall: [9500, 10700] → [9500, 7900]
  ...
```

### Agent 调用时机

| 场景 | 时机 | 目的 |
|------|------|------|
| generate-workflow 感知阶段 | 读完 zones 后 | 获取墙面清单，替代手动推断 |
| generate-zoning 完成后 | 写入 subZones 后 | 获取新 subZone 的墙/通道分类 |

---

## 十一、BoundaryDebugPanel Zone 可视化

### 概述

BoundaryDebugPanel 是 Web 端的调试面板，使用独立 Three.js 场景渲染 Zone 的 boundarySegments 数据，用于直观验证算法输出。

### 渲染方案

| 元素 | 实现 | Y 层级 | 说明 |
|------|------|--------|------|
| Zone 绿色填充 | `THREE.Mesh` + `ShapeGeometry` | y=3（顶层） | 半透明绿色，直观标识 Zone 区域 |
| Zone 虚线边框 | `THREE.Line` + `LineDashedMaterial` | y=4（最顶层） | dashSize=150, gapSize=100，标识 Zone 边界 |
| 墙/门/窗构件 | `THREE.Mesh`（按厚度外扩） | y=1~2 | 从 segments 坐标向**房间外部**偏移生成矩形轮廓 |

### 关键算法：构件外扩方向

墙、门、窗构件需要从边界段坐标向房间**外部**偏移一定厚度（如墙 200mm、门 100mm）生成轮廓。方向判定：

```
1. computeWindingSign(polygon) — Shoelace 公式判定多边形绕向
2. CCW（正面积）→ outwardSign = -1 → 使用右法线 (dirY, -dirX) → 外部
3. CW（负面积）→ outwardSign = +1 → 使用左法线 (-dirY, dirX) → 外部
```

### 交互设计

- Zone 填充区域**可点击**，选中后显示完整属性（ID/name/roomId/type/reason），子分区额外显示 parentZoneId
- 墙/门/窗段**可点击**查看属性（类型、坐标、长度）
- 属性数据从 `canvasStore.projectData` 查找（含嵌套子分区搜索）
- 无 X 对角线、无 Zone 标签（简化方案，与主画布差异化）

### 生命周期管理

- `disposeThree()` 必须置空 `renderer`/`scene`/`camera`，防止 `watch(boundaryData)` 与 `watch(visible)` 竞态
- 面板关闭后再次接收 SignalR 事件时，`watch(visible)` 负责重新初始化 Three.js

---

## 十二、提示词调整方向（待实施）

### generate-workflow（感知阶段）
增加指引：调用 `get_zone_boundaries` 获取边界语义数据，直接使用此数据进行墙面分析，不再从 rawBoundary 顶点手动推断。

### generate-bedroom（空间理解补充）
将"列出所有实墙段"改为"从 boundarySegments 中筛选 type=wall 的段"，门窗信息从 door/window 段直接获取。

### generate-zoning（示例更新）
让 Agent 理解：创建 subZone 后，调用 `get_zone_boundaries` 可获取新 subZone 的墙/通道分类。

---

## 十三、实际改动文件清单

| 层 | 文件 | 操作 | 内容 |
|----|------|------|------|
| **Core** | `Models/Computed/BoundarySegment.cs` | ✅ 新建 | BoundarySegment（4 字段 + NullValueHandling） |
| **Core** | `Models/Computed/ZoneBoundaryData.cs` | ✅ 新建 | ZoneBoundaryData 包装类 |
| **Server** | `Services/ZoneBoundaryService.cs` | ✅ 新建 | 四阶段算法 + 坐标四舍五入 + 共线合并 |
| **Server** | `Program.cs` | ✅ 修改 | +1 行 AddSingleton<ZoneBoundaryService> |
| **Server** | `Controllers/ValidationController.cs` | ✅ 修改 | zone-boundaries 端点 + 构造函数注入 |
| **Agent** | `src/mcp/canvas.py` | ✅ 修改 | get_zone_boundaries 工具 + 格式化 + 注册 |
| **Agent** | Skill 提示词 | 🔶 待做 | generate-workflow/bedroom/zoning 引导使用 |
| **Web** | `components/UI/BoundaryDebugPanel.vue` | ✅ 修改 | Zone 可视化 + Zone 属性面板 + disposeThree 竞态修复 |
| **Web** | `stores/canvasStore.ts` | ✅ 修改 | findObjectById 嵌套子分区搜索 + sceneDataCache 兼容 userData |

### 算法方法清单（ZoneBoundaryService.cs）

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
  List<TaggedInterval> ClassifyEdge(Point2D edgeStart, Point2D edgeEnd,
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

  // 工具
  BoundarySegment IntervalToSegment(Point2D edgeStart, Point2D edgeEnd,
      double tStart, double tEnd, string type, string? id)
```

---

## 十四、可复用的已有代码

| 需求 | 方法 | 位置 |
|------|------|------|
| NTS 类型转换 | `NtsConverter.ToNtsLineSegment()` | `Core/Converters/NtsConverter.cs` |
| 平行检测 | `NtsGeometryHelper.IsParallel()` | `Core/Algorithms/Geometries/NtsGeometryHelper.cs` |
| 多边形边遍历 | `NtsGeometryHelper.GetPolygonLines()` | 同上 |
| 投影因子 | `LineSegment.ProjectionFactor()` | NTS 内置 |
| 点沿线段插值 | `LineSegment.PointAlong()` | NTS 内置 |

---

## 附录：算法对比分析（T4 vs EdgeLoopExtractor）

### 问题域差异

两个算法解决的核心问题**不同**，但在"将多边形边切割为语义段"这一步骤上**高度相似**。

| 维度 | EdgeLoopExtractor | T4 boundarySegments |
|------|-------------------|---------------------|
| **作用层级** | 房间级（单层多边形） | 子分区级（父子多边形关系） |
| **输入** | 房间轮廓 + 已投影的构件 | Zone 树 + 未投影的 openings |
| **核心问题** | 墙段 vs 门窗的切割 | **墙 vs 通道**的分类（门窗是次要问题） |
| **坐标精度** | 高（Revit 精确导出） | 低（子 zone 是 Agent 四舍五入的坐标） |
| **构件位置** | 已投影到轮廓边上（精确） | 在墙中线，离 zone 边有 50-100mm 偏移 |
| **额外输出** | Corner（凸/凹角）、Group | 无 |

### 参数化定位对比

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

**✅ 保留 T4 的点积投影法**。

### 构件匹配策略对比

**EdgeLoopExtractor** — 预投影 + 精确匹配（两阶段分离）：每个构件先投影到最近轮廓边，再用 10mm 容差匹配。

**T4 改进** — 借鉴两阶段分离思路：每个 opening 先匹配到垂距最近的 zone 边（150mm 容差），再精确切割。每个 opening 只归属一条边（最近原则），消除重复匹配。

**✅ 采纳 EdgeLoopExtractor 的两阶段分离思路**。

### 切割算法对比

| | EdgeLoopExtractor 切割点法 | T4 区间法 |
|--|---------|--------|
| 概念模型 | 切割点 + 配对 | 区间 + 游标 |
| 处理重叠构件 | 需要额外的配对逻辑 | 天然处理（区间覆盖） |
| 代码复杂度 | 中等（需要 IsStart/IsEnd 跟踪） | 较低（游标即可） |

**实质等价**，T4 区间法与上游 FindOverlapRange 一致，更自然。**✅ 保留 T4 区间法**。

### 不采纳的部分

| 部分 | 理由 |
|------|------|
| Corner 信息 | 增加复杂度，偏离 T4 核心目标，Agent 可自行推断 |
| Group 合并 | 丢失精度，与 T4 目标冲突 |
| 主轴分量法 | 不如点积法通用 |

### 算法来源总结

| 模块 | 来源 | 说明 |
|------|------|------|
| Phase A: Opening 预归属 | EdgeLoopExtractor 启发 | 借鉴"预投影 + 精确切割"的两阶段分离思想 |
| Phase B: Wall/Passage 分类 | T4 原创 | EdgeLoopExtractor 无此能力 |
| Phase B: FindOverlapRange | T4 原创 | 近共线重叠检测（平行+垂距+参数化投影） |
| Phase B: 点积投影法 | T4 原创 | 比 EdgeLoopExtractor 的主轴分量法更通用 |
| Phase C: 线性游标切割 | 两者共有 | T4 用区间法，EdgeLoopExtractor 用切割点法，实质等价 |
| Phase D: 后处理 | T4 原创 | EdgeLoopExtractor 输入精确无需后处理 |
