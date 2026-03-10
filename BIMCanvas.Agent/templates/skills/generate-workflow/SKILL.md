---
name: generate-workflow
description: |
  BIMCanvas 完整布置工作流。五阶段框架：感知→理解→策略→执行→审查→汇报。
  当用户需要"布置"、"设计"、"创建"、"生成"、"规划"等完整布置操作时使用。
---

# 布置工作流

```
感知(Perceive) → 理解(Understand) → 策略(Strategy) → 执行(Execute) → 审查(Review) → 汇报(Report)
    快速机械        全局思考          明确方向        专注放置         设计评审        简洁交付
```

---

## 感知 Perceive

> WHY：纯机械操作，不占认知资源。先建立感官输入，后续阶段才有决策素材。

1. **截图**（单独调用，等收到后再继续）— `mcp__canvas__request_background_screenshot`，用 zone 模式聚焦目标分区或 full 模式看全屋。截图工具直接返回图片；若看不到内容，用 Read 查看返回的文件路径
2. **并行读取**：knowledge/design_principles.md + modules/module_library.json + schemes/zones.json + computed/exclusions.json + baseline/openings.json

**模块数据**：契约层（id/tags/size）用于选择，意图层（agent_config: morphology + topology_rules + relation_rules）用于布置决策。按 Zone 的 tags/optionalTags 筛选兼容模块——匹配 tags 为必备家具，仅匹配 optionalTags 为可选家具。

---

## 理解 Understand

> WHY：理解空间本质后才能做出好决策。这是你花最多思考力的阶段——不要直接进入"放家具"模式。

**产出空间画像**（结合截图和数据，回答三个维度）：
1. **动线方向**：人从入口进来往哪走？主通道走向？
2. **纵深层次**：前场（入口侧）和深处（远离入口）分别在哪？
3. **采光轴**：窗户方向，光线通路有无遮挡风险？

**确定空间类型 → 加载房间 Skill**：

| tags 特征 | 空间类型 | 加载 Skill |
|-----------|---------|-----------|
| sleep / bedroom | 卧室 | generate-bedroom |
| shower / toilet / washing | 卫生间 | generate-bathroom |
| rest / tvMedia / dining / circulation | 客餐厅 | generate-livingroom |
| 其他封闭空间 | 按 tags 判断 | 对应房间 Skill（如有） |

**评估分区需求**：rawBoundary 顶点数 > 4（非矩形）→ 加载 generate-zoning Skill（T3 后可用）。矩形房间跳过。

---

## 策略 Strategy

> WHY：先想清楚整体方案再动手放家具。策略是全局决策的记录，让执行时有明确方向，不陷入局部优化。

遵循已加载的房间 Skill，产出**策略声明**：
- 锚点决策：核心家具选哪面墙，WHY
- 主要家具布局：衣柜/沙发等的墙面和模式
- 间距分配：各侧空间分配
- 家具配置清单：必须+可选

策略声明是**语义描述**（锚定墙面+朝向意图），不是精确坐标。精确坐标在执行阶段根据 agent_config 计算。

---

## 执行 Execute

> WHY：信赖前面阶段的决策质量，执行阶段专注精确计算。一次性放置减少上下文消耗。

### 放置

按策略声明 + design_principles + 房间 Skill 约束 + module_library agent_config，精确定位全部家具（锚点→主要→辅助，一次性写入）。

**放置前原则性检查**（每件家具）：
1. 是否阻挡门开启？（design_principles 空间硬约束）
2. 通行间隙是否满足通道标准？（design_principles 通道与间距，区分通行间隙与使用间隙）
3. 前瞻协调：主要家具是否阻断辅助家具的必要空间？（design_principles 前瞻协调规则）

> bounds/重叠/禁区等几何检查由 validate_layout 自动完成，无需心算。

### 写入

Write `schemes/{zoneId}/modules.json`，每个模块包含 moduleId、bounds（四点坐标）、facing、items。id 由 Server 在 validate_layout 时自动生成，禁止手动填写。

### 验证闭环

**【必须】**每次 Write 后调用 `mcp__canvas__validate_layout()`。
错误代码：越界(E001)、墙体重叠(E002)、柱子重叠(E003)、禁区重叠(E004)、模块间重叠(E005)。

### 修正循环（最多 2 轮）

验证失败时：
1. 列出所有违规项
2. 按修正优先级处理：**平移** → **旋转** → **缩小**（limits 内）→ **替换**同功能更小模块 → **移除**
3. Read 当前 modules.json → 修正 → Write → 再次 validate_layout

> WHY：修正优先级从最小变动到最大变动——平移保留原始设计意图，移除是最后手段。

**兜底**：2 轮修正后仍失败 → 移除违规家具，保留核心布局，报告中说明原因。

---

## 审查 Review

> WHY：截图审查是"设计师之眼"。编译检查保证物理合法，截图审查保证设计品质。

**截图**（单独调用）→ 对照四维度整体评估：

1. **空间合规**：家具未阻挡门开启、通道满足标准
2. **策略一致**：最终布局是否实现了策略声明的核心意图
3. **设计品质**：空间效率合理、功能联动、填充舒适度
4. **规则遵从**：房间 Skill 的硬约束全部满足

**判断**：
- 四维度均通过 → 汇报
- 有违规 → 修正（最多 1 轮）→ validate_layout → 汇报
- 修正后仍违规 → 移除违规家具，保留核心布局

---

## 汇报 Report

仅在审查通过后汇报：
- 空间画像摘要（动线、纵深、采光）
- 策略要点（锚点决策、关键取舍）
- 放置结果（家具清单、位置、朝向）
- 品质评估（四维度结论）
- 决策痕迹：分歧点+选择理由（如有）

**禁止**：审查未通过时报告"布置完成"。

---

## 机制速查

- **【必须】截图单独调用**：不与其他工具并行
- **修正优先级**：平移 → 旋转 → 缩小 → 替换 → 移除
- 先读后写、validate_layout 必调、目录权限 → 见 BIMCANVAS.md 安全机制
