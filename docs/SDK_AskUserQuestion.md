# AskUserQuestion 内置工具调研报告

> Claude Agent SDK 内置工具 `AskUserQuestion` 的数据结构、交互协议与集成方案调研。
> 目标：为 Web 端 AI 对话窗口实现选择控件提供技术基础。

---

## 1. 工具概述

`AskUserQuestion` 是 Claude Agent SDK 的**内置工具**（非 MCP 工具），允许 Claude 在执行过程中向用户提出澄清问题，获取关键决策信息后再继续执行。

**典型场景**：Claude 需要用户在多个方案中做选择时，不是用纯文本提问，而是通过结构化的选项卡让用户快速选择。

---

## 2. 数据结构

### 2.1 输入结构 (AskUserQuestionInput)

```typescript
interface AskUserQuestionInput {
  /**
   * 要提问的问题数组（1-4 个问题）
   */
  questions: Array<{
    /**
     * 完整问题文本，应清晰、具体，以问号结尾
     */
    question: string;

    /**
     * 显示为 chip/tag 的简短标签（最多 12 字符）
     * 示例: "Auth method", "Library", "Approach"
     */
    header: string;

    /**
     * 可用选项（2-4 个）。系统自动追加 "Other" 选项
     */
    options: Array<{
      /** 选项显示文本（1-5 个单词） */
      label: string;
      /** 选项含义说明 */
      description: string;
    }>;

    /**
     * true = 多选（Checkbox），false = 单选（Radio）
     */
    multiSelect: boolean;
  }>;

  /**
   * 用户答案（由权限系统填入）。
   * key = 问题文本，value = 选中的 label（多选用逗号分隔）
   */
  answers?: Record<string, string>;
}
```

### 2.2 输出结构 (AskUserQuestionOutput)

```typescript
interface AskUserQuestionOutput {
  /** 原样传回的问题数组 */
  questions: Array<{
    question: string;
    header: string;
    options: Array<{ label: string; description: string }>;
    multiSelect: boolean;
  }>;

  /** 用户答案。key = 问题文本，value = 答案字符串 */
  answers: Record<string, string>;
}
```

### 2.3 约束一览

| 项目 | 限制 |
|------|------|
| 问题数量 | 1-4 |
| 每题选项数 | 2-4（+ 自动 "Other"） |
| header 长度 | ≤ 12 字符 |
| label 长度 | 1-5 个单词 |
| 单选答案 | 单个 label 字符串 |
| 多选答案 | 逗号分隔字符串，如 `"Authentication, Caching"` |

---

## 3. 交互协议

### 3.1 调用流程

```
Claude 决策需要用户输入
  |
  v
调用内置工具 AskUserQuestion（携带 questions 数组）
  |
  v
SDK 拦截工具调用，触发 canUseTool 回调
  参数: toolName = "AskUserQuestion"
        input   = { questions: [...] }
        context = { signal, suggestions }
  |
  v
应用层处理回调（展示 UI → 收集用户选择）
  |
  v
返回 PermissionResultAllow(updated_input={ questions, answers })
  |
  v
SDK 将 answers 回传给 Claude
  |
  v
Claude 基于用户答案继续执行
```

### 3.2 关键接口

**canUseTool 回调签名（Python）：**

```python
async def can_use_tool(
    tool_name: str,           # "AskUserQuestion"
    tool_input: dict,         # { "questions": [...] }
    context: ToolPermissionContext
) -> PermissionResultAllow | PermissionResultDeny
```

**返回用户答案：**

```python
return PermissionResultAllow(
    updated_input={
        "questions": tool_input["questions"],   # 原样传回
        "answers": {
            "Which database should we use?": "PostgreSQL",
            "Which features?": "Authentication, Caching"  # 多选逗号分隔
        }
    }
)
```

**拒绝/取消：**

```python
return PermissionResultDeny(message="用户取消", interrupt=False)
```

### 3.3 示例数据

**Claude 发出的请求：**

```json
{
  "questions": [
    {
      "question": "Which database should we use?",
      "header": "Database",
      "options": [
        { "label": "PostgreSQL", "description": "Relational, ACID compliant" },
        { "label": "MongoDB", "description": "Document-based, flexible schema" }
      ],
      "multiSelect": false
    },
    {
      "question": "Which features should we enable?",
      "header": "Features",
      "options": [
        { "label": "Authentication", "description": "User login and sessions" },
        { "label": "Logging", "description": "Request and error logging" },
        { "label": "Caching", "description": "Redis-based response caching" }
      ],
      "multiSelect": true
    }
  ]
}
```

**应用层返回的答案：**

```json
{
  "questions": [ "...原样传回..." ],
  "answers": {
    "Which database should we use?": "PostgreSQL",
    "Which features should we enable?": "Authentication, Caching"
  }
}
```

---

## 4. 当前项目现状

### 4.1 Agent 端

**文件**：`BIMCanvas.Agent/src/agent/main_agent.py:232-244`

```python
can_use_tool=self._auto_approve_tool  # 当前：自动批准所有工具

async def _auto_approve_tool(self, tool_name, tool_input, context):
    return PermissionResultAllow()    # 无差别放行，不填 answers
```

**问题**：`_auto_approve_tool` 会直接放行 AskUserQuestion 但不填 `answers`，Claude 会收到空答案。

### 4.2 Web 端

尚无 AskUserQuestion 相关实现。

---

## 5. 集成方案设计

### 5.1 三层改动

| 层 | 职责 | 改动要点 |
|----|------|---------|
| **Agent** | 识别 + 转发 | `_auto_approve_tool` 中判断 `tool_name == "AskUserQuestion"`，将问题转发到 Server，异步等待用户答案 |
| **Server** | 中转 + 推送 | 通过 SignalR 将问题推送到 Web 前端，接收前端返回的答案后回传给 Agent |
| **Web** | 渲染 + 交互 | AI 对话窗口中渲染选择控件，收集答案后通过 SignalR 回传 |

### 5.2 前端控件设计要素

根据 SDK 数据结构，选择控件需支持：

- **header** → chip/badge 样式的分类标签
- **question** → 问题正文
- **options** → 选项列表，每项含 label + description
- **multiSelect=false** → 单选模式（Radio 按钮组 或 可点击卡片）
- **multiSelect=true** → 多选模式（Checkbox 组）
- **"Other" 选项** → 自动追加，点击后展开自由输入框
- **多问题** → 支持 1-4 个问题同时展示，每个问题独立选择
- **确认/取消** → 提交按钮发送所有答案，取消按钮触发 Deny

### 5.3 Agent 端改造思路

```python
async def _auto_approve_tool(self, tool_name, tool_input, context):
    if tool_name == "AskUserQuestion":
        # 1. 将 questions 通过 Server 推送到 Web
        # 2. 异步等待用户选择
        # 3. 将 answers 填入 updated_input 返回
        answers = await self._forward_question_to_user(tool_input["questions"])
        return PermissionResultAllow(updated_input={
            "questions": tool_input["questions"],
            "answers": answers
        })

    # 其他工具继续自动批准
    return PermissionResultAllow()
```

---

## 6. 已实现的文件

| 操作 | 文件路径 | 说明 |
|------|---------|------|
| 修改 | `BIMCanvas.Agent/src/server/http_server.py` | 新增 Question SSE/REST 端点 + `request_user_question` 函数 |
| 修改 | `BIMCanvas.Agent/src/agent/main_agent.py` | `_auto_approve_tool` 拦截 AskUserQuestion |
| 修改 | `BIMCanvas.Web/src/types/agent.ts` | 扩展 BubbleType + ChatBubble + 新增类型 |
| 修改 | `BIMCanvas.Web/src/utils/bubbleManager.ts` | 新增 `createQuestionBubble` |
| 修改 | `BIMCanvas.Web/src/components/UI/AICommandCenter.vue` | 集成 QuestionBubble + useQuestion |
| 新建 | `BIMCanvas.Web/src/services/QuestionService.ts` | 问题 SSE 监听 + 答案提交 |
| 新建 | `BIMCanvas.Web/src/composables/aiCommandCenter/useQuestion.ts` | 问题交互逻辑 |
| 新建 | `BIMCanvas.Web/src/components/UI/QuestionBubble.vue` | 选择控件组件 |

---

## 7. 参考资料

| 内容 | 文件路径 |
|------|---------|
| AskUserQuestionInput/Output 完整类型定义 | `docs/agent_sdk/docs/TypeScript SDK.md` (行 871-919, 1345-1365) |
| canUseTool 处理 AskUserQuestion 指南 | `docs/agent_sdk/docs/Guides/Handling Permissions.md` (行 364-418) |
| Python SDK 权限回调示例 | `docs/agent_sdk/examples/claude-agent-sdk-python/examples/tool_permission_callback.py` |
| Python SDK 类型定义 | `docs/agent_sdk/examples/claude-agent-sdk-python/src/claude_agent_sdk/types.py` |
| 官方 Excel Demo 工具展示组件 | `docs/agent_sdk/examples/claude-agent-sdk-demos/excel-demo/src/renderer/components/ToolUseDisplay.tsx` |
| 官方工具元数据定义 | `docs/agent_sdk/examples/claude-agent-sdk-demos/excel-demo/src/renderer/components/utils/toolMetadata.ts` |
