# BIMCanvas.Agent / spike

实验性脚本目录,不在生产路径上、不被 pytest 收集、不引用任何业务知识。
新增 spike 一律落在本目录下,文件命名 `<topic>.py`。

---

## `multi_mcp_test.py` — SDK 多 MCP server spike

### 背景

主真理源 v1.1 §3.8 设想每个 plugin 独立 namespace,以
`ClaudeAgentOptions(mcp_servers={"canvas": core, "<plugin-ns>": plugin_mcp, ...})`
形态注入 SDK。**此 dict 形态在 claude-agent-sdk 0.1.41 下的实际行为本组未在生产代码内验证**,
所以主真理源 §6.1 风险表里把它列为 **R2** —— 若失败,fallback 是单 server + 工具名前缀。

本 spike 在最小用例上把 R2 的两个关键问题压一遍,给指挥部一个干净的"PASS/FAIL"决策依据:

| 子实验 | 验证什么 |
|---|---|
| **E1** | 双 server `{"a": ..., "b": ...}` 下,LLM 能否同时发现并调用 `mcp__a__echo` 与 `mcp__b__echo`? |
| **E2** | `mcp__a__echo` 运行时抛 `RuntimeError` 时,`mcp__b__echo` 是否仍可被 LLM 成功调用? |

> **注意**:本 spike **不覆盖** plugin loader `register(builder)` 阶段的半加载防御 ——
> 那属于主真理源 §3.8 BIMCanvas 平台层逻辑(组 2 `PluginLifecycleService` 的 V11 T3 测试),
> 不是 SDK 行为。本 spike 范围严格限定在 SDK 层。

### 环境准备

干净的 Python 3.10+ 环境(推荐 venv):

```bash
python -m venv .venv
# Windows
.venv\Scripts\activate
# Linux/macOS
source .venv/bin/activate

pip install --upgrade pip
pip install "claude-agent-sdk==0.1.41"
```

只装 `claude-agent-sdk`,不需要项目的其他依赖 —— spike 是纯 SDK 探针。

LLM 凭证(二选一,与项目运行时设置一致):

| 环境变量 | 说明 |
|---|---|
| `ANTHROPIC_API_KEY` | 直接调用 Anthropic 官方 API 的 key |
| `ANTHROPIC_AUTH_TOKEN` | 走代理 / 自托管路由时常用的等价凭证(配合 `ANTHROPIC_BASE_URL` 用) |

可选环境变量(覆盖默认行为):

| 环境变量 | 说明 |
|---|---|
| `ANTHROPIC_BASE_URL` | 自定义 API base URL(走代理或 BIMCanvas 内置 Provider Adapter 时常用) |

如不熟悉哪些 vars 适合本机,参照 `%USERPROFILE%\Documents\BIMCanvas\config.dev.local.json` 与
`ccr_config.dev.local.json` 现有内容自行 export。

### 运行命令

```bash
python BIMCanvas.Agent/spike/multi_mcp_test.py
```

进程退出码:

| exit code | 含义 |
|---|---|
| `0` | E1 + E2 都未在 SDK 客户端层抛出未捕获异常(**不等于** 实验通过,需读 stdout 判定) |
| `1` | E1 或 E2 中有 SDK 客户端层未捕获异常 |
| `2` | 缺少 LLM 凭证环境变量,未发起任何实验 |

### 预期成功输出样例(片段)

stdout 应大致出现以下结构(具体 repr 内容随 SDK 版本与模型推理细节波动,只看关键 token):

```
========== E1 双 server 工具发现 + 调用 ==========
... AssistantMessage(...) ...
... ToolUseBlock(name='mcp__a__echo', input={'text': 'hello-from-a'}) ...
... ToolResultBlock(content=[TextBlock(text='[A] hello-from-a')], ...) ...
... ToolUseBlock(name='mcp__b__echo', input={'text': 'hello-from-b'}) ...
... ToolResultBlock(content=[TextBlock(text='[B] hello-from-b')], ...) ...
... ResultMessage(...) ...

[E1] DONE - 由指挥部根据 stdout 判定两个工具是否都被调到。

========== E2 server_a 工具运行时异常 → server_b 仍可调用 ==========
... ToolUseBlock(name='mcp__a__echo', input={'text': 'will-fail'}) ...
... ToolResultBlock(content=[TextBlock(text='Tool execution failed: ...')], is_error=True) ...
... ToolUseBlock(name='mcp__b__echo', input={'text': 'should-still-work'}) ...
... ToolResultBlock(content=[TextBlock(text='[B] should-still-work')], ...) ...
... ResultMessage(...) ...

[E2] DONE - 由指挥部根据 stdout 判定:
    (1) mcp__a__echo 是否如预期返回 error / 工具调用被标记失败;
    (2) mcp__b__echo 是否随后仍被 LLM 成功调用并返回 [B] should-still-work。
```

**判定为 PASS 的关键观察点**:

- E1:同一段 stdout 内 grep 到 `mcp__a__echo` 与 `mcp__b__echo` 各至少一次 `ToolUseBlock`,
  以及对应返回 `[A] hello-from-a` / `[B] hello-from-b` 文本。
- E2:`mcp__a__echo` 的 `ToolResultBlock` 标记 `is_error=True`(或返回明显错误文本),
  **且** `mcp__b__echo` 仍出现 `ToolUseBlock` 调用与 `[B] should-still-work` 返回。

### 预期失败输出样例(场景与对策)

| 场景 | 现象 | 对策 |
|---|---|---|
| 凭证未设 | 进程立刻 exit 2,stderr 提示 ANTHROPIC_API_KEY 缺失 | 按「环境准备」export 凭证 |
| LLM 找不到 `mcp__b__echo` | stdout 只看到 `mcp__a__echo` 调用,LLM 文字回应称 "no such tool b" | E1 FAIL → 组 3 走单 server + 工具名前缀 fallback |
| server_a 工具异常拖垮整个 server 列表 | E2 中 `mcp__b__echo` 完全不被调用,或 SDK 客户端层抛 unhandled exception | E2 FAIL → 同上 fallback,且需追加保护层 |
| 网络/路由问题 | stderr 出现 `aiohttp.ClientError` 或 `httpx.ConnectError` | 检查 `ANTHROPIC_BASE_URL`,与 BIMCanvas 主程序运行时同源 |

如果 SDK 抛出未捕获异常,traceback 会出现在 stderr,exit code = 1。把整段 traceback 贴回指挥部窗口由指挥部决断。

### 实验结论回填

进程末尾会打印一段 `结论模板(指挥部填写)`,把 E1 / E2 各自的 PASS / FAIL 填进去,
并按下表选择组 3 实现路线:

| E1 | E2 | 组 3 实现路线 |
|---|---|---|
| PASS | PASS | multi-MCP-server dict(主真理源 §3.8 原方案) |
| PASS | FAIL | dict 形态可用但需追加错误隔离层;指挥部决定是否仍走原方案 |
| FAIL | — | 单 server + 工具名前缀方案(主真理源 §6.1 R2 fallback) |

任一 FAIL,**组 1 的 `docs/plugin-manifest-schema.json` 中 `mcpNamespace` 字段语义需重审**
(单 server 路线下,`mcpNamespace` 不再映射 server dict key,而是变成工具名前缀)。指挥部
据 spike 结果决定是否阻塞组 1 Step 3。
