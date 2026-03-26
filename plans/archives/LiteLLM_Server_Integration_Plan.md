# LiteLLM 集成与 Server 托管实施计划

## 摘要

- 目标链路：`BIMCanvas.Agent / Claude Code -> LiteLLM -> 下游供应商`
- 不引入本地策略代理；LiteLLM 负责多协议适配，Server 负责依赖检查、模板初始化与进程托管。
- 本期边界：
  - LiteLLM 使用共用系统 Python 安装 `litellm[proxy]`
  - Server 启动时负责检测、安装、生成 `model_list` 模板并启动 LiteLLM
  - 当前活跃供应商通过配置文件切换，修改后重启生效
  - 不做前端配置界面，不做 LiteLLM 管理 API
  - 如果 LiteLLM 不可用，Server 继续启动，Agent 仍照常启动，AI 请求失败由运行时暴露并记录日志

## 关键改动

### 1. Server 配置与模板

- 扩展 `BIMCanvas.Server/Models/ServerConfig.cs`，新增 `LiteLlmSection`
- 更新 `BIMCanvas.Server/Templates/server_config.json`，加入 `liteLlm` 节点
- 新增 `BIMCanvas.Server/Templates/litellm_config.yaml`
- 更新 `BIMCanvas.Server/Templates/program_manifest.json`，由 `ConfigService.EnsureDefaultConfigs()` 初始化 `litellm_config.yaml`
- 固定 alias 约定：
  - `bc-{provider}-opus`
  - `bc-{provider}-sonnet`
  - `bc-{provider}-haiku`
  - `bc-{provider}-subagent`
- 模板中提供 `anthropic`、`openai`、`vertex_ai` 三组 `model_list` 示例
- 更新 `BIMCanvas.Server/Templates/web_config.json` 默认模型列表为稳定家族名：
  - `opus`
  - `sonnet`
  - `haiku`

### 2. Server 启动与进程托管

- 在 `BIMCanvas.Server/Program.cs` 的环境检测阶段新增 LiteLLM 检查：
  1. 检测 Python
  2. 检测 LiteLLM 可执行性：`python -m litellm --help`
  3. 缺失时执行 `python -m pip install "litellm[proxy]"`
  4. 确认 `Documents/BIMCanvas/litellm_config.yaml` 已初始化
  5. 启动 LiteLLM 进程
  6. 再启动 Agent
- LiteLLM 启动命令固定为：
  - `python -m litellm --config <Documents/BIMCanvas/litellm_config.yaml> --host <host> --port <port>`
- LiteLLM 进程采用与 Agent 相同的托管方式：
  - 后台启动
  - stdout/stderr 实时转发
  - 控制台前缀使用 `[LiteLLM]` / `[LiteLLM:ERR]`
  - Server 退出时主动清理由自己拉起的 LiteLLM 进程
- 端口占用处理沿用现有 `Program.cs` 风格：
  - 如 LiteLLM 端口被占用且能识别为 LiteLLM 残留进程，则先清理再启动
  - 识别失败只记录警告，不强杀未知进程
- 新增辅助方法：
  - `IsLiteLlmReady()`
  - `TryInstallLiteLlmDependencies()`
  - `StartLiteLlmProcess()`
  - `IsLiteLlmProcess()`
  - `BuildLiteLlmStartInfo()`

### 3. Agent 与 Claude Code 映射接入

- 不修改整体 Agent 架构，不新增本地代理
- Server 在启动 Agent 进程时追加环境变量：
  - `AGENT_SDK_BASE_URL=http://127.0.0.1:{liteLlmPort}`
  - `MODEL_NAME={defaultModelFamily}`
  - `ANTHROPIC_DEFAULT_OPUS_MODEL=bc-{activeProvider}-opus`
  - `ANTHROPIC_DEFAULT_SONNET_MODEL=bc-{activeProvider}-sonnet`
  - `ANTHROPIC_DEFAULT_HAIKU_MODEL=bc-{activeProvider}-haiku`
  - `CLAUDE_CODE_SUBAGENT_MODEL=bc-{activeProvider}-subagent`
- `BIMCanvas.Agent/src/agent/main_agent.py` 继续将这些环境变量透传给 Claude Code 子进程
- 保留 `BIMCanvas.Agent` 现有 `config.json` / `.env` 兼容性
- 本期不新增 Server API，也不做 Web 端供应商切换界面；供应商切换只通过 `Documents/BIMCanvas/server_config.json > liteLlm.activeProvider` 修改并重启生效

### 4. 配置行为与失败模式

- `activeProvider` 只负责选择当前 alias 组，不直接决定 LiteLLM 的 `model_list` 内容
- 如果 LiteLLM 配置不完整或进程启动失败：
  - Server 正常启动
  - LiteLLM 记为不可用
  - Agent 仍照常启动
  - AI 失败通过日志明确暴露为网关不可用或请求失败
- 不做自动回退到直连 Anthropic
- 不做运行时热切换；修改 `activeProvider` 后需重启 Server

## 公开接口与类型变更

- `Documents/BIMCanvas/server_config.json` 新增 `liteLlm` 配置段
- 新增公开运行时文件：`Documents/BIMCanvas/litellm_config.yaml`
- `Documents/BIMCanvas/web_config.json` 默认 `customModels` 改为 `opus/sonnet/haiku`
- `BIMCanvas.Server/Models/ServerConfig.cs` 新增 `LiteLlmSection`
- 不新增 REST API，不修改 `api/web_config` 与 `api/config` 的响应结构

## 测试计划

- 新机首启：
  - 系统已装 Python，但未装 LiteLLM
  - Server 自动检测缺失、执行安装、初始化 `litellm_config.yaml`
  - LiteLLM 成功启动并输出日志
- 配置模板初始化：
  - 删除 `Documents/BIMCanvas/litellm_config.yaml` 后重启
  - Server 重新复制模板，且不覆盖已存在文件
- 模型映射生效：
  - `activeProvider=anthropic` 时，主请求与后台请求命中 `bc-anthropic-*`
  - `activeProvider=openai` 后重启，再次请求命中 `bc-openai-*`
  - 至少验证 `sonnet`、`haiku`、`subagent`
- 流式能力：
  - Web 发起 `/api/chat/stream`
  - 经过 LiteLLM 后仍保持 SSE / 流式输出
- `count_tokens` 能力：
  - Claude Code / LiteLLM 路径下不报协议错误
- 启动失败路径：
  - LiteLLM 端口被占用
  - `litellm_config.yaml` 中 provider key 缺失
  - LiteLLM 启动失败时 Server 继续、Agent 照常启动、AI 请求在运行时明确报错
- 旧配置兼容：
  - 已存在 `~/.bimcanvas/config.json` 的环境中，Agent 仍能启动
  - Server 注入的 gateway 环境变量优先生效

## 假设与默认值

- 本期目标是 Windows 本地开发链路优先；LiteLLM 管理代码复用现有跨平台命令分支风格
- LiteLLM 默认安装方式锁定为共用系统 Python
- 当前活跃供应商切换方式锁定为“改配置文件后重启”
- LiteLLM 负责多协议适配；BIMCanvas 不实现多协议转换逻辑
- 本期只做 `model_list` 模板初始化和进程托管，不做 LiteLLM 配置可视化编辑器
