# Bring Your Own Plugin

> Plugin 作者从零开始写一个 BIMCanvas plugin 的完整指南。
>
> 前置阅读:[plugin-architecture.md](./plugin-architecture.md)(平台总览)+ [plugin-manifest-schema.md](./plugin-manifest-schema.md)(字段字典)。
>
> 配套素材:`.dev/templates/create-bimcanvas-plugin/`(本仓库内,后续会推到 `vibedrafting/create-bimcanvas-plugin` GitHub Template Repository)。

---

## 1. 三步流程

### 1.1 Use this template(GitHub)

在 GitHub 打开 [`vibedrafting/create-bimcanvas-plugin`](https://github.com/vibedrafting/create-bimcanvas-plugin) → 点 **Use this template** → 选 **Create a new repository** → 命名你的 plugin repo(如 `BIMCanvas-MyPlugin`)。

你会得到一个完整骨架,含:

- `bimcanvas-plugin.json`(必填字段填了示例值)
- `.claude-plugin/plugin.json`(派生)
- `BIMCANVAS.md`(占位提示词 + `## TODO` 标记)
- `agents/README.md`(说明用法,目录为空)
- `skills/example-skill/SKILL.md`(echo 示范)
- `mcp_tools/example.py`(register(builder) 示范,**重点参考本文件**)
- `projectMount/{manifest.json, README.md}`
- `README.md`(三步流程提示)
- `.gitignore`(预禁 `CLAUDE.md` / `.claude/` 等污染文件)
- `.dev-home/plugins/<self>/README.md`(沙盒入口说明)

### 1.2 本地测试

`git clone` 你刚创建的 repo,修改占位文本:

```bash
git clone https://github.com/<your-org>/BIMCanvas-MyPlugin.git
cd BIMCanvas-MyPlugin
```

按 `mcp_tools/example.py` 的注释写你自己的 `register(builder)`。改完后,在沙盒 `BIMCANVAS_HOME` 下启 BIMCanvas:

```bash
# Linux / macOS
export BIMCANVAS_HOME="$(pwd)/.dev-home"

# Windows PowerShell
$env:BIMCANVAS_HOME = "$PWD\.dev-home"

cd <path-to-BIMCanvas-checkout>
dotnet run --project BIMCanvas.Server
```

沙盒细节见 §5。

### 1.3 发布到 GitHub

测试通过 → 推送(Use this template 已经初始化了 git):

```bash
git add .
git commit -m "initial plugin"
git push -u origin main
```

接下来在 plugin repo 的 README 写一句:**「在 BIMCanvas Web 端 → 设置 → 插件管理 → `[+ 安装新插件]` → 粘贴 `https://github.com/<your-org>/BIMCanvas-MyPlugin`」**,你的 plugin 就可以被任何用户安装。

---

## 2. `register(builder)` 模式

每个 plugin 的 `mcp_tools/<entry>.py` 必须暴露一个 `register(builder)` 函数。SDK 公开 API:

```python
from bimcanvas_plugin_sdk import McpServerBuilder, PluginContext


def register(builder: McpServerBuilder) -> None:
    """builder 提供:
    - builder.tool(name, description, schema) 装饰器
    - builder.context: PluginContext 实例(运行时是真实 context;dry-run 阶段是占位)
    - builder.build(): 构造 in-process MCP server(平台内部调用,你不需要)
    """

    ctx = builder.context  # 闭包捕获;⚠️ register 体内不能读 ctx 字段(见 §4)

    @builder.tool(
        "my_tool",
        "工具的人类可读描述,影响 LLM 是否选用本工具",
        {
            "type": "object",
            "properties": {
                "arg1": {"type": "string", "description": "..."},
            },
            "required": ["arg1"],
            "additionalProperties": False,
        },
    )
    async def my_tool(args: dict) -> dict:
        # ✅ 在 handler 内读 ctx 字段(运行时是真实 PluginContext)
        async with ctx.session.post(
            f"{ctx.server_url}/api/my-endpoint",
            json={"arg1": args["arg1"], "sceneId": ctx.active_scene_id},
        ) as resp:
            data = await resp.json()

        return {
            "content": [{"type": "text", "text": f"OK: {data}"}],
        }
```

工具调用名规则:`mcp__<mcpNamespace>__<tool_name>`。如 `mcpNamespace = "my-plugin"` + `tool name = "my_tool"` → LLM 调用 `mcp__my-plugin__my_tool`。

---

## 3. 工具元数据 vs 工具 handler

理解这两层是写好 plugin 的关键:

| 层 | 何时执行 | 能做什么 | 能读 `ctx` 吗? |
|---|---|---|---|
| **`register(builder)` 函数体** | 1. ExecutablePluginProbe trust 阶段(dry-run,使用 `_FakeBuilder`)<br>2. Agent 正式启动时(使用真实 builder) | **只声明工具元数据**(name / description / schema);不做副作用 | ❌ **绝不读** ctx 字段 |
| **`async def my_tool(args)` handler 函数体** | 每次 LLM 调用本工具时 | 实际执行业务逻辑;可读写 / 调 API / 用 ctx | ✅ 可读 ctx 全部字段(运行时已注入真实 context) |

**为什么这样区分**:`register` 在 dry-run 阶段必须能跑通(否则 plugin 永远拿不到 `trusted`),而 dry-run 阶段 context 是占位的。把 ctx 读取下沉到 handler,既能通过 dry-run,又能在运行时拿到真实数据。

---

## 4. Plugin 作者必读 — `register` 函数体内两条硬约束

> ⚠️ **这两条是 dogfood 阶段最常踩的坑**。Template 仓库的 `mcp_tools/example.py` 已经在注释里反复强调。

### 约束 1:`register` 函数体内严禁访问 `builder.context` 任何字段做条件注册

**错误示例**:

```python
def register(builder: McpServerBuilder) -> None:
    ctx = builder.context
    if ctx.active_scene_id == "interior-layout-1":   # ❌ 错!
        @builder.tool("scene_specific_tool", ...)
        async def f(args): ...
```

**理由**:平台用 `ExecutablePluginProbe` 在 trust 阶段做 dry-run 时,会传入一个最小占位
`PluginContext`(空字符串 / `None` / 占位 logger / `session=None`)。`register` 函数体内
读 context 字段会拿到占位值,条件分支走错 —— 你以为永远注册的工具实际上从未被注册,
或反之。

**正确做法**:`register` 只声明工具元数据(名字 / schema / description),context 字段
读取必须在工具 handler 内进行(运行时 SDK 注入真实 context):

```python
def register(builder: McpServerBuilder) -> None:
    @builder.tool("my_tool", ...)
    async def my_tool(args, ctx):                    # ✅ handler 内读 context
        if ctx.active_scene_id == "interior-layout-1":
            ...
```

### 约束 2:`register` 函数体内严禁 `isinstance(builder, McpServerBuilder)`

**错误示例**:

```python
def register(builder):
    assert isinstance(builder, McpServerBuilder)     # ❌ 错!Probe 阶段会失败
```

**理由**:`ExecutablePluginProbe` 当前使用 `_FakeBuilder` 做 dry-run(避免在 C# 子进程内
引入 Python SDK 依赖),fake builder **不是** `McpServerBuilder` 子类。Plugin 作者必须
接受 `builder` 是 duck-typed 协议对象。

dogfood 阶段如果 `_FakeBuilder` 出现误报,平台会切换到真实 `McpServerBuilder`(组3 已
确认 API 已稳定支持此切换),届时本约束可放宽。Phase 1 保守。

---

## 5. `.dev-home` 沙盒用法

`BIMCANVAS_HOME` 是平台运行时根目录(默认 Windows `%USERPROFILE%\Documents\BIMCanvas\` /
Linux `~/.bimcanvas/`)。Plugin 作者开发时不应污染这个全局目录,Template 自带
`.dev-home/` 沙盒就是给你单独的根。

### 5.1 启动方式

```bash
# Linux / macOS
export BIMCANVAS_HOME="$(pwd)/.dev-home"
cd <path-to-BIMCanvas-checkout>
dotnet run --project BIMCanvas.Server

# Windows PowerShell
$env:BIMCANVAS_HOME = "$PWD\.dev-home"
cd <path-to-BIMCanvas-checkout>
dotnet run --project BIMCanvas.Server
```

`BIMCANVAS_HOME` 的解析逻辑在 `BIMCanvas.Agent/src/config/loader.py` 函数
`resolve_bimcanvas_home`:

```python
def resolve_bimcanvas_home() -> Path:
    configured_home = os.getenv("BIMCANVAS_HOME", "").strip()
    if configured_home:
        return Path(os.path.expandvars(os.path.expanduser(configured_home))).resolve()

    if os.name == "nt":
        return (Path.home() / "Documents" / "BIMCanvas").resolve()
    return (Path.home() / ".bimcanvas").resolve()
```

### 5.2 把 plugin 项目根挂到沙盒

详细操作见 Template 中 `.dev-home/plugins/my-plugin/README.md`。推荐**软链**方式,改代码即时生效。

### 5.3 标记 trusted(绕过 GitHub install 流程)

本地开发态可以直接编辑 `.dev-home/plugins-state.json`,把你的 plugin 标记为
`trustState: "trusted" + sourceKind: "local"`。具体格式见 plugin-security-model.md §4.1。

`sourceKind: local` 会在 UI 上标记「复现性较弱」 —— 因为没有 GitHub URL 与
`resolvedCommit`,接收方无法重建相同环境。**只在你自己机器上有效**,不要把该状态推到
公开 plugin repo。

---

## 6. 上传 GitHub(手动 push 三行)

Phase 1 不做"一键发布",手动 push 即可:

```bash
git init                       # 如果你是从 Template 创建的, 已经 init 过
git remote add origin https://github.com/<your-org>/<your-plugin-repo>.git
git add .
git commit -m "initial plugin"
git push -u origin main
```

之后任何用户在 BIMCanvas Web 端粘贴你的 repo URL → `[+ 安装新插件]` 即可安装。

Phase 2 视社区反馈可能加 Web `[新建本地]` / `[校验]` / `[导出 zip]` / `[打开目录]` 四个
高级按钮,届时不需要 CLI 也能 develop。

---

## 7. 目录纯净纪律 ⚠️(首页警告级)

**这一条踩坑率最高**。BIMCanvas Agent 用 Claude Plugin 旁路策略加载 Skills(主真理源
§3.7 + Agent README 开发难点 #4),这要求 **plugin 目录绝对纯净**:

### 7.1 绝不能放的文件

- `CLAUDE.md`(在任何位置)
- `settings.local.json`
- `.claude/`(任何隐藏目录)
- `.bimcanvas/install.json` 或任何尝试伪造 trust 状态的文件(详见 plugin-security-model.md §4.3)

### 7.2 现象

`BIMCanvas.Agent/README.md` 开发难点 #4 原文:

> **现象**:使用 `setting_sources=["project"]` 加载 Skills 时,CLI 同时注入了
> `~/.claude/CLAUDE.md` 全局配置(Git 存档规则、MSBuild 路径等),导致 Agent 行为异常
>
> **根因**:SDK 的 `setting_sources` 是粗粒度开关,无法单独加载 Skills 而不加载其他
> 配置。即使只设 `"project"`,CLI 仍扫描全局 CLAUDE.md
>
> **方案**:**Plugin 旁路策略** —— `setting_sources=None`(不加载任何配置)+
> `plugins=[{"type": "local", "path": "<BIMCANVAS_HOME>"}]`(通过 Plugin 机制独立
> 加载 Skills)
>
> **状态**:✅ 已解决(HTTP 抓包验证零污染)

正是因为 BIMCanvas 走了这条旁路,所以**任何 plugin 目录内多余的配置文件都会污染 Agent
行为**。

### 7.3 强制执行

- `StaticPluginValidator`(install-time)的规则 (b) 会扫描 plugin 根目录,发现上述任一
  文件 → **拒绝安装** + 返回 `code: "directory_not_pure"`
- Template 自带的 `.gitignore` 已经预禁这些路径
- `bimcanvas-plugin-validate ./` CLI 本地校验时也会跑这条规则

---

## 8. 常见错误对照表

| Server 返回 code | 错误信息 | 你应该 |
|---|---|---|
| `invalid_request` | repoUrl 必须非空 | 检查你粘贴的 URL 是否真的是 GitHub HTTPS 地址 |
| `schema_validation_failed` | `bimcanvas-plugin.json` 字段不合法 | 看响应 `details` 数组,逐条修复;参考 [plugin-manifest-schema.md](./plugin-manifest-schema.md) 字段字典 |
| `directory_not_pure` | plugin 目录含 `CLAUDE.md` / `.claude/` 等污染文件 | 见 §7。删除这些文件,重新 push,重装 |
| `path_escape` | `mcpTools` 路径含 `..` 或绝对路径 | 改为相对 plugin 根的纯相对路径,如 `mcp_tools/entry.py` |
| `namespace_conflict` | 你的 `mcpNamespace` 与已安装 plugin 冲突 或 等于保留字 `canvas` | 改 `bimcanvas-plugin.json` 的 `mcpNamespace` 字段 |
| `overrides_declaration` | 你的 agent / skill 与 core-base 同名但 manifest 没声明 overrides | 在 `bimcanvas-plugin.json` 的 `overrides.agents` / `overrides.skills` 数组中显式列出同名条目;或者改你的文件名避免冲突 |
| `plugin_not_trusted` | 试图把 untrusted plugin 设为 active | 先点 `[信任并激活]` 走二次确认对话框 |
| `project_pending_binding` | 项目未绑定 scene 时写入 | 在 Web 端 `SceneBindingDialog` 中选 `[新增 scene]` 完成绑定 |
| `plugin_clone_failed` | git clone 失败 | 检查 URL 是否拼写正确 / 仓库是否 public / 网络是否通 |
| `plugin_probe_failed` | ExecutablePluginProbe dry-run 失败(register 抛异常 / 工具名冲突) | 看响应详情。最常见原因是 §4 两条硬约束之一被违反 |
| `plugin_not_found` | 操作了一个不存在的 plugin id | 用 `GET /api/plugins` 确认 id 拼写 |

---

## 9. 测试 plugin 是否工作

启动 BIMCanvas + 沙盒 → Web 顶栏切到 active = your-plugin → 在 chat 发一条触发你的
Skill 的消息(比如 Template 默认是 `echo hello`)。

如果工具调用成功,返回会带:

```
[my-plugin] hello
(plugin_id=my-plugin, scene_id=None, server_url=http://...)
```

如果没成功,按 §8 表格排查。

---

## 10. 进阶:跨 scene 协作

如果你的 plugin 想读其他 plugin 写的数据(例如点位 plugin 想看家具 plugin 的布局做底图),
用平台基座的两个新 MCP 工具:

```python
# 在你的 handler 内
async def my_handler(args, ctx):
    # 列出当前项目所有 scenes
    scenes_resp = await call_tool(
        "mcp__canvas__list_project_scenes", {}
    )
    # → [{sceneId, scene, plugin: {id, versionRange}, status, createdAt, isActive}]

    # 读其他 scene 的 artifact:聚合读(scene 内所有同名文件)
    modules = await call_tool(
        "mcp__canvas__load_scene_artifact",
        {"sceneId": "interior-layout-1", "artifactKind": "modules"}
    )
    # 或 path 精确读单文件 schemes/{sceneId}/{path}/{artifactKind}.json
    plan = await call_tool(
        "mcp__canvas__load_scene_artifact",
        {"sceneId": "interior-layout-1", "artifactKind": "semantic_plan", "path": "rz_3"}
    )
```

`artifactKind` 是 plugin 自定义字符串(字符集 `^[a-z][a-z0-9_-]*$`),无需在任何地方预注册。
Reserved 通用 kind:`modules` / `zones` / `readme`(baseline 派生 / AI 直写)。
其余 kind(`semantic_plan` / `reference_analysis` / 你自己的 `points` 等)是 plugin domain 产物。
读写契约(通用端点、`path` 子路径、写入隔离、`SceneArtifactUpdated` 通知)见
[plugin-architecture.md](./plugin-architecture.md) §7.1。

**plugin 自包业务纪律**:你的 domain 业务(数据 schema、tag 体系、方案合并 / 校验等)必须在
plugin 工具体 / `lib/` 内实现,通过通用 artifact 端点落盘——
**禁止依赖 Server 端为某 plugin 单开的 domain controller**(平台基座不再提供此类入口)。
写自己的 artifact 用 `POST /api/scheme/scenes/{sceneId}/artifacts/{artifactKind}`(`load_scene_artifact` 的写对端)。

跨 scene 读不影响 Server 写入 gate —— 你**只能写**你自己 active scene 的命名空间,
读则不限。

---

## 11. 反馈

发现本文档错漏 / Template 字段表达不清 / 错误信息没指向修复方向,请到 BIMCanvas 主仓库
开 Issue。dogfood 阶段反馈直接驱动本文档与错误信息的优化。

---

**End of Bring Your Own Plugin.**
