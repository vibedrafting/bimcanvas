# BIMCanvas.Web

> 室内设计 AI Copilot 的画布前端 —— BIMCanvas 整体架构里的「皮肤 + 眼睛」。

BIMCanvas.Web 是一个基于 **Vue 3 + Vite + Three.js** 的单页应用，承担两件事：

1. **渲染层**：把 BIMCanvas 的标准空间数据（户型、Zone、家具、禁区、门窗）渲染成可交互的 2.5D 俯视画布；
2. **交互层**：作为 AI 指挥中心（AI Command Center）的入口，承载与 Server / Agent 的对话、提案审阅、空间标记、版本切换等设计协同动作。

它**不是查看器**——前端不持业务状态、不做几何计算、不做约束验证；这些都在 BIMCanvas.Server。Web 的边界是：呈现、收集意图、可视化结果。

> ⚠️ **暗色模式优先**：项目当前以暗色主题为生产基线，亮色主题代码完整但 UI 颜色未充分适配。**正式使用与演示请保持暗色模式**。

---

## 1. 在 BIMCanvas 中的位置

```
┌──────────────────────────────────────────────────────────────┐
│                      BIMCanvas.Web (本项目)                    │
│                Vue 3 + TypeScript + Three.js                  │
│           渲染 · 交互 · 可视化 · AI 对话 UI                     │
└──────────┬───────────────────────────────────────────────────┘
           │ REST · SignalR · SSE          ┌──────────────────┐
           ▼                               │  BIMCanvas.Agent │
   ┌──────────────────────────┐  HTTP/SSE  │  (Python)         │
   │   BIMCanvas.Server       │ ◄────────► │  AI 决策 · 工具调用│
   │   (.NET 8)               │            └──────────────────┘
   │   状态 · 几何 · 通信中枢   │
   │   Canvas-MCP 工具         │
   └──────────────────────────┘
```

**关键边界**

- Web 通过 REST + SignalR + SSE 与 **Server** 通信；**Agent API 走 `Server /agent` 代理**，前端不直连 Agent 端口。
- Web 不直接读 `.bcp` 文件、不直接调 Agent，所有对外通信经 Server 一层收口。
- 决策（放哪里 / 怎么放）属于 Agent；几何 / 约束 / 持久化属于 Server；前端只是呈现与交互。

权威定位见 `docs/Architecture.md` —— **「皮肤 + 眼睛 | 渲染展示、用户交互」**。

---

## 2. 快速上手

### 2.1 环境要求

- **Node.js ≥ 20.19**（Vite 7 要求）
- npm / pnpm / yarn 任选
- 现代浏览器：Chrome、Edge 推荐

### 2.2 三种启动姿势

**全栈开发（推荐）** —— 在仓库根目录：

```bash
dotnet run --project BIMCanvas.Server
```

会自动启动 Server（5000）+ Agent + Web（5173），并打开浏览器。Vite HMR 直接生效。

**仅前端开发**：

```bash
cd BIMCanvas.Web
npm install
npm run dev
```

启动 Vite 开发服务器。若 1 秒内探测不到 Server，会自动降级到 StandaloneRuntime（见 §3）。

**生产构建**：

```bash
npm run build      # vue-tsc 类型检查 + Vite 构建 → dist/
npm run preview    # 本地预览构建产物
```

### 2.3 环境变量

均为可选，留空走默认。

| 变量 | 默认行为 | 用途 |
|---|---|---|
| `VITE_SERVER_URL` | DEV: `http://<当前 host>:5000`；PROD: 同源 | Server 基址 |
| `VITE_AGENT_URL` | `${VITE_SERVER_URL}/agent` | Agent 基址（仅在需要绕过 Server 代理时设） |
| `VITE_WEB_RUNTIME` | 自动探测 | 强制 `connected` 或 `standalone` |

放在 `.env.development` / `.env.production`。

---

## 3. 两种运行时模式

Web 启动时通过 `createWebRuntime()` **一次性**选择运行模式，运行期间不热切换。两种模式共享同一份构建产物，模式在运行时由探测或环境变量决定。

### 3.1 对比一览

| 维度 | **ConnectedRuntime（联机态）** | **StandaloneRuntime（独立态）** |
|---|---|---|
| 适用场景 | 完整开发 / 工作站使用 / 多人协作 | Demo / 离线评审 / 单机分发 |
| 外部依赖 | Server + Agent 在线 | 仅浏览器 |
| 项目载体 | Server 持有的 `.bcp` 项目目录 | 浏览器内存 + `WebSnapshot` JSON |
| 项目创建 | ❌（待 Server 接口） | ✅ 创建空白内存项目 |
| 编辑持久化 | ✅ Server 自动写回 `.bcp` | 用户手动 Export Snapshot |
| `.bcp` 导出 | ✅ | ❌ |
| Snapshot 导入 | ❌ | ✅ |
| Snapshot 导出 | ✅ | ✅ |
| 实时同步（SignalR） | ✅ 双向 | ❌ |
| AI 对话（Agent / SSE） | ✅ 全功能 | ❌ |
| Git 分支 / Worktree | ✅ | ❌ |
| 模块库 | Server 提供 | 取决于导入的 Snapshot 是否带 `moduleLibrary` |
| 撤销 / 重做 | ✅ | ✅ |

### 3.2 激活逻辑

```
启动 → createWebRuntime()
        │
        ├─ VITE_WEB_RUNTIME = 'connected' ────────► ConnectedRuntime
        ├─ VITE_WEB_RUNTIME = 'standalone' ───────► StandaloneRuntime
        └─ 未强制 → 探测 GET ${SERVER_API}/project/status（1 秒超时）
                    ├─ 200 OK ────────────────────► ConnectedRuntime
                    └─ 失败 / 超时 ─────────────────► StandaloneRuntime
```

### 3.3 内存真相源

两个 Runtime 共享同一个数据源：

```ts
canvasStore.projectData: ProjectData | null
```

UI、Canvas、交互工具**只消费 `ProjectData`**。`.bcp` 只属于 Server，`WebSnapshot` 只是 IO 格式——都不直接进入 UI。迁移路径固定：Connected 打开 `.bcp` → Web 导出 Snapshot → Standalone 导入 Snapshot。

### 3.4 关键文件

| 文件 | 作用 |
|---|---|
| `src/runtime/WebRuntimeProtocol.ts` | Runtime 接口与 `WebCapabilities` 类型 |
| `src/runtime/createWebRuntime.ts` | 工厂函数 + 自动探测（超时 1000 ms） |
| `src/runtime/capabilities.ts` | 两种模式的能力矩阵 |
| `src/runtime/ConnectedRuntime.ts` | 联机态实现 |
| `src/runtime/StandaloneRuntime.ts` | 独立态实现 |
| `src/runtime/standalone/SnapshotReader.ts` | WebSnapshot 导入 |
| `src/runtime/standalone/SnapshotWriter.ts` | WebSnapshot 导出 |

UI 守卫请使用 `supports(runtime.capabilities.xxx)` 判断能力，不要在组件里散落 `runtime.mode === 'standalone'` 这类分支。

---

## 4. 技术栈

| 层 | 选型 | 版本 |
|---|---|---|
| 框架 | Vue 3 + TypeScript（Composition API + `<script setup>`） | 3.5 |
| 构建 | Vite | 7.2 |
| 类型检查 | vue-tsc | 3.1 |
| 渲染 | Three.js + CSS2DRenderer + three-stdlib | 0.182 |
| 状态 | Pinia | 3.0 |
| 通信 | axios（REST）· @microsoft/signalr · 原生 fetch（SSE） | — |
| 样式 | Vanilla CSS + CSS Variables + SCSS | — |
| 流式渲染 | markstream-vue | 0.0.5-beta |
| 截图 | html2canvas | 1.4 |

`vite.config.ts` 极简（仅 `plugins: [vue()]`），**无多 mode 分支、无 server.proxy、无 base 自定义**——一切环境差异通过 `import.meta.env` 在运行时解析。

---

## 5. 目录结构

```
src/
├── main.ts                # 应用启动：选 Runtime → 装 Pinia → 挂 App
├── App.vue                # 根组件，按 location.pathname 决定主流程或截图视图
├── style.css              # 全局样式入口
│
├── config/
│   └── api.ts             # SERVER_BASE / SERVER_API / SIGNALR_HUB / AGENT_API
│
├── runtime/               # Web Runtime Protocol：模式抽象层
│   ├── WebRuntimeProtocol.ts
│   ├── createWebRuntime.ts
│   ├── capabilities.ts
│   ├── ConnectedRuntime.ts · StandaloneRuntime.ts
│   └── standalone/        # WebSnapshot 进出
│
├── stores/                # Pinia 状态（6 个 store）
│   ├── canvasStore.ts     # ProjectData 真相源 + 选区 + 脏标记 + saveModules
│   ├── appStore.ts        # 视图导航（homepage / workspace）
│   ├── windowStore.ts     # AI 多窗口（Worktree 隔离）
│   ├── gitStore.ts        # Git 分支 / Worktree
│   ├── mergeStore.ts      # 分支合并向导
│   └── debugStore.ts      # 调试控制台
│
├── services/              # 核心业务逻辑
│   ├── builders/          # Three.js 场景构建器（墙/柱/家具/Zone/标签…）
│   ├── interaction/       # 交互层
│   │   ├── InteractionService.ts
│   │   ├── ShortcutManager.ts · GhostManager.ts
│   │   ├── snap/          # 语义吸附引擎
│   │   └── tools/         # PlaceTool / MoveTool / RotateTool / MirrorTool / CopyTool / MeasurementTool
│   ├── three/             # Three.js 集成（场景、相机、渲染器、图层）
│   ├── theme/             # ★ 主题系统（见 §7）
│   ├── canvas/            # 画布样式
│   ├── validation/        # ConstraintService
│   ├── state/             # TimelineManager（Undo/Redo）
│   ├── screenshot/        # 后台截图
│   ├── SignalRService.ts        # 实时同步
│   ├── ProjectService.ts        # 项目加载
│   ├── ModuleLibraryService.ts  # 模块库
│   ├── ChatAttachmentService.ts # 对话附件
│   ├── InteractionChannelService.ts # SSE 交互通道
│   └── …
│
├── composables/           # 组合式逻辑
│   └── aiCommandCenter/   # AI 指挥中心子能力（聊天流、窗口、滚动、截图、上下文）
│
├── components/UI/         # 主要 UI 组件
│   ├── AICommandCenter.vue      # AI 对话 + 任务卡 + 提案 + 警报
│   ├── DynamicIsland.vue        # 顶部灵动岛 + 主题切换按钮
│   ├── ModuleLibraryPanel.vue   # 浮动模块库面板
│   ├── HomeSettingsPanel.vue    # 首页实例设置台
│   └── Ribbon/                  # 工具栏
│
├── views/
│   ├── HomePage.vue             # 项目列表 / 创建 / 导入
│   └── ScreenshotRenderView.vue # 后台截图渲染（路径 /screenshot-render）
│
├── layouts/MainLayout.vue # 主布局：3D 画布 + 侧面板 + AI Command Center
├── styles/variables.css   # CSS 变量（运行时由 ThemeService 写入）
├── utils/coordinates.ts   # ★ 坐标转换唯一事实源（见 §9）
├── types/                 # TypeScript 类型定义
└── constants/             # 常量与文案
```

> 项目**没有引入 vue-router**。首页与工作区切换由 `appStore.currentView` 在 `'homepage'` / `'workspace'` 之间驱动；`/screenshot-render` 是后台截图的特殊路径，由 `main.ts` 直接判断 `location.pathname` 接管。

---

## 6. 开发地图（想改 X，去哪改 Y）

| 我想改…… | 去这里 |
|---|---|
| 主题颜色 / 玻璃感 / 标签字体 | `src/services/theme/ThemeService.ts`（见 §7） |
| 全局 CSS 变量 | `src/styles/variables.css` |
| 3D 渲染逻辑（墙、柱、家具、Zone、网格、标签） | `src/services/builders/*.ts` |
| 坐标转换 | `src/utils/coordinates.ts`（**唯一事实源**） |
| 交互工具（移动 / 旋转 / 镜像 / 放置 / 测量） | `src/services/interaction/tools/*.ts` |
| 全局快捷键 | `src/services/interaction/ShortcutManager.ts` |
| 语义吸附 | `src/services/interaction/snap/` |
| AI 对话 UI / 流式渲染 | `src/components/UI/AICommandCenter.vue` + `src/composables/aiCommandCenter/*` |
| 推送通知 / 标记"需重启"状态 | `src/stores/systemStore.ts`(`pushToast` / `markRestartRequired`),见 §10 |
| API 基址 / 端口 | `src/config/api.ts` + `.env.*` |
| SignalR 监听 | `src/services/SignalRService.ts` |
| SSE 流式聊天 | `src/composables/aiCommandCenter/useChatStream.ts` |
| SSE 交互通道（截图 / 提问） | `src/services/InteractionChannelService.ts` |
| Pinia 状态 | `src/stores/*.ts` |
| 首页 / 工作区切换 | `src/stores/appStore.ts`（无 vue-router） |
| 模块库 / 放置预览 | `src/services/ModuleLibraryService.ts` + `src/services/interaction/tools/PlaceTool.ts` |
| 撤销 / 重做策略 | `src/services/state/TimelineManager.ts` |
| 增加新的运行时模式 | 实现 `WebRuntimeProtocol` → 在 `createWebRuntime.ts` 注册 → 在 `capabilities.ts` 添加能力矩阵 |

---

## 7. 主题与样式

### 7.1 暗色模式优先

> ⚠️ 当前 `lightTheme` 代码完整，但 UI 在亮色模式下未做完整对比度与质感适配，部分组件存在视觉问题。**生产与演示请保持暗色模式**；亮色模式仅作为开发期参考保留。

### 7.2 主题定义在哪里

| 内容 | 文件 |
|---|---|
| **主题对象（颜色、玻璃、阴影、标签——所有真值）** | `src/services/theme/ThemeService.ts` 中的 `darkTheme` 与 `lightTheme` |
| CSS 变量（运行时由 ThemeService 写入 `document.documentElement.style`） | `src/styles/variables.css` |
| 全局样式入口 | `src/style.css`（被 `main.ts` 引入） |
| 主题切换按钮 UI | `src/components/UI/DynamicIsland.vue` |
| 切换事件广播 | `window.dispatchEvent('bimcanvas:theme-change', { detail: theme })` |

### 7.3 改样式的标准动作

- 改 **3D 渲染颜色**（墙体、柱、家具、AI 视觉层、标签描边）→ `ThemeService.ts` 里 `darkTheme.scene` / `aiVision` / `componentLabel`
- 改 **UI 毛玻璃 / 阴影 / 主色调** → `darkTheme.css.*`
- **新增 CSS 变量** → 同时在 `variables.css` 与 `ThemeService.ts` 增减；ThemeService 是真源
- **临时验证** → 在浏览器 DevTools 直接改 `:root` 上的 CSS 变量，刷新即可还原
- ❌ **不要在组件 `<style>` 里硬编码颜色** —— 会跳过主题切换、被 review 退回

---

## 8. 与 Server / Agent 的通信

### 8.1 三条通道

| 协议 | 用途 | 入口 |
|---|---|---|
| HTTP REST | 项目读写、模块库、保存、设置 | `src/services/ProjectService.ts`、`src/stores/canvasStore.ts#saveModules` |
| SignalR | 实时双向同步（Git 状态、Server 推送、Agent 通知） | `src/services/SignalRService.ts` |
| SSE | Agent 流式聊天、交互请求（截图 / 提问） | `src/composables/aiCommandCenter/useChatStream.ts` · `src/services/InteractionChannelService.ts` |

### 8.2 baseUrl 解析（`src/config/api.ts`）

```
SERVER_BASE = VITE_SERVER_URL ?? (DEV ? `${protocol}//${hostname}:5000` : '')
SERVER_API  = `${SERVER_BASE}/api`
SIGNALR_HUB = `${SERVER_BASE}/hubs/canvas`
AGENT_API   = VITE_AGENT_URL ?? `${SERVER_BASE}/agent`
```

- **开发态**：未设 env 时默认 `http://<当前 host>:5000`
- **生产态**：同源（`/api` · `/hubs/canvas` · `/agent`）
- **Agent 始终走 `Server /agent` 代理**，前端**禁止**硬编码 `localhost:8865` 这类直连地址

---

## 9. 工程纪律（不可越线）

写给后续维护者，每条都对应过去踩过的坑：

1. **坐标系唯一事实源是 `src/utils/coordinates.ts`** —— 数据系是 Y-Up CAD（mm），渲染系是 Three.js 俯视。**禁止**在业务代码里手动 `× -1` 翻转 Y 轴。
2. **角度系统**：交互角（`atan2(z,x)`）是 CW+，数据模型角是 CCW+。传入 `rotatePoint2D()` 前必须取反，详见 `docs/Architecture.md §1.5`。
3. **Agent API 必须走 `Server /agent`** —— 前端不直连 Agent 端口。
4. **「放哪里」的决策不属于前端** —— 前端只呈现、收集意图、展示结果；几何 / 约束 / 决策属于 Server / Agent。
5. **新增 Runtime 能力先扩 `WebRuntimeProtocol`** —— 在 `Connected` / `Standalone` 两端同步落地，UI 通过 `supports(capability)` 守卫，而不是 `runtime.mode` 散点判断。
6. **样式改 `ThemeService.ts`** —— 不要直接改组件 `<style>` 里的硬编码颜色，否则破坏主题切换。
7. **ProjectData 是单一真相源** —— UI 与工具只读写 `canvasStore.projectData`；`.bcp` 只属于 Server，`WebSnapshot` 只是 IO 格式。

---

## 10. 通知与重启入口（不允许另起炉灶）

UX 一致性约束。所有用户可见的反馈与"需要重启"状态都走两个统一通道，不要在面板里自建 inline alert / 模态对话框 / 横条 banner：

### 10.1 瞬时反馈 —— 左下 toast-list

| 项 | 内容 |
|---|---|
| 唯一容器 | `src/components/UI/AgentNotificationModal.vue`（位于 App.vue 全局挂载，左下角，从下往上堆叠） |
| 推送 API | `systemStore.pushToast({ title, message, type })`，4 种 type：`info` / `success` / `warning` / `error` |
| 容量 | 最多同时 3 条可见，溢出显示 `+N 条更多` 徽章 + `全部清除` 按钮 |
| 底层通道 | `window.dispatchEvent(new CustomEvent('bimcanvas:agent-notification', ...))` —— 不要在业务代码里直接 dispatch，统一走 `pushToast()` |

### 10.2 持久状态 —— 顶栏 [需要重启] 按钮

| 项 | 内容 |
|---|---|
| 状态 store | `systemStore.restartRequired`（computed，由 `restartReasons: Set<string>` 驱动） |
| 标记 API | `systemStore.markRestartRequired(reason: string)` —— `reason` 用 `'settings:server'` / `'plugin:indoor-layout'` 这种带 namespace 的字符串去重 |
| 渲染组件 | `src/components/UI/GlobalRestartButton.vue`，Teleport 到 `#global-header-actions` 插槽（`views/HomePage.vue` 三种 mode 互斥嵌入） |
| 执行 | `systemStore.performRestart()` → `RestartService.performRestart(SERVER_BASE)`：POST `/api/settings/restart` + 20×1.5s `/health` 轮询 + `window.location.reload()` |
| UX 纪律 | 点击按钮 **直接重启,无二次确认**（toast 已告知"需重启",按钮点击 = 用户确认） |

### 10.3 禁止反模式

- ❌ 在面板内写 `<div class="alert alert-error">{{ store.lastError }}</div>` 这种 inline alert —— 改 `pushToast({type:'error'})`
- ❌ 在面板内写"需要重启 + 立即重启 / 稍后重启" 模态对话框 —— 改 `markRestartRequired(reason)` 让顶栏按钮自动出现
- ❌ 在业务 store 里加 `restartRequired: ref<boolean>` / `lastError: ref<...>` —— 跨业务统一在 `systemStore`，避免散落
- ❌ 新增 toast 容器（右上 / 中央 / 自建 Vue Teleport）—— 用户多次明确要求"左下角统一",违反会被退回

历史 commit:`7cbcbfa 功能：顶栏全局 [需要重启] 按钮 + 统一左下 toast-list,删除实例设置模态对话框`

---

## 11. 日志系统（F12 / 面板）

全前端日志走**单一出口** `src/utils/logger.ts`,**禁止**散用 `console.*`(唯一例外是 `logger.ts` 自身内部的 console sink）。这是一次彻底重构的产物:旧的「`debugStore`(Pinia)+ 47 文件散用 `console.*`」双轨已废弃,`stores/debugStore.ts` 已删除。

### 11.1 设计目标与互补边界

日志要做到:**统一输出、足够简洁、显示用户关键操作、显示信息收发、显示关键渲染报错**。核心手段是**单一分级结构化 logger + turn/操作粒度聚合**(不在 chunk 粒度打点,否则 SSE delta 一秒几十条会淹没有效信息)。

与 Server 日志**功能互补**,两端以 `windowId` + 时间戳对齐,不互相重复:

| 维度 | Server 日志（终端控制台 + `logs/session_*.log`，Console Tee 镜像) | Web 日志（F12 + 面板) |
|---|---|---|
| 视角 | 服务端**执行与状态** | 浏览器**意图与感知** |
| SSE 流内容 | **盲区**——`Program.cs` 的 `ProxyToAgentAsync` 透明转发,零日志 | **主战场**:记录实际解析到的事件流 |
| 用户操作 | 看不到,只见 HTTP 到达 | **唯一来源**:发送 / 中止 / 开关项目 / 编辑 |
| 几何 / Zone | 计算、验证、落盘(PlacementService 等) | 只记「收到推送 + 渲染成败」,不记几何细节 |
| Git / Worktree | 完整 | 不记,收到 `git-status-changed` 通知即可 |

一句话:**Web 记意图与感知,Server 记执行与状态;流是 Server 的结构性盲区,正好由 Web 补全。**

### 11.2 用法

```ts
import { createLogger } from '@/utils/logger';   // 实际用相对路径,如 '../../utils/logger'
const log = createLogger('STREAM');              // 模块级声明,按归属域取
log.info('turn.completed', { win: 'main', dur: '12s', tokens: 4521 });
// 输出:[23:45:12.341] [Web:STREAM] turn.completed win=main dur=12s tokens=4521
```

- API:`createLogger(domain)` 返回 `{ error, warn, info, debug }`,每个签名 `(msg: string, fields?: Record<string, unknown>)`。
- `msg` 用**英文短语**,变量进 `fields` 对象(**不要字符串拼接**);`fields` 渲染成 `key=val`,带空格的值自动加引号,`Error` 取 `.message`,对象 `JSON.stringify` 且超 200 字截断。
- 格式 `[时间] [Web:域] msg key=val` 与 Server `[时间] [Server] msg` 视觉对齐,F12 与 Server 终端并排可对照。
- 关联键:涉及某窗口的日志带 `win=<windowId>`;一次对话用 `clientMessageId` 贯穿 send→turn。

### 11.3 五域事件地图

| 域 | 颜色 | 记什么 | 关键触发点(file) |
|---|---|---|---|
| `USER` | 紫 | 用户关键操作 | `useChatStream.ts`(send / interrupt)、`appStore.ts`(open / close / delete project)、interaction tools(move / rotate / copy / mirror / place) |
| `STREAM` | 蓝 | SSE 流收发 + turn 状态 | `useChatStream.ts`(send 载荷、`turn.completed` / `turn.failed`、SSE 解析失败、history 同步) |
| `RECV` | 绿 | SignalR 推送接收 | `SignalRService.ts`(`ReceiveUpdate` / `GitStatusChanged` / `AgentNotification` / `SceneArtifactUpdated` + 连接生命周期)、`canvasStore.ts`(server-update / 变体同步) |
| `RENDER` | 粉 | Three / 渲染 | `ThreeSceneService.ts`(模块库加载失败、场景重建、fitToScreen)、`builders/*`、`LayerManager.ts` |
| `SYS` | 灰 | 系统 / 生命周期 / 项目加载 | `App.vue`(挂载、进工作区)、`canvasStore.ts`(project loaded / save)、各 service |

> SignalRService 的 4 个推送处理器原本**零日志**,本次补齐——这是 Server 看不到的「信息接受」面。

### 11.4 分级纪律(保证简洁)

| 级别 | 默认可见 | 内容 |
|---|---|---|
| error / warn | ✓ | 真错误 / 降级 / 重连 / 约束违规 |
| info | ✓ | 用户操作、turn 里程碑、关键 SignalR 推送、保存/合并/分支/截图成功 |
| debug | ✗ | delta、心跳、几何细节、查找未命中、开发噪音 |

- **阈值门控**:低于当前级别的日志直接丢弃(不进 buffer、不打 console),默认 `import.meta.env.DEV ? 'info' : 'warn'`。
- 频繁事件(SSE delta、坐标 / SVG 调试)**必须降 `debug`**;一个 turn 只打里程碑,不逐 chunk 打。
- ⚠️ **禁止在 computed / getter 内写日志**——logger 会 `unshift` reactive `logBuffer`,在 computed 内触发响应式副作用导致无限循环(`canvasStore.findObjectById` 在 computed 中调用,故其内部不打任何日志,见该函数注释)。

### 11.5 运行时开关 + 面板

- 切级别:F12 控制台 `__bimlog.setLevel('debug')`(全局钩子,另含 `__bimlog.level` / `.clear()` / `.buffer`),或 `localStorage.bimlog='debug'`(持久化,刷新保留)。
- 应用内面板 `src/components/UI/DebugConsole.vue`:`Ctrl+\`` 开关(`App.vue` 的 `handleKeydown` → `logStore.toggle()`),支持级别切换、域过滤、复制全部。

### 11.6 架构与文件

- `src/utils/logger.ts` —— **framework-agnostic 纯 TS 模块**(只 `import { ref } from 'vue'` 做 buffer),非 Pinia store。这样服务层单例类(`SignalRService`)、Three 服务、甚至 Pinia 初始化前的早期日志都能直接 `import`,且「产生日志」与「展示日志」解耦。持有阈值级别 + reactive `logBuffer`(newest-first,环形上限 **500**) + console sink(按级别映射 `console.error/warn/log/debug`,带域色前缀)。
- `src/stores/logStore.ts` —— 极薄 Pinia store,只剩**面板 UI 状态**(`isVisible` / 域过滤 / 级别按钮),数据源 `visibleLogs` 取自 logger 的 `logBuffer`,无任何 console 逻辑。
- `src/components/UI/DebugConsole.vue` —— 面板视图,挂在 `App.vue` 全局。

数据流:`createLogger(domain).info(...)` → `emit()` 阈值门控 → 同时(a) console sink 打印、(b) `unshift` 进 `logBuffer` → `logStore.visibleLogs`(域过滤) → `DebugConsole.vue` 渲染。

历史 commit:`enhance/web-logging 分支 —— Web 日志系统统一重构(logger 核心 + 协议层迁移 + 51 文件噪音清扫,删除旧 debugStore)`
