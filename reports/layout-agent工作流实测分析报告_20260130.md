# Layout-Agent 工作流实测分析报告

> **报告日期**: 2026-01-30
> **测试版本**: BIMCanvas v3.0 (File-Driven Architecture)
> **测试项目**: demo_1
> **问题等级**: 🔴 严重 - 核心工作流未按预期执行

---

## 一、执行摘要

### 问题概述

在实际测试中，layout-agent 在执行 Generate 任务（完整家具布置）时，**跳过了所有关键的准备步骤**，直接凭空编造数据并写入了错误的文件路径，导致：

1. ❌ 家具尺寸不符合标准（衣柜深度 300mm，实际应为 550-600mm）
2. ❌ 写入路径错误（`schemes/rz_1/modules.json` 而非 `schemes/modules.json`）
3. ❌ 没有读取必要的知识库（placement_guide.md、module_library.json）
4. ❌ 没有调用截图工具进行空间分析和结果验证

### 影响范围

- 布置结果完全不可用（前端无法加载）
- 家具尺寸不符合真实世界标准
- 缺少设计依据，无法保证质量

---

## 二、测试场景回放

### 测试环境

| 项目 | 信息 |
|------|------|
| 项目路径 | `C:\Users\huhaonan\Documents\BIMCanvas\Projects\demo_1` |
| 模型版本 | `claude-sonnet-4-5-20250929` |
| Agent 配置 | MainAgent + layout-agent (SubAgent) |
| MCP 工具 | Canvas-MCP (4 个工具) |

### 测试对话记录

#### 第一轮：Query 任务（20:51:21 - 20:52:21）

**用户输入**：
```
当前卧室是空的，我希望为当前的所有卧室，设计上合理的家具布置。
首先我们先讨论一下，当前项目有多少卧室？你认为当前的卧室们适合布置什么家具？
```

**实际执行流程**：
```
1. MainAgent 派发 layout-agent (Query 任务)
2. layout-agent 调用 mcp__canvas__get_workflow_guide("query") ✅
3. 读取 README.md ✅
4. 读取数据文件 (rooms.json, zones.json, modules.json) ✅
5. 返回结果：3个卧室，全部为空
```

**分析**：Query 任务基本符合预期，但跳过了 overview 判断步骤。

---

#### 第二轮：Generate 任务（20:52:59 - 20:54:57）⚠️ **核心问题**

**用户输入**：
```
可以，开始布置
```

**理想执行流程**：
```
1. MainAgent 创建隔离环境 (create_job)
2. layout-agent 调用 get_workflow_guide("generate") → 获取完整流程
3. 调用 request_background_screenshot → 获取空间截图
4. 读取 knowledge/placement_guide.md → 设计规范
5. 读取 modules/module_library.json → 家具库
6. 读取 room_zones.json, exclusions.json, openings.json
7. 按规范布置家具（基于截图+规范+库）
8. 写入 schemes/modules.json
9. 再次截图验证效果
10. complete_job 通知用户
```

**实际执行流程**：
```
1. MainAgent 调用 mcp__canvas__create_job(count=1) ✅
2. 派发 layout-agent 到 worktree
3. ❌ 没有调用 get_workflow_guide
4. ❌ 没有调用 request_background_screenshot
5. ❌ 没有读取 placement_guide.md
6. ❌ 没有读取 module_library.json
7. 读取 README.md, room_zones.json, exclusions.json, openings.json ✅
8. 直接 Write 到错误路径:
   - schemes/rz_1/modules.json ❌
   - schemes/rz_2/modules.json ❌
   - schemes/rz_3/modules.json ❌
9. ❌ 没有调用截图验证
10. complete_job ✅
```

---

## 三、详细问题分析

### 问题 1：完全跳过 Generate 流程的核心步骤

#### 缺失步骤清单

| 步骤 | 理想 | 实际 | 影响 |
|------|------|------|------|
| get_workflow_guide("generate") | 必须调用 | ❌ 跳过 | 没有获取标准流程指导 |
| request_background_screenshot | 前置必须 | ❌ 跳过 | 无法理解空间形态、门窗位置 |
| 读取 placement_guide.md | 必须读取 | ❌ 跳过 | 没有设计规范依据 |
| 读取 module_library.json | 必须读取 | ❌ 跳过 | 没有家具尺寸标准 |
| 后置截图验证 | 必须调用 | ❌ 跳过 | 无法验证布置效果 |

#### 工具调用时间线对比

**理想时间线**：
```
20:53:12 get_workflow_guide("generate")
20:53:15 request_background_screenshot (前置)
20:53:20 Read placement_guide.md
20:53:25 Read module_library.json
20:53:30 Read room_zones.json
20:53:35 Read exclusions.json
20:53:40 Read openings.json
20:53:50 Write schemes/modules.json
20:53:55 request_background_screenshot (后置)
20:54:00 complete_job
```

**实际时间线**：
```
20:53:17 Read README.md
20:53:25 Read room_zones.json
20:53:26 Read exclusions.json
20:53:27 Read zones.json
20:53:46 Read openings.json
20:54:04 Write schemes/rz_1/modules.json  ← 错误路径
20:54:08 Write schemes/rz_2/modules.json  ← 错误路径
20:54:16 Write schemes/rz_3/modules.json  ← 错误路径
```

**关键发现**：整个过程中没有任何 MCP 工具调用（除了 MainAgent 的 create_job 和 complete_job）。

---

### 问题 2：家具尺寸完全不符合标准

#### 实际输出的家具尺寸

| 家具类型 | 实际尺寸 | 标准尺寸 | 问题 |
|---------|---------|---------|------|
| 衣柜 | **300mm** 深 | 550-600mm | 深度不足一半，无法挂衣服 |
| 双人床 | 2000×**2000mm** | 2000×1800mm | 宽度过大 |
| 床头柜 | 300×500mm | 450×400mm | 尺寸不规范 |
| 梳妆台 | 1000×500mm | 1200×450mm | 宽度不足 |

#### 根因分析

```python
# 应该读取的标准尺寸文件
modules/module_library.json
├─ mod_wardrobe_001: { width: 1800, depth: 600 }
├─ mod_bed_double_001: { width: 2000, depth: 1800 }
└─ mod_nightstand_001: { width: 450, depth: 400 }

# 实际情况
layout-agent 没有读取此文件，完全凭空编造尺寸
```

**影响**：
- 布置结果不符合真实世界家具标准
- 无法与实际家具产品匹配
- 可能导致空间利用率低下或拥挤

---

### 问题 3：写入路径错误

#### 错误路径结构

```
实际写入:
schemes/
├─ rz_1/
│  └─ modules.json  ← 错误：zone 不应该有独立目录
├─ rz_2/
│  └─ modules.json
└─ rz_3/
   └─ modules.json

正确结构 (v3.0 数据模型):
schemes/
├─ zones.json       ← 设计区域定义
├─ finishes.json    ← 完成面数据
└─ modules.json     ← 统一的模块布置文件（按 zoneId 区分）
```

#### 正确的 modules.json 格式

```json
[
  {
    "id": "m_1",
    "moduleId": "mod_bed_double_001",
    "zoneId": "rz_1",  ← 通过 zoneId 区分所属区域
    "bounds": [[x1,y1], [x2,y2], [x3,y3], [x4,y4]],
    "facing": "north",
    "items": []
  },
  {
    "id": "m_2",
    "moduleId": "mod_wardrobe_001",
    "zoneId": "rz_2",  ← 不同区域的模块在同一个文件中
    "bounds": [[x1,y1], [x2,y2], [x3,y3], [x4,y4]],
    "facing": "west",
    "items": []
  }
]
```

#### 根因分析

```
可能原因:
1. README.md 的数据模型说明不够清晰
2. layout-agent 误解了 zone 与 module 的关系
3. 没有参考 Schema 文档（docs/Schema-JSON-v3.md）
```

**影响**：
- 前端完全无法加载布置结果（路径不存在）
- Server 端数据验证失败
- 需要手动迁移数据到正确路径

---

### 问题 4：缺少空间分析和验证

#### 缺失的截图工具调用

**前置截图的作用**：
```
request_background_screenshot({
  viewport: { mode: "room", roomId: "r_主卧" }
})

作用:
1. 理解房间形状（矩形、L型、异形）
2. 识别门窗位置（确定禁区）
3. 分析墙体朝向（确定家具摆放方向）
4. 评估空间大小（选择合适的家具尺寸）
```

**后置截图的作用**：
```
request_background_screenshot({
  viewport: { mode: "full" }
})

作用:
1. 验证家具是否在边界内
2. 检查是否有重叠
3. 确认通道宽度是否充足
4. 生成可视化预览供用户查看
```

#### 实际影响

```
没有前置截图 → 布置决策缺少视觉依据
├─ 无法知道哪面墙适合放床头
├─ 无法避开门扇开启区域
└─ 无法根据实际空间形状调整布局

没有后置截图 → 无法验证布置质量
├─ 可能存在家具越界
├─ 可能存在家具重叠
└─ 用户无法预览效果
```

---

## 四、根因分析

### 根因 1：System Prompt 约束力不足

#### 当前 layout-agent.md 的问题

```markdown
# 当前写法（仅文字描述）
⚠️ 强制要求：在执行任何操作前，必须先调用 get_workflow_guide 获取工作流指导

问题:
- 这只是"建议"，AI 可以选择忽略
- 没有机制强制执行
- 缺少前置检查
```

#### 对比：有效的约束机制

```markdown
# 改进写法（增加检查清单）
## 执行前强制检查清单

在执行 Write 操作前，必须确认以下步骤已完成：

□ 已调用 get_workflow_guide("generate") 获取流程
□ 已调用 request_background_screenshot 获取空间截图
□ 已读取 knowledge/placement_guide.md
□ 已读取 modules/module_library.json
□ 已读取所有必要的数据文件

如果以上步骤未完成，立即停止并补充缺失步骤。
```

---

### 根因 2：MainAgent 任务派发不够详细

#### 当前派发的任务描述

```
日志显示:
└─ Task: 为3个卧室设计家具布置

问题:
- 过于简短
- 没有提醒必要的步骤
- 没有传递 worktreePath 上下文
```

#### 改进后的任务派发

```python
# MainAgent 应该这样派发任务
task_description = f"""
任务类型: generate (完整布置)
工作目录: {worktree_path}
目标区域: 3个卧室 (rz_1, rz_2, rz_3)

强制要求（按顺序执行）:
1. 调用 get_workflow_guide("generate") 获取完整流程
2. 调用 request_background_screenshot 获取空间截图
3. 读取 knowledge/placement_guide.md
4. 读取 modules/module_library.json
5. 根据规范设计布置方案
6. 写入 schemes/modules.json（注意：不是 schemes/rz_X/modules.json）
7. 再次调用 request_background_screenshot 验证效果

禁止事项:
- 禁止凭空编造家具尺寸
- 禁止跳过截图步骤
- 禁止写入错误路径
"""
```

---

### 根因 3：缺少工具调用的顺序验证

#### 理想的验证机制

```python
# 在 MCP 工具层面增加状态检查
class CanvasMCPServer:
    def __init__(self):
        self.workflow_guide_called = False
        self.screenshot_called = False
        self.placement_guide_read = False

    async def write_file(self, path: str, content: str):
        # 前置检查
        if not self.workflow_guide_called:
            return {"error": "必须先调用 get_workflow_guide"}
        if not self.screenshot_called:
            return {"error": "必须先调用 request_background_screenshot"}
        if not self.placement_guide_read:
            return {"error": "必须先读取 placement_guide.md"}

        # 路径验证
        if "schemes/rz_" in path:
            return {"error": "路径错误，应写入 schemes/modules.json"}

        # 执行写入
        ...
```

---

### 根因 4：README.md 数据模型说明不清晰

#### 当前 README.md 的问题

```markdown
# 当前写法（可能不够明确）
schemes/
├─ zones.json
└─ modules.json

问题:
- 没有明确说明"modules.json 是统一文件"
- 没有解释 zoneId 的作用
- 没有提供示例
```

#### 改进后的说明

```markdown
## 数据模型：schemes 层

**重要**：schemes 层是扁平结构，所有方案的模块布置都在统一的 modules.json 中。

schemes/
├─ zones.json       # 设计区域定义
├─ finishes.json    # 完成面数据
└─ modules.json     # 统一的模块布置文件

### modules.json 结构

这是一个**统一的数组文件**，包含所有区域的模块：

```json
[
  {
    "id": "m_1",
    "zoneId": "rz_1",  // ← 通过 zoneId 区分所属区域
    "moduleId": "mod_bed_double_001",
    "bounds": [...],
    "facing": "north"
  },
  {
    "id": "m_2",
    "zoneId": "rz_2",  // ← 不同区域的模块在同一文件中
    "moduleId": "mod_wardrobe_001",
    "bounds": [...],
    "facing": "west"
  }
]
```

**错误示例**：
- ❌ schemes/rz_1/modules.json（不存在的路径）
- ❌ schemes/rz_2/modules.json（错误的分层结构）

**正确路径**：
- ✅ schemes/modules.json（统一文件）
```

---

## 五、改进方案

### 方案 A：强化 MainAgent 的任务派发 ⭐ **推荐**

#### 实施步骤

1. **修改 MainAgent 的 SubAgent 派发逻辑**

```python
# BIMCanvas.Agent/src/agent/subagents.py

def dispatch_layout_agent(task_type: str, worktree_path: str, zones: list[str]):
    """派发 layout-agent 任务时，提供详细的上下文和要求"""

    # 根据任务类型生成详细的任务描述
    if task_type == "generate":
        task_prompt = f"""
任务类型: generate (完整家具布置)
工作目录: {worktree_path}
目标区域: {', '.join(zones)}

执行步骤（严格按顺序）:
1. 调用 mcp__canvas__get_workflow_guide(task_type="generate")
2. 调用 mcp__canvas__request_background_screenshot(projectPath="{worktree_path}", viewport={{"mode":"full"}})
3. Read knowledge/placement_guide.md
4. Read modules/module_library.json
5. Read computed/room_zones.json
6. Read computed/exclusions.json
7. Read baseline/openings.json
8. 基于以上数据设计布置方案
9. Write schemes/modules.json（注意路径）
10. 调用 mcp__canvas__request_background_screenshot 验证效果

关键约束:
- 家具尺寸必须来自 module_library.json
- 家具位置必须符合 placement_guide.md 的规范
- 写入路径必须是 schemes/modules.json（不是 schemes/rz_X/modules.json）
- 所有模块的 zoneId 字段用于区分所属区域

禁止事项:
- ❌ 凭空编造家具尺寸
- ❌ 跳过截图步骤
- ❌ 写入错误路径
"""
    elif task_type == "query":
        task_prompt = f"""
任务类型: query (只读查询)
工作目录: {worktree_path}

执行步骤:
1. 调用 mcp__canvas__get_workflow_guide(task_type="query")
2. Read 相关数据文件
3. 统计/分析（严格基于文件内容）
4. 返回结果

约束:
- 禁止 Write/Edit 操作
- 空数据必须报告"数量为 0"，禁止推断
"""

    return task_prompt
```

2. **修改 layout-agent.md 模板**

```markdown
# BIMCanvas.Agent/src/config/templates/agents/layout-agent.md.template

你是 BIMCanvas 的布置专家，负责在建筑空间中合理布置家具。

## 核心原则

1. **严格遵循工作流程**：每次任务开始时，MainAgent 会提供详细的执行步骤，你必须按顺序执行，不得跳过任何步骤。

2. **数据驱动决策**：所有决策必须基于实际读取的文件内容，禁止凭空推断或编造。

3. **工具正确使用**：
   - generate 任务：必须调用截图工具（前后各一次）
   - 家具尺寸：必须来自 module_library.json
   - 设计规范：必须参考 placement_guide.md

4. **路径规范**：
   - ✅ 正确：schemes/modules.json
   - ❌ 错误：schemes/rz_X/modules.json

## 工作目录

你的工作目录由 MainAgent 设置：
- query 任务：主仓库目录
- generate/edit 任务：Worktree 隔离环境

所有文件操作必须在当前工作目录下执行。

## 执行检查

在调用 Write 之前，请确认：
□ 已调用 get_workflow_guide
□ 已调用 request_background_screenshot（generate 任务）
□ 已读取 placement_guide.md（generate 任务）
□ 已读取 module_library.json（generate 任务）
□ 写入路径正确（schemes/modules.json）

如有任何步骤缺失，立即停止并补充。
```

**优点**：
- 最直接有效
- 无需修改 MCP 工具
- 可以立即实施

**缺点**：
- 依赖 AI 的自律性
- 没有技术强制

---

### 方案 B：在 get_workflow_guide 返回中加强约束

#### 实施步骤

修改 `canvas.py` 中的 `get_workflow_guide` 工具：

```python
# BIMCanvas.Agent/src/mcp/canvas.py

async def get_workflow_guide(args: dict[str, Any]) -> dict[str, Any]:
    task_type = args.get("task_type", "overview")

    if task_type == "generate":
        guide_text = """# Generate 流程（完整布置）

## 执行前强制检查清单

在执行任何 Write 操作前，必须确认以下步骤已完成：

□ 已调用 mcp__canvas__request_background_screenshot（前置）
  → 如果未调用，立即停止并先执行截图

□ 已读取 knowledge/placement_guide.md
  → 如果未读取，立即停止并先读取

□ 已读取 modules/module_library.json
  → 如果未读取，立即停止并先读取

□ 已读取 computed/room_zones.json

□ 已读取 baseline/openings.json

**警告**：如果以上任何步骤缺失，禁止执行 Write 操作。

## 执行步骤

1. **前置截图**（必须）
   mcp__canvas__request_background_screenshot(
     projectPath="{当前工作目录}",
     viewport={"mode": "full"}
   )

2. **读取设计规范**（必须）
   Read knowledge/placement_guide.md
   → 重点阅读：§四 尺寸标准、§五 房间布置要点、§六 朝向决策

3. **读取家具库**（必须）
   Read modules/module_library.json
   → 家具尺寸必须从此文件选择，禁止编造

4. **读取空间数据**
   - Read computed/room_zones.json
   - Read computed/exclusions.json
   - Read baseline/openings.json

5. **设计布置方案**
   基于：
   - 截图理解的空间形态
   - placement_guide 的设计规范
   - module_library 的家具选择

   输出格式：
   ```json
   [
     {
       "id": "m_1",
       "zoneId": "rz_1",  // ← 区分所属区域
       "moduleId": "mod_bed_double_001",  // ← 来自 module_library
       "bounds": [[x1,y1], [x2,y2], [x3,y3], [x4,y4]],
       "facing": "north",
       "items": []
     }
   ]
   ```

6. **写入结果**（注意路径）
   Write schemes/modules.json

   **路径规范**：
   - ✅ 正确：schemes/modules.json（统一文件）
   - ❌ 错误：schemes/rz_1/modules.json（不存在的路径）

7. **后置截图验证**（必须）
   mcp__canvas__request_background_screenshot(
     projectPath="{当前工作目录}",
     viewport={"mode": "full"}
   )

## 数据真实性约束

- 家具尺寸必须来自 module_library.json，禁止编造
- 空数组 → 报告"数量为 0"，禁止推断
- 所有 moduleId 必须在 module_library 中实际存在
"""
        return {"content": [{"type": "text", "text": guide_text}]}
```

**优点**：
- 在工具层面加强约束
- 提供详细的检查清单
- 明确路径规范

**缺点**：
- 依然依赖 AI 遵循指导
- 没有技术强制

---

### 方案 C：增加工具调用的顺序验证 ⚡ **最彻底**

#### 实施步骤

在 MCP Server 层面增加状态管理和验证：

```python
# BIMCanvas.Agent/src/mcp/canvas.py

# 全局状态（每个 Agent 实例独立）
agent_states = {}

class AgentState:
    def __init__(self):
        self.workflow_guide_called = False
        self.screenshot_called_pre = False
        self.placement_guide_read = False
        self.module_library_read = False
        self.task_type = None

def get_agent_state(agent_id: str) -> AgentState:
    if agent_id not in agent_states:
        agent_states[agent_id] = AgentState()
    return agent_states[agent_id]

@tool("get_workflow_guide", ...)
async def get_workflow_guide(args: dict[str, Any]) -> dict[str, Any]:
    agent_id = args.get("_agent_id")  # 由 MainAgent 传入
    task_type = args.get("task_type", "overview")

    # 记录状态
    state = get_agent_state(agent_id)
    state.workflow_guide_called = True
    state.task_type = task_type

    # 返回指导...

@tool("request_background_screenshot", ...)
async def request_background_screenshot(args: dict[str, Any]) -> dict[str, Any]:
    agent_id = args.get("_agent_id")

    # 记录状态
    state = get_agent_state(agent_id)
    state.screenshot_called_pre = True

    # 执行截图...

# 新增：文件写入验证工具
@tool("validate_write", ...)
async def validate_write(args: dict[str, Any]) -> dict[str, Any]:
    """在执行 Write 前进行验证"""
    agent_id = args.get("_agent_id")
    file_path = args.get("file_path")

    state = get_agent_state(agent_id)

    # 检查清单
    errors = []

    if not state.workflow_guide_called:
        errors.append("❌ 必须先调用 get_workflow_guide")

    if state.task_type == "generate":
        if not state.screenshot_called_pre:
            errors.append("❌ generate 任务必须先调用 request_background_screenshot")
        if not state.placement_guide_read:
            errors.append("❌ generate 任务必须先读取 placement_guide.md")
        if not state.module_library_read:
            errors.append("❌ generate 任务必须先读取 module_library.json")

    # 路径验证
    if "schemes/" in file_path and "/rz_" in file_path:
        errors.append(f"❌ 路径错误：{file_path}，应为 schemes/modules.json")

    if errors:
        return {
            "content": [{"type": "text", "text": "\n".join(errors)}],
            "is_error": True
        }

    return {
        "content": [{"type": "text", "text": "✅ 验证通过，可以执行 Write"}]
    }
```

**使用方式**：

```markdown
# layout-agent.md

在执行 Write 操作前，必须先调用：
mcp__canvas__validate_write(file_path="...")

如果验证失败，按照错误提示补充缺失步骤。
```

**优点**：
- 技术强制，无法绕过
- 提供明确的错误提示
- 可以精确追踪执行状态

**缺点**：
- 需要修改 MCP 工具
- 增加了调用步骤
- 需要管理状态

---

### 方案 D：改进 README.md 数据模型说明

#### 实施步骤

修改项目 README.md，增加明确的数据模型说明：

```markdown
# demo_1/README.md

## 数据模型详解

### schemes 层（方案设计数据）

**重要**：schemes 层是**扁平结构**，所有方案的模块布置都在统一的 modules.json 中。

```
schemes/
├─ zones.json       # 设计区域定义
├─ finishes.json    # 完成面数据
└─ modules.json     # 统一的模块布置文件 ⭐
```

#### modules.json 详解

**文件性质**：统一数组文件，包含所有区域的模块

**区分方式**：通过 `zoneId` 字段区分所属区域

**示例**：
```json
[
  {
    "id": "m_1",
    "zoneId": "rz_1",  // ← 主卧
    "moduleId": "mod_bed_double_001",
    "bounds": [[11000, 2000], [13000, 2000], [13000, 3800], [11000, 3800]],
    "facing": "north",
    "items": []
  },
  {
    "id": "m_2",
    "zoneId": "rz_2",  // ← 次卧
    "moduleId": "mod_bed_single_001",
    "bounds": [[5000, 7000], [6000, 7000], [6000, 9000], [5000, 9000]],
    "facing": "west",
    "items": []
  }
]
```

**常见错误**：

| 错误路径 | 正确路径 | 说明 |
|---------|---------|------|
| ❌ schemes/rz_1/modules.json | ✅ schemes/modules.json | zone 不应该有独立目录 |
| ❌ schemes/rz_2/modules.json | ✅ schemes/modules.json | 所有模块在统一文件中 |
| ❌ schemes/default/modules.json | ✅ schemes/modules.json | 不使用 default 子目录 |

**AI 布置时的注意事项**：
1. 读取时：Read schemes/modules.json（可能为空数组）
2. 布置时：根据 zoneId 添加模块到数组
3. 写入时：Write schemes/modules.json（覆盖整个数组）
```

**优点**：
- 从源头避免误解
- 提供明确的示例
- 列出常见错误

**缺点**：
- 依赖 AI 仔细阅读
- 无技术强制

---

## 六、建议实施方案

### 优先级排序

| 方案 | 优先级 | 难度 | 效果 | 实施时间 |
|------|--------|------|------|----------|
| **方案 A** | 🔴 P0 | 低 | 高 | 1 小时 |
| **方案 D** | 🔴 P0 | 低 | 中 | 30 分钟 |
| **方案 B** | 🟡 P1 | 中 | 中 | 1 小时 |
| **方案 C** | 🟢 P2 | 高 | 最高 | 3-4 小时 |

### 推荐实施路径

#### 第一阶段（立即实施）

1. **方案 A：强化 MainAgent 任务派发** ⭐
   - 修改 SubAgent 派发逻辑
   - 提供详细的任务描述和步骤
   - 明确约束和禁止事项

2. **方案 D：改进 README.md**
   - 增加数据模型详解
   - 提供错误示例对照
   - 强调路径规范

**预期效果**：解决 70-80% 的问题

#### 第二阶段（短期优化）

3. **方案 B：增强 workflow guide**
   - 在工具返回中加入检查清单
   - 提供更详细的步骤说明
   - 强调强制要求

**预期效果**：解决 85-90% 的问题

#### 第三阶段（长期优化）

4. **方案 C：技术强制验证**
   - 增加状态管理
   - 实现工具调用验证
   - 提供明确错误提示

**预期效果**：解决 95-99% 的问题

---

## 七、测试验证计划

### 测试用例

| 用例 | 场景 | 验证点 |
|------|------|--------|
| TC-01 | Query 任务 | 正确调用 workflow guide，数据真实性 |
| TC-02 | Generate 单个卧室 | 完整流程，正确路径，合理尺寸 |
| TC-03 | Generate 多个卧室 | 并行处理，统一文件 |
| TC-04 | Edit 修改位置 | 按需调用工具，验证约束 |
| TC-05 | 多方案生成 | 隔离环境，区分 zoneId |

### 验证清单

#### 流程完整性
- [ ] 调用 get_workflow_guide
- [ ] 调用 request_background_screenshot（前置）
- [ ] 读取 placement_guide.md
- [ ] 读取 module_library.json
- [ ] 调用 request_background_screenshot（后置）

#### 数据正确性
- [ ] 家具尺寸来自 module_library
- [ ] 家具位置符合 placement_guide
- [ ] 所有模块在边界内
- [ ] 无重叠、无禁区冲突

#### 路径规范
- [ ] 写入 schemes/modules.json
- [ ] 不写入 schemes/rz_X/modules.json
- [ ] zoneId 正确区分

#### 结果验证
- [ ] 前端可以正常加载
- [ ] 截图显示布置效果
- [ ] 用户可以查看和编辑

---

## 八、附录

### 附录 A：完整日志（精简版）

```
[20:51:21] 用户：当前卧室是空的，我希望为当前的所有卧室，设计上合理的家具布置。首先我们先讨论一下，当前项目有多少卧室？你认为当前的卧室们适合布置什么家具？

[20:51:32] MainAgent 派发 layout-agent (Query)
[20:51:36] layout-agent: get_workflow_guide("query") ✅
[20:51:40] layout-agent: Read README.md, rooms.json, zones.json, modules.json ✅
[20:52:09] layout-agent 完成：3个卧室，全部为空

[20:52:59] 用户：可以，开始布置

[20:53:02] MainAgent: create_job(count=1) ✅
[20:53:12] MainAgent 派发 layout-agent (Generate)
[20:53:17] layout-agent: Read README.md
[20:53:25] layout-agent: Read room_zones.json
[20:53:26] layout-agent: Read exclusions.json
[20:53:27] layout-agent: Read zones.json
[20:53:46] layout-agent: Read openings.json
[20:54:04] layout-agent: Write schemes/rz_1/modules.json ❌
[20:54:08] layout-agent: Write schemes/rz_2/modules.json ❌
[20:54:16] layout-agent: Write schemes/rz_3/modules.json ❌
[20:54:49] MainAgent: complete_job(["agent-main-job1-02"]) ✅

关键问题:
- 没有调用 get_workflow_guide ❌
- 没有调用 request_background_screenshot ❌
- 没有读取 placement_guide.md ❌
- 没有读取 module_library.json ❌
- 写入了错误路径 ❌
```

### 附录 B：理想 vs 实际对比表

| 步骤 | 理想流程 | 实际执行 | 缺失原因 |
|------|----------|----------|----------|
| 1 | get_workflow_guide("generate") | ❌ 跳过 | System Prompt 约束力不足 |
| 2 | request_background_screenshot (前) | ❌ 跳过 | 任务描述未强调 |
| 3 | Read placement_guide.md | ❌ 跳过 | 未明确为强制步骤 |
| 4 | Read module_library.json | ❌ 跳过 | 未明确为强制步骤 |
| 5 | Read room_zones.json | ✅ 执行 | - |
| 6 | Read exclusions.json | ✅ 执行 | - |
| 7 | Read openings.json | ✅ 执行 | - |
| 8 | Write schemes/modules.json | ❌ 路径错误 | README 说明不够清晰 |
| 9 | request_background_screenshot (后) | ❌ 跳过 | 任务描述未强调 |

### 附录 C：关键文件路径

| 文件 | 路径 |
|------|------|
| MainAgent 配置 | `~/.bimcanvas/BIMCANVAS.md` |
| layout-agent 模板 | `~/.bimcanvas/agents/layout-agent.md` |
| Canvas MCP 工具 | `BIMCanvas.Agent/src/mcp/canvas.py` |
| 布置知识库 | `BIMCanvas.Server/Resources/placement_guide.md` |
| 家具库 | `demo_1/modules/module_library.json` |
| 项目 README | `demo_1/README.md` |
| 布置结果 | `demo_1/schemes/modules.json` |

---

## 九、总结

### 核心问题

layout-agent 在执行 Generate 任务时，**跳过了所有关键的准备步骤**，导致：
1. 布置结果不符合标准（尺寸错误）
2. 写入路径错误（前端无法加载）
3. 缺少设计依据（未读取知识库）
4. 没有验证（未调用截图工具）

### 根本原因

1. System Prompt 约束力不足（只是文字描述）
2. MainAgent 任务派发过于简短
3. 缺少工具调用的顺序验证
4. README 数据模型说明不够清晰

### 改进方向

1. **短期**：强化 MainAgent 任务派发 + 改进 README（1-2 小时）
2. **中期**：增强 workflow guide 的约束力（1 小时）
3. **长期**：实现技术强制验证机制（3-4 小时）

### 预期效果

实施方案 A + D 后，预期可以解决 70-80% 的问题。
完整实施所有方案后，预期可以解决 95-99% 的问题。

---

**报告生成时间**: 2026-01-30 21:30
**下一步行动**: 立即实施方案 A（强化 MainAgent 任务派发）
