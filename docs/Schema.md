# BIMCanvas .bcp 数据格式规范

> **本文用途**：`.bcp` 项目格式的字段级权威规范——每个 JSON 文件的结构、字段、枚举与坐标约定。`.bcp` 是开放格式，任何工具都可读写。
>
> **读者**：要读写 `.bcp` 数据、做集成或导出的工程师。
>
> **状态**：2026-06 当前态。`project.json.version` 字段固定为 `"3.0"`（格式主版本号，未随架构小版本变动）。宏观架构见 [Architecture.md](./Architecture.md)，方案层的设计模型见 [Arch_Design_Delivery.md](./Arch_Design_Delivery.md)。

---

## 1. 设计原则

| 原则 | 说明 |
|------|------|
| **文件即真理源** | 所有数据落在 `.bcp` 目录的 JSON / Markdown 文件，无中心数据库 |
| **AI = OBB 规划师** | AI 输出有向包围盒（`bounds` + `facing`），精确几何由 Core 计算，AI 不算几何 |
| **三层数据权限** | `baseline/` 只读、`schemes/` 可写、`computed/` 自动生成 |
| **坐标系** | 笛卡尔，原点视图左下角，X 右为正、Y 上为正，单位毫米（mm），标识 `cartesian_mm_yUp` |

### 几何图元

| 类型 | JSON 形态 | 示例 | 说明 |
|------|-----------|------|------|
| **Point2D** | `[x, y]` | `[3000.5, 2500.0]` | 绝对坐标点 |
| **Vec2D** | `[dx, dy]` | `[-600.0, 0.0]` | 相对偏移 / 方向向量 |
| **Line2D** | `[[x1,y1],[x2,y2]]` | `[[2000,0],[2900,0]]` | 线段 |
| **Polygon2D** | `[[x,y], ...]` | `[[0,0],[6000,0],...]` | 多边形，**隐式闭合**（首尾不重复） |

### 枚举序列化形态（关键契约，易踩坑）

不同枚举的序列化形态**不统一**，消费方必须按下表区分，不能假设全是字符串或全是整数：

| 枚举 | 序列化 | 取值 |
|------|--------|------|
| `OpeningType` | **整数** | `0`=门 `1`=窗 |
| `doorOperation` | **整数** | `0`=平开 `1`=推拉 |
| `RoomType` | **整数** | `0`~`10`（见 §4.4） |
| `ZoneType` | **字符串** | `"exclusion"` / `"room"` / `"designable"` |
| `ZoneTag` / `optionalTags` | **字符串**（camelCase） | `"sleep"` / `"wardrobeStorage"` ...（见 §5.2） |
| `StrategyApproach` | **整数** | `0`~`4` |
| `StrategyStatus` | **整数** | `0`=valid `1`=dirty `2`=invalid |
| `Facing.semantic` | **字符串** | `"north"` / `"south"` ... |

> 设计上业务枚举默认整数序列化（前端历史按整数比较）；仅 `Zone` 系列显式用 `StringEnumConverter` 转字符串。集成时务必逐枚举确认。

---

## 2. 文件夹结构

```
project.bcp (ZIP)
├── project.json                  项目入口
│
├── baseline/                     【基准层】只读，Revit 导出
│   ├── baseline.manifest         清单（version / generatedAt / baselineHash）
│   ├── metadata.json             坐标转换参数
│   ├── architecture.json         墙、柱
│   ├── openings.json             门窗
│   ├── rooms.json                房间边界
│   └── location_lines.json       完成面定位线
│
├── computed/                     【计算层】Server 自动生成
│   ├── computed.manifest         清单（含 baselineHash 校验）
│   ├── room_zones.json           可设计区（rz_*，从 rooms 派生）
│   └── exclusions.json           禁区（ez_*，门扇扫过区等）
│
└── schemes/                      【方案设计层】可写
    ├── strategy.json             策略元数据
    ├── zones.json                全局基线分区（rz_*/ez_*，保留用户调整）
    ├── finishes.json             完成面配置（可空数组）
    ├── {zoneId}/                 设计区 / 容器（见 §5.4）
    │   ├── DESIGN.md             frontmatter: adopted: {slug}（生效指针）
    │   └── {slug}/               平级候选方案
    │       ├── modules.json      家具布置
    │       └── [zones.json]      （可选）方案内部 AI 分区
    └── ...

运行时另由 active 域插件物化到项目全局（非 .bcp 固有，打开项目时按需补齐）：
modules/      模块素材库（module_library.json + 资源）
references/   设计规则（*.md）
```

---

## 3. project.json

```json
{
  "id": "proj_demo_001",
  "name": "理想户型A - 三室两厅",
  "version": "3.0",
  "createdAt": "2025-12-24T10:00:00Z",
  "updatedAt": "2025-12-24T21:00:00Z",
  "coordinateSystem": "cartesian_mm_yUp",
  "activeSchemeId": "default",
  "schemes": [
    { "id": "default", "path": "./schemes", "name": "默认策略" }
  ]
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `id` | string | 是 | 项目唯一标识 |
| `name` | string | 是 | 项目名称 |
| `version` | string | 是 | 格式版本，固定 `"3.0"` |
| `createdAt` / `updatedAt` | string | 是 | ISO 8601 |
| `coordinateSystem` | string | 是 | 固定 `cartesian_mm_yUp` |
| `activeSchemeId` | string | 否 | 当前激活策略 ID |
| `schemes` | array | 是 | 策略引用列表 `{id, path, name}` |

---

## 4. 基准层（baseline/）

只读，由 Revit 插件导出后锁定。`baseline.manifest` 记录 `baselineHash`，computed 层据此校验是否需重算。

### 4.1 metadata.json

```json
{
  "exportDate": "2026-04-29T10:48:21+08:00",
  "revitVersion": "2019",
  "placementElevation": 0.0,
  "origin": [0, 0, 0],
  "rotation": 0.0,
  "transformMethod": "boundingBox",
  "unitSystem": "metric_mm",
  "baselineHash": "sha256:49909f5a..."
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `exportDate` | string | 导出时间 |
| `revitVersion` | string | Revit 版本 |
| `placementElevation` | number | 布置高度（mm） |
| `origin` | number[3] | 坐标原点 `[x,y,z]` |
| `rotation` | number | 视图旋转（弧度） |
| `transformMethod` | string | `projectBasePoint` / `boundingBox` / `cropBox` |
| `unitSystem` | string | 固定 `metric_mm` |
| `baselineHash` | string | `sha256:` 前缀，基准层内容哈希 |

### 4.2 architecture.json

```json
{
  "walls": [
    { "id": "w_1", "elementId": 431228, "isStructural": false,
      "polygon": [[0,0],[5000,0],[5000,200],[0,200]] }
  ],
  "columns": [
    { "id": "c_1", "elementId": 100010, "isStructural": true,
      "polygon": [[2400,1900],[2600,1900],[2600,2100],[2400,2100]] }
  ]
}
```

Wall / Column 字段一致：`id` / `elementId`（Revit 元素 ID）/ `isStructural` / `polygon`（轮廓多边形）。墙厚体现在 `polygon` 几何中，**无独立 `thickness` 字段**。

### 4.3 openings.json（数组）

```json
[
  { "id": "d_1", "type": 0, "doorOperation": 0,
    "line": [[2259.99,8949.99],[3259.99,8949.99]],
    "facingDirection": [0,1], "handDirections": [[1,0]] },
  { "id": "win_1", "type": 1,
    "line": [[2999.99,800.0],[7999.99,800.0]], "facingDirection": [0,1] }
]
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `id` | string | 是 | 门 `d_*`、窗 `win_*` |
| `type` | number | 是 | **整数** `0`=门 `1`=窗 |
| `doorOperation` | number | 仅门 | `0`=平开 `1`=推拉 |
| `windowOperation` | number | 仅窗 | 预留，可为 null |
| `line` | Line2D | 是 | 定位线段 |
| `facingDirection` | Vec2D | 是 | 朝向（室内方向，单位向量） |
| `handDirections` | Vec2D[] | 仅门 | 把手方向（支持多扇门，可空） |

### 4.4 rooms.json（数组）

```json
[
  { "id": "r_1", "name": "次卧一", "type": 3,
    "boundary": [[9399.99,10499.99],[6599.99,10499.99],[6599.99,7099.99],[9399.99,7099.99]] }
]
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | string | `r_*` |
| `name` | string | 房间名称 |
| `type` | number | **整数** RoomType（见下） |
| `boundary` | Polygon2D | 房间边界 |

**RoomType**：`0` 客厅 / `1` 餐厅 / `2` 主卧 / `3` 次卧 / `4` 书房 / `5` 厨房 / `6` 卫生间 / `7` 玄关 / `8` 阳台 / `9` 走廊 / `10` 储藏间。

### 4.5 location_lines.json

```json
{ "lines": [
  { "id": "ll001", "wallId": "w_42", "roomId": "r_6",
    "side": "interior", "line": [[200,200],[4800,200]], "length": 4600 }
] }
```

`id` / `wallId` / `roomId` / `side`（`interior`/`exterior`）/ `line`（Line2D）/ `length`（mm，冗余便于计算）。

---

## 5. 计算层与方案层

### 5.1 computed/（Server 自动生成，勿手改）

禁区、房间、可设计区统一用 **Zone** 模型表示，`type` 字段区分。`room_zones.json` 与 `exclusions.json` 均为 Zone 数组。

```json
// room_zones.json
[
  { "id": "rz_1", "name": "次卧一", "roomId": "r_1", "type": "room",
    "reason": "room:Bedroom", "rawBoundary": [...], "computedBoundary": null,
    "tags": ["sleep", "wardrobeStorage"], "optionalTags": ["generalStorage"],
    "finishRequirements": [], "schemeId": null, "visible": true }
]
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | string | `rz_*`（可设计区）/ `ez_*`（禁区） |
| `name` | string | 区域名称 |
| `roomId` | string | 来源房间（禁区可为空串） |
| `type` | string | **字符串** ZoneType：`exclusion` / `room` / `designable` |
| `reason` | string | 生成原因（如 `door_swing:...`） |
| `rawBoundary` | Polygon2D | 原始边界 |
| `computedBoundary` | Polygon2D \| null | 扣除完成面后的边界 |
| `tags` | string[] | 功能标签（ZoneTag，见 §5.2） |
| `optionalTags` | string[] | 建议标签（可选家具） |
| `finishRequirements` | array | 完成面需求 |
| `schemeId` | string \| null | 关联策略 |
| `visible` | boolean | 是否对用户可见 |
| `subZones` | array | 子分区（条件序列化，无则省略） |

### 5.2 ZoneTag 枚举

camelCase 字符串，按功能分组（持续扩展，权威定义见 `BIMCanvas.Core/Models/Shared/ZoneTag.cs`）：

- 居住：`sleep` / `rest` / `reading` / `work` / `study`
- 储物：`wardrobeStorage` / `shoeStorage` / `generalStorage`
- 餐厨：`dining` / `cooking` / `foodPrep` / `bar`
- 卫浴：`shower` / `bathtub` / `toilet` / `washing` / `laundry` / `vanity`
- 影音/动线/其他：`tvMedia` / `audioVideo` / `entry` / `passage` / `display` / `plants`

### 5.3 strategy.json

```json
{
  "id": "default", "name": "Default", "approach": 0,
  "description": "默认策略", "createdAt": "...", "updatedAt": "...",
  "origin": null, "lastValidatedBaselineHash": "sha256:...", "status": 0
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `approach` | number | **整数** StrategyApproach：`0` 动线优先 / `1` 主家具优先 / `2` 空间利用率 / `3` 风格变体 / `4` 自定义 |
| `origin` | object \| null | 衍生来源（原创为 null），含 `sourceStrategyId/sourceBranch/sourceCommit/derivedAt/derivationReason` |
| `lastValidatedBaselineHash` | string | 最后校验的基准层哈希 |
| `status` | number | **整数** `0` valid / `1` dirty / `2` invalid。dirty 时禁止导出 Revit |

### 5.4 方案结构（指针式平级 + 递归嵌套）

`schemes/` 顶层有全局 `strategy.json` / `zones.json`（基线分区）/ `finishes.json`。设计成果按设计区组织：

- **容器判据**：`{nodePath}/zones.json` 存在 → 该节点是**容器**（本级不布置，递归其子分区）；不存在 → 是**设计区**（读 `DESIGN.md` 的 `adopted` 指针定生效方案）。
- **DESIGN.md**：YAML frontmatter 唯一字段 `adopted: {slug}`，Server 只读写此字段、不解析正文。正文是给人和 LLM 读的设计意图（见 [Arch_Design_Delivery.md](./Arch_Design_Delivery.md)）。
- **方案叶子**：设计区无内部分区 → `{designZone}/{slug}/modules.json`；有内部分区 → `{designZone}/{slug}/zones.json` + `{designZone}/{slug}/{leafId}/modules.json`。

**采纳 = 翻 `adopted` 指针**，零复制零删除完全可逆。

### 5.5 finishes.json

`FinishSegment` 数组（可空）：

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | string | 段 ID |
| `sourceLineId` | string | 引用的定位线 |
| `range` | [number, number] | 范围 `[起点偏移, 终点偏移]`，**绝对 mm** |
| `finishModuleId` | string | 完成面模块 ID |
| `thickness` | number | 厚度（mm） |
| `source` | string | `room_default` / `zone_override` / `user_override`（优先级低→高） |
| `zoneId` | string | 触发区域（仅 zone_override） |
| `reason` | string | 配置原因 |

### 5.6 modules.json（家具布置，叶子粒度）

**包装格式**：顶层是 `{ schemeMetadata, modules }`，不是裸数组。

```json
{
  "schemeMetadata": { "summary": "床头靠北墙，主动线沿东侧" },
  "modules": [
    {
      "id": "m_a1b2c3d4",
      "moduleId": "bed_modern_1800",
      "moduleName": "现代双人床 1.8m",
      "zoneId": "rz_1",
      "bounds": [[600,400],[2400,400],[2400,2400],[600,2400]],
      "facing": { "value": [0,1], "semantic": "north" },
      "items": [
        { "familyId": "fam_bed_001", "offset": [0,0], "role": "主体" },
        { "familyId": "fam_nightstand_001", "offset": [-550,200], "role": "左床头柜" }
      ],
      "placementReason": "床头靠北墙居中"
    }
  ]
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `schemeMetadata.summary` | string | 否 | 一句话设计意图 |
| `id` | string | 是 | 实例 ID（`m_` + 随机，缺失时自动补全） |
| `moduleId` | string | 是 | 模块库类型 ID |
| `moduleName` | string | 否 | 可读名称 |
| `zoneId` | string | 是 | 所属区域（运行时填充） |
| `bounds` | Polygon2D | 是 | 4 顶点矩形边界 |
| `facing` | Facing | 是 | 朝向对象（见下） |
| `items` | ModuleItem[] | 否 | 内部家具清单 |
| `placementReason` | string | 否 | 布置理由 |

**Facing**（混合对象，**不再支持纯字符串**）：

```json
{ "value": [0, 1], "semantic": "north" }
```

- `value`：Vec2D 单位向量，**几何真理**。`[0,1]`=north / `[0,-1]`=south / `[1,0]`=east / `[-1,0]`=west，斜向用 `[0.707,0.707]` 等。
- `semantic`：语义字符串，AI 输入槽，可空；以 `value` 为准。

**ModuleItem**：`familyId`（族库 ID）/ `offset`（Vec2D，相对模块中心）/ `role`（角色）。

---

## 6. 与代码的对应

| 数据 | 权威模型 / 服务 |
|------|----------------|
| project / strategy / baseline.manifest | `BIMCanvas.Core/Models/Project/` |
| Zone / ZoneType / ZoneTag | `BIMCanvas.Core/Models/Computed/Zone.cs` · `Shared/` |
| Module / ModuleItem / Facing | `BIMCanvas.Core/Models/Layout/` · `Semantic/Facing.cs` |
| Opening / Room / RoomType | `BIMCanvas.Core/Models/Revit/` |
| schemes 拓扑 / DESIGN.md 指针 | `BIMCanvas.Server/Services/ModuleFileTopologyService.cs` · `SchemeDesignDocService.cs` |
| computed 生成 / modules 读写 | `ComputedDataService.cs` · `ModulesReaderService.cs` / `ModulesWriterService.cs` |
