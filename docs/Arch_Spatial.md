# Arch_Spatial — 空间几何与约束

> **本文用途**：讲清 BIMCanvas 如何让 AI 理解并操作建筑空间——坐标系、AI 的空间抽象（OBB 规划师）、语义意图到精确几何的转换链路、以及保证布置合法的约束验证。
>
> **读者**：想了解"自然语言怎么变成精确几何、又怎么保证它不越界不打架"的工程师。
>
> **状态**：2026-06 当前态。字段级数据格式见 [Schema.md](./Schema.md)，坐标/角度的前端细节权威在 `BIMCanvas.Web/README.md`。

---

## 1. 坐标系

BIMCanvas 全程使用 **Y-up 笛卡尔坐标系**，单位毫米（mm），原点在视图裁剪框左下角，X 右为正、Y 上为正——符合 CAD/BIM 与数学直觉。这与 Web 屏幕坐标（原点左上、Y 下为正）相反，**转换只在前端渲染层发生**：

- 渲染：`y_screen = height - y_model`（**禁用 CSS `scaleY(-1)`**，会导致文字倒置）；事件反向转换。
- Core 层做纯笛卡尔运算，**不做任何坐标系转换**；Revit 导出时已归一化为 Y-up/mm。

项目中并存三套角度系统，混用会产生"方向相反"的 bug，写几何/渲染代码前必须确认所在层：

| 角度系统 | 正方向 | 用途 |
|----------|--------|------|
| 数据模型角 | CCW+（north=`[0,1]`=0°） | JSON 存储、几何运算 |
| 交互角 | CW+（`atan2(z, x)`） | 鼠标拖动 |
| Three.js 角 | CCW+（`rotation.y`） | 3D 渲染 |

转换规则权威在 `BIMCanvas.Web/README.md` 与 `src/utils/coordinates.ts`，此处不重述（避免双份维护漂移）。

## 2. OBB 规划师：AI 的空间抽象

BIMCanvas 的核心隐喻是 **AI 是 OBB 规划师（Oriented Bounding Box Planner）**：人和 AI 看到的是同一个空间的两层——

| | 人类（Web/SVG） | AI（JSON/Core） |
|--|----------------|----------------|
| 看到 | "皮"：家具的真实形状、材质、光影 | "骨"：世界由 OBB（有向矩形盒）构成，任何家具只取其外接矩形 |
| 关注 | 美感、氛围 | 拓扑（在区内、不重叠）、数值约束、语义方向 |

**AI 不计算精确几何，只决策 OBB 规划意图**——中心、尺寸、朝向。精确几何由 Core 编译：

```
AI 规划意图 (center + size + facing)
   └→ GeometryNormalizer.CreateRectangle()   # 朝北矩形 → 按 facing 旋转 → 平移到世界坐标
        └→ Polygon2D（4 顶点旋转矩形）→ 落盘为 Module.bounds
```

`bounds`（Polygon2D）是几何**真理**；朝向单独存为 **Facing 混合对象** `{value, semantic}`：

- `value`：单位向量，几何真理（north=`[0,1]`，朝向角以 north 为 0、逆时针为正，`atan2` 相对 Y 轴）；
- `semantic`：语义字符串（north/south/...），AI 的临时输入槽，可空，以 `value` 为准。

八方向语义↔向量映射由 `FacingHelper` 承担；`FacingConverter` 在反序列化时兼容历史格式（旧字符串、旧数组、新对象），序列化统一输出对象。

## 3. 几何转换链路与 Core 薄设计

`BIMCanvas.Core` 的定位是 **"薄数据契约 + 语义桥梁"**：定义通用数据模型、实现 AI 语义→几何转换、提供单位转换；**不做复杂几何运算**（委托 NetTopologySuite），**不持状态、不做业务决策**。

几何类型严格分层转换，禁止跨层：

```
Revit API (XYZ/Solid, feet, 项目坐标)
   ↕  RevitNtsConverter            （Revit 插件侧）
NTS (Polygon/Coordinate, feet → mm) ← CoordinateTransformer（原点偏移+旋转+单位）
   ↕  NtsConverter                 （Core 侧）
Core.Models (Polygon2D/Point2D/Facing, mm, Y-up)
```

关键类：

| 类 | 职责 |
|----|------|
| `GeometryNormalizer` | AI 规划意图（center+size+facing）→ Polygon2D |
| `PlacementValidator` | 布置验证——**只验证，不修正**（"床头靠墙"是 AI 的规划职责，不是 Core 的修正职责） |
| `CollisionDetector` | NTS 驱动的碰撞/包含判断（Intersects / Overlaps / IsWithin） |
| `NtsConverter` | Polygon2D ↔ NTS Polygon 双向转换（支持内环） |
| `NtsGeometryHelper` | NTS 算法库：布尔运算、多边形规范化、偏移内缩 |
| `UnitConverter` | feet↔mm、rad↔deg 常量转换 |

**NTS（NetTopologySuite）是底层几何引擎**——碰撞检测、布尔运算（Union/Difference/Intersection）、多边形规范化（合并共线段/去小边）、偏移内缩（computedBoundary）全部走它；Core 不自己实现浮点几何与精度处理。

## 4. 约束验证：两道防线

数据质量由两道**职责正交**的防线保障，分别拦截不同类的错误。它们都体现一条职责边界：**Server/平台持有约束验证，Agent 不做几何检查**。

| 防线 | 拦什么 | 现状实现 |
|------|--------|---------|
| **① 布置合法性**（`validate_layout`） | 模块超出设计区边界 / 模块间碰撞 / 与禁区重叠 三类硬错误 | Server 端点 `POST /api/validation/layout` 委派 `PluginValidatorOrchestrator` 执行**插件提供的 validators 脚本**（v3.4+ 下沉到 plugin，平台不内置 domain 校验规则；C# `SchemeValidator` 保留作对照） |
| **② 数据质量**（Load 闸门） | modules.json 反序列化 schema + 数据规范化 | `ModulesReaderService`（严格 wrapper 格式）+ `ModuleNormalizationService`（facing `semantic→value` 规范化等） |

一句话分工：**`validate_layout` 查"布置对不对"，Load 闸门查"数据干不干净"**。`validate_layout` 是几何/碰撞校验下沉插件后的入口；任何写入家具的步骤后都应过一遍它（workflow 编排会强制核验，见 [Arch_Workflow.md](./Arch_Workflow.md) §5）。

## 5. Server 持有的几何

平台基座（Server）一次算好所有派生几何，AI 只读不算：

| 几何 | 来源 / 算法 | 落点 |
|------|------------|------|
| 禁区 | 门扇扫过区 + 门前净空，从 `openings` 派生（`ComputedDataService`） | `computed/exclusions.json`（ez_*） |
| 可设计区 | 从 baseline `rooms` 派生（`ComputedDataService`） | `computed/room_zones.json`（rz_*） |
| 完成面内缩边界 | `FinishRules` 触发厚度 → `NtsGeometryHelper.SetOffset` 内缩 | Zone 的 `computedBoundary` |
| 边界段语义 | `ZoneBoundaryService` 标注每段是 wall/passage/door/window | 供 AI 理解"哪条边能靠、哪条是通道" |

这正是"Server 做几何、Agent 做决策"职责边界的落地：AI 拿到的是已经算好禁区与边界语义的"安全工作区"，它只需决定在区内怎么摆。

## 6. AI 空间理解：皮骨二分与视觉验证

AI 主要在"骨"层（OBB 拓扑 + 数值约束）工作，但纯几何无法捕捉"看起来对不对"。BIMCanvas 用**视觉验证**补这一环：布置完成、机器校验通过后，同一设计会话调 `canvas_vision`（截图/识图）做定点自评——问聚焦小问题（"床头柜与床是否贴邻？"），报警逐条处置。这把"外部视觉视角"和"内部设计上下文"闭合在同一会话里（详见 [Arch_Workflow.md](./Arch_Workflow.md) §4 落地段）。

## 7. 关联

| 主题 | 文档 |
|------|------|
| `bounds` / `facing` / Zone 字段级定义 | [Schema.md](./Schema.md) |
| 坐标系在总架构中的位置 | [Architecture.md](./Architecture.md) §8 |
| 视觉自评在工作流中的位置 | [Arch_Workflow.md](./Arch_Workflow.md) |
| 校验插件化（validators 脚本契约） | [Arch_Plugin.md](./Arch_Plugin.md) |
