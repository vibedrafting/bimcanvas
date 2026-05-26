# Plugin Manifest Schema(可读版)

> `bimcanvas-plugin.json` 字段字典。机器可读 schema(JSONSchema draft-07)见 [plugin-manifest-schema.json](./plugin-manifest-schema.json),本文是其人类可读对照表。
>
> Plugin 作者只需手写 `bimcanvas-plugin.json`,**不**手写 `.claude-plugin/plugin.json`(由 `bimcanvas-plugin-validate` CLI 派生)。
>
> 主真理源 v1.1 §3.1 / §3.12 / §6.2。

---

## 1. 必填字段(5 个)

| 字段 | 类型 | 校验 | 含义 |
|---|---|---|---|
| `name` | string | `^[a-z0-9-]+$`,1-64 字符 | Plugin 唯一标识。会作为 `BIMCANVAS_HOME/plugins/<name>/` 目录名;也是 `mcpNamespace` 缺省值的来源。 |
| `version` | string | semver(`\d+\.\d+\.\d+(-pre)?(+build)?`) | Plugin 自身版本号。被记入 `plugins-state.json.installedVersion` 与 `plugins.lock.json.version`。 |
| `type` | string | const = `"bimcanvas-plugin"` | 固定常量,用于 `StaticPluginValidator` 第一步 quick-reject 非 BIMCanvas plugin。 |
| `schemaVersion` | integer | const = `1` | 本 manifest schema 的版本号。Phase 1 固定为 1;未来破坏性变更走 `schemaVersion=2` + 6 个月过渡期(R3)。 |
| `compatibility.bimcanvas` | string | 非空 semver range | Plugin 与平台基座的兼容性声明,如 `"^1.0.0"` / `">=1.0 <2.0"`。`PluginLifecycleService` 启动 Agent 前据此决定是否拒绝加载。 |

---

## 2. 可选字段

### 2.1 展示元数据

| 字段 | 类型 | 校验 | 含义 |
|---|---|---|---|
| `displayName` | string | 1-80 字符 | Web 设置页 + 顶部 active plugin 标签显示的人类可读名称。缺省时 UI fallback 到 `name`。 |
| `description` | string | ≤ 1024 字符 | Web 设置页 plugin 卡片摘要。一句话定位即可,详细文档放 `README.md` / `homepage`。 |
| `homepage` | string | URI 格式 | Plugin 主页 / 仓库 URL。Web 设置页提供「打开主页」链接。 |

### 2.2 内容入口(相对 plugin 根)

| 字段 | 类型 | 默认 | 校验 | 含义 |
|---|---|---|---|---|
| `systemPrompt` | string | `"BIMCANVAS.md"` | 非空,以 `.md` 结尾 | 域 system prompt 文件相对路径。运行时与 core-base 的 `BIMCANVAS.md` 拼接,边界标识 `## Active Domain Contract: <name>` 硬性插入。 |
| `agents` | string | `"agents/"` | 非空目录路径 | SubAgents 目录。`loader.py` 显式 `glob *.md` 解析,**SDK plugin 机制不扫描 agents**(主真理源 §3.6)。 |
| `skills` | string | `"skills/"` | 非空目录路径 | Skills 目录。走 SDK `plugins=[{type:"local", path:...}]` 自动扫描(主真理源 §3.7);要求 plugin 根含 `.claude-plugin/plugin.json` 触发器。 |
| `mcpTools` | string | 无 | `^(?!.*\.\.)[^/\\][^\s]*\.py$`,即 `.py` 结尾、绝不含 `..`、绝不以 `/` `\` 开头 | Plugin MCP 工具入口 Python 文件相对路径。运行时由 `_build_mcp_servers` 通过 `importlib.util.spec_from_file_location` 加载,文件中需暴露 `register(builder)` 函数。 |
| `projectMount.manifest` | string | 无 | 相对路径,非 `..` 逃逸 | projectMount manifest 文件路径,声明 plugin 想物化到 `.bcp` 项目下的文件清单。M2 bind-time 由 `MountSceneScaffold` 物化到 sceneId 命名空间。 |

### 2.3 MCP 命名空间

| 字段 | 类型 | 校验 | 含义 |
|---|---|---|---|
| `mcpNamespace` | string | `^[a-z0-9-]+$`,1-32 字符,**不能为 `canvas`** | Plugin MCP server 的 namespace key,决定 LLM 可见的工具调用名 `mcp__<namespace>__<tool>`。`canvas` 是核心基座保留 namespace。 |

**为什么 `canvas` 保留**:core-base 的 7 个底座工具(`mcp__canvas__screenshot` 等)全部走该 namespace;plugin 占用会直接覆盖核心工具,引发不可预测后果。

**为什么唯一性**:Bundle 装配阶段把所有 mcpServer 合并到 `Dict[ns → server]`,key 冲突会导致后注册的覆盖先注册的;`StaticPluginValidator` 在 install-time 检测已安装 plugin 的 namespace,提前拒绝。

### 2.4 权限声明

| 字段 | 类型 | 含义 |
|---|---|---|
| `permissions.allow` | string[] | 白名单工具名,如 `"Read"` / `"Bash(git status)"` / `"mcp__canvas__list_project_scenes"`。 |
| `permissions.deny` | string[] | 黑名单工具名,**deny 最后生效**(优先级高于 allow)。 |

`loader.py` 动态聚合三层:用户偏好 + 本字段 + SDK 内置。

### 2.5 依赖声明

| 字段 | 类型 | 含义 |
|---|---|---|
| `requires.platformTools` | string[] | core-base MCP 工具名集合,如 `"mcp__canvas__load_scene_artifact"` / `"mcp__canvas__list_project_scenes"`。若声明的工具不存在,`StaticPluginValidator` 拒绝安装。 |

### 2.6 稳定性 / 成熟度

| 字段 | 类型 | 枚举 | 含义 |
|---|---|---|---|
| `referenceStability` | string | `frozen` / `semver-tracked` / `experimental` | Plugin 内对外发布的参考资料(references / module_library / prompts)的稳定性承诺。`frozen` = 一旦发布不变;`semver-tracked` = 按 semver 演化;`experimental` = 随时可能 breaking。 |
| `maturity` | string | `experimental` / `beta` / `stable` | Plugin 整体成熟度。Web 设置页据此显示徽章;dogfood 阶段建议 `experimental`。 |

### 2.7 Web 展示元数据(M1 设置页 + Phase 2 banner 扩展)

| 字段 | 类型 | 含义 |
|---|---|---|
| `web.displayName` | string | 覆盖 `displayName`,**仅用于 Web UI**;CLI / 日志仍用根 `displayName`。 |
| `web.icon` | string | Plugin 图标:相对路径(相对 plugin 根)或 emoji。 |
| `web.category` | string | 插件分类标签,如 `"residential"` / `"mep"` / `"construction"`,用于设置页分组与搜索。 |
| `web.settingsHints` | string | Plugin 设置页面提示文字,M1 阶段仅占位。 |

### 2.8 Scene 命名

| 字段 | 类型 | 含义 |
|---|---|---|
| `defaultSceneIdPattern` | string | 新建 scene 时的 sceneId 默认 pattern,如 `"interior-layout-{n}"` / `"electrical-points-{n}"`;`{n}` 由 Server 在 `POST /api/project/{id}/scenes` 内推断递增。用户可手改,但需匹配 `^[a-z0-9-]+$` 且项目内唯一(主真理源 §3.9)。 |

### 2.9 SubAgent / Skill 覆盖声明

| 字段 | 类型 | 含义 |
|---|---|---|
| `overrides.agents` | string[] | 覆盖 core-base 的 SubAgent 文件名(不含 `.md` 后缀),如 `["layout-agent"]`。 |
| `overrides.skills` | string[] | 覆盖 core-base 的 Skill 目录名,如 `["generate-planning"]`。 |

**为什么必须显式声明**:`loader.py` 检测到两 plugin 目录有同名 agent / skill 但未声明 overrides 时,**直接抛 `OverrideNotDeclaredError`** 拒绝启动 Agent。这避免了 plugin 作者无意覆盖核心能力造成隐蔽 bug。详见主真理源 §3.6。

---

## 3. 完整最小示例

```json
{
  "name": "my-plugin",
  "version": "0.1.0",
  "type": "bimcanvas-plugin",
  "schemaVersion": 1,
  "compatibility": { "bimcanvas": "^1.0.0" }
}
```

这是合法的最小 manifest —— 只声明了一个无 MCP 工具、无 SubAgent、无系统提示词的"空" plugin。`StaticPluginValidator` 会通过,`mcpNamespace` 缺省为 `"my-plugin"`,但因为没有 `mcpTools` 字段,`_build_mcp_servers` 不会尝试 import 任何 Python 文件。

---

## 4. 完整全字段示例

```json
{
  "name": "indoor-layout",
  "version": "1.0.0",
  "type": "bimcanvas-plugin",
  "schemaVersion": 1,
  "compatibility": { "bimcanvas": "^1.0.0" },

  "displayName": "室内布置",
  "description": "BIMCanvas 室内家具布置域 plugin (参考方案 + 智能布置 + 多分区并行)",
  "homepage": "https://github.com/vibedrafting/BIMCanvas-IndoorLayout",

  "systemPrompt": "BIMCANVAS.md",
  "agents": "agents/",
  "skills": "skills/",
  "mcpTools": "mcp_tools/entry.py",
  "mcpNamespace": "indoor-layout",

  "projectMount": {
    "manifest": "projectMount/manifest.json"
  },

  "permissions": {
    "allow": [
      "Read",
      "mcp__canvas__list_project_scenes",
      "mcp__canvas__load_scene_artifact"
    ],
    "deny": []
  },

  "requires": {
    "platformTools": [
      "mcp__canvas__load_scene_artifact",
      "mcp__canvas__list_project_scenes"
    ]
  },

  "referenceStability": "semver-tracked",
  "maturity": "stable",

  "web": {
    "displayName": "室内布置",
    "icon": "🛋️",
    "category": "residential"
  },

  "defaultSceneIdPattern": "interior-layout-{n}",

  "overrides": {
    "agents": [],
    "skills": []
  }
}
```

---

## 5. 校验 CLI(`bimcanvas-plugin-validate`)

Plugin 作者本地开发时可跑校验工具(同时跑 Static 与 Executable 两道防线):

```bash
bimcanvas-plugin-validate ./my-plugin/
```

输出:

- ✅ `bimcanvas-plugin.json` 通过 JSONSchema 校验
- ✅ 目录纯净(无 `CLAUDE.md` / `settings.local.json` / `.claude/`)
- ✅ `mcpTools` 路径合法
- ✅ `register(builder)` 函数存在,dry-run 通过
- ❌ 任何失败会给出具体行号 + 建议

工具会顺便派生 `.claude-plugin/plugin.json`(从 `bimcanvas-plugin.json` 的 `name` / `description` / `version` 字段)。

---

## 6. 字段演化承诺

Phase 1(`schemaVersion=1`):

- 上表所有字段在 Phase 1 内**只增不删**
- 现有字段的 **校验规则只放松不收紧**(避免破坏已发布 plugin)
- 新字段必须 optional + 有合理默认行为

Phase 2 引入破坏性变更(如收紧 enum 范围 / 改字段语义):

- 升 `schemaVersion=2`
- 至少 6 个月过渡期内同时支持 v1 与 v2
- 公开升级指南

详见主真理源 §6.1 R3 + §6.3 字段表。

---

## 7. 与 `.claude-plugin/plugin.json` 的关系

| 文件 | 角色 | 作者手写? | 字段 |
|---|---|---|---|
| `bimcanvas-plugin.json` | 平台权威 manifest | ✅ 手写 | 见本文 |
| `.claude-plugin/plugin.json` | Claude SDK 触发器 | ❌ 工具派生 | `name` / `description` / `version` 三个最小字段 |

`.claude-plugin/plugin.json` 的存在让目录被 SDK 识别为 local plugin(触发 Skills 扫描);它的字段集是 Anthropic 的(不是 BIMCanvas 平台契约)。

---

**End of Plugin Manifest Schema.**
