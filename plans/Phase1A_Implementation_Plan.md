# Phase 1A 实施计划：工作流结构边界改造

> 基于 `reviews/WorkflowSpeedup_Review.md` 共识，为新窗口实施提供完整的操作指南。
> 目标输出位置：`E:\工作文档\开发类\MyCode\BIMCanvas\plans\Phase1A_Implementation_Plan.md`

---

## Context

### 问题

BIMCanvas Agent 执行主卧布置任务耗时 26:53，其中 thinking 占 20:22（75%）。Agent 的设计质量很高（validate_layout 一次通过、设计目标全达成），但规划阶段（Stage 2）的两个 thinking block 分别长达 3:47 和 8:43，Stage 3 写入前的心算预验证长达 5:50。

### 根因

提示词中的行为引导（"出声思考"、"精度边界"、"允许试错"）运行在对话层，但 Agent 的分析发生在 thinking 层。两层之间没有强制同步点——只要 thinking token 预算够用，Agent 就会在 thinking 中完成所有分析后才输出。这是模型训练的自然倾向，提示词无法覆盖。

### 解决方案

用 MCP 工具调用 `save_semantic_plan` 作为每个规划子阶段的"提交按钮"。工具调用结束当前 turn 的 thinking，返回结果后开启新 turn——每个 turn 只处理一个子阶段。同时删除已证明无效的行为引导规则，释放注意力预算。

### 预期效果

| 指标 | 当前基线 | Phase 1A 目标 |
|------|---------|--------------|
| Stage 2 最大单次 thinking | 8:43 | <= 4 min / turn |
| 首次 Write 时间 | 第 24 分钟 | < 第 15 分钟 |
| validate_layout | 首次通过 | 最多 2 轮通过 |
| 设计质量 | 4 目标全达成 | 保持 |

---

## 实施纪律（Phase 1A 硬边界）

**以下约束贯穿整个 Phase 1A，不可违反：**

1. `save_semantic_plan` 只做三件事：保存版本、Web 展示、turn 边界。**不含 next_stage_hints、不含候选墙段、不含任何衍生分析字段**
2. `semantic_plan.json` 只存四个字段：zoneId、version、content、timestamp
3. 不动 `config.json` 参数（effort=low、thinking=adaptive、maxThinkingTokens=16000 保持不变）
4. 不动房间策略文件（bedroom.md / bathroom.md / livingroom.md）
5. 不做 C 类计算指令迁移（留给 Phase 2）
6. `BIMCANVAS.md` 只做与新结构直接相关的最小修改

---

## 交付物 1：MCP 工具 `save_semantic_plan`

### 1.1 Python 端（Agent）

**文件**：`BIMCanvas.Agent/src/mcp/canvas.py`

**改动**：在现有三个工具之后（第 814 行附近），新增第四个工具：

```python
@tool(
    "save_semantic_plan",
    "保存语义方案版本。在规划阶段的每个子阶段（2.1/2.2/2.3）完成后调用，提交当前版本的语义方案。",
    {
        "$schema": "http://json-schema.org/draft-07/schema#",
        "type": "object",
        "properties": {
            "zoneId": {
                "type": "string",
                "description": "目标 Zone ID，如 'rz_3'"
            },
            "version": {
                "type": "string",
                "enum": ["v0.1", "v0.2", "v0.3"],
                "description": "语义方案版本：v0.1=空间骨架, v0.2=主体框架, v0.3=完整方案"
            },
            "content": {
                "type": "string",
                "description": "语义方案文本内容（markdown 格式）"
            }
        },
        "required": ["zoneId", "version", "content"],
        "additionalProperties": False
    }
)
async def save_semantic_plan(args: dict[str, Any]) -> dict[str, Any]:
    """保存语义方案版本"""
    zone_id = args["zoneId"]
    version = args["version"]
    content = args["content"]

    body = {
        "zoneId": zone_id,
        "version": version,
        "content": content
    }

    try:
        async with aiohttp.ClientSession() as session:
            async with session.post(
                f"{SERVER_URL}/api/semantic-plan/save",
                json=body
            ) as resp:
                if resp.status == 200:
                    result = await resp.json()
                    return {
                        "content": [{
                            "type": "text",
                            "text": f"语义方案 {version} 已保存。继续下一阶段。"
                        }]
                    }
                else:
                    error_text = await resp.text()
                    return {
                        "content": [{"type": "text", "text": f"保存失败: {error_text}"}],
                        "is_error": True
                    }
    except aiohttp.ClientError as e:
        return {
            "content": [{"type": "text", "text": f"无法连接 Server: {str(e)}"}],
            "is_error": True
        }
```

**工具注册**：修改 `canvas_mcp` 的 tools 列表（约第 818-828 行），添加 `save_semantic_plan`：

```python
canvas_mcp = create_sdk_mcp_server(
    name="canvas",
    version="1.0.0",
    tools=[
        request_background_screenshot,
        validate_layout,
        get_zone_boundaries,
        save_semantic_plan,  # 新增
    ],
)
```

**CANVAS_ALLOWED_TOOLS**：添加新工具到预批准列表（约第 831-837 行）：

```python
CANVAS_ALLOWED_TOOLS = [
    "mcp__canvas__request_background_screenshot",
    "mcp__canvas__validate_layout",
    "mcp__canvas__get_zone_boundaries",
    "mcp__canvas__save_semantic_plan",  # 新增
]
```

### 1.2 C# 端（Server）

**新建文件**：`BIMCanvas.Server/Controllers/SemanticPlanController.cs`

参照 `ValidationController.cs` 的模式，创建新的 Controller：

```csharp
[ApiController]
[Route("api/semantic-plan")]
public class SemanticPlanController : ControllerBase
{
    private readonly ProjectContext _projectContext;
    private readonly IHubContext<CanvasHub> _hubContext;
    private readonly ILogger<SemanticPlanController> _logger;

    // 构造函数注入 ProjectContext, HubContext, Logger

    [HttpPost("save")]
    public async Task<ActionResult> SaveSemanticPlan([FromBody] SaveSemanticPlanRequest request)
    {
        if (!_projectContext.IsLoaded)
            return BadRequest(new { message = "没有加载的项目" });

        var projectPath = _projectContext.GetActiveWorktreePath()
                          ?? _projectContext.CurrentProjectPath!;

        // 存储路径：schemes/{zoneId}/semantic_plan.json
        var schemesDir = Path.Combine(projectPath, "schemes", request.ZoneId);
        Directory.CreateDirectory(schemesDir);
        var filePath = Path.Combine(schemesDir, "semantic_plan.json");

        // 读取现有版本（如果存在）
        var versions = new List<SemanticPlanVersion>();
        if (System.IO.File.Exists(filePath))
        {
            var existing = System.IO.File.ReadAllText(filePath);
            versions = JsonConvert.DeserializeObject<List<SemanticPlanVersion>>(existing)
                       ?? new List<SemanticPlanVersion>();
        }

        // 添加或更新版本
        var entry = new SemanticPlanVersion
        {
            ZoneId = request.ZoneId,
            Version = request.Version,
            Content = request.Content,
            Timestamp = DateTime.UtcNow.ToString("o")
        };

        // 如果同版本已存在则覆盖
        versions.RemoveAll(v => v.Version == request.Version);
        versions.Add(entry);
        versions.Sort((a, b) => string.Compare(a.Version, b.Version, StringComparison.Ordinal));

        // 写入文件
        var json = JsonConvert.SerializeObject(versions, Formatting.Indented);
        await System.IO.File.WriteAllTextAsync(filePath, json);

        // SignalR 推送到 Web 端
        await _hubContext.Clients.All.SendAsync("SemanticPlanUpdated", new
        {
            zoneId = request.ZoneId,
            version = request.Version,
            content = request.Content,
            timestamp = entry.Timestamp
        });

        _logger.LogInformation(
            "[SemanticPlan] 已保存 {ZoneId} {Version}",
            request.ZoneId, request.Version);

        return Ok(new { saved = true, version = request.Version });
    }
}

public class SaveSemanticPlanRequest
{
    public string ZoneId { get; set; }
    public string Version { get; set; }
    public string Content { get; set; }
}

public class SemanticPlanVersion
{
    public string ZoneId { get; set; }
    public string Version { get; set; }
    public string Content { get; set; }
    public string Timestamp { get; set; }
}
```

### 1.3 Web 端

**文件**：`BIMCanvas.Web/src/services/SignalRService.ts`

在 `setupListeners()` 方法中添加新的监听器：

```typescript
this.connection.on("SemanticPlanUpdated", (data: any) => {
    window.dispatchEvent(new CustomEvent('bimcanvas:semantic-plan-updated', { detail: data }));
});
```

**最简展示**：在合适的 Vue 组件中监听 `bimcanvas:semantic-plan-updated` 事件，展示版本号 + 内容文本。具体 UI 设计不在 Phase 1A 范围内，可以先用 console.log 或简单的文本面板。

---

## 交付物 2：generate-workflow/SKILL.md 重写

**文件**：`BIMCanvas.Agent/templates/skills/generate-workflow/SKILL.md`

### 2.1 删除的内容

以下段落需要删除（释放注意力预算）：

**(A) "规划 = 出声思考" 段落（约第 185 行附近）**

删除整段：
```markdown
> **规划 = 出声思考。** 你的推理过程直接输出到对话——用户想看到你如何决策，不只是最终结论。在 thinking 中只做初步判断（几秒钟），然后在对话中展开分析。
```

WHY（删除理由）：日志证明此指令对 thinking block 无约束力。Agent 在 thinking 中完成所有分析后才输出结论。工具调用边界将替代此行为引导。

**(B) "精度边界" 表格（约第 187-197 行）**

删除整个"精度边界"小节（包含表格和 WHY 段落）：
```markdown
### 精度边界

规划阶段全程只做**空间预演级别的粗估**，不计算精确坐标：

| 规划阶段允许 | 规划阶段不允许 |
|------------|-------------|
| ... | ... |

WHY：在 thinking 中算一次坐标→发现需要验证碰撞→...
```

WHY（删除理由）：日志证明 Agent 不遵守此规则。删除后由结构边界（每个子阶段一个 turn）自然限制分析范围。

**(C) 语义方案冗长文字说明**

精简"语义方案"段开头的描述性段落。保留演进规则表格（v0.1/v0.2/v0.3 定义）和确定性标记说明。删除以下冗余文字：

```markdown
语义方案是**持续演进的决策文本**——用自然语言描述家具与墙面的语义映射...
```

和：

```markdown
**【必须】每完成一个子阶段（2.1/2.2/2.3），立即将当前版本输出到对话中**——这是独立的对话消息...

WHY：在 thinking 中保留未输出的决策 = 未锚定 = ...
```

这些将被工具调用规则替代（见下方新增内容）。

### 2.2 新增的内容

**(A) 语义方案提交规则（替代原来的"输出到对话"指令）**

在语义方案段落中，替代原有的输出指令：

```markdown
### 语义方案提交

**【必须】每完成一个子阶段（2.1/2.2/2.3），调用 `save_semantic_plan` 提交当前版本。**

WHY：工具调用是阶段完成的唯一标志。未调用 = 未完成，不得继续下一子阶段。提交行为将你的决策从内部草稿变为外部锚点——已提交的决策是后续推进的基础，不再重新推导。
```

**(B) 各子阶段结尾的提交指令**

在 Stage 2.1 结尾（"产出语义方案 v0.1"之后）：
```markdown
**【必须】**调用 `save_semantic_plan(zoneId, "v0.1", 方案文本)` 提交 v0.1。
```

在 Stage 2.2 / Step 3 结尾（"产出语义方案 v0.2"之后）：
```markdown
**【必须】**调用 `save_semantic_plan(zoneId, "v0.2", 方案文本)` 提交 v0.2。
```

在 Stage 2.3 结尾（"v0.3 完成后"之后）：
```markdown
**【必须】**调用 `save_semantic_plan(zoneId, "v0.3", 方案文本)` 提交 v0.3。
```

**(C) 在两个示例中更新提交方式**

将示例中的"↓ Stage 2.1 完成后，立即输出以下内容到对话 ↓"替换为：

```markdown
↓ Stage 2.1 完成后，调用 save_semantic_plan(zoneId, "v0.1", 以下内容) ↓
```

对 v0.2、v0.3 的示例同理更新。

### 2.3 修改的内容

**(A) Stage 2 开头的引导语**

将：
```markdown
> **规划 = 出声思考。** ...
```

替换为：
```markdown
> **规划 = 分阶段提交。** 每个子阶段完成后调用 `save_semantic_plan` 提交决策。
> 提交后的决策是后续的锚点，不再重新推导。
```

**(B) Stage 3 "尝试 ↔ 修改" 开头的引导语（约第 279 行附近）**

将现有的引导语：
```markdown
> **核心姿态：允许试错。** 像人类设计师一样工作——先放下去看效果，出错了调整就好。validate_layout 是安全网，它比心算更快、更准。不需要在脑中完成所有验证后才敢动手。
>
> WHY："一次完美"的心理预期是 thinking block 膨胀的隐性推手。快速迭代（写入→验证→修正）的成本远低于在 thinking 中预防所有问题的成本。
```

替换为（更强的"快速草稿"姿态）：
```markdown
> 语义方案 v0.3 已确定了每件家具的墙面归属和大致位置。
> Stage 3 的任务是把语义描述转化为坐标——写入 modules.json，让 validate_layout 校对。
>
> **【姿态】快速落地。** validate_layout 比心算更快更准——写入一个合理的草稿，让工具告诉你哪里需要修正。首版写入后被 validate_layout 报错是正常流程，不是失败。
>
> WHY：快速迭代（写入→验证→修正）的总成本远低于在写入前预防所有问题的成本。
```

**(C) Stage 3 "手动检查清单" 精简**

保留检查清单的表格结构，但在表格前加一句：
```markdown
Agent 在写入前**仅**检查以下 validate_layout 无法检测的语义问题——其余几何问题全部交给 validate_layout：
```

（这一句已存在于现有文本中，确认保留即可。）

### 2.4 保留不动的内容

以下内容已被日志证明有效，不做任何修改：

- 两个完整示例（L 形主卧 + 窄长卫生间）——核心锚定物
- 确定性标记（✓/~）说明
- "先家具→再分区"单向约束
- validate_layout 必调规则
- 修正优先级（平移→旋转→缩小→替换→移除）
- Layer 1 验证流程
- Stage 4 优化审查流程
- Stage 5 汇报格式
- "机制速查"尾部段落

---

## 交付物 3：generate-zoning/SKILL.md 边界收紧

**文件**：`BIMCanvas.Agent/templates/skills/generate-zoning/SKILL.md`

### 3.1 修改文件开头的协调说明（约第 10-16 行）

将现有的：
```markdown
**与 generate-workflow 的协调**：本 Skill 在 Stage 2.2（分区设计）被加载。此时 generate-workflow 已完成：
- 语义方案 v0.1（空间骨架：动线/纵深/采光/初步意图）
- 基于物理约束确定的主要家具最优墙面位置

本 Skill 基于这些已有分析做分区评估——不重新推导空间信息。
```

强化为：
```markdown
**与 generate-workflow 的协调**：本 Skill 在 Stage 2.2（分区设计）被加载。此时 generate-workflow 已通过 `save_semantic_plan(v0.1)` 提交了：
- 语义方案 v0.1（空间骨架：动线/纵深/采光/初步意图）
- 基于物理约束确定的主要家具最优墙面位置

**输入**：v0.1 已提交的空间骨架 + 主要家具墙面结论
**输出**：分区兼容性结论 + 功能定义（写入 v0.2）
**不做**：不重新分析空间几何、不重新推导墙面语义、不重做主家具决策

本 Skill 基于这些已有分析做分区评估——不重新推导空间信息。
```

### 3.2 补"不分割"快速路径示例

在现有示例 2（客餐一体）之后，新增一个"快速判断不分割"的短示例：

```markdown
### 示例 3：矩形主卧（快速判断 — 不分割）

场景：矩形 3600×4200mm，tags=[sleep, wardrobeStorage]，北墙入口，南墙窗

空间阅读：矩形 + 单功能标签组 + 无空间冲突信号 → 快速判断

步骤 1 — 快速判断：矩形空间，单一功能标签组，无异形、无多功能冲突
→ 结论：无需物理分割

步骤 2 — 功能定义：整体空间 = 睡眠+收纳区，功能单一，不需要子空间标签

→ 不产出 subZones，直接继续 generate-workflow
```

WHY（新增理由）：日志显示 Agent 在"不分割"场景中仍展开了过多分析。短路径示例锚定"快速完成审视后得出结论"的节奏。

---

## 交付物 4：BIMCANVAS.md 最小同步修改

**文件**：`BIMCanvas.Agent/templates/BIMCANVAS.md`

### 4.1 身份段落补充（约第 5-12 行）

在现有"思维方式"描述之后，追加一句：

```markdown
每个规划子阶段完成后，通过 `save_semantic_plan` 提交决策——已提交的决策是后续推进的锚点。
```

### 4.2 工具优先级更新（约第 92 行）

将现有的：
```markdown
**工具优先级**：①遵守 Skill > 其他 ②**【必须】**validate_layout 每次 Write 后必调 ③专用 MCP > Bash ④无依赖可并行
```

修改为：
```markdown
**工具优先级**：①遵守 Skill > 其他 ②**【必须】**save_semantic_plan 每个规划子阶段完成后必调 ③**【必须】**validate_layout 每次 Write 后必调 ④专用 MCP > Bash ⑤无依赖可并行
```

### 4.3 硬约束补充（约第 89 行）

在现有硬约束列表中追加：

```markdown
不跳过 save_semantic_plan 提交（规划子阶段未提交 = 未完成）
```

### 4.4 不做的修改

- 不重写身份段落
- 不重写多房间派发逻辑
- 不重写对话能力规则
- 不重写安全机制的其他部分

---

## 验证步骤

### 步骤 1：编译验证

```bash
# Server 端编译
dotnet build BIMCanvas.Server --no-restore

# 确认新 Controller 被编译
```

### 步骤 2：工具注册验证

启动 Agent 服务后，检查日志输出：
```
[MCP] Canvas MCP 已注册，工具: ['mcp__canvas__request_background_screenshot', 'mcp__canvas__validate_layout', 'mcp__canvas__get_zone_boundaries', 'mcp__canvas__save_semantic_plan']
```

确认 `save_semantic_plan` 出现在工具列表中。

### 步骤 3：功能冒烟测试

向 Agent 发送"为主卧设计布局"，在 Server 日志中确认：
1. Agent 在 Stage 2.1 完成后调用了 `save_semantic_plan(v0.1)`
2. Agent 在 Stage 2.2 完成后调用了 `save_semantic_plan(v0.2)`
3. Agent 在 Stage 2.3 完成后调用了 `save_semantic_plan(v0.3)`
4. 每次调用之间是独立的 turn（[THINK] 块被拆分）

### 步骤 4：性能对比

使用同一场景（金凤127 主卧 rz_3）测试，对比 Server 日志：

| 指标 | 改动前基线 | Phase 1A 结果 | 判定 |
|------|-----------|--------------|------|
| Stage 2 最大单次 thinking | 8:43 | ? | <= 4 min = PASS |
| 首次 Write 时间 | 第 24 分钟 | ? | < 15 min = PASS |
| validate_layout 最终通过 | 首次通过 | ? | 最多 2 轮 = PASS |
| 设计质量 | 4 目标全达成 | ? | 无明显降级 = PASS |
| save_semantic_plan 调用次数 | 0 | ? | = 3 = PASS |

### 步骤 5：存储验证

测试完成后检查项目目录：
```
schemes/rz_3/semantic_plan.json
```

确认文件存在且包含三个版本（v0.1、v0.2、v0.3），每个版本有 zoneId、version、content、timestamp 四个字段。

---

## 文件改动清单

| 文件 | 改动类型 | 改动量 |
|------|---------|-------|
| `BIMCanvas.Agent/src/mcp/canvas.py` | 修改（新增工具） | +~60 行 |
| `BIMCanvas.Server/Controllers/SemanticPlanController.cs` | **新建** | ~80 行 |
| `BIMCanvas.Web/src/services/SignalRService.ts` | 修改（新增监听） | +~5 行 |
| `BIMCanvas.Agent/templates/skills/generate-workflow/SKILL.md` | 重写（核心） | 删除~30行 + 新增~20行 + 修改~15行 |
| `BIMCanvas.Agent/templates/skills/generate-zoning/SKILL.md` | 修改（边界收紧） | 修改~10行 + 新增示例~15行 |
| `BIMCanvas.Agent/templates/BIMCANVAS.md` | 修改（最小同步） | 修改~5行 |

---

## Phase 1A 之后

Phase 1A 验证通过后，后续路线图：

- **Phase 1B**：`config.json` 中 `defaultEffort` 从 "low" 改为 "medium"，跑对照日志
- **Phase 2**：`save_semantic_plan` 返回值增加 `next_stage_hints`；封套思维/Chunking 在 bedroom.md 落地；C 类计算指令迁移到验证阶段
- **Phase 3**：根据 Phase 2 日志定向修正房间策略
