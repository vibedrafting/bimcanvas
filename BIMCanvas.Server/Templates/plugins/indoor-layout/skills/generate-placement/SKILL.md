---
name: generate-placement
description: |
  Generate 布置 Skill。负责把 `construction-brief` 语义合同转成 modules.json，
  并执行验证、必要修正、品质复核和最终汇报。
---

# Generate 布置

> 你在本 Skill 中是施工方兼品质把关人。你只读取自包含的 `construction-brief` 语义合同，不读取 raw reference_analysis。

## 路径约定

- Skill 文件本体不决定业务文件根目录；以下相对路径均以当前项目目录为根目录
- `references/*.md` 是项目级运行时参考规则
- `modules/`、`computed/`、`schemes/` 也都是当前项目目录下的业务数据

**【必须】**模块布置只写入目标叶子分区的 `modules.json`。
**【禁止】**写入 `schemes/modules.json`、`modules/modules.json`，或有 `subZones` 的容器分区 `modules.json`。

示例：`rz_3` 有 `subZones=[dz_1,dz_2]` 时：
- 衣帽/梳妆家具 → `schemes/rz_3/dz_1/modules.json`
- 睡眠家具 → `schemes/rz_3/dz_2/modules.json`
- 禁止 → `schemes/modules.json` 或 `schemes/rz_3/modules.json`

WHY：Server 和验证器按叶子分区加载模块。写到根级或容器分区会导致“0 个模块，0 个错误”的假成功。

## 1. 执行模式

进入本 Skill 后，先确认执行模式：

- **交互模式**：当前可用工具包含 `AskUserQuestion`（主控 Agent）
- **自主模式**：当前可用工具不包含 `AskUserQuestion`（layout-agent）

后续所有需要用户确认的节点，统一按当前模式处理：

- 交互模式 → AskUserQuestion
- 自主模式 → 不能改图，只能上报“自动改图建议”

---

## 2. 入场动作

**【必须】**进入本 Skill 后第一步调用：

```text
load_semantic_plan({ zoneId })
```

检查返回值：

- `status = ok` → 继续
- `status = missing` → 停止，说明未找到可施工图纸
- `status = ambiguous_legacy` → 停止，说明旧图纸不可自动判定
- `status = legacy_reference_requires_replan` → 停止，说明当前仍是旧版 reference 工作流，必须先重新规划

读取后必须显式复述：

- `effectiveTag`（必须是 `construction-brief`）
- 关键家具墙面归属
- 若存在 `referenceAnalysisTag`，只把它当作“本合同消费自哪版定稿参考分析”的审计元数据复述，不得把它当运行时输入
- 若有“自动代决”“自动适配”标记，也必须复述

**【必须】**placement 只读取 `construction-brief.content`。不得调用 `load_reference_analysis`，也不得根据历史 reference 文件补充理解。

**【必须】**`load_semantic_plan` 之后，立刻调用 `mcp__canvas__get_zone_boundaries({ zoneId })`，并读取 `computed/exclusions.json`。

**【必须】**`zone boundaries`、`passage`、`exclusions` 与 `construction-brief` 合同并列为施工前事实，不得等 `validate_layout` 报错后才第一次考虑。

---

## 3. 施工前读取

**必读输入**：

- `load_semantic_plan` 返回的 `construction-brief`
- `mcp__canvas__get_zone_boundaries({ zoneId })`
- `computed/exclusions.json`
- `references/design_principles.md`
- `references/design_evaluation.md`
- `modules/module_library.json`
- `schemes/zones.json`
- 当前目标叶子分区的 `modules.json`（若已存在）；若目标 zone 是容器，读取其所有叶子子分区的 `modules.json`
- 对应房间策略文件：`references/bedroom.md` / `references/bathroom.md` / `references/livingroom.md`

**读取顺序**：

1. 先读 `load_semantic_plan` 返回的 `construction-brief`
2. 再读 `mcp__canvas__get_zone_boundaries({ zoneId })`
3. 再读 `computed/exclusions.json`
4. 最后读模块库、房间策略、当前目标叶子分区的 `modules.json`、设计原则与评估规则

---

## 4. 按图施工

### Step 1：解析语义合同

按以下 canonical 章节顺序解析 `construction-brief.content`：

- `## 主要家具`
- `## 可选/附属家具`
- `## 保留空段与关键留白`
- `## 关键关系与分区意图`
- `## 合同内 fallback`
- `## 参考采纳与偏离摘要`
- `## 自动标记`

若 `## 主要家具` 缺失，或任一主家具条目缺少墙面归属或朝向语义，停止并上报“`construction-brief` 合同不完整”，不得靠自由推断继续施工。

从合同中提取：

- 家具清单（主家具 + 可选家具 + 附属家具）
- 墙面归属
- 朝向
- 关键留白 / 保留空段
- 关键邻接关系
- 合同内 fallback 的触发条件、可自动执行方案、不可越界边界

### Step 2：按图施工

**施工顺序**：

1. 主家具
2. 可选家具
3. 附属家具

**坐标计算**：

- 写入前先根据 `zone boundaries`、`passage`、`exclusions` 过滤候选坐标
- 根据墙面归属和 zone boundaries 计算精确坐标
- 根据朝向计算 facing
- 根据模块尺寸计算 bounds

**modules 写入工具**：

**【必须】**用 `Write` / `Edit` 工具直接编辑 `modules.json` 文件。modules.json 形态为 wrapper `{schemeMetadata: {summary}, modules: [...]}`：

- **canonical** 写入（无 variant）：路径 `schemes/{designZoneId}/modules.json`（顶层叶子）或 `schemes/{designZoneId}/{leafZoneId}/modules.json`（嵌套叶子）。首次写入时 `schemeMetadata.summary` 可为空字符串。
- **variant** 写入：调用方应先调 `mcp__canvas__register_variant` 创建目录骨架；之后只用 `Write` / `Edit` 修改 `modules` 数组，**必须保留 register_variant 写入的 `schemeMetadata.summary`**。

写入模板（Write 整个文件内容）：

```json
{
  "schemeMetadata": { "summary": "" },
  "modules": [
    {
      "moduleId": "mod_bed_001",
      "moduleName": "双人床",
      "bounds": [[9100, 1750], [11100, 1750], [11100, 3750], [9100, 3750]],
      "facing": { "value": null, "semantic": "south" },
      "items": []
    }
  ]
}
```

- `bounds`：矩形 4 顶点，顺序 左下→右下→右上→左上，单位 mm
- `moduleName`：必填，与 `module_library.json` 一致
- `items`：必填，无子项时写 `[]`
- **必须保留** 外层 `schemeMetadata` 字段（即使 summary 为空字符串也要保留键）——误删会让 Reader 出错或 Web 端 tooltip 丢失

**facing 规则**：

- `facing` 写成对象：`{ "value": [x, y] | null, "semantic": string | null }`
- `value` 是常规读取阶段的方向真理；`semantic` 是 AI 语义输入槽，只接受 8 个标准方向词
- **推荐**默认写 `semantic`，`value` 留 `null`。示例：`"facing": { "value": null, "semantic": "south" }`
- 若 `value` 与 `semantic` 同时存在：常规读取只认 `value`；`validate_layout` 会用有效 `semantic` 覆盖 `value`，再把 `semantic` 清空为 `null`

**【必须】**`validate_layout` 只做编译验证与修正触发，不承担第一次发现几何事实的职责。

**冲突处理**：

- 若发生冲突 → 进入修正循环

### Step 3：修正循环

**触发条件**：`validate_layout` 返回错误

#### 几何级修正（可自动执行）

以下操作不改变语义合同，可自动执行：

1. 同一墙面内微调
2. 旋转但不改变语义朝向
3. 在合同允许范围内缩小模块（不得改变合同写明的尺寸等级、满墙/填满有效段等意图）
4. 收缩或删除附属件
5. 同类模块的小幅替换（不改变合同含义）

执行后统一记为 `[自动适配]`。

**WHY**：这些操作只是实现层面的微调，不改变设计意图。类比建筑施工中的"现场微调"——墙面内左右挪 50mm、旋转 5° 对齐墙面、缩小 100mm 避让管道，都不需要重新报审图纸。

#### 合同内 fallback（可自动执行）

若 `construction-brief` 的 `## 合同内 fallback` 已明确写入 fallback，且当前失败原因与触发条件一致，可执行其中“可自动执行的下一档方案”，并统一记为 `[自动适配]`。

执行前必须确认 fallback 仍满足全部边界：
- 不改变墙面归属
- 不改变核心功能数量
- 不侵占关键留白
- 不破坏满墙/填满有效段意图
- 不引入新的用户偏好选择

**WHY**：placement 不判断领域策略本身，只执行 `construction-brief` 已预授权的替代施工路径。这样既保留“construction-brief 是唯一施工合同”，又避免把某个房间或家具的专属知识写进通用施工规则。

#### 语义级改图（不能静默执行）

以下操作会改写语义合同，必须升级：

- 跨墙面迁移
- 增加合同中没有的家具
- 删除合同中明确存在的家具
- 侵占合同中写明的保留空段
- 改变关键邻接关系、角部关系或核心分区意图
- 缩短合同明确要求的满墙窗帘
- 降级合同明确锁定的主家具尺寸等级或核心组合配置
- 压缩合同明确选择的衣柜等级，或破坏“填满有效段”的合同意图

例外：`construction-brief` 已在 `## 合同内 fallback` 明确授权的下一档方案不属于合同外语义改图，可按“合同内 fallback”执行。未写入 `construction-brief` 的换墙、删家具、降级主家具等级、截断窗帘仍必须升级。

以上情况统一记为 `[自动改图建议]`，不得降格为 `[自动适配]`。

**WHY**：这些操作改变了设计意图，需要重新确认。类比建筑施工中的"设计变更"——把衣柜从北墙挪到西墙、增加一个书桌、删除床头柜、截断满墙窗帘或把 1500 衣柜退成 1200 柜，都需要设计师重新签字。

**合同冲突处理**：若 `validate_layout` 报错只能通过缩短满墙窗帘、降级主家具等级、压缩衣柜等级等方式解决，且 `construction-brief` 未明确授权对应 fallback，说明合同与几何事实冲突。此时停止静默修正，按当前执行模式升级处理，不得为了得到 0 error 而改写设计意图。

**交互模式**：
- AskUserQuestion 征求授权

**自主模式**：
- 停止自动改图
- 输出“自动改图建议”
- 不得静默继续落地

### Step 4：Layer 1 验证

**验证内容**：

- 可达性验证
- 功能完整性验证

**处理方式**：

- 若验证失败且仍能通过几何级修正解决 → 回到修正循环
- 若验证失败且需要语义级改图 → 升级处理
- 若多次失败 → 汇报失败原因

**【必须】**一次性写入完整结果，再调用 `mcp__canvas__validate_layout({ zoneIds: [目标叶子zoneIds] })`。

**验证闸门**：
- 验证报告中的模块总数必须与本轮目标叶子文件中的模块总数一致
- 若本轮写入了模块但验证显示 `0 个模块`，这是路径错误，不是验证通过
- 必须重新解析叶子分区路径并写入正确文件，禁止汇报成功

---

## 5. 优化阶段

优化也必须遵守“几何级可自动，语义级需升级”的边界。

### 自动可执行的优化

- 不改变墙面归属的细微平移
- 不改变合同含义的附属件整理
- 不破坏留白的局部间距优化

执行后统一记为 `[自动适配]`。

**WHY**：优化阶段的自动执行边界与修正阶段一致——只要不改变语义合同，就可以自动执行。这保证了"施工方可以优化细节，但不能擅自改图"的一致性原则。

### 不可静默执行的优化

- 会导致跨墙面迁移
- 会新增或删除家具
- 会改变关键留白、邻接或分区意图

以上情况统一记为 `[自动改图建议]`。

**WHY**：即使是"优化"，只要触及语义边界，就必须征求授权。避免"我觉得这样更好"的单方面决策。

#### 交互模式

1. 调用截图工具审查结果
2. 按 `design_evaluation.md` 做品质复核
3. 识别优化建议
4. 仅当优化不改写合同才自动执行；否则 AskUserQuestion 征求授权

#### 自主模式

1. 调用截图工具审查结果
2. 做品质复核
3. 不改合同的优化可执行一次
4. 改合同的优化只记录为“自动改图建议”

**【必须】**审查截图时以当前视觉证据为准。若截图显示布局与 `modules.json` 不一致，以截图为准重新审查，不得用已写入数据解释截图。

---

## 6. 汇报

最终汇报必须包含：

**基础信息**
- 施工依据：`effectiveTag=construction-brief`
- 若存在 `referenceAnalysisTag`，说明它只是溯源字段

**放置结果**
- 家具、墙面、朝向
- 验证结果：布局验证 + 可达性 + 功能完整性

**修正与优化**
- 自动执行了哪些几何级修正
- 执行了哪些合同内 fallback
- 执行了哪些不改合同的优化
- 哪些建议因会改合同而未执行

**偏离标注**
- 自动代决项
- 自动适配项
- 自动改图建议

**合同同步**
- 若本轮执行了 `construction-brief` 合同内 fallback，或用户授权了语义级改图，最终汇报前必须调用 `save_semantic_plan({ tag: "construction-brief" })` 重写当前生效合同，使 `semantic_plan construction-brief` 与最终 `modules.json` 一致。
- 重写后再汇报，不得只更新 `modules.json` 就宣布完成。

**WHY**：`validate_layout` 只证明几何合法，不证明施工结果仍匹配合同。后续 edit、二次 placement 和审计都会读取 `construction-brief`，合同不一致会把已经解决的问题重新带回流程。

---

## 约束总览

**【硬约束】**

- 入场必须 `load_semantic_plan`
- 只读取 `construction-brief` 自包含合同
- 一次性写入后必须 `validate_layout`
- 合同内 fallback 或授权改图后必须重写 `construction-brief`
- 不编造家具尺寸
- 不得静默执行语义级改图

**【软指导】**

- 修正优先级：同墙微调 → 旋转 → 缩小 → 附属件收缩 → 同类替换
- 优化阶段每个维度最多一次改善
- 战略级改图在交互模式下应询问用户

**【自由区域】**

- 家具间精确间距
- 附属件精确位置
- limits 范围内的参数化尺寸
- 坐标计算方式
