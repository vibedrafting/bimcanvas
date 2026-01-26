# Claude Code 中的 MCP 与 Skills 官方文档提取

> 从 `Claude测试请求日志4.json` 中提取的官方文档
>
> **文档来源**: Claude Code v2.1.17 的实际 API 请求日志
>
> **提取日期**: 2026-01-24

---

## 目录

1. [MCP (Model Context Protocol) 官方文档](#mcp-model-context-protocol-官方文档)
   - [系统提示词中的 MCP 介绍](#系统提示词中的-mcp-介绍)
   - [MCP 工具定义](#mcp-工具定义)
2. [Skills 官方文档](#skills-官方文档)
   - [Skill 工具完整定义](#skill-工具完整定义)
   - [可用 Skills 列表](#可用-skills-列表)

---

## MCP (Model Context Protocol) 官方文档

### 系统提示词中的 MCP 介绍

**位置**: `system[2].text` - Line 35

```markdown
# MCP Server Instructions

The following MCP servers have provided instructions for how to use their tools and resources:

## context7
Use this server to retrieve up-to-date documentation and code examples for any library.
```

**关键要点**:
- MCP Server 在系统提示词的第 3 个元素（`system[2]`）中引入
- 通过 `# MCP Server Instructions` 标题独立成节
- MCP Server 可以提供自定义的使用说明
- 示例中展示了 `context7` MCP Server，用于检索库文档和代码示例

---

### MCP 工具定义

Claude Code 通过 `tools` 数组引入 MCP 工具，工具名称使用 `mcp__<server>__<tool>` 命名规范。

#### 1. `mcp__context7__resolve-library-id`

**位置**: `tools[21]` - Lines 801-822

**完整定义**:

```json
{
  "name": "mcp__context7__resolve-library-id",
  "description": "Resolves a package/product name to a Context7-compatible library ID and returns matching libraries.\n\nYou MUST call this function before 'query-docs' to obtain a valid Context7-compatible library ID UNLESS the user explicitly provides a library ID in the format '/org/project' or '/org/project/version' in their query.\n\nSelection Process:\n1. Analyze the query to understand what library/package the user is looking for\n2. Return the most relevant match based on:\n- Name similarity to the query (exact matches prioritized)\n- Description relevance to the query's intent\n- Documentation coverage (prioritize libraries with higher Code Snippet counts)\n- Source reputation (consider libraries with High or Medium reputation more authoritative)\n- Benchmark Score: Quality indicator (100 is the highest score)\n\nResponse Format:\n- Return the selected library ID in a clearly marked section\n- Provide a brief explanation for why this library was chosen\n- If multiple good matches exist, acknowledge this but proceed with the most relevant one\n- If no good matches exist, clearly state this and suggest query refinements\n\nFor ambiguous queries, request clarification before proceeding with a best-guess match.\n\nIMPORTANT: Do not call this tool more than 3 times per question. If you cannot find what you need after 3 calls, use the best result you have.",
  "input_schema": {
    "type": "object",
    "properties": {
      "query": {
        "type": "string",
        "description": "The user's original question or task. This is used to rank library results by relevance to what the user is trying to accomplish. IMPORTANT: Do not include any sensitive or confidential information such as API keys, passwords, credentials, or personal data in your query."
      },
      "libraryName": {
        "type": "string",
        "description": "Library name to search for and retrieve a Context7-compatible library ID."
      }
    },
    "required": ["query", "libraryName"],
    "additionalProperties": false,
    "$schema": "http://json-schema.org/draft-07/schema#"
  }
}
```

**参数说明**:

| 参数 | 类型 | 必需 | 说明 |
|------|------|------|------|
| `query` | string | ✅ | 用户的原始问题或任务，用于按相关性排序库结果 |
| `libraryName` | string | ✅ | 要搜索的库名称，用于检索 Context7 兼容的库 ID |

**使用约束**:
- ⚠️ **调用次数限制**: 每个问题最多调用 3 次
- 🔐 **安全要求**: query 参数中不得包含敏感信息（API keys、密码、凭证、个人数据）
- 📋 **调用顺序**: 必须在 `query-docs` 之前调用（除非用户明确提供了 `/org/project` 格式的库 ID）

**选择逻辑**:
1. 分析查询以理解用户要找的库/包
2. 基于以下标准返回最相关的匹配：
   - 名称相似度（优先精确匹配）
   - 描述与查询意图的相关性
   - 文档覆盖度（优先代码片段数量多的库）
   - 来源声誉（优先 High/Medium 声誉的库）
   - Benchmark Score（质量指标，满分 100）

---

#### 2. `mcp__context7__query-docs`

**位置**: `tools[22]` - Lines 824-845

**完整定义**:

```json
{
  "name": "mcp__context7__query-docs",
  "description": "Retrieves and queries up-to-date documentation and code examples from Context7 for any programming library or framework.\n\nYou must call 'resolve-library-id' first to obtain the exact Context7-compatible library ID required to use this tool, UNLESS the user explicitly provides a library ID in the format '/org/project' or '/org/project/version' in their query.\n\nIMPORTANT: Do not call this tool more than 3 times per question. If you cannot find what you need after 3 calls, use the best information you have.",
  "input_schema": {
    "type": "object",
    "properties": {
      "libraryId": {
        "type": "string",
        "description": "Exact Context7-compatible library ID (e.g., '/mongodb/docs', '/vercel/next.js', '/supabase/supabase', '/vercel/next.js/v14.3.0-canary.87') retrieved from 'resolve-library-id' or directly from user query in the format '/org/project' or '/org/project/version'."
      },
      "query": {
        "type": "string",
        "description": "The question or task you need help with. Be specific and include relevant details. Good: 'How to set up authentication with JWT in Express.js' or 'React useEffect cleanup function examples'. Bad: 'auth' or 'hooks'. IMPORTANT: Do not include any sensitive or confidential information such as API keys, passwords, credentials, or personal data in your query."
      }
    },
    "required": ["libraryId", "query"],
    "additionalProperties": false,
    "$schema": "http://json-schema.org/draft-07/schema#"
  }
}
```

**参数说明**:

| 参数 | 类型 | 必需 | 说明 |
|------|------|------|------|
| `libraryId` | string | ✅ | Context7 兼容的精确库 ID（从 `resolve-library-id` 获取或用户提供） |
| `query` | string | ✅ | 需要帮助的问题或任务，应具体且包含相关细节 |

**使用约束**:
- ⚠️ **调用次数限制**: 每个问题最多调用 3 次
- 🔐 **安全要求**: query 参数中不得包含敏感信息
- 📋 **前置条件**: 必须先调用 `resolve-library-id` 获取 `libraryId`（除非用户明确提供）

**库 ID 格式示例**:
- `/mongodb/docs`
- `/vercel/next.js`
- `/supabase/supabase`
- `/vercel/next.js/v14.3.0-canary.87` (带版本号)

**Query 参数最佳实践**:
- ✅ **好的查询**: `"How to set up authentication with JWT in Express.js"`
- ✅ **好的查询**: `"React useEffect cleanup function examples"`
- ❌ **差的查询**: `"auth"`（太宽泛）
- ❌ **差的查询**: `"hooks"`（太模糊）

---

### MCP 工具命名规范

从日志中提取的 MCP 工具命名模式：

```
mcp__<server_name>__<tool_name>
```

**示例**:
- `mcp__context7__resolve-library-id` → Server: `context7`, Tool: `resolve-library-id`
- `mcp__context7__query-docs` → Server: `context7`, Tool: `query-docs`

---

## Skills 官方文档

### Skill 工具完整定义

**位置**: `tools[11]` - Lines 641-661

**完整定义**:

```json
{
  "name": "Skill",
  "description": "Execute a skill within the main conversation\n\nWhen users ask you to perform tasks, check if any of the available skills below can help complete the task more effectively. Skills provide specialized capabilities and domain knowledge.\n\nWhen users ask you to run a \"slash command\" or reference \"/<something>\" (e.g., \"/commit\", \"/review-pr\"), they are referring to a skill. Use this tool to invoke the corresponding skill.\n\nExample:\n  User: \"run /commit\"\n  Assistant: [Calls Skill tool with skill: \"commit\"]\n\nHow to invoke:\n- Use this tool with the skill name and optional arguments\n- Examples:\n  - `skill: \"pdf\"` - invoke the pdf skill\n  - `skill: \"commit\", args: \"-m 'Fix bug'\"` - invoke with arguments\n  - `skill: \"review-pr\", args: \"123\"` - invoke with arguments\n  - `skill: \"ms-office-suite:pdf\"` - invoke using fully qualified name\n\nImportant:\n- When a skill is relevant, you must invoke this tool IMMEDIATELY as your first action\n- NEVER just announce or mention a skill in your text response without actually calling this tool\n- This is a BLOCKING REQUIREMENT: invoke the relevant Skill tool BEFORE generating any other response about the task\n- Only use skills listed in \"Available skills\" below\n- Do not invoke a skill that is already running\n- Do not use this tool for built-in CLI commands (like /help, /clear, etc.)\n- If you see a <command-name> tag in the current conversation turn (e.g., <command-name>/commit</command-name>), the skill has ALREADY been loaded and its instructions follow in the next message. Do NOT call this tool - just follow the skill instructions directly.\n\nAvailable skills:\n- docx: Comprehensive document creation, editing, and analysis with support for tracked changes, comments, formatting preservation, and text extraction. When Claude needs to work with professional documents (.docx files) for: (1) Creating new documents, (2) Modifying or editing content, (3) Working with tracked changes, (4) Adding comments, or any other document tasks\n- furniture-svg: 生成建筑平面图家具SVG缩略图。当需要根据家具名称和尺寸(宽x深,单位mm)生成极简风格的俯视图SVG图块时使用此技能。适用于:(1) 创建新家具图块,(2) 批量生成家具库SVG,(3) 生成符合CAD风格的矢量家具图。\n- pdf: Comprehensive PDF manipulation toolkit for extracting text and tables, creating new PDFs, merging/splitting documents, and handling forms. When Claude needs to fill in a PDF form or programmatically process, generate, or analyze PDF documents at scale.\n- pptx: Presentation creation, editing, and analysis. When Claude needs to work with presentations (.pptx files) for: (1) Creating new presentations, (2) Modifying or editing content, (3) Working with layouts, (4) Adding comments or speaker notes, or any other presentation tasks\n- skill-creator: Guide for creating effective skills. This skill should be used when users want to create a new skill (or update an existing skill) that extends Claude's capabilities with specialized knowledge, workflows, or tool integrations.\n- xlsx: Comprehensive spreadsheet creation, editing, and analysis with support for formulas, formatting, data analysis, and visualization. When Claude needs to work with spreadsheets (.xlsx, .xlsm, .csv, .tsv, etc) for: (1) Creating new spreadsheets with formulas and formatting, (2) Reading or analyzing data, (3) Modify existing spreadsheets while preserving formulas, (4) Data analysis and visualization in spreadsheets, or (5) Recalculating formulas\n- git:worktree: 基于 $ARGUMENTS 创建并行开发环境:\n- git:merge-worktree: 合并并清理指定的worktree:\n- git:merge-branch: 安全合并当前临时分支到约定基线分支:\n- git:commit: 根据当前工作区状态和开发上下文,生成合适的中文git commit信息并提交,提交主要目的是 $ARGUMENTS 。\n- doc:update: 检查 $ARGUMENTS 相关代码修改后文档更新需求并执行:\n- doc:save: 判断 $ARGUMENTS 相关知识是否需要知识积累并执行:\n- doc:read: 主动查阅 $ARGUMENTS 相关知识,优先复用已有经验:\n",
  "input_schema": {
    "$schema": "https://json-schema.org/draft/2020-12/schema",
    "type": "object",
    "properties": {
      "skill": {
        "description": "The skill name. E.g., \"commit\", \"review-pr\", or \"pdf\"",
        "type": "string"
      },
      "args": {
        "description": "Optional arguments for the skill",
        "type": "string"
      }
    },
    "required": ["skill"],
    "additionalProperties": false
  }
}
```

---

### Skills 调用机制

#### 1. 用户表达方式

用户可以通过以下方式请求 Skill：

| 表达方式 | 示例 | 说明 |
|----------|------|------|
| Slash Command | `run /commit` | 用户明确使用 `/skill-name` 格式 |
| 引用 | `/review-pr` | 直接引用 Skill 名称 |
| 自然语言 | "帮我提交代码" | AI 判断应使用 `git:commit` Skill |

#### 2. 调用示例

```javascript
// 基本调用
{ skill: "pdf" }

// 带参数调用
{ skill: "commit", args: "-m 'Fix bug'" }
{ skill: "review-pr", args: "123" }

// 完全限定名调用
{ skill: "ms-office-suite:pdf" }
```

#### 3. 关键约束

⚠️ **立即调用要求**:
- 当 Skill 相关时，必须**立即调用此工具**作为第一个动作
- **绝不**仅在文本响应中提及 Skill 而不实际调用
- 这是**阻塞性要求**：在生成任何其他响应之前调用相关 Skill 工具

🚫 **禁止操作**:
- 不要调用已在运行的 Skill
- 不要将此工具用于内置 CLI 命令（如 `/help`、`/clear`）
- 如果看到 `<command-name>` 标签，说明 Skill 已加载，直接遵循指令即可

📋 **仅使用列表中的 Skills**:
- 只使用 "Available skills" 中列出的 Skill
- 不要猜测或发明新的 Skill 名称

---

### 可用 Skills 列表

从日志中提取的完整 Skills 列表及其说明：

#### 办公文档类 Skills

##### 1. `docx` - Word 文档处理

**功能**:
- 创建新文档
- 修改或编辑内容
- 处理修订追踪（tracked changes）
- 添加评论
- 格式保留
- 文本提取

**使用场景**:
- 创建专业文档（.docx 文件）
- 编辑现有文档内容
- 处理文档修订和评论

---

##### 2. `pdf` - PDF 文档处理

**功能**:
- 提取文本和表格
- 创建新 PDF
- 合并/拆分文档
- 处理表单填写

**使用场景**:
- 填写 PDF 表单
- 编程方式处理、生成或分析 PDF 文档
- 批量 PDF 操作

---

##### 3. `pptx` - PowerPoint 演示文稿处理

**功能**:
- 创建新演示文稿
- 修改或编辑内容
- 处理布局
- 添加评论或演讲备注

**使用场景**:
- 创建/编辑 .pptx 文件
- 演示文稿内容管理
- 批注和备注管理

---

##### 4. `xlsx` - Excel 表格处理

**功能**:
- 创建带公式和格式的新表格
- 读取或分析数据
- 修改现有表格（保留公式）
- 数据分析和可视化
- 重新计算公式

**支持格式**: .xlsx, .xlsm, .csv, .tsv

**使用场景**:
1. 创建带公式和格式的新表格
2. 读取或分析数据
3. 修改现有表格（保留公式）
4. 数据分析和可视化
5. 重新计算公式

---

#### Git 工作流 Skills

##### 5. `git:worktree` - Git Worktree 管理

**功能**: 基于 `$ARGUMENTS` 创建并行开发环境

**说明**: 使用 Git worktree 功能创建独立的工作目录，允许在同一仓库的不同分支上并行工作

---

##### 6. `git:merge-worktree` - Worktree 合并

**功能**: 合并并清理指定的 worktree

**说明**: 合并 worktree 的更改并清理临时工作目录

---

##### 7. `git:merge-branch` - 分支合并

**功能**: 安全合并当前临时分支到约定基线分支

**说明**: 按照约定的工作流安全地将特性分支合并到主分支

---

##### 8. `git:commit` - 智能提交

**功能**: 根据当前工作区状态和开发上下文，生成合适的中文 git commit 信息并提交

**参数**: 提交主要目的是 `$ARGUMENTS`

**说明**:
- 自动分析工作区变更
- 生成符合规范的中文 commit 信息
- 支持通过参数指定提交目的

---

#### 文档管理 Skills

##### 9. `doc:read` - 知识查阅

**功能**: 主动查阅 `$ARGUMENTS` 相关知识，优先复用已有经验

**说明**:
- 从项目文档中检索相关知识
- 优先使用已记录的最佳实践
- 避免重复踩坑

---

##### 10. `doc:save` - 知识积累

**功能**: 判断 `$ARGUMENTS` 相关知识是否需要知识积累并执行

**说明**:
- 评估是否需要将新知识记录到文档
- 自动更新项目知识库
- 积累最佳实践和解决方案

---

##### 11. `doc:update` - 文档更新

**功能**: 检查 `$ARGUMENTS` 相关代码修改后文档更新需求并执行

**说明**:
- 代码变更后自动检查文档一致性
- 识别需要更新的文档部分
- 执行文档同步更新

---

#### 专用工具 Skills

##### 12. `furniture-svg` - 家具 SVG 生成（自定义）

**功能**: 生成建筑平面图家具 SVG 缩略图

**输入**: 家具名称和尺寸（宽x深，单位 mm）

**输出**: 极简风格的俯视图 SVG 图块

**使用场景**:
1. 创建新家具图块
2. 批量生成家具库 SVG
3. 生成符合 CAD 风格的矢量家具图

---

##### 13. `skill-creator` - Skill 创建器

**功能**: 创建有效 Skills 的指南

**使用场景**: 当用户想创建新 Skill 或更新现有 Skill 时使用，帮助扩展 Claude 的能力

**说明**: 提供 Skill 创建的最佳实践和模板

---

### Skills 参数传递机制

#### `$ARGUMENTS` 变量

部分 Skills（如 Git 和文档管理类）使用 `$ARGUMENTS` 变量接收参数：

```bash
# git:commit 示例
Skill(skill: "git:commit", args: "修复登录bug")
→ 生成提交信息: "修复: 修复登录bug"

# doc:read 示例
Skill(skill: "doc:read", args: "用户认证流程")
→ 查阅项目中关于用户认证流程的文档

# git:worktree 示例
Skill(skill: "git:worktree", args: "feature/new-login")
→ 基于 feature/new-login 创建新的 worktree
```

---

### Skills 加载状态检测

Claude Code 会通过 `<command-name>` 标签指示 Skill 的加载状态：

```xml
<!-- Skill 已加载 -->
<command-name>/commit</command-name>
```

**处理逻辑**:
- ✅ 看到 `<command-name>` 标签 → Skill 已加载，直接遵循后续指令
- ❌ 未看到标签 → 使用 Skill 工具调用该 Skill

---

## 总结对比

### MCP vs Skills

| 维度 | MCP Tools | Skills |
|------|-----------|--------|
| **用途** | 外部服务集成（如文档查询） | 内置专用工作流（如文档处理、Git 操作） |
| **命名** | `mcp__<server>__<tool>` | 短名称（如 `pdf`、`git:commit`） |
| **参数** | 严格的 JSON Schema | 字符串参数（args）+ 变量替换 |
| **调用方式** | 直接工具调用 | 通过 Skill 工具调用 |
| **用户触发** | 隐式（AI 判断） | Slash 命令（`/commit`）或自然语言 |
| **扩展性** | 通过 MCP Server 添加 | 通过 skill-creator 创建 |

---

### 关键设计模式

1. **MCP 的两阶段查询模式**:
   ```
   resolve-library-id → query-docs
   ```
   先解析库 ID，再查询文档

2. **Skills 的参数替换模式**:
   ```
   Skill(skill: "git:commit", args: "修复bug")
   → $ARGUMENTS 被替换为 "修复bug"
   ```

3. **调用次数限制**:
   - MCP 工具：每个问题最多 3 次（防止滥用）
   - Skills：无明确限制（但有状态检测机制）

---

## 实际应用示例

### MCP 工具调用流程

```javascript
// 步骤 1: 解析库 ID
mcp__context7__resolve-library-id({
  query: "我想学习 React Hooks",
  libraryName: "react"
})
// 返回: { libraryId: "/facebook/react" }

// 步骤 2: 查询文档
mcp__context7__query-docs({
  libraryId: "/facebook/react",
  query: "React useEffect cleanup function examples"
})
// 返回: 详细的文档和代码示例
```

### Skills 调用流程

```javascript
// 用户输入: "帮我提交代码，修复了登录bug"

// AI 识别到需要使用 git:commit Skill
Skill({
  skill: "git:commit",
  args: "修复登录bug"
})

// Skill 执行:
// 1. 分析工作区状态 (git status, git diff)
// 2. 生成 commit 信息: "修复: 修复登录bug相关代码"
// 3. 执行 git commit
```

---

## 附录：完整 JSON Schema

### Skill 工具 Input Schema

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "type": "object",
  "properties": {
    "skill": {
      "description": "The skill name. E.g., \"commit\", \"review-pr\", or \"pdf\"",
      "type": "string"
    },
    "args": {
      "description": "Optional arguments for the skill",
      "type": "string"
    }
  },
  "required": ["skill"],
  "additionalProperties": false
}
```

### MCP resolve-library-id Input Schema

```json
{
  "type": "object",
  "properties": {
    "query": {
      "type": "string",
      "description": "The user's original question or task..."
    },
    "libraryName": {
      "type": "string",
      "description": "Library name to search for..."
    }
  },
  "required": ["query", "libraryName"],
  "additionalProperties": false,
  "$schema": "http://json-schema.org/draft-07/schema#"
}
```

### MCP query-docs Input Schema

```json
{
  "type": "object",
  "properties": {
    "libraryId": {
      "type": "string",
      "description": "Exact Context7-compatible library ID..."
    },
    "query": {
      "type": "string",
      "description": "The question or task you need help with..."
    }
  },
  "required": ["libraryId", "query"],
  "additionalProperties": false,
  "$schema": "http://json-schema.org/draft-07/schema#"
}
```

---

## 文档版本信息

- **提取自**: `Claude测试请求日志4.json`
- **Claude Code 版本**: v2.1.17
- **请求模型**: claude-sonnet-4-5-20250929
- **提取日期**: 2026-01-24
- **文档作者**: Claude Code Assistant
