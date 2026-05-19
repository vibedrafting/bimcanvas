# 工具权限配置迁移指南 (v3.2 / v3.3)

> 适用版本:BIMCanvas 工具权限配置 v3.2 与 v3.3 (含 v3.3.2 manifest schema 精简)
>
> 适用人群:从 v3.1 或更早版本升级的 BIMCanvas 用户 / 开发者,以及从 v3.2 升级到 v3.3 的开发者
>
> 设计稿参考:`.dev/plans/工具权限配置系统/工具权限_v3.3_plugin-manifest-接管.md` (最新), `.dev/plans/工具权限配置系统/工具权限配置系统重设计.md` (v3.2)

---

## ⭐ v3.2 → v3.3 升级说明 (2026-05-20)

### 0. 一句话变化

**v3.2**:工具权限源 = `<HOME>/config.json.<provider>.tools / agents`。
**v3.3**:工具权限源 = `<HOME>/plugins/<id>/bimcanvas-plugin.json` 的 `tools / agents` 字段。`config.json` 退化为纯 Provider 连接配置。

### 1. 关键模型反转:**Fallback / 完全接管**(不是 merge)

v3.3 引入 **active 专业插件 100% 接管主控权限** 模型:

```
effective.tools = active_domain_plugin.tools    if 有 active 专业插件
                = core-base.tools                否则 (fallback)
```

**不做并集 / 不 merge / 不去重**。装了 interior-layout 后:
- 主控的 `allowed_tools` 完全 = `interior-layout.tools.allow`
- core-base 提供的 `mcp__canvas__*` 工具**不会自动加入** —— interior-layout 必须自己在 `tools.allow` 里列出每一个用得到的 canvas 工具
- 卸载 interior-layout → 主控权限自动 fallback 到 core-base.tools

**直接后果**:专业插件的 `tools.allow` 必须列**完整工具集**(内建 9 + 用到的 canvas 工具 + 自身 MCP 工具)。漏列任何工具,主控运行时 tool-not-found。

### 2. 用户行动

#### HOME/config.json 中的 tools/agents 字段

- v3.3 升级后**可以删除**这两个字段(不删除也行,loader 启动时 warning 提示)
- 不会阻断启动(C3 警告非 fail-fast)

#### 自定义工具权限

旧:编辑 HOME/config.json 的 `<provider>.tools.allow`
新:编辑 **当前 active plugin** 的 `<HOME>/plugins/<id>/bimcanvas-plugin.json` 的 `tools.allow`
- 装了 interior-layout → 改 `<HOME>/plugins/interior-layout/bimcanvas-plugin.json`
- 没装专业插件 → 改 `<HOME>/plugins/core-base/bimcanvas-plugin.json`(仅作 fallback)
- **注意**:有 active 专业插件时改 core-base.tools **不会影响主控运行**

### 3. plugin manifest schema 大精简 (v3.3.2)

`bimcanvas-plugin.json` 从 22 字段精简到 **9 字段**,大量过度设计字段已删除:

**删除字段**(写在 manifest 里会被 JSON Schema 校验拒绝):
`type` / `schemaVersion` / `systemPrompt` / `agents`(原字符串路径) / `skills`(原字符串路径) / `mcpTools` / `mcpNamespace` / `projectMount.manifest`(对象) / `requires` / `permissions` / `referenceStability` / `maturity`(顶层) / `homepage` / `web.*` 整块

**保留 9 字段**:`name` / `version` / `compatibility.bimcanvas` / `displayName` / `description` / `tools` / `agents` / `defaultSceneIdPattern`(可选) / `$schema`(可选)

**约定俗成的资源路径**(代码写死,manifest 不再声明):
| 路径 | 用途 |
|---|---|
| `BIMCANVAS.md` | system prompt,存在即叠加 |
| `agents/*.md` | SubAgent 定义,存在即扫 |
| `skills/<name>/SKILL.md` | Skills,存在即扫 |
| `mcp_tools/<namespace>.py` | MCP server 入口。**namespace 自动 = 文件名 stem**(如 `mcp_tools/interior-layout.py` → namespace `interior-layout`) |
| `projectMount/manifest.json` | 项目脚手架 manifest |

### 4. 行为变化

#### 装/卸 plugin 无须配置同步
旧:装 plugin 后用户要手工抄工具名到 HOME/config.json.tools.allow
新:plugin 装上权限自动就位,卸载自动消失。**零配置同步**。

#### plugin 作者维护负担
旧:plugin 只列自己注册的 MCP 工具
新:plugin 必须维护完整工具清单(含 core-base 工具),**core-base 升级新增工具时,所有专业插件 manifest 需要跟着更新**(fallback 模型的代价)

#### MCP namespace 命名约定
旧:在 manifest 里写 `mcpNamespace: "interior-layout"`
新:**约定 `mcp_tools/<namespace>.py` 文件名 stem 即 namespace**。改 namespace 就改文件名,不再有独立字段

### 5. 完整 23 项 plugin manifest 示例(interior-layout)

```jsonc
{
  "$schema": "../../../../docs/plugin-manifest-schema.json",

  "name": "interior-layout",
  "version": "1.0.0",
  "compatibility": { "bimcanvas": "^1.0.0" },

  "displayName": "🛋️ 室内布置设计",
  "description": "BIMCanvas 室内家具布置 domain plugin",

  "tools": {
    "allow": [
      "Read", "Write", "Edit", "Bash", "Glob", "Grep",
      "Task", "Skill", "AskUserQuestion",
      "mcp__canvas__request_background_screenshot",
      "mcp__canvas__validate_layout",
      "mcp__canvas__get_zone_boundaries",
      "mcp__canvas__register_variant",
      "mcp__canvas__list_variants",
      "mcp__canvas__analyze_image",
      "mcp__canvas__create_job",
      "mcp__canvas__complete_job",
      "mcp__canvas__list_project_scenes",
      "mcp__canvas__load_scene_artifact",
      "mcp__interior-layout__save_semantic_plan",
      "mcp__interior-layout__load_semantic_plan",
      "mcp__interior-layout__load_reference_analysis",
      "mcp__interior-layout__save_reference_analysis"
    ],
    "deny": []
  },
  "agents": { "allow": [], "deny": [] },

  "defaultSceneIdPattern": "interior-layout-{n}"
}
```

### 6. v3.2 → v3.3 HOME/config.json diff 示例

```diff
 {
   "runtimeProvider": "claude",
   "claude": {
     "baseUrl": "...",
     "apiKey": "...",
     "defaultModel": "opus",
-    "tools": {
-      "allow": [...19 项...],
-      "deny": []
-    },
-    "agents": { "allow": [], "deny": [] },
     "modelMapping": { ... }
   },
   "openai": { /* 同样删 tools / agents */ }
 }
```

(留着不删也行,loader 会 warning 提示但不阻断启动。)

### 7. SDK 0.1.41 已知约束

`AgentDefinition` 字段不含 `disallowedTools`。所有 plugin manifest 的 `tools.deny` 经 merge / fallback 后,最终只走 `ClaudeAgentOptions.disallowed_tools` 全局通道(对主控 + SubAgent 派发的工具调用统一拦截)。**不存在 per-SubAgent deny**。SubAgent `.md` frontmatter 不接受 `disallowedTools` / `toolsDeny` 字段。

---

## (v3.1 → v3.2 历史升级指南)

---

## 0. 为什么有这份文档

升级到 v3.2 后启动 BIMCanvas，如果你的 `<BIMCANVAS_HOME>/config.json`（默认在 `%USERPROFILE%\Documents\BIMCanvas\config.json`）还是旧 schema，会**立即触发 fail-fast 错误**：

```
检测到 config.json 含旧版 `claude.permissions` 字段。
工具权限配置已重设计 (v3.2)，请参考迁移文档手工调整:
  docs/Tool_Permissions_Migration.md
```

这份文档就是手工迁移的操作手册。**不提供自动迁移脚本**，理由见 §5。

---

## 1. 必读：行为变化警告

v3.2 删除了 `main_agent.py` 里"主控自动合入所有 MCP 工具名"的隐式逻辑。**plugin 注册的 MCP 工具不再自动进入主控的 allow 列表**。

具体影响：

- 装了 `interior-layout` plugin 后，主控**不能直接调** `mcp__interior-layout__save_semantic_plan` / `load_semantic_plan` / `load_reference_analysis` / `save_reference_analysis` 这 4 个工具，除非你把它们手工加进 `<provider>.tools.allow`
- 装了任何其他 plugin 也一样——plugin 的 MCP 工具必须显式列在 allow 里才能被主控调用
- SubAgent 不受影响：`.md` 显式声明了 `tools: ...` 的 SubAgent 还按 `.md` 列表走

这是**显式优于隐式**的代价。设计稿 §7.2 / §11.6 反复强调过。

---

## 2. Schema 字段对照表

| 旧字段（v3.1-）| 新字段（v3.2+）| 语义 |
|---|---|---|
| `<provider>.permissions.allow` | `<provider>.tools.allow` | 主控允许工具列表。**空 list = SDK 全开**（跟随 SDK 语义，下同） |
| `<provider>.permissions.deny` | `<provider>.tools.deny` | 主控禁止工具列表。与 allow 共存时 **deny 优先** |
| —（无对应）| `<provider>.agents.allow` | **新增**：允许装配的 SubAgent 名字白名单。空 list = 全部已加载 SubAgent 都装配 |
| —（无对应）| `<provider>.agents.deny` | **新增**：禁止装配的 SubAgent 名字黑名单。允许 `[]` |

`<provider>` 是 `claude` 或 `openai`。**两个 provider 段都要改**。

`allow` 字段在 v3.1 接受 `null` 表示"默认全开"；v3.2 起 `allow` 必须是数组，**空数组 `[]` 表示"默认全开"**。`null` 不再合法。

---

## 3. 迁移操作步骤

### Step 1：定位你的 HOME/config.json

Windows 默认路径：

```
%USERPROFILE%\Documents\BIMCanvas\config.json
```

如果你设置了 `BIMCANVAS_HOME` 环境变量，则在 `$BIMCANVAS_HOME/config.json`。

打开它，确认 `claude` / `openai` 两段下含 `"permissions": { "allow": [...], "deny": [...] }` —— 这就是要改的目标。

### Step 2：抓"主控当前实际能调"的工具集（关键审查步骤）

旧版本下主控的 `allowed_tools` 是这样组装的：

```
HOME/config.json 里 permissions.allow 列出的工具
    + bundle.mcp_tool_names（所有 MCP server 注册的工具）
    + "Skill"
```

升级后 plugin MCP 工具不再自动合入，**如果你不手工把它们加回 allow，主控就调不动了**。所以迁移前必须知道当前主控实际有哪些工具，才能决定哪些保留。

抓现状方法：

1. **先不要重启 BIMCanvas**（保留旧逻辑）
2. 临时在 `BIMCanvas.Agent/src/agent/main_agent.py` `_create_options` 方法的 `all_allowed = list(dict.fromkeys(...))` 后面加一行：
   ```python
   import json
   print("ALL_ALLOWED:", json.dumps(all_allowed, indent=2))
   ```
3. 启动 BIMCanvas、随便发一句话给主控触发一次 `_create_options`
4. 把 Agent 控制台打印的 `ALL_ALLOWED` 列表抄下来
5. 删除临时 print，不要 commit

（如果你已经升级到 v3.2、自动合并逻辑已被删，此方法失效。可参考仓库 `.dev/plans/工具权限配置系统重设计.md` 附录 A 里的样例列表。）

### Step 3：审查抓到的列表

逐项判断"主控是否真的需要直接调"。判断原则：

- **内建工具**（`Read` / `Write` / `Edit` / `Bash` / `Glob` / `Grep` / `Task` / `Skill` / `AskUserQuestion` 等）：主控通常都要
  - `Task` / `Skill` 必须保留（C2 警告会提醒你）：删了主控派发不了 SubAgent / 调不了 Skill
- **`mcp__canvas__*`（core-base 工具）**：按主控实际使用范围保留。Phase 0 抓现状会显示哪些 core-base server 真正在注册
- **`mcp__<plugin-id>__*`（plugin 工具）**：按 plugin 的使用模式判断。如 `interior-layout` 4 个工具是主控直接调用还是只 SubAgent 用？两种情况都见过，看你的工作流
- **不在 `bundle.mcp_tool_names` 里的旧 `mcp__canvas__*` 工具**：是历史遗留（如旧版 core-base 有现在被 plugin 接管的工具名），可删掉

### Step 4：改 HOME/config.json

把每个 provider 段的 `permissions` 块改成 `tools` + `agents` 两块。

**改前（旧 schema）**：

```json
"claude": {
  "baseUrl": "...",
  "apiKey": "...",
  "defaultModel": "opus",
  ...
  "permissions": {
    "allow": [
      "Read", "Write", "Task", "Skill",
      "mcp__canvas__validate_layout"
    ],
    "deny": []
  },
  "modelMapping": { ... }
}
```

**改后（新 schema）**：

```json
"claude": {
  "baseUrl": "...",
  "apiKey": "...",
  "defaultModel": "opus",
  ...
  "tools": {
    "allow": [
      "Read", "Write", "Task", "Skill",
      "mcp__canvas__validate_layout",
      "mcp__canvas__get_zone_boundaries",
      "mcp__canvas__analyze_image",
      "mcp__interior-layout__save_semantic_plan",
      "mcp__interior-layout__load_semantic_plan",
      "mcp__interior-layout__load_reference_analysis",
      "mcp__interior-layout__save_reference_analysis"
    ],
    "deny": []
  },
  "agents": {
    "allow": [],
    "deny": []
  },
  "modelMapping": { ... }
}
```

`openai` 段做同样改造。

### Step 5：启动 BIMCanvas 验证

- C1 不再报错（旧 `permissions` 字段已经没了）
- 主控启动日志里"允许工具"行应该和 Step 2 抓到的列表（减去你审查后剔除的项）一致
- 试一次实际任务，确认主控能正常调你保留的所有工具

---

## 4. `.md` SubAgent 的 `tools:` 字段语义变化

| `.md` 写法 | v3.1- 行为 | v3.2+ 行为 |
|---|---|---|
| 整行省略 `tools:` | 全开 | **继承主控 allow + deny** |
| `tools:`（值为空） | 全开 | **继承主控 allow + deny** |
| `tools: Read, Write, ...` | 白名单：仅可调列出的工具 | 显式自主：直接用此列表，不再继承主控 |

**SubAgent 继承装配的精确规则**（设计稿 §5.2 + §7.1）：

- 主控 `tools.allow == []`（SDK 全开模式）+ SubAgent 继承 → SubAgent 的 `AgentDefinition.tools = None`（SDK inherit-all）
- 主控 `tools.allow == [X, Y, Z]`（白名单）+ SubAgent 继承 → SubAgent 的 `AgentDefinition.tools = [X, Y, Z]` 深拷贝
- 主控 `tools.deny == []` + SubAgent 继承 → SubAgent 的 `AgentDefinition.disallowedTools = None`
- 主控 `tools.deny == [W]` + SubAgent 继承 → SubAgent 的 `AgentDefinition.disallowedTools = [W]` 深拷贝

**关键不变量**：SDK 字段 `tools: None` 和 `tools: []` 语义不同。`None` = 省略 = inherit all；`[]` = 明确空 = 零工具。继承装配时必须传 `None` 给"全开模式"的 SubAgent，而不是 `[]`。这一点 v3.1- 实现错了（`subagents.py:79` 的旧 `cfg.tools if cfg.tools else None` 把空 list 误转 None），v3.2 修复。

**当前外部 plugin `.md` 文件影响**：

- `bimcanvas-plugin-interior-layout/agents/*.md` 三个 SubAgent 都已经显式列了 `tools: ...`
- 这种情况下 v3.2 行为与 v3.1 **完全一致**——显式列表始终是显式自主
- 只有 SubAgent `.md` 不写 `tools:` 字段（空值或省略整行）时才会触发新的继承语义

也就是说，**v3.2 升级不要求修改任何 plugin 的 `.md` 文件**。

---

## 5. 为什么不提供自动迁移

设计稿 §6 + §10 第 11 条明确决定不做自动迁移，理由：

1. 用户 HOME 里的 `config.json` 是**个性化定制过的**——加过 plugin 工具、调过 deny、装过第三方 plugin。自动迁移识别不了哪些是用户定制、哪些是默认值
2. **删了主控 MCP 自动合并后，allow 列表必须人工审查**——哪些 plugin 工具主控真需要直接调、哪些只 SubAgent 用，机器判断不了。Step 2 + Step 3 的审查环节是迁移核心
3. **C1 fail-fast 比静默自动改更安全**——破坏性 schema 升级让用户显式感知，比悄悄改完都不知道好

---

## 6. interior-layout plugin 用户的最小迁移清单

如果你用的是 `interior-layout` plugin（当前唯一 domain plugin），按 v3.2 Templates 的 19 项审查清单（参考 Phase 0 抓现状结果）补全 + 把 plugin 工具加回去，新 `claude.tools.allow` 大致是 23 项：

```json
"tools": {
  "allow": [
    "Read", "Write", "Edit", "Bash",
    "Glob", "Grep",
    "Task", "Skill", "AskUserQuestion",
    "mcp__canvas__request_background_screenshot",
    "mcp__canvas__validate_layout",
    "mcp__canvas__get_zone_boundaries",
    "mcp__canvas__register_variant",
    "mcp__canvas__list_variants",
    "mcp__canvas__analyze_image",
    "mcp__canvas__create_job",
    "mcp__canvas__complete_job",
    "mcp__canvas__list_project_scenes",
    "mcp__canvas__load_scene_artifact",
    "mcp__interior-layout__save_semantic_plan",
    "mcp__interior-layout__load_semantic_plan",
    "mcp__interior-layout__load_reference_analysis",
    "mcp__interior-layout__save_reference_analysis"
  ],
  "deny": []
},
"agents": {
  "allow": [],
  "deny": []
}
```

`openai` 段写同一份。

---

## 7. C1 报错典型表现

启动 BIMCanvas.Server 或 Agent 时抛 `InvalidOperationException` / `ValueError`，文案：

```
检测到 config.json 含旧版 `claude.permissions` 字段。
工具权限配置已重设计 (v3.2)，请参考迁移文档手工调整:
  docs/Tool_Permissions_Migration.md
  旧 `claude.permissions.allow / deny` → 新 `claude.tools.allow / deny`
  另外新增 `claude.agents.allow / deny` 块需添加 (可填空数组)。
BIMCanvas 不会自动迁移旧结构。
```

`openai.permissions` 字段存在时报相同文案的 openai 版本。

修复：照 §3 Step 4 改完 HOME/config.json，重启。

---

## 8. FAQ

**Q：升级后启动报错说我有旧 `permissions` 字段，但我看 HOME 里已经没有 `permissions` 块了。**

A：检查两个 provider 段（`claude` 和 `openai`）是否都已经清干净。C1 检测两段都查。

**Q：我不想列那么长的 allow 列表，能不能用通配？**

A：不能。v3.2 设计明确禁止 `mcp__<server>` 两段简写、`*` 通配、正则。要么逐项列，要么 `allow: []` 让 SDK 全开（但后者会让所有已注册工具都能用，慎用）。

**Q：能不能写 `allow: []` 然后用 `deny` 屏蔽不要的工具？**

A：可以，跟随 SDK 语义。`allow: []` = SDK 全开 + `deny: [X, Y]` = 实际能用"所有工具除 X、Y"。这种 deny-only 模式适合主控想用大部分工具但禁用少数几个的场景。

**Q：`agents.allow / deny` 默认怎么填？**

A：两个都填 `[]`。表示"全部已加载 SubAgent 都装配，无黑名单"。只在调试时想临时禁用某 SubAgent（不删 `.md` 文件）才往 `deny` 里写名字。

**Q：升级后 SubAgent 行为有变化吗？**

A：取决于 `.md` 文件。**外部 plugin（如 interior-layout）的 SubAgent 都显式列了 `tools:`，行为完全不变**。如果你有自己写的 SubAgent `.md` 没写 `tools:` 字段（或值为空），v3.1- 是"全开"、v3.2+ 是"继承主控"——这是有意的语义修复。
