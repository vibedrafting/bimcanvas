# T4: Zone 边界语义化 — boundarySegments

> 为 Zone 添加边界段类型描述（wall/passage/door/window），解决 Agent 无法区分墙和通道的问题。

**前置任务**：T3-zoning-architecture（分区架构已完成）
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
| Skill 提示词调整 | 🔶 待做 | generate-workflow/bedroom/zoning 引导使用 boundarySegments |
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

## 三、计算算法（Server 层）

### `ZoneBoundaryService.CalculateBoundarySegments()`

四阶段算法，详见 `T4-boundary-algorithm-design.md`。

```
Phase A: Opening 预归属 — 每个 opening 绑定到垂距最小的 zone 边（平行+垂距<150mm）
Phase B: Wall/Passage 分类 — "非 wall 即 passage"：parent 匹配→wall，补集→passage，sibling 匹配→passage.Id
Phase C: 门窗精确切割 — 线性游标拆分 wall 段为 wall+door/window+wall
Phase D: 后处理 — 极短段合并(<50mm) + 短passage修正(<200mm) + 共线同类型合并 + 首尾闭合
```

### 关键算法：`FindOverlapRange`

平行检测（叉积<0.05≈3°）+ 垂距检测（到无限延长线<150mm）+ 参数化投影 [0,1]

### 关键常量

| 常量 | 值 | 用途 |
|------|------|------|
| ParallelTolerance | 0.05 | ~3° 平行检测 |
| DistanceTolerance | 150mm | 覆盖 Revit 偏移 + Agent 四舍五入 |
| MinOverlapLength | 10mm | 最小有效重叠 |
| MergeThreshold | 50mm | 极短段合并 |
| ShortPassageThreshold | 200mm | 短 passage 修正 |

### 边界情况处理

| 场景 | 处理 |
|------|------|
| Zone.RawBoundary 为 null | 跳过，返回空 segments |
| Opening.Line 为 null | 跳过该 opening |
| 顶层 room zone（无 parent） | 所有边默认 wall，只做门窗切割 |
| Polygon2D.Vertices 隐式闭合 | 遍历时处理 Vn→V0 边 |
| Revit 浮点噪声 | 输出坐标 Math.Round 到整数 mm |
| 共线同类型段 | PostProcess 自动合并 |

---

## 四、交付方式：MCP 工具（按需计算，不存文件）

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

## 五、实际改动文件清单

| 层 | 文件 | 操作 | 内容 |
|----|------|------|------|
| **Core** | `Models/Computed/BoundarySegment.cs` | ✅ 新建 | BoundarySegment（4 字段 + NullValueHandling） |
| **Core** | `Models/Computed/ZoneBoundaryData.cs` | ✅ 新建 | ZoneBoundaryData 包装类 |
| **Server** | `Services/ZoneBoundaryService.cs` | ✅ 新建 | 四阶段算法 + 坐标四舍五入 + 共线合并 |
| **Server** | `Program.cs` | ✅ 修改 | +1 行 AddSingleton<ZoneBoundaryService> |
| **Server** | `Controllers/ValidationController.cs` | ✅ 修改 | zone-boundaries 端点 + 构造函数注入 |
| **Agent** | `src/mcp/canvas.py` | ✅ 修改 | get_zone_boundaries 工具 + 格式化 + 注册 |
| **Agent** | Skill 提示词 | 🔶 待做 | generate-workflow/bedroom/zoning 引导使用 |

---

## 六、提示词调整方向（待实施）

### generate-workflow（感知阶段）
增加指引：调用 `get_zone_boundaries` 获取边界语义数据，直接使用此数据进行墙面分析，不再从 rawBoundary 顶点手动推断。

### generate-bedroom（空间理解补充）
将"列出所有实墙段"改为"从 boundarySegments 中筛选 type=wall 的段"，门窗信息从 door/window 段直接获取。

### generate-zoning（示例更新）
让 Agent 理解：创建 subZone 后，调用 `get_zone_boundaries` 可获取新 subZone 的墙/通道分类。

---

## 七、验证记录

### 金凤143 rz_2 实测（2026-03-11）

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
