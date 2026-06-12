# BIMCanvas 平台与 Plugin 体系

> **契约文档**:本文与代码不一致即视为 bug,改动平台/plugin 机制必须同回合更新本文。
> 整合自原 plugin-architecture / plugin-lifecycle-states / plugin-security-model / plugin-manifest-schema / BYO-Plugin / Tool_Permissions_Migration 六份文档(2026-06-12 合并,内容已对照代码校正)。
> 机器可读 manifest schema:`BIMCanvas.Server/Schemas/plugin-manifest-schema.json`(随程序复制到 `{BaseDirectory}/Schemas/`,`StaticPluginValidator` 运行时加载)。

---

## 1. 定位

**BIMCanvas = 通用 BIM-AI 平台基座 + 域插件(plugin)**:基座一次写、所有 domain 共享;每个域(室内布置 / MEP / 点位 / ……)封装为独立 plugin。平台基座**绝不内置任何 domain 知识**;所有因 domain 而异的能力(系统提示词 / SubAgents / Skills / MCP 工具 / 模块库 / 参考资料)全部走 plugin 投影。

- `core-base` 是平台自带的"真插件"(源在 `BIMCanvas.Agent/plugins/core-base/`,首启动 bootstrap 到 `BIMCANVAS_HOME/plugins/core-base/`),提供基础 chat / 机械编辑,作为无专业插件时的 fallback。
- domain plugin 各自独立 GitHub 仓库,用户 Web 端粘贴 URL 安装。当前唯一 domain plugin:`interior-layout`(本机路径 `vibedrafting/bimcanvas-plugin-interior-layout/`)。

## 2. Plugin 目录与 manifest(v3.3.2)

```text
my-plugin/
├── bimcanvas-plugin.json     ← 平台权威 manifest(唯一手写契约)
├── .claude-plugin/plugin.json ← Claude SDK 触发器(name/description/version 三字段;缺失则整个 plugin 被 SDK 跳过)
├── BIMCANVAS.md              ← 域系统提示词(存在即叠加)
├── agents/*.md               ← SubAgents(存在即扫)
├── skills/<name>/SKILL.md    ← Skills(存在即扫)
├── mcp_tools/<namespace>.py  ← MCP 工具入口;namespace 自动 = 文件名 stem
├── projectMount/manifest.json ← 项目脚手架清单
└── scenes/                   ← 可选:向平台贡献「从场景新建」模板(index.json + {id}/scene.bcp)
```

**资源路径全部约定俗成、代码写死,manifest 不声明路径**。

### manifest 字段(9 + configSchema)

| 字段 | 必填 | 说明 |
|---|---|---|
| `name` | ✅ | `^[a-z0-9-]+$`;同时是 plugins 目录名、MCP namespace 缺省来源、sceneId 前缀缺省 |
| `version` | ✅ | semver,记入 plugins-state.json / plugins.lock.json |
| `compatibility.bimcanvas` | ✅ | 平台版本 semver range |
| `displayName` / `description` | ✅ | Web 设置页展示 |
| `tools.allow / deny` | ✅ | 主控工具权限(见 §5) |
| `agents.allow / deny` | ✅ | SubAgent 装配开关(空数组 = 全装配) |
| `defaultSceneIdPattern` | 可选 | 如 `interior-layout-{n}`;缺省 `{name}-{n}` |
| `configSchema` | 可选 | 用户配置项声明(key/label/secret/required),UI 生成表单,存 `instance.config.json.pluginConfigs.{name}`,运行时 `ctx.get_config()` 读取,免重启 |
| `$schema` | 可选 | 指向 schema 文件,仅 IDE 体验 |

旧 22 字段模型(`type`/`schemaVersion`/`mcpNamespace`/`permissions`/`requires`/`web.*` 等)已于 v3.3.2 删除,**写了会被 JSONSchema 校验拒绝**(`additionalProperties: false`)。

## 3. 投影与加载

Agent 启动时(`config_bundle.py`)把 `core-base + active plugin` 合并注入 `ClaudeAgentOptions`:系统提示词(两层 `BIMCANVAS.md` 拼接落盘为 `SystemPromptFile`)、agents、skills(走 SDK `plugins=[{type:"local",path}]` 旁路 + `setting_sources=[]` 防污染)、mcp_servers(`{"canvas": core} + {<stem>: plugin}`)、allowed/disallowed_tools。装配细节与 SDK 参数归属见 `docs/Doc_SDK_Config.md`。

plugin Python 代码的执行点只有两个:trust 阶段 `ExecutablePluginProbe` dry-run(`_FakeBuilder`),与 Agent 正式启动 `register(builder)`(真实 builder)。

## 4. 平台基座 canvas 工具(5 个)

`mcp__canvas__*` 是平台核心工具,namespace `canvas` 保留、plugin 不可占用:

| 工具 | 用途 |
|---|---|
| `create_job` / `complete_job` | 并行工作环境(Git Worktree)创建与收口 |
| `canvas_vision` | 截图 / 识图 / 截图+识图 三模式 |
| `load_artifact` | 读 `schemes/` 下 artifact(聚合读或 `path` 精确读;结果上限 500K) |
| `validate_layout` | 几何 / 碰撞校验 |

domain 工具走 plugin 自己的 namespace(如 `mcp__interior-layout__get_zone_boundaries` / `register_variant` / `adopt_variant`)。

**artifact IO 契约**:`artifactKind` 为 plugin 自定义字符串(`^[a-z][a-z0-9_-]*$`),Server 只提供通用文件 IO(`GET/POST /api/scheme/artifacts/{artifactKind}`,`path` 为 `schemes/` 内相对子路径、禁 `..`),不校验 domain schema、不为任何 plugin 单开 controller;写入 gate 仅锁 `baseline/` + `computed/`;写成功广播 SignalR `SceneArtifactUpdated`。新 domain plugin 接入主仓库零代码改动。

## 5. 工具权限:fallback / 完全接管模型

```
effective.tools = active_domain_plugin.tools   (有 active 专业插件)
                = core-base.tools              (否则 fallback)
```

**不 merge、不并集**。因此专业插件的 `tools.allow` 必须列**完整清单**(内建工具 + 用到的 canvas 工具 + 自身工具),漏列即运行时 tool-not-found;core-base 新增工具时各专业插件 manifest 需跟进。禁止 `mcp__<server>` 两段简写、`*` 通配。`allow: []` = SDK 全开;deny 优先于 allow。

**SubAgent `.md` 的 `tools:` 三态**(`subagents.py`):

| 写法 | 行为 |
|---|---|
| 省略 / 空值 | 继承主控 allow + deny(deny 回填 `AgentDefinition.disallowedTools`) |
| 显式列表 | 自主清单,不继承;主控 deny 仍经全局 `disallowed_tools` 兜底 |

不变量:SDK 中 `tools: None` = inherit-all,`tools: []` = 零工具,继承装配"全开模式"必须传 `None`。

历史:工具权限源 v3.2 在 `HOME/config.json.<provider>.tools`,v3.3 起由 plugin manifest 接管;config.json 中残留 `tools`/`agents` 字段仅 warning,残留更老的 `permissions` 字段 fail-fast。

## 6. 生命周期五轴(互不联动)

| 轴 | 取值 / 落盘 | 触发 |
|---|---|---|
| installed | `BIMCANVAS_HOME/plugins/<id>/` 存在 | `POST /api/plugins/install`(clone + 静态校验,**绝不执行代码**) |
| trustState | `plugins-state.json[<id>].trustState`:`untrusted`/`trusted` | `POST /api/plugins/{id}/trust-and-activate`(ExecutableProbe 通过) |
| active | `server_config.json.agent.activePlugin`(同时最多一个) | `POST /api/plugins/active`;untrusted → 403 `plugin_not_trusted`;切换需重启 Agent |
| bound | `<project>/project.json.scenes[]` + `plugins.lock.json` | `POST /api/project/{id}/scenes` |
| launched | Agent 子进程存活 | Server 启动 Agent |

安装≠信任≠激活≠绑定≠启动。打开项目的三态:`bound`(直接进入)/ `sceneSelectRequired`(多候选弹选择)/ `requiresSceneBinding`(未命中弹绑定对话框);Server 端 `ProjectContext` 为 `Pending` 时写入全 403 `project_pending_binding`。

**projectMount 物化(2026-06-01 改版)**:打开 / 绑定项目时按**当前 activePlugin** 把 `projectMount/` 物化到**项目全局**路径(`modules/`、`references/` 等),与 validator / skills 实际读取路径一致;**仅缺失补齐、绝不覆盖**(幂等,顶层根已存在即跳过,保护用户数据——R10 防覆盖不变量)。旧"bind-time 物化到 sceneId 命名空间"方案已被取代(`ProjectFixedFilesBootstrapService.cs`)。

## 7. 安全模型(R9:安装阶段 RCE 防御)

威胁:恶意 repo 的 `mcp_tools/*.py` 顶层代码在 install-time 被 import 即 RCE。防御分层:

| 层 | 时机 | 执行代码? | 内容 |
|---|---|---|---|
| `StaticPluginValidator` | install | **绝不** | JSONSchema 校验 manifest、目录纯净检查、`mcp_tools` 路径防 `..` 逃逸。实现内禁止任何 import/exec/eval/AST 解析触达 plugin 文件 |
| `ExecutablePluginProbe` | trust-and-activate | dry-run 一次 | import 入口 + `register(_FakeBuilder)` 不抛异常 + 工具名不与 core-base 冲突 |
| 用户感知防线 | UI | — | 首次激活按钮强制文案 `[信任并激活]` + `TrustAndActivateDialog` 二次确认(展示 sourceUrl / resolvedCommit + "激活将执行该插件的 Python 代码"警告) |

**信任元数据存平台外**:`BIMCANVAS_HOME/plugins-state.json`(与 `plugins/` 同级、不在其内),唯一写入口 `PluginTrustService`(单写锁 + 原子替换);plugin 内任何伪造 trust 文件被忽略。

Phase 1 **不做沙箱**:trusted plugin 与 Agent 同进程、完全信任(类比 VS Code extension);同时只 active 一个 plugin。

## 8. 写一个 plugin(作者速查)

1. **骨架**:GitHub Template `vibedrafting/create-bimcanvas-plugin` → Use this template;或照 §2 目录手建。
2. **`register(builder)` 范式**(`mcp_tools/<namespace>.py`):

```python
def register(builder) -> None:
    ctx = builder.context          # 闭包捕获

    @builder.tool("my_tool", "描述", {...json schema...})
    async def my_tool(args: dict) -> dict:
        # ✅ ctx 字段只能在 handler 内读(运行时是真实 PluginContext)
        ...
        return {"content": [{"type": "text", "text": "OK"}]}
```

   **两条硬约束**(违反 → `plugin_probe_failed`):
   - `register` 体内**严禁读 `builder.context` 字段做条件注册**(probe 阶段是占位 context,分支会走错);
   - **严禁 `isinstance(builder, McpServerBuilder)`**(probe 传 `_FakeBuilder`,duck-typed)。

   `register` 只声明元数据,副作用(网络 / 文件 IO)一律下沉 handler。
3. **目录纯净纪律**(踩坑率最高):plugin 目录绝不能含 `CLAUDE.md` / `settings.local.json` / `.claude/`——Agent 走 Plugin 旁路加载 Skills(`setting_sources=[]`),任何多余配置文件都会污染 Agent 行为;`StaticPluginValidator` 检出即拒装(`directory_not_pure`)。
4. **本地开发**:设 `BIMCANVAS_HOME` 指向沙盒目录(解析逻辑 `BIMCanvas.Agent/src/config/loader.py:resolve_bimcanvas_home`;Windows 默认 `%USERPROFILE%\Documents\BIMCanvas\`),plugin 软链进 `plugins/`,手工把 `plugins-state.json` 标 `trustState: "trusted", sourceKind: "local"`。
5. **发布**:push 到 GitHub public repo → 用户 Web 端粘贴 URL 安装。改动 domain plugin 必须在其独立仓库内进行(主仓库不含 plugin 副本)。

### 常见错误码

| code | 处理 |
|---|---|
| `schema_validation_failed` | manifest 字段不合法(注意旧 22 字段模型已删,见 §2) |
| `directory_not_pure` | 删除 CLAUDE.md / .claude/ 等污染文件重装 |
| `path_escape` | mcp_tools 路径含 `..` 或绝对路径 |
| `namespace_conflict` | 文件名 stem 与已装 plugin 冲突或为保留字 `canvas` |
| `plugin_not_trusted` | 先走 [信任并激活] |
| `plugin_probe_failed` | §8.2 两条硬约束之一被违反 |
| `project_pending_binding` | 项目未绑 scene 时写入,先完成绑定 |

---

**相关**:流协议契约 `docs/Arch_Stream_Protocol.md`;SDK 参数配置 `docs/Doc_SDK_Config.md`;平台/插件职责划分台账 `.dev/plans/平台基座与Domain插件职责划分/`。
