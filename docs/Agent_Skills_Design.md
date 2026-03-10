# BIMCanvas Agent Skills 设计文档

> **版本**：v1.0 | **更新日期**：2026-01-15
> **状态**：设计中
>
> ⚠️ **注意**：本文档为 Skills 机制的早期设计文档。当前 Skills 架构已在工作流重构（T1-T3）中全面升级：
> - Skills 按功能分为：`generate-workflow`（五阶段主流程）、`generate-zoning`（分区能力）、`generate-bedroom/bathroom/livingroom`（房间策略）
> - 知识库已从 `placement_guide.md` 迁移为 `design_principles.md` + 各房间 Skill
> - layout-agent 角色已从「家具布置专家」重定位为「单房间设计专家」
> - 详见：`plans/workflow-refactor/overview.md`
>
> **相关文档**：
> - [Agent SDK 官方文档](../docs/Agent_SDK/docs/)
> - [Flow_Git_Operations.md](./Flow_Git_Operations.md) - Git 标准工作流
>
> **Agent 项目路径**：`C:\Users\huhaonan\.bimcanvas\`（类似 Claude Code 的 `.claude` 目录）
> - 主控Agent提示词：`BIMCANVAS.md`
> - layout-agent提示词：`agents/layout-agent.md`
> - Skills目录：`skills/`

---

## 一、Skills 概述

### 1.1 什么是 Skills

Skills 是 Claude Agent SDK 提供的一种**工作流封装机制**，以 `SKILL.md` 文件形式定义，Claude 会根据用户请求自动判断何时调用。

**核心特点**：

| 特性 | 说明 |
|------|------|
| **文件驱动** | 存储在 `.claude/skills/{skill-name}/SKILL.md` |
| **自主调用** | Claude 根据 `description` 字段自动匹配触发 |
| **按需加载** | 只有触发时才加载完整内容，节省 Token |
| **无需编码** | Markdown 格式，易于维护和修改 |

### 1.2 Skills 文件结构

```markdown
---
name: skill-id                              # 唯一标识符
description: "触发条件描述"                  # Claude 用来判断是否调用
allowed-tools: Read, Write, Edit            # 可选：限制工具访问
---

# Skill 标题

工作流程内容...
```

### 1.3 Skills vs Agent 提示词

| 维度 | Agent 提示词 | Skills |
|------|-------------|--------|
| **加载时机** | 每次调用都加载 | 按需加载（触发时） |
| **适合内容** | 角色定义、基本能力 | 具体工作流程、详细规则 |
| **Token 消耗** | 固定消耗 | 按需消耗 |
| **维护方式** | 单一大文件 | 多个专注小文件 |

---

## 二、为什么在 BIMCanvas 中使用 Skills

### 2.1 当前痛点

**现状**：layout-agent 的提示词包含所有内容：
- 角色定义
- query/execute 两种模式
- 布置规则（床头靠墙、沙发朝向等）
- 工作流程
- 输出格式

**问题**：
1. **Token 浪费**：简单查询任务（"有多少模块"）也加载完整布置规则
2. **缺乏精准引导**：所有任务看到同样的提示词，无法针对性指导
3. **硬性要求易遗漏**："必须阅读 placement_guide" 写在提示词中，但不与具体任务绑定

### 2.2 Skills 带来的收益

| 收益 | 说明 |
|------|------|
| **渐进式披露** | 简单任务只加载简单流程，复杂任务才加载完整规则 |
| **任务精准引导** | 不同任务类型使用不同 Skill，获得针对性指导 |
| **强制要求绑定** | 将"必须阅读 placement_guide"与 generate 任务绑定 |
| **易于维护** | 修改某类任务流程只需编辑对应 Skill 文件 |

### 2.3 适用场景分析

基于 BIMCanvas 的任务类型（参见 Flow_Git_Operations.md §1.6）：

| 任务类型 | 示例 | 复杂度 | Skills 收益 |
|----------|------|--------|-------------|
| **query** | "统计模块数量" | 低 | 高 - 无需加载布置规则 |
| **execute-edit** | "把床移动50cm" | 中 | 中 - 只需编辑规则 |
| **execute-generate** | "布置整个客厅" | 高 | 高 - 需要完整布置知识 |

---

## 三、BIMCanvas Skills 架构

### 3.1 整体架构

**Agent 项目根目录**：`C:\Users\huhaonan\.bimcanvas\`

```
C:\Users\huhaonan\.bimcanvas\
│
├── BIMCANVAS.md                    # 主控Agent提示词
├── agents/
│   └── layout-agent.md             # layout-agent提示词
│
├── skills/                         # Skills 目录
│   ├── git-workflow/               # 主控Agent使用
│   │   └── SKILL.md                # Git 操作标准流程
│   │
│   └── layout-agent/               # layout-agent 使用
│       ├── query/
│       │   └── SKILL.md            # 查看/统计任务
│       ├── edit/
│       │   └── SKILL.md            # 编辑任务
│       └── generate/
│           └── SKILL.md            # 生成任务（含强制要求）
│
├── knowledge/                      # 知识库
│   └── placement_guide.md          # 布置规则指南
│
└── ...
```

### 3.2 职责划分

#### 主控Agent + git-workflow

| 职责 | 说明 |
|------|------|
| 任务理解 | 分析用户需求，判断任务类型 |
| Git 操作 | 创建 Worktree、提交、合并、清理 |
| 任务协调 | 调用 layout-agent，传递工作目录 |
| 结果汇报 | 整合执行结果，向用户反馈 |

#### layout-agent + query/edit/generate Skills

| 职责 | 说明 |
|------|------|
| 数据读取 | 读取房间、模块、门窗数据 |
| 布置决策 | 根据规则决定家具位置 |
| 文件写入 | 输出布置结果到 modules.json |

**关键原则**：
- **主控Agent 管 Git**：layout-agent 不直接操作 Git
- **layout-agent 管布置**：只在主控Agent 指定的目录中工作

### 3.3 各 Skill 详细定义

#### git-workflow（主控Agent）

```yaml
name: git-workflow
description: |
  execute 类任务的 Git 操作流程。
  当需要执行写操作（布置、编辑、生成）时触发。
```

**核心流程**：
1. 准备阶段：创建 Worktree
2. 执行阶段：调用 layout-agent（传递工作目录路径）
3. 提交阶段：提交更改
4. 合并阶段：合并到用户分支
5. 清理阶段：删除 Worktree

#### query（layout-agent）

```yaml
name: query
description: |
  只读查询任务。
  当用户请求"查看"、"统计"、"列出"、"有多少"时触发。
```

**核心流程**：
1. Read 读取目标数据
2. 空数据检查
3. 分析/统计
4. 返回结果（禁止 Write/Edit）

**强制约束**：
- 禁止使用 Write、Edit 工具
- 输出必须严格基于实际读取的文件内容

#### edit（layout-agent）

```yaml
name: edit
description: |
  单一模块编辑任务。
  当用户请求"移动"、"删除"、"旋转"、"调整"时触发。
```

**核心流程**：
1. Read 读取当前布置
2. 定位目标模块
3. 执行修改操作
4. 验证约束（间距、边界）
5. Write 保存结果

#### generate（layout-agent）

```yaml
name: generate
description: |
  完整布置生成任务。
  当用户请求"布置"、"设计"、"生成"、"创建方案"时触发。
```

**强制要求**：
- **必须**先阅读 `knowledge/placement_guide.md`
- **必须**按优先级布置（锚点→主要→辅助）
- **必须**验证布置结果后再输出

**核心流程**：
1. Read placement_guide.md（强制）
2. Read room_zones.json
3. Read module_library.json
4. Read openings.json
5. 分析空间约束
6. 按优先级布置
7. 验证布置结果
8. Write modules.json

---

## 四、调用机制

### 4.1 混合模式

BIMCanvas 采用**混合调用模式**：

| 模式 | 说明 | 适用场景 |
|------|------|---------|
| **自主模式** | Claude 根据任务描述自动选择 Skill | 常规任务 |
| **显式模式** | 主控Agent 在任务描述中指定 Skill | 需要精确控制时 |

### 4.2 自主模式触发

Claude 根据 Skill 的 `description` 字段匹配用户请求：

```
用户: "当前项目有多少模块"
         ↓ 匹配 "统计"、"有多少"
Claude: 触发 query Skill
```

```
用户: "帮我布置客厅"
         ↓ 匹配 "布置"
Claude: 触发 generate Skill
```

### 4.3 显式模式指定

主控Agent 在调用 layout-agent 时显式指定：

```
【操作类型】: execute
【Skill】: generate              ← 显式指定
【用户需求】: 布置主卧
【目标对象】: 房间 rz_3（主卧）
【工作目录】: .worktrees/ai-001
```

### 4.4 Skill 发现机制

Agent SDK 在启动时自动扫描 `skills/` 目录：

```
启动时：
  → 扫描 skills/ 目录
  → 读取每个 SKILL.md 的 YAML 前置元数据（name, description）
  → 构建 Skill 索引

运行时：
  → 用户请求到达
  → 匹配 description
  → 触发时加载完整 SKILL.md 内容
```

---

## 五、与现有工作流的集成

### 5.1 当前工作流（无 Skills）

```
用户请求
    │
    ▼
主控Agent (BIMCANVAS.md)
    │
    ├─► 判断任务类型 (query/execute)
    │
    ▼
调用 layout-agent (layout-agent.md)
    │
    ├─► 完整提示词加载（含所有规则）
    │
    ▼
执行任务
    │
    ▼
返回结果
```

### 5.2 目标工作流（有 Skills）

```
用户请求
    │
    ▼
主控Agent
    │
    ├─► 判断任务类型
    │
    ├─► [execute 任务] 触发 git-workflow Skill
    │       │
    │       ├─► 创建 Worktree
    │       ├─► 调用 layout-agent（传递工作目录）
    │       ├─► 提交、合并、清理
    │       │
    │       ▼
    │   layout-agent
    │       │
    │       ├─► 根据任务匹配 Skill (query/edit/generate)
    │       ├─► 加载对应 Skill 内容
    │       ├─► 执行任务
    │       │
    │       ▼
    │   返回结果
    │
    └─► [query 任务] 直接调用 layout-agent
            │
            ├─► 触发 query Skill
            ├─► 只读执行
            │
            ▼
        返回结果
```

### 5.3 提示词精简

**迁移前**（layout-agent.md）：
```markdown
# 完整提示词（约 2000 字）
- 角色定义
- query/execute 模式
- 布置规则（靠墙、间距等）
- 工作流程
- 输出格式
```

**迁移后**（layout-agent.md）：
```markdown
# 精简提示词（约 500 字）
- 角色定义
- 基本能力声明
- Skill 调用说明
```

**迁移到 Skills**：
```
query/SKILL.md      ← query 模式流程
edit/SKILL.md       ← 编辑规则
generate/SKILL.md   ← 布置规则、工作流程、输出格式
```

---

## 六、实现指南

### 6.1 前置条件

1. **Agent SDK 环境**：确保已安装 `anthropic` 包
2. **目录结构**：创建 `skills/` 目录

### 6.2 Step 1: 创建 Skill 文件

在 Agent 项目根目录 `C:\Users\huhaonan\.bimcanvas\` 下创建目录结构：

```bash
# Windows PowerShell
New-Item -ItemType Directory -Path "skills\git-workflow" -Force
New-Item -ItemType Directory -Path "skills\layout-agent\query" -Force
New-Item -ItemType Directory -Path "skills\layout-agent\edit" -Force
New-Item -ItemType Directory -Path "skills\layout-agent\generate" -Force
```

```bash
# Git Bash / Linux
mkdir -p skills/git-workflow
mkdir -p skills/layout-agent/query
mkdir -p skills/layout-agent/edit
mkdir -p skills/layout-agent/generate
```

### 6.3 Step 2: 编写 git-workflow Skill

创建 `skills/git-workflow/SKILL.md`：

```markdown
---
name: git-workflow
description: |
  execute 类任务的 Git 操作流程。
  当需要执行写操作（布置、编辑、生成）时触发。
---

# Git 工作流程

## 适用场景
- 布置家具（execute-generate）
- 编辑模块（execute-edit）
- 任何需要修改项目数据的操作

## 标准流程

### 1. 准备阶段
创建 Worktree 隔离环境：
- 调用 Server API: POST /api/git/worktree
- 获取工作目录路径

### 2. 执行阶段
调用 layout-agent：
- 传递工作目录路径
- 等待执行完成

### 3. 提交阶段
提交更改：
- 调用 Server API: POST /api/git/commit
- 提交信息格式: "feat(layout): {描述}"

### 4. 合并阶段
合并到用户分支：
- 调用 Server API: POST /api/git/merge

### 5. 清理阶段
删除 Worktree：
- 调用 Server API: DELETE /api/git/worktree/{name}

## 任务描述格式

调用 layout-agent 时使用以下格式：

【操作类型】: execute
【Skill】: {query|edit|generate}
【用户需求】: {用户原始需求}
【目标对象】: {目标房间/区域}
【工作目录】: {Worktree 路径}
```

### 6.4 Step 3: 编写 layout-agent Skills

#### query/SKILL.md

```markdown
---
name: query
description: |
  只读查询任务。
  当用户请求"查看"、"统计"、"列出"、"有多少"时触发。
allowed-tools: Read, Glob, Grep
---

# 查询任务流程

## 强制约束
- **禁止**使用 Write、Edit 工具
- 输出必须**严格基于**实际读取的文件内容
- 空数组 → 报告"数量为 0"，禁止推断

## 工作流程

1. **Read** 读取目标数据（如 schemes/modules.json）
2. **空数据检查**：如果数组为空，直接返回"数量为 0"
3. **分析/统计**（仅基于实际读取的数据）
4. **验证**：确认报告内容与文件实际内容一致
5. **返回结果**

## 输出规范
- 报告的每个模块 ID 必须在文件中实际存在
- 统计结果必须与数组长度一致
```

#### edit/SKILL.md

```markdown
---
name: edit
description: |
  单一模块编辑任务。
  当用户请求"移动"、"删除"、"旋转"、"调整"时触发。
---

# 编辑任务流程

## 适用操作
- 移动模块位置
- 旋转模块朝向
- 删除模块
- 调整模块属性

## 工作流程

1. **Read** schemes/modules.json
2. **定位**目标模块（根据 ID 或位置描述）
3. **执行**修改操作
4. **验证**约束：
   - 间距检查（通道 ≥ 800mm）
   - 边界检查（不超出房间）
   - 碰撞检查（不与其他模块重叠）
5. **Write** 保存结果

## 约束规则
- 移动后保持与墙面最小间距
- 旋转角度自动吸附到 45° 倍数（可选）
- 删除时检查依赖关系
```

#### generate/SKILL.md

```markdown
---
name: generate
description: |
  完整布置生成任务。
  当用户请求"布置"、"设计"、"生成"、"创建方案"时触发。
---

# 布置生成流程

## 强制要求（必须遵守）

1. **必须**先阅读 `knowledge/placement_guide.md`
2. **必须**按优先级布置（锚点→主要→辅助）
3. **必须**验证布置结果后再输出

## 工作流程

### 1. 知识准备（强制）
- Read knowledge/placement_guide.md

### 2. 数据读取
- Read computed/room_zones.json（房间分区）
- Read modules/module_library.json（模块库）
- Read baseline/openings.json（门窗位置）

### 3. 空间分析
- 识别可用空间边界
- 标记门窗禁区
- 计算通道需求

### 4. 布置执行

#### 布置优先级
1. **锚点家具**：客厅→电视柜，卧室→床，餐厅→餐桌
2. **主要家具**：围绕锚点布置
3. **辅助家具**：填充剩余空间

#### 核心规则
- 大型家具靠墙（床、衣柜、沙发）
- 电视柜居中于电视墙
- 沙发正对电视（2.5-4m）
- 床头不靠窗
- 家具不阻挡门
- 通道宽度 ≥ 800mm

### 5. 验证检查
- 边界检查：所有模块在房间内
- 碰撞检查：模块间无重叠
- 禁区检查：不与门窗禁区重叠
- 通道检查：主通道 ≥ 800mm

### 6. 输出结果
Write schemes/modules.json

## 输出格式

```json
[
  {
    "id": "m_1",
    "moduleId": "mod_bed_001",
    "zoneId": "rz_3",
    "bounds": [[x1,y1], [x2,y2], [x3,y3], [x4,y4]],
    "facing": "north",
    "items": []
  }
]
```

## 标签驱动选择
根据 zone.tags 筛选 module.tags 有交集的模块。
```

### 6.5 Step 4: 精简 Agent 提示词

修改 `layout-agent.md`，移除已迁移到 Skills 的内容：

```markdown
---
name: layout-agent
description: 家具布置专家。用于空间规划、家具摆放、布局优化、布置查询任务。
tools: Read, Write, Edit, Glob, Grep
model: opus
---

你是 BIMCanvas 的 layout-agent，专业家具布置专家。

## 职责
1. 读取房间分区数据，理解空间特点
2. 分析门窗位置，规划动线
3. 根据布置规则为房间布置家具
4. 输出符合规范的布置结果

## 可用 Skills
- **query**: 查看/统计任务（只读）
- **edit**: 编辑任务（单一模块修改）
- **generate**: 生成任务（完整布置）

## 行为边界

### 禁止行为
- 执行用户未明确要求的操作
- 根据房间信息推断/编造不存在的模块
- 报告与文件实际内容不符的结果

### 数据真实性
- 输出必须**严格基于**实际读取的文件内容

## 文件结构
- **输入**：computed/room_zones.json, baseline/openings.json, modules/module_library.json
- **输出**：schemes/modules.json

## 交互规范
使用简洁专业中文，完成后汇报结果。
```

### 6.6 Step 5: 配置 Agent SDK

确保 Agent 配置中启用 Skills 加载：

```python
from anthropic import Anthropic

client = Anthropic()

# 关键配置
options = {
    "cwd": r"C:\Users\huhaonan\.bimcanvas",  # Agent 项目根目录
    "setting_sources": ["user", "project"],  # 启用 Skills 加载
    "allowed_tools": ["Skill", "Read", "Write", "Edit", "Glob", "Grep"]
}
```

**配置说明**：
- `cwd`：指向 Agent 项目根目录（包含 `skills/` 目录）
- `setting_sources`：必须包含 `"project"` 才能加载项目级 Skills
- `allowed_tools`：必须包含 `"Skill"` 才能触发 Skill 调用

---

## 七、最佳实践

### 7.1 Skill description 编写

**好的 description**：
```yaml
description: |
  完整布置生成任务。
  当用户请求"布置"、"设计"、"生成"、"创建方案"时触发。
```
- 明确触发条件
- 列出关键词

**差的 description**：
```yaml
description: 布置家具
```
- 太模糊，容易误触发

### 7.2 强制要求的放置

将强制要求放在 Skill 开头，确保 Claude 优先看到：

```markdown
## 强制要求（必须遵守）

1. **必须**先阅读 knowledge/placement_guide.md
2. ...

## 工作流程
...
```

### 7.3 Skill 粒度控制

| 粒度 | 优点 | 缺点 |
|------|------|------|
| 太细（每个操作一个 Skill） | 精准控制 | 管理复杂，可能误触发 |
| 太粗（所有任务一个 Skill） | 简单 | 失去按需加载优势 |
| **适中（按任务类型）** | 平衡精准性和管理成本 | - |

**推荐**：按任务类型划分（query/edit/generate）

---

## 八、常见问题

### Q1: Skills 没有被触发

**排查步骤**：
1. 检查 `setting_sources` 是否包含 `"project"`
2. 检查 `cwd` 是否指向包含 `skills/` 的目录
3. 检查 Skill 的 `description` 是否匹配用户请求

### Q2: 多个 Skills 同时触发

**解决方案**：
- 优化 `description`，使关键词更精确
- 使用显式模式指定 Skill

### Q3: 如何调试 Skill 内容

**方法**：
- 在 Skill 中添加"执行检查点"输出
- 使用 `--verbose` 模式查看 Skill 加载日志

---

## 九、版本历史

| 版本 | 日期 | 变更内容 |
|------|------|----------|
| v1.0 | 2026-01-15 | 初始版本：Skills 概念、架构设计、实现指南 |

---

## 十、附录

### A. 参考资料

- [Claude Agent SDK 官方文档](../docs/Agent_SDK/docs/)
- [Agent SDK 示例代码](../docs/Agent_SDK/examples/)
- [Flow_Git_Operations.md](./Flow_Git_Operations.md)

### B. 术语表

| 术语 | 说明 |
|------|------|
| Skill | 以 SKILL.md 文件形式定义的工作流指令包 |
| description | Skill 的触发条件描述，Claude 根据此字段匹配 |
| 渐进式披露 | 按需加载内容，而非一次性加载全部 |
| 混合模式 | 默认自主选择 Skill，必要时可显式指定 |
