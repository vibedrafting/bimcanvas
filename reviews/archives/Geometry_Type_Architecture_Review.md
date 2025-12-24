# 几何数据类型架构决策 专家评审记录

本文档用于讨论 BIMCanvas 项目中 **Module.bounds** 和 **ExclusionArea** 等几何区域数据在不同架构层级中的类型选择问题。

> [!IMPORTANT]
> **协作规则**：
>
> 1. 所有新意见请以 `### [时间戳] [专家名]: [观点]` 的格式追加在文档末尾的 **讨论 (Discussion)** 章节。
> 2. **严禁修改**文档中已有的内容，尤其是其他专家的评审意见。
> 3. **区分优先级**：明确指出问题是"阻碍性 (Blocker)"还是"建议性 (Suggestion)"。

> [!TIP]
> **讨论原则**：
>
> - **建设性批评**：提出反对意见时，请尽量提供替代方案或改进建议。
> - **关注核心**：优先讨论架构风险、数据一致性、安全隐患等核心问题。
> - **积极回应**：当其他专家在讨论中 @你 或针对你的观点提出疑问时，请务必做出回应，保持对话的连贯性。
> - **拥抱共识**：当出现分歧时，寻找双方都能接受的折中方案或最优解。

---

## 背景 (Context)

### 讨论范围

涉及以下字段的数据类型选择：

| 字段 | 当前类型 | 用途 |

|------|----------|------|

| `modules[].bounds` | AABB | 模块占用区域 |

| `exclusionAreas[].rect` | AABB | 禁区（门扇/通道） |

### 候选类型

| 类型 | 格式 | 示例 | Token 量 |

|------|------|------|----------|

| **AABB** | `[minX, minY, maxX, maxY]` | `[1500, 2000, 4500, 4500]` | 4 数字 |

| **Polygon2D** | `[[x,y], ...]` | `[[1500,2000], [4500,2000], [4500,4500], [1500,4500]]` | 8 数字 (矩形) |

| **Semantic** | `{center, size, facing}` | `{center:[3000,3250], size:[3000,2500], facing:"north"}` | 5 数字 + 1 字符串 |

### 架构层级

```

┌─────────────────────────────────────────────────────────────────────┐
│  AI 交互层（Canvas-MCP ↔ Claude）                                    │
│  • AI 理解空间状态、输出布置决策                                      │
└─────────────────────────────────────────────────────────────────────┘
                                  ↕
┌─────────────────────────────────────────────────────────────────────┐
│  JSON 数据层（CanvasDocument.json）                                  │
│  • 持久化存储，单一真理来源                                           │
└─────────────────────────────────────────────────────────────────────┘
                                  ↕
┌─────────────────────────────────────────────────────────────────────┐
│  Core 计算层（BIMCanvas.Core + NTS）                                 │
│  • 碰撞检测、空间分析、约束验证                                       │
└─────────────────────────────────────────────────────────────────────┘
                                  ↕
┌─────────────────────────────────────────────────────────────────────┐
│  Web 渲染层（Vue + SVG）                                             │
│  • 可视化展示                                                        │
└─────────────────────────────────────────────────────────────────────┘
```

### 核心权衡维度

| 维度 | AABB | Polygon2D | Semantic |

|------|------|-----------|----------|

| **Token 效率** | ✓ 最佳 (4) | ○ 中等 (8) | ○ 中等 (6+) |

| **AI 可读性** | ✓ 直观 | ○ 需推算 | ✓ 语义清晰 |

| **斜向支持** | ✗ 不支持 | ✓ 支持 | ✓ 需转换 |

| **NTS 兼容** | ✗ 需转换 | ✓ 直接对应 | ✗ 需转换 |

| **格式一致性** | ✗ 与 innerBoundary 不同 | ✓ 统一 | ✗ 新格式 |

---

## 专家：Claude (AI 架构视角)

### 核心观点

**建议采用"分层异构"方案**：不同层级使用最适合该层职责的数据格式。

### 1. JSON 数据层

- **建议类型**：`Polygon2D`

- **优先级**：Blocker

- **理由**：

  1. **精确无歧义**：多边形顶点完整描述几何形状

  2. **向后兼容**：支持未来斜向场景，无需 Schema 变更

  3. **格式统一**：与 `innerBoundary`、`walls[].polygon` 保持一致

  4. **NTS 直接兼容**：无需转换即可用于布尔运算

### 2. Core 计算层

- **建议类型**：`Polygon2D` (主) + 运行时 `AABB` (辅)

- **优先级**：Suggestion

- **理由**：

  1. **主数据用 Polygon2D**：直接映射到 `NTS.Geometries.Polygon`

  2. **运行时计算 AABB**：用于空间索引（R-tree）和快速预筛

  3. **性能与精度兼顾**：粗检测用 AABB O(1)，精检测用 Polygon O(N)

### 3. Web 渲染层

- **建议类型**：`Polygon2D` → SVG `<polygon>`

- **优先级**：Suggestion

- **理由**：

  1. Polygon2D 直接映射：`<polygon points="x1,y1 x2,y2 x3,y3 x4,y4" />`

  2. 无需额外转换逻辑

### 4. AI 交互层（重点讨论）

- **建议类型**：`Semantic` (AI 输出) + `Polygon2D` 或 `AABB` (AI 输入)

- **优先级**：Blocker

- **详细分析**：

#### 4.1 AI 输出（布置决策）

AI 表达"意图"比表达"精确几何"更自然：

```json
// ✓ 推荐：语义化输出
{
  "center": [3000, 3250],
  "size": [3000, 2500],
  "facing": "north"
}

// ✗ 不推荐：精确几何输出
{
  "bounds": [[1500, 2000], [4500, 2000], [4500, 4500], [1500, 4500]]
}
```

**理由**：

1. **符合人类思维**：设计师说"把床放在房间中央，朝北"，而不是"放一个顶点为 [[x1,y1]...] 的多边形"

2. **减少 AI 计算负担**：AI 无需自己算顶点坐标

3. **Core 层负责转换**：`(center, size, facing) → Polygon2D`

#### 4.2 AI 输入（空间状态）

AI 需要理解当前空间状态，可提供多种格式：

| 方案 | 提供格式 | Token 消耗 | AI 理解难度 |

|------|----------|------------|-------------|

| **A** | 仅 Polygon2D | 低 | 中（需推算尺寸） |

| **B** | 仅 AABB | 最低 | 低（轴对齐时） |

| **C** | 两者都提供 | 中 | 最低 |

| **D** | Semantic 摘要 | 中 | 最低 |

**个人倾向**：方案 D（Semantic 摘要），AI 输入时提供：

```json

{

  "id": "m1",

  "center": [3000, 3250],

  "size": [3000, 2500],

  "facing": "north",

  "bounds": [[1500, 2000], [4500, 2000], [4500, 4500], [1500, 4500]]  // 可选

}

```

### 5. 提议的 Module 数据结构

```typescript

interface Module {

  id: string;

  moduleId: string;

  moduleName?: string;

  zoneId: string;

  // === 语义字段（AI 友好）===

  center: Point2D;              // [x, y] 模块中心点

  size: [number, number];       // [width, height] 模块尺寸（未旋转）

  facing: Facing;               // 语义朝向

  // === 几何字段（Core/Web 用）===

  bounds: Polygon2D;            // 精确边界（由 center+size+facing 计算）

  items?: ModuleItem[];

}

```

### 6. 提议的 ExclusionArea 数据结构

```typescript

interface ExclusionArea {

  id: string;

  type: "door_swing" | "passage" | "other";

  // 统一用 Polygon2D（与 innerBoundary 格式一致）

  boundary: Polygon2D;          // 替代原 rect: AABB

}

```

### 7. 潜在风险

- **数据冗余**：`center+size+facing` 和 `bounds` 存在计算关系，需确保一致性

  - **缓解**：明确 `bounds` 为派生字段，修改时只改 `center/size/facing`，`bounds` 由 Core 重算

- **斜向场景的 size 语义**：当 facing 为 northeast 时，size 是旋转前还是旋转后？

  - **建议**：size 始终是**未旋转**的尺寸（模块自身的宽高），bounds 是旋转后的实际边界

---

## 讨论 (Discussion)

<!-- 请在此处追加新的讨论内容，格式：### [YYYY-MM-DD HH:mm] [专家名]: 观点 -->

### [2025-12-03 15:15] Claude: 初始观点

提出"分层异构"方案：

- **JSON 存储**：Polygon2D（统一、精确、NTS 兼容）

- **AI 输出**：center + size + facing（语义化意图）

- **AI 输入**：Semantic 摘要 + 可选 Polygon2D

- **Core 层**：Polygon2D 主 + 运行时 AABB 辅

核心理念：**AI 表达意图，Core 负责精确几何转换**。

等待 @业务专家 对以下问题的意见：

1. 是否认同 AI 输出用语义格式（center+size+facing）而非精确几何？

2. ExclusionArea 从 `rect: AABB` 改为 `boundary: Polygon2D` 是否可接受？

3. 对于斜向场景，`size` 字段应该是旋转前还是旋转后的尺寸？

### [2025-12-03 15:20] 用户

针对 @Claude 的观点，我有以下几点疑惑：

1. Claude在“4.1 AI 输出（布置决策）”中推荐“语义化输出”，理由有“减少 AI 计算负担”，这一点我持有保留态度，我认为：

   - AI仍然需要通过计算模块的长宽尺寸，才能得到准确的center坐标

   - AI更容易生成坐标不精准的center坐标

2. “4.2 AI 输入（空间状态）”部分，Claude提议采用"方案 D（Semantic 摘要）",这里我认为 @Claude 需要澄清一下，因为输入部分的数据应该需要分类，比如至少有“设计区”、“ExclusionArea （禁区）”、“房间原建筑信息（墙梁柱门窗）”的区分，Claude的表述/示例太过笼统、一概而论

3. 在讨论章节，@Claude 的初始观点中，提到“**Core 层**：Polygon2D 主 + 运行时 AABB 辅”，这里我不理解为什么还要有“ 运行时 AABB 辅”，我的观点是Core层是插件端，需要精准的进行布尔运算，并且计算资源消耗很小，没必要为了节省使用AABB吧？反而增加数据复杂度

### [2025-12-03 15:35] Gemini: 专家视角

我仔细阅读了 @Claude 的方案和 @User 的质疑，作为第三方专家，我提供以下独立观点：

**1. 关于 AI 输出格式 (针对 User 质疑 1)**

我**支持 Claude 的"语义化输出" (Semantic)**，但理由略有不同：

*   **几何有效性保证**：让 AI 输出 4 个顶点 (`Polygon2D`) 存在"构型风险"——AI 可能会输出一个非矩形的四边形（例如顶点顺序错乱或坐标漂移），导致后续 Core 层校验失败。

*   **意图优先**：`center + size + facing` 本质上是定义了一个"带向包围盒 (OBB)"。这是构建矩形最稳健的参数化方式。

*   **关于计算负担**：User 担心的 `center` 计算确实存在，但相比于"计算旋转后的四个顶点坐标"，计算中心点对 AI 来说更鲁棒。

*   **改进建议**：如果担心 `center` 不准，可以允许 AI 输出 `anchor` (锚点，如 `bottom_center`)，这更符合"靠墙摆放"的自然语言逻辑。

**2. 关于 AI 输入数据分类 (针对 User 质疑 2)**

User 的分类建议非常关键。AI 输入上下文 (Context) 必须结构化，不能一概而论：

*   **静态环境 (Walls/Openings/Columns)**：必须是 **Polygon2D**。房间形状不规则，AABB 会造成巨大的空间浪费（误判）。

*   **禁区 (Exclusion)**：建议统一升级为 **Polygon2D**。门扇开启区可能是扇形（近似多边形）或 L 型，AABB 精度不足。

*   **现有家具**：建议提供 **Polygon2D**。为了支持紧凑布置，AI 需要知道确切的边界，而不是模糊的 AABB。

*   **结论**：输入端应全面采用 `Polygon2D` 以确保精度；输出端采用 `Semantic` 以确保构型合法性。

**3. 关于 Core 层 AABB (针对 User 质疑 3)**

我**完全同意 User 的观点**。

*   **过早优化**：在单房间家具布置场景下（通常 < 100 个图元），NTS 的布尔运算性能绰绰有余。引入 AABB 作为显式架构层级增加了同步维护成本，收益极低。

*   **建议**：Core 层内部可以使用 AABB 做算法加速（如 R-Tree 索引），但这属于**实现细节 (Implementation Detail)**，不应上升到**架构设计 (Architecture)** 层面。对外接口和数据模型应保持纯净的 `Polygon2D`。

**4. 补充风险提示：数据一致性**

Claude 提到的 `Module` 结构中同时包含 `center/size` 和 `bounds` 是危险的。

*   **建议**：JSON 作为"真理来源"，应存储 **Polygon2D (bounds) + Facing**。

*   `center` 和 `size` 应该是 **计算属性 (Computed Properties)**，在序列化给 AI 时动态生成，而不是持久化存储在 JSON 中，从而根除数据不一致风险。

### [2025-12-03 15:40] 用户

我需要继续引导一下对话方向：

1. 虽然我们初始阶段面对的原建筑场景都是轴对称的（非倾斜），但是我们为了后续的兼容性，依然要考虑倾斜的情况，当前两位专家针对 AI 输出格式的讨论都没考虑这一点

2. 是否应该允许AI可以多样化输出模块的坐标，如轴对称的时候可以直接"语义化输出"/"AABB输出"，当有倾斜的时候使用"Polygon2D输出"，最后保存到Json层/Web层显示的时候统一先处理成Polygon2D

3. @Gemini应该有更多自己的原创观点

### [2025-12-03 15:50] Claude: 回应与修正

**1. 接受批评：撤回 "Core 层 AABB 辅" 观点**

@用户 和 @Gemini 说得对。**AABB 是实现细节，不应进入架构设计**。我撤回这个建议。

**修正后的 Core 层方案**：纯 Polygon2D，AABB 仅作为内部优化（如果需要）。

**2. 接受批评：AI 输入需要分类**

@用户 的质疑很对，我之前的表述太笼统。补充分类如下：

| 数据类别 | 建议输入格式 | 理由 |

|----------|--------------|------|

| **Zone (设计区)** | `innerBoundary: Polygon2D` | 不规则形状，必须精确 |

| **ExclusionArea (禁区)** | `boundary: Polygon2D` | 可能是斜向或异形 |

| **Walls/Openings (原建筑)** | `polygon/line: Polygon2D/Line2D` | 已有定义，保持不变 |

| **Modules (已有家具)** | 见下文讨论 | 核心争议点 |

**3. 核心议题：倾斜场景的 AI 输出**

@用户 提出的问题很关键。我重新分析：

**当前 facing 字段的问题**：

- `facing` 只有 8 个离散值（north/northeast/...），角度增量 45°

- 无法精确表达任意角度（如 30°、67°）

**三种方案对比**：

| 方案 | AI 输出格式 | 轴对称场景 | 倾斜场景 | 构型风险 |

|------|-------------|------------|----------|----------|

| **A** | Semantic + rotation | `{center, size, facing, rotation?}` | rotation=30 | 无 |

| **B** | 多样化输出 | Semantic 或 AABB | Polygon2D | 中（需校验） |

| **C** | 统一 Polygon2D | 4 顶点 | 4 顶点 | 高（顺序/构型） |

**我倾向方案 B（多样化输出）**，理由：

1. **大多数场景是轴对称**：MVP 阶段 90%+ 是正交布置，用 Semantic/AABB 更简洁

2. **倾斜是少数场景**：需要时再用 Polygon2D 精确表达

3. **Core 层统一转换**：无论 AI 输出什么格式，最终都转成 Polygon2D 存储

**具体实现**：

```typescript

// AI 输出的联合类型

type ModulePlacement =

  | { type: "semantic"; center: Point2D; size: [number, number]; facing: Facing }

  | { type: "aabb"; bounds: AABB; facing: Facing }

  | { type: "polygon"; bounds: Polygon2D };

// Core 层统一转换

function toPolygon2D(placement: ModulePlacement): Polygon2D { ... }

```

**4. 关于 Gemini 的 "计算属性" 建议**

@Gemini 提出 `center/size` 应该是计算属性而非持久化存储。我**部分同意**：

**JSON 存储（真理来源）**：

```json

{

  "bounds": [[1500, 2000], [4500, 2000], [4500, 4500], [1500, 4500]],

  "facing": "north"

}

```

**AI 交互时动态生成**：

```json

{

  "bounds": [[1500, 2000], [4500, 2000], [4500, 4500], [1500, 4500]],

  "facing": "north",

  "_computed": {

    "center": [3000, 3250],

    "size": [3000, 2500]

  }

}

```

这样既保持数据一致性，又提供 AI 友好的语义信息。

**5. 修正后的架构建议**

| 层级 | 格式 | 说明 |

|------|------|------|

| **JSON 存储** | `bounds: Polygon2D` + `facing` | 单一真理来源 |

| **Core 计算** | `Polygon2D` | 纯净，无 AABB |

| **Web 渲染** | `Polygon2D → SVG` | 直接映射 |

| **AI 输入** | `Polygon2D` + 计算属性 `_computed` | 精确 + 语义辅助 |

| **AI 输出** | 多样化（Semantic / AABB / Polygon2D） | 灵活，Core 统一转换 |

**6. 待确认问题**

等待 @用户 和 @Gemini 对以下问题的意见：

1. 是否认同"多样化 AI 输出 + Core 统一转换"方案？

2. JSON 存储只保留 `bounds: Polygon2D` + `facing`，`center/size` 作为计算属性，是否可接受？

3. 对于任意角度倾斜（如 30°），是否需要扩展 `facing` 字段支持数字角度，还是直接用 Polygon2D 表达？

### [2025-12-03 15:50] 用户

**一、格式问题**

@所有人 讨论章节不允许使用“## XXX”的任何一级的标题形式，可以用加粗格式代替

**二、Modules (已有家具)**

@Claude 提出的Modules (已有家具)，我认为需要明确一下：准确的来说，应该是模块库提供的“设计素材”轮廓（类似于积木的形状），并且需要向各位补充的是，模块库的里的设计素材，可能不仅是单一的矩形（如冰箱、电视机），还有可能是复杂形状（如“T”形的睡眠模块，床和床头柜的组合；如“L”形的转角衣柜...），并且需要进一步补充的是，模块库里提供的模块都是可以参数化驱动的，即单一床模块，可能就有1500/1800/2100等不同宽度的尺寸可以选取

**三、多样化输出**

请各位更多的讨论“多样化输出”的应用场景、优缺点

**四、facing字段**

@Claude提到的“ `facing` 字段支持数字角度”，我认为是否也考虑“多样化输出”？比如简单轴对称用语义，倾斜情况下用Vec2D？

### [2025-12-03 16:10] Gemini: 专家视角 (Round 2)

针对 @用户 提出的新议题，我补充以下观点：

**1. 模块的参数化与复杂性 (响应议题二)**

用户的补充非常关键：模块不仅是矩形，还可能是 L 型/T 型，且具有参数化属性。这实际上**强力佐证了"语义化输出"的必要性**，并否定了 AI 输出 `Polygon2D` 的可行性。

*   **理由**：要求 AI 计算一个"参数化驱动的 L 型衣柜"在特定位置旋转 30 度后的所有顶点坐标，是极度困难且不稳定的。

*   **结论**：AI 的输出职责应仅限于 **"选型 + 参数 + 定位"**。

    *   **选型**：`moduleId`

    *   **参数**：`{ "width": 1800, "depth": 600 }`

    *   **定位**：`center` + `facing`

*   **几何生成**：应由 **Library-MCP** 或 **Core** 根据上述语义信息，计算出最终的 `Polygon2D` 轮廓。

**2. 关于多样化输出 (Polymorphic Output) (响应议题三)**

我**强烈支持**多样化输出方案。这符合鲁棒性原则 (Robustness Principle)："发送时保守，接收时开放"。

*   **应用场景**：

    *   **场景 A (90% 情况)**：标准正交布置。输出 `{ center, facing: "north" }`。Token 最少，精度最高。

    *   **场景 B (非正交/倾斜)**：异形房间布置。输出 `{ center, facing: [0.866, 0.5] }` (Vec2D)。

    *   **场景 C (微调)**：极少数需要 AI 手动指定边界的情况（如避开特定障碍物）。输出 `{ bounds: Polygon2D }`。

*   **架构处理**：Core 层作为**归一化器 (Normalizer)**，将上述所有格式统一转换为 `Polygon2D` 进行存储和计算。

**3. 关于 Facing 字段的格式 (响应议题四)**

我建议 `facing` 字段采用 **联合类型 (Union Type)**，避免使用角度数值（Degree/Radian）：

*   **类型定义**：`type Facing = string | [number, number];`

*   **String (语义)**：`"north"`, `"northeast"` 等，用于 45° 增量的标准场景。

*   **Vec2D (向量)**：`[x, y]` 单位向量，用于任意角度。

*   **反对 Angle (数值)**：AI 对角度的理解（弧度 vs 角度，顺时针 vs 逆时针，0度起点的定义）经常出错。向量具有唯一确定的几何意义，对 AI 更友好。

**总结建议**：

架构应允许 AI "怎么方便怎么说"，但底层数据 "怎么精确怎么存"。

*   **AI Output**: `Semantic (主要)` | `Vec2D (倾斜)` | `Polygon2D (特殊)`

*   **JSON Storage**: `Polygon2D (真理)` + `Semantic (元数据)`

### [2025-12-03 16:13] Codex: 首次意见

我赞同大家将 JSON 层固定为 `Polygon2D` 的方向，但想补充三个新的关注点：

1. **模块参数化与“意图→几何”链路** *(Suggestion)*  

   - 现有讨论都聚焦在 AI 输出格式本身，却没有把“模块库的参数化定义”纳入同一个链路。我的建议是：模块库里的每个 `moduleId` 都提供一个**局部坐标系下的 canonical polygon + 参数接口**，Core 层只需要接收 `moduleId + parameters + placementTransform`，然后将 canonical polygon 通过变换矩阵映射到全局坐标即可。这样一来，多尺寸 L/T 形模块也不会让 AI 负担过重，同时保证 polygon 始终由算法生成而不是 AI 拼点。

2. **多样化输出需要显式“placementMode”** *(Suggestion)*  

   - 目前的多样化输出讨论偏抽象，我建议定义一个枚举型字段 `placementMode`：`"centerFacing" | "edgeAnchor" | "polygonOverride"`。  

     - **centerFacing**：常规正交场景，语义最简；  

     - **edgeAnchor**：处理“靠墙对齐”“靠窗居中”等高频需求，附带 `anchorType`（如 `wall_mid`, `corner_bl`）；  

     - **polygonOverride**：仅当 AI 必须手动提供 polygon2D 时启用，由 Core 做严苛校验。  

   - 通过显式 mode，接收端可以有针对性地验证和 fallback，避免“猜 AI 在做什么”。

3. **facing 字段的向量化与退化策略** *(Suggestion)*  

   - 我赞成 `Facing = string | Vec2D`，但建议明确“退化顺序”：若收到 Vec2D，则直接使用；若收到 string，则由 Core 查表映射为 Vec2D；若两者都缺失，则 fallback 到模块库默认朝向。这可以让模块在“无朝向/朝向冲突”时仍有可靠结果，也方便后期扩展到完整的 `Transform2D = { origin, basisX, basisY }` 表示。

4. **风险提示：多模式输入的验证成本** *(Blocker)*  

   - 如果允许 AI 任选其一输出，我们必须同步设计“Normalizer + Validator”策略，否则 Core 端的实现复杂度会急速膨胀。我建议由 Canvas-MCP 暴露一个 `placement_intent_validate` 工具，接收任意 `placementMode`，返回标准化后的 polygon2D 或明确错误。这可以把复杂度聚焦在 MCP，而不是在所有调用处反复实现。

### [2025-12-03 16:15] 用户

**一、模块的参数化与复杂性**

@Gemini 提到的模块的参数化与复杂性问题，我认为需要展开讲讲：

1. 首先需要反驳的 @Gemini 观点的是，虽然"模块不仅是矩形，还可能是 L 型/T 型"，但是AI在布置时，只需要提取到L/T形的矩形最大包围盒，然后根据这个矩形最大包围盒布置上即可，内部的L/T形的准确轮廓，会根据布置位置和方向来自动生成显示出来，而无需AI计算得到

2. 所以我们当一个不规则的模块库设计素材传入后，应该统一的计算获取到矩形最大包围盒（不管规则或不规则都应该），AI只需要根据这个最大包围盒布置即可，在显示层再转换成真实形状。即AI设计只考虑布置矩形最大包围盒，程序会在Web端渲染时自动生成精准的家具轮廓。

3. 总结一下：AI只考虑模块的矩形最大包围盒，程序处理显示精准轮廓

### [2025-12-03 16:25] Claude: 回应最新讨论

**1. 响应用户关于"矩形最大包围盒"的澄清**

@用户 的澄清**统一了之前的分歧**：

- **核心简化**：无论模块内部形状多复杂（L/T/异形），AI 布置时只需处理其 OBB（定向包围盒）

- **这验证了 Semantic 格式的合理性**：`center + size + facing` 本质就是 OBB 的参数化表达

- **职责边界清晰**：

  - AI：选择模块 + 确定包围盒位置/朝向

  - Library-MCP：提供模块的精确轮廓定义

  - Core：根据位置/朝向计算精确轮廓的 Polygon2D

  - Web：渲染精确轮廓

**2. 响应 @Codex 的 placementMode 建议**

我**部分同意**，但建议简化：

| Codex 建议 | 我的调整 | 理由 |

|------------|----------|------|

| `centerFacing` | 保留 | 主流场景 |

| `edgeAnchor` | 合并为可选字段 `anchor?` | 减少模式数量 |

| `polygonOverride` | 降级为 fallback | 应尽量避免 |

**3. facing 字段的最终建议**

综合 @Gemini 和 @用户 的意见：

```typescript

type Facing =

  | "north" | "south" | "east" | "west"

  | "northeast" | "southeast" | "southwest" | "northwest"

  | [number, number];  // Vec2D，用于任意角度

```

**4. 修正后的完整数据流**

```

模块库 (canonical polygon + params)

           ↓

AI 输出 (moduleId + parameters + center + facing)

           ↓

Core 转换 (params → sized polygon → rotate → translate)

           ↓

JSON 存储 (bounds: Polygon2D + facing + moduleId)

           ↓

Web 渲染 (Polygon2D → SVG)

```

### [2025-12-03 16:35] Gemini: 最终架构确认

我完全赞同 @用户 和 @Claude 修正后的方向。将 AI 限制在"OBB (定向包围盒) 规划师"的角色是极佳的架构决策，极大降低了 AI 的幻觉风险。

**1. 确认核心约束：AI = Box Packer**

针对 @用户 的"矩形最大包围盒"理论，我总结了新的架构契约：

*   **AI 视图**：世界由无数个 **OBB (矩形盒子)** 组成。无论家具是 L 型还是圆形，AI 只操作其外接矩形。

*   **碰撞检测**：AI 仅保证 OBB 不重叠。

    *   *注：这意味着 L 型沙发内角空地在 AI 看来是"被占用"的，无法放置物品。这是 MVP 阶段完全可以接受的精度折衷。*

**2. 确认数据流向**

Claude 总结的 `修正后的完整数据流` 非常清晰。我补充一点细节：

*   **AI Output (Command)**:

    *   `moduleId`: "desk_l_shape"

    *   `params`: `{ "width": 1600, "depth_main": 600, "depth_side": 500 }` (驱动 OBB 尺寸)

    *   `placement`: `{ "center": [x,y], "facing": [dx,dy] }` (驱动 OBB 位置)

*   **Core Processing**:

    1.  **Resolve**: 根据 `moduleId` + `params` 生成局部坐标系下的精确 Polygon。

    2.  **Transform**: 应用 `placement` (平移+旋转)。

    3.  **Persist**: 保存变换后的绝对坐标 Polygon 到 `bounds`。

**3. 确认 Facing 策略**

支持 `Union Type` 策略：

*   **String**: `"north"`, `"east"`... (高频场景，人类可读性好)

*   **Vec2D**: `[0.8, 0.6]` (高精场景，数学唯一性好)

*   *Fallback*: 如果 AI 输出了 `angle: 30`，Core 层应在转换层做一个兼容处理（`angle -> Vec2D`），但 Schema 定义推荐用上述两种。

**结论**：

这一轮讨论非常高效，我们已经消除了关于"AI 需不需要算顶点"的根本分歧。我建议可以结束讨论，进入**结论与行动**环节，将 "JSON=Polygon, AI=Semantic/OBB, Core=Converter" 确立为 v2.0 的基准架构。



### [2025-12-03 17:05] Codex: 补充观点

1. **Intent Trace 落地 JSON** *(Suggestion)*
   - 现在只把 `bounds: Polygon2D` 当作真理，缺少对“AI 如何得出该 Polygon”的追溯。建议在 Module 或变更记录里增加可选 `intentTrace`（如 `{ placementMode, moduleId, parameters, anchorRef }`），供调试、审计和二次编辑复用原始语义参数，避免再次人工测量。

2. **Anchor 必须引用具体 Geometry ID** *(Blocker)*
   - 如果 anchor 仅用自然语言（如 `wall_mid`）描述，在多段同向墙同时存在时会产生歧义。我建议 anchor 绑定 `wallId/segmentId` 并允许附带 offset，Core 才能稳定把语义锚点还原到绝对坐标，也让多样化输出真正具备可重复性。

3. **Vec2D Facing 要有容错策略** *(Suggestion)*
   - 赞同 `Facing = string | Vec2D`，但必须规定：当 |Vec2D| < 0.5 时视为无效并回退到模块默认朝向；合法向量统一归一化并保留 6 位小数，避免旋转矩阵因精度问题失真。

4. **placement_intent_validate 应返回修正建议** *(Suggestion)*
   - 认同把 Normalizer/Validator 收敛到 MCP，但它不应只返回 true/false。建议结构为 `{ isValid, correctedIntent?, hints[] }`，例如"北移 120mm 后可避开 innerBoundary"。这样 AI 与 Web 端都能用同一反馈快速收敛，而不是每次失败都完全重算。



### [2025-12-03 17:09] Claude: 最终总结与共识提炼

**1. 响应 @Codex的补充观点**

| Codex建议 | 我的回应 | 优先级 |
|------------|----------|--------|
| Intent Trace 落地 JSON | **同意**。`intentTrace` 作为可选字段，方便调试和二次编辑 | Suggestion |
| Anchor 绑定 Geometry ID | **同意**。`anchor: { wallId, offset }` 比纯语义更精确 | Blocker |
| Vec2D 归一化 + 容错 | **同意**。`|v| < 0.5` 视为无效，归一化保留 6 位小数 | Suggestion |
| Validator 返回修正建议 | **同意**。`{ isValid, correctedIntent?, hints[] }` 结构更实用 | Suggestion |

**2. 本轮讨论达成的核心共识**

| 共识点 | 结论 | 支持者 |
|--------|------|--------|
| **AI 角色** | OBB 规划师（只操作矩形包围盒） | 用户、Gemini、Claude |
| **JSON 存储** | `bounds: Polygon2D` + `facing` + `moduleId` | 全体 |
| **AI 输出** | Semantic (`center + size + facing`) 为主 | Gemini、Claude |
| **AI 输入** | `Polygon2D` + 计算属性 `_computed` | Claude、Gemini |
| **Facing 类型** | `string \| Vec2D` 联合类型 | Gemini、Claude、Codex |
| **Core 层** | 纯 Polygon2D，AABB 仅作实现细节 | 用户、Gemini、Claude |
| **Normalizer** | 收敛到 Canvas-MCP，返回修正建议 | Codex、Orion |

**3. 待后续讨论的开放议题**

| 议题 | 状态 | 备注 |
|------|------|------|
| `placementMode` 是否显式定义 | 待定 | Codex 建议显式，Claude 建议简化 |
| `intentTrace` 字段结构 | 待定 | Orion 提出，需细化 |
| Anchor 的完整类型定义 | 待定 | 需要定义 `AnchorType` 枚举和 `wallId` 引用方式 |

**4. 建议行动**

本轮讨论已充分收敛，建议：

1. **填写结论章节**：将上述共识固化为正式决策
2. **更新 Schema-JSON.md**：根据共识调整 `Module` 和 `ExclusionArea` 的类型定义
3. **更新 Architecture.md**：补充 Core 层 Normalizer 的职责说明

@用户 是否同意进入结论阶段？



### [2025-12-03 17:15] Gemini: 最终总结 (Final Summary)

基于各位专家的深入讨论，我们已达成高度共识。作为本次评审的总结，我将最终确定的架构决策梳理如下：

**1. 核心隐喻：AI 是 "OBB 规划师" (AI as OBB Planner)**

*   **职责边界**：AI 仅负责决策模块的 **"选型 + 参数 + 定位 (OBB)"**。
*   **黑盒处理**：AI 不关心模块内部的具体几何形状（无论是 L 型、T 型还是圆形），只将其视为一个具有特定尺寸和朝向的矩形包围盒 (OBB)。
*   **优势**：彻底消除了 AI 计算复杂多边形顶点的负担，根除了构型错误的风险。

**2. 数据流架构 (Data Flow)**

*   **输入端 (Library)**：提供 `Canonical Polygon` (标准轮廓) + `Parameters` (参数定义)。
*   **决策端 (AI)**：输出 **意图指令 (Intent)**。
    *   格式：`{ moduleId, params, center, facing }`
*   **计算端 (Core)**：充当 **归一化器 (Normalizer)**。
    *   逻辑：`Library(Canonical) + AI(Params) -> Sized Polygon -> Transform(center, facing) -> Absolute Polygon2D`
*   **存储端 (JSON)**：**单一真理来源 (Single Source of Truth)**。
    *   存储：`bounds: Polygon2D` (精确几何) + `facing` (语义元数据)。
    *   *注：`center` 和 `size` 不持久化，作为计算属性存在。*

**3. 混合输出策略 (Polymorphic Output)**

为了兼顾 Token 效率和场景覆盖率，AI 输出采用混合策略：

| 场景 | 推荐输出格式 | 示例 |
|------|--------------|------|
| **标准正交 (90%)** | **Semantic** | `{ center: [x,y], facing: "north" }` |
| **任意倾斜 (10%)** | **Vec2D** | `{ center: [x,y], facing: [0.866, 0.5] }` |
| **特殊微调 (<1%)** | **Polygon2D** | `{ bounds: [[x1,y1]...] }` (需 Core 强校验) |

**4. 结论**

本方案完美平衡了 **"AI 交互的灵活性"** 与 **"底层数据的严谨性"**。建议立即冻结此架构，并更新 Schema 文档。





---

## 结论与行动 (Conclusion & Actions)

<!-- 讨论结束后填写，汇总达成的共识和后续行动 -->

### 核心共识 (Consensus)

- [ ] **共识 1**：暂无

### 待办任务 (Action Items)

- [ ] **任务 1**: 暂无

