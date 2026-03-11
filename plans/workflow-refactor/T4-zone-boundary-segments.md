# T4: Zone 边界语义化 — boundarySegments

> 为 Zone 添加边界段类型描述（wall/passage/door/window），解决 Agent 无法区分墙和通道的问题。

**前置任务**：T3-zoning-architecture（分区架构已完成）
**状态**：设计完成，待实现

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

## 一、数据模型（Core 层新增）

### `BIMCanvas.Core/Models/Computed/BoundarySegment.cs`（新建）

```csharp
/// <summary>
/// Zone 边界段：wall/passage/door/window
/// 有序数组，首尾闭合，完整描述 zone 的边界语义。
/// </summary>
public class BoundarySegment
{
    /// <summary>门窗 ID（d_5/wi_5）或相邻 zone ID（dz_1），wall 时为 null</summary>
    public string? Id { get; set; }

    /// <summary>"wall" | "passage" | "door" | "window"</summary>
    public string Type { get; set; }

    public Point2D Start { get; set; }
    public Point2D End { get; set; }
}
```

### `BIMCanvas.Core/Models/Computed/ZoneBoundaryData.cs`（新建）

```csharp
public class ZoneBoundaryData
{
    public string ZoneId { get; set; }
    public List<BoundarySegment> Segments { get; set; } = new();
}
```

**设计要点**：
- 4 字段、每段自包含、有序闭合
- 门窗从 wall 段中拆出成独立段（不是嵌套在 wall 的 openings 字段里）
- 不修改 Zone.cs：boundarySegments 是派生计算数据

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

## 三、计算算法（Server 层）— 待深入讨论

### 核心逻辑：`ZoneBoundaryService.CalculateZoneBoundarySegments()`

```
输入：allZones（含 subZones）, openings
输出：List<ZoneBoundaryData>

对每个叶子 zone Z：
  parent = Z 的父 zone（room zone）
  siblings = 同父 zone 的兄弟叶子 zone

  对 Z.rawBoundary 的每条边 E (Vi → Vi+1)：

    // 1. 检查 E 是否在父 zone 的边界上（共线+重叠判定，容差 ~150mm）
    wallOverlap = findOverlapWithPolygon(E, parent.rawBoundary)

    // 2. 检查 E 是否与兄弟 zone 共享
    passageOverlaps = []
    for each sibling S:
      passageOverlaps += findOverlapWithPolygon(E, S.rawBoundary)

    // 3. 混合边自动拆分
    //    将 wallOverlap 和 passageOverlaps 投影到 E 的参数化区间 [0,1]
    //    合并+排序 → 生成多个子段，每段标记 wall 或 passage
    //    未被覆盖的部分默认为 wall（外墙，可能因浮点精度未匹配到父边界）

    segments += splitEdgeByRanges(E, wallOverlap, passageOverlaps)

  // 4. 匹配门窗到 wall 段，拆分为 wall + door/window + wall
  for each seg where seg.Type == "wall":
    for each opening in openings:
      if opening.Line 中点到 seg 直线距离 < 150mm:
        投影 opening 端点到 seg 上 → 拆分 seg 为最多 3 段
```

### 关键算法细节

**共线+重叠判定（容差 150mm）**：
- 开口（门窗）在 Revit 导出时坐标有 ~50-100mm 偏移（门线在墙中线，zone 边界在墙内面）
- 子 zone 的 rawBoundary 是 Agent 四舍五入的整数坐标
- 使用 150mm 容差覆盖这两种误差

### 正确分类示例（金凤143 rz_3 → dz_1 + dz_2）

**父 zone rz_3 边界**（关键段）：
- x=12100 线：y=7800→10700 是墙
- x=12000 线：y=5350→5800 是墙（注意是 12000 不是 12100）
- y=7800 线：x=9500→12100 是墙
- y=5350 线：x=9500→12000 是墙

**dz_1** `[[12100,5800],[15500,5800],[15500,10700],[12100,10700]]`：

| 边 | 方向 | 父边界匹配 | 类型 |
|----|------|-----------|------|
| (12100,5800)→(15500,5800) | south | (12000,5800)→(13650,5800) ≈匹配 | **wall** |
| (15500,5800)→(15500,10700) | east | 完全匹配 | **wall** |
| (15500,10700)→(12100,10700) | north | 完全匹配 | **wall**（含 wi_5） |
| (12100,10700)→(12100,5800) | west | y=10700→7800 匹配，y=7800→5800 不匹配 | **拆分**：wall(10700→7800) + passage(7800→5800, →dz_2) |

**dz_2** `[[9500,5350],[12100,5350],[12100,7800],[9500,7800]]`：

| 边 | 方向 | 父边界匹配 | 类型 |
|----|------|-----------|------|
| (9500,5350)→(12100,5350) | south | (9500,5350)→(12000,5350) 部分匹配 | **wall**（含 d_6） |
| (12100,5350)→(12100,7800) | east | 父边界无 x=12100 y<7800 的段 | **passage**（→dz_1） |
| (12100,7800)→(9500,7800) | north | (12100,7800)→(9500,7800) 完全匹配 | **wall**（含 d_5） |
| (9500,7800)→(9500,5350) | west | (9500,7800)→(9500,5350) 完全匹配 | **wall** |

---

## 四、交付方式：MCP 工具（按需计算，不存文件）

### 设计决策

boundary segments 是**派生计算**（由 zones + openings 完全决定），不是持久状态。计算量极小（微秒级），无需缓存。

**不存文件** → 不需要触发/同步机制 → 架构更简单。

### MCP 工具：`get_zone_boundaries`

```
输入：{ projectPath, zoneIds?: string[] }
输出：List<ZoneBoundaryData>

Server 内部：
1. 读当前有效 zones（schemes/zones.json 优先，回退 computed/room_zones.json）
2. 读 baseline/openings.json
3. 实时计算 boundary segments
4. 返回
```

zoneIds 可选：指定时只返回这些 zone 的 segments，不指定时返回所有叶子 zone。

### Agent 调用时机

| 场景 | 时机 | 目的 |
|------|------|------|
| generate-workflow 感知阶段 | 读完 zones 后 | 获取墙面清单，替代手动推断 |
| generate-zoning 完成后 | 写入 subZones 后 | 获取新 subZone 的墙/通道分类 |

两个触发场景都是 **Agent 主动调用**，Server 无需任何监听逻辑。

---

## 五、改动文件清单

| 层 | 文件 | 操作 | 内容 |
|----|------|------|------|
| **Core** | `Models/Computed/BoundarySegment.cs` | 新建 | BoundarySegment（4 字段） |
| **Core** | `Models/Computed/ZoneBoundaryData.cs` | 新建 | ZoneBoundaryData 包装类 |
| **Server** | `Services/ZoneBoundaryService.cs` | 新建 | 计算逻辑：CalculateZoneBoundarySegments() |
| **Server** | Controller（扩展现有 MCP 端点） | 修改 | MCP 端点：get_zone_boundaries |
| **Agent** | MCP 工具注册 | 修改 | 注册 get_zone_boundaries 工具 |
| **Agent** | Skill 提示词 | 修改 | generate-bedroom、generate-workflow 引导使用 boundarySegments |

---

## 六、提示词调整方向

### generate-workflow（感知阶段）
增加指引：调用 `get_zone_boundaries` 获取边界语义数据，直接使用此数据进行墙面分析，不再从 rawBoundary 顶点手动推断。

### generate-bedroom（空间理解补充）
将"列出所有实墙段"改为"从 boundarySegments 中筛选 type=wall 的段"，门窗信息从 door/window 段直接获取。

### generate-zoning（示例更新）
让 Agent 理解：创建 subZone 后，调用 `get_zone_boundaries` 可获取新 subZone 的墙/通道分类。

---

## 七、实施顺序

1. Core 层：新建 BoundarySegment.cs、ZoneBoundaryData.cs
2. Server 算法：新建 ZoneBoundaryService，实现计算逻辑（核心难点）
3. Server 端点：MCP Controller 端点暴露 get_zone_boundaries
4. Agent MCP：注册工具 + Skill 提示词调整
5. 编译验证 + 用金凤143数据测试

---

## 八、验证方案

1. **编译**：Core + Server 编译通过
2. **MCP 调用测试**：调用 get_zone_boundaries(金凤143, ["dz_1", "dz_2"])，验证：
   - dz_1 西边拆分为 wall(10700→7800) + passage(7800→5800)
   - dz_2 东边整体为 passage
   - dz_2 南墙：wall + d_6(door) + wall
   - dz_2 北墙：wall + d_5(door) + wall
   - dz_1 北墙：wall + wi_5(window) + wall
3. **Agent 端到端**：重新运行主卧布局任务，观察 Agent 是否避免在 passage 段放家具
