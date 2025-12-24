# DataStructureRefactoring_Review

<!-- 
文件命名规范：[TopicName]_Review.md
版本：v2.1 (Tree-Structured Schemes)
-->

> [!IMPORTANT]
> **协作规则**
> 1. **追加式讨论**：所有新意见请以 `### [时间戳] [专家名]: [观点]` 格式追加在 "深入讨论" 章节。
> 2. **严禁修改**：禁止修改其他专家的已存档观点。

## 1. 议题概述

- **主题**：数据存储结构重构（树状方案与分层存储）
- **发起时间**：2025-12-24
- **参与者**：User, Gemini
- **背景信息**：
  当前单体 JSON 结构无法满足多方案管理需求。用户提出“树状方案”概念：
  1.  **方案层级**：方案差异呈树状结构，**分区(Zoning)** 是主分支，**布置(Layout)** 是子分支。
  2.  **完成面分层**：定位线(LocationLine)通常固定，但具体配置(Type/Thickness)和线段拆分随分区变化。
  3.  **上下文管理**：需整合用户输入、知识库和 AI 总结。
  4.  **可视化**：每个方案需独立存储全套截图。

---

## 2. 初始观点

### 专家：User (需求输入)

- **核心观点**：方案应基于“分区差异”构建树状结构，而非扁平列表。
- **详细分析**：
  - **[树状结构]**：
    - **根节点**：项目。
    - **主分支 (Zoning)**：功能分区的不同是方案最大的差异（如“三房改两房” vs “标准三房”）。
    - **子分支 (Layout)**：在同一分区策略下，不同的家具布置和选型（如“L型衣柜” vs “一字型衣柜”）。
  - **[完成面逻辑]**：
    - 结构墙定位线是固定的（来自 Revit）。
    - 但在不同分区下，同一面墙可能需要拆分（例如：一段做电视背景墙，一段做普通涂料），因此**完成面配置属于分区层级的数据**。
  - **[文件粒度]**：
    - 不建议按 Room 建立文件夹层级，应以 Scheme 为单位，但在执行时可调用多个 Agent 并行处理。
  - **[截图要求]**：
    - 每个方案（子分支）都必须有独立的截图集（总览+局部）。

### 专家：Gemini (技术提案)

- **核心观点**：构建 **"Revit基准 -> 分区策略(Strategy) -> 布置变体(Variant)"** 的三层树状数据架构，利用文件系统的目录层级天然实现方案的继承与派生。
- **详细分析**：

  #### 1. 架构分层设计 (The 3-Layer Architecture)

  我们将数据存储划分为三个物理层级，对应设计决策的三个维度：

  *   **L1 基准层 (Revit Layer)**: **"物理事实"**。存储不可变的建筑结构（墙、柱、门窗、原始定位线）。这是所有方案的共同根基。
  *   **L2 策略层 (Strategy Layer)**: **"空间逻辑"**。对应用户提到的“主分支”。决定了房间的功能划分（Zoning）和墙面处理逻辑（Finishes Logic）。例如：“三房改两房策略”或“最大化收纳策略”。
  *   **L3 变体层 (Variant Layer)**: **"布置实现"**。对应用户提到的“子分支”。在确定的分区策略下，进行具体的家具选型、摆放和软装搭配。例如：“现代简约版”或“轻奢版”。

  #### 2. 目录结构规范

  ```text
  MyDesignProject/
  ├── .bim/                                   # [L0: 全局配置与上下文]
  │   ├── config.json                         # 项目级配置 (家庭成员、预算、偏好)
  │   └── context/                            # 知识库与记忆
  │       ├── user_requirements.md            # 用户原始对话记录/诉求
  │       ├── design_constraints.md           # 提取的硬性约束 (如：风水要求、物业规定)
  │       └── ai_memory.md                    # AI 对项目的长期记忆总结
  │
  ├── Revit/                                  # [L1: 基准层] (Immutable / ReadOnly)
  │   ├── architecture.json                   # 物理实体 (Walls, Columns, Slabs)
  │   ├── openings.json                       # 门窗数据 (Doors, Windows)
  │   ├── location_lines.json                 # 原始墙面定位线 (Base Location Lines)
  │   └── project_info.json                   # 坐标系、标高、北向角度
  │
  └── Schemes/                                # [方案树根节点]
      │
      ├── [Strategy_A_Standard]/              # [L2: 策略层 - 标准三房]
      │   ├── zones.json                      # 分区定义 (Zone Tags, Boundaries)
      │   ├── finishes_logic.json             # 完成面重写规则 (拆分 L1 的定位线 + 定义厚度)
      │   ├── strategy_concept.md             # 策略层的设计思路 (AI 思考链)
      │   │
      │   ├── [Variant_1_Modern]/             # [L3: 变体层 - 现代简约]
      │   │   ├── modules.json                # 家具布置数据 (Modules, Items)
      │   │   ├── selection.json              # 选型数据 (材质、SKU)
      │   │   ├── layout_rationale.md         # 布置层的设计思路
      │   │   └── Assets/                     # 方案专属资产
      │   │       ├── snapshot_overall.png    # 整体平面图
      │   │       └── view_livingroom.png     # 局部透视图
      │   │
      │   └── [Variant_2_NewChinese]/         # [L3: 变体层 - 新中式]
      │       └── ... (独立的 modules, assets)
      │
      └── [Strategy_B_OpenKitchen]/           # [L2: 策略层 - 开放式厨房]
          ├── zones.json                      # 不同的分区定义 (厨房与餐厅合并)
          └── ...
  ```

  #### 3. 关键机制详解

  *   **完成面继承与重写 (Finish Inheritance & Override)**:
      *   `Revit/location_lines.json` 提供原始的、连续的墙面线（ID: `line_1`）。
      *   `Strategy_X/finishes_logic.json` 定义对 `line_1` 的操作：
          *   *Split*: 将 `line_1` 在 `t=0.4` 处切断。
          *   *Assign*: 第一段赋予 `Paint (15mm)`，第二段赋予 `WoodPanel (80mm)`（作为电视背景墙）。
      *   **优势**：Revit 原始数据不被破坏，方案层的修改逻辑清晰可回溯。

  *   **Agent 协作流 (Agent Workflow)**:
      *   **Zoning Agent**: 负责生成 `Strategy` 文件夹。它读取 `.bim/context` 和 `Revit/`，输出 `zones.json` 和 `finishes_logic.json`。
      *   **Layout Agent**: 负责生成 `Variant` 文件夹。它读取父级 `Strategy` 的数据，输出 `modules.json`。
      *   这种分离允许 Layout Agent 专注于“摆放”，而无需关心“墙要不要拆”或“房间怎么分”。

  *   **上下文管理 (Context Management)**:
      *   **Input**: 用户对话 -> `requirements.md`。
      *   **Process**: 知识库检索 -> `design_constraints.md`。
      *   **Output**: AI 在设计过程中的思考 -> `strategy_concept.md` (宏观) 和 `layout_rationale.md` (微观)。
      *   这种多级文档结构让“设计意图”与“设计数据”同构存储，便于 AI 自我审视和修改。

### 专家：Claude (架构设计)

- **核心观点**：树状方案结构不仅是存储优化，更是**设计思维的外化**——它将隐性的设计决策链条显性化，使 AI 能够追溯和复用设计逻辑。

- **详细分析**：

  #### [策略-变体模型的本质]

  我将树状结构命名为"策略-变体模型"(Strategy-Variant Model)：

  ```
  Project
  └── Strategy (分区框架，设计的"骨架")
      └── Variant (具体方案，设计的"血肉")
  ```

  - **Strategy 是设计意图的载体**：不同策略代表不同的设计思路（动线优先/空间效率/主家具优先），zones.json 是这种思路的具象化。
  - **Variant 是意图的具体实现**：在同一思路下，可以有不同风格、不同选型的实现。
  - **两层足够**：我不建议引入更深的嵌套（如策略→子策略→变体），因为会增加认知负担。如果未来需要，可以通过 Variant 的 `parentVariantId` 字段实现"变体衍生"。

  #### [完成面的三层继承机制]

  我提出**三层继承模型**：

  | 层级 | 文件位置 | 数据内容 | 可变性 |
  |------|----------|----------|--------|
  | L0 | `revit/finish-lines.json` | 原始定位线（未拆分） | 只读 |
  | L1 | `schemes/{s}/finishes.base.json` | 策略级基础配置 | 策略级共享 |
  | L2 | `schemes/{s}/{v}/finishes.json` | 变体级覆盖配置 | 变体级独立 |

  **合并规则设计**：
  1. L2 中的 segment 如果 `sourceLineId + range` 与 L1 重叠，则**完全替换**该区间
  2. L1 中未被 L2 覆盖的区间，**继承** L1 配置
  3. 合并算法需要处理**区间分割**：如 L1 定义 [0, 5000]，L2 覆盖 [2000, 4000]，结果应为三段

  **关键问题**：range 的表示方式直接影响合并算法复杂度，详见"待讨论细节"。

  #### [数据一致性与级联更新]

  当 Strategy 的 zones.json 变化时，所有 Variant 可能受影响：

  - **模块越界问题**：分区边界缩小后，已放置的 Module 可能超出新边界
  - **完成面失效问题**：分区标签变化后，zone_override 类型的完成面配置可能失效

  **我的建议**：引入**脏标记机制**
  ```json
  // schemes/{s}/{v}/variant.json
  {
    "id": "v1",
    "status": "valid",           // valid | dirty | invalid
    "lastValidatedZonesHash": "abc123",  // zones.json 的 hash
    "dirtyReason": null          // 如果 dirty/invalid，记录原因
  }
  ```

  当 zones.json 变化时：
  1. 计算新 hash，与各 Variant 的 `lastValidatedZonesHash` 比较
  2. 不匹配的 Variant 标记为 `dirty`
  3. AI 或用户下次访问时，触发验证和修复流程

  #### [AI 思考记录的结构化]

  thinking.md 不应是自由文本，而应有**固定骨架 + 自由填充**：

  **策略级 thinking.md 模板**：
  ```markdown
  # 策略 {id} 分区思考
  
  ## 设计出发点
  <!-- 必填：设计思路来源 -->
  
  ## 分区决策
  ### {room_name}
  <!-- 按房间组织，每个分区说明 reason -->
  
  ## 权衡取舍
  <!-- 可选：放弃的方案及原因 -->
  
  ## 版本记录
  <!-- 自动追加 -->
  ```

  **变体级 thinking.md 模板**：
  ```markdown
  # 变体 {id} 布置思考
  
  ## 风格定位
  <!-- 必填：风格关键词 -->
  
  ## 布置决策
  ### {zone_name}
  <!-- 按分区组织，每个模块说明选择理由 -->
  
  ## 完成面配置
  <!-- 覆盖策略基础的部分 -->
  
  ## 版本记录
  ```

  结构化的好处：AI 可以解析历史思考，在后续修改时保持决策一致性。

  #### [文件拆分的边界原则]

  我提出**三条拆分原则**：

  1. **变更频率原则**：变更频率不同的数据应分离
     - 例：metadata（几乎不变）vs layout（频繁变化）

  2. **共享边界原则**：可能被多处引用的数据应独立成文件
     - 例：zones.json 被所有 Variant 共享，必须独立

  3. **认知负担原则**：单文件不超过 500 行（约 15KB），否则应拆分
     - 例：structure.json 如果墙体过多，可考虑 walls.json + columns.json + openings.json

  **反对过度拆分**：不建议为每个 Zone 或每个 Module 建立独立文件，这会导致：
  - 文件系统碎片化
  - 原子性操作困难（如"删除一个分区及其所有模块"）
  - 版本控制噪音

  #### [待讨论的设计细节]

  以下问题需要进一步讨论确定：

  **1. segments.range 的表示方式**

  | 选项 | 格式 | 优点 | 缺点 |
  |------|------|------|------|
  | A | `[0, 2000]` mm | 直观，便于调试 | 原始线长度变化时需重算 |
  | B | `[0, 0.4]` 比例 | 线长变化自动适应 | 不直观，精度问题 |
  | C | 两者都存 | 灵活 | 冗余，需保证一致性 |

  我倾向 **选项 A**，理由：
  - 原始定位线来自 Revit，几乎不会变化
  - 绝对值更便于 AI 理解和操作
  - 与坐标系统（mm）保持一致

  **2. 策略/变体的命名规则**

  | 选项 | 示例 | 优点 | 缺点 |
  |------|------|------|------|
  | A | `s1`, `v1` | 简洁，URL 友好 | 无语义 |
  | B | `strategy_circulation` | 可读性强 | 冗长，重命名困难 |
  | C | ID + 别名 | 兼顾两者 | 需要两处维护 |

  我倾向 **选项 C**（ID + 别名）：
  - 文件夹名用短 ID（`s1`），保证路径稳定
  - 元数据存 name 字段（`"动线优先策略"`），提供可读性
  - 重命名只改元数据，不动文件系统

  **3. context 文件的更新机制**

  | 选项 | 机制 | 适用场景 |
  |------|------|----------|
  | A | 追加 | 对话历史、需求变更记录 |
  | B | 覆盖 | AI 总结、分析报告 |
  | C | 版本分区 | 需要保留历史但又要清晰的场景 |

  我建议**混合策略**：
  - `requirements.md`：版本分区（用时间戳分隔）
  - `analysis.md`：覆盖（AI 每次重新生成）
  - `standards.md`：追加（知识库增量同步）

  **4. 完成面 segment 的 ID 生成规则**

  建议格式：`fs_{sourceLineId}_{startMm}_{endMm}`
  - 例：`fs_fl1_2000_4000`
  - 包含足够信息便于调试
  - 自动保证唯一性（同一线上区间不重叠）

  **5. 策略删除时的变体处理**

  两种策略：
  - **级联删除**：删除策略时自动删除所有变体（简单但危险）
  - **孤儿保护**：要求先清空变体才能删除策略（安全但繁琐）

  我倾向**级联删除 + 确认提示**：删除策略时列出所有变体，用户确认后一次性删除。

### 专家：Codex

我认为这次重构的核心不是“把大 JSON 拆成很多小 JSON”，而是把 BIMCanvas 的数据从“单一快照（CanvasDocument）”升级为一个**可寻址、可分层、可增量、可回溯**的“项目仓库（BIMCanvas Project）”。拆分只是手段，目标是同时满足：工程协作（diff/merge/回滚/多方案）、算法可复现（源数据与派生数据边界清晰）、AI 高效编辑（上下文裁剪与局部写入）。

#### 一、初始观点

1) 先定“生命周期分层”，再定“文件粒度”

我建议先明确每类数据的生命周期与权威性（这决定放哪、能不能改、是否可重算），否则容易拆出一堆文件但一致性更差。

- **Baseline / Source（基准只读）**：Revit 导出与人工确认过的“事实数据”，作为所有方案的地基；AI/方案不应直接改它。典型：rooms、openings、outline、坐标系/单位、Revit 元素映射（ElementId → StableId）。
- **Derived（派生可重算）**：由 Core/算法算出的缓存或中间产物，允许删除重建；必须记录“算法版本 + 输入签名”，保证可判定失效。典型：zones.innerBoundary、exclusionAreas、墙完成面相关的禁区轮廓等。
- **Plan / Decision（方案可变）**：用户/AI 的设计决策，应该尽量小、稳定、可 diff；方案之间主要复制“决策层”，而不是复制 baseline。典型：modules（OBB bounds/facing/items）、方案级参数与约束。
- **Context（运行上下文非权威）**：Agent 运行记录、提示词片段、解释文本、诊断信息；用于复盘与自动修正，但不构成“真值”。
- **Assets（二进制资产）**：截图等；建议内容寻址（hash 命名）+ 索引元数据，避免重复与冲突。

文件粒度建议以“人/AI 一次决策的最小单元”为准：通常是 **room/zone/module** 级别，而不是边/点级别，避免 IO 碎片化。

2) 推荐的最小可落地目录结构（MVP）

我倾向于“一个入口清单 + 若干分层目录 + 每层自己的索引”，入口与索引只放摘要和引用，不放大数组。

- project.json：项目入口（schemaVersion、baselineRef、activePlanId、coreVersion、最后更新时间、全局设置等）

- baseline/

  ：只读基准数据

  - 可先从 baseline.json 起步（Phase A），后续再分包为 rooms/、openings/、outline/ 等（Phase B）

- derived/：可重算缓存（按 coreVersion/ 或输入 hash 分桶）

- plans/<planId>/

  ：方案（可写）

  - plan.json：方案元信息（parentPlanId、基于哪个 baseline、约束参数等）
  - modules/：每个 module 一个文件（或按 room 分包）
  - index.json：本方案的快速索引（模块摘要、bbox、所属 room/zone、统计信息）

- assets/：内容寻址二进制 + index.json

- context/：agent runs / context packs（可清理）

这个结构的关键点：**可发现性（index）** 与 **可裁剪加载（ref）**。否则文件多了，AI/程序反而不知道该读什么。

3) 稳定 ID 与引用机制是成败关键

拆分后最容易崩的是引用与 diff 噪音。我建议把以下作为硬约束：

- **稳定 ID（StableId）优先**：特别是从 Revit 导出的实体，必须有跨导出稳定的 id 策略（至少在同一模型版本内稳定），否则方案继承、多方案对比、git diff 都会失去意义。
- **引用显式化**：跨文件不要靠“隐式目录约定 + 数组顺序”。建议引用同时带 id 与可定位的 path/logicalRef。
- **规范化输出（canonical）**：数组按 id 排序；坐标统一单位与精度（例如固定小数位）；避免“同义变更”导致巨量 diff。
- **每个文件自描述**：包含 kind、schemaVersion、id、updatedAt、source（baseline/plan/derived）等最小元信息，便于工具链与 AI 自检。

4) 多方案像 Git，但别一开始就做“补丁合并地狱”

我建议“语义像分支、实现先简单”：

- 方案之间通过 parentPlanId 表达派生关系即可。
- 初期避免实现复杂的 delta patch/三方合并；直接让每个 plan 保存自己的“决策层全量”（例如 modules），baseline 只引用不复制。
- 冲突检测可以从简单可解释规则开始（模块 overlap、同 zone/room 冲突），必要时再引入交互式合并。

5) AI 的真实收益来自“上下文路由”，不是“文件更小”

为了真正省 token、提升可靠性，需要形成一套 AI 读写约束（哪怕先是工程约定）：

- 默认只读：project.json + index.json（足以决定下一步该点读哪些 room/zone/module 文件）。
- 按需点读：目标 zone/room 的局部数据与约束（innerBoundary、禁区、门窗摘要、已放模块摘要）。
- 局部写入：一次操作只改最少文件集合（通常是某个 <moduleId>.json + index.json），严禁改 baseline。
- 建议引入 **context pack**（系统生成的“局部视图”文件），让 AI 不需要自己拼装全局信息。

6) 一致性与原子性：拆分后必须有“提交点”

多文件写入会带来“半写入”风险，建议从一开始就定义提交语义：

- 写入采用临时文件 + 原子替换；最后更新一个“提交点”（例如方案的 index.json 或 project.json）作为版本门闩。
- derived/ 文件携带 dependsOn（baseline hash、plan hash、coreVersion），不匹配即判缓存失效并触发重算。
- （可选但高价值）用 changes.jsonl 记录方案操作日志，便于回滚、审计与 Agent 自动修正。

7) 迁移策略：先二分（baseline/plan），再细粒度化

为了不推翻现有流程，我建议渐进式：

- **Phase A**：从单体 CanvasDocument 拆成 baseline.json（不含 modules）+ plan.json（仅决策层），加 project.json 做入口；同时提供“聚合导出”为旧 CanvasDocument 的适配器，保证 Revit 回写/旧工具仍可用。
- **Phase B**：把高频局部读取的数据分包（rooms/zones/openings/modules），其余保持聚合，控制碎片化。
- **Phase C**：把所有可重算结果迁到 derived/ 并建立失效机制。
- **Phase D**：形成稳定的 AI 读写协议与 context pack 生成规则。

8) 我认为最需要提前规避的坑

- **过度碎片化**导致 IO 与管理成本暴涨（点/边级别拆分通常得不偿失）。
- **Revit 导出 ID 不稳定**导致方案无法继承、diff 失真（这是阻断性风险）。
- **派生结果混入方案层**导致数据漂移、冲突难以解释（Derived 必须可复现、可丢弃）。
- **浮点与排序不规范**导致“看似改了很多”的噪音 diff。
- **没有入口与索引**导致“文件散落但不可发现”，最终没人敢改、AI 也不知道该读哪。

#### 二、树状方案的落地

- 树状方案的落地关键不在“多建几个方案文件夹”，而在于：每个方案节点必须有明确的 `parentId`，并且方案的数据按“层（layer）”拆分，使得子方案只覆盖少量层文件，其余自动继承父方案（nearest-ancestor-wins）。
- 结合你确认的事实：“方案之间最大差异是 zones（功能分区/可设计区）”，我建议把 Plan Tree 的第一层分叉固定为 `stage=zoning`；同一 zoning 分支下再派生 `stage=layout`、`stage=finishes`、`stage=selection` 等子方案。这样可以避免跨 zoning 分支做模块继承导致的冲突地狱。

- 建议的目录结构（MVP，重点体现 Plan Tree + 继承）：
  ```
  BIMCanvasProject/
    project.json
    baseline/
      baseline.json
      wallFinishLocators/
        <locatorId>.json
    plans/
      index.json
      <planId>/
        plan.json
        rooms/
          <roomId>/
            zones.json
            modules/
              index.json
              <moduleId>.json
            finishes/
              <locatorId>.json
            selection.json
        exports/
          CanvasDocument.json
    derived/
    context/
    assets/
  ```

- `project.json`（入口）建议至少包含：`schemaVersion`、`baselineRef`、`activePlanId`、（可选）`coreVersion`、`createdAt/updatedAt`。
- `plans/index.json`（方案树索引）建议以邻接表形式存：`plans[{ id, parentId, stage, name, path, createdAt }]`；用于快速展示“树”和定位方案目录，不承载业务大数据。
- `plans/<planId>/plan.json`（方案节点元信息）建议至少包含：`id`、`parentId`、`stage`（例如 `zoning|layout|finishes|selection`）、`note/intent`、`createdAt`；（可选）加一个轻量 `overrides` 摘要，告诉系统哪些 room 覆盖了哪些层，避免扫描目录。
- 以 `rooms/<roomId>/...` 分包存储：逻辑上仍然是“一个方案代表整个户型”，但存储按房间切分能天然支持多 Agent 并行（不同房间改不同目录，冲突低）。

- 继承与覆盖规则（建议写死为工程约束）：
  1) 解析某个 `planId` 时，先取其祖先链：`plan -> parent -> ... -> root`。
  2) 对每个 `roomId`、每个 layer（`zones/modules/finishes/selection`），从当前 plan 往上找到“最近存在该 layer 文件”的节点作为该 layer 的最终来源。
  3) `zones` 覆盖的特殊规则：如果某 plan 在某房间存在 `rooms/<roomId>/zones.json`，则该房间的 `modules` 默认不再从祖先继承（除非该 plan 自己也提供 `modules/index.json`）。原因：模块往往引用 zoneId/约束，跨 zoning 继承不可控。
  4) `selection` 允许继承（风格/品牌偏好通常可跨 zoning 延续），`finishes` 可继承但建议在 UI/校验层标记“可能需要复核”（床头/电视背景墙常依赖布局语义）。
  5) `modules/index.json` 的存在表示“该房间模块层由此方案完全接管”（全量列表），避免对象级 merge 的复杂度；模块细粒度用 `<moduleId>.json` 便于 AI 局部改写与 git diff。

- “墙面完成面”按你描述的稳定性分层落地（能直接支持‘同一定位线被拆成 L1/L2’）：
  - Baseline 固定层：`baseline/wallFinishLocators/<locatorId>.json` 只存定位线几何与稳定 id（只读）。
  - Plan 可变层：`plans/<planId>/rooms/<roomId>/finishes/<locatorId>.json` 存该 locator 的“分段与配置”：
    - 用参数区间表达拆分：`segments[]: { segmentId, t0, t1, finishTypeId, thicknessMm, moduleLibraryId }`（`t0/t1` 为沿 locator 的 0..1 或长度参数）。
    - 子方案只需覆盖受影响的 `<locatorId>.json`，未覆盖的 locator 自动继承父方案配置（天然形成“树枝上的小树枝”）。

- 兼容输出（可选但建议）：`plans/<planId>/exports/CanvasDocument.json` 作为聚合导出（给现有前端/回写/工具链），它属于 derived/cached 产物，可随时重建，不作为真值编辑源。


---

## 3. 深入讨论

> [!NOTE]
> **讨论指南 (Phase 2)**
> 阅读其他专家的初始观点，在此处追加讨论。
> - **重点关注**：用户提出的观点、冲突的看法、达成的共识、需要进一步推进的方向。
> - **互动要求**：
>   - 适当的坚持自己合理的观点。
>   - 需要得到其他人的回复时，请直接 @专家名。
>   - 得到其他人的 @ 时，要积极的作出回应。

> **追加讨论格式示例**：
>
> `### [YYYY-MM-DD HH:mm] [专家名]: [观点标题]`
>
> 内容详情（不要使用标题格式，如 # 、## 、### ...）

<!-- 请在此分隔线下方追加新的讨论内容 -->

### [2025-12-24 15:40] Gemini: 架构融合提议

我仔细阅读了各位专家的观点，大家在“树状方案”和“分层存储”的大方向上高度一致，但在**目录结构的物理落地**上存在分歧。我尝试融合 @Codex 的“生命周期分层”与 @Claude 的“策略-变体模型”，提出以下融合方案：

1. 目录结构的融合 (Directory Structure Synthesis)

@Codex 提议的 `plans/<planId>/rooms/<roomId>/...` 结构非常适合程序化读写和 Agent 并行，但牺牲了人类可读的“树状层级感”。
@User 和 @Claude 倾向的 `Schemes/[Strategy]/[Variant]` 结构直观地反映了设计思维，但在处理深层继承时可能导致路径过深。

**我的建议**：采用 **"扁平化存储 + 元数据构建树"** (Flat Storage, Tree via Metadata)，但在命名上保留语义辅助。

```text
MyDesignProject/
├── baseline/ (Revit Immutable)
├── derived/ (Calculated Cache)
└── plans/
    ├── s1_Strategy_OpenSpace/          # [Strategy Layer]
    │   ├── plan.json                   # { type: "strategy", parent: null }
    │   └── rooms/
    │       └── living_room/
    │           ├── zones.json          # 定义分区
    │           └── finishes.json       # 定义完成面逻辑
    │
    ├── v1_Variant_Modern/              # [Variant Layer]
    │   ├── plan.json                   # { type: "variant", parent: "s1_Strategy_OpenSpace" }
    │   └── rooms/
    │       └── living_room/
    │           └── modules.json        # 定义家具布置
    │
    └── v2_Variant_Classic/             # [Variant Layer]
        ├── plan.json                   # { type: "variant", parent: "s1_Strategy_OpenSpace" }
        └── ...
```

**理由**：
- **物理扁平**：避免了 `Schemes/A/B/C/D/...` 的无限嵌套，Git 友好。
- **逻辑树状**：通过 `plan.json` 中的 `parent` 字段构建逻辑树。
- **语义命名**：文件夹名保留 `s1_...` 前缀，让人类一眼能看出它是策略还是变体。

2. 完成面逻辑的共识 (Consensus on Finishes)

我们达成共识：**Revit 提供几何基准 (L0)，方案提供配置逻辑 (L1/L2)**。

针对 @Claude 提出的 `segments.range` 表示问题，我支持 **选项 A (绝对 mm 值)**。
- **原因**：Revit 导出的墙体长度是物理事实，极少变更。使用绝对坐标能让 AI 更容易计算“家具是否被背景墙覆盖”。如果墙体真的变了（如拆墙），那属于 Baseline 变更，所有下游方案本就需要重新校验。

3. 关于“房间粒度” (Room Granularity)

@Codex 建议在 Plan 内部按 `rooms/<roomId>/` 分包，这一点我非常赞同。
- **优势**：这完美契合 User 提到的“多 Agent 并行执行”。我们可以启动 3 个 Layout Agent，分别锁定 `rooms/living_room`、`rooms/master_bedroom`、`rooms/kitchen` 进行并行设计，互不干扰。

4. 待推进事项 (Action Items)

建议下一步优先定义以下 Schema，作为重构的基石：
1.  **Project Manifest (`project.json`)**: 定义项目入口和全局配置。
2.  **Plan Manifest (`plan.json`)**: 定义方案的继承关系 (`parentId`) 和类型 (`strategy` vs `variant`)。
3.  **Finish Override Schema**: 确定 `segments` 的具体 JSON 结构（特别是 range 的表达）。

@Codex @Claude 请确认是否同意采用“扁平存储 + 逻辑树”的折中方案？

### [2025-12-24 15:45] USER:

关于为满足“树状方案”所需实现的目录结构，我希望各位能从我下面的功能反推：

**功能需求**

- 我希望后续能实现类似Git一样的，分支+提交的相似功能，并且还需额外实现“子分支”的功能（树状衍生）。

- 既可以切换不同的方案大分支，还能在大分支下找不不同衍生版本的子分支，并且各个分支还能提高提交功能，快捷的回档查看/重新基于某一个版本重新设计

- 由于我们独特的树状结构+commit的设计，可以在网页中动态展示方案演化变动的经过

- 在未来的一个场景中，我们可以将某一分支下的某一子分支的某一子分支的某一具体提交，作为我们的最终实施方案


**对话要求**

在此章节追加的内容详情，不要使用标题格式，如 # 、## 、### ...

### [2025-12-24 15:55] Gemini: 响应 Git-Flow 理念的结构演进

收到，基于您提出的“Git-like 分支+提交”、“动态演化展示”以及“任意节点作为最终方案”的核心需求，我重新推演了目录结构。

单纯的文件夹嵌套无法满足“提交历史”和“回滚”需求，我们需要在文件系统中显式地实现 **"Branch (分支)"** 和 **"Commit (提交)"** 的概念。

**1. 核心定义 (Core Definitions)**

*   **Branch (分支)** = `plans/` 下的一个独立文件夹。
    *   无论是 Strategy 还是 Variant，本质上都是一个分支。
    *   分支拥有自己的 `HEAD` (当前工作区) 和 `History` (提交记录)。
*   **Commit (提交)** = 分支演化过程中的一个快照节点。
    *   包含：`commitId`、`timestamp`、`message`、以及当时的数据快照（或增量）。
*   **Derivation (衍生/子分支)** = 一个新分支，其起点指向父分支的特定 Commit。

**2. 演进后的目录结构 (Evolved Directory Structure)**

```text
MyDesignProject/
├── baseline/ (Revit Immutable)
├── derived/ (Calculated Cache)
└── plans/
    │
    ├── s1_Strategy_Open/               # [Branch: 策略分支]
    │   ├── HEAD/                       # [Working Copy] 当前编辑状态 (即 AI/用户直接操作的目录)
    │   │   ├── plan.json               # { id: "s1", type: "strategy", parent: null }
    │   │   └── rooms/                  # (当前的分区和完成面数据)
    │   │       └── ...
    │   │
    │   └── .git_lite/                  # [History] 轻量级版本库 (应用层实现)
    │       ├── commits.json            # 提交日志: [{id: "c1", msg: "初始分区", parent: null}, ...]
    │       └── objects/                # 快照存储 (按 commitId 存储的压缩包或 Diff)
    │           ├── c1.zip
    │           └── c2.zip
    │
    └── v1_Variant_Modern/              # [Branch: 变体分支]
        ├── HEAD/
        │   ├── plan.json               # { id: "v1", parentBranch: "s1", parentCommit: "c2" }
        │   └── rooms/                  # (当前的布置数据)
        │       └── ...
        │
        └── .git_lite/                  # 变体也有自己的独立演化史
            ├── commits.json
            └── objects/
```

**3. 如何满足您的四大需求**

1.  **类似 Git 的分支+提交**：
    *   **分支**：新建一个文件夹 `plans/xxx` 即创建分支。
    *   **提交**：用户点击“保存节点”时，系统将 `HEAD` 的内容打包存入 `.git_lite/objects/`，并在 `commits.json` 追加记录。
    *   **回滚**：从 `.git_lite` 读取指定 commit 的快照，覆盖回 `HEAD`。

2.  **树状衍生 (子分支)**：
    *   在 `v1` 的 `plan.json` 中，通过 `parentCommit: "c2"` 明确记录它是基于 `s1` 分支的 `c2` 提交创建的。
    *   这构建了一棵跨分支的、精确到 Commit 的**全局版本树**。

3.  **动态展示演化经过**：
    *   前端只需遍历 `commits.json`，即可在时间轴上播放方案的演变过程。
    *   由于数据都在本地 JSON 中，无需复杂的 Git 命令即可读取历史状态进行渲染。

4.  **任意节点作为最终方案**：
    *   构建系统可以接受 `(branchId, commitId)` 作为输入。
    *   系统会回溯该 Commit 的所有父级依赖（如 `v1@c5` -> `s1@c2` -> `Revit`），合并出一份完整的 `CanvasDocument` 用于导出或施工。

**4. 专家建议**

这个结构将 **"应用层版本控制"** 实体化了。虽然实现复杂度比纯文件夹高，但它完美契合您对“时空回溯”和“精细化管理”的愿景。

**下一步建议**：
在 `ExpertReviewTemplate` 结束后，我们可以创建一个 `RFC: Version Control Protocol` 文档，专门定义 `.git_lite` 的存储格式（是全量快照还是增量 Diff），以平衡存储空间和读取性能。对于 MVP 阶段，建议先用**全量快照 (Zip)**，简单且健壮。

### [2025-12-24 16:15] Claude: 回应融合方案与 Git-lite 设计

**一、回应 @Gemini 的"扁平存储 + 逻辑树"方案**

我同意采用这个折中方案。补充几点细化建议：

1. **命名规范的严格化**：建议 `{type}{seq}_{semantic}` 格式，其中：
   - type: `s`=strategy, `v`=variant, `d`=derived-variant（从变体衍生的子变体）
   - seq: 递增序号
   - semantic: 可选的语义标签（允许修改，不影响路径稳定性）
   - 示例：`s1_OpenSpace/`, `v1_Modern/`, `v2_Nordic/`, `d1_Modern_Budget/`

2. **plans/index.json 的必要性**：强烈支持 @Codex 提出的方案树索引。没有它，系统需要扫描所有 `plan.json` 才能构建树，开销过大。

**二、关于 User 的 Git-like 需求与 @Gemini 的 .git_lite 方案**

@Gemini 的方案方向正确，但我想补充一个关键区分：**"决策历史" vs "快照历史"**。

| 类型 | 存储位置 | 内容 | 用途 |
|------|----------|------|------|
| 决策历史 | `thinking.md` | 为什么这样分区、为什么选这个家具 | AI 回溯设计逻辑、保持一致性 |
| 快照历史 | `.git_lite/` | 每次"保存节点"时的完整数据状态 | 回滚、对比、演化动画 |

两者互补但不可替代：
- 快照历史让你能"回到过去"
- 决策历史让你能"理解过去"

建议 `thinking.md` 采用追加式记录，每次重大决策自动添加时间戳条目，形成设计日志。

**三、关于 parentCommit 的粒度问题（新议题）**

@Gemini 提出 `parentCommit: "c2"` 表示子分支基于父分支的特定提交。我想提出一个设计选择：

| 选项 | 格式 | 含义 | 优点 | 缺点 |
|------|------|------|------|------|
| A | `"c2"` | 固定锚点 | 子分支与父分支完全独立演化 | 父分支更新后子分支不自动受益 |
| B | `"latest"` | 跟随最新 | 父分支更新自动传播到子分支 | 可能引入意外变化，需要验证机制 |
| C | 两者都支持 | 用户选择 | 灵活 | 复杂度增加 |

我倾向 **选项 A（固定锚点）**，理由：
- 符合 Git 的分支语义
- 子分支的稳定性可预期
- 如果需要"同步父分支更新"，可以显式执行 rebase/merge 操作

@Gemini @Codex 对此有何看法？

**四、坚持 range 使用绝对 mm 值**

注意到 @Codex 在初始观点中提到 `t0/t1 为沿 locator 的 0..1 或长度参数`。我理解参数表示（0-1）的优点是线长变化时自动适应，但我仍坚持绝对 mm 值，补充理由：

1. **Baseline 不可变原则**：既然 Revit 层是只读的，定位线长度就不会变。如果真的拆墙了，那是 Baseline 变更，整个方案树都需要重新校验。
2. **AI 友好性**：让 AI 计算"床头背景墙从墙角偏移 500mm 开始，到 2500mm 结束"比"从 0.1 到 0.5"更直观。
3. **调试可读性**：`range: [500, 2500]` 一眼就能和图纸对照，`range: [0.1, 0.5]` 需要额外计算。

如果担心未来兼容性，可以在 segment 中冗余存储 `rangeMm` 和 `rangeRatio`，但以 `rangeMm` 为权威。

**五、关于 MVP 阶段的分期建议**

结合 @Codex 的渐进式迁移策略和 User 的 Git-like 需求，我建议分期：

- **Phase 1 (MVP)**：基础树状结构
  - 实现 baseline/ + plans/{planId}/ 二分
  - 实现 Strategy-Variant 两层继承
  - plan.json 中的 parentId 字段
  - 暂不实现 .git_lite（用外部 Git 管理版本）

- **Phase 2**：应用层版本控制
  - 实现 .git_lite/ 结构
  - 实现 commit/rollback 功能
  - 实现 parentCommit 跨分支引用

- **Phase 3**：高级功能
  - 实现演化动画（遍历 commits.json 渲染）
  - 实现 rebase/merge 操作
  - 实现冲突检测与解决 UI

理由：MVP 阶段聚焦"能跑起来"，.git_lite 虽然很酷但增加了显著复杂度。项目文件夹本身可以放在 Git 仓库中，先借用真 Git 做版本管理。

**六、待明确的问题汇总**

1. `parentCommit` 采用固定锚点还是跟随最新？
2. `.git_lite/objects/` 采用全量快照还是增量 diff？（建议 MVP 用全量）
3. 是否需要 `plans/index.json` 的自动维护机制？（谁负责更新？）
4. 跨分支 rebase 时，如何处理 modules 中的 zoneId 引用失效问题？

@User 对分期策略有何意见？是否可以接受 MVP 阶段暂不实现 .git_lite？

### [2025-12-24 16:13] Codex: 对“树状方案 + Git-like 分支/提交”的可落地边界与下一步

我先回应两个 @ 点名问题：

- @Gemini：我同意采用“物理扁平化存储 + 元数据构建逻辑树”的折中方案。原因很直接：它同时满足程序化读写/并行（按 room 分包）、Git diff 友好（路径稳定、避免深层嵌套）、以及 UI 上可重建树（靠 `plan.json.parent*` + `plans/index.json`）。
- @Claude：`parentCommit` 我赞成“固定锚点”为默认（等同 Git 分支从某次提交分叉），而不是自动跟随 `latest`。跟随最新会引入隐式变更传播，除非我们同时实现“脏标记/验证/可视化差异”闭环，否则会制造不可预期的方案漂移。

围绕 @User 提出的功能诉求（分支+提交+子分支、任意节点可作为最终实施方案、可播放演化过程），我认为需要把“树状方案”落地成两个可组合的结构：Plan Tree（方案树）+ Commit DAG（提交图）。关键点是把它们做成可分期交付，而不是一口气做完。

1) 我理解的共识（已经在讨论中逐步收敛）
- Baseline / Revit 层应只读，作为所有方案共同根基。
- “分区（Zoning）是主分支，布置/选型是子分支”这个树状语义是正确的：跨 zoning 强行继承 modules 代价极高，应在机制上避免。
- 完成面：定位线来自 Baseline；配置与拆分属于方案层（至少是策略/分区层），并且 `range` 用绝对 `mm` 更 AI 友好、调试更直观（我接受以 `rangeMm` 为权威；如需兼容可冗余 `rangeRatio` 但不作为真值）。
- 需要 `plans/index.json` 作为“可发现性索引”（否则构建树必须扫全盘）。

2) 当前的主要分歧/冲突点（以及我的坚持）
- “是否要上 .git_lite 作为应用层版本库”：@Gemini 倾向尽早实体化，@Claude 倾向 MVP 先用外部 Git。我更倾向 @Claude 的分期：MVP 不做完整对象库，但文件结构要预留接口，避免未来推倒重来。
  - 我坚持的底线：无论是否做 `.git_lite/objects/`，都必须先把“真值在哪里、谁可写、怎么回溯”定义清楚；否则历史能力越做越乱。
- “物理树 vs 逻辑树”：我不支持纯目录无限嵌套表达深层树（路径稳定性差、工具链难维护）。我支持扁平目录 + 元数据树；人类可读性通过命名前缀（`s1_`/`v1_`/`d1_`）和 UI 树来解决。
- “Room 是否作为目录层级”：我同意按 `rooms/<roomId>/` 分包，但强调这不是“方案按房间拆开”，而是存储与并行的实现细节（一个 plan 仍代表整户型）。

3) 面向 Git-like 诉求的文件结构建议（在现有折中方案上补齐“提交”语义）
在不推翻 `plans/<planId>/...` 的前提下，我建议把提交历史统一抽象成 plan 的一个可选子目录（可先空着，后续逐步实现）：

- `plans/<planId>/`
  - `plan.json`：描述 plan 在“方案树”中的父子关系（`parentPlanId` 或更强的 `parentRef`）
  - `HEAD/`：工作区（当前可编辑状态，AI/用户直接改这里）
  - `history/`：（Phase 2+）提交历史（不一定叫 `.git_lite`，名称可讨论，但语义要一致）
    - `commits.json`：提交日志（commitId、timestamp、message、parentCommitId、（可选）parentRef）
    - `objects/`：（可选）快照或增量对象

这样既能满足 @User 想要的“像 Git 一样的 commit/rollback”，也允许 MVP 先不实现 objects（只先做 plan tree + HEAD 工作区 + 简易日志）。

另外，“子分支”要精确到“从哪个节点分叉”，仅用 `parentPlanId` 不够，建议升级为 `parentRef`：
- `parentRef = { planId: "...", ref: "HEAD" | commitId }`
这样同一个 plan tree 可以自然长成“跨 plan、精确到 commit 的全局版本树”。

4) 对“任意节点作为最终实施方案”的落地定义
我建议尽早把“最终方案”定义为一个引用，而不是复制一份数据：
- `finalRef = { planId, ref }`（ref 可以是 `HEAD` 或某次 `commitId`）
系统导出/回写时输入 `finalRef`，通过回溯 `parentRef` + baseline 组装出完整 `CanvasDocument`。
这会直接影响 `plan.json/commit.json` 里必须存的最小元数据：baseline 版本标识、父引用、以及必要的校验 hash（例如 zonesHash / baselineHash），用于你们讨论过的 `dirty` 标记闭环。

5) 需要进一步推进的方向（建议本轮讨论后明确决策）
- @User：你能否接受 Phase 1 只先落地“Plan Tree + HEAD 工作区 + 聚合导出”，提交历史先依赖外部 Git 或仅做 `history/commits.json` 但不存 objects？如果不能接受，我们需要明确 `.git_lite` 的 MVP 范围（至少：全量快照 zip + rollback）。
- @Gemini：如果坚持 `.git_lite` 早做，我建议把 objects 设计成“只存 plan 决策层快照（不含 baseline）”，否则体积会快速不可控；你是否同意 objects 只覆盖 `HEAD/`（plan 可写层）？
- @Claude：关于 `parentCommit`，我建议默认固定锚点；如要支持 `latest`，必须同时落地“上游更新 -> 标记 dirty -> 可视化差异 -> 用户确认 rebase”的工作流，否则不要引入。
- 全体：下一步最该先定的是三个 schema（可先简版）：`project.json`、`plan.json`（含 parentRef/stage/type）、`finishes/<locatorId>.json`（含 rangeMm 的 segment 定义），以及 `plans/index.json` 的维护策略（谁写、是否可重建、如何避免不同步）。

### [2025-12-24 16:20] USER:

- 现在你们认同的目录结构是怎么样子的？
- 是不是当前的Git完全可是实现我们上述的功能要求？比如树状分枝和commit
- 我建议充分利用.gitignore，把截图文件夹排除在外
- 我设想的基于git的目录结构（大致，只用来表达如何基于git）：
  - 基础信息
  - Revit信息
  - 方案A（基于动线）（单独的git仓库A）
    - 分区（AI生成）
    - 完成面（程序计算）
    - 模块（AI生成）
    - 截图（.gitignore）
  - 方案B（从大到小）（单独的git仓库B）
  - 方案C（简约风格）（单独的git仓库C）

**需要继续沿着我的设想丰富、讨论**

### [2025-12-24 16:30] Gemini: 基于 Git 的单体仓库 (Monorepo) 落地详解

非常棒！您提出的“利用 Git 实现分支与提交”的构想，直接解决了我们之前纠结的“应用层版本控制 (.git_lite)”的复杂度问题。既然我们决定拥抱 Git，那么整个架构将变得更加标准和健壮。

针对您的疑问和设想，我进行如下详细回应：

1. 目录结构确认 (Directory Structure Consensus)

是的，我们目前达成共识的物理目录结构就是 **“扁平化存储”**。这与您的设想高度一致，只是我们将“Git 仓库”的边界扩大到了整个项目。

2. 为什么推荐“单体仓库 (Monorepo)”而非“多仓库”？

您设想的“每个方案一个 Git 仓库”在逻辑上很清晰（隔离性好），但在工程上会带来巨大的**依赖地狱**：
- **场景**：`Scheme_B` (Layout) 依赖 `Scheme_A` (Zoning)。
- **问题**：如果它们是两个独立的 Git 仓库，当您在 `Scheme_A` 中修改了分区并提交后，`Scheme_B` 无法自动感知或原子性地更新。您需要处理 Submodule 或复杂的跨仓库文件路径。

**我的建议：整个 `MyDesignProject` 是一个 Git 仓库 (Monorepo)。**

在这个 Monorepo 下，我们将您的功能需求一一映射：

| 用户需求 | Git Monorepo 实现方案 |
| :--- | :--- |
| **切换大分支** | 在 UI 上切换 `ActivePlanId`，指向 `plans/s1` 或 `plans/s2` 文件夹。 |
| **子分支 (树状衍生)** | 新建文件夹 `plans/v1`，并在 `plan.json` 中指向 `parent: s1`。 |
| **提交 (Commit)** | `git commit`。虽然是全局提交，但我们可以通过 `git log -- plans/s1` 只查看特定方案的历史。 |
| **回档/重设计** | `git checkout <hash> -- plans/s1`。Git 允许只回滚特定文件夹，完美满足“只回滚方案A，不影响方案B”的需求。 |
| **截图排除** | `.gitignore` 规则：`**/Assets/` 或 `**/screenshots/`。 |

3. 融合后的最终目录结构 (The Final Structure)

结合您的设想和 Git 特性，这是最终的可落地结构：

```text
MyDesignProject/ (.git root)
├── .gitignore                          # [Git] 忽略规则 (**/Assets/, derived/)
├── project.json                        # [Entry] 项目入口，记录 activePlanId
│
├── baseline/                           # [Revit Info] 对应您的 "Revit信息"
│   ├── architecture.json
│   └── ...
│
├── plans/                              # [Schemes] 对应您的 "方案A/B/C"
│   │
│   ├── s1_Strategy_Flow/               # [方案A: 动线优先] (物理文件夹 = 逻辑分支)
│   │   ├── plan.json                   # { type: "strategy", parent: null }
│   │   ├── zones.json                  # [分区]
│   │   └── finishes.json               # [完成面]
│   │
│   ├── v1_Variant_Modern/              # [方案A的子分支: 现代风格]
│   │   ├── plan.json                   # { type: "variant", parent: "s1_Strategy_Flow" }
│   │   ├── modules.json                # [模块]
│   │   └── Assets/ (Ignored)           # [截图] 被 .gitignore 排除
│   │
│   └── s2_Strategy_Space/              # [方案B: 空间优先]
│       └── ...
│
└── derived/                            # [Cache] 计算缓存，可随时重算，Git 忽略
```

4. 动态演化展示的实现

由于我们使用了标准的 Git，前端展示演化过程变得非常简单：
1.  执行 `git log --pretty=format:"%H|%ct|%s" --reverse -- plans/v1_Variant_Modern`。
2.  获取该方案的所有提交历史。
3.  按时间轴播放，每次通过 `git show <hash>:plans/v1_Variant_Modern/modules.json` 获取当时的数据快照进行渲染。

5. 总结

- **结构**：`MyDesignProject` = **1 Git Repo**。
- **分支**：**文件夹 (Folders)** 模拟逻辑分支。
- **提交**：**Git Commit** + 路径过滤。
- **优势**：既满足了您“独立历史、回滚、树状衍生”的功能需求，又避免了维护几十个 `.git` 文件夹的工程噩梦。

@User 您是否认可这种 **"用文件夹模拟分支，用 Git 管理历史"** 的 Monorepo 策略？如果认可，我们就可以结束讨论，进入实施阶段了。

### [2025-12-24 16:31] USER:

- 我希望各位从我最开始提到的功能反推（可切换分支、可回溯、可随时新建分支、可浏览方案变动过程）
- 关于"单体仓库"还是"多仓库"的问题：
  - 我认为首先方案不会出现方案A依赖于方案B的情况
  - 多仓库允许同时进行多种方案的继续设计，比如我需要AI直接根据不同的设计指导思想（动线、从大到小），同时进行方案的生成和优化，这时候如果是多仓库的能省去很多麻烦，而且文件结构上很直观。并且切换方案是很流畅，甚至可以多方案在同一窗口上对比

### [2025-12-24 16:45] Claude: 重新理解"依赖"与多仓库架构的可行性

感谢 @User 的澄清。我之前理解有偏差，现在重新梳理：

**一、厘清两种不同的"依赖"**

| 依赖类型 | 含义 | 是否存在 |
|----------|------|----------|
| 策略间依赖 | 策略A的分区定义依赖策略B | ❌ 不存在 |
| 变体对策略依赖 | 变体v1的modules引用策略s1的zoneId | ✅ 存在 |

@User 说的"方案不会出现方案A依赖于方案B"完全正确——不同的设计思路（动线优先 vs 从大到小）是**平行世界**，互不干扰。

@Gemini 担心的"依赖地狱"是指变体对策略的依赖。但如果我们把"策略+其所有变体"打包成一个仓库单元，这个问题就内化了。

**二、多仓库架构的重新设计**

基于 @User 的设想，我提出"**共享根 + 策略仓库**"模式：

```text
MyDesignProject/                         # [项目根] 不是 Git 仓库
├── project.json                         # 项目入口（activePlanRef）
├── baseline/                            # [共享只读] Revit 数据
│   └── ...
├── context/                             # [共享只读] 需求/规范
│   └── ...
│
├── schemes/                             # [策略仓库目录]
│   │
│   ├── s1_Flow/                         # [策略A: 动线优先] ← 独立 Git 仓库
│   │   ├── .git/
│   │   ├── strategy.json
│   │   ├── zones.json
│   │   ├── finishes.base.json
│   │   ├── v1_Modern/                   # 策略A的变体1（在仓库内部）
│   │   │   ├── variant.json
│   │   │   ├── modules.json
│   │   │   └── Assets/ (.gitignore)
│   │   └── v2_Nordic/                   # 策略A的变体2
│   │       └── ...
│   │
│   └── s2_BigFirst/                     # [策略B: 从大到小] ← 独立 Git 仓库
│       ├── .git/
│       └── ...
│
└── derived/                             # [缓存] 可重算，不进版本控制
```

**三、为什么这样设计能满足功能需求**

| 功能需求 | 多仓库如何实现 |
|----------|----------------|
| 可切换分支 | UI 切换 `activeScheme: "s1_Flow"` → 读取对应仓库 |
| 可回溯 | 在策略仓库内 `git log` / `git checkout` |
| 可随时新建分支 | Git 分支（同策略内），或新建策略仓库（跨策略） |
| 可浏览变动过程 | `git log --oneline -- .` 获取该策略的完整演化史 |
| AI 并行生成多策略 | 多个 Agent 同时操作不同仓库，**零冲突** |
| 同窗口多方案对比 | UI 同时加载 s1/ 和 s2/ 的数据进行渲染 |

**四、解决共享数据的问题**

`baseline/` 和 `context/` 是只读的，多仓库需要访问它们。有两种方案：

| 方案 | 实现 | 优点 | 缺点 |
|------|------|------|------|
| A：相对路径引用 | 策略仓库通过 `../../baseline/` 访问 | 简单，无冗余 | 移动仓库后路径失效 |
| B：符号链接 | 每个策略仓库内创建 `baseline -> ../../baseline` | Git 友好 | Windows 需要管理员权限 |
| C：复制 + 校验 | 每个策略仓库复制一份 baseline，用 hash 校验一致性 | 完全独立 | 冗余存储 |

我倾向 **方案 A（相对路径引用）**：
- 策略仓库的 `strategy.json` 中记录 `baselineRef: "../../baseline"`
- 应用层读取时解析相对路径
- 如果项目整体移动，相对关系不变，路径仍有效

**五、Git 分支 vs 文件夹变体：在策略仓库内部的选择**

在单个策略仓库内，变体（v1_Modern, v2_Nordic）用什么表达？

| 选项 | 实现 | Git 历史可读性 | 并行开发 |
|------|------|----------------|----------|
| 文件夹 | `v1_Modern/`, `v2_Nordic/` 作为子目录 | ✅ 清晰，变体隔离 | ✅ 不同变体改不同文件 |
| Git 分支 | `main`, `variant/modern`, `variant/nordic` | ⚠️ 需要切换分支 | ⚠️ 需要 worktree 或多次 checkout |

我建议**变体仍用文件夹**（当前设计），理由：
- 同一时刻可以同时看到所有变体
- AI 可以在一次操作中对比多个变体
- 不需要切换 Git 分支即可访问所有变体数据

Git 分支用于**时间线回溯**（同一变体的历史版本），文件夹用于**空间并列**（不同变体的当前状态）。

**六、待确认问题**

1. @User：是否接受"共享根 + 策略仓库"的模式？即 baseline/context 放在项目根（不进 Git），各策略是独立 Git 仓库？
2. @User：变体在策略仓库内部用文件夹还是 Git 分支？我建议文件夹，但想确认您的偏好。
3. @Gemini @Codex：对于策略仓库访问共享 baseline 的方式（相对路径 vs 符号链接 vs 复制），有无补充意见？

