# T3：分区架构 + generate-zoning Skill

> **定位**：T3 计划文档，交给执行窗口实施。
> **前置依赖**：T1（工作流+卧室）、T2（知识体系+其余房间）完成后执行。
> **分支**：`refactor/workflow-zoning`

---

## 一、改造目标

**解决的问题**：当前系统无法处理异形空间（L 形卧室）和开放空间（客餐一体）。分区能力是这些场景的前置条件。

**核心洞察**：分区的本质是"空间降维"——把复杂空间降维为多个简单空间，使每个子空间可用现有房间 Skill 独立处理。

**六项交付**：

| # | 交付 | 层 | 类型 |
|---|------|---|------|
| 1 | Zone.cs 扩展（SubZones 嵌套） | Core | 修改 |
| 2 | generate-zoning/SKILL.md | Agent | 新建 |
| 3 | generate-workflow/SKILL.md 更新 | Agent | 修改 |
| 4 | generate-livingroom/SKILL.md 升级 | Agent | 修改 |
| 5 | Server 端代码适配 | Server | 修改 |
| 6 | Web 端代码适配 | Web | 修改 |

**完成后效果**：Agent 能处理 L 形卧室（异形拆解）、客餐一体空间（功能分区）等复杂场景。所有房间类型的布置能力全部就绪。

---

## 二、核心设计决策

| # | 决策 | 说明 | 理由 |
|---|------|------|------|
| D-T3-1 | 统一方法论 | 异形拆解和功能分区共用三步框架 | 本质相同——空间降维 |
| D-T3-2 | Agent 推理坐标 | Agent 直接推理分区边界，不新增 Server API | 分区边界是矩形近似，Agent 几何推理能力足够 |
| D-T3-3 | 暂用 RawBoundary | ComputedBoundary 后续优化 | 当前为 null，validate_layout 已有 fallback |
| D-T3-4 | Zone 嵌套 subZones | Zone 新增 `SubZones: List<Zone>?` | 层级天然表达父子关系，复用 Zone 类型 |
| D-T3-5 | 叶子放置 | 只有叶子 zone 才能放置家具 | 分区的目的就是降维为可独立处理的子空间 |
| D-T3-6 | 目录跟随嵌套 | `schemes/rz_3/dz_1/modules.json` | 与 Arch_Parallel_Development.md 一致 |
| D-T3-7 | 跳过 openingIds | Agent 从 openings.json 按坐标匹配 | 性价比不高，Agent 已具备坐标推理能力 |
| D-T3-8 | 全栈交付 | Core/Server/Web/Agent 全部变更 | 保证端到端可测试 |

---

## 三、数据架构变更

### 3.1 Zone.cs 扩展

**文件**：`BIMCanvas.Core/Models/Computed/Zone.cs`

**新增字段**：

```csharp
/// <summary>
/// 子分区列表。非空时，当前 Zone 为容器（不参与布置），
/// 只有叶子 Zone（SubZones 为 null 或空）才接受家具放置。
/// </summary>
public List<Zone>? SubZones { get; set; }
```

**设计规则**：

| 规则 | 说明 |
|------|------|
| SubZones 非空 → 容器 zone | 不参与布置，只有叶子 zone 接受家具 |
| SubZones 为 null → 叶子 zone | zone 自身接受家具（当前所有 zone 的行为） |
| 子 zone Type = Designable | 区分于父 zone 的 Room 类型 |
| 子 zone RoomId 继承父 zone | 同一物理房间 |
| 子 zone Tags ⊆ 父 zone Tags | 功能分配，不创造新功能 |
| 子 zone Id 格式 = `dz_{n}` | d = designable，区分 rz_ (room) 和 ez_ (exclusion) |

### 3.2 schemes/zones.json 新格式

```json
[
  {
    "id": "rz_3",
    "name": "客餐厅",
    "type": "room",
    "roomId": "r_3",
    "tags": ["tvMedia", "rest", "dining"],
    "rawBoundary": [[0,0], [7200,0], [7200,4800], [0,4800]],
    "subZones": [
      {
        "id": "dz_1",
        "name": "客厅区",
        "type": "designable",
        "roomId": "r_3",
        "tags": ["tvMedia", "rest"],
        "optionalTags": ["display"],
        "rawBoundary": [[0,0], [4200,0], [4200,4800], [0,4800]],
        "reason": "南侧靠窗，自然光充足，适合休息和观影"
      },
      {
        "id": "dz_2",
        "name": "餐厅区",
        "type": "designable",
        "roomId": "r_3",
        "tags": ["dining"],
        "rawBoundary": [[4200,0], [7200,0], [7200,4800], [4200,4800]],
        "reason": "北侧靠厨房和入口"
      }
    ]
  },
  {
    "id": "rz_1",
    "name": "主卧",
    "type": "room",
    "roomId": "r_1",
    "tags": ["sleep", "wardrobeStorage"],
    "rawBoundary": [[...]],
    "subZones": null
  }
]
```

### 3.3 目录结构

```
schemes/
├── zones.json
├── rz_1/                    # 无 subZones → 叶子 zone，直接放家具
│   └── modules.json
├── rz_3/                    # 有 subZones → 容器 zone，不放家具
│   ├── dz_1/                # 子分区：客厅区
│   │   └── modules.json
│   └── dz_2/                # 子分区：餐厅区
│       └── modules.json
```

### 3.4 openings / exclusions 策略

- **不在 Zone 上新增** openingIds / exclusionAreaIds 字段
- Agent 从 `baseline/openings.json` 和 `computed/exclusions.json` 按坐标匹配
- WHY：Agent 已在理解阶段做坐标推理，openings 匹配是自然延伸。额外字段增加维护成本但收益有限。如实践中发现 Agent 匹配困难，后续迭代可加。

### 3.5 向下兼容

- SubZones 为 null 的 zone 行为完全不变
- 现有项目无需迁移（rz_* 无 subZones，保持叶子 zone 行为）
- 新增的嵌套能力仅在 Agent 主动创建 subZones 时激活

---

## 四、Server 端变更

### 4.1 ProjectService.CreateZoneDirectories()

**文件**：`BIMCanvas.Server/Services/ProjectService.cs`

**变更**：递归遍历 zones，为有 subZones 的 zone 创建嵌套目录结构。

```
对每个 zone：
  如果 zone.SubZones 非空：
    创建 schemes/{zoneId}/ 目录（不创建 modules.json）
    对每个 subZone：
      创建 schemes/{zoneId}/{subZoneId}/ 目录
      创建 schemes/{zoneId}/{subZoneId}/modules.json（空 []）
  如果 zone.SubZones 为空或 null：
    创建 schemes/{zoneId}/modules.json（当前逻辑，保持不变）
```

### 4.2 ProjectWatcherService — zones.json 变更触发

**文件**：`BIMCanvas.Server/Services/ProjectWatcherService.cs`

**变更**：当检测到 `schemes/zones.json` 变更时，调用 CreateZoneDirectories 刷新目录结构。

WHY：Agent 在理解阶段完成分区后写入 subZones，Server 自动创建对应子目录。这是 File-Driven 架构的自然延伸——文件变更驱动基础设施响应。

### 4.3 ValidationController.LoadZoneData()

**文件**：`BIMCanvas.Server/Controllers/ValidationController.cs`

**变更**：展平嵌套 zone，只取叶子 zone 参与验证。

```
原逻辑：直接将 schemes/zones.json 的所有 zone 加入 designZones
新逻辑：
  对每个 zone：
    如果 zone.SubZones 非空 → 递归取叶子 zone，加入 designZones
    如果 zone.SubZones 为空 → 直接加入 designZones（不变）
```

WHY：验证器检查"模块是否在合法区域内"。容器 zone 的边界是子 zone 的并集，直接用子 zone 验证更精确。

### 4.4 ValidationController.LoadAllModules()

**文件**：`BIMCanvas.Server/Controllers/ValidationController.cs`

**确认**：当前逻辑遍历 `schemes/` 下所有子目录读取 modules.json。嵌套目录 `schemes/rz_3/dz_1/modules.json` 应能被正确遍历。如不能，需调整遍历逻辑支持递归子目录。

---

## 五、Web 端变更

### 5.1 Zone TypeScript 接口

**文件**：`BIMCanvas.Web/src/types/canvas.ts`

**变更**：Zone 接口新增 subZones 字段。

```typescript
interface Zone {
  // ... 现有字段 ...
  subZones?: Zone[];
}
```

### 5.2 ZoneBuilder.ts

**文件**：`BIMCanvas.Web/src/services/builders/ZoneBuilder.ts`

**变更**：支持渲染子分区。

| 场景 | 渲染方式 |
|------|---------|
| 无 subZones 的 zone | 保持现有渲染（不变） |
| 有 subZones 的父 zone | 半透明轮廓线（整体边界参考） |
| subZones 中的子 zone | 与现有 Designable zone 渲染一致（带功能标签颜色） |

---

## 六、Agent 端变更

### 6.1 generate-zoning/SKILL.md（新建）

**路径**：`BIMCanvas.Agent/templates/skills/generate-zoning/SKILL.md`

**定位**：跨房间类型的通用分区能力 Skill。被 generate-workflow 在理解阶段条件加载。

**注意力预算**：≤ 100 行

**结构设计**：

```
# 分区 Skill

> 将复杂空间降维为多个简单空间，使每个子空间可用对应房间 Skill 独立处理。
> 分区是"理解"的延伸，不是独立阶段。

## 一、触发条件

| 场景 | 触发信号 | 分区类型 |
|------|---------|---------|
| 异形空间 | rawBoundary 顶点 > 4 | 几何分区 |
| 开放多功能 | 多功能标签组 + 面积 > 阈值 | 功能分区 |
| 两者兼有 | 上述条件同时满足 | 先几何后功能 |

WHY：矩形+单功能 = 简单空间，现有房间 Skill 直接处理。
复杂空间需先降维，否则房间 Skill 无法有效处理。

## 二、分区方法（统一三步框架）

### 步骤 1：识别子空间（几何层面）
- 矩形房间 → 跳过
- 非矩形 → 识别主体矩形 + 异形区
  - 异形区 = "有独立空间特征的迷你房间"（≥2面墙 + 共享墙角）
  WHY：物理围合创造独立空间体验，不是"主区域附属墙段"

### 步骤 2：定义功能（语义层面）
- 先问"这个空间最适合什么功能？"
- 再选"实现该功能需要什么家具组合？"
WHY：功能驱动产出"有使用场景的空间"；
几何驱动产出"零散家具的堆放区"。

### 步骤 3：输出 subZones（数据层面）
- 修改 schemes/zones.json 中父 zone 的 subZones 字段
- 每个子 zone 为 Designable 类型
- Id 格式：dz_{n}

## 三、两种场景差异

### 几何分区
- 确定性高，几何驱动
- 关键：子空间边界不重叠、覆盖原始房间

### 功能分区
- 需设计判断，可能需用户确认
- 空间阅读三要素：动线方向、纵深层次、采光轴
- 关键：动线区域通透、功能区面积匹配需求

## 四、分区策略声明示例

### 示例 1：L 形主卧（几何分区）
场景：主体 3.6m×4.2m，东南延伸 1.8m×2.4m，南墙窗，北墙入口

步骤 1 — 识别子空间：
- 主体矩形 3.6m×4.2m
- 延伸区 1.8m×2.4m（三面墙围合）

步骤 2 — 定义功能：
- 主体 → 睡眠区（核心功能）
- 延伸区 → 更衣区（独立空间适合收纳+梳妆）

步骤 3 — subZones：
- dz_1: 睡眠区, tags=[Sleep]
- dz_2: 更衣区, tags=[WardrobeStorage, Vanity]

### 示例 2：客餐一体（功能分区）
场景：7.2m×4.8m，tags=[TvMedia,Rest,Dining]，南墙大窗，北墙入口

空间阅读：
- 动线：北→南，入口到窗前
- 纵深：入口区→用餐区→休息区→采光区
- 采光：南窗全面

步骤 2 — 定义功能：
- 南侧 → 客厅区（采光好，适合日常活动）
- 北侧 → 餐厅区（靠入口和厨房）

步骤 3 — subZones：
- dz_1: 客厅区, tags=[TvMedia, Rest]
- dz_2: 餐厅区, tags=[Dining]

## 五、关键约束

【硬约束】
① 子空间边界不重叠（WHY：一个位置不能同时属于两个功能区）
② 子空间覆盖原始 rawBoundary（WHY：不遗漏可用空间）
③ 功能定义必须具体（"更衣区"✓ "过渡区"✗）
   WHY：模糊功能无法映射到房间 Skill 的 Tags

【软指导】
① 异形区优先搜索辅助家具位置（WHY：独特尺寸恰好适配特定家具）
② 动线区域保持通透（WHY：通行功能优先于放置功能）
③ 子空间边界用矩形近似（WHY：与 OBB 设计原则一致，简化坐标推理）

【自由区域】
- 分区线的精确位置
- 功能区面积比例分配
- 子空间命名风格
```

### 6.2 generate-workflow/SKILL.md（更新）

**文件**：T1 交付后的 `BIMCanvas.Agent/templates/skills/generate-workflow/SKILL.md`

**更新三处**：

**理解阶段** — 分区判断从预留接口变为可用功能：
```
T1 写的（预留）：
  "如需分区 → 加载 generate-zoning（T3 后可用）"

T3 替换为：
  "评估分区需求：
   - rawBoundary 顶点 > 4 → 加载 generate-zoning（异形空间需几何拆解）
   - 多功能标签组 + 面积较大 → 加载 generate-zoning（开放空间需功能分区）
   执行分区 → 产出 subZones → 为每个叶子 zone 分别加载房间 Skill"
```

**执行阶段** — 嵌套写入路径：
```
新增说明：
  "分区场景的家具写入路径：schemes/{parentZoneId}/{childZoneId}/modules.json
   例如 schemes/rz_3/dz_1/modules.json"
```

**审查阶段** — 跨分区动线检查：
```
新增说明：
  "分区场景额外审查：相邻子分区之间的动线是否连贯"
```

### 6.3 generate-livingroom/SKILL.md（升级为完整版）

**文件**：T2 交付后的 `BIMCanvas.Agent/templates/skills/generate-livingroom/SKILL.md`

**注意力预算**：≤ 150 行（从框架级 ≤80 行升级）

**补充内容**：

1. **分区逻辑**：
   - 开放空间判断：tags 含多个功能组（如 tvMedia+dining）或面积 > 25㎡
   - 触发 generate-zoning → 按功能分区后，每个子 zone 独立执行策略

2. **客餐一体分区策略声明示例**（完整版，从 §6.1 示例 2 展开）

3. **电视墙选择优先级链 + WHY**

4. **开放空间动线规划原则**

5. **硬约束新增**：
   - 开放空间必须先分区再布置（加载 generate-zoning）

### 6.4 zone_tools.py（更新）

**文件**：`BIMCanvas.Agent/src/tools/zone_tools.py`

**更新**：

- `get_all_zones()`：同时读取 `computed/room_zones.json`（无 subZones）和 `schemes/zones.json`（可能有 subZones）
- `get_zone(zone_id)`：支持在嵌套结构中递归查找（查父 zone 和子 zone）
- 新增 `get_leaf_zones()`：只返回叶子 zone（可放置家具的 zone）
- `get_exclusions(zone_id)`：当前返回全部禁区。T3 不改变此行为（Agent 按坐标匹配）

---

## 七、分区方法论

### 本质：空间降维

```
复杂空间（Agent 直接处理困难）
    ↓ generate-zoning
多个简单空间（每个可用现有房间 Skill 独立处理）
```

### 统一三步框架

| 步骤 | 几何分区（异形拆解） | 功能分区（开放空间） |
|------|-------------------|-------------------|
| 1. 识别子空间 | 按 rawBoundary 几何形状拆为矩形 | 按动线/功能/采光划分区域 |
| 2. 定义功能 | 为每个子区域分配功能（相对简单） | 为每个区域分配功能（核心工作） |
| 3. 输出 subZones | 写入 zones.json 的 subZones 字段 | 同左 |

### 从旧体系迁移的知识清单

| 旧文件位置 | 有效内容 | 新文件位置 |
|-----------|---------|-----------|
| placement_guide §1.1 空间阅读 | 动线方向、纵深层次、采光轴 | generate-zoning §三 功能分区 |
| placement_guide §1.1 功能分区 | "先定功能→再选家具→后排墙面" | generate-zoning §二 步骤 2 |
| placement_guide §1.3 异形利用 | 主体矩形 vs 异形区判断方法 | generate-zoning §二 步骤 1 |
| placement_guide §1.3 异形优先 | "异形区是有独立空间特征的迷你房间" | generate-zoning §五 软指导 |
| SKILL.md §6.1b 子空间识别 | ≥2 面墙 + 共享墙角 = 独立子空间 | generate-zoning §二 步骤 1 |
| SKILL.md §6.1b 功能定义 | 功能名必须具体，禁止模糊用语 | generate-zoning §五 硬约束③ |

---

## 八、遵循原则

与 T1/T2 一致：

1. **注意力零和**：generate-zoning ≤ 100 行，generate-livingroom ≤ 150 行
2. **WHY 优先**：每条规则必须附带理由
3. **示例锚定基准**：至少 2 个分区策略声明示例（L 形 + 客餐一体）
4. **三级约束分明**：硬约束 / 软指导 / 自由区域
5. **职责单一**：generate-zoning 管"怎么拆分空间"，不管"怎么设计家具方案"
6. **留白是设计选择**：分区线位置、面积比例让 Agent 自主判断

---

## 九、验收标准

### P0：必须通过

**Core/数据**：
- [ ] Zone.cs 新增 SubZones 字段，Core 编译通过
- [ ] schemes/zones.json 支持嵌套格式，向下兼容（无 subZones 的 zone 行为不变）

**Server**：
- [ ] CreateZoneDirectories 支持递归创建子目录
- [ ] LoadZoneData 正确展平嵌套 zone（只取叶子 zone 参与验证）
- [ ] validate_layout(zoneIds=["dz_1"]) 能正确验证嵌套子 zone 内的模块
- [ ] Server 编译通过

**Agent**：
- [ ] generate-zoning ≤ 100 行，覆盖异形 + 开放两种场景
- [ ] generate-workflow 理解阶段分区判断可用（非预留）
- [ ] generate-livingroom 升级为完整版（≤ 150 行）

### P1：应该满足

- [ ] 至少 2 个分区策略声明示例（L 形卧室 + 客餐一体）
- [ ] 每条规则有 WHY
- [ ] ZoneBuilder 渲染子分区
- [ ] zone_tools.py 支持嵌套查找（get_leaf_zones）
- [ ] Web Zone 接口同步更新

---

## 十、注意事项

1. **generate-zoning 是能力 Skill，不是房间 Skill**：只包含空间拆分方法论，不包含任何房间特定的设计知识（如"卧室延伸区适合做更衣间"）。功能定义的具体知识由 Agent 的理解能力和房间 Skill 共同提供。

2. **分区在理解阶段完成，不增加新阶段**：五阶段流程不变（感知→理解→策略→执行→审查→汇报）。分区是理解阶段的深化。

3. **Agent 坐标精度预期**：矩形近似边界，数十 mm 误差可接受。边界的主要用途：(a) validate_layout 约束区域；(b) 房间 Skill 的可用空间参考。

4. **分区后的 layout-agent 行为**：自行完成分区后，对每个叶子 zone 独立执行策略和放置。不会为子 zone 再派发新的 layout-agent。

5. **向下兼容**：SubZones 为 null 的 zone 行为完全不变。现有项目无需迁移。

6. **Server 变更范围**：虽然涉及多个文件，但每处变更都是在已有逻辑上增加"递归/展平"能力，不改变核心架构。

7. **Web 变更范围**：ZoneBuilder 的子分区渲染应尽量复用现有 Designable zone 的渲染逻辑，差异仅在于父 zone 的视觉降级（半透明轮廓）。

---

## 十一、与 T1/T2 联动

### 与 T1 联动
- T1 的 generate-workflow 预留了"加载 generate-zoning"接口 → **T3 激活**
- T1 的 generate-bedroom 预留了"如有分区，由 generate-zoning 处理" → **T3 使其可用**
- T1 的 layout-agent 自主加载机制 → 分区场景下先加载 zoning 再加载房间 Skill

### 与 T2 联动
- T2 的 generate-livingroom 标注了"待 T3 后充实" → **T3 完成升级**
- T2 的 design_principles 不含异形利用规则 → **已预留给 T3 的 generate-zoning**
- T2 的 placement_guide §1.1/1.3 → **方法论提炼到 generate-zoning**

---

## 十二、参考材料

| # | 文档 | 路径 | 用途 |
|---|------|------|------|
| 1 | 重构总览 | `plans/workflow-refactor/overview.md` | 统一上下文 |
| 2 | T1 计划 | `plans/workflow-refactor/T1-agent-workflow.md` | 分区预留接口 |
| 3 | T2 计划 | `plans/workflow-refactor/T2-knowledge-system.md` | livingroom 框架级定义 |
| 4 | 提示词哲学 | `docs/Agent_Prompt_Design_Philosophy.md` | 设计理论基础 |
| 5 | 空间类型架构 | `plans/Space_Type_Workflow_Vision.md` | 开放空间分析 |
| 6 | placement_guide | `BIMCanvas.Server/Templates/knowledge/placement_guide.md` | 分区知识来源 |
| 7 | 旧 SKILL.md | `BIMCanvas.Agent/templates/skills/generate-workflow/SKILL.md` | 旧子空间逻辑 |
| 8 | Zone.cs | `BIMCanvas.Core/Models/Computed/Zone.cs` | 数据模型 |
| 9 | ZoneTag.cs | `BIMCanvas.Core/Models/Shared/ZoneTag.cs` | 功能标签枚举 |
| 10 | ProjectService.cs | `BIMCanvas.Server/Services/ProjectService.cs` | 目录创建逻辑 |
| 11 | ValidationController.cs | `BIMCanvas.Server/Controllers/ValidationController.cs` | 验证逻辑 |
| 12 | ZoneBuilder.ts | `BIMCanvas.Web/src/services/builders/ZoneBuilder.ts` | 前端渲染 |
| 13 | 并行开发架构 | `docs/Arch_Parallel_Development.md` | 嵌套目录设计 |
| 14 | 归档设计规格 | `docs/archives/Agent_Design_Spec.md` | parentRoomZoneId 提议 |

---

## 十三、关键文件清单

| 层 | 文件 | 变更类型 | 内容 |
|----|------|---------|------|
| Core | `BIMCanvas.Core/Models/Computed/Zone.cs` | 修改 | 新增 SubZones 字段 |
| Server | `BIMCanvas.Server/Services/ProjectService.cs` | 修改 | CreateZoneDirectories 递归 + zones.json 变更触发 |
| Server | `BIMCanvas.Server/Controllers/ValidationController.cs` | 修改 | LoadZoneData 展平 + LoadAllModules 嵌套路径 |
| Server | `BIMCanvas.Server/Services/ProjectWatcherService.cs` | 修改 | zones.json 变更时刷新目录 |
| Web | `BIMCanvas.Web/src/types/canvas.ts` | 修改 | Zone 接口加 subZones |
| Web | `BIMCanvas.Web/src/services/builders/ZoneBuilder.ts` | 修改 | 渲染子分区 |
| Agent | `BIMCanvas.Agent/templates/skills/generate-zoning/SKILL.md` | 新建 | 分区方法论 |
| Agent | `BIMCanvas.Agent/templates/skills/generate-workflow/SKILL.md` | 修改 | 激活分区接口 |
| Agent | `BIMCanvas.Agent/templates/skills/generate-livingroom/SKILL.md` | 修改 | 框架→完整版 |
| Agent | `BIMCanvas.Agent/src/tools/zone_tools.py` | 修改 | 支持嵌套查找 |
