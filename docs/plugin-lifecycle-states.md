# Plugin Lifecycle States

> Plugin 状态机精确定义。本文回答:**「我点了 [信任并激活] 之后,具体哪个文件改了?Agent 重启前我能写项目数据吗?」**
>
> 配合 [plugin-architecture.md](./plugin-architecture.md) §5 总览阅读。

---

## 1. 四个独立状态轴

BIMCanvas plugin 在生命周期内有 **4 个状态轴**,**彼此独立、互不联动**:

| 轴 | 取值 | 落盘位置 |
|---|---|---|
| `installed` | 存在 / 不存在 | `BIMCANVAS_HOME/plugins/<id>/` 目录是否存在 |
| `trustState` | `untrusted` / `trusted` | `BIMCANVAS_HOME/plugins-state.json[<id>].trustState` |
| `active` | 是 / 否(同时最多一个 plugin 为 active) | `BIMCANVAS_HOME/server_config.json.agent.activePlugin` |
| `bound` | 是 / 否(按 .bcp 项目计) | `<project>/project.json.scenes[]` 是否含该 plugin id |
| `launched` | 是 / 否 | Agent 子进程是否存活 |

「**互不联动**」的意思:

- 一个 plugin 可以是 `installed + untrusted + 非 active + 非 bound + 非 launched`(刚安装完)
- 一个 plugin 可以是 `installed + trusted + active + 非 bound`(切了 active 但没开过项目)
- 安装 ≠ 信任 ≠ 激活 ≠ 绑定 ≠ 启动

---

## 2. trustState 子状态(R9 RCE 防御核心)

`installed` 内部含 `trustState` 子状态。**这是为防御供应链 RCE 攻击专门拆出的层**。

```text
                  POST /api/plugins/install
                  (clone + StaticValidator)
       ┌─────────────────────────────────────────────┐
       │                                             ↓
   (无此 plugin)                          [installed + untrusted]
                                                     │
                  POST /api/plugins/{id}/trust-and-activate
                  (ExecutableProbe dry-run 通过)
                                                     │
                                                     ↓
                                          [installed + trusted]
                                                     │
                                                     ▼
                                          可被设为 active
                                          可被 bind 到 scene
```

| 子状态 | Python 代码已被执行? | 可设 active? | 写入哪? |
|---|---|---|---|
| `installed + untrusted` | ❌ 从未 | ❌ 设 active 返回 403 + `code: "plugin_not_trusted"` | StaticValidator 仅做纯文本校验 |
| `installed + trusted` | ✅ 一次(ExecutableProbe dry-run) | ✅ | `plugins-state.json[<id>].trustState = "trusted"` + `trustedAt` 时间戳 |

**为什么必须拆**:任何用户粘贴 GitHub URL 都能触发 clone;若 clone 后立即 import,等同于"粘贴任意 URL 就能 RCE 任意代码"。安全模型详见 [plugin-security-model.md](./plugin-security-model.md)。

---

## 3. 状态转换图(完整)

```text
        ┌─────────────────┐
        │ (无此 plugin)   │
        └────────┬────────┘
                 │
                 │ POST /api/plugins/install
                 │   (git clone → StaticPluginValidator)
                 │   ❌ 不执行 Python 代码
                 ▼
   ┌─────────────────────────────┐
   │ installed + untrusted       │
   │ — plugins-state.json 写入   │
   │ — 卡片显示 [未信任] 标签    │
   └────┬─────────────────────┬──┘
        │                     │
DELETE  │                     │ POST /api/plugins/{id}/trust-and-activate
/api/   │                     │   (ExecutableProbe dry-run + 设 active)
plugins/│                     │   ✅ 第一次执行 Python (register dry-run)
{id}    │                     ▼
        │             ┌─────────────────────────────────┐
        │             │ installed + trusted + active    │
        │             │ — server_config.activePlugin 写 │
        │             │ — Web 显示 "需重启" banner      │
        │             └─────┬────────────────────────┬──┘
        │                   │                        │
        │                   │ 用户重启程序           │ POST /api/project/{id}/scenes
        │                   ▼                        │   (写 scenes[] + MountSceneScaffold
        │             ┌──────────────────────┐       │    + 生成 LaunchContext + SetBound)
        │             │ + launched (无项目)  │       │
        │             │ — Agent 进程在跑     │       │
        │             │ — LaunchMode =       │       ▼
        │             │   Projectless        │   ┌─────────────────────────────┐
        │             │ — 写入 API 全 403    │   │ + bound (scenes 绑定到项目) │
        │             └──────────────────────┘   │ — 写 project.json.scenes[]  │
        │                                        │ — 写 plugins.lock.json      │
        │                                        │ — LaunchContext.Mode =      │
        │                                        │   ProjectBound              │
        │                                        │ — Server 写入 gate 放行     │
        │                                        │   (限 activeSceneId)        │
        │                                        └──────────────┬──────────────┘
        │                                                       │
        │                                                       │ Server gate 放行写入
        │                                                       ▼
        │                                          ┌─────────────────────────────┐
        │                                          │ + launched (项目就绪)       │
        │                                          │ — Agent 拥有完整能力        │
        │                                          └─────────────────────────────┘
        │
        ▼
  (回到无此 plugin)
```

---

## 4. 每个状态对应的 API 与 UI 行为

### 4.1 `installed + untrusted`

- **Web UI**:卡片显示 `[未信任]` 灰色标签;按钮显示 `[信任并激活]`(高亮);`[卸载]` 可见
- **API**:
  - `GET /api/plugins` 返回 `trustState: "untrusted"`
  - `POST /api/plugins/active`(对该 plugin id)→ **403** + `code: "plugin_not_trusted"`
  - `DELETE /api/plugins/{id}` → 卸载,plugin 目录与 `plugins-state.json` 条目都被清除
- **写入**:无写入能力(尚未 active)

### 4.2 `installed + trusted + 非 active`

- **Web UI**:卡片显示 `[已信任]` 标签;按钮显示 `[设为激活]`(普通态,无二次确认)
- **API**:
  - `POST /api/plugins/active` → 200,写 `server_config.json.agent.activePlugin = <id>`,返回 `restartRequired: true`
- **写入**:仍无(不是 active)

### 4.3 `+ active(非 bound)`

- **Web UI**:卡片显示 `[已激活]` 徽章 + 「需重启」横幅;按钮无操作
- **API**:`GET /api/plugins` 返回 `isActive: true`
- **LaunchContext**:`Mode = Projectless`(若程序已重启 / Agent 已启)
- **写入**:**全部 403** + `code: "project_pending_binding"`(因 ProjectContext 未 Bound)

### 4.4 `+ bound`(到某 .bcp 项目)

- **触发**:`POST /api/project/{id}/scenes` body `{ sceneId, scene, plugin: { id, versionRange } }`
- **写入**:
  - `project.json.scenes[]` 追加新 scene(JObject patch,保留其他扩展字段)
  - `plugins.lock.json` 追加 lock entry
  - **`MountSceneScaffold(sceneId, pluginId)`** 把 plugin 的 `projectMount/` 物化到 `<project>/<sceneId>/` 命名空间(**唯一物化入口**;不在 open project 时触发)
- **LaunchContext**:`Mode = ProjectBound, ActiveSceneId = <newSceneId>`
- **Server gate**:`schemes/{activeSceneId}/...` 可写;其他 sceneId / `baseline/` / `computed/` 403

### 4.5 `+ launched`

- **触发**:Server 在启 Agent 子进程时
- **状态**:Agent 进程存活,可接收 chat;若 `Mode = ProjectBound`,完整能力;若 `Mode = Projectless`,只能 chat + 读 plugin 元数据
- **重启时机**:active plugin 切换后需要重启 Agent 才生效(Web 显示重启 banner)

---

## 5. 关键不变量

| 不变量 | 理由 |
|---|---|
| **install ≠ activate** | 安装只是 clone + 静态校验;激活是用户决策(且需 trusted)。用户可同时装 N 个 plugin 但只 active 1 个。 |
| **activate ≠ bind** | 激活是全局开关(server_config 级);绑定是项目级(每个 .bcp 独立)。同一 plugin 可激活但未绑到当前项目。 |
| **bind ≠ launch** | 绑定写元数据 + 物化 scaffold,但 Agent 进程可能尚未启动 / 已 shutdown。 |
| **untrusted 永远不能 active** | V13 T6b 单测的核心约束(虽然本期不做测试,但 Server 实现已保证) |
| **install-time 绝不执行 Python 代码** | R9 RCE 防御(详见 security-model)。`StaticPluginValidator` 是纯文本校验。 |
| **trust 状态存平台外** | `plugins-state.json` 不在 plugin 目录内,plugin 代码触达不到。详见 security-model §4。 |
| **bind 是 projectMount 唯一物化入口** | R10 静默覆盖防御。open project 不触发 MountSceneScaffold。 |

---

## 6. [信任并激活] 二次确认 UX 的设计动机

主真理源 §8.2:「**给最终用户的安全提示;trust 不是技术黑盒,要让用户明白"激活 = 信任执行代码"**」。

- 首次激活按钮文案**强制为 [信任并激活]**(不是 [激活] / [启用]),按下后弹 `TrustAndActivateDialog`
- 对话框展示 `pluginId` / `sourceUrl` / `resolvedCommit`,加黄色警告条:
  > 「激活将执行该插件 `<plugin-id>` 的 Python 代码。请确认你信任来源 `<sourceUrl>`。」
- 两按钮:`[确认信任并激活]` / `[取消]`
- 后续切换 active(plugin 已 trusted)→ 用普通 `[设为激活]` 按钮,无二次确认(因已通过一次 ExecutableProbe + 用户已明确信任)

这是用户对 R9 RCE 风险的**最后一道感知防线**;UI 实现见 `BIMCanvas.Web/src/components/UI/dialogs/TrustAndActivateDialog.vue`。

---

## 7. 与 ProjectContext 状态机的关系

Server 端 `ProjectContext` 内部也有状态机(主真理源 §4.7 / `BIMCanvas.Server/Models/Plugins/OpenStatus.cs`):

| ProjectState | 触发 | 写入能力 |
|---|---|---|
| `None` | 无项目打开 | 否 |
| `Pending` | 项目解析完但 scene 未绑定 / 多候选未选 | **全 403** + `code: "project_pending_binding"`(V12a) |
| `Bound` | scene 已绑定,LaunchContext 已生成 | `schemes/{activeSceneId}/...` 放行 |
| `Launched` | + Agent 子进程健康 | 同 Bound |

**OpenStatus 三态**(由 `OpenProject` 返回给 Web 决定弹什么对话框):

| OpenStatus | 含义 | Web 反应 |
|---|---|---|
| `bound` | 命中唯一 scene(或 legacy 无 activePlugin) | 直接进入项目 |
| `sceneSelectRequired` | 命中多个匹配 scene | 弹 `SceneSelectorDialog`(用户选一个) |
| `requiresSceneBinding` | 未命中任何 scene(legacy 项目或新场景类型) | 弹 `SceneBindingDialog`(用户决定新增或切回) |

---

**End of Plugin Lifecycle States.**
