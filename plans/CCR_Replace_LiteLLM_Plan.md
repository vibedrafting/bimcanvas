# CCR 替换 LiteLLM 方案

> **分支**: `test/ccr-replace-litellm`
> **目标**: 用 claude-code-router (CCR) 替代 LiteLLM 作为 Gemini 供应商的 API 网关，解决工具调用参数丢失问题
> **CCR 源码**: `E:\工作文档\开发类\MyCode\claude-code-router`

---

## 1. 问题回顾

LiteLLM 的 Anthropic Messages API → Gemini 翻译层存在根本性缺陷，四轮测试均无法完全修复：

| 配置 | 供应商 | 单次调用 | 并行调用 |
|------|--------|---------|---------|
| 原始 | YesCode | ❌ | ❌ |
| `stream:false` + thinking:adaptive | YesCode | ✅ | ❌ |
| `stream:false` + thinking:adaptive | Sub2API | ❌ | ❌ |
| `stream:false` + thinking:disabled | YesCode | ❌ | ❌ |

**根因**：LiteLLM 在 Gemini 响应翻译（尤其是多工具调用和流式增量拼接）中丢失 `functionCall.args`。

---

## 2. CCR 为什么能修复

### 2.1 翻译路径对比

```
LiteLLM（有 Bug）:
  Gemini functionCall.args
    → 流式增量拼接（id=None 时跳过 delta）
    → OpenAI tool_calls.arguments
    → Anthropic tool_use.input
  问题：增量拼接逻辑在并行工具调用时丢失参数

CCR（直接映射）:
  Gemini functionCall.args
    → JSON.stringify（一步到位）
    → tool_calls.arguments
  并行工具调用通过 index 追踪，互不干扰
```

### 2.2 CCR 的额外保险

- **EnhanceTool transformer**：三层 JSON 修复（JSON.parse → JSON5 → jsonrepair），即使参数略有畸形也能恢复
- **Gemini transformer**：直接对 `parts[]` 中每个 `functionCall` 做映射，无中间拼接环节
- **Thinking 支持**：正确提取 `thought: true` 的 parts，不会与 tool call 冲突

---

## 3. 架构设计

### 3.1 当前架构（LiteLLM）

```
Agent SDK (claude.exe)
  ↓ POST /v1/messages (Anthropic 格式)
  ↓ model: "bc-gemini_yescode-sonnet"
LiteLLM (localhost:4000)
  ↓ Anthropic → Gemini 翻译（Bug 所在）
ProviderAdapter (localhost:4101, 可选)
  ↓ URL 重写
Gemini API (co.yes.vg/gemini)
```

### 3.2 目标架构（CCR）

```
Agent SDK (claude.exe)
  ↓ POST /v1/messages (Anthropic 格式)
  ↓ model: "gemini-yescode,gemini-3-flash-preview"  ← 改为 CCR 格式
CCR (localhost:3456)
  ↓ Anthropic → Unified → Gemini 翻译（CCR 自己的翻译链）
  ↓ 不需要 ProviderAdapter（CCR 自带 URL 构建）
Gemini API (co.yes.vg/gemini)
```

**关键变化**：
- CCR 替代 LiteLLM + ProviderAdapter（二合一）
- 模型名格式从 `bc-{provider}-{family}` 改为 `{ccr_provider},{gemini_model}`
- 端口从 4000 改为 3456

### 3.3 共存方案（推荐）

保留 LiteLLM 供 `anthropic_proxy` 使用，CCR 仅服务 Gemini 供应商：

```
Agent SDK
  ├─ model 含 "anthropic" → LiteLLM (localhost:4000) → Anthropic API
  └─ model 含 "gemini"    → CCR (localhost:3456) → Gemini API
```

**实现方式**：根据 `activeProvider` 决定 `AGENT_SDK_BASE_URL` 指向哪个网关。

---

## 4. 实施步骤

### Phase 1：手动验证 CCR（不改 BIMCanvas 代码）

**目标**：确认 CCR + Gemini 的工具调用参数不丢失

#### 步骤 1.1：构建 CCR

```bash
cd "E:\工作文档\开发类\MyCode\claude-code-router"
pnpm install
pnpm build
```

#### 步骤 1.2：配置 CCR

创建配置文件 `~/.claude-code-router/config.json`：

```json
{
  "PORT": 3456,
  "HOST": "127.0.0.1",
  "LOG": true,
  "LOG_LEVEL": "debug",
  "API_TIMEOUT_MS": 300000,
  "Providers": [
    {
      "name": "gemini-yescode",
      "api_base_url": "https://co.yes.vg/gemini/v1beta/models/",
      "api_key": "cr_2bee0f94e10f9a1857c94b8ee2c98ccc1ff815658d8ac673de87716af012ac09",
      "models": [
        "gemini-3-flash-preview",
        "gemini-3.1-pro-preview"
      ],
      "transformer": {
        "use": ["gemini", "enhancetool"]
      }
    },
    {
      "name": "gemini-sub2api",
      "api_base_url": "https://css.youngala.com/v1beta/models/",
      "api_key": "sk-715fca35f0d602ed381e2a43e48797ba257dd0b0114cc78af0fad63124d8e499",
      "models": [
        "gemini-3-flash-preview",
        "gemini-3.1-pro-preview"
      ],
      "transformer": {
        "use": ["gemini", "enhancetool"]
      }
    }
  ],
  "Router": {
    "default": "gemini-yescode,gemini-3-flash-preview",
    "think": "gemini-yescode,gemini-3.1-pro-preview",
    "background": "gemini-yescode,gemini-3-flash-preview"
  }
}
```

#### 步骤 1.3：手动启动 CCR

```bash
cd "E:\工作文档\开发类\MyCode\claude-code-router"
node packages/server/dist/index.js
# 或
npx ccr
```

#### 步骤 1.4：手动测试

临时修改 BIMCanvas 的环境变量注入，将 `AGENT_SDK_BASE_URL` 指向 CCR：

```
AGENT_SDK_BASE_URL=http://127.0.0.1:3456
AGENT_SDK_API_KEY=test
```

同时修改模型名为 CCR 格式：

```
ANTHROPIC_DEFAULT_OPUS_MODEL=gemini-yescode,gemini-3.1-pro-preview
ANTHROPIC_DEFAULT_SONNET_MODEL=gemini-yescode,gemini-3-flash-preview
ANTHROPIC_DEFAULT_HAIKU_MODEL=gemini-yescode,gemini-3-flash-preview
CLAUDE_CODE_SUBAGENT_MODEL=gemini-yescode,gemini-3-flash-preview
```

运行 BIMCanvas Server + Agent，发送 "hi"，观察是否能成功调用 Read/Glob 等工具。

#### 步骤 1.5：验证标准

- [ ] 单次工具调用（Read、Glob）参数不丢失
- [ ] 并行工具调用（多路 Read）参数不丢失
- [ ] Thinking 内容正常显示
- [ ] Skill 工具调用参数不丢失
- [ ] generate-workflow 完整运行

**如果 Phase 1 失败，停止后续步骤，分析 CCR 日志定位原因。**

---

### Phase 2：集成到 BIMCanvas Server

**目标**：Server 自动管理 CCR 子进程生命周期

#### 步骤 2.1：新增 CCR 配置模型

**修改文件**：`BIMCanvas.Server/Models/ServerConfig.cs`

```csharp
/// <summary>
/// Claude Code Router (CCR) 配置
/// </summary>
public class CcrSection
{
    /// <summary>
    /// 是否启用 CCR 网关（替代 LiteLLM 用于 Gemini 供应商）
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 是否由 Server 自动启动 CCR
    /// </summary>
    public bool AutoStart { get; set; } = true;

    /// <summary>
    /// CCR 监听主机
    /// </summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>
    /// CCR 监听端口
    /// </summary>
    public int Port { get; set; } = 3456;

    /// <summary>
    /// CCR 可执行路径（node 脚本入口）
    /// </summary>
    public string EntryPath { get; set; } = "";

    /// <summary>
    /// CCR 配置文件路径
    /// </summary>
    public string ConfigPath { get; set; } = "";
}
```

在 `ServerConfig` 中添加：

```csharp
public CcrSection Ccr { get; set; } = new();
```

#### 步骤 2.2：新增 CCR 配置模板

**新建文件**：`BIMCanvas.Server/Templates/ccr_config.json`

内容同 Phase 1 步骤 1.2 的 JSON 配置。

#### 步骤 2.3：修改 server_config.json

**修改文件**：`BIMCanvas.Server/Templates/server_config.json`

```json
{
  "ccr": {
    "enabled": false,
    "autoStart": true,
    "host": "127.0.0.1",
    "port": 3456,
    "entryPath": "",
    "configPath": ""
  }
}
```

#### 步骤 2.4：新增 CCR 子进程启动逻辑

**修改文件**：`BIMCanvas.Server/Program.cs`

在 LiteLLM 启动之后、Agent 启动之前，添加 CCR 启动逻辑：

```csharp
// 2.5 启动 CCR（如果启用）
Process? ccrProcess = null;
if (config.Ccr.Enabled && config.Ccr.AutoStart)
{
    // 清理端口占用
    if (IsPortOccupied(config.Ccr.Port, out var ccrOccupyingPid))
    {
        KillProcess(ccrOccupyingPid);
        Thread.Sleep(500);
    }

    WriteWithColoredPrefix("[Server]", "CCR 服务启动中...", ConsoleColor.White);
    ccrProcess = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = "node",
            Arguments = config.Ccr.EntryPath,
            WorkingDirectory = Path.GetDirectoryName(config.Ccr.EntryPath),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        }
    };
    // 注入端口和配置路径
    ccrProcess.StartInfo.Environment["SERVICE_PORT"] = config.Ccr.Port.ToString();
    ccrProcess.Start();

    // 等待 CCR 就绪（轮询 /health 端点）
    var ccrReady = await WaitForServiceReadyAsync(
        $"http://{config.Ccr.Host}:{config.Ccr.Port}/health",
        timeoutSeconds: 15
    );

    if (ccrReady)
        WriteWithColoredPrefix("[CCR]", $"CCR 已就绪: http://{config.Ccr.Host}:{config.Ccr.Port}", ConsoleColor.Magenta);
    else
        WriteWithColoredPrefix("[Server:WARN]", "CCR 未在预期时间内就绪", ConsoleColor.DarkYellow);
}
```

#### 步骤 2.5：修改 Agent 环境变量注入

**修改文件**：`BIMCanvas.Server/Program.cs`（约 line 459-476）

当 `activeProvider` 是 Gemini 系列且 CCR 启用时，将 Agent 指向 CCR：

```csharp
if (config.LiteLlm.Enabled)
{
    var activeProvider = NormalizeProviderName(config.LiteLlm.ActiveProvider);
    var defaultModelFamily = NormalizeModelFamily(config.LiteLlm.DefaultModelFamily);

    // 判断当前供应商是否走 CCR
    bool useCcr = config.Ccr.Enabled
                  && activeProvider.StartsWith("gemini_");

    if (useCcr)
    {
        // CCR 模式：指向 CCR 端口，使用 CCR 格式的模型名
        var gatewayUrl = $"http://{config.Ccr.Host}:{config.Ccr.Port}";
        agentProcess.StartInfo.Environment["AGENT_SDK_BASE_URL"] = gatewayUrl;
        agentProcess.StartInfo.Environment["AGENT_SDK_API_KEY"] = "bimcanvas-ccr";
        agentProcess.StartInfo.Environment["MODEL_NAME"] = defaultModelFamily;

        // CCR 模型名格式：{ccr_provider},{gemini_model}
        var ccrProvider = MapToCcrProvider(activeProvider);  // e.g. "gemini-yescode"
        var opusModel = GetGeminiModel(activeProvider, "opus");     // e.g. "gemini-3.1-pro-preview"
        var sonnetModel = GetGeminiModel(activeProvider, "sonnet"); // e.g. "gemini-3-flash-preview"
        var haikuModel = GetGeminiModel(activeProvider, "haiku");   // e.g. "gemini-3-flash-preview"

        agentProcess.StartInfo.Environment["ANTHROPIC_DEFAULT_OPUS_MODEL"] = $"{ccrProvider},{opusModel}";
        agentProcess.StartInfo.Environment["ANTHROPIC_DEFAULT_SONNET_MODEL"] = $"{ccrProvider},{sonnetModel}";
        agentProcess.StartInfo.Environment["ANTHROPIC_DEFAULT_HAIKU_MODEL"] = $"{ccrProvider},{haikuModel}";
        agentProcess.StartInfo.Environment["CLAUDE_CODE_SUBAGENT_MODEL"] = $"{ccrProvider},{haikuModel}";
    }
    else
    {
        // LiteLLM 模式（保持不变）
        var gatewayUrl = $"http://{config.LiteLlm.Host}:{config.LiteLlm.Port}";
        agentProcess.StartInfo.Environment["AGENT_SDK_BASE_URL"] = gatewayUrl;
        agentProcess.StartInfo.Environment["AGENT_SDK_API_KEY"] = "bimcanvas-local-gateway";
        agentProcess.StartInfo.Environment["MODEL_NAME"] = defaultModelFamily;
        agentProcess.StartInfo.Environment["ANTHROPIC_DEFAULT_OPUS_MODEL"] = $"bc-{activeProvider}-opus";
        agentProcess.StartInfo.Environment["ANTHROPIC_DEFAULT_SONNET_MODEL"] = $"bc-{activeProvider}-sonnet";
        agentProcess.StartInfo.Environment["ANTHROPIC_DEFAULT_HAIKU_MODEL"] = $"bc-{activeProvider}-haiku";
        agentProcess.StartInfo.Environment["CLAUDE_CODE_SUBAGENT_MODEL"] = $"bc-{activeProvider}-subagent";
    }
}
```

辅助函数：

```csharp
static string MapToCcrProvider(string bimCanvasProvider)
{
    // bc 格式: "gemini_yescode" → ccr 格式: "gemini-yescode"
    return bimCanvasProvider.Replace("_", "-");
}

static string GetGeminiModel(string provider, string family)
{
    // 从 litellm_config.yaml 模板中获取实际模型名
    // opus → gemini-3.1-pro-preview
    // sonnet/haiku/subagent → gemini-3-flash-preview
    return family == "opus" ? "gemini-3.1-pro-preview" : "gemini-3-flash-preview";
}
```

---

### Phase 3：清理和优化

#### 步骤 3.1：CCR 子进程日志集成

将 CCR 的 stdout/stderr 重定向到 Server 控制台，使用 `[CCR]` 前缀：

```csharp
_ = Task.Run(async () =>
{
    while (!ccrProcess.HasExited)
    {
        var line = await ccrProcess.StandardOutput.ReadLineAsync();
        if (!string.IsNullOrEmpty(line))
            WriteWithColoredPrefix("[CCR]", line, ConsoleColor.Magenta);
    }
});
```

#### 步骤 3.2：进程清理

在 Server 退出时确保 CCR 子进程被终止（与 LiteLLM 清理逻辑对齐）。

#### 步骤 3.3：恢复 thinking 和 effort 配置

Phase 1 测试时关闭的 thinking/effort 配置恢复为正常值，验证 CCR 在 thinking:adaptive 模式下仍然正常。

---

## 5. 模型名映射表

| BIMCanvas 环境变量 | LiteLLM 模式（当前） | CCR 模式（新） |
|-------------------|---------------------|---------------|
| `ANTHROPIC_DEFAULT_OPUS_MODEL` | `bc-gemini_yescode-opus` | `gemini-yescode,gemini-3.1-pro-preview` |
| `ANTHROPIC_DEFAULT_SONNET_MODEL` | `bc-gemini_yescode-sonnet` | `gemini-yescode,gemini-3-flash-preview` |
| `ANTHROPIC_DEFAULT_HAIKU_MODEL` | `bc-gemini_yescode-haiku` | `gemini-yescode,gemini-3-flash-preview` |
| `CLAUDE_CODE_SUBAGENT_MODEL` | `bc-gemini_yescode-subagent` | `gemini-yescode,gemini-3-flash-preview` |

CCR 格式要求：`{provider_name},{model_name}`，逗号分隔，provider_name 对应 CCR config 中 `Providers[].name`。

---

## 6. CCR 配置与 LiteLLM 配置的对应关系

| LiteLLM 配置项 | CCR 对应配置 | 说明 |
|---------------|-------------|------|
| `model: gemini/gemini-3-flash-preview` | `Providers[].models: ["gemini-3-flash-preview"]` | 模型名不带前缀 |
| `api_base: https://co.yes.vg/gemini` | `Providers[].api_base_url: "https://co.yes.vg/gemini/v1beta/models/"` | CCR 需要完整路径到 models/ |
| `api_key: cr_xxx` | `Providers[].api_key: "cr_xxx"` | 直接对应 |
| `stream: false` | `transformer.use: ["enhancetool"]` | enhancetool 缓冲完整响应 |
| `litellm_settings.drop_params` | Gemini transformer 内置 schema 清理 | CCR 自动处理 |

---

## 7. 关键文件索引

### 需要修改的 BIMCanvas 文件

| 文件 | Phase | 修改内容 |
|------|-------|---------|
| `BIMCanvas.Server/Models/ServerConfig.cs` | 2 | 新增 `CcrSection` |
| `BIMCanvas.Server/Templates/server_config.json` | 2 | 新增 `ccr` 配置块 |
| `BIMCanvas.Server/Program.cs` (lines ~358, ~459) | 2 | CCR 子进程启动 + 环境变量注入 |

### 需要新建的文件

| 文件 | Phase | 内容 |
|------|-------|------|
| `BIMCanvas.Server/Templates/ccr_config.json` | 2 | CCR Provider 和 Router 配置 |

### CCR 源码参考

| 文件 | 说明 |
|------|------|
| `packages/core/src/server.ts` | 服务启动、模型名解析（line 228: `model.split(",")`) |
| `packages/core/src/utils/router.ts` | 路由逻辑、自定义路由支持 |
| `packages/core/src/transformer/gemini.transformer.ts` | Gemini 翻译器入口 |
| `packages/core/src/utils/gemini.util.ts` | Gemini 请求/响应转换核心 |
| `packages/core/src/transformer/enhancetool.transformer.ts` | 工具参数修复 |
| `packages/core/src/transformer/anthropic.transformer.ts` | Anthropic 格式转换 |
| `packages/server/src/index.ts` | Server 包入口（默认端口 3456） |

---

## 8. 风险和回退

### 风险

| 风险 | 影响 | 缓解措施 |
|------|------|---------|
| CCR 的 Gemini transformer 也有 Bug | 工具调用仍然失败 | Phase 1 手动验证，失败则不继续 |
| enhancetool 禁用流式传输 | 无实时输出，用户体验下降 | 先测试不带 enhancetool（只用 gemini），如果不带就能成功则不需要 enhancetool |
| Node.js 新依赖 | 部署环境需要 Node.js | BIMCanvas 已有 npm（Web 前端），Node.js 已是必备环境 |
| CCR 与 Agent SDK 不完全兼容 | 未知协议差异 | Phase 1 手动测试覆盖 |

### 回退方案

本方案在 `test/ccr-replace-litellm` 分支上开发，不影响 `feature/fast-workflow` 主线。

如果 CCR 方案失败：
1. 切回 `feature/fast-workflow` 分支
2. 继续使用 `anthropic_proxy` 作为主力供应商
3. 等待 LiteLLM 修复 #20711 和 #17949

---

## 9. 执行顺序

```
Phase 1（手动验证）
  ├─ 1.1 构建 CCR
  ├─ 1.2 配置 CCR
  ├─ 1.3 手动启动 CCR
  ├─ 1.4 临时修改 BIMCanvas 环境变量
  └─ 1.5 验证工具调用 ← 关键门控点
        │
        ├─ 失败 → 分析 CCR 日志 → 尝试不同 transformer 组合 → 仍失败则放弃
        │
        └─ 成功 → 进入 Phase 2
              │
Phase 2（集成）
  ├─ 2.1 新增 CcrSection 配置模型
  ├─ 2.2 新增 CCR 配置模板
  ├─ 2.3 修改 server_config.json
  ├─ 2.4 新增 CCR 子进程启动逻辑
  └─ 2.5 修改 Agent 环境变量注入
        │
Phase 3（清理）
  ├─ 3.1 日志集成
  ├─ 3.2 进程清理
  └─ 3.3 恢复 thinking/effort 配置
```
