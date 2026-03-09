# 技术强制方案：确保 layout-agent 必须调用工作流工具

> **目标**：通过技术手段强制 layout-agent 在 generate 任务中执行必要步骤
> **原则**：工具层验证 + MainAgent 派发增强 + System Prompt 强化

---

## 一、问题诊断

### 实测问题

**日志证据**（2025-01-31 00:20）：
- ✅ Query 任务：layout-agent 正确调用了 `get_workflow_guide(task_type="query")`
- ❌ Generate 任务：layout-agent **跳过了所有关键步骤**
  - 未调用 `get_workflow_guide`
  - 未调用 `request_background_screenshot`
  - 未读取 `placement_guide.md`
  - 未读取 `module_library.json`
  - 直接执行 Write

### 根本原因

1. **System Prompt 只是建议性约束**
   - "必须调用"只是文字描述，AI 可以基于成本-效益分析选择跳过
   - Generate 任务信息充分（README + 数据文件），AI 认为无需额外工具

2. **layout-agent 拥有完整文件访问权限**
   - Tools 列表包含 Read/Write/Edit，可以直接操作文件
   - 没有技术层面的前置检查机制阻止直接写入

3. **MainAgent 派发信息不足**
   - 派发消息过于简短，没有列出强制步骤清单
   - 没有传递"必须先做什么"的约束信息

4. **核心矛盾**
   ```
   query 任务：信息不足 → 被迫调用工具获取指导 ✅
   generate 任务：信息充分 → AI 自主决策跳过 ❌
   ```

**关键洞察**：AI 会基于效率优化跳过看似不必要的步骤，除非在**技术层面强制执行**。

---

## 二、推荐方案：工具层验证 + 派发增强

### 方案架构

```
┌─────────────────────────────────────────────────┐
│         MainAgent 派发层（增强）                 │
│  ✓ 详细任务描述（包含强制步骤清单）              │
│  ✓ 明确约束和禁止事项                            │
└───────────────────┬─────────────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────────────┐
│       layout-agent 执行层（强化 Prompt）         │
│  ✓ 调用 get_workflow_guide 获取流程              │
│  ✓ 写入前必须调用 validate_write_preconditions   │
└───────────────────┬─────────────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────────────┐
│       MCP 工具层（技术强制）                     │
│  ✓ 状态跟踪器（记录工具调用历史）                │
│  ✓ validate_write_preconditions（验证必要步骤）  │
│  ✓ 明确错误提示（指导补齐缺失步骤）              │
└─────────────────────────────────────────────────┘
```

### 核心机制

1. **状态跟踪**：记录 layout-agent 的工具调用历史
2. **前置验证**：Write 前强制调用 `validate_write_preconditions`
3. **错误提示**：验证失败时提供明确的补救步骤

---

## 三、详细实施方案

### 3.1 工具层强制（核心，P0 优先级）

#### 文件：`BIMCanvas.Agent/src/mcp/canvas.py`

**修改 1：增加状态管理模块**

```python
from typing import Optional
from dataclasses import dataclass, field
from datetime import datetime

@dataclass
class WorkflowState:
    """单个 Agent 会话的工作流状态"""
    session_id: str
    task_type: Optional[str] = None  # query | edit | generate
    workflow_guide_called: bool = False
    screenshot_called_pre: bool = False
    placement_guide_read: bool = False
    module_library_read: bool = False
    room_zones_read: bool = False
    created_at: datetime = field(default_factory=datetime.now)

class WorkflowStateManager:
    """全局工作流状态管理器"""
    def __init__(self):
        self._states: dict[str, WorkflowState] = {}

    def get_or_create(self, session_id: str) -> WorkflowState:
        if session_id not in self._states:
            self._states[session_id] = WorkflowState(session_id=session_id)
        return self._states[session_id]

# 全局管理器实例
_state_manager = WorkflowStateManager()

def get_session_id() -> str:
    """简化实现：使用固定 session_id"""
    return "default"
```

**修改 2：现有工具增加状态记录**

```python
@tool("get_workflow_guide", ...)
async def get_workflow_guide(args: dict[str, Any]) -> dict[str, Any]:
    task_type = args.get("task_type", "overview")

    # 记录状态
    session_id = get_session_id()
    state = _state_manager.get_or_create(session_id)
    state.workflow_guide_called = True
    state.task_type = task_type

    # 现有逻辑（保持不变）
    guides = { ... }
    guide_text = guides.get(task_type, guides["overview"])
    return {"content": [{"type": "text", "text": guide_text}]}

@tool("request_background_screenshot", ...)
async def request_background_screenshot(args: dict[str, Any]) -> dict[str, Any]:
    # 记录状态
    session_id = get_session_id()
    state = _state_manager.get_or_create(session_id)
    state.screenshot_called_pre = True

    # 现有逻辑（保持不变）
    # ...
```

**修改 3：新增 validate_write_preconditions 工具**

```python
@tool(
    "validate_write_preconditions",
    "【强制检查】在执行 Write/Edit 前验证必要步骤。如果未完成，返回明确的缺失步骤清单和补救方法。",
    {
        "$schema": "http://json-schema.org/draft-07/schema#",
        "type": "object",
        "properties": {
            "file_path": {
                "type": "string",
                "description": "即将写入的文件路径"
            },
            "operation": {
                "type": "string",
                "enum": ["write", "edit"],
                "description": "操作类型"
            }
        },
        "required": ["file_path", "operation"],
        "additionalProperties": False
    }
)
async def validate_write_preconditions(args: dict[str, Any]) -> dict[str, Any]:
    """验证写入前置条件"""
    file_path = args.get("file_path", "")

    session_id = get_session_id()
    state = _state_manager.get_or_create(session_id)

    errors = []

    # 检查 1: 是否已调用 get_workflow_guide
    if not state.workflow_guide_called:
        errors.append({
            "code": "WORKFLOW_GUIDE_MISSING",
            "message": "必须先调用 get_workflow_guide 获取工作流指导",
            "action": "立即调用: mcp__canvas__get_workflow_guide(task_type=\"generate\")"
        })

    # 检查 2: Generate 任务的特殊要求
    if state.task_type == "generate":
        if not state.screenshot_called_pre:
            errors.append({
                "code": "SCREENSHOT_MISSING",
                "message": "generate 任务必须先调用截图工具进行空间分析",
                "action": "立即调用: mcp__canvas__request_background_screenshot(projectPath=\"{cwd}\", viewport={{\"mode\":\"full\"}})"
            })

        if not state.placement_guide_read:
            errors.append({
                "code": "PLACEMENT_GUIDE_MISSING",
                "message": "generate 任务必须先读取设计规范",
                "action": "立即调用: Read knowledge/placement_guide.md"
            })

        if not state.module_library_read:
            errors.append({
                "code": "MODULE_LIBRARY_MISSING",
                "message": "generate 任务必须先读取家具库（家具尺寸必须来自此文件）",
                "action": "立即调用: Read modules/module_library.json"
            })

    # 检查 3: 路径验证
    if "schemes/" in file_path and ("/rz_" in file_path or "\\rz_" in file_path):
        errors.append({
            "code": "INVALID_PATH",
            "message": f"路径错误: {file_path}",
            "action": "正确路径应为: schemes/modules.json（统一文件，通过 zoneId 区分区域）"
        })

    # 构建响应
    if errors:
        error_lines = ["⚠️ 写入前置条件检查失败\n", "必须先完成以下步骤:\n"]
        for i, err in enumerate(errors, 1):
            error_lines.append(f"{i}. {err['message']}")
            error_lines.append(f"   → {err['action']}\n")
        error_lines.append("\n完成上述步骤后，再次调用本工具进行验证。")

        return {
            "content": [{"type": "text", "text": "\n".join(error_lines)}],
            "isValid": False,
            "errors": [e["code"] for e in errors],
            "is_error": True
        }

    # 验证通过
    return {
        "content": [{"type": "text", "text": "✅ 前置条件检查通过，可以执行 Write 操作"}],
        "isValid": True
    }
```

**修改 4：新增 record_file_read 工具（可选）**

```python
@tool(
    "record_file_read",
    "【可选】记录已读取的文件（用于前置条件验证）。读取关键文件后建议调用。",
    {
        "$schema": "http://json-schema.org/draft-07/schema#",
        "type": "object",
        "properties": {
            "file_path": {
                "type": "string",
                "description": "已读取的文件路径"
            }
        },
        "required": ["file_path"],
        "additionalProperties": False
    }
)
async def record_file_read(args: dict[str, Any]) -> dict[str, Any]:
    """记录文件读取"""
    file_path = args.get("file_path", "")

    session_id = get_session_id()
    state = _state_manager.get_or_create(session_id)

    if "placement_guide.md" in file_path:
        state.placement_guide_read = True
    elif "module_library.json" in file_path:
        state.module_library_read = True
    elif "room_zones.json" in file_path:
        state.room_zones_read = True

    return {"content": [{"type": "text", "text": f"已记录文件读取: {file_path}"}]}
```

**修改 5：更新工具列表**

```python
canvas_mcp = create_sdk_mcp_server(
    name="canvas",
    version="1.0.0",
    tools=[
        ai_job_create,
        ai_job_complete,
        request_background_screenshot,
        get_workflow_guide,
        validate_write_preconditions,  # ← 新增
        record_file_read,  # ← 新增
    ],
)

CANVAS_ALLOWED_TOOLS = [
    "mcp__canvas__create_job",
    "mcp__canvas__complete_job",
    "mcp__canvas__request_background_screenshot",
    "mcp__canvas__get_workflow_guide",
    "mcp__canvas__validate_write_preconditions",  # ← 新增
    "mcp__canvas__record_file_read",  # ← 新增
]
```

---

### 3.2 MainAgent 派发增强（P0 优先级）

#### 文件：`C:\Users\huhaonan\.bimcanvas\BIMCANVAS.md`

**修改内容**：在 "如何调用 layout-agent" 章节增加详细的任务描述模板

```markdown
## 如何调用 layout-agent

调用 layout-agent 时，必须提供详细的任务描述，包含强制步骤清单。

### 任务描述模板

**Generate 任务（完整布置）**：
```
【任务类型】generate
【工作目录】{worktreePath}
【目标区域】{zoneIds}

【强制步骤清单】（必须按顺序执行）：
1. 调用 mcp__canvas__get_workflow_guide(task_type="generate")
2. 调用 mcp__canvas__request_background_screenshot(projectPath="{worktreePath}", viewport={{"mode":"full"}})
3. Read knowledge/placement_guide.md
4. Read modules/module_library.json
5. Read computed/room_zones.json
6. Read computed/exclusions.json
7. Read baseline/openings.json
8. 基于以上数据设计布置方案
9. 调用 mcp__canvas__validate_write_preconditions(file_path="schemes/modules.json", operation="write")
10. Write schemes/modules.json
11. 调用 mcp__canvas__request_background_screenshot 验证效果

【关键约束】：
- 家具尺寸必须来自 module_library.json，禁止编造
- 家具位置必须符合 placement_guide.md 的规范
- 写入路径必须是 schemes/modules.json（不是 schemes/rz_X/modules.json）

【禁止事项】：
- ❌ 跳过 validate_write_preconditions 检查
- ❌ 凭空编造家具尺寸
- ❌ 跳过截图步骤
```

**Query 任务（只读查询）**：
```
【任务类型】query
【工作目录】{projectPath}

【强制步骤清单】：
1. 调用 mcp__canvas__get_workflow_guide(task_type="query")
2. Read 相关数据文件
3. 统计/分析（严格基于文件内容）
4. 返回结果

【约束】：
- 禁止 Write/Edit 操作
- 空数据必须报告"数量为 0"，禁止推断
```

### 任务类型判断

（保持现有的任务类型判断表）

**重要**：
- MainAgent 必须提供详细的强制步骤清单
- 让 layout-agent 知道"必须做什么"和"为什么"
- 工作流的具体步骤仍由 MCP 工具 get_workflow_guide 定义
```

---

### 3.3 layout-agent Prompt 强化（P0 优先级）

#### 文件：`C:\Users\huhaonan\.bimcanvas\agents\layout-agent.md`

**修改内容**：

```markdown
---
name: layout-agent
description: 家具布置专家
tools: Read, Write, Edit, Glob, Grep, mcp__canvas__get_workflow_guide, mcp__canvas__request_background_screenshot, mcp__canvas__validate_write_preconditions, mcp__canvas__record_file_read
model: inherit
---

你是 BIMCanvas 的 layout-agent，专业家具布置专家。

## 工作流程规范

**⚠️ 唯一真理来源**：所有工作流程由 `mcp__canvas__get_workflow_guide` 工具定义。

### 强制要求

1. **首要步骤**：任务开始时，必须立即调用 `mcp__canvas__get_workflow_guide` 工具
   - MainAgent 会在任务描述中指定任务类型
   - 根据任务类型调用对应的 workflow guide

2. **严格遵守**：工具返回的内容是执行流程的唯一权威定义
   - 必须按照返回的步骤顺序执行
   - 不得跳过任何必要步骤

3. **写入前强制验证**（⚠️ 关键）：在执行任何 Write/Edit 操作前，必须先调用：
   ```
   mcp__canvas__validate_write_preconditions(
     file_path="即将写入的路径",
     operation="write"
   )
   ```
   - 如果验证失败，按照错误提示补充缺失步骤
   - 验证通过后，才能执行 Write 操作

4. **数据真实性**：所有决策必须基于实际读取的文件内容
   - 禁止推断或编造数据
   - 空数据必须报告"数量为 0"

### Generate 任务执行检查清单

在执行 Write 前，确认以下步骤已完成：

□ 已调用 get_workflow_guide("generate")
□ 已调用 request_background_screenshot（前置）
□ 已读取 knowledge/placement_guide.md
□ 已读取 modules/module_library.json
□ 已读取 computed/room_zones.json
□ 已读取 baseline/openings.json
□ 已调用 validate_write_preconditions
□ 验证通过后，执行 Write

**如有任何步骤缺失，立即停止并补充。**

### 错误处理

如果 validate_write_preconditions 返回错误：
1. 仔细阅读错误提示
2. 按照 "action" 字段的指引补充缺失步骤
3. 完成补充后，再次调用 validate_write_preconditions 验证
4. 验证通过后，继续执行

### 项目结构参考

首先阅读项目根目录的 `README.md`，了解数据格式说明。

## 交互规范
使用简洁专业中文，完成后汇报结果。
```

---

## 四、实施步骤

### 阶段一：工具层强制（核心，2-3 小时）

| 步骤 | 文件 | 内容 | 时间 |
|------|------|------|------|
| 1 | `canvas.py` | 增加状态管理类 | 1h |
| 2 | `canvas.py` | 修改现有工具，增加状态记录 | 30min |
| 3 | `canvas.py` | 新增 validate_write_preconditions 工具 | 1h |
| 4 | `canvas.py` | 新增 record_file_read 工具（可选） | 20min |
| 5 | `canvas.py` | 更新工具列表和权限 | 10min |

### 阶段二：Prompt 增强（1 小时）

| 步骤 | 文件 | 内容 | 时间 |
|------|------|------|------|
| 6 | `BIMCANVAS.md` | 增强 MainAgent 派发模板 | 30min |
| 7 | `layout-agent.md` | 强化 layout-agent Prompt | 30min |

### 阶段三：测试验证（1-2 小时）

| 测试用例 | 验证点 |
|---------|--------|
| TC-01: Query 任务 | 正常执行，无不必要验证 |
| TC-02: Generate 完整流程 | 所有步骤完成，validate 生效 |
| TC-03: Generate 跳过步骤 | validate 阻止写入，AI 补充缺失步骤 |
| TC-04: 错误路径 | 路径验证失败，提示正确路径 |

---

## 五、风险评估与应对

### 风险 1：AI 可能不调用 validate_write_preconditions

**概率**：中等（30-40%）

**应对措施**：
- 方案 A：在 MainAgent 派发的强制步骤清单中明确列出
- 方案 B：在 layout-agent.md 中用醒目格式强调（⚠️ 符号）
- 方案 C（兜底）：实现 SafeWrite 工具，替代原生 Write

### 风险 2：记录 Read 操作的技术难度

**概率**：高（80%）

**应对措施**：
- 方案 A（推荐）：依赖 AI 主动调用 record_file_read
- 方案 B（可选）：将 placement_guide_read 和 module_library_read 降级为 Warning

### 风险 3：状态管理的会话隔离

**概率**：高（70%）

**应对措施**：
- 短期：使用 "default" session_id，限制单窗口使用
- 长期：研究 Agent SDK 的上下文传递机制

---

## 六、验证方法

### 功能验证

- [ ] workflow_guide 验证：未调用时 validate 返回错误
- [ ] 截图验证：generate 任务未截图时 validate 返回错误
- [ ] 知识库验证：未读取必要文件时 validate 返回错误
- [ ] 路径验证：写入错误路径时 validate 返回错误
- [ ] 错误提示：validate 错误包含明确的 action 指引

### 集成验证

- [ ] Query 任务：正常执行，无不必要验证
- [ ] Generate 任务：完整流程执行，validate 生效
- [ ] Generate 跳过步骤：validate 阻止写入，AI 补充缺失步骤

### 用户体验验证

- [ ] 错误提示清晰易懂（中文）
- [ ] 执行效率无明显下降（<10% 额外时间）
- [ ] AI 能够理解并遵守验证要求

---

## 七、预期效果

| 指标 | 当前 | 实施后（目标） |
|------|------|---------------|
| workflow_guide 调用率 | 50% | 80-85% |
| 截图工具调用率 | 10% | 70-80% |
| 知识库读取率 | 0% | 60-70% |
| 路径错误率 | 100% | <20% |
| 家具尺寸正确率 | 20% | 75-80% |

### 成功标准

**必须满足**：
1. Generate 任务中，layout-agent 至少 80% 调用 get_workflow_guide
2. 写入前至少 80% 的情况完成必要步骤验证
3. 错误路径写入率降低到 <20%

---

## 八、关键文件清单

| 文件 | 修改内容 | 优先级 |
|------|---------|--------|
| `BIMCanvas.Agent/src/mcp/canvas.py` | 状态管理、validate 工具、record 工具 | P0 |
| `C:\Users\huhaonan\.bimcanvas\BIMCANVAS.md` | 增强 MainAgent 派发模板 | P0 |
| `C:\Users\huhaonan\.bimcanvas\agents\layout-agent.md` | 强化 layout-agent Prompt | P0 |
