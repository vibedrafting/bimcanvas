# T2：知识体系重构 + 其余房间策略 Skill

> 依赖：T1（主工作流框架 + 房间 Skill 接口规范 + generate-bedroom 范例）
> 上游文档：`plans/workflow-refactor/overview.md`

---

## 一、改造目标

1. **新建 design_principles.md**：从 placement_guide 提取跨房间通用原则（≤100 行），替代 652 行的旧知识库
2. **新建 generate-bathroom/SKILL.md**：卫生间策略 Skill，模板匹配工作流（≤150 行）
3. **新建 generate-livingroom/SKILL.md**：客厅策略 Skill 框架级定义（≤80 行），完整策略待 T3 分区能力后充实
4. **更新 generate-workflow/SKILL.md**：感知阶段引用从 placement_guide → design_principles

完成后效果：
- Agent 能用新知识体系布置**卧室**（T1）和**卫生间**（T2）
- 客厅 Skill 框架就绪，待 T3 分区能力后可执行
- placement_guide 退役，旧知识体系完成迁移

---

## 二、修改范围

### 2.1 design_principles.md（新建）

**源码路径**：`BIMCanvas.Server/Templates/knowledge/design_principles.md`

**定位**：跨房间的通用设计原则。每个原则 = WHY + 关键数值速查。不含任何房间特定策略。

**注意力预算**：≤ 100 行

**内容来源**：从 placement_guide.md §1-§6 提取，按"原理+速查混合"模式重写。

**提取规则**：

| placement_guide 章节 | 提取到 design_principles? | 说明 |
|---------------------|--------------------------|------|
| §1.1 全局规划（动线、纵深、采光） | ✅ 提取 | 跨房间通用 |
| §1.2 评估维度（空间效率、功能联动） | ✅ 提取 | 跨房间通用 |
| §1.3 异形利用 | ❌ 不提取 | 非通用原则，属于 generate-zoning（T3） |
| §2 布置优先级（锚点→主要→辅助） | ✅ 提取 | 跨房间通用 |
| §3 空间约束（4条硬约束） | ✅ 提取 | 跨房间通用 |
| §4.1 通道分类判断 | ✅ 提取 | 通用方法论 |
| §4.2 通用通道宽度 | ✅ 提取 | 主通道/次通道标准 |
| §4.3 房间特定使用距离 | ❌ 不提取 | 各房间 Skill 自定义 |
| §5.1 成套家具概念 | ✅ 提取 | 跨房间的依赖关系模式 |
| §5.2 各房间必须vs可选表 | ❌ 不提取 | 各房间 Skill 自定义 |

```
结构草案：

# 通用设计原则

> 跨房间的设计原理和标准。房间特定策略在各房间 Skill 中定义。

## 一、空间分析三要素

### 动线
WHY：动线决定人在空间中的行为流畅度。好的动线让人无意识中觉得空间顺畅。
- 主通道（贯穿空间）≥ 900mm
- 次通道（到达家具）≥ 600mm
- 判断标准：人需要从此处走过到达其他区域 → 通行间隙；仅在此处使用家具 → 使用间隙

### 纵深
WHY：纵深层次创造空间感。扁平摆放让空间显得拥挤，有前后层次才有呼吸。
- 入口 → 核心区 → 窗侧，形成自然的空间层次
- 大家具靠墙，小家具可居中或填充

### 采光
WHY：采光影响空间感知和使用舒适度。遮挡采光会让空间变暗变压抑。
- 窗前留出采光通道（勿用高家具遮挡主采光面）
- 功能区采光需求：工作/阅读区 > 休息区 > 存储区

## 二、布置优先级

锚点 → 主要 → 辅助。先确定功能核心，再围绕它组织空间。

| 层级 | 定义 | 位置特征 | 示例 |
|------|------|---------|------|
| 锚点 | 功能核心 | 由空间决定，几乎无选择 | 卧室的床、客厅的沙发 |
| 主要 | 功能必须 | 由锚点+空间共同决定 | 衣柜、茶几 |
| 辅助 | 功能补充 | 较自由，填充剩余空间 | 梳妆台、边几 |

WHY：锚点锁定功能核心 → 主要家具建立空间骨架 → 辅助家具填充空余。
反过来做会导致辅助家具占了好位置，核心家具被迫妥协。

## 三、硬约束（任何房间都必须遵守）

1. bounds 完全在 zone.innerBoundary 内（不越界）
2. bounds 不与 zone.exclusionAreas 重叠（不进禁区）
3. bounds 不与其他已放置模块重叠（不重叠）
4. 不阻挡门窗的正常开启（门前净空）

## 四、通道标准

| 通道类型 | 最小宽度 | 典型场景 |
|----------|---------|---------|
| 主通道 | ≥ 900mm | 入口→核心区的主动线 |
| 次通道 | ≥ 600mm | 到达辅助家具的支路 |

**判断方法**：
- 通行间隙：人需要从此处走过到达其他区域 → 按通道标准
- 使用间隙：仅在此处操作家具（如拉抽屉、开柜门）→ 按家具使用需求

WHY：区分通行和使用，避免把所有间隙都按最大标准留，浪费空间。

> 房间特定的使用距离（如床侧通道、马桶两侧间距）在各房间 Skill 中定义。

## 五、家具依赖

### 成套家具
同组家具必须在同阶段放置，保证空间关系协调。

| 成套组 | 组成 |
|--------|------|
| 睡眠组 | 床 + 床头柜(1~2) |
| 客厅组 | 沙发 + 茶几 |
| 餐厅组 | 餐桌 + 餐椅(N) |
| 工作组 | 书桌 + 椅子 |

WHY：成套家具有固定的空间关系（如床头柜紧贴床头两侧），拆开放置会破坏使用逻辑。

> 各房间 Skill 中定义哪些家具是必须、哪些可选。

## 六、三级约束框架

| 层级 | 语气 | Agent 行为 |
|------|------|-----------|
| 硬约束 | 必须/禁止 | 无条件遵守 |
| 软指导 | 应/建议 | 默认遵守，可说明理由后偏离 |
| 自由区域 | （不写规则） | Agent 自主决策 |

WHY：规则分层让 Agent 知道哪些绝对不能违反、哪些有灵活空间。
不分层的规则体系会让 Agent 要么全部机械执行（过度僵硬），要么随意偏离（过度自由）。
```

---

### 2.2 generate-bathroom/SKILL.md（新建）

**源码路径**：`BIMCanvas.Agent/templates/skills/generate-bathroom/SKILL.md`

**定位**：卫生间策略 Skill。卫生间是**模板匹配型空间**——Agent 角色从"设计师"切换为"工程师"。

**注意力预算**：≤ 150 行

**内容来源**：
- placement_guide.md §11（卫生间专项规则，150 行）
- SKILL.md 卫生间流程分支（479-537 行）
- module_library.json 卫浴模块的 agent_config

**核心设计决策**：

| 决策 | 说明 | 理由 |
|------|------|------|
| 模板匹配而非规划设计 | 测量→查表→定位，极低自由度 | 卫生间空间小、功能单一、约束强，规划式设计是过度设计 |
| 五阶段适配 | 理解=模式识别，策略=参数配置 | 复用统一框架，但内容极度简化 |
| 链型排列而非树型依赖 | 台盆→马桶→淋浴，入口→深处 | 卫生间洁具是线性排列的，没有"围绕锚点展开"的结构 |
| 模式决策树 | 5种模式，由空间几何特征决定 | 简洁有效的分类方法，避免 Agent 在有限空间中过度推理 |

**五阶段适配说明**：

| 阶段 | 卧室（设计师模式） | 卫生间（工程师模式） |
|------|-------------------|-------------------|
| 理解 | 深度空间画像，分析床头墙/衣柜墙候选 | 快速识别：量尺寸→查决策树→确定模式 |
| 策略 | 锚点决策链，多种方案权衡 | 按模式直接确定各洁具位置，几乎无选择 |
| 执行 | 一次性放置，修正循环 | 参数化定位，精确放置 |
| 审查 | 设计评审，关注动线/均衡 | 功能验证，关注间距/通道 |

```
结构草案：

# 卫生间策略 Skill

> 卫生间是模板匹配型空间——测量空间，选择布局模式，参数化定位洁具。
> Agent 角色：工程师（执行精度优先，设计自由度极低）。

## 一、适用范围
- tags 包含 shower / toilet / washing 的空间
- 主卫、公卫、半卫（无淋浴设施）

## 二、空间理解（补充主工作流）

卫生间的"理解"是快速模式识别，不需要深度空间画像。

分析维度：
① 短边宽度（关键阈值：1800mm）
② 长宽比（>1.5 为窄长型）
③ 门的位置 → 决定洁具排布起点
④ 窗的位置 → 淋浴可临窗（与卧室不同）
⑤ 可用面积 → 决定配置丰富度

## 三、模式选择决策树

是否有淋浴需求？（无 shower tag → 否）
├─ 否 → 模式E（半卫）
└─ 是
   └─ 短边 ≤ 1800mm？
      ├─ 是 → 模式A（单侧线性）
      └─ 否
         └─ 长宽比 > 1.5？
            ├─ 是 → 模式B（淋浴占远端）
            └─ 否
               └─ 面积充裕？（≥ 5㎡）
                  ├─ 否 → 模式C（近方形紧凑）
                  └─ 是 → 模式D（宽裕型）

## 四、各模式布局策略

### 通用原则（所有模式适用）
- 排布方向：入口侧 → 深处（台盆靠入口，淋浴在最里）
  WHY：动线自然，隐私递进，干湿分离
- 台盆贴角：紧贴入口侧墙角，禁止留 <600mm 缝隙
  WHY：窄缝无法使用，积灰难清洁
- 马桶居中：在可用墙段内居中，两侧预留操作空间
- 淋浴连接实墙：至少连接两面实墙
  WHY：结构支撑稳固 + 防水施工可靠

### 模式A：单侧线性（窄长型，短边≤1800mm）
所有洁具沿一侧墙排列，对侧保留完整通道（≥600mm）。
WHY：窄空间两侧放置会堵死通道。

定位逻辑：
- 确定布置墙（通常是长边墙）
- 台盆紧贴入口端墙角 → 马桶接台盆后方 → 淋浴屏占尽端
- 台盆宽度：horizontal_fill 策略，填满到马桶前缘
- 淋浴屏宽度：horizontal_fill 策略，尽端墙到马桶后缘

### 模式B：淋浴占远端（长方形，长宽比>1.5）
淋浴间横占远端整面宽度，台盆和马桶在近端侧面排列。
WHY：利用远端整面宽度创造宽裕淋浴区，近端分配给需要通道的洁具。

### 模式C：近方形紧凑
洁具分布于不同墙面，利用方形空间的多面可用性。
台盆和马桶各占一面墙，淋浴房占角落。

### 模式D：宽裕型（面积≥5㎡）
空间充裕，三面墙各放一类洁具。可考虑浴缸替代/补充淋浴。

### 模式E：半卫（无淋浴）
台盆 + 马桶并列，最简配置。入口侧台盆，内侧马桶。

## 五、策略声明示例

**场景**：1.6m × 2.8m 公卫，北墙入口偏右，东墙高窗

**空间识别**：
- 短边 1600mm < 1800mm → 模式A（单侧线性）
- 北墙入口偏右 → 洁具沿西墙排列（入口左侧）
- 东墙高窗 → 不影响洁具布置

**策略声明**：
- 模式：A（单侧线性）
- 布置墙：西墙（2.8m）
- 台盆：西墙北端，紧贴北墙角，定制宽度 850mm（horizontal_fill）
- 马桶：台盆南侧，西墙上居中预留
- 淋浴屏：西墙南端→南墙连接，一字型，宽度=剩余空间
- 通道：东侧保留 ≥600mm 完整通道

## 六、关键约束

【硬约束】
① 洁具排布顺序：台盆(入口) → 马桶(中) → 淋浴(深处)
   WHY：动线自然，隐私递进，干湿分离
② 台盆贴角：禁止留 <600mm 缝隙
   WHY：窄缝无法使用且积灰
③ 淋浴屏/房连接实墙（≥2面）
   WHY：结构支撑 + 防水可靠

【软指导】
① 马桶避正对门
   WHY：隐私
② 台盆靠近入口
   WHY：使用频率最高，进门即用
③ 窄长型（≤1800mm）单侧布置
   WHY：保留完整通道

【自由区域】
- 洁具间的精确间距
- 配件位置（毛巾架、镜柜等）
```

**与 module_library 的分工**：
- generate-bathroom 定义"策略级"规则（模式选择、排布逻辑）
- module_library 中卫浴模块的 agent_config 定义"家具级"规则（单个洁具的拓扑/关系约束）
- 禁止重复：如"台盆贴角"只在 generate-bathroom 中定义，module_library 的 mod_basin 不重复此规则

---

### 2.3 generate-livingroom/SKILL.md（新建 — 框架级）

**源码路径**：`BIMCanvas.Agent/templates/skills/generate-livingroom/SKILL.md`

**定位**：客厅/餐厅策略 Skill。当前为**框架级定义**，完整策略待 T3 分区能力 + 实际测试后充实。

**注意力预算**：≤ 80 行（框架级，非完整版）

**内容来源**：
- placement_guide.md §8 客厅（8行）、§9 书房（10行）、§10 餐厅（15行）
- module_library.json 客厅/餐厅模块的 agent_config

**框架级定义的含义**：
- ✅ 定义适用范围、空间理解维度、核心决策链、家具配置、关键约束
- ❌ 不包含完整的策略声明示例（等实际测试后补充）
- ❌ 不包含开放空间分区策略（T3 的工作）

```
结构草案：

# 客厅策略 Skill

> 客厅（含餐厅）是社交与生活核心空间。
> 独立封闭空间按常规策略执行，开放的客餐一体空间需先加载 generate-zoning。
>
> 当前版本：框架级。完整策略待 T3 分区能力 + 实际测试后充实。

## 一、适用范围
- tags 包含 tvMedia / rest / display → 客厅
- tags 包含 dining → 餐厅
- 独立封闭客厅、独立封闭餐厅、已分区的开放空间子区域

## 二、空间理解（补充主工作流）

客厅分析维度：
① 电视墙候选：最长实墙，避门窗干扰
② 主沙发朝向：面向电视墙
③ 采光面：主窗户方向，沙发宜侧对或背对
④ 空间形态：矩形 / L形 / 开放连通
⑤ 是否需要分区？→ 开放空间加载 generate-zoning

餐厅分析维度：
① 可用面积 → 决定餐桌尺寸
② 四周动线空间 → 餐桌需四面留出 ≥600mm 通道
③ 与厨房的关系 → 靠近为宜

## 三、策略生成

### 客厅核心决策链
锚点决策：沙发 → 选择面向电视墙方向
  WHY：电视墙决定空间主轴，沙发面向它是使用核心

主要家具：
- 茶几 → 沙发正前方，与沙发居中对齐
- 电视柜 → 电视墙（如有对应模块）

辅助家具：位置自由

### 餐厅核心决策链
锚点决策：餐桌 → 空间中心或靠墙
  WHY：餐桌四周需要就座动线，居中是最自然的选择

主要家具：
- 餐椅 → 围绕餐桌

辅助家具：餐边柜等，靠墙

### 家具配置
客厅：锚点=沙发 | 主要=茶几 | 辅助=电视柜（可选）
餐厅：锚点=餐桌 | 主要=餐椅 | 辅助=餐边柜（可选）

## 四、关键约束

【硬约束】
① 沙发面向电视墙
② 餐桌四周 ≥ 600mm 动线
③ 开放空间必须先分区再布置（加载 generate-zoning）

【软指导】
① L形沙发宜靠角放置（WHY：贴合墙角最大化利用空间）
② 茶几与沙发居中对齐（WHY：使用对称性）
③ 餐桌靠近厨房方向（WHY：缩短上菜动线）

【自由区域】
- 沙发靠墙 vs 岛式
- 茶几尺寸选择
- 餐椅数量和间距

## 五、待充实内容（T3 后）
- 客餐一体的分区策略
- 开放空间的动线规划
- 电视墙选择的完整优先级链 + WHY
- 策略声明示例（≥1 个完整场景）
- 书房区域的策略（如客书一体）
```

---

### 2.4 generate-workflow/SKILL.md（更新）

**源码路径**：`BIMCanvas.Agent/templates/skills/generate-workflow/SKILL.md`

**更新范围**：仅更新 T1 中预留的过渡引用，不改动工作流结构。

**更新内容**：

1. **感知阶段引用更新**：
   ```
   T1 写的（过渡版）：读取 placement_guide.md
   T2 更新为：读取 design_principles.md
   ```

2. **空间类型→Skill 映射表补充**：
   ```
   T1 已有：bedroom → generate-bedroom
   T2 新增：bathroom → generate-bathroom
   T2 新增：livingroom / diningroom → generate-livingroom
   ```

---

## 三、placement_guide 退场方案

### 旧内容迁移对照表

| placement_guide 章节 | 新归属 | 说明 |
|---------------------|--------|------|
| §1.1 全局规划（动线/纵深/采光） | design_principles §一 | 精简为原理+标准 |
| §1.2 评估维度（空间效率/功能联动） | design_principles §二（融入优先级） | 概念保留，表述精简 |
| §1.3 异形利用 | T3 generate-zoning | 不属于通用原则 |
| §2 布置优先级 | design_principles §二 | 保留 |
| §3 空间约束 | design_principles §三 | 保留 |
| §4.1 通道分类判断 | design_principles §四 | 保留 |
| §4.2 通用通道宽度 | design_principles §四 | 保留 |
| §4.3 房间特定使用距离 | 各房间 Skill | 如：床侧通道→bedroom、马桶间距→bathroom |
| §5.1 成套家具概念 | design_principles §五 | 保留概念定义 |
| §5.2 必须vs可选表 | 各房间 Skill | 各房间自定义 |
| §7 卧室规则 | generate-bedroom（T1 已完成） | 床头墙/衣柜/间距 |
| §8 书房规则 | 暂留（后续按需创建 generate-study） | T2 不处理 |
| §9 客厅规则 | generate-livingroom §三 | 核心决策链 |
| §10 餐厅规则 | generate-livingroom §三 | 合并到客厅Skill |
| §11 卫生间规则 | generate-bathroom | 完整迁移 |
| §12 常见错误 | 各文件的约束部分 | 分散到对应位置 |

### 退场步骤

1. T2 执行完成后，placement_guide.md 标记为 deprecated
2. generate-workflow 中的引用已更新为 design_principles.md
3. 旧文件保留在原位作为参考（不删除），文件顶部加注：
   ```
   > ⚠️ DEPRECATED: 本文件已被 design_principles.md + 各房间 Skill 替代。
   > 如需查阅，请参考对应的新文件。
   ```

### 信息安全检查

以下是 placement_guide 中可能遗漏的内容，执行窗口需逐项确认：

- [ ] §1.2 评估维度中"特色资源"概念（延伸区、凹陷区）→ 确认由 generate-zoning（T3）覆盖
- [ ] §4.3 使用距离表中所有数值 → 确认已分散到对应房间 Skill
- [ ] §5.3 互斥依赖（如"浴缸和淋浴屏不同时出现"）→ 确认已在 generate-bathroom 中体现
- [ ] §12 常见错误 11 条 → 确认每条都被新体系的某个约束覆盖

---

## 四、遵循原则

### 设计哲学原则

> 完整论述见 `docs/Agent_Prompt_Design_Philosophy.md`

1. **注意力零和**：新增三个文件总计 ≤ 330 行（design_principles ≤100 + bathroom ≤150 + livingroom ≤80）
2. **WHY 优先**：每条规则必须附带理由
3. **示例锚定基准**：generate-bathroom 包含至少 1 个完整策略声明示例；generate-livingroom 框架级暂不要求
4. **三级约束分明**：硬约束"必须/禁止"、软指导"应/建议"、自由区域不写规则
5. **职责单一**：每条知识只在一个文件中定义，禁止跨文件重复
6. **留白是设计选择**：自由区域是有意识地让 Agent 施展判断力

### 写作原则

1. design_principles 用 **原理+速查** 模式：每个原则 = 一句 WHY + 关键数值/标准
2. generate-bathroom 用 **决策树+模板** 模式：减少推理，增加确定性
3. generate-livingroom 用 **框架+预留** 模式：建立结构，标注待充实内容
4. **禁止重复**：与 module_library.json 中 agent_config 已有的规则不重复

### 从旧体系提取的有效内容

| 旧文件位置 | 有效内容 | 新文件位置 |
|-----------|---------|-----------|
| placement_guide §1.1 | 动线/纵深/采光三要素 | design_principles §一 |
| placement_guide §2 | 锚点→主要→辅助优先级 | design_principles §二 |
| placement_guide §3 | 四条硬约束 | design_principles §三 |
| placement_guide §4.1 | 通行间隙 vs 使用间隙判断 | design_principles §四 |
| placement_guide §5.1 | 成套家具概念 | design_principles §五 |
| placement_guide §11 | 卫生间5种模式决策树 | generate-bathroom §三 |
| placement_guide §11 | 台盆贴角/马桶居中/淋浴连墙 | generate-bathroom §四 |
| placement_guide §8-10 | 客厅/餐厅核心决策 | generate-livingroom §三 |
| SKILL.md 卫生间流程 | 测量→查表→定位的工作模式 | generate-bathroom §二-四 |

---

## 五、与其他任务的联动点

### 与 T1 的联动

- **T1 定义接口，T2 实现内容**：T1 的 generate-workflow 引用 design_principles（T2 创建）、支持加载 bathroom/livingroom Skill（T2 创建）
- **T1 范例，T2 遵循**：generate-bedroom 是房间 Skill 的范例，generate-bathroom 遵循相同结构
- **T2 更新 T1 的过渡引用**：generate-workflow 中 placement_guide → design_principles

### 与 T3 的联动

- **generate-livingroom 预留分区接口**：开放空间→加载 generate-zoning（T3 提供）
- **design_principles 不含异形利用规则**：留给 T3 的 generate-zoning
- **T3 完成后，generate-livingroom 从框架升级为完整版**

---

## 六、验收标准

### P0：必须通过

- [ ] design_principles ≤ 100 行，仅包含跨房间通用原则
- [ ] generate-bathroom ≤ 150 行，包含 5 种模式决策树 + ≥1 个策略声明示例
- [ ] generate-livingroom ≤ 80 行，框架完整，标注待充实内容
- [ ] generate-workflow 引用已从 placement_guide → design_principles
- [ ] 空间类型→Skill 映射表已补充 bathroom 和 livingroom
- [ ] **无信息重复**：design_principles、各房间 Skill、module_library 之间无规则重复
- [ ] 每条规则都有 **WHY**
- [ ] placement_guide 标记为 deprecated

### P1：应该满足

**design_principles**：
- [ ] 动线/纵深/采光三要素清晰，各附 WHY
- [ ] 通道标准表格简洁（仅通用标准，房间特定在各 Skill 中）
- [ ] 成套家具概念定义清晰
- [ ] 三级约束框架有说明

**generate-bathroom**：
- [ ] 决策树逻辑清晰，阈值明确（1800mm、面积≥5㎡等）
- [ ] 各模式布局策略覆盖定位逻辑
- [ ] 模式A（最常见）描述最详细
- [ ] 通用原则（排布方向、台盆贴角、淋浴连墙）在模式之前说明
- [ ] 策略声明示例展示完整的"测量→识别模式→定位"推导

**generate-livingroom**：
- [ ] 客厅和餐厅的核心决策链清晰
- [ ] 分区接口预留明确
- [ ] 待充实内容已标注

**退场方案**：
- [ ] placement_guide 每个章节都有明确归属
- [ ] 信息安全检查清单完整

---

## 七、注意事项

1. **generate-bathroom 是第二个房间 Skill 范例**：与 generate-bedroom 形成对比——设计师模式 vs 工程师模式。两者结构相同但内容风格不同，展示了同一框架的弹性。

2. **generate-livingroom 是框架级**：有意留白。当前知识不足以写出完整的客厅策略（只有8行旧规则），强行充实会降低质量。等 T3 分区能力和实际测试数据后再升级。

3. **design_principles 是"精华提炼"而非"简单删减"**：从 652 行提炼到 ≤100 行，不是砍掉 550 行，而是用更高密度的表述覆盖核心原理。房间特定内容不是被删除，而是被迁移到各房间 Skill。

4. **module_library.json 不调整**：T2 聚焦知识体系重构。module_library 中的 agent_config 规则可能与新 Skill 有少量重叠（如 mod_toilet_001 的 relation_rules 与 generate-bathroom 的约束），但暂时容忍，后续迭代统一清理。

5. **书房（generate-study）不在 T2 范围**：placement_guide §8 书房规则（10行）暂不迁移，保留在 deprecated 的 placement_guide 中。后续按需创建 generate-study Skill。

---

## 八、参考材料

执行 T2 前必须阅读：

| # | 文档 | 路径 | 用途 |
|---|------|------|------|
| 1 | 重构总览 | `plans/workflow-refactor/overview.md` | 统一上下文 |
| 2 | T1 计划文档 | `plans/workflow-refactor/T1-agent-workflow.md` | 接口规范 + 范例 |
| 3 | 提示词设计哲学 | `docs/Agent_Prompt_Design_Philosophy.md` | 设计理论基础 |
| 4 | 当前 placement_guide | `BIMCanvas.Server/Templates/knowledge/placement_guide.md` | 提取来源 |
| 5 | 模块库 | `BIMCanvas.Server/Templates/modules/module_library.json` | 家具规则参照 |
| 6 | 空间类型架构 | `plans/Space_Type_Workflow_Vision.md` | 卫生间模式分析 |
| 7 | T1 交付的 generate-workflow | `BIMCanvas.Agent/templates/skills/generate-workflow/SKILL.md` | 更新引用 |
| 8 | T1 交付的 generate-bedroom | `BIMCanvas.Agent/templates/skills/generate-bedroom/SKILL.md` | 结构范例 |
