# T1：Agent 工作流 + 双层 Agent 定义 + 卧室策略 Skill

> 依赖：无（第一波任务）
> 上游文档：`plans/workflow-refactor/overview.md`

---

## 一、改造目标

1. **重写 generate-workflow/SKILL.md**：从 601 行的线性流程改为 ~150 行的五阶段主工作流框架
2. **调整 BIMCANVAS.md**：主控 Agent 重定义为"全屋协调者 + 用户代言人"，新增对话通用能力
3. **改写 layout-agent.md**：从"单区布置执行者"重定位为"单房间设计专家"
4. **新建 generate-bedroom/SKILL.md**：第一个房间策略 Skill，作为其他房间 Skill 的范例

完成后效果：Agent 能使用新工作流完整布置**简单矩形卧室**（单房间和多房间并行派发）。分区能力由 T3 提供。

---

## 二、修改范围

### 2.1 generate-workflow/SKILL.md（重写）

**源码路径**：`BIMCanvas.Agent/templates/skills/generate-workflow/SKILL.md`

**当前内容**（601 行）：前置准备 6 步 + 骨架规划 + 阶段 A + 阶段 B + 卫生间流程 + 报告 + 常见错误 20 条

**目标内容**（≤ 150 行）：纯流程框架，不含任何房间特定知识

```
结构草案：

# 布置工作流

## 感知
- 截图（单独调用）
- 并行读取：design_principles.md、module_library.json、zones.json、exclusions.json、openings.json

## 理解
- 产出空间画像（引导维度：形状、动线、采光、关键资源）
- 确定空间类型 → 加载对应房间 Skill
- 评估分区需求 → 如需要，加载 generate-zoning Skill（T3 后可用）

## 策略
- 遵循已加载的房间 Skill 产出策略声明
- 策略声明引导问题（主通道、关键墙面、家具清单）

## 执行
- 按策略一次性放置全部家具
- Write → validate_layout（layout-agent 传入 zoneIds 仅验证所负责分区）
- 修正循环（最多 2 轮）
- 兜底：移除违规家具，保留核心布局

## 审查
- 截图 → 基于设计原则整体评估
- 四个评估维度（动线、均衡、功能、品质）
- 发现问题 → 修正 → validate → 重新审查（最多 1 轮）

## 汇报
- 空间画像摘要 + 策略要点 + 放置结果 + 品质评估

## 数据格式
- modules.json 写入格式
- 项目目录权限（baseline 只读、schemes 可写等）

## 保留机制
- validate_layout：每次 Write 后必调
- 先读后写：修改前先读取当前内容
- 修正优先级：平移 → 旋转 → 缩小 → 替换 → 移除
```

### 2.2 BIMCANVAS.md（调整）

**源码路径**：`BIMCanvas.Agent/templates/BIMCANVAS.md`

**当前内容**（142 行）：身份定义（执行者+设计师）+ 约束层级 + 执行规范 + 任务判断 + 多分区派发 + 工具优先级

**调整要点**：

1. **身份定义调整**：
   ```
   当前："兼具执行者和设计师两个角色"
   目标：强调 Agent 是"理解空间的设计师"——先理解，后决策，最后执行
   ```

2. **新增：对话作为通用能力**：
   ```
   对话不是工作流的固定步骤，而是 Agent 的行为能力。
   触发条件：
   - 空间复杂度较高，存在多种有效方案
   - 分区决策需要用户确认
   - 设计偏好影响方案方向
   行为规范：
   - 展示专业分析 → 征求确认（不是问问题）
   - 可以在理解后、策略前、或策略后对话
   ```

3. **任务类型路由保持不变**：query / edit / generate
4. **generate 路由更新**：
   - generate 触发 generate-workflow Skill
   - generate-workflow 内部按需加载房间 Skill 和 zoning Skill
5. **多房间派发调整**：每个房间可能是不同空间类型，layout-agent 自主加载各自的房间 Skill
6. **工具优先级保持**：validate_layout 必调、专用 MCP 工具 > Bash
7. **约束层级保持三级**：必须/建议/提示

### 2.3 layout-agent.md（改写）

**源码路径**：`BIMCanvas.Agent/templates/agents/layout-agent.md`

**当前内容**：定位为"单区布置专家"，严格遵守 Skill 步骤，设计分歧上报

**新定位**："单房间设计专家"——运行完整五阶段工作流，自主加载 Skill，具备独立设计判断力

**调整要点**：

1. **身份重定义**：
   ```
   当前："单区布置专家，由主控 Agent 派发，专注于单个设计区的布置"
   目标："单房间设计专家，由主控 Agent 派发，负责单个房间的完整设计流程"
   ```

2. **核心行为约束**（区别于主控 Agent）：
   - **【必须】静默执行**：不使用 AskUserQuestion，遇到设计分歧在输出中上报
   - **【必须】单房间验证**：调用 validate_layout 时必须传入 zoneIds=[自己负责的 zoneId]，禁止全局验证
   - **【必须】不派发任务**：不能创建子任务或派发其他 Agent

3. **Skill 自主加载**：
   ```
   收到任务后：
   1. 加载 generate-workflow Skill（主工作流）
   2. 在理解阶段自主判断空间类型 → 加载对应房间 Skill
   3. 如判断需要分区 → 加载 generate-zoning Skill（T3 后可用）
   ```

4. **分歧上报机制**：
   - 遇到需要用户确认的设计分歧（如多种有效方案）
   - 在任务输出中详细描述分歧：推荐方案 + 替代方案 + 核心取舍
   - 由主控 Agent 决定是否向用户提问

5. **文件写入范围**：保持只写入 `schemes/{指定zoneId}/modules.json`

6. **MCP 工具使用约束**：
   - validate_layout：必须传入 zoneIds 参数，仅验证自己负责的分区
   - WHY：layout-agent 作用域限定为单房间，全局验证是主控 Agent 的职责

### 2.4 generate-bedroom/SKILL.md（新建）

**源码路径**：`BIMCanvas.Agent/templates/skills/generate-bedroom/SKILL.md`

**定位**：卧室策略 Skill，被 generate-workflow 在理解阶段动态加载。**不含分区逻辑**（分区由 generate-zoning 负责）。

**目标内容**（≤ 150 行）：

```
结构草案：

# 卧室策略 Skill

## 适用范围
- 主卧、次卧、儿童房等有明确墙体围合的睡眠空间
- tags 包含 bedroom 或类似标签

## 空间理解（补充主工作流的空间画像）
- 卧室特有分析维度：
  - 床头墙候选：哪些墙是实墙？排除窗墙和门段
  - 衣柜墙候选：哪些墙有足够长的有效段？
  - 窗户朝向：采光方向
- （注：如空间需要分区，由 generate-zoning Skill 处理，本 Skill 接收分区结果）

## 策略生成
- 锚点决策：床 → 选择床头墙
  - 优先级：靠实墙 > 侧对窗户 > 远离门 > 不正对门
  - WHY: 靠实墙有安全感，侧对窗户采光柔和
- 衣柜决策：选墙 → 选模式
  - 墙面选择：排除窗墙 → 计算有效段 → 选最长
  - 布局模式：传统 L 形（WHY: 创造半封闭换衣区）→ L 型靠墙（回退）→ 线性
- 家具配置清单：
  - 主卧：床 → 衣柜 → 床头柜×2 → 梳妆台（可选）
  - 次卧：床 → 衣柜（可选）→ 床头柜≥1 → 书桌（可选）
- 辅助家具：位置由 Agent 自主决定（自由区域），优先填充剩余墙段

## 策略声明示例

> 以下示例展示从空间画像到策略的推导过程，Agent 应参照此密度和推理风格。

**场景**：3.6m × 4.2m 主卧，南墙窗户，北墙入口偏左

**空间画像摘要**：
- 南墙窗户占大部分墙面，采光充足
- 北墙入口偏左，右侧有 2.4m 连续实墙
- 东墙完整实墙 3.6m，是最长实墙
- 西墙完整实墙 3.6m

**策略推导**：
- 床头墙 → 东墙（最长实墙、侧对南窗采光柔和、不正对北墙门）
- 衣柜墙 → 北墙入口右侧 2.4m 段 + 西墙形成 L 形（创造半封闭换衣区）
- 主通道 → 入口→床侧，宽度 ≥600mm
- 家具清单 → 床(1800×2000) + 衣柜 L 形 + 床头柜×2 + 梳妆台（西墙窗侧）

## 关键约束
- 【硬约束】床头禁止靠窗墙（WHY: 睡眠安全感 + 窗帘安装空间）
- 【硬约束】衣柜前方净空（平开门 ≥600mm / 移门 0mm，WHY: 开门操作空间）
- 【软指导】衣柜应填满有效段全长（WHY: 连续柜面视觉整洁 + 最大化收纳）
- 【软指导】床侧对窗户（WHY: 侧面采光柔和，不直射眼睛）
- 【软指导】成对床头柜（WHY: 视觉对称 + 双人使用便利）
```

---

## 三、遵循原则

### 设计哲学原则

> 完整论述见 `docs/Agent_Prompt_Design_Philosophy.md`

1. **注意力零和**：三个文件总计 ≤ 400 行
2. **WHY 优先**：每条规则必须附带理由
3. **三级约束分明**：硬约束"必须/禁止"、软指导"应/建议"、自由区域不写规则
4. **留白是设计选择**：辅助家具位置、间距比例等让 Agent 自主判断
5. **职责单一**：generate-workflow 管流程、generate-bedroom 管策略、BIMCANVAS.md 管对话行为
6. **示例锚定基准**：房间 Skill 必须包含至少 1 个策略声明示例，展示从空间画像到策略的完整推导

### 写作原则

1. **流程框架用祈使句**：简洁直接
2. **策略知识用原则+理由**：不是命令而是引导
3. **避免硬编码数值在 SKILL.md 中**：数值放在 design_principles 或 module_library 中
4. **禁止重复**：信息只在一处定义
5. **示例优先于文字规则**：能用示例校准的行为，优先用示例；场景多变时用 WHY

### 从旧体系提取的有效内容

| 旧文件位置 | 有效内容 | 新文件位置 |
|-----------|---------|-----------|
| SKILL.md §6.1-6.4 骨架规划 | 空间阅读的维度（动线、纵深、采光） | generate-workflow 理解阶段 |
| SKILL.md §6A 预检 | 门前净空、通道、间距检查概念 | generate-workflow 执行阶段 |
| SKILL.md 修正循环 | 修正优先级（平移→旋转→缩小→替换→移除） | generate-workflow 执行阶段 |
| SKILL.md AskUserQuestion | 对话触发原则 | BIMCANVAS.md 对话能力 |
| placement_guide §7 卧室布置要点 | 床头墙选择、衣柜选墙逻辑 | generate-bedroom |
| placement_guide §7 朝向逻辑 | 床朝向优先级 | generate-bedroom |
| BIMCANVAS.md 约束层级 | 必须/建议/提示三级 | BIMCANVAS.md（保留） |
| BIMCANVAS.md 先读后写 | 安全机制 | BIMCANVAS.md（保留） |
| layout-agent.md 范围约束 | 只写指定分区、不修改 baseline | layout-agent.md（保留） |
| layout-agent.md 分歧上报 | 不用 AskUserQuestion，上报给主控 | layout-agent.md（增强） |

---

## 四、与其他任务的联动点

### 与 T2（知识体系）的联动

- T1 定义了 `design_principles.md` 的**读取时机**（感知阶段），T2 负责**编写内容**
- T1 定义了房间 Skill 的**接口规范**（结构和风格），T2 按此编写其余房间 Skill
- T1 的 generate-bedroom 是**范例**，T2 的 generate-bathroom 和 generate-livingroom 应遵循相同结构

### 与 T3（分区架构 + zoning Skill）的联动

- T1 的 generate-workflow 在理解阶段预留了"加载 generate-zoning"的接口
- T1 先只处理简单矩形卧室（不加载 zoning），T3 完成后自动解锁分区能力
- T1 的 generate-bedroom 预留"接收分区结果"的接口（"如有分区，由 generate-zoning 提供"）

---

## 五、参考材料

执行 T1 前必须阅读：

1. `plans/workflow-refactor/overview.md` — 统一说明文档
2. `docs/Agent_Prompt_Design_Philosophy.md` — 提示词设计哲学
3. `plans/Space_Type_Workflow_Vision.md` — 空间类型差异分析
4. `BIMCanvas.Agent/templates/skills/generate-workflow/SKILL.md` — 当前 SKILL.md
5. `BIMCanvas.Agent/templates/BIMCANVAS.md` — 当前 BIMCANVAS.md
6. `BIMCanvas.Agent/templates/agents/layout-agent.md` — 当前 layout-agent.md
6. `BIMCanvas.Server/Templates/knowledge/placement_guide.md` — 当前 placement_guide（卧室策略来源）
7. `BIMCanvas.Server/Templates/modules/module_library.json` — 模块库
8. `docs/Agent_Workflows.md` — Agent 工作流架构（Skill 加载机制）
9. `docs/Agent_Design.md` — Agent 架构设计（SubAgent 机制）

---

## 六、验收标准

### 6.1 结构验收

- [ ] `generate-workflow/SKILL.md` ≤ 150 行
- [ ] `generate-bedroom/SKILL.md` ≤ 150 行
- [ ] `BIMCANVAS.md` ≤ 100 行
- [ ] `layout-agent.md` ≤ 80 行
- [ ] 四个文件间无信息重复
- [ ] 五阶段流程完整（感知→理解→策略→执行→审查→汇报）
- [ ] Skill 加载机制有明确说明（房间 Skill + zoning Skill 预留接口）

### 6.2 内容验收

- [ ] 每条规则都有 WHY
- [ ] 硬约束、软指导、自由区域有明确区分
- [ ] 空间画像的输出格式有引导（不是死板模板）
- [ ] 策略声明有引导结构
- [ ] 修正循环有明确规则（最多 N 轮、兜底策略）
- [ ] validate_layout 的调用时机明确
- [ ] 截图审查的评估维度是原则性的（非清单式）
- [ ] 房间 Skill 包含至少 1 个策略声明示例（示例锚定基准）

### 6.3 对话能力验收（BIMCANVAS.md）

- [ ] 对话定义为 Agent 通用能力（不是工作流固定阶段）
- [ ] 触发条件清晰（复杂空间、多方案、分区确认等）
- [ ] 行为规范明确（展示分析→征求确认）
- [ ] 可以在工作流的任何阶段触发

### 6.4 layout-agent.md 验收

- [ ] 定位为"单房间设计专家"（非"单区布置执行者"）
- [ ] 明确三个核心约束：静默执行、单房间验证、不派发任务
- [ ] Skill 自主加载机制清晰（generate-workflow + 房间 Skill + zoning 预留）
- [ ] 分歧上报机制明确（详情格式、由主控决定是否询问用户）
- [ ] 文件写入范围约束保留
- [ ] validate_layout 调用约束明确（必须传入 zoneIds，禁止全局验证）

### 6.5 卧室策略验收

- [ ] 覆盖主卧和次卧的策略差异
- [ ] 床头墙选择逻辑完整（优先级链 + WHY）
- [ ] 衣柜布局模式决策清晰（传统 L 形 / L 型靠墙 / 线性 + WHY）
- [ ] **不含分区逻辑**（预留接口即可）
- [ ] 辅助家具有自由区域标注
- [ ] 与 module_library 的关系清晰
- [ ] 包含至少 1 个完整的策略声明示例（空间画像→策略推导）

### 6.5 BIMCANVAS.md 验收

- [ ] Agent 角色定义强调"理解空间 → 设计决策 → 精确执行"
- [ ] 任务路由（query/edit/generate）完整
- [ ] 多分区派发逻辑适配新架构
- [ ] 约束层级与 design_principles 一致
- [ ] 先读后写、validate_layout 等安全机制保留

---

## 七、注意事项

1. **generate-bedroom 是范例**：其结构和风格将被 T2 的其他房间 Skill 参照
2. **design_principles.md 暂不创建**：T1 中 generate-workflow 引用它，但感知阶段暂读取现有 placement_guide（T2 完成前的过渡方案）
3. **分区接口预留但不实现**：generate-workflow 中写明"如需分区→加载 generate-zoning"，但 T1 阶段该 Skill 不存在
4. **保持向后兼容**：新工作流使用现有 MCP 工具（validate_layout、request_background_screenshot）和数据格式（modules.json）

---

## 附录 A：AskUserQuestion 驱动机制调研

> 调研目标：如何让 Agent 更自然、主动地调用 AskUserQuestion，而非依赖规则表触发。
> 约束条件：无法修改 claude_agent_sdk 源码（即无法修改 AskUserQuestion 的工具描述）。

### A.1 Claude Code 的三层驱动机制

通过逆向分析 Claude Code v2.1.71 的系统提示词和 HTTP 请求日志，发现 AskUserQuestion 在系统提示词中**仅出现 2 次**，却能实现自然的主动调用。核心在于三层协同：

**第 1 层：工具描述（Tool Description）— 最直接的驱动力**

SDK 内置的 AskUserQuestion 工具描述开头就是 "Use this tool when you need to ask the user questions during execution"，并列出 4 个具体场景：
1. Gather user preferences or requirements
2. Clarify ambiguous instructions
3. Get decisions on implementation choices as you work
4. Offer choices to the user about what direction to take

关键设计：用 "during execution" 强调这是工作中的自然行为；提供结构化选项（options/preview/multiSelect）降低 AI 决定"怎么问"的认知负担。

> **BIMCanvas 约束**：此层无法修改（SDK 源码），需通过其他两层补偿。

**第 2 层：行为倾向（Behavioral Disposition）— 系统提示词营造"问"的氛围**

Claude Code 不直接规定"你必须调用 AskUserQuestion"，而是在系统提示词中反复营造 **"问比猜好"** 的价值观：

- "check with the user before proceeding"
- "when in doubt, ask before acting"
- "The cost of pausing to confirm is low, while the cost of an unwanted action can be very high"（给出合理性论证）
- "do not attempt to brute force your way to the outcome"（禁止蛮干，隐含"应该问"）
- "Match the scope of your actions to what was actually requested"（不要自作主张）

这些句子都不提 AskUserQuestion，但构建了一个"不确定时应该问"的行为倾向。AI 内化价值观后，AskUserQuestion 成为自然的执行手段。

**第 3 层：场景嵌入（Contextual Triggers）— 在工作流中埋入触发点**

AskUserQuestion 在系统提示词中的 2 次出现，都是嵌入在具体场景中：
- 触发点 1："If the user denies a tool you call... use the AskUserQuestion to ask them."
- 触发点 2："If your approach is blocked... **consider** using the AskUserQuestion to align with the user on the right path forward."

注意触发点 2 用 "consider using" 而非 "must use"——给 AI 留判断空间，避免过度提问。

### A.2 当前 BIMCanvas 设计的四个薄弱点

| # | 问题 | 详细说明 |
|---|------|---------|
| 1 | **缺少行为倾向层** | BIMCANVAS.md 直接跳到"什么时候问"的规则，缺少"为什么要问"的价值观建设。没有价值观支撑的规则，AI 执行起来是机械的——匹配场景才问，不匹配就不问。 |
| 2 | **触发场景过于具体** | 当前 4 个触发场景（功能区联动、动线策略重写、使用姿态选择、空间性格选择）是**示例**而非**原则**。AI 遇到新的战略分歧时，如果不能匹配这 4 个模式就不会提问。缺少一个泛化原则统领这些示例。 |
| 3 | **注意力位置效应** | AskUserQuestion 指导在 SKILL.md 的 554-577 行（总 601 行），正处于注意力薄弱的中后段。根据提示词哲学的"位置效应"：头尾内容获得更多注意力，中间容易被遗忘。 |
| 4 | **"禁止"信号压过"鼓励"信号** | 禁止场景（query/edit、战术级、已明确偏好）非常明确，但鼓励的部分力度不足。AI 在不确定时倾向保守——"不确定要不要问"时选择不问，因为禁止信号更强。 |

### A.3 可迁移的三个改动方向

以下改动方向对应 T1 中已规划的文件修改：

**方向 1 → 对应 §2.2 BIMCANVAS.md 调整**

在角色定位段落中融入协作价值观（行为倾向层），而非作为独立规则。参考 Claude Code 的 "the cost of pausing to confirm is low" 模式：

```
翻译到室内设计语境：
"确认偏好只需几秒，但按错误方向完成整个布置的返工成本很高"
```

这不是一条规则，而是一个设计价值观。AI 内化后，会在不确定时自然倾向于问。

**方向 2 → 对应 §2.1 generate-workflow/SKILL.md 重写**

在触发场景前添加泛化原则，让具体场景变为原则的"示例"而非"穷举"：

```markdown
判断原则：如果存在两种方案，且选择不同会导致用户日常使用体验有质的差异，
则向用户展示推荐方案和替代方案。
典型场景（非穷举）：...
```

关键是 "（非穷举）" 三个字——明确告诉 AI 这不是完整列表，鼓励其识别新场景。

同时，将用户沟通判断从中后段提到策略阶段之前（利用位置效应）。

**方向 3 → 对应 §2.3 layout-agent.md 改写**

用 Claude Code 的 "consider" 模式调整分歧上报措辞——禁止直接调用 AskUserQuestion 不变，但积极鼓励上报分歧：

```markdown
当你发现两种同样合理的方案时，不要自行选择，将分歧上报给主控 Agent。
确认偏好比猜测偏好更有效率。
```

### A.4 参考资料索引

| 资料 | 路径 | 关键内容 |
|------|------|---------|
| Claude Code 系统提示词 | `references/Claude Code请求日志/Claude Code系统提示词_0309.md` | 完整系统提示词，AskUserQuestion 出现在第 8、20 行 |
| Claude Code HTTP 日志 | `references/Claude Code请求日志/0309日志.json` | 完整请求体，含 AskUserQuestion 工具定义（第 526-639 行）和 EnterPlanMode 定义 |
| BIMCanvas Agent 入口 | `BIMCanvas.Agent/src/agent/main_agent.py` | `_auto_approve_tool()` 方法（第 235-255 行）：AskUserQuestion 的侧信道实现 |
| HTTP Server | `BIMCanvas.Agent/src/server/http_server.py` | `request_user_question()`（第 472-565 行）：SSE 推送 + Future 等待架构 |
| MCP 工具定义 | `BIMCanvas.Agent/src/mcp/canvas.py` | `@tool()` 装饰器模式（无法用此模式自定义 AskUserQuestion 描述） |
| 配置加载器 | `BIMCanvas.Agent/src/config/loader.py` | `load_system_prompt()` / `load_agents()`：提示词注入链路 |
| 当前 BIMCANVAS.md | `BIMCanvas.Agent/templates/BIMCANVAS.md` | 第 112-124 行：现有用户沟通规范 |
| 当前 SKILL.md | `BIMCanvas.Agent/templates/skills/generate-workflow/SKILL.md` | 第 554-577 行：现有 AskUserQuestion 触发条件 |
| 当前 layout-agent.md | `BIMCanvas.Agent/templates/agents/layout-agent.md` | 第 36-47 行：禁止直接调用 + 分歧上报格式 |
| 提示词设计哲学 | `docs/Agent_Prompt_Design_Philosophy.md` | 注意力零和、位置效应、WHY=泛化能力等底层机制 |
