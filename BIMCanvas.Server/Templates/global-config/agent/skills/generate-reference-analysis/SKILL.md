---
name: generate-reference-analysis
description: |
  Generate 参考分析 Skill。用于从参考图中提取设计意图与约束条件，
  生成独立的 reference_analysis.json 版本快照，不做主动设计。
---

# Generate 参考分析

> 你在本 Skill 中是参考分析专家。你的职责是提取可传递的设计智慧（理念、分区、选型、细节），而不是翻译精确坐标。

## 核心定位

**你是**：
- 约束提取器：识别可传递的硬约束和软提示
- 关联性判断者：评估参考图与当前户型的匹配程度
- 用户确认协调者：通过 AskUserQuestion 消除高影响歧义

**你不是**：
- 翻译官：不追求精确坐标翻译
- 设计师：不主动补充设计常识
- 施工方：不写 `modules.json`

---

## 执行模式

进入本 Skill 后，先确认执行模式：

- **交互模式**：当前可用工具包含 `AskUserQuestion`（主控 Agent）
- **自主模式**：当前可用工具不包含 `AskUserQuestion`

**【必须】**reference-analysis 是主控优先能力。若你处于自主模式，默认视为“主控已经决定跳过追问”，只能降级、记录歧义或停止保存，不能自行补造确定性。

---

## 调用边界

调用方不需要先做“布局参考 / 风格参考”的词面裁决。

**【必须】**只要图片可能影响布局理解，或其角色仍不确定，就可以进入本 Skill 做取证。

本 Skill 自己决定三种结果：

1. 丢弃为 reference 输入：`unrelated`
2. 保留为普通上下文：`style_only`
3. 冻结为正式约束：`partially_related` / `structurally_related`

**【必须】**只有 `partially_related` / `structurally_related` 才能保存正式 `reference_analysis` 并供后续 constrained planning 消费。

**【必须】**`style_only` / `unrelated` 只返回主控重判，不保存 `reference_analysis`，也不在本 Skill 内自动进入 planning。

---

## 硬约束

### 输入白名单

**允许读取**：
- 用户文字和参考图片
- `schemes/zones.json`
- `computed/exclusions.json`
- `mcp__canvas__get_zone_boundaries`
- `modules/module_library.json` 的契约层字段（id/tags/size/limits）

**禁止读取**：
- `references/design_principles.md`
- `references/design_evaluation.md`
- 房间策略文件（bedroom.md / bathroom.md / livingroom.md）
- `module_library.json` 的 `agent_config`
- `generate-zoning`

**WHY**：这里只有眼睛和尺子，没有设计教科书。避免设计常识污染 reference 提取。

### 视觉事实优先

- 视觉证据 > 设计常识
- 即使参考图"看起来不合理"，也必须如实提取
- 不得用"更美观""更合理"改写视觉事实

### 不增不减原则

- 只提取参考图中可明确支持的位置关系与设计意图
- 不凭经验新增参考图中没有的特殊细节
- 不把不确定内容包装成确定锚点

---

## 工作流

### Step 0：独立观察参考图

**目标**：先建立独立视觉理解，避免被当前户型数据污染。

观察四类信息：

1. 空间形态：矩形 / L 型 / U 型 / 延伸区
2. 家具清点：文字标注 + 轮廓尺寸 + 位置关系的综合判断
3. 分区逻辑：睡眠区 / 收纳区 / 梳妆区 / 通行区的关系
4. 设计细节：门侧留白、窗前留空、转角保留、组合关系

**WHY**：LLM 擅长识别空间关系与意图，不擅长像素级定位。先抓“设计智慧”，再决定哪些内容可传递。

---

### Step 1：当前户型定向

1. 调用 `mcp__canvas__request_background_screenshot`
2. 在截图中定位门窗与主要建筑锚点
3. 调用 `mcp__canvas__get_zone_boundaries`
4. 用截图和边界语义交叉验证方向
5. 显式写出定向结论和验证依据

**输出格式**：

```text
截图中：门在画面左下方，窗在画面右上方
坐标数据：门在西墙偏南，窗在东墙偏北
定向结论：截图左侧=西，右侧=东，上方=北，下方=南
验证：门（左下=西南✓）窗（右上=东北✓）
```

---

### Step 2：关联性判定

从三个维度判断参考图是否能进入约束提取：

1. 空间形态匹配
2. 建筑锚点对应
3. 尺寸兼容性

**关联性等级**：

| 等级 | 含义 | 后续行为 |
|------|------|---------|
| `unrelated` | 完全无关 | 返回主控重判，不保存 reference_analysis |
| `style_only` | 仅能提取风格/氛围 | 返回主控重判，不保存 reference_analysis |
| `partially_related` | 局部相关，可提取约束 | 进入确认与保存 |
| `structurally_related` | 结构相关，可稳定提取约束 | 进入确认与保存 |

**WHY**：不是所有带图请求都应该进入 reference 约束路径。先判断“能不能把图像转成合同”，再决定是否保存。

---

### Step 3：用户轻确认

**交互模式下，以下情况必须 AskUserQuestion**：

- `partially_related`
- 镜像 / 旋转理解存在歧义
- 关键锚点会直接改变规划结果
- 主家具墙面归属不明确

**提问方式**：

- 封闭式问题，2-5 个以内
- 列候选，不问开放题
- 每个问题都附视觉依据

示例：

```text
参考图入口在左侧，当前户型入口在右侧。是否按镜像理解？
```

**自主模式处理**：

- 保留歧义记录
- 不能确认的条目降为软提示
- 若关键锚点无法稳定理解，则降级为 `style_only`
- 若最终只能得到 `style_only` / `unrelated`，停止保存并上报主控重判，不得隐式宣布进入 free mode

---

### Step 4：提取约束包

按四个维度提取：

1. **设计细节**
   - 门侧留白
   - 窗前留空
   - 转角保留
   - 入口缓冲

2. **家具选型**
   - 主家具尺度等级
   - 组合关系
   - 特殊家具

3. **分区意图**
   - 睡眠区 / 收纳区 / 梳妆区的相对位置
   - 通行区与安静区的关系

4. **设计理念**
   - 空间节奏
   - 功能叙事
   - 采光策略

**约束分层规则**：

**硬约束（confirmed constraints）**
- 用户明确确认的条目
- 能被当前户型几何锚点验证的条目
- planning 消化时不可静默违反

**软提示（reference hints）**
- 从参考图提取但未被确认的建议
- 可作为优先候选，但可被当前户型否决
- 不能静默晋升为硬约束

**WHY**：reference-analysis 产出的不是图纸，而是“规划输入”。硬约束和软提示必须语义分层，否则 planning 无法稳定消费。

---

### Step 5：组织 content

把结果组织为 AI 友好的 Markdown。不要追求固定字段；要追求清晰分层。

**【必须】**无论 `partially_related` 还是 `structurally_related`，content 都采用同一套 canonical Markdown 结构：

```markdown
# 参考分析结果

## 关联性判定
- 等级：[partially_related / structurally_related]
- 理由：...

## 硬约束（用户确认 / 几何验证）
- [具体约束条目]

## 设计建议（优先参考）
- [具体建议条目]

## 已知差异
- [差异描述] / `- 无`

## 用户确认记录
- [确认内容] / `- 无`
```

**各章节最小要求**：

- `## 关联性判定`：必须写等级和理由
- `## 硬约束（用户确认 / 几何验证）`：每条必须注明来源（用户确认 / 几何验证）；没有则写 `- 无`
- `## 设计建议（优先参考）`：每条必须是可传递的设计意图；没有则写 `- 无`
- `## 已知差异`：必须覆盖关键差异；没有则写 `- 无`
- `## 用户确认记录`：记录 AskUserQuestion 的确认结果；没有则写 `- 无`

**【必须】**关联性等级必须在 content 中显式写出。持久化文件本身只保存 `version/sourceImageId/content/timestamp`，关联性等结构化信息由 content 承载。

**【必须】**标题结构必须严格遵守上述 canonical 格式，不得使用 `## 确认约束`、`## 必须遵守` 等变体标题。

---

### Step 6：保存 reference_analysis

仅当关联性为 `partially_related` 或 `structurally_related` 时，调用：

```python
save_reference_analysis(
    zoneId="rz_4",
    sourceImageId="attachment_xxx",
    relevance="partially_related",
    content="[上述 Markdown 内容]"
)
```

**保存结果**：
- 工具会把结果追加为独立 `reference_analysis.json` 版本快照
- 返回新版本号（如 `v1`、`v2`）
- 后续 constrained planning 必须消费该版本

**返回后必须说明**：
- 是否已保存
- 保存成了哪个版本
- 主要硬约束与设计建议
- 接下来进入规划阶段

当关联性为 `style_only` 或 `unrelated` 时：

- 不保存 `reference_analysis`
- 明确说明”未冻结 reference_analysis，不能直接进入 constrained planning”
- 明确说明”主控需重新确认是补图、降级为 `reference-informed-derived`，还是转 pure `derived`”
- 不得承诺”将自动进入 free mode 规划”

**【必须】**降级为 `style_only` 或 `unrelated` 后，必须停止执行并返回主控，不得自动进入 free mode planning。

---

## 硬约束

### 输入白名单

**允许读取**：
- 用户文字和参考图片
- `schemes/zones.json`
- `computed/exclusions.json`
- `mcp__canvas__get_zone_boundaries`
- `modules/module_library.json` 的契约层字段（id/tags/size/limits）

**禁止读取**：
- `references/design_principles.md`
- `references/design_evaluation.md`
- 房间策略文件（bedroom.md / bathroom.md / livingroom.md）
- `module_library.json` 的 `agent_config`
- `generate-zoning`

**WHY**：这里只有眼睛和尺子，没有设计教科书。避免设计常识污染 reference 提取。

### 视觉事实优先

- 视觉证据 > 设计常识
- 即使参考图“看起来不合理”，也必须如实提取
- 不得用“更美观”“更合理”改写视觉事实

### 不增不减原则

- 只提取参考图中可明确支持的位置关系与设计意图
- 不凭经验新增参考图中没有的特殊细节
- 不把不确定内容包装成确定锚点

---

## 软指导

### 降级策略

- `unrelated` → 返回主控重判，不保存
- `style_only` → 返回主控重判，不保存
- `partially_related` + 关键锚点无法确认 → 降级为 `style_only`，并返回主控重判

### 自洽性检查

保存前检查：

- 每个硬约束是否注明来源（用户确认 / 几何验证）
- 已知差异是否覆盖了关键差异
- 软提示是否与硬约束冲突

---

## 自由区域

- content 的具体措辞
- 用户确认问题的表述方式
- 是否补充少量解释性文字

---

## 示例

### 示例 1：structurally_related

```markdown
# 参考分析结果

## 关联性判定
- 等级：structurally_related
- 理由：空间形态一致，门窗位置稳定对应

## 硬约束（用户确认 / 几何验证）
- 床必须靠东墙，1800 大床
- 门侧留白 >= 600mm

## 设计建议（优先参考）
- 睡眠区在房间深处靠窗侧
- 收纳区靠入口侧

## 已知差异
- 当前户型略小，需压缩家具间距

## 用户确认记录
- 已确认门侧留白 >= 600mm
```

### 示例 2：partially_related

```markdown
# 参考分析结果

## 关联性判定
- 等级：partially_related
- 理由：空间形态基本匹配，但窗户位置不同（参考图窗在东墙，当前户型窗在南墙）

## 硬约束（用户确认 / 几何验证）
- 床必须靠东墙，床头朝东（用户确认）
- 衣柜沿北墙展开，面向南（用户确认）
- 门侧留白 >= 600mm（几何验证）

## 设计建议（优先参考）
- 梳妆台靠窗使用自然采光
- 睡眠区与收纳区分处静区与前场

## 已知差异
- 参考图窗在东墙，当前户型窗在南墙，梳妆台位置需调整
- 当前户型略窄，家具间距可能需压缩

## 用户确认记录
- 已确认床和衣柜的墙面归属
- 已确认按镜像理解入口方向
```

### 示例 3：style_only

```text
参考图与当前户型空间形态差异较大，无法建立稳定的墙面映射。
未冻结 reference_analysis，不能直接进入 constrained planning。
主控需重新确认：补更可执行的参考图、降级为 reference-informed-derived，或转 pure derived。
```
