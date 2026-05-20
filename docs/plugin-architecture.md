# BIMCanvas Plugin Architecture

> 平台 + plugin 架构总图,面向 plugin 作者与平台贡献者。本文配合 [plugin-lifecycle-states.md](./plugin-lifecycle-states.md)(状态机)+ [plugin-security-model.md](./plugin-security-model.md)(安全边界)+ [plugin-manifest-schema.md](./plugin-manifest-schema.md)(字段字典)+ [BYO-Plugin.md](./BYO-Plugin.md)(动手指南)一起阅读。
>
> 主真理源:`.dev/plans/BIMCanvas平台化改造实施计划/BIMCanvas平台化改造实施计划.md` v1.1。

---

## 1. 一句话定位

**BIMCanvas = 通用 BIM-AI 平台基座 + 域插件(plugin)**:基座一次写、所有 domain 共享;每个域(室内布置 / MEP / 点位 / 施工序列 / 材料推荐 / ……)封装在一个独立 plugin,用户按需安装。

```text
┌───────────────────────────────────────────────────────────┐
│                  通用平台基座 (Platform)                  │
│                                                           │
│  Core 几何/碰撞     Server REST/SignalR     Web 画布      │
│  Agent 运行底座     Canvas-MCP (7 工具)     文件驱动      │
│  Git Worktree       .bcp 项目格式 (多 scene)              │
│                                                           │
│  core-base plugin (基础 chat / edit)  ← 平台首启动自带    │
└─────────────────────────┬─────────────────────────────────┘
                          │
       ┌──────────────────┼──────────────────┐
       ↓                  ↓                  ↓
┌─────────────┐   ┌──────────────┐   ┌─────────────────┐
│ indoor-     │   │ electrical-  │   │ commercial-     │
│ layout      │   │ points       │   │ layout / mep /  │
│ (家具布置)  │   │ (精装点位)   │   │ construction-…  │
└─────────────┘   └──────────────┘   └─────────────────┘
   每个域 = 一个独立 GitHub repo;用户 Web 端粘贴 URL 安装
```

平台基座的核心约束:**绝不内置任何 domain 知识**(没有"家具"、"管线"、"灯具"概念);所有"会因 domain 而异"的能力(系统提示词 / SubAgents / Skills / MCP 工具 / 模块库 / 参考资料)全部走 plugin 投影。

---

## 2. Plugin 是什么

一个 BIMCanvas plugin = 一个**纯净目录**,包含七类资源(全部可选,但 `bimcanvas-plugin.json` + `.claude-plugin/plugin.json` 必有):

```text
my-plugin/
├── bimcanvas-plugin.json     ← 平台权威 manifest (你唯一手写的契约文件)
├── .claude-plugin/
│   └── plugin.json           ← Claude SDK 触发器 (派生,name/description/version)
├── BIMCANVAS.md              ← 域系统提示词 (可选, 默认空)
├── agents/                   ← SubAgents 目录 (*.md, 可选)
├── skills/                   ← Skills 目录 (*/SKILL.md, 可选)
├── mcp_tools/
│   └── entry.py              ← MCP 工具入口, 暴露 register(builder) 函数 (可选)
└── projectMount/             ← 项目级脚手架 (M2 bind-time 物化, 可选)
    └── manifest.json
```

**「纯净」纪律**:plugin 目录绝不能放 `CLAUDE.md` / `settings.local.json` / `.claude/`(会污染 SDK 配置链,详见 [BYO-Plugin.md](./BYO-Plugin.md) 第 7 节)。

---

## 3. 五层投影:Plugin 怎么注入 Agent

平台启动 Agent 子进程前,装配器 `ResolvedPluginBundle` 把 `core-base + active plugin` 合并为 **5 层投影**,注入 `ClaudeAgentOptions`(无副作用纯函数):

| 投影 | 类型 | 来源 | 用途 |
|---|---|---|---|
| `systemPromptParts` | str | 两个 `BIMCANVAS.md` 拼接(带 `## BIMCanvas Core Contract` / `## Active Domain Contract: <name>` 边界标识) | `options.system_prompt` |
| `agents` | `Dict[name → AgentDefinition]` | `core-base/agents/*.md` + `active/agents/*.md`,同名走 `overrides.agents` 显式声明 | `options.agents` |
| `skillPaths` | `List[Path]` | 两个 plugin 根目录(各自含 `.claude-plugin/plugin.json` 触发器) | `options.plugins=[{type:"local", path:...}]` |
| `mcpServers` | `Dict[ns → McpServer]` | `{"canvas": core_mcp}` + `{<active.mcpNamespace>: built}`(register 模式动态构造) | `options.mcp_servers` |
| `allowedTools` | `List[str]` | 用户偏好 + plugin `permissions.allow` + SDK 内置;`permissions.deny` 最后生效 | `options.allowed_tools` |
| `diagnostics` | `List[str]` | 装配过程 warning / info | 写日志 + Web 设置页显示 |

**Bundle 在 Python Agent 侧构造**,Server 不参与(职责分离)。

详见主真理源 §3.4-§3.8。

---

## 4. 加载时序

```text
程序启动
   │
   ├─ Server 起 → 读 server_config.json.agent.activePlugin
   │
   ├─ Server 启动 Agent 子进程(传 BIMCANVAS_HOME 与 LaunchContext)
   │
   ▼
Agent 进程
   │
   ├─ loader.py: 读 plugins/core-base/{BIMCANVAS.md, agents/, skills/, .claude-plugin/}
   │            读 plugins/<active>/{BIMCANVAS.md, agents/, skills/, .claude-plugin/, mcp_tools/}
   │
   ├─ resolver: 合并为 ResolvedPluginBundle(5 层投影,见 §3)
   │
   ├─ _build_mcp_servers(launch_ctx):
   │     servers = {"canvas": build_core_canvas_mcp(launch_ctx)}
   │     if active and manifest.mcpTools:
   │         spec = importlib.util.spec_from_file_location(...)
   │         module.register(builder)        ← 在这里 plugin 代码首次被执行
   │         servers[manifest.mcpNamespace] = builder.build()
   │
   ├─ ClaudeAgentOptions(
   │      system_prompt=bundle.systemPromptParts,
   │      agents=bundle.agents,
   │      plugins=[{"type":"local","path": p} for p in bundle.skillPaths],
   │      mcp_servers=bundle.mcpServers,
   │      allowed_tools=bundle.allowedTools,
   │      setting_sources=None,             ← 关键:Plugin 旁路策略,见 BYO-Plugin §7
   │   )
   │
   └─ ClaudeSDKClient(options) → ready
```

**安全要点**:`spec.loader.exec_module(module)` 是 plugin Python 代码**第一次也是唯一一次**被执行的时机;Server 端 `ExecutablePluginProbe` 在 trust 阶段做过一次 dry-run(使用 `_FakeBuilder`),正式启动时才用真实 `McpServerBuilder` 跑。详见 [plugin-security-model.md](./plugin-security-model.md) §3。

---

## 5. 四态生命周期(总览)

每个 plugin 有 4 个独立状态轴,**互不联动**:

```text
installed         active         bound          launched
   ↓ (clone + 静态校验)
[installed + untrusted]
   ↓ ([信任并激活] + ExecutableProbe 通过)
[installed + trusted]   →   active   →   bound   →   launched
                            ↓ 写         ↓ 写        ↓ Server 生成
                       server_config  project       LaunchContext
                       .activePlugin  .scenes[]     Agent 启动
```

| 状态 | 写到哪 | 由谁触发 | 是否影响其他状态 |
|---|---|---|---|
| **installed** | `BIMCANVAS_HOME/plugins/<id>/` + `plugins-state.json` | `POST /api/plugins/install`(只调 StaticValidator,**绝不执行代码**) | 否 |
| **trustState** | `plugins-state.json[<id>].trustState` | `POST /api/plugins/{id}/trust-and-activate`(先调 ExecutableProbe) | trustState=trusted 后才可设 active |
| **active** | `server_config.json.agent.activePlugin` | `POST /api/plugins/active`(对 untrusted plugin 返回 403) | 切换需重启 Agent 才生效 |
| **bound** | `<project>/project.json.scenes[]` + `plugins.lock.json` | `POST /api/project/{id}/scenes` | 同时调 `MountSceneScaffold` 物化 projectMount(**唯一物化入口**) |
| **launched** | Agent 子进程在跑 | Server 启 Agent 时 | 写入 / Agent 通信均就绪 |

详细状态机与转换图见 [plugin-lifecycle-states.md](./plugin-lifecycle-states.md)。

---

## 6. 一次"全流程"数据流(场景 1 摘要)

新用户首次安装 + 激活 `indoor-layout`:

```text
1. 用户点 Web [+ 安装新插件] → 输入 GitHub URL
   ↓
2. POST /api/plugins/install
   ├─ Process.Start("git", ["clone", "--depth", "1", url, "<tmp>"])
   ├─ StaticPluginValidator.Validate(<tmp>)        ← 纯文本,5 条规则
   ├─ 通过 → 原子移动 <tmp> → BIMCANVAS_HOME/plugins/<id>/
   └─ 写 plugins-state.json: { trustState: "untrusted", sourceUrl, resolvedCommit, ... }
   ↓
3. Web 列表渲染:plugin 卡片显示 [未信任] 标签 + [信任并激活] 按钮
   ↓
4. 用户点 [信任并激活] → TrustAndActivateDialog 二次确认
   ├─ 展示 pluginId / sourceUrl / resolvedCommit
   └─ 黄色警告条:"激活将执行该插件 <id> 的 Python 代码"
   ↓
5. 用户确认 → POST /api/plugins/{id}/trust-and-activate
   ├─ ExecutablePluginProbe.Probe(<id>)           ← 第一次 import + dry-run
   │     spec_from_file_location → register(_FakeBuilder)
   │     收集 tool_names,检查与 core-base 冲突
   ├─ 通过 → plugins-state.json[<id>].trustState = "trusted"
   └─ server_config.json.agent.activePlugin = <id>
   ↓
6. 响应 { restartRequired: true } → Web 显示重启 banner
   ↓
7. 用户重启 → Agent 加载 ResolvedPluginBundle → 五层投影注入 → Ready
```

场景 2(同一 .bcp 跨 plugin 接力)与场景 3(本地 plugin 开发)详见主真理源 §2.2 / §2.3。

---

## 7. 平台基座 7 个 MCP 工具

`mcp__canvas__*` 是平台一次写、所有 plugin 共享的核心工具(`namespace="canvas"`,plugin 不可占用此 namespace):

| 工具 | 用途 |
|---|---|
| `screenshot` | 截图(后台 / 前台,带 viewport / shots 参数) |
| `validate_layout` | 几何 / 碰撞校验 |
| `get_zone_boundaries` | 读取设计区边界语义 |
| `save_modules` | 写 `schemes/{sceneId}/modules.json`(Server gate 强制隔离) |
| `analyze_image` | 大模型图像理解(参考图分析等) |
| **`list_project_scenes`** | 列出当前项目所有 scenes(供跨 scene 协作) |
| **`load_scene_artifact`** | 读 scene 下的 artifact;聚合读(scene 内所有同名文件)或 `path` 精确读单文件 |

Plugin 自己的 MCP 工具走自己的 namespace,如 `mcp__interior-layout__save_semantic_plan`。

### 7.1 通用 artifact IO 契约(Server 通用层 vs Plugin domain 层边界)

平台基座对 scene 业务数据只提供**通用、plugin-agnostic 的文件 IO**,不持有任何 domain 业务知识。
artifact 读写经 Server REST 端点(`load_scene_artifact` 工具背后的同一组端点):

| 端点 | 用途 |
|---|---|
| `GET /api/scheme/scenes/{sceneId}/{artifactKind}` | 聚合读 scene 内所有同名 artifact |
| `GET /api/scheme/scenes/{sceneId}/{artifactKind}?path={subPath}` | 精确读单文件 `schemes/{sceneId}/{subPath}/{artifactKind}.json` |
| `POST /api/scheme/scenes/{sceneId}/artifacts/{artifactKind}` body `{path?, content}` | 写到 `schemes/{sceneId}/{path?}/{artifactKind}.json` |

- **`artifactKind`** 是 plugin 自定义字符串,字符集 `^[a-z][a-z0-9_-]*$`。
  Reserved 通用 kind:`modules`(AI 直写 Write/Edit)、`zones`(全 scene 共享 baseline 派生)、`readme`(baseline)——这三个不允许 plugin 经 POST 写入。
  其余 kind(如 `semantic_plan` / `reference_analysis` / `points`)是 plugin domain 产物,Server 不校验其 schema、不嵌任何 domain 逻辑。
- **`path`** 是 scene namespace 内相对子路径(如 `rz_3` / `rz_3/variants/abc`),字符集 `[a-zA-Z0-9_/-]+`,禁止 `..` 穿越;解析后必须落在 `schemes/{sceneId}/` 内。
- **写入隔离**:POST 走 `ProjectContext.CheckWriteAllowed` 的 V12b 路径隔离,plugin 只能写自己 active scene 的命名空间(scene namespace 之外一律 403)。
- **变更通知**:写成功后 Server 广播通用 SignalR 事件 `SceneArtifactUpdated`,payload `{sceneId, artifactKind, path?, plugin?, timestamp}`。

> **边界纪律**:domain 业务(tag 体系、方案合并、planType 判定等)一律在 plugin 工具体 / `lib/` 内实现;
> Server 端不为任何 plugin 单开 domain controller。新 domain plugin 接入主仓库**零代码改动**。

---

## 8. 与本仓库其他文档的关系

| 文档 | 你想知道什么 |
|---|---|
| **本文档** | 平台总体长什么样 / 5 层投影是什么 / 一次完整流程怎么走 |
| [plugin-lifecycle-states.md](./plugin-lifecycle-states.md) | 四态 + trustState 的精确转换规则,每个状态对应的 API / UI 行为 |
| [plugin-security-model.md](./plugin-security-model.md) | 为什么安装不执行代码 / `plugins-state.json` 为什么放平台外 / R9 R10 防御 |
| [plugin-manifest-schema.md](./plugin-manifest-schema.md) | `bimcanvas-plugin.json` 每个字段的含义 / 校验规则 / 示例 |
| [BYO-Plugin.md](./BYO-Plugin.md) | 怎么从零写一个 plugin、上传 GitHub、本地测试、目录纯净纪律、常见错误 |
| [plugin-manifest-schema.json](./plugin-manifest-schema.json) | 机器可读 schema 本体(JSONSchema draft-07) |
| [bcp-scenes-schema.json](./bcp-scenes-schema.json) | `project.json.scenes[]` 字段 schema |

---

**End of Plugin Architecture.**
