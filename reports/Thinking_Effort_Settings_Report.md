# Thinking/Effort 设置生效问题探索报告

> **日期**：2026-02-27
> **范围**：Web UI → Agent 后端 → API 代理的完整数据流
> **结论**：后端修复成功，Thinking 设置正确传递至 API；Effort 不透传 API，仅影响 CLI 内部编排

---

## 1. 问题描述

用户在 Web 端切换 Thinking/Effort 设置后不生效。无论前端如何配置，API 请求始终使用默认值。

---

## 2. 根因分析

### 2.1 后端预连接导致设置被忽略

**文件**：`BIMCanvas.Agent/src/server/http_server.py`

`get_agent()` 函数在创建 Agent 后立即调用 `await agent.connect()`（第 71 行），不带任何参数，使用 `config.json` 中的默认值预连接。

```python
# 修复前
agent = MainAgent(project_path, working_directory=working_dir, window_seq=seq)
await agent.connect()  # 预连接 — 使用默认值，忽略用户设置
```

之后 `chat_stream()` 中的延迟连接逻辑（`main_agent.py:637-640`）因 `self._connected=True` 被跳过，用户通过 Web UI 设置的 effort/thinking 参数从未被应用。

```python
# main_agent.py chat_stream()
async def chat_stream(self, user_message, images=None, effort=None, thinking=None, ...):
    if not self._connected:
        await self.connect(effort=effort, thinking=thinking)  # 永远不会执行
```

### 2.2 修复方案

**删除预连接调用**（一行改动）：

```python
# 修复后
agent = MainAgent(project_path, working_directory=working_dir, window_seq=seq)
# 不再预连接，等待首条消息时带参数连接
```

`chat_stream()` 已有延迟连接逻辑，会在用户发送首条消息时带着当前的 effort/thinking 参数调用 `connect()`。

---

## 3. 完整数据流追踪

### 3.1 端到端流程

```
Web UI 设置
    ↓ (用户选择 Thinking/Effort 级别)
POST /api/chat/stream
    { message, effort: "max", thinking: "adaptive" }
    ↓
http_server.py: chat_stream_handler()
    提取 effort, thinking 参数
    ↓
main_agent.py: chat_stream()
    if not self._connected:
        await self.connect(effort, thinking)
    ↓
main_agent.py: _create_options()
    effort  → sdk_effort (直传或 None)
    thinking → ThinkingConfigAdaptive / ThinkingConfigDisabled
    ↓
ClaudeAgentOptions → Agent SDK → SubprocessCLITransport
    --effort max --max-thinking-tokens 32000
    ↓
Claude Code CLI → API 代理请求
    thinking: {type:"enabled", budget_tokens:31999}
```

### 3.2 参数转换规则

#### Thinking 转换（`main_agent.py:178-184`）

| Web UI | Python 代码 | CLI 参数 | API 请求体 |
|--------|------------|----------|-----------|
| Adaptive | `ThinkingConfigAdaptive(type="adaptive")` | `--max-thinking-tokens 32000` | `thinking: {type:"enabled", budget_tokens:31999}` |
| Off | `ThinkingConfigDisabled(type="disabled")` | `--max-thinking-tokens 0` | 不发送 `thinking` 字段 |

#### Effort 转换（`main_agent.py:176-177`）

| Web UI | Python 代码 | CLI 参数 | API 请求体 |
|--------|------------|----------|-----------|
| Off | `sdk_effort = None` | 不传 `--effort` | 无 |
| Low | `sdk_effort = "low"` | `--effort low` | 无 |
| Medium | `sdk_effort = "medium"` | `--effort medium` | 无 |
| High | `sdk_effort = "high"` | `--effort high` | 无 |
| Max | `sdk_effort = "max"` | `--effort max` | 无 |

**关键发现**：Effort 不是 Anthropic Messages API 的直接参数，仅作为 CLI 命令行参数影响 Claude Code CLI 的内部编排行为（决策轮数、工具调用策略等），不会体现在最终的 API 请求体中。

---

## 4. 抓包验证

### 4.1 测试一：Effort=Off, Thinking=Adaptive

**API 请求关键字段**：
```json
{
  "max_tokens": 32000,
  "thinking": {
    "budget_tokens": 31999,
    "type": "enabled"
  },
  "stream": true
}
```

- `thinking` 字段存在且为 `enabled` → Adaptive 设置生效
- 无 `effort` 字段 → Off 映射为 None，不发送
- 无 `temperature` 字段 → thinking 和 temperature 互斥

### 4.2 测试二：Effort=Max, Thinking=Off

**API 请求关键字段**：
```json
{
  "max_tokens": 32000,
  "temperature": 1,
  "stream": true
}
```

- 无 `thinking` 字段 → Off 设置生效（对比测试一有明显差异）
- 无 `effort` 字段 → Max 不透传 API（CLI 内部处理）
- `temperature: 1` 出现 → thinking 关闭时 CLI 可设置 temperature

### 4.3 验证结论

| 设置项 | 是否正确传递至 API | 备注 |
|--------|-------------------|------|
| Thinking=Adaptive | 是 | `thinking: {type:"enabled", budget_tokens:31999}` |
| Thinking=Off | 是 | 不发送 thinking 字段 |
| Effort=任意值 | 否（设计如此） | Effort 仅影响 CLI 编排，非 API 参数 |

---

## 5. 前端 UI 改进

### 5.1 首条消息后禁用控件

由于 Thinking/Effort 仅在首次 `connect()` 时生效，不支持动态调整，因此在发送首条消息后禁用这两个控件，避免用户误操作。

**文件**：`BIMCanvas.Web/src/components/UI/AICommandCenter.vue`

**改动**：
1. 添加计算属性判断是否已有用户消息：
   ```typescript
   const isConfigLocked = computed(() => chatMessages.value.some(m => m.role === 'user'));
   ```

2. Effort/Thinking Pill 按钮增加 disabled 状态：
   ```html
   <div class="control-pill-wrapper effort" :class="{ disabled: isConfigLocked }">
       <button class="control-pill" :disabled="isConfigLocked">
   ```

3. 禁用样式（降低透明度，禁止光标）：
   ```scss
   .control-pill-wrapper.disabled {
       .control-pill {
           opacity: 0.35;
           cursor: not-allowed;
       }
   }
   ```

**注意**：判断条件使用 `m.role === 'user'` 而非 `messages.length > 0`，排除 AI 欢迎消息的干扰。

---

## 6. 对自定义 API 代理的影响

本项目使用自定义 API 代理（将 Anthropic 格式请求转发至 Gemini 模型），而非直连 Anthropic 官方 API。因此需要评估两个设置项在代理场景下的实际意义。

### 6.1 Thinking — 有意义（部分生效，需代理配合）

**当前状态**：设置确实体现在 API 请求体中，代理能收到该字段。

- **Thinking=Adaptive** 时，请求体包含：
  ```json
  "thinking": { "type": "enabled", "budget_tokens": 31999 }
  ```
- **Thinking=Off** 时，请求体**不包含** `thinking` 字段

两种配置在 API 请求层面有明确差异，代理完全有能力根据 `thinking` 字段的有无来决定是否启用模型的思考能力。

**当前问题**：API 代理（gemini-pro → gemini-3.1-pro-high）当前**忽略了 `thinking` 字段**，无论是否发送，始终返回 thinking block。可能原因是代理仅根据请求头 `anthropic-beta: interleaved-thinking-2025-05-14` 来判断是否启用思考，而该头由 Claude Code CLI 始终携带。

**结论**：Thinking 控件有保留价值。如需生效，代理层需增加对请求体中 `thinking` 字段的判断逻辑：有该字段且 `type` 为 `"enabled"` 时启用思考，无该字段时禁用思考。

### 6.2 Effort — 对自定义代理无意义

**当前状态**：Effort 设置**不体现在 API 请求体中**，代理完全无法感知。

Effort 参数的传递路径在 CLI 层终止：
```
Web UI (effort="max") → Agent SDK → CLI 命令行 --effort max → [CLI 内部消化，不透传 API]
```

Effort 仅影响 Claude Code CLI 的内部编排行为，包括：
- 决策轮数（低 effort 更快结束，高 effort 更多轮推理）
- 工具调用策略（是否主动探索更多可能性）
- 自我反思深度

这些编排策略的设计对象是 Claude 模型。当代理背后是 Gemini 模型时，CLI 的编排策略对 Gemini 的推理深度没有控制力——CLI 只能控制"调用几轮"，无法改变 Gemini 每轮回复的思考深度。

**结论**：Effort 控件在自定义代理场景下无实际作用，可考虑：
- 直接隐藏该控件
- 或标注为"仅限 Claude 官方 API"，避免用户困惑

### 6.3 总结

| 设置项 | API 请求体可见 | 代理可感知 | 代理可控制 | 当前生效 |
|--------|--------------|-----------|-----------|---------|
| **Thinking** | 是 | 是 | 是（需改造） | 否（代理忽略） |
| **Effort** | 否 | 否 | 否 | 否（设计如此） |

---

## 7. 修改文件清单

| 文件 | 改动 | Commit |
|------|------|--------|
| `BIMCanvas.Agent/src/server/http_server.py` | 删除 `await agent.connect()` 预连接 | `3bf6b04` |
| `BIMCanvas.Web/src/components/UI/AICommandCenter.vue` | 添加 `isConfigLocked` + 禁用控件 | `3bf6b04` |
| `BIMCanvas.Web/src/components/UI/AICommandCenter.vue` | 修复判断条件（排除欢迎消息） | `f2460d5` |

---

## 8. 关键代码位置索引

| 功能 | 文件 | 行号 |
|------|------|------|
| HTTP 端点接收 effort/thinking | `http_server.py` | 226-227 |
| 参数传递给 chat_stream() | `http_server.py` | 262 |
| 延迟连接逻辑 | `main_agent.py` | 637-640 |
| connect() 方法 | `main_agent.py` | 264-306 |
| _create_options() 核心转换 | `main_agent.py` | 151-222 |
| Thinking 类型定义 | `main_agent.py` | 23 (import) |
| 默认配置加载 | `settings.py` | 52-53 |
| 前端 isConfigLocked | `AICommandCenter.vue` | ~123 |
| 前端 Effort Pill | `AICommandCenter.vue` | ~1130 |
| 前端 Thinking Pill | `AICommandCenter.vue` | ~1154 |
| SDK CLI 参数构建 | `subprocess_cli.py` | 300-313 |
