# Claude Code 系统提示词深度解读

> 目标：理解 Claude Code 的提示词设计模式，为 BIMCanvas MainAgent 设计提示词提供参考

---

## 📖 提示词整体结构

Claude Code 的提示词遵循清晰的层次结构：

```
1. 身份定义（Identity）
2. 安全边界（Safety Guardrails）
3. 交互风格（Tone & Style）
4. 行为准则（Behavioral Guidelines）
5. 任务执行规范（Task Execution）
6. 工具使用策略（Tool Usage Policy）
7. 环境信息（Runtime Context）
```

---

## 🔍 逐段深度分析

### 第 1 段：身份定义

```
You are an interactive CLI tool that helps users with software engineering tasks.
Use the instructions below and the tools available to you to assist the user.
```

**设计意图**：
- **简洁的角色定位**：一句话说明"我是谁"和"我做什么"
- **工具导向**：强调使用"tools available to you"，暗示这是一个工具驱动的 Agent
- **服务姿态**："assist the user"明确了辅助而非主导的定位

**对 BIMCanvas 的启发**：
```
你是 BIMCanvas 的主控 Agent，负责在建筑平面内智能布置符合设计逻辑的家具组合。
使用以下说明和 MCP 工具来完成用户的布置任务。
```

---

### 第 2 段：安全边界（重复两次！）

```
IMPORTANT: Assist with authorized security testing... Refuse requests for
destructive techniques, DoS attacks...
IMPORTANT: You must NEVER generate or guess URLs...
```

**设计意图**：
- **IMPORTANT 前缀**：信号词，让模型注意这是硬性约束
- **正面 + 反面表述**：先说"可以做什么"（authorized security testing），再说"不能做什么"（destructive techniques）
- **具体化**：不说"危险操作"这种模糊词，而是明确列出"DoS attacks, mass targeting, supply chain compromise"
- **末尾再次重复**：同样的安全规则在第 61 行再次出现，强化记忆

**设计模式**：`允许的场景 → 禁止的场景 → 例外条件`

**对 BIMCanvas 的启发**：
```
重要约束：你只能操作 modules[]（家具模块），不能修改 baseline/（建筑数据）。
你可以：读取房间边界、查询禁区、放置/移动/删除家具
你不能：修改墙体、门窗、房间边界等建筑基础数据
```

---

### 第 3 段：帮助与反馈入口

```
If the user asks for help or wants to give feedback inform them of the following:
- /help: Get help with using Claude Code
- To give feedback, users should report the issue at https://github.com/...
```

**设计意图**：
- **兜底机制**：当用户不知道怎么用时，提供明确出口
- **分流**：帮助走 /help，反馈走 GitHub Issues

**对 BIMCanvas 的启发**：
```
如果用户不确定如何操作：
- 输入 /help 查看可用命令
- 输入 /tags 查看房间标签说明
```

---

### 第 4 段：语气和风格（Tone & Style）

```
- Only use emojis if the user explicitly requests it.
- Your output will be displayed on a command line interface.
  Your responses should be short and concise.
- Output text to communicate with the user; all text you output outside
  of tool use is displayed to the user.
- NEVER create files unless they're absolutely necessary...
- Do not use a colon before tool calls.
```

**设计意图**：

| 规则 | 为什么这么写 |
|------|-------------|
| 禁止 emoji | CLI 环境下 emoji 可能显示异常，且显得不专业 |
| 短而精炼 | CLI 屏幕空间有限，长文本体验差 |
| 文本 vs 工具分离 | 避免用 `echo` 或代码注释和用户对话 |
| 优先编辑不新建 | 减少代码库膨胀，符合开发最佳实践 |
| 工具调用前不加冒号 | "让我读取文件："后面如果工具调用不显示，用户只看到冒号很奇怪 |

**这是"输出格式规范"的典型写法**：`渲染环境限制 → 格式要求 → 禁止事项`

---

### 第 5 段：专业客观性（Professional Objectivity）

```
Prioritize technical accuracy and truthfulness over validating the user's beliefs.
Focus on facts and problem-solving...
Avoid using over-the-top validation or excessive praise...
```

**设计意图**：
- **反对"讨好型"回复**：禁止"你说得对！"这种无意义认同
- **技术准确优先**：宁可得罪用户，也要给出正确答案
- **调查优先于假设**：不确定时先查证，而非附和

**这是 Anthropic 的价值观体现**：诚实 > 讨好

**对 BIMCanvas 的启发**：
```
当布置方案与用户期望冲突时，优先解释设计规范约束，而非强行满足不合理需求。
如果用户要求将床放在卫生间，应拒绝并说明原因，而非盲目执行。
```

---

### 第 6 段：不给时间估计（No Time Estimates）

```
Never give time estimates or predictions for how long tasks will take...
Avoid phrases like "this will take me a few minutes"...
```

**设计意图**：
- **避免承诺无法兑现**：AI 执行时间不可预测
- **避免用户焦虑**：说"5分钟"结果10分钟没完成，体验很差
- **专注于"做什么"而非"多久"**

**对 BIMCanvas 的启发**：
```
不要说"布置客厅需要几秒钟"，而是说"正在分析房间边界和禁区..."
```

---

### 第 7 段：工作时提问（Asking Questions）

```
You have access to the AskUserQuestion tool to ask the user questions
when you need clarification...
```

**设计意图**：
- **明确提问工具**：不是随便输出问号就行，要用专门的工具
- **提问时机**：需要澄清、验证假设、做决定时
- **Hooks 机制**：用户可以配置钩子脚本，Agent 要尊重钩子反馈

---

### 第 8 段：任务执行规范（Doing Tasks）- 这是最核心的部分！

```
- NEVER propose changes to code you haven't read.
- Use the AskUserQuestion tool to ask questions...
- Be careful not to introduce security vulnerabilities...
- Avoid over-engineering. Only make changes that are directly requested...
```

**设计意图拆解**：

#### 8.1 先读后改原则
```
NEVER propose changes to code you haven't read.
```
**为什么**：避免 AI 凭猜测修改代码，造成破坏

#### 8.2 反过度工程（这部分写得极其详细！）
```
- Don't add features, refactor code, or make "improvements" beyond what was asked.
- Don't add error handling, fallbacks, or validation for scenarios that can't happen.
- Don't create helpers, utilities, or abstractions for one-time operations.
```

**为什么这么详细**：
- AI 天然有"过度工程化"倾向（因为训练数据里好代码都有完善的错误处理）
- 必须明确告诉它：**不要画蛇添足**
- "三行相似代码比过早抽象好" 这句话是精髓

**对 BIMCanvas 的启发**：
```
只执行用户明确要求的布置操作：
- 用户说"放一张床"，就只放一张床，不要自动添加床头柜
- 用户说"布置客厅"，才可以规划完整家具组合
- 不要为"未来可能的需求"预留复杂逻辑
```

---

### 第 9 段：工具使用策略（Tool Usage Policy）- 高度实用！

```
- When doing file search, prefer to use the Task tool...
- You can call multiple tools in a single response...
- Use specialized tools instead of bash commands when possible...
- VERY IMPORTANT: When exploring the codebase... use Task tool with subagent_type=Explore
```

**设计意图拆解**：

| 规则 | 原因 |
|------|------|
| 文件搜索用 Task 工具 | 减少上下文消耗 |
| 无依赖的工具并行调用 | 提高效率 |
| 专用工具 > bash 命令 | 更好的用户体验 + 安全 |
| 探索代码库用 Explore Agent | 避免主 Agent 上下文膨胀 |

**示例写法非常好**：
```xml
<example>
user: Where are errors from the client handled?
assistant: [Uses the Task tool with subagent_type=Explore...]
</example>
```

**对 BIMCanvas 的启发**：
```
工具使用优先级：
1. 查询类操作：直接调用 get_room_zones、get_exclusions 等 MCP 工具
2. 复杂探索：使用 SubAgent 搜集信息后汇总
3. 批量操作：使用事务模式，一次提交多个变更
```

---

### 第 10 段：代码引用格式

```
When referencing specific functions or pieces of code include the pattern
`file_path:line_number` to allow the user to easily navigate...
```

**设计意图**：
- **可点击跳转**：IDE 集成时可以直接跳转到代码位置
- **精确定位**：避免"在某个文件里"这种模糊表述

---

### 第 11 段：环境信息

```xml
<env>
Working directory: C:\\Users\\huhaonan
Is directory a git repo: No
Platform: win32
Today's date: 2026-02-02
</env>
```

**设计意图**：
- **动态注入**：每次会话时自动填充当前环境
- **平台感知**：让 AI 知道用 Windows 命令还是 Unix 命令
- **时间感知**：知道"今天"是哪天，避免过时信息

---

## 🎯 设计模式总结

### 1. 层次化结构
```
身份 → 边界 → 风格 → 行为 → 任务 → 工具 → 环境
```

### 2. 正反面表述
```
你可以... / 你不能...
优先做... / 避免做...
```

### 3. 具体化而非抽象
```
❌ "不要做危险操作"
✅ "不要做 DoS 攻击、供应链入侵、大规模攻击"
```

### 4. 重复强调重要规则
安全规则出现了 2 次，关键工具使用规则用 "VERY IMPORTANT" 标注

### 5. 示例驱动
使用 `<example>` 标签展示正确行为

---

## ❓待讨论问题

在为 BIMCanvas MainAgent 设计提示词时，我们需要确定：

1. **角色定位**：MainAgent 是"设计师"还是"助手"？
2. **安全边界**：哪些操作需要明确禁止？
3. **交互风格**：面向开发者调试还是最终用户？
4. **工具优先级**：MCP 工具的使用顺序？
5. **反过度工程**：如何避免 AI 过度布置？

---

## 下一步

1. 确认以上分析是否清晰
2. 讨论 BIMCanvas MainAgent 的特定需求
3. 起草 MainAgent 系统提示词初稿
