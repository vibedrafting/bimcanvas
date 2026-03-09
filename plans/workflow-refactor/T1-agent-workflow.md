# T1：Agent 工作流 + 身份定义 + 卧室策略 Skill

> 依赖：无（第一波任务）
> 上游文档：`plans/workflow-refactor/overview.md`

---

## 一、改造目标

1. **重写 generate-workflow/SKILL.md**：从 601 行的线性流程改为 ~150 行的五阶段主工作流框架
2. **调整 BIMCANVAS.md**：Agent 身份重定义 + 对话作为 Agent 通用能力
3. **新建 generate-bedroom/SKILL.md**：第一个房间策略 Skill，作为其他房间 Skill 的范例

完成后效果：Agent 能使用新工作流完整布置**简单矩形卧室**（主卧和次卧）。分区能力由 T3 提供。

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
- Write → validate_layout
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
5. **多分区派发调整**：每个分区可能是不同空间类型，SubAgent 独立加载各自的房间 Skill
6. **工具优先级保持**：validate_layout 必调、专用 MCP 工具 > Bash
7. **约束层级保持三级**：必须/建议/提示

### 2.3 generate-bedroom/SKILL.md（新建）

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

### 写作原则

1. **流程框架用祈使句**：简洁直接
2. **策略知识用原则+理由**：不是命令而是引导
3. **避免硬编码数值在 SKILL.md 中**：数值放在 design_principles 或 module_library 中
4. **禁止重复**：信息只在一处定义

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
- [ ] 三个文件间无信息重复
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

### 6.3 对话能力验收（BIMCANVAS.md）

- [ ] 对话定义为 Agent 通用能力（不是工作流固定阶段）
- [ ] 触发条件清晰（复杂空间、多方案、分区确认等）
- [ ] 行为规范明确（展示分析→征求确认）
- [ ] 可以在工作流的任何阶段触发

### 6.4 卧室策略验收

- [ ] 覆盖主卧和次卧的策略差异
- [ ] 床头墙选择逻辑完整（优先级链 + WHY）
- [ ] 衣柜布局模式决策清晰（传统 L 形 / L 型靠墙 / 线性 + WHY）
- [ ] **不含分区逻辑**（预留接口即可）
- [ ] 辅助家具有自由区域标注
- [ ] 与 module_library 的关系清晰

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
