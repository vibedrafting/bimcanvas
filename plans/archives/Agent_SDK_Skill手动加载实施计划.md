# Agent SDK Skill 恢复实施计划（三套方案对比）

**计划日期**: 2026-01-26
**计划版本**: v2.0（整合 Gemini 专家报告启发）
**任务目标**: 在不破坏配置隔离的前提下，恢复 Skill 功能
**预计时间**: 20 分钟（方案 A）或 30 分钟（方案 B）

---

## 执行摘要

### 当前状态
- ✅ 配置污染问题已修复（`setting_sources=None`）
- ✅ Canvas MCP 工具正常工作
- ❌ Skill 功能失效（`<available_skills>` 为空）

### 目标状态
- ✅ Skill 功能恢复（`git-workflow`, `layout-guide` 可用）
- ✅ 配置隔离保持（不加载 `~/.claude/CLAUDE.md`）
- ✅ Canvas MCP 正常（不受影响）

### 推荐实施路径

**优先级排序**（基于 Gemini 专家报告验证）：
1. **方案 A（Plugins 加载）⭐ 优先尝试** - SDK 原生支持，代码量最少
2. **方案 B（手动加载）** - 如果 Plugins 不可用，立即回退
3. **方案 C（编程式工具）** - 未来重构时考虑（长期演进）

---

## 方案对比矩阵

| 对比维度 | 方案 A（Plugins）⭐ | 方案 B（手动加载） | 方案 C（编程式工具） |
|---------|------------------|-------------------|-----------------|
| **实施成本** | ✅ 低（5行代码） | ⚠️ 中（35行） | 🔴 高（重写Skill） |
| **代码复杂度** | ✅ 低 | ⚠️ 中 | ⚠️ 中 |
| **配置隔离** | ✅ 完美（SDK保证） | ✅ 完美（手动控制） | ✅ 完美（无文件依赖） |
| **维护成本** | ✅ 低（SDK管理） | ⚠️ 中（手动维护） | ✅ 低（类型安全） |
| **文件系统依赖** | ✅ 有（独立目录） | ✅ 有（用户目录） | ✅ 无 |
| **类型安全** | ❌ 无 | ❌ 无 | ✅ 强类型（Pydantic） |
| **调试难度** | ⚠️ 中（SDK黑盒） | ✅ 易（可打印日志） | ✅ 易（标准Debugger） |
| **跨平台兼容** | ⚠️ 需验证 | ✅ 稳定 | ✅ 稳定 |
| **SDK版本要求** | 需支持 `plugins` | ✅ 无要求 | 需支持 MCP |
| **推荐场景** | 首选方案 | Plugins不可用时 | 生产环境重构 |

**结论**：优先尝试方案 A（最简洁），如遇问题立即回退到方案 B（最稳定）。

---

## Skill 文件现状

### 目录结构
```
~/.bimcanvas/skills/
├── git-workflow/
│   └── SKILL.md (3091 字节)
└── layout-guide/
    └── SKILL.md (3753 字节)
```

### Skill 功能说明

#### git-workflow
- **用途**: MainAgent 专用 Git 工作流指导
- **核心决策**: 判断任务类型（query vs execute）
- **关键功能**:
  - Execute 任务：创建隔离环境（ai_job_create → SubAgent → ai_job_complete）
  - Query 任务：直接调用 SubAgent（不创建 worktree）

#### layout-guide
- **用途**: layout-agent 专用操作指南
- **核心流程**: 决策树式任务分类（query/edit/generate）
- **关键约束**:
  - 数据真实性：严格基于实际读取的文件
  - 布置规则：大型家具靠墙、通道宽度 ≥ 800mm
  - 空数据检查：空则报告"数量为 0"，禁止推断

---

## 方案 A：Plugins 加载（⭐ 推荐优先尝试）

### 核心原理（基于 Gemini 专家报告 + SDK 源码验证）

**关键发现**：Agent SDK 支持 `plugins` 参数（`types.py` L425-433, L670）

```python
class SdkPluginConfig(TypedDict):
    """SDK plugin configuration."""
    type: Literal["local"]
    path: str

# ClaudeAgentOptions 中的字段
plugins: list[SdkPluginConfig] = field(default_factory=list)
```

**为什么能解决污染？**
- SDK 扫描插件目录时，**只看其内部的 `.claude/skills/`**
- **不会回溯**到项目根目录的 `CLAUDE.md`
- 插件目录与项目根目录**完全解耦**（Gemini 报告关键洞察）

---

### 实施步骤

#### Phase 1：准备插件目录（5 分钟）

**Step 1.1：创建独立插件目录**

```bash
# Windows PowerShell
mkdir -p $HOME\.bimcanvas\skills-plugin\.claude\skills
```

**Step 1.2：移动现有 Skills**

```bash
# 将现有 Skills 移动到插件目录
Move-Item "$HOME\.bimcanvas\skills\git-workflow" "$HOME\.bimcanvas\skills-plugin\.claude\skills\"
Move-Item "$HOME\.bimcanvas\skills\layout-guide" "$HOME\.bimcanvas\skills-plugin\.claude\skills\"
```

**最终目录结构**：
```
~/.bimcanvas/
└── skills-plugin/          # 独立插件目录
    └── .claude/            # 必须包含 .claude 子目录
        └── skills/
            ├── git-workflow/
            │   └── SKILL.md
            └── layout-guide/
                └── SKILL.md
```

---

#### Phase 2：修改代码（10 分钟）

**文件**: `BIMCanvas.Agent/src/agent/main_agent.py`
**方法**: `_create_options(self, thinking_level: str = None)`

**修改点 1：添加插件配置**

**插入位置**: 在 `return ClaudeAgentOptions(...)` 之前

```python
def _create_options(self, thinking_level: str = None) -> ClaudeAgentOptions:
    settings = get_settings()
    system_prompt = self._config_loader.load_system_prompt()
    system_prompt = system_prompt + f"\n\n工作目录: {self.working_directory}"

    allowed_tools, disallowed_tools = self._config_loader.load_permissions()
    # ... 构建环境变量、获取思考强度 ...

    # MCP 服务器配置
    mcp_tools = CANVAS_ALLOWED_TOOLS
    self._agent_logger._print(f"[MCP] Canvas MCP 已注册，工具: {mcp_tools}")

    # ==================== 修改点 1：添加 Skill 工具 ====================
    all_allowed = (allowed_tools or []) + mcp_tools + ["Skill"]
    # ====================================================================

    # ==================== 修改点 2：配置插件目录 ====================
    from pathlib import Path
    user_home = Path.home()
    skills_plugin_dir = user_home / ".bimcanvas" / "skills-plugin"

    # 检查插件目录是否存在
    if skills_plugin_dir.exists():
        self._agent_logger._print(f"[Plugins] Skills 插件目录: {skills_plugin_dir}")
    else:
        self._agent_logger.log_warning(
            f"[Plugins] ⚠️ Skills 插件目录不存在，Skills 将不可用: {skills_plugin_dir}"
        )
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

        # ==================== 修改点 3：启用插件加载 ====================
        plugins=[
            {
                "type": "local",
                "path": str(skills_plugin_dir)
            }
        ],
        # ====================================================================

        # ==================== 修改点 4：保持配置隔离 ====================
        setting_sources=[],  # 或 None，完全禁用全局扫描
        # ====================================================================
    )
```

**代码改动量**：约 15 行（含注释）

---

#### Phase 3：验证测试（5 分钟）

**测试步骤**：
1. 启动 Agent：`python BIMCanvas.Agent/src/main.py`
2. 发送测试消息：`"hi"`
3. 检查控制台日志
4. 检查请求日志：`references/Agent SDK测试3请求日志/`

**验证清单**：
- [ ] 控制台显示 `[Plugins] Skills 插件目录: ...`
- [ ] `<available_skills>` 包含 `git-workflow`, `layout-guide`
- [ ] system prompt 不包含 `~/.claude/CLAUDE.md` 内容
- [ ] Canvas MCP 工具存在

**如果验证失败**：
- 检查 SDK 版本是否支持 `plugins` 参数
- 检查插件目录结构是否正确（必须包含 `.claude/skills/`）
- **立即回退到方案 B（手动加载）**

---

## 方案 B：手动加载（备选方案）

### 适用场景

- 方案 A（Plugins）验证失败
- SDK 版本不支持 `plugins` 参数
- 跨平台兼容性问题

### 实施步骤

#### Phase 1：代码修改（15 分钟）

#### Step 1.1：添加 Skill 加载逻辑

**插入位置**: 在 `system_prompt = self._config_loader.load_system_prompt()` **之后**

```python
# 从配置加载系统提示词和工具权限
system_prompt = self._config_loader.load_system_prompt()

# ==================== 手动加载 Skills ====================
from pathlib import Path

user_home = Path.home()
skills_dir = user_home / ".bimcanvas" / "skills"

if skills_dir.exists():
    self._agent_logger._print(f"[Skills] 正在加载 Skills: {skills_dir}")

    skill_dirs = sorted([d for d in skills_dir.iterdir() if d.is_dir()])

    for skill_dir in skill_dirs:
        skill_file = skill_dir / "SKILL.md"

        if skill_file.exists():
            skill_name = skill_dir.name

            try:
                skill_content = skill_file.read_text(encoding="utf-8")
                system_prompt += f"\n\n# Skill: {skill_name}\n{skill_content}"

                self._agent_logger._print(
                    f"[Skills] ✅ 已加载: {skill_name} ({len(skill_content)} 字符)"
                )
            except Exception as e:
                self._agent_logger.log_warning(
                    f"[Skills] ❌ 加载失败: {skill_name} - {e}"
                )
        else:
            self._agent_logger.log_warning(
                f"[Skills] ⚠️ Skill 目录缺少 SKILL.md: {skill_dir}"
            )
else:
    self._agent_logger._print(
        f"[Skills] ⚠️ Skills 目录不存在: {skills_dir}"
    )
# ====================================================================

# 追加工作目录到 system prompt
system_prompt = system_prompt + f"\n\n工作目录: {self.working_directory}"
```

#### Step 1.2：恢复 Skill 工具

**位置**: 约 L196

```python
# 修改前
all_allowed = (allowed_tools or []) + mcp_tools

# 修改后
all_allowed = (allowed_tools or []) + mcp_tools + ["Skill"]
```

#### Step 1.3：确认配置隔离

**位置**: 约 L211

```python
setting_sources=None,  # ✅ 保持隔离，不使用 SDK 自动加载
```

**代码改动量**：约 35 行（含注释）

---

#### Phase 2：验证测试（15 分钟）

验证步骤同方案 A。

---

## 方案 C：编程式工具定义（长期演进）

### 核心概念

**转换思路**：将 SKILL.md 的"知识"改写为 Python 函数，直接注册为工具。

**优势**：
- ✅ 完全抛弃文件系统依赖
- ✅ 类型安全（Pydantic 验证）
- ✅ 标准 Debugger 调试
- ✅ 高内聚、低耦合

### 示例：将 git-workflow Skill 改写为 Python 工具

```python
from claude_agent_sdk import tool, create_sdk_mcp_server

@tool(
    name="git_workflow_helper",
    description="判断任务类型并指导 Git 工作流",
    input_schema={
        "type": "object",
        "properties": {
            "user_request": {"type": "string"},
            "task_type": {"type": "string", "enum": ["query", "execute"]}
        },
        "required": ["task_type"]
    }
)
async def git_workflow_helper(args: dict) -> dict:
    task_type = args["task_type"]

    if task_type == "execute":
        return {
            "content": [{
                "type": "text",
                "text": "Execute 任务流程：\n"
                        "1. 调用 mcp__canvas__create_job\n"
                        "2. 获得 worktreePath\n"
                        "3. 调用 SubAgent\n"
                        "4. 调用 mcp__canvas__complete_job"
            }]
        }
    else:
        return {
            "content": [{
                "type": "text",
                "text": "Query 任务流程：直接调用 SubAgent"
            }]
        }

# 创建内存中的 MCP 服务器
internal_mcp = create_sdk_mcp_server(
    name="bimcanvas-internal",
    version="1.0.0",
    tools=[git_workflow_helper]
)

# 在 ClaudeAgentOptions 中注册
ClaudeAgentOptions(
    setting_sources=[],
    mcp_servers={
        "canvas": canvas_mcp,
        "internal": internal_mcp
    },
    allowed_tools=[
        "mcp__canvas__create_job",
        "mcp__canvas__complete_job",
        "mcp__internal__git_workflow_helper"
    ]
)
```

### 何时考虑方案 C

**触发条件**（满足任一）：
- Skill 逻辑变得非常复杂（需要条件判断、状态管理）
- 需要频繁调试 Skill 行为
- 团队规模扩大，需要类型安全和代码审查
- 生产环境部署，追求极致稳定性

**优先级**：低（当前方案 A 或 B 已足够）

---

## 关键文件清单

### 方案 A 需要修改的文件

| 文件 | 修改内容 | 代码量 | 优先级 |
|------|---------|--------|--------|
| `BIMCanvas.Agent/src/agent/main_agent.py` | 添加 plugins 配置 | 15 行 | P0 |

### 方案 B 需要修改的文件

| 文件 | 修改内容 | 代码量 | 优先级 |
|------|---------|--------|--------|
| `BIMCanvas.Agent/src/agent/main_agent.py` | 手动加载逻辑 + 恢复 Skill 工具 | 35 行 | P0 |

### 需要验证的资源

| 资源 | 验证内容 | 优先级 |
|------|---------|--------|
| `~/.bimcanvas/skills-plugin/.claude/skills/git-workflow/SKILL.md` | 方案 A：插件目录中的 Skill 文件 | P0 |
| `~/.bimcanvas/skills/git-workflow/SKILL.md` | 方案 B：原始位置的 Skill 文件 | P0 |

---

## 风险评估

| 风险 | 方案 A | 方案 B | 方案 C | 缓解措施 |
|------|--------|--------|--------|----------|
| SDK 版本不支持 | 🔴 高 | ✅ 低 | ⚠️ 中 | 先尝试 A，失败立即回退 B |
| 跨平台兼容性 | ⚠️ 中 | ✅ 低 | ✅ 低 | 在 Windows 验证后测试 Linux |
| 配置污染再现 | ✅ 低 | ✅ 低 | ✅ 低 | setting_sources=[] 保证隔离 |
| Skill 工具无法调用 | ⚠️ 中 | ⚠️ 中 | ✅ 低 | 验证 `<available_skills>` |
| 开发成本过高 | ✅ 低 | ⚠️ 中 | 🔴 高 | 优先选择 A |

---

## 成功标准

### 必须达成（P0）

- ✅ Skills 已加载（控制台显示加载日志）
- ✅ `<available_skills>` 不为空（请求日志验证）
- ✅ 配置隔离保持（无 `~/.claude/CLAUDE.md` 注入）
- ✅ Canvas MCP 正常（`mcp__canvas__*` 工具存在）

### 建议达成（P1）

- ✅ git-workflow Skill 可被 MainAgent 调用
- ✅ layout-guide Skill 可被 layout-agent 调用
- ✅ ai_job_create/complete 工作流正常

---

## 参考资料

### 相关文档

| 文档 | 路径 | 说明 |
|------|------|------|
| 方案评审 | `reviews/Agent_SDK_Skill恢复方案评审.md` | 方案 B.1（手动加载）评审 |
| 配置污染报告 | `reports/Agent_SDK配置污染问题报告.md` | 问题背景和修复历史 |
| Gemini 专家报告 | `reports/Skill/Claude Agent SDK 配置深度解析_by_Gemini.md` | 方案 A（Plugins）和方案 C 来源 |
| SDK 源码 | `docs/agent_sdk/claude_agent_sdk/types.py` | `SdkPluginConfig` 定义（L425-433, L670） |

### 关键引用

- **Gemini 报告 §4（方案一）**："插件旁路加载策略"
- **Gemini 报告 §5（方案二）**："进程内 MCP 与编程式工具定义"
- **SDK types.py L670**：`plugins: list[SdkPluginConfig] = field(default_factory=list)`

---

## 总结

### 推荐实施路径

```
优先尝试：方案 A（Plugins）
    ├─ 成功 → ✅ 最优解（代码量最少，SDK 原生支持）
    └─ 失败 → 立即回退方案 B（手动加载）
              ├─ 成功 → ✅ 稳定方案
              └─ 失败 → 深入调查 SDK 版本/环境问题

未来演进：方案 C（编程式工具）
    - 触发条件：Skill 逻辑复杂化 or 生产环境部署
    - 优先级：低（当前无需）
```

### 核心优势对比

| 方案 | 代码量 | 实施难度 | 长期维护 | 适用场景 |
|------|--------|----------|----------|----------|
| **A（Plugins）** | ✅ 最少（15行） | ✅ 最简单 | ✅ SDK管理 | **首选** |
| **B（手动加载）** | ⚠️ 中等（35行） | ⚠️ 中等 | ⚠️ 手动维护 | A不可用时 |
| **C（编程式）** | 🔴 最多（重写） | 🔴 最复杂 | ✅ 类型安全 | 生产重构 |

**计划编制**: BIMCanvas 开发团队
**文档版本**: v2.0（整合 Gemini 专家报告）
**最后更新**: 2026-01-26

**插入位置**: 在 `system_prompt = self._config_loader.load_system_prompt()` **之后**

**代码**:
```python
def _create_options(self, thinking_level: str = None) -> ClaudeAgentOptions:
    settings = get_settings()

    # 从配置加载系统提示词和工具权限
    system_prompt = self._config_loader.load_system_prompt()

    # ==================== 新增：手动加载 Skills ====================
    from pathlib import Path
    import os

    # Skill 目录路径：~/.bimcanvas/skills/
    user_home = Path.home()
    skills_dir = user_home / ".bimcanvas" / "skills"

    if skills_dir.exists():
        self._agent_logger._print(f"[Skills] 正在加载 Skills: {skills_dir}")

        # 按名称排序，确保加载顺序一致
        skill_dirs = sorted([d for d in skills_dir.iterdir() if d.is_dir()])

        for skill_dir in skill_dirs:
            skill_file = skill_dir / "SKILL.md"

            if skill_file.exists():
                skill_name = skill_dir.name

                try:
                    skill_content = skill_file.read_text(encoding="utf-8")

                    # 追加到 system_prompt（格式：# Skill: {name}\n{content}）
                    system_prompt += f"\n\n# Skill: {skill_name}\n{skill_content}"

                    self._agent_logger._print(
                        f"[Skills] ✅ 已加载: {skill_name} ({len(skill_content)} 字符)"
                    )
                except Exception as e:
                    self._agent_logger.log_warning(
                        f"[Skills] ❌ 加载失败: {skill_name} - {e}"
                    )
            else:
                self._agent_logger.log_warning(
                    f"[Skills] ⚠️ Skill 目录缺少 SKILL.md: {skill_dir}"
                )
    else:
        self._agent_logger._print(
            f"[Skills] ⚠️ Skills 目录不存在: {skills_dir}"
        )
    # ====================================================================

    # 追加工作目录到 system prompt，让 AI 知道自己的工作路径
    system_prompt = system_prompt + f"\n\n工作目录: {self.working_directory}"

    # ... 后续代码不变 ...
```

---

#### 修改点 2：恢复 Skill 工具

**位置**: L196（约）

**修改前**:
```python
# 合并工具权限
all_allowed = (allowed_tools or []) + mcp_tools
```

**修改后**:
```python
# 合并工具权限（恢复 Skill 工具）
all_allowed = (allowed_tools or []) + mcp_tools + ["Skill"]
```

---

#### 修改点 3：保持配置隔离

**位置**: L211（约）

**确认不变**:
```python
setting_sources=None,  # ✅ 保持隔离，不使用 SDK 自动加载
```

**重要**：不要修改此行，保持 `setting_sources=None`。

---

### Phase 2：验证测试（15 分钟）

#### 测试 1：控制台日志验证

**操作**: 启动 Agent
```bash
cd BIMCanvas.Agent
python src/main.py
```

**预期输出**:
```
[Skills] 正在加载 Skills: C:\Users\huhaonan\.bimcanvas\skills
[Skills] ✅ 已加载: git-workflow (3091 字符)
[Skills] ✅ 已加载: layout-guide (3753 字符)
[MCP] Canvas MCP 已注册，工具: ['mcp__canvas__create_job', 'mcp__canvas__complete_job']
[MainAgent] ========== 配置信息 ==========
[MainAgent] 允许工具: [..., 'Skill']
```

**验证点**:
- ✅ 显示 "正在加载 Skills"
- ✅ 两个 Skill 都显示 "✅ 已加载"
- ✅ allowed_tools 包含 "Skill"

---

#### 测试 2：请求日志验证

**操作**: 发送测试消息
```
用户输入: "hi"
```

**日志位置**: `references/Agent SDK测试3请求日志/`

**验证点**:

**1. system prompt 包含 Skill 内容**
```json
{
  "system": [
    {
      "text": "...\n\n# Skill: git-workflow\n# Git 标准工作流\n...\n\n# Skill: layout-guide\n# Layout Agent 操作指南\n..."
    }
  ]
}
```

**2. tools 列表包含 Skill 工具**
```json
{
  "tools": [
    {
      "name": "Skill",
      "description": "...\n<available_skills>\n- git-workflow: MainAgent 专用：教会何时/如何使用...\n- layout-guide: layout-agent 专用：决策树式操作指导...\n</available_skills>"
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

**3. 无配置污染**
- ❌ system prompt 不包含 `~/.claude/CLAUDE.md` 内容
- ❌ 无 Git 自动存档规则
- ❌ 无 Claude Code 工具（`mcp__context7__*`）

---

#### 测试 3：功能验证

**测试 git-workflow Skill**:
```
用户: "请帮我创建一个隔离环境来执行家具布置任务"
```

**预期行为**:
1. MainAgent 识别到 execute 任务
2. MainAgent 调用 `mcp__canvas__create_job`
3. MainAgent 获得 worktreePath
4. MainAgent 调用 layout-agent SubAgent（传递 worktreePath）
5. SubAgent 执行完毕后，MainAgent 调用 `mcp__canvas__complete_job`

---

**测试 layout-guide Skill**:
```
用户: "统计当前卧室有多少家具"
```

**预期行为**:
1. layout-agent 识别到 query 任务
2. layout-agent 使用 Read 工具读取 `modules.json`
3. layout-agent 基于实际数据统计数量
4. layout-agent 返回统计结果（不编造数据）

---

### Phase 3：文档更新（5 分钟）

#### 更新配置污染报告

**文件**: `reports/Agent_SDK配置污染问题报告.md`

**修改位置**: Phase 4 章节

**标记完成**:
```markdown
#### Phase 4（可选）：恢复 Skill 功能

**状态**: ✅ **已完成**（2026-01-26）

**实施方案**: 方案 B（手动加载 Skill 文件）

**修改内容**:
1. 在 `_create_options()` 添加手动加载逻辑
2. 从 `~/.bimcanvas/skills/` 读取 SKILL.md
3. 追加到 system_prompt（格式：`# Skill: {name}\n{content}`）
4. 恢复 "Skill" 工具到 allowed_tools

**验证结果**:
- ✅ Skills 已加载（git-workflow, layout-guide）
- ✅ `<available_skills>` 不为空
- ✅ 配置隔离保持（setting_sources=None）
- ✅ Canvas MCP 正常工作

**参考文档**:
- plans/Agent_SDK_Skill手动加载实施计划.md
- reviews/Agent_SDK_Skill恢复方案评审.md
```

---

#### 更新 Agent README（可选）

**文件**: `BIMCanvas.Agent/README.md`（如果存在）

**新增章节**:
```markdown
## Skill 管理

### Skill 加载机制

BIMCanvas.Agent 使用**手动加载**方式管理 Skills，确保配置隔离：

- **Skills 目录**: `~/.bimcanvas/skills/`
- **加载时机**: Agent 初始化时（`_create_options()` 方法）
- **加载方式**: 读取 `SKILL.md` 文件，追加到 system_prompt

### 可用 Skills

| Skill | 用途 | 目标 Agent |
|-------|------|-----------|
| git-workflow | Git 工作流指导（ai_job_create/complete） | MainAgent |
| layout-guide | 家具布置操作指南（决策树式流程） | layout-agent |

### 添加新 Skill

1. 在 `~/.bimcanvas/skills/` 创建目录（如 `my-skill/`）
2. 创建 `SKILL.md` 文件
3. 重启 Agent，自动加载

**格式要求**:
```markdown
# Skill 标题

> 简短描述

## 章节 1
...
```
```

---

## 验证清单

### 代码修改完成

- [ ] 添加 Skill 加载逻辑（L156-190，约）
- [ ] 恢复 "Skill" 工具（L196）
- [ ] 确认 `setting_sources=None` 未修改（L211）

---

### 控制台日志验证

- [ ] 显示 "[Skills] 正在加载 Skills"
- [ ] 显示 "[Skills] ✅ 已加载: git-workflow (3091 字符)"
- [ ] 显示 "[Skills] ✅ 已加载: layout-guide (3753 字符)"
- [ ] allowed_tools 包含 "Skill"

---

### 请求日志验证

- [ ] system prompt 包含 `# Skill: git-workflow`
- [ ] system prompt 包含 `# Skill: layout-guide`
- [ ] `<available_skills>` 不为空
- [ ] tools 列表包含 "Skill" 工具
- [ ] tools 列表包含 Canvas MCP 工具
- [ ] system prompt 不包含 `~/.claude/CLAUDE.md` 内容

---

### 功能验证

- [ ] git-workflow Skill 可被 MainAgent 调用
- [ ] layout-guide Skill 可被 layout-agent 调用
- [ ] ai_job_create/complete 工作流正常
- [ ] Canvas MCP 工具正常工作

---

## 关键文件清单

### 需要修改的文件

| 文件 | 修改内容 | 行号（约） | 优先级 |
|------|---------|-----------|--------|
| `BIMCanvas.Agent/src/agent/main_agent.py` | 添加 Skill 加载逻辑 | L156-190 | P0 |
| `BIMCanvas.Agent/src/agent/main_agent.py` | 恢复 "Skill" 工具 | L196 | P0 |

---

### 需要验证的文件

| 文件 | 验证内容 | 优先级 |
|------|---------|--------|
| `~/.bimcanvas/skills/git-workflow/SKILL.md` | 文件存在且可读 | P0 |
| `~/.bimcanvas/skills/layout-guide/SKILL.md` | 文件存在且可读 | P0 |

---

### 需要更新的文档

| 文件 | 更新内容 | 优先级 |
|------|---------|--------|
| `reports/Agent_SDK配置污染问题报告.md` | 标记 Phase 4 完成 | P1 |
| `BIMCanvas.Agent/README.md` | 添加 Skill 管理章节 | P2（可选） |

---

## 技术细节

### Skill 加载原理

**核心机制**：
- Skills 是**提示词模板注入**，不是"工具"
- SDK 解析 system prompt 中的 `# Skill: {name}` 标记
- 自动填充 `<available_skills>` 列表

**加载流程**：
```
启动 Agent
    ↓
调用 _create_options()
    ↓
读取 ~/.bimcanvas/skills/git-workflow/SKILL.md
    ↓
追加到 system_prompt: "\n\n# Skill: git-workflow\n{内容}"
    ↓
读取 ~/.bimcanvas/skills/layout-guide/SKILL.md
    ↓
追加到 system_prompt: "\n\n# Skill: layout-guide\n{内容}"
    ↓
添加 "Skill" 工具到 allowed_tools
    ↓
创建 ClaudeAgentOptions(setting_sources=None)
    ↓
SDK 解析 system_prompt，提取 Skill 列表
    ↓
填充 <available_skills>
    ↓
用户可调用 Skill 工具
```

---

### 为什么不用 SDK 自动加载？

**问题**：`setting_sources=["project"]` 会加载**整个 `.claude/` 目录**
```
.claude/
├── settings.json          ← ⚠️ 可能污染环境变量
├── CLAUDE.md              ← ⚠️ 可能加载项目级指令
├── commands/              ← 自定义命令
├── agents/                ← 自定义 subagent
└── skills/                ← ✅ 我们需要的
```

**风险**：
- 🔴 可能重新引入配置污染
- 🔴 无法细粒度控制加载内容
- 🔴 官方尚未提供独立的 `skills_path` 配置（GitHub Issue #456）

**方案 B 优势**：
- ✅ 完全可控，只加载 Skills
- ✅ `setting_sources=None` 保持不变
- ✅ 零配置污染风险

---

### Skill 文件格式要求

**文件名**：必须是 `SKILL.md`（大小写敏感）

**内容格式**：
```markdown
# Skill 标题

> 简短描述

## 章节 1
内容...

## 章节 2
内容...
```

**重要**：
- SDK 会解析 `# Skill: {name}` 标记（由加载逻辑添加）
- Skill 名称 = 目录名称（如 `git-workflow`）
- 内容是 Markdown 格式的提示词模板

---

## 风险评估

| 风险 | 等级 | 缓解措施 | 状态 |
|------|------|----------|------|
| Skill 文件不存在 | ⚠️ 中 | 代码中添加 exists() 检查，打印警告 | ✅ 已处理 |
| Skill 文件编码错误 | ⚠️ 中 | 使用 `encoding="utf-8"` 显式指定 | ✅ 已处理 |
| Skill 文件读取失败 | ⚠️ 中 | 添加 try-except，记录错误日志 | ✅ 已处理 |
| 配置污染再现 | ✅ 低 | `setting_sources=None` 保持不变 | ✅ 不会发生 |
| Canvas MCP 工具失效 | ✅ 低 | 不修改 MCP 相关代码 | ✅ 不会发生 |
| Skill 工具无法调用 | ⚠️ 中 | 添加详细日志，验证 `<available_skills>` | ⏳ 需测试验证 |

---

## 调试建议

### 如果 Skill 未加载

**检查清单**：
1. 确认 Skills 目录存在：`ls ~/.bimcanvas/skills/`
2. 确认 SKILL.md 存在：`ls ~/.bimcanvas/skills/git-workflow/SKILL.md`
3. 检查控制台日志是否有 "[Skills] ⚠️" 警告
4. 检查文件权限：`ls -la ~/.bimcanvas/skills/git-workflow/SKILL.md`

---

### 如果 `<available_skills>` 为空

**可能原因**：
1. system_prompt 格式不正确（检查是否包含 `# Skill: {name}`）
2. "Skill" 工具未添加到 allowed_tools
3. SDK 版本问题（确认使用最新版 Agent SDK）

**调试步骤**：
1. 打印 system_prompt 内容（前 500 字符）
   ```python
   print(f"[DEBUG] system_prompt 预览:\n{system_prompt[:500]}...")
   ```
2. 检查请求日志中的 system 字段
3. 检查请求日志中的 tools 字段，确认 Skill 工具存在

---

### 如果 Canvas MCP 工具失效

**检查清单**：
1. 确认 `setting_sources=None` 未被修改
2. 确认 mcp_tools 仍包含 Canvas MCP 工具
3. 检查请求日志中的 tools 字段

---

## 后续优化（可选）

### 优化 1：Skill 加载条件过滤

```python
# 仅加载特定 Skills
ALLOWED_SKILLS = ["git-workflow", "layout-guide"]

for skill_dir in skill_dirs:
    skill_name = skill_dir.name

    # 过滤条件
    if skill_name not in ALLOWED_SKILLS:
        self._agent_logger._print(f"[Skills] ⏭️ 跳过: {skill_name}")
        continue

    # ... 加载逻辑
```

**优先级**：低（当前所有 Skills 都需要）

---

### 优化 2：Skill 内容缓存

```python
class MainAgent:
    def __init__(self, ...):
        self._skill_cache: dict[str, str] = {}

    def _load_skills(self) -> str:
        if self._skill_cache:
            return "".join(self._skill_cache.values())

        # 读取 Skills
        for skill_dir in skill_dirs:
            # ...
            self._skill_cache[skill_name] = f"\n\n# Skill: {skill_name}\n{skill_content}"

        return "".join(self._skill_cache.values())
```

**优先级**：低（_create_options 不会频繁调用）

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

**优先级**：低（增加复杂度，收益有限）

---

## 成功标准

### 必须达成（P0）

- ✅ Skills 已加载（控制台显示 "✅ 已加载"）
- ✅ `<available_skills>` 不为空（请求日志验证）
- ✅ 配置隔离保持（无 `~/.claude/CLAUDE.md` 注入）
- ✅ Canvas MCP 正常（`mcp__canvas__*` 工具存在）

---

### 建议达成（P1）

- ✅ git-workflow Skill 可被 MainAgent 调用
- ✅ layout-guide Skill 可被 layout-agent 调用
- ✅ ai_job_create/complete 工作流正常

---

### 可选达成（P2）

- ✅ 文档更新（配置污染报告、Agent README）
- ✅ 代码优化（Skill 过滤、缓存等）

---

## 参考资料

### 相关文档

| 文档 | 路径 | 说明 |
|------|------|------|
| 方案评审 | `reviews/Agent_SDK_Skill恢复方案评审.md` | 方案对比和推荐理由 |
| 配置污染报告 | `reports/Agent_SDK配置污染问题报告.md` | 问题背景和修复历史 |
| 社区研究报告 | `reports/Agent_SDK_Skill配置隔离问题研究报告_by_Claude.md` | GitHub Issue 和社区方案 |

---

### GitHub Issues

| Issue | 标题 | 链接 |
|-------|------|------|
| #456 | Feature: Add explicit skill/command path configuration | https://github.com/anthropics/claude-agent-sdk-python/issues/456 |

---

### 技术文章

| 文章 | 链接 |
|------|------|
| Claude Agent Skills: A First Principles Deep Dive | https://leehanchung.github.io/blogs/2025/10/26/claude-skills-deep-dive/ |

---

## 总结

### 核心策略
- **手动加载** Skills 到 system_prompt
- **保持隔离** `setting_sources=None`
- **零风险** 不引入配置污染

---

### 关键优势
- ✅ 完全可控，明确知道加载了什么
- ✅ 社区验证，技术原理支持
- ✅ 代码简洁，仅 30 行

---

### 实施时间
- **代码修改**: 15 分钟
- **验证测试**: 15 分钟
- **总计**: 30 分钟

---

**计划编制**: BIMCanvas 开发团队
**文档版本**: v1.0
**最后更新**: 2026-01-26
