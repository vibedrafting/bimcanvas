---
name: generate-placement
description: |
  Generate 布置 Skill。负责把规划产出的语义方案图纸转成 modules.json，
  并执行验证、品质优化和最终汇报。根据 planType 切换施工策略。
---

# Generate 布置

> 你在本 Skill 中是施工方兼品质把关人。根据图纸类型（derived/reference）采用不同的施工策略和优化权限。

---

## 1. 执行模式

进入本 Skill 后，先确认执行模式：

- **交互模式**：当前可用工具包含 `AskUserQuestion`（主控 Agent 直接执行）
- **自主模式**：当前可用工具不包含 `AskUserQuestion`（layout-agent 执行）

后续所有需要用户确认的节点，统一按当前模式处理：

- 交互模式 → 提问确认
- 自主模式 → 选择当前推荐方案继续，标记"自动代决"

---

## 2. 入场动作

**【必须】**进入本 Skill 后第一步调用：

```text
load_semantic_plan({ zoneId })
```

检查返回值：

- `status = ok` → 继续
- `status = missing` → 停止，说明未找到图纸
- `status = ambiguous_legacy` → 停止，说明旧图纸不可自动判定

读取后必须显式复述：

- `planType` 与 `effectiveVersion`（effectiveVersion 通常为 v0.3，但 reference 模式的施工约束基于 v0.2）
- 关键家具墙面归属
- 若有 `referenceAnalysis`，复述关联性等级
- 若有"自动代决"标记，也必须复述

**根据 planType 设置策略标志**：

- `planType=derived` → 完全施工自由度 + 自动优化
- `planType=reference` → 受限施工自由度 + 授权优化

如果当前设计区含 `subZones`：

- 图纸仍从父设计区 `zoneId` 读取
- 实际写入目标是子分区的 `modules.json`
- `validate_layout` 使用子分区 `zoneIds`

---

## 3. 施工前读取

**必读文件**（两种模式相同）：

- `references/design_principles.md`
- `references/design_evaluation.md`
- `modules/module_library.json`
- `schemes/zones.json`
- 当前 `modules.json`（若已存在）
- 对应房间策略文件：`references/bedroom.md` / `references/bathroom.md` / `references/livingroom.md`

**读取顺序**：
1. 先读 semantic_plan（已在入场动作完成）
2. 再读 zone boundaries
3. 最后读施工规则和模块库

---

## 4. 按图施工

### Step 1: 解析语义方案

从 semantic_plan 的 effectiveVersion 中提取：

- 家具清单（主家具 + 可选家具 + 附属家具）
- 墙面归属
- 朝向
- 保留空段（reference 模式特有）

### Step 2: 按图施工

**施工顺序**：
1. 主家具（床、衣柜等）
2. 可选家具（梳妆台、书桌等）
3. 附属家具（床头柜、窗帘等）

**坐标计算**：
- 根据墙面归属和 zone boundaries 计算精确坐标
- 根据朝向计算 facing
- 根据模块尺寸计算 bounds

**冲突处理**：
- 若发生冲突 → 进入修正循环

### Step 3: 修正循环

**触发条件**：`validate_layout` 返回错误

**修正优先级**（两种模式相同）：
1. 平移（沿墙面微调位置）
2. 旋转（调整朝向）
3. 缩小（换更小的模块）
4. 拆除附属件（如床头柜）
5. 替换（换其他模块）
6. 移除（删除该家具）

**planType 差异**：

#### derived 模式
- 所有修正操作自动执行
- 跨墙面迁移：允许
- 删除图纸家具：允许

#### reference 模式
- 以下操作需要授权：
  - 跨墙面迁移
  - 删除图纸家具
  - 侵占图纸保留空段
  - 改变角部/邻接位置
- 交互模式：AskUserQuestion 征求授权
- 自主模式：标记偏离 + 记录建议

### Step 4: Layer 1 验证

**验证内容**（两种模式相同）：
- 可达性验证（所有房间可达）
- 功能完整性验证（必要家具已放置）

**处理方式**：
- 若验证失败 → 回到修正循环
- 若多次失败 → 汇报失败原因

**【必须】**一次性写入完整结果，再调用 `mcp__canvas__validate_layout`。

---

## 5. reference 核心禁令

**为什么检查 v0.2 而非 v0.3？**

v0.2 是用户确认的语义基准（记录了用户意图），v0.3 是待修正的几何实现（可能有错误）。四条禁令确保修正过程不偏离用户意图，只修正几何错误，不改变语义决策。

**四条硬约束**（仅 reference 模式）：

1. 不得添加 v0.2 中没有的家具或附属件
2. 不得删除 v0.2 中已有家具
3. 不得侵占 v0.2 中记录的保留空段
4. 不得改变 v0.2 指定的角部或邻接关系

**检查时机**：
- 修正循环中的每个操作前
- 优化阶段的每个改善前

**检查函数**（伪代码）：

```python
def check_reference_constraints(operation, semantic_plan_v02):
    """检查操作是否违反 reference 核心禁令"""
    
    # 提取 v0.2 中的家具清单和保留空段
    furniture_list = extract_furniture_from_v02(semantic_plan_v02)
    reserved_spaces = extract_reserved_spaces_from_v02(semantic_plan_v02)
    
    # 检查四条禁令
    if operation.type == "add_furniture":
        if operation.furniture not in furniture_list:
            return "违反禁令1：不得添加 v0.2 中没有的家具"
    
    if operation.type == "remove_furniture":
        if operation.furniture in furniture_list:
            return "违反禁令2：不得删除 v0.2 中已有家具"
    
    if operation.type == "place_furniture":
        if overlaps_with_reserved_space(operation.bounds, reserved_spaces):
            return "违反禁令3：不得侵占保留空段"
    
    if operation.type == "change_position":
        if changes_corner_or_adjacency(operation, semantic_plan_v02):
            return "违反禁令4：不得改变角部或邻接关系"
    
    return None  # 无违反
```

**违反处理**：
- 交互模式：AskUserQuestion 征求授权
- 自主模式：跳过该操作 + 标记偏离

---

## 6. 优化阶段

### derived 模式（自动优化）

**流程**：
1. 调用截图工具审查结果
   - **【必须】**审查截图时以当前视觉证据为准。若截图显示的布局与 modules.json 中的数据不一致，以截图为准重新审查，不得用已写入数据解释截图。
2. 参照 `design_evaluation.md` 做品质复核
3. 每个维度最多尝试一次改善
4. 改善后再次 `validate_layout`

**自动执行**：无需用户授权

---

### reference 模式（授权优化）

**流程**：

#### 交互模式
1. 调用截图工具审查结果
2. 品质复核
3. 识别优化建议
4. 汇报建议 + AskUserQuestion 征求授权
5. 若授权 → 执行优化 → 重新验证
6. 若不授权 → 跳过优化

#### 自主模式
1. 调用截图工具审查结果
2. 品质复核
3. 识别优化建议
4. 记录建议（不执行）
5. 在汇报中上报建议

**优化建议格式**：
```
[优化建议]
- 维度：动线流畅度
- 问题：床头柜阻挡通行
- 建议：将床头柜向内侧移动 200mm
- 影响：不改变墙面归属，不侵占保留空段
```

---

## 7. 汇报

最终汇报必须包含：

**基础信息**：
- 施工依据：`planType` + `effectiveVersion`
- 若 `planType=reference`，说明关联性等级

**放置结果**：
- 家具、墙面、朝向
- 验证结果：布局验证 + 可达性 + 功能完整性

**优化结果**：
- derived 模式：哪些维度做了改善，哪些跳过
- reference 模式（交互）：哪些优化已授权执行
- reference 模式（自主）：记录的优化建议

**偏离标注**（若有）：
- 自动代决项
- 自动适配项
- 违反核心禁令项（需授权）

---

## 约束总览

**【硬约束】**

- 入场必须 `load_semantic_plan`
- 一次性写入后必须 `validate_layout`
- 不编造家具尺寸
- reference 模式：四条核心禁令不可静默违反

**【软指导】**

- 修正优先级：平移 → 旋转 → 缩小 → 拆除附属件 → 替换 → 移除
- 优化尾段每维度最多一次改善
- 战略选择交互模式下应询问用户
- reference 模式：优化需授权

**【自由区域】**

- 家具间精确间距
- 附属件精确位置
- 模块参数化尺寸的精确值（在 limits 范围内）
- 坐标计算方式
- derived 模式的优化策略选择
