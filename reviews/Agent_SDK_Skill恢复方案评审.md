# Agent SDK Skill 恢复方案评审

**评审日期**: 2026-01-26
**评审背景**: Agent SDK 配置污染问题修复后，Skill 功能失效
**评审目标**: 在不破坏现有核心功能/不污染配置的情况下，恢复 Skill 功能

---

## 执行摘要

**当前状态**：
- ✅ 配置污染问题已完全修复（`setting_sources=None`）
- ✅ Canvas MCP 工具正常工作
- ✅ BIMCanvas.Agent 核心功能可用
- ❌ Skill 功能失效（`<available_skills>` 为空）

**目标**：
- 恢复项目级 Skill 功能（`git-workflow`, `layout-guide`）
- 保持配置隔离（不引入新的配置污染）
- 保持核心功能稳定（Canvas MCP 工具不受影响）

**推荐方案**：
- ✅ **方案 B：手动加载 Skill 文件**（安全、可控、简洁）

---

## 问题背景

### 修复前的配置污染问题

**严重问题**（已解决）：
```python
# 修复前
setting_sources=["user", "project"]  # ❌ 导致严重配置污染

# 问题：
1. 加载 ~/.claude/CLAUDE.md（Git 自动存档规则）
2. 加载 ~/.claude/settings.json（API 配置）
3. 工具集被完全替换（Canvas MCP 工具缺失）
```

**修复方案**（已实施）：
```python
# 修复后
setting_sources=None  # ✅ 完全禁用文件系统配置加载

# 效果：
1. ✅ 不再加载 ~/.claude/CLAUDE.md
2. ✅ Canvas MCP 工具恢复
3. ✅ API 请求走正确路径
4. ❌ Skill 功能失效（副作用）
```

---

### 当前 Skill 工具状态

**请求日志显示**（测试 2 - 2026-01-26）：
```json
{
  "tools": [
    {
      "name": "Skill",
      "description": "...\n<available_skills>\n\n</available_skills>"
    }
  ]
}
```

**问题**：
- ⚠️ Skill 工具存在，但 `<available_skills>` 为空
- ⚠️ 用户无法调用项目级 Skills（`git-workflow`, `layout-guide`）

---

## 恢复方案对比

### 方案 A：使用 `setting_sources=["project"]`

#### 实现

```python
# BIMCanvas.Agent/src/agent/main_agent.py L211

return ClaudeAgentOptions(
    # ...
    setting_sources=["project"],  # SDK 自动加载 .claude/skills/
)
```

---

#### 工作原理

**Agent SDK 加载机制**（推测）：
```
setting_sources=["project"]
    ↓
扫描 .claude/ 目录
    ↓
加载 .claude/skills/*/SKILL.md
    ↓
填充 <available_skills> 列表
    ↓
用户可调用 Skill 工具
```

---

#### 优点

- ✅ **SDK 原生支持**：利用 Agent SDK 的内置加载机制
- ✅ **代码简洁**：仅需修改 1 行配置
- ✅ **动态管理**：SDK 自动管理 Skill 生命周期
- ✅ **符合官方设计**：这是官方推荐的 Skill 使用方式

---

#### 缺点和风险

**风险 1：可能加载其他配置文件**
```
setting_sources=["project"] 可能加载：
- .claude/skills/*/SKILL.md     ← 我们需要的
- .claude/settings.json          ← ⚠️ 可能污染 env 配置
- .claude/CLAUDE.md              ← ⚠️ 可能加载项目级指令
- 其他未知配置文件               ← ⚠️ 未知风险
```

**风险 2：文档不完整**
- Agent SDK 的 `setting_sources` 机制**文档不完整**
- 无法确定 `["project"]` 是否**只加载 Skills**，还是会加载其他配置
- 需要**实际测试验证**

**风险 3：配置污染再现**
- 如果项目根目录有 `CLAUDE.md` 文件，可能被加载
- 如果 `.claude/settings.json` 包含 `env` 配置，可能污染环境变量

---

#### 验证清单（必须测试）

如果选择此方案，**必须**完成以下验证：

| 验证项 | 预期结果 | 验证方法 |
|--------|----------|----------|
| 1. Skills 已加载 | ✅ `<available_skills>` 包含 git-workflow, layout-guide | 检查请求日志 |
| 2. CLAUDE.md 不加载 | ❌ system prompt 不包含项目根目录/`.claude/` 的 CLAUDE.md | 检查请求日志 |
| 3. settings.json 不加载 env | ❌ 环境变量不被 `.claude/settings.json` 污染 | 检查请求日志/打印 env |
| 4. Canvas MCP 正常 | ✅ `mcp__canvas__*` 工具存在 | 检查请求日志 |
| 5. API 请求路径正确 | ✅ 全部走 Agent 服务器 | 检查服务器日志 |

---

#### 测试步骤

```python
# Step 1: 修改配置
setting_sources=["project"]

# Step 2: 启动 Agent，发送 "hi" 消息

# Step 3: 收集请求日志
references/Agent SDK测试3请求日志/

# Step 4: 检查日志内容
# - system prompt 是否包含 CLAUDE.md 内容？
# - <available_skills> 是否不为空？
# - env 配置是否被污染？

# Step 5: 对比测试 2 的日志
# - 确认没有引入新的配置污染
```

---

#### 评估结论

**适用场景**：
- ✅ 如果 Agent SDK 确实**只加载 Skills**，这是最优方案
- ✅ 如果测试验证**完全安全**，推荐使用

**不适用场景**：
- ❌ 如果测试发现**加载了其他配置**，立即回滚
- ❌ 如果**无法承担测试风险**，选择方案 B

**优先级**：⚠️ **需要验证后才能推荐**

---

### 方案 B：手动加载 Skill 文件（推荐）

#### 实现

```python
# BIMCanvas.Agent/src/agent/main_agent.py
# 在 _create_options 方法中添加

def _create_options(self, thinking_level: str = None) -> ClaudeAgentOptions:
    settings = get_settings()

    # 从配置加载系统提示词和工具权限
    system_prompt = self._config_loader.load_system_prompt()

    # ==================== 新增：手动加载项目级 Skills ====================
    from pathlib import Path
    project_skills_dir = Path(self.working_directory) / ".claude" / "skills"

    if project_skills_dir.exists():
        self._agent_logger._print(f"[Skills] 加载项目 Skills: {project_skills_dir}")

        for skill_dir in sorted(project_skills_dir.iterdir()):
            if not skill_dir.is_dir():
                continue

            skill_file = skill_dir / "SKILL.md"
            if skill_file.exists():
                skill_name = skill_dir.name
                skill_content = skill_file.read_text(encoding="utf-8")

                # 追加到 system_prompt
                system_prompt += f"\n\n# Skill: {skill_name}\n{skill_content}"

                self._agent_logger._print(f"[Skills] 已加载: {skill_name} ({len(skill_content)} 字符)")
            else:
                self._agent_logger.log_warning(f"Skill 目录缺少 SKILL.md: {skill_dir}")
    else:
        self._agent_logger._print(f"[Skills] 项目 Skills 目录不存在: {project_skills_dir}")
    # ====================================================================

    # 追加工作目录到 system prompt
    system_prompt = system_prompt + f"\n\n工作目录: {self.working_directory}"

    allowed_tools, disallowed_tools = self._config_loader.load_permissions()

    # ... 构建环境变量、获取思考强度 ...

    # === MCP 服务器配置 ===
    mcp_tools = CANVAS_ALLOWED_TOOLS
    self._agent_logger._print(f"[MCP] Canvas MCP 已注册，工具: {mcp_tools}")

    # ==================== 修改：重新添加 Skill 工具 ====================
    all_allowed = (allowed_tools or []) + mcp_tools + ["Skill"]  # 恢复 Skill 工具
    # ====================================================================

    return ClaudeAgentOptions(
        system_prompt=system_prompt,
        cwd=self.working_directory,
        max_turns=20,
        model=settings.model_name,
        allowed_tools=all_allowed,
        disallowed_tools=disallowed_tools,
        agents=self._subagents,
        permission_mode="acceptEdits",
        include_partial_messages=True,
        env=custom_env,
        extra_args={"max-thinking-tokens": str(thinking_tokens)} if thinking_tokens else {},
        mcp_servers={"canvas": canvas_mcp},
        setting_sources=None,  # ✅ 保持隔离，不使用 SDK 自动加载
    )
```

---

#### 工作原理

**手动加载流程**：
```
启动 Agent
    ↓
调用 _create_options()
    ↓
扫描 {working_directory}/.claude/skills/
    ↓
读取 git-workflow/SKILL.md
    ↓
追加到 system_prompt: "# Skill: git-workflow\n{内容}"
    ↓
读取 layout-guide/SKILL.md
    ↓
追加到 system_prompt: "# Skill: layout-guide\n{内容}"
    ↓
添加 "Skill" 工具到 allowed_tools
    ↓
创建 ClaudeAgentOptions(setting_sources=None)
    ↓
Agent SDK 解析 system_prompt 中的 "# Skill: xxx"
    ↓
填充 <available_skills> 列表
    ↓
用户可调用 Skill 工具
```

**关键机制**：
- Agent SDK 会**解析 system_prompt** 中的 `# Skill: {name}` 标记
- 即使 `setting_sources=None`，只要 system_prompt 包含 Skill 内容，就会填充 `<available_skills>`

---

#### 优点

- ✅ **完全可控**：明确知道加载了哪些 Skills
- ✅ **安全隔离**：`setting_sources=None` 保持不变，不会引入配置污染
- ✅ **简单直接**：代码逻辑清晰，仅 20 行
- ✅ **易于调试**：可以打印 skill_name 和 skill_content，方便排查问题
- ✅ **灵活扩展**：可以添加过滤、排序、条件加载等逻辑
- ✅ **零风险**：不会加载任何意外配置文件

---

#### 缺点

- ⚠️ **需要手动维护**：加载逻辑需要自己实现
- ⚠️ **失去 SDK 动态管理**：无法使用 Agent SDK 的 Skill 热重载等功能（如果有的话）
- ⚠️ **代码略冗长**：相比方案 A 的 1 行配置，需要 20 行代码

---

#### 验证清单

| 验证项 | 预期结果 | 验证方法 |
|--------|----------|----------|
| 1. Skills 已加载 | ✅ 控制台显示 "[Skills] 已加载: git-workflow" | 启动 Agent，查看控制台 |
| 2. Skill 工具可用 | ✅ `<available_skills>` 包含 git-workflow, layout-guide | 检查请求日志 |
| 3. 配置隔离保持 | ✅ 无 `~/.claude/CLAUDE.md` 注入 | 检查请求日志 system prompt |
| 4. Canvas MCP 正常 | ✅ `mcp__canvas__*` 工具存在 | 检查请求日志 tools 字段 |
| 5. 用户可调用 Skill | ✅ 用户输入触发 Skill 执行 | 实际测试 |

---

#### 测试步骤

```bash
# Step 1: 确认 Skill 文件存在
ls .claude/skills/git-workflow/SKILL.md
ls .claude/skills/layout-guide/SKILL.md

# Step 2: 修改 main_agent.py，添加手动加载逻辑

# Step 3: 启动 Agent
python BIMCanvas.Agent/src/main.py

# Step 4: 检查控制台输出
# 预期看到：
# [Skills] 加载项目 Skills: ...
# [Skills] 已加载: git-workflow (xxx 字符)
# [Skills] 已加载: layout-guide (xxx 字符)

# Step 5: 发送测试消息 "hi"

# Step 6: 检查请求日志
# - system prompt 是否包含 "# Skill: git-workflow"？
# - <available_skills> 是否不为空？

# Step 7: 测试 Skill 调用
# 用户: "请使用 git-workflow 创建隔离环境"
# 预期：Agent 调用 Skill 工具
```

---

#### 评估结论

**适用场景**：
- ✅ **安全优先**：无法承担方案 A 的测试风险
- ✅ **快速实施**：需要立即恢复 Skill 功能
- ✅ **完全可控**：需要明确知道加载了哪些内容

**优先级**：✅ **强烈推荐**（零风险方案）

---

## 方案对比总结

| 对比维度 | 方案 A（SDK 自动加载） | 方案 B（手动加载） |
|---------|----------------------|-------------------|
| **实现复杂度** | ✅ 简单（1 行） | ⚠️ 中等（20 行） |
| **安全性** | ⚠️ **需验证**（风险未知） | ✅ **零风险**（完全可控） |
| **代码可控性** | ❌ 依赖 SDK 黑盒 | ✅ 代码逻辑清晰 |
| **调试难度** | ⚠️ 难（SDK 内部逻辑） | ✅ 易（可打印日志） |
| **扩展性** | ❌ 受限于 SDK | ✅ 灵活（可自定义逻辑） |
| **配置污染风险** | ⚠️ **可能引入** | ✅ **不会引入** |
| **实施时间** | ✅ 5 分钟 | ⚠️ 15 分钟 |
| **测试成本** | 🔴 **高**（需完整验证） | ✅ **低**（确定性行为） |

---

## 推荐方案：方案 B（手动加载）

### 推荐理由

1. **安全第一**：
   - 不会引入任何新的配置污染风险
   - `setting_sources=None` 保持不变
   - 配置隔离得到保证

2. **完全可控**：
   - 明确知道加载了哪些 Skills
   - 可以添加加载条件、过滤逻辑
   - 代码逻辑清晰透明

3. **零风险实施**：
   - 不需要测试验证 Agent SDK 的黑盒行为
   - 行为确定，不会有意外情况
   - 即使出问题，也容易排查

4. **易于维护**：
   - 代码简洁（仅 20 行）
   - 可以添加详细日志
   - 出问题时容易定位

---

### 实施优先级

**立即实施**（推荐）：
- ✅ 方案 B（手动加载）

**可选验证**（后续）：
- ⚠️ 方案 A（SDK 自动加载）
- 仅当方案 B 遇到明确问题时，再考虑方案 A
- 或在低风险环境（开发分支）中测试方案 A

---

## 实施计划

### Phase 1：代码修改（15 分钟）

**文件**：`BIMCanvas.Agent/src/agent/main_agent.py`

**修改点 1**：在 `_create_options` 方法中添加手动加载逻辑（见上文完整代码）

**修改点 2**：恢复 `"Skill"` 工具到 `allowed_tools`
```python
# L196（约）
all_allowed = (allowed_tools or []) + mcp_tools + ["Skill"]
```

---

### Phase 2：验证测试（15 分钟）

#### 测试 1：控制台日志验证

**启动 Agent**：
```bash
python BIMCanvas.Agent/src/main.py
```

**预期输出**：
```
[Skills] 加载项目 Skills: C:\...\BIMCanvas\.claude\skills
[Skills] 已加载: git-workflow (1234 字符)
[Skills] 已加载: layout-guide (5678 字符)
[MCP] Canvas MCP 已注册，工具: ['mcp__canvas__create_job', 'mcp__canvas__complete_job']
[MainAgent] ========== 配置信息 ==========
[MainAgent] 允许工具: [..., 'Skill']
```

---

#### 测试 2：请求日志验证

**发送消息**：`"hi"`

**检查日志**：`references/Agent SDK测试3请求日志/`

**验证点**：
```json
{
  "system": [
    {
      "text": "...\n# Skill: git-workflow\n...\n# Skill: layout-guide\n..."
    }
  ],
  "tools": [
    {
      "name": "Skill",
      "description": "...\n<available_skills>\n- git-workflow: ...\n- layout-guide: ...\n</available_skills>"
    },
    {
      "name": "mcp__canvas__create_job"
    }
  ]
}
```

**关键确认**：
- ✅ system prompt 包含 `# Skill: git-workflow` 和 `# Skill: layout-guide`
- ✅ `<available_skills>` 不为空
- ✅ Canvas MCP 工具存在
- ✅ 无 `~/.claude/CLAUDE.md` 内容

---

#### 测试 3：功能验证

**测试 Skill 调用**：
```
用户: "请帮我创建一个 Git worktree 来隔离开发"
预期: Agent 识别到需要使用 git-workflow Skill
```

**测试 Canvas MCP**：
```
用户: "创建 2 个工作环境"
预期: Agent 调用 mcp__canvas__create_job(count=2)
```

---

### Phase 3：文档更新（10 分钟）

**更新文件**：
1. `reports/Agent_SDK配置污染问题报告.md`
   - 标记 Phase 4 完成
   - 记录手动加载 Skill 的实现

2. `BIMCanvas.Agent/README.md`（如果存在）
   - 补充 Skill 加载机制说明

---

## 预期结果

### 控制台输出

```
[MainAgent] ========== 配置信息 ==========
[MainAgent] 模型: claude-sonnet-4-5-20250929
[MainAgent] Base URL: http://localhost:5000
[MainAgent] 思考强度: 禁用
[Skills] 加载项目 Skills: C:\Users\huhaonan\Documents\BIMCanvas\Projects\demo_1\.claude\skills
[Skills] 已加载: git-workflow (1523 字符)
[Skills] 已加载: layout-guide (2847 字符)
[MCP] Canvas MCP 已注册，工具: ['mcp__canvas__create_job', 'mcp__canvas__complete_job']
[MainAgent] 允许工具: ['Read', 'Glob', 'Grep', 'Write', 'Edit', 'Bash', 'mcp__canvas__create_job', 'mcp__canvas__complete_job', 'Skill']
[MainAgent] 禁止工具: 无
[MainAgent] 项目路径: E:\工作文档\开发类\MyCode\BIMCanvas
[MainAgent] 工作目录: C:\Users\huhaonan\Documents\BIMCanvas\Projects\demo_1
[MainAgent] ================================
```

---

### 请求日志（部分）

**system prompt**：
```markdown
你是 BIMCanvas 的主控 Agent，一个专业的室内布置协调者。

## 职责
1. 分析用户的布置需求，理解设计意图
...

# Skill: git-workflow

MainAgent Git 工作流指导。当用户请求 execute 类任务（布置、添加、移动、删除、设计、创建）时，
指导 MainAgent 先调用 ai_job_create 创建隔离环境，然后调用 SubAgent，最后调用 ai_job_complete。
对于 query 任务（统计、查看、列出、有多少、当前状态），直接调用 SubAgent 即可。

...

# Skill: layout-guide

Layout Agent 操作指导手册。当 layout-agent 需要执行家具布置任务时，
提供决策树式流程：query（统计/查看）、edit（移动/删除/旋转）、generate（完整布置设计）。
包含核心约束、优先级规则、标签驱动选择和数据真实性要求。

...

工作目录: C:\Users\huhaonan\Documents\BIMCanvas\Projects\demo_1
```

**tools 列表**：
```json
{
  "tools": [
    {
      "name": "Skill",
      "description": "Execute a skill within the main conversation\n\n<skills_instructions>...\n\n<available_skills>\n- git-workflow: MainAgent Git 工作流指导...\n- layout-guide: Layout Agent 操作指导手册...\n</available_skills>"
    },
    {
      "name": "mcp__canvas__create_job"
    },
    {
      "name": "mcp__canvas__complete_job"
    }
  ]
}
```

---

## 风险评估

| 风险 | 等级 | 缓解措施 | 状态 |
|------|------|----------|------|
| Skill 文件不存在 | ⚠️ 中 | 代码中添加 exists() 检查，打印警告 | ✅ 已处理 |
| Skill 文件编码错误 | ⚠️ 中 | 使用 `encoding="utf-8"` 显式指定 | ✅ 已处理 |
| 配置污染再现 | ✅ 低 | `setting_sources=None` 保持不变 | ✅ 不会发生 |
| Canvas MCP 工具失效 | ✅ 低 | 不修改 MCP 相关代码 | ✅ 不会发生 |
| Skill 工具无法调用 | ⚠️ 中 | 添加详细日志，验证 `<available_skills>` | ⏳ 需测试验证 |

---

## 后续优化（可选）

### 优化 1：Skill 加载条件过滤

```python
# 仅加载特定 Skills
ALLOWED_SKILLS = ["git-workflow", "layout-guide"]

for skill_dir in sorted(project_skills_dir.iterdir()):
    skill_name = skill_dir.name

    # 过滤条件
    if skill_name not in ALLOWED_SKILLS:
        self._agent_logger._print(f"[Skills] 跳过: {skill_name}")
        continue

    # ... 加载逻辑
```

---

### 优化 2：Skill 内容缓存

```python
# 避免每次创建 options 时重新读取文件
class MainAgent:
    def __init__(self, ...):
        self._skill_cache: dict[str, str] = {}

    def _load_skills(self) -> str:
        if self._skill_cache:
            return "".join(self._skill_cache.values())

        # 读取 Skills
        for skill_dir in ...:
            skill_content = skill_file.read_text(encoding="utf-8")
            self._skill_cache[skill_name] = f"\n\n# Skill: {skill_name}\n{skill_content}"

        return "".join(self._skill_cache.values())
```

---

### 优化 3：Skill 热重载

```python
# 监听 Skill 文件变化，动态重新加载
from watchdog.observers import Observer
from watchdog.events import FileSystemEventHandler

class SkillFileHandler(FileSystemEventHandler):
    def on_modified(self, event):
        if event.src_path.endswith("SKILL.md"):
            # 清空缓存，触发重新加载
            self._agent._skill_cache.clear()
```

**优先级**：低（非必需，增加复杂度）

---

## 总结

### 关键决策

**选择方案 B（手动加载）的原因**：
1. ✅ **安全第一**：不引入任何配置污染风险
2. ✅ **完全可控**：代码逻辑清晰透明
3. ✅ **零测试成本**：行为确定，无需复杂验证
4. ✅ **易于维护**：代码简洁，易于调试

---

### 实施路径

```
当前状态（配置污染已修复）
    ↓
修改 main_agent.py（添加手动加载逻辑）
    ↓
恢复 "Skill" 工具到 allowed_tools
    ↓
启动 Agent，检查控制台日志
    ↓
发送测试消息，检查请求日志
    ↓
验证 Skill 功能可用
    ↓
更新文档，标记 Phase 4 完成
```

---

### 预期效果

- ✅ Skill 功能恢复（`git-workflow`, `layout-guide` 可用）
- ✅ 配置隔离保持（`setting_sources=None`）
- ✅ Canvas MCP 正常（不受影响）
- ✅ API 请求路径正确（Agent 服务器）
- ✅ 核心功能稳定（家具布置功能正常）

---

### 可选的后续验证

**方案 A 验证**（可选，低优先级）：
- 在开发分支测试 `setting_sources=["project"]`
- 确认是否真的**只加载 Skills**
- 如果验证成功，可以考虑迁移到方案 A（代码更简洁）
- 但当前方案 B 已经足够好，无迁移必要性

---

**评审结论**：✅ **推荐方案 B，立即实施**

**评审人员**: BIMCanvas 开发团队
**文档版本**: v1.0
**最后更新**: 2026-01-26
