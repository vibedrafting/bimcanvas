# Stage 2.3：construction-brief 可选家具收束规则

> strategic-plan 是可选功能候选发现和战略分歧确认阶段。
> 本阶段只把 strategic-plan 已发现、已确认或已自动代决的候选，收束成可施工的 construction-brief 表达。

## 候选来源

- 可选家具候选必须来自 `strategic-plan` 的候选集合、功能带归属或自动代决记录。
- 若 `optionalTags` 存在但 `strategic-plan` 没有候选集合，不得在 `construction-brief` 首次发明位置；应先补齐并重新保存 `strategic-plan`。
- 若 `strategic-plan` 已记录多个战略候选，本阶段只能收束已确认或已自动代决的候选，不得静默切换到另一个候选。

WHY：AskUserQuestion 只能在候选已经被发现后触发。把候选发现推迟到 construction-brief，会让战略选择被包装成施工细节，最终变成静默代决。

## 收束边界

- 不重新比较床头墙候选。
- 不重新评估主家具基本墙面归属或分区方案。
- 不新增 `strategic-plan` 没有出现过的战略候选。
- 可在 `strategic-plan` 授权范围内完成尺寸、锚点、朝向、留白和 fallback 表达。

若可选家具无法按已确认候选施工，优先在候选自身范围内调整（缩小尺寸、同墙微调、按 strategic-plan 记录省略）；需要跨候选切换、改变主家具关系或改变功能带归属时，回到战略选择处理。

WHY：本阶段避免循环重评，但不能用“禁止回流”掩盖 strategic-plan 的候选缺失。正确边界是：strategic-plan 负责发现与选择，construction-brief 负责施工收束。

## construction-brief 内容要求

construction-brief 必须包含每件保留的可选家具的完整选型结论：
- 模块 ID + 精确尺寸（宽x深）+ 朝向
- 位置来源：来自 strategic-plan 的哪个候选或自动代决
- 省略时写明：省略原因 + 保留为空的空间意图

**睡眠组额外要求**：construction-brief 必须同时写出**模块选型**和**组装锚坐标**——睡眠组在床头墙轴上的起始位置（Stage 3 顺序累加的唯一起点）：
- 完整配置：明确写 `mod_bed_001 + 床头柜×2` 或 `mod_bed_002 + 床头柜×2`，使用窗侧锚（= 床头墙窗侧边界坐标），向使用侧顺序组装
- 紧凑兜底：仅当 `mod_bed_002 + 床头柜×2` 不成立时，才允许写 `mod_bed_002 + 床头柜×1`，使用使用侧锚（= 相邻衣柜/墙边推算），向窗侧顺序组装
- 示例（1500单柜兜底）：`mod_bed_002 + 床头柜×1，使用侧锚 Y=8050（衣柜北边界7700 + 操作间距350），由此向窗侧：床头柜(450mm)→床(1500mm)→窗帘(200mm)`
- **禁止**写"居中布置"——此描述无法直接转化为精确坐标，是缝隙产生的直接来源

WHY：Stage 3 只做坐标计算，不做模块选型和位置推算。选型决策（`mod_bed_001` / `mod_bed_002`）+ 睡眠组锚坐标必须在 construction-brief 中完成。Stage 2 已有相邻家具位置信息（衣柜坐标），此时计算锚坐标成本为零；若推迟到 Stage 3，AI 需重建上下文，精度更低。
