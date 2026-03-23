# LiteLLM + Gemini 工具调用参数丢失问题调查报告

> **报告日期**: 2026-03-23
> **问题级别**: Critical — 导致 Gemini 供应商完全不可用于工具密集型工作流
> **影响范围**: 所有通过 LiteLLM Anthropic Messages API 端点调用的 Gemini 模型
> **当前状态**: 已定性，LiteLLM 翻译层 Bug，暂无本地修复方案

---

## 1. 问题摘要

使用 `gemini_yescode` 或 `gemini_sub2api` 供应商时，Agent 在执行工具密集型工作流（如 `generate-workflow`）过程中，大量工具调用失败——工具被正确选择，但 **必需参数全部为空**：

```
Tool Error (Read): The required parameter `file_path` is missing
Tool Error (Glob): The required parameter `pattern` is missing
Tool Error (Bash): The required parameter `command` is missing
```

模型在 thinking 输出中明确写出了正确的参数值，但参数在翻译链路中被丢失。

---

## 2. 调用链路架构

```
┌──────────────────────────────┐
│  Agent SDK (claude.exe)      │  发送 Anthropic Messages API
│  ClaudeAgentOptions:         │  POST /v1/messages?beta=true
│    model = "sonnet"          │  格式: tool_use { input: {...} }
│    base_url = localhost:4000 │
└──────────┬───────────────────┘
           │
    ┌──────▼───────────────────────┐
    │  LiteLLM Gateway             │  ★ 翻译发生在这里
    │  localhost:4000              │
    │                              │  Anthropic Messages API
    │  model: bc-gemini_*-sonnet   │    → OpenAI Chat Completions
    │  实际模型: gemini/gemini-*   │    → Gemini Native API
    └──────┬───────────────────────┘
           │
    ┌──────▼──────────────────────────────┐
    │  ProviderAdapter (可选)              │  纯 HTTP 反向代理
    │  localhost:4101                      │  仅做路径重写 (/v1beta)
    │  gemini_yescode: adapter 模式       │  不修改请求/响应体
    │  gemini_sub2api: direct 模式（跳过）│
    └──────┬──────────────────────────────┘
           │
    ┌──────▼──────────────────┐
    │  Gemini API             │  接收 Gemini 原生格式
    │  co.yes.vg/gemini       │  返回 functionCall + args
    │  css.youngala.com       │
    └─────────────────────────┘
```

**关键翻译路径**（双重翻译）：

| 方向 | Anthropic 格式 | OpenAI 中间格式 | Gemini 原生格式 |
|------|---------------|----------------|----------------|
| 请求 | `tool_use` + `input_schema` | `tool_calls` + `parameters` | `FunctionDeclaration` |
| 响应 | `tool_use` + `input` | `tool_calls` + `arguments` | `functionCall` + `args` |

---

## 3. 对照实验

### 3.1 实验设计

三个供应商，同一 Agent SDK、同一 Claude Code CLI、同一工具定义、同一工作流（`generate-workflow`），仅切换 `activeProvider`。

### 3.2 实验结果

| 供应商 | 实际模型 | ProviderAdapter | 翻译路径 | 简单对话 | 单工具 | 多工具 | 结论 |
|--------|---------|----------------|---------|---------|-------|-------|------|
| `anthropic_proxy` | Claude Sonnet 4.5 | 无（direct） | Anthropic→Anthropic（无翻译） | ✅ | ✅ | ✅ 4路并行 | **完美** |
| `gemini_yescode` (opus) | gemini-3.1-pro-preview | 有（adapter） | Anthropic→Gemini（双重翻译） | ✅ | ✅ | ❌ 截图后连续7次参数丢失 | **失败** |
| `gemini_yescode` (sonnet) | gemini-3-flash-preview | 有（adapter） | Anthropic→Gemini（双重翻译） | ✅ | ✅ | ❌ 连续多次参数丢失 | **失败** |
| `gemini_sub2api` (sonnet) | gemini-3-flash-preview | 无（direct） | Anthropic→Gemini（双重翻译） | ✅ | ✅ | ❌ 截图前就连续7次参数丢失 | **失败** |

### 3.3 排除法

| 假设 | 排除证据 | 判定 |
|------|---------|------|
| 模型能力不足（Flash 太弱） | Pro (gemini-3.1-pro-preview) 同样失败 | ❌ 排除 |
| ProviderAdapter 引入问题 | `gemini_sub2api` 是 direct 模式，不走 adapter，照样失败 | ❌ 排除 |
| Workflow 设计过于激进 | `anthropic_proxy` 跑同一 workflow 完美通过（含 4 路并行读取） | ❌ 排除 |
| 大图片（截图 base64）破坏翻译 | `gemini_sub2api` 在截图之前就开始失败 | ❌ 排除 |
| 中文路径触发级联 | `anthropic_proxy` 同样面对中文路径，完全正常 | ❌ 排除 |
| **LiteLLM Anthropic→Gemini 翻译层丢失参数** | 两个 Gemini 供应商均失败，唯一的 Anthropic 供应商完美 | ✅ **确认** |

---

## 4. 日志证据详解

### 4.1 证据一：模型知道正确参数但到达 Agent 时为空

`gemini_sub2api` 日志中，模型 thinking 输出：

```
I'm now correctly specifying
C:\Users\huhaonan\Documents\BIMCanvas\Projects\金凤127\context\requirements.md
to uncover any pre-existing user requirements
```

但紧接着的工具调用结果：

```
Tool Error (Read): The required parameter `file_path` is missing
```

**结论**：模型正确生成了 `functionCall` 的 `args`，但 LiteLLM 在将 Gemini 响应翻译回 Anthropic `tool_use` 格式时，`input` 字段丢失。

### 4.2 证据二：单工具调用也失败

`gemini_sub2api` 日志中，每次只调用一个 Read 工具（非并行），连续 7 次失败。这排除了"并行调用太多导致参数丢失"的假设。

### 4.3 证据三：count_tokens 端点直接 500

```
[LiteLLM] "POST /v1/messages/count_tokens?beta=true HTTP/1.1" 500 Internal Server Error
```

LiteLLM 对 Gemini 模型甚至无法正确处理 token 计数请求，说明其 Anthropic Messages API 的 Gemini 兼容层本身不完整。

### 4.4 证据四：超时可能加剧问题

`gemini_yescode` (opus) 日志中出现 ProviderAdapter 超时：

```
[11:22:15] ProviderAdapter 转发失败: HttpClient.Timeout of 100 seconds elapsing
```

100 秒超时后 LiteLLM 会话状态可能不一致，可能进一步加剧了翻译层的参数丢失问题。

### 4.5 附注：日志串号问题

Agent 日志中偶尔出现"工具名和错误内容不匹配"的现象：

```
Tool Error (Glob): Read failed due to the following issue:
The required parameter `file_path` is missing
```

这是 `main_agent.py` 中 `_current_tool_name` 变量在并行工具调用时被覆盖导致的日志显示 Bug，不影响实际执行，但会干扰故障分析。

---

## 5. LiteLLM 已知相关缺陷

通过 GitHub Issues 和文档调研，找到以下与本问题直接相关的已知缺陷：

### 5.1 高度相关（与症状直接匹配）

| Issue | 描述 | 相关性 |
|-------|------|--------|
| [#20711](https://github.com/BerriAI/litellm/issues/20711) | 流式响应中 tool call 的 argument 增量事件因 `id=None` 被跳过，~90% 参数丢失 | ⭐⭐⭐ 最可能的直接原因 |
| [#17949](https://github.com/BerriAI/litellm/issues/17949) | Gemini 3+ 的 `thought_signature` 导致多轮工具调用中 part count 不匹配 | ⭐⭐⭐ thinking + tool call 组合时高度相关 |
| [#21744](https://github.com/BerriAI/litellm/issues/21744) | `MALFORMED_FUNCTION_CALL` 被静默映射为 `stop`，不抛异常 | ⭐⭐ 部分空响应可能是被吞掉的畸形调用 |

### 5.2 中度相关（Schema 兼容性）

| Issue | 描述 | 相关性 |
|-------|------|--------|
| [#9793](https://github.com/BerriAI/litellm/issues/9793) | `default` 字段不被 Gemini 支持，Claude Code 工具 schema 大量使用 | ⭐⭐ 可能导致 schema 解析异常 |
| [#12222](https://github.com/BerriAI/litellm/issues/12222) | 任一工具含可选参数，所有工具调用失败 | ⭐⭐ Claude Code 的 Read/Glob 等工具有大量可选参数 |
| [#9289](https://github.com/BerriAI/litellm/issues/9289) | 空 `properties` 对象导致 400 错误（仍 Open） | ⭐ 部分工具可能触发 |
| [#5055](https://github.com/BerriAI/litellm/issues/5055) | 无参数函数缺少 `type: object` 导致错误 | ⭐ |

### 5.3 低度相关（其他已知问题）

| Issue | 描述 |
|-------|------|
| [#16533](https://github.com/BerriAI/litellm/issues/16533) | 非 ASCII 字符（中文）在 function call arguments 中被 Unicode 转义 |
| [#6495](https://github.com/BerriAI/litellm/issues/6495) | 参数类型缺失（已修复） |

---

## 6. 根因定性

### 6.1 确定的根因

**LiteLLM 的 Anthropic Messages API (`/v1/messages`) 对 Gemini 模型的工具调用翻译存在 Bug，导致模型生成的 `functionCall.args` 无法正确映射为 Anthropic 格式的 `tool_use.input`。**

最可能的具体机制：

1. **流式响应参数丢失**（#20711）：LiteLLM 在流式模式下处理 Gemini 响应时，tool call 的 argument 增量事件因缺少 `id` 字段被跳过，导致最终组装的 tool_use 块 `input` 为空。
2. **thinking + tool call 冲突**（#17949）：开启 adaptive thinking 后，Gemini 返回的 `thought_signature` 与 `functionCall` 在同一 assistant 消息中，LiteLLM 将其分离时破坏了 tool call 的参数关联。

### 6.2 排除的假设

| 假设 | 排除依据 |
|------|---------|
| Gemini 模型能力不足 | Pro 和 Flash 表现一致；模型 thinking 中正确写出了参数 |
| ProviderAdapter 引入问题 | direct 模式（跳过 adapter）同样失败 |
| Workflow 设计问题 | anthropic_proxy 跑同一工作流完美通过 |
| 截图大图片触发翻译异常 | gemini_sub2api 在截图之前就开始失败 |
| 中文路径/编码问题 | anthropic_proxy 同样处理中文路径，无任何问题 |
| 网络/供应商端点问题 | 两个不同的 Gemini 供应商端点（yescode / sub2api）都失败 |

---

## 7. 与外部专家分析的对比

Codex 专家给出了一份多层诊断分析，核心论点为"模型太弱 + 工作流太激进 + 首次失败后连锁恢复"。以下是基于实验数据的逐点评估：

| Codex 观点 | 本报告评估 | 实验证据 |
|------------|-----------|---------|
| "主因：gemini-3-flash-preview 太弱" | **否定** | Pro (gemini-3.1-pro-preview) 同样失败 |
| "ProviderAdapter 只改 URL，不是问题" | **同意** | direct 模式同样失败，确认 adapter 无关 |
| "中文路径 Bash 失败触发级联" | **否定为根因**（是加速器） | anthropic_proxy 面对中文路径完全正常 |
| "日志串号（_current_tool_name 覆盖）" | **同意** | 确实存在日志显示 Bug |
| "Workflow 让模型自己找路径，增加脆弱性" | **同意是放大器** | 但 anthropic_proxy 证明 workflow 本身没问题 |
| "建议换成 Pro 模型" | **实验证伪** | opus→Pro 后同样失败 |

**结论**：Codex 分析中关于日志串号和 workflow 放大器的观点有价值，但核心论点"模型太弱"被实验数据否定。

---

## 8. 应对方案

### 8.1 短期（立即可执行）

**使用 `anthropic_proxy` 作为工具密集型工作流的主力供应商。**

操作：修改 `server_config.json` 中 `liteLlm.activeProvider` 为 `anthropic_proxy`。

```json
{
  "liteLlm": {
    "activeProvider": "anthropic_proxy"
  }
}
```

优点：已验证完美运行，零风险。
缺点：依赖 Anthropic 代理节点的稳定性和配额。

### 8.2 中期（需要测试验证）

**升级 LiteLLM + 配置调整，尝试恢复 Gemini 供应商可用性。**

1. **升级 LiteLLM 到最新 stable**：

   ```bash
   pip install litellm --upgrade
   ```

2. **在 `litellm_config.yaml` 中添加兼容性配置**：

   ```yaml
   litellm_settings:
     drop_params: true          # 自动清除 Gemini 不支持的 schema 字段
     set_verbose: false          # 调试时开启
   ```

3. **对 Gemini 模型禁用流式传输**（针对 #20711）：

   ```yaml
   - model_name: bc-gemini_yescode-sonnet
     litellm_params:
       model: gemini/gemini-3-flash-preview
       api_base: https://co.yes.vg/gemini
       api_key: ...
       stream: false             # 禁用流式，避免参数丢失
   ```

4. **测试关闭 thinking**（针对 #17949）：
   在 Agent 配置中将 `defaultThinking` 设为 `off`，排除 thought_signature 干扰。

### 8.3 长期（架构层面）

| 方案 | 描述 | 复杂度 |
|------|------|--------|
| 等待 LiteLLM 修复 | 关注 #20711、#17949 的修复进度 | 低（被动） |
| 绕过 LiteLLM 直接对接 Gemini | 在 ProviderAdapter 中实现 Anthropic↔Gemini 翻译 | 高 |
| 使用 Gemini 的 OpenAI 兼容端点 | 避免 Anthropic Messages API 翻译路径 | 中 |
| 多供应商自动 fallback | Gemini 工具调用失败时自动切换 anthropic_proxy | 中 |

---

## 9. 独立的代码修复建议

以下问题与 LiteLLM Bug 无关，但在调查过程中发现，建议独立修复：

### 9.1 日志串号 Bug

**文件**: `BIMCanvas.Agent/src/agent/main_agent.py`

**问题**: `_current_tool_name` 是单值变量，并行工具调用时后面的工具名覆盖前面的，导致日志中工具名与错误内容不匹配。

**建议**: 改用 `tool_use_id → tool_name` 的映射字典，确保每个工具结果关联正确的工具名。

### 9.2 ProviderAdapter 超时

**文件**: `BIMCanvas.ProviderAdapter/Program.cs`

**问题**: 默认 `HttpClient.Timeout` 为 100 秒，对于 Gemini 的长思考场景可能不够。

**建议**: 增加超时时间或使其可配置。

---

## 10. 相关文件索引

| 组件 | 文件路径 |
|------|---------|
| LiteLLM 配置模板 | `BIMCanvas.Server/Templates/litellm_config.yaml` |
| Server 配置模板 | `BIMCanvas.Server/Templates/server_config.json` |
| Server 配置模型 | `BIMCanvas.Server/Models/ServerConfig.cs` |
| LiteLLM 运行时配置生成器 | `BIMCanvas.Server/Services/LiteLlmRuntimeConfigBuilder.cs` |
| Server 启动流程 | `BIMCanvas.Server/Program.cs` |
| ProviderAdapter | `BIMCanvas.ProviderAdapter/Program.cs` |
| Agent 设置 | `BIMCanvas.Agent/src/config/settings.py` |
| Agent 主入口 | `BIMCanvas.Agent/src/agent/main_agent.py` |
| MCP 工具定义 | `BIMCanvas.Agent/src/mcp/canvas.py` |

---

## 11. 参考资料

### LiteLLM GitHub Issues

- [#20711 — Streaming drops tool call argument deltas](https://github.com/BerriAI/litellm/issues/20711)
- [#17949 — Multi-turn function calling part count mismatch](https://github.com/BerriAI/litellm/issues/17949)
- [#21744 — MALFORMED_FUNCTION_CALL silently normalized](https://github.com/BerriAI/litellm/issues/21744)
- [#16651 — MALFORMED_FUNCTION_CALL silent failure](https://github.com/BerriAI/litellm/issues/16651)
- [#9793 — `default` field errors with Gemini](https://github.com/BerriAI/litellm/issues/9793)
- [#12222 — Optional args cause all tools to fail](https://github.com/BerriAI/litellm/issues/12222)
- [#9289 — Empty properties 400 error](https://github.com/BerriAI/litellm/issues/9289)
- [#5055 — Unable to call function without parameters](https://github.com/BerriAI/litellm/issues/5055)
- [#16533 — Non-ASCII Unicode escape in arguments](https://github.com/BerriAI/litellm/issues/16533)

### LiteLLM 文档

- [Gemini Provider Docs](https://docs.litellm.ai/docs/providers/gemini)
- [Anthropic /v1/messages Endpoint](https://docs.litellm.ai/docs/anthropic_unified/)
- [Use Claude Code with Non-Anthropic Models](https://docs.litellm.ai/docs/tutorials/claude_non_anthropic_models)
