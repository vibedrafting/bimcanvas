# Agent SDK Skill 配置使用研究报告

**研究日期**: 2025-01-25
**研究目标**: 理解如何在 Agent SDK 框架下配置和使用 Skill
**研究状态**: ✅ 已完成

---

## 执行摘要

通过对 Agent SDK 官方文档和示例代码的深度研究，明确了 Skill 的本质、配置方式和使用场景。核心发现：**Skill 是文件系统工件，通过语义描述触发，不需要编程实现**。

---

## 一、Skill 核心概念

### 1.1 Skill 本质

| 特性 | 说明 |
|------|------|
| **文件形式** | 以 `SKILL.md` 文件存在于 `.claude/skills/` 目录 |
| **触发方式** | AI 根据 YAML frontmatter 中的 `description` 自动判断何时调用 |
| **调用机制** | 通过 CLI 的 `Skill` 工具触发（类似 Read、Write 等工具） |
| **编程接口** | ❌ 不提供编程 API，纯文件系统配置 |

### 1.2 与其他机制的区别

| 特性 | Skill | SubAgent | Plugin |
|------|-------|----------|--------|
| **定义方式** | 仅文件系统 | 文件 + 代码 | 仅文件系统 |
| **触发方式** | AI 自动判断 | Task 工具调用 | 斜杠命令 `/skill-name` |
| **编程 API** | ❌ 无 | ✅ `AgentDefinition` | ❌ 无 |
| **适用场景** | 静态知识库 | 动态决策流程 | 用户手动触发 |

---

## 二、配置方式详解

### 2.1 目录结构

```
project/
└── .claude/
    └── skills/
        └── my-skill/              # Skill 标识符
            ├── SKILL.md           # 必需：主文件
            ├── REFERENCE.md       # 可选：详细文档
            └── templates/         # 可选：辅助文件
                └── example.json
```

### 2.2 SKILL.md 格式规范

```markdown
---
name: skill-identifier                # Skill 唯一标识符
description: |                        # 关键：触发条件描述
  详细描述何时应该使用此 Skill：
  - 包含触发关键词（如"家具布置"、"卧室设计"）
  - 说明适用场景（如"当用户要求规划房间时"）
  - 列出相关术语（如"摆放"、"布局"）
license: "MIT"                        # 可选：授权协议
---

# Skill 使用说明

## 概述
简要说明此 Skill 的用途

## 工作流程
1. 步骤一
2. 步骤二
3. ...

## 代码示例
\```python
# 示例代码
\```

## 注意事项
- 关键约束
- 最佳实践
```

### 2.3 Python SDK 配置

```python
from claude_agent_sdk import ClaudeAgentOptions

options = ClaudeAgentOptions(
    # 关键1: 必须指定配置源（包含 project 以加载 .claude/skills/）
    setting_sources=["user", "project"],

    # 关键2: 必须在 allowed_tools 中包含 "Skill"
    allowed_tools=["Skill", "Read", "Write", "Bash"],

    # 工作目录（包含 .claude/ 目录）
    cwd="/path/to/project"
)

agent = Agent(options=options)
```

**重要提示**：
- `setting_sources` 必须包含 `"project"` 才能加载项目级 Skills
- `allowed_tools` 必须包含 `"Skill"` 工具
- 无需在 Python 代码中显式注册或引用 Skill

---

## 三、工作原理

### 3.1 触发流程

```
1. 用户输入 → "帮我设计一个卧室布局"
              ↓
2. Claude 检查 Skills 的 description 字段
              ↓
3. 匹配 "bedroom-design" Skill（description 包含"卧室"）
              ↓
4. Claude 调用 Skill 工具加载 SKILL.md 内容
              ↓
5. 使用 Skill 中的知识回答用户问题
```

### 3.2 Description 编写技巧

**优秀示例**（语义丰富）：
```yaml
description: |
  卧室家具布置设计专家。
  当用户提到以下需求时自动触发：
  - 关键词：卧室、主卧、次卧、儿童房、睡眠区
  - 场景：设计卧室布局、规划床位摆放、优化睡眠空间
  - 任务：家具选型、动线规划、采光优化
```

**糟糕示例**（语义不足）：
```yaml
description: "Bedroom design skill"  # ❌ 过于简单，AI 难以判断触发时机
```

---

## 四、对 BIMCanvas.Agent 的应用建议

### 4.1 Skill 适用场景

**✅ 应该使用 Skill 的场景**：
1. **静态设计规范**：
   - `.claude/skills/bedroom-design/SKILL.md` → 卧室设计标准
   - `.claude/skills/living-room-design/SKILL.md` → 客厅设计规范
   - `.claude/skills/furniture-specs/SKILL.md` → 家具规格知识库

2. **领域知识库**：
   - 建筑术语词汇表
   - 无障碍设计规范
   - 风水布局原则

### 4.2 SubAgent 适用场景

**✅ 应该使用 SubAgent 的场景**：
1. **动态决策流程**：
   ```python
   agents = {
       "placement-planner": AgentDefinition(
           description="根据房间尺寸和用户需求规划家具布置方案",
           tools=["Read", "mcp__canvas__get-zones", "mcp__canvas__place-module"]
       ),
       "collision-resolver": AgentDefinition(
           description="检测并解决家具碰撞冲突",
           tools=["mcp__canvas__validate-placement"]
       )
   }
   ```

2. **多步骤任务**：
   - 读取房间数据 → 分析空间 → 生成方案 → 验证碰撞 → 优化调整

### 4.3 组合使用策略

```
用户请求: "帮我布置一个20㎡的卧室"
         ↓
1. Skill 触发
   - 加载 bedroom-design Skill（设计规范知识）
         ↓
2. SubAgent 执行
   - placement-planner 读取房间数据
   - 根据 Skill 知识生成布置方案
   - 调用 MCP 工具放置家具
         ↓
3. MCP Tool 操作
   - mcp__canvas__place-module() 写入 JSON
   - mcp__canvas__validate-placement() 验证合规性
```

---

## 五、实施路线图

### Phase 1: 创建静态知识 Skills（优先级：高）

```bash
.claude/skills/
├── bedroom-design/
│   ├── SKILL.md           # 卧室设计规范（床头朝向、衣柜位置等）
│   └── REFERENCE.md       # 详细尺寸标准
├── living-room-design/
│   └── SKILL.md           # 客厅设计规范（沙发、茶几布局）
└── furniture-catalog/
    └── SKILL.md           # 家具规格知识库（尺寸、类型、适用场景）
```

### Phase 2: 实现 SubAgent 决策流程（优先级：高）

```python
# BIMCanvas.Agent/main_agent.py

from claude_agent_sdk import Agent, AgentDefinition, ClaudeAgentOptions

agents = {
    "placement-planner": AgentDefinition(
        description="家具布置规划专家，负责生成初步布置方案",
        tools=["Read", "mcp__canvas__get-zones", "mcp__canvas__place-module"]
    ),
    "validator": AgentDefinition(
        description="布置方案验证专家，检查碰撞和规范遵守情况",
        tools=["mcp__canvas__validate-placement"]
    )
}

options = ClaudeAgentOptions(
    setting_sources=["user", "project"],  # 加载 .claude/skills/
    allowed_tools=["Skill", "Task", "Read", "Write"],
    agents=agents,
    cwd="/path/to/BIMCanvas"
)

agent = Agent(options=options)
```

### Phase 3: 集成测试（优先级：中）

1. **单元测试**：验证 Skill 能否正确触发
2. **集成测试**：验证 Skill + SubAgent + MCP Tool 协同工作
3. **用户测试**：验证自然语言交互体验

---

## 六、关键文档路径

| 文档类型 | 路径 | 说明 |
|---------|------|------|
| **Skill 官方指南** | `docs/agent_sdk/docs/Guides/Agent Skills in the SDK.md` | Skill 配置详解 |
| **Python SDK 参考** | `docs/agent_sdk/docs/Python SDK.md` | SDK API 文档 |
| **完整示例** | `docs/agent_sdk/examples/claude-agent-sdk-demos/` | 官方示例代码 |
| **MCP 工具定义** | `docs/Arch_Agent_Git_Workflow.md` | Canvas-MCP 工具规范 |

---

## 七、常见误区与澄清

### ❌ 误区 1：尝试编程创建 Skill
```python
# 错误做法（Skill 没有 Python API）
from claude_agent_sdk import Skill  # ❌ 不存在此类

skill = Skill(name="bedroom", description="...")  # ❌ 无法编程创建
```

**✅ 正确做法**：创建文件 `.claude/skills/bedroom-design/SKILL.md`

### ❌ 误区 2：混淆 Skill 和 SubAgent
- **Skill**：静态知识，AI 自动加载
- **SubAgent**：动态流程，需要编程定义

### ❌ 误区 3：忘记配置 `setting_sources`
```python
# 错误配置（Skill 不会加载）
options = ClaudeAgentOptions(
    allowed_tools=["Skill"]
    # ❌ 缺少 setting_sources=["project"]
)
```

---

## 八、后续研究方向

1. **多语言 Skill 支持**：研究如何处理中英文混合场景
2. **Skill 版本管理**：探索 Skill 更新策略（是否需要迁移机制）
3. **性能优化**：测量 Skill 加载对推理速度的影响
4. **Skill 间依赖**：研究一个 Skill 引用另一个 Skill 的最佳实践

---

## 附录 A：SKILL.md 模板

```markdown
---
name: template-skill
description: |
  [详细描述触发条件]
  - 关键词：[列出触发词]
  - 场景：[说明适用场景]
  - 任务：[列举相关任务]
license: "MIT"
---

# [Skill 名称]

## 概述
[简要说明此 Skill 的用途]

## 适用场景
- 场景 1
- 场景 2

## 工作流程
1. 步骤 1
2. 步骤 2
3. ...

## 关键约束
- 约束 1
- 约束 2

## 示例

### 示例 1
[描述 + 代码]

### 示例 2
[描述 + 代码]

## 参考资料
- [链接或文件路径]
```

---

**报告编制**: BIMCanvas 开发团队
**文档版本**: v1.0
**最后更新**: 2025-01-25
