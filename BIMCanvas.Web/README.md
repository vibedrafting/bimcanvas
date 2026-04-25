# BIMCanvas.Web 项目文档

**BIMCanvas.Web** 是 BIMCanvas 系统的前端可视化核心，致力于提供现代化的、基于 Web 的 3D 建筑空间展示与交互体验。项目采用 "Calm Tech" 设计理念，通过高对比度的暗色主题和极简的 UI 设计，让用户专注于空间设计本身。

## 🌟 项目概述

本项目是一个基于 **Vue 3** 和 **Three.js** 的单页应用 (SPA)，主要职责是加载、解析并渲染 BIMCanvas 的标准 JSON 数据格式 (`CanvasDocument`)。它不仅是一个查看器，更是未来 AI 辅助设计 (Copilot) 的交互界面。

## 🔌 运行模式与接口寻址

### Development

- 开发服务器默认由 Vite 提供：`http://localhost:5173`
- 若显式配置：
  - `VITE_SERVER_URL` → 指向 Server 基址
  - `VITE_AGENT_URL` → 指向 Agent 基址
- 若未显式配置，前端会回退到“当前主机 + `5000`”的开发态寻址策略
- 开发态下 Agent API 默认统一走 `Server /agent` 代理，不再要求前端固定直连 `8865`

### Production / Docker

- 生产静态资源由 Server 直接托管，页面基址默认与 Server 同源
- `VITE_SERVER_URL` 为空时，Server API 与 SignalR 默认走同源路径
- `VITE_AGENT_URL` 为空时，生产态默认走同源 `/agent`
- 这意味着阶段四服务器部署时，Web 不再假设 Vite dev server 存在

### 首页实例设置台

首页“实例设置”已经是 Docker 部署链路中的正式实例配置入口：

- 统一读取/编辑 `server/web/agent/ccr` 四组配置
- `web_config` 保存后可立即生效
- `config/server/ccr` 的改动会被标记为“需重启实例”
- 点击重启后，通过 `/api/settings/restart` 交给 Docker restart policy 接管重启

## ✨ 核心功能与开发状态 (Feature Status)

> 状态图例: ✅ 已完成 | 🔶 进行中 | ⬜ 待开发

### 1. 视图与渲染 (View & Rendering)
- ✅ **3D 渲染引擎**: 基于 Three.js 实现高性能建筑模型渲染，支持门窗颜色优化与材质升级。
- ✅ **双渲染模式 (Dual Render Mode)**:
    - **Human View (默认)**: 拟真材质、柔和光影 (AO)、极简信息，面向人类设计师。
    - **Agent View (AI)**: 开启所有辅助图层（网格、标签、包围盒），并叠加 **AI Vision Layer**。
- ✅ **AI 视觉层 (AI Vision Layer)**:
    - 专为 Agent 识别优化的高对比度语义图层。
    - 采用 "Elegant Tech" 配色方案，通过明度与色相差异清晰区分构件。
- ✅ **CAD 图层管理器 (Layer Manager)**:
    - FloatingLayerManager + LayerManager 已完成，支持 11 个图层独立控制。
    - 支持 Grid、Labels、Bounds、Outline、SVG、Zones、Semantic、AI Vision、Architecture、Furniture 等图层。

### 2. 交互与编辑 (Interaction & Editing)
- ✅ **基础导航**: 平移 (Pan)、缩放 (Zoom)、旋转视图 (Orbit)。
- ✅ **对象选择**: 支持点击选择场景中的构件，显示高亮包围盒。
- ✅ **移动 (Move)**: 支持对象拖拽移动，集成幽灵显示 (Ghosting) 预览。
- ✅ **旋转 (Rotate)**: 支持对象旋转，集成角度吸附与幽灵预览。
- 🔶 **镜像 (Mirror)**: 逻辑已实现 (`MirrorTool`)，UI 按钮待集成。
- ✅ **幽灵系统 (Ghost System)**: 移动/旋转操作时显示半透明预览，操作结束后自动清除。
    - **技术要点**: 使用 `LineLoop` 从 bounds 生成本地坐标轮廓，而非 `BoxHelper`。详见下方"开发经验"章节。
- ✅ **模块库与放置系统 (Module Library & Placement)**:
    - **模块库面板**: Ribbon 工具栏 Library > Local 按钮触发，PropertyPanel 风格浮动窗口，支持标签筛选、SVG 缩略图预览、拖拽移动和自由调整大小。
    - **放置工具 (PlaceTool)**: 选择模块后鼠标跟随 LineLoop 矩形轮廓 + 朝向箭头预览，点击放置，支持连续放置（Revit 风格），R 键顺时针旋转 90°，Esc 退出。
    - **Ghost 保留机制**: PlaceTool 预览标记 `userData.isGhost`，SceneBuilder.clearScene() 跳过 Ghost 对象，确保场景重建不会清除放置预览。
    - **快捷键隔离**: 工具激活时自动禁用 ShortcutManager，避免全局快捷键与工具内部按键冲突。
- 🔶 **语义吸附 (Semantic Snapping)**: SnappingEngine + SnapConfig + SnapIndex2D 已实现基础框架，集成调优中。

### 3. 数据与协作 (Data & Sync)
- 🔶 **AI 实时同步**: SignalR 基础连接已实现（事件监听 + 重连机制），集成收尾中。
- ✅ **撤销/重做 (Undo/Redo)**: TimelineManager 已完成（快照、历史策略、变更来源检测）。
- ✅ **首页实例设置台 (Homepage Instance Settings)**:
    - 首页右上角新增“实例设置”入口，离开首页后入口自动消失。
    - 已作为实例内部应用配置的正式入口，统一读取/编辑 `server/web/agent/ccr` 四组配置。
    - `web_config` 通过事件热更新即时生效，需重启配置通过 `/api/settings/restart` 触发实例重启。
- ⬜ **补丁审查 (Patch Review)**: 可视化审查 AI 提出的修改建议 (Diff)。

### 4. 调试与辅助 (Debug & Tools)
- ✅ **调试控制台 (Debug Console)**: 
    - 悬浮式调试面板，支持 `Ctrl + \`` 快捷键唤起。
    - 实时显示错误日志与执行状态，不遮挡主界面。

### 5. 界面与体验 (UI & UX)
- ✅ **灵动岛 (Dynamic Island)**:
    - 顶部居中悬浮工具栏，支持折叠/展开交互。
    - **状态反馈**: 实时显示 Agent 连接状态 (红/绿/黄点) 和当前操作 (Moving/Rotating/Selecting)。
    - **物理动效**: 采用 Apple 风格的 Spring 弹簧物理动画，交互流畅自然。
- ✅ **主题系统 (Theme System)**:
    - 支持 **明亮 (Light)** / **暗色 (Dark)** 模式一键切换。
    - **Premium Glass Aesthetic**: 
        - **Dark Mode**: "Aurora" 极光风格，深邃背景 + 高通透毛玻璃。
        - **Light Mode**: "Curved Glass" 曲面玻璃风格，纯净白底 + 锐利边框 + 强反光。
    - 基于 CSS Variables 实现，自动适配 3D 渲染背景、网格及 UI 控件颜色。

### 6. AI 指挥中心 (AI Command Center)

> **核心定义**：这是一个**“并行设计团队的指挥塔”**，而非简单的聊天窗口。它负责将 AI 的隐形工作（分支、策略、验证）可视化，并赋予用户精细的决策权。

#### 6.1 界面架构 (UI Architecture)

界面采用 **“三层汉堡”** 结构：

1.  **上下文顶栏 (Context Header)**:
    *   显示当前工作区域 (Scope) 和数据分支 (Branch)。
    *   支持对话/评审模式切换。
2.  **智能流 (Intelligence Stream)**:
    *   **任务卡 (Task Card)**: 可视化后台并行任务进度。
    *   **提案卡 (Proposal Card)**: **轮播图**形式展示多个平行方案，支持悬停预览 (Ghost Overlay)。
    *   **警报卡 (Alert Card)**: 冲突检测与自动修复建议。
3.  **指令底栏 (Command Footer)**:
    *   **上下文状态栏**: 显示 AI 当前关注的对象 (Selection) 和范围。
    *   **策略开关**: 切换 创意 (Creative) / 严格 (Strict) 模式。

#### 6.2 接入指南 (Integration Guide)

要接通真实 AI (Anthropic Agent)，需要完成以下对接：

1.  **后端架构**:
    *   **BIMCanvas.Agent (Python)**: 运行 Agent SDK，负责推理与工具调用。
    *   **BIMCanvas.Server (.NET)**: 提供 SSE (Server-Sent Events) 推送流和 REST API 指令接口。

2.  **前端改造**:
    *   **移除 Mock 数据**: 替换 `AICommandCenter.vue` 中的静态数据。
    *   **SSE 监听**: 连接 `/api/events/stream` 接收 `task_progress`, `new_proposal`, `alert` 事件。
    *   **状态同步**: 
        *   **指令发送**: 必须携带当前 `Context` (Zone ID, Selection IDs)。
        *   **预览同步**: 悬停提案卡时，需调用 `CanvasStore` 加载临时 JSON 数据以实现预览。
    
3.  **注意事项**:
    *   **异步体验**: AI 生成耗时较长 (10-30s)，必须利用任务卡进度条安抚用户，支持后台运行。
    *   **流式渲染**: 推荐让 AI 分阶段推送数据（如先推墙体再推家具），提升感知速度。

#### 6.3 当前实现状态与代码结构 (Implementation Snapshot)

**已实现能力**:
- **多窗口隔离**: 基于 Worktree 的虚拟窗口，窗口间聊天、分支、滚动状态互不干扰。
- **SSE 流式对话**: `/api/chat/stream` 逐行推送，支持思考过程与分段输出。
    - 主消费协议已切换到 `eventType + payload` envelope；`turn.completed / turn.failed` 是对话终态，`[DONE]` 仅保留兼容兜底。
- **子任务可视化**: SubAgent/ToolCall 气泡模型 + Waiting 提示。
- **统一 InteractionChannel**:
    - Question / Screenshot 共用 `/api/interaction/events` SSE，并统一通过 `/api/interaction/{id}/submit|cancel` 完成交互。
    - Reload 时仅对“当前页面已存在的窗口”执行 `GET /api/interaction?windowId=...` 恢复；question bubble 按 interaction record 的 `windowId` 路由，不再猜测当前激活窗口。
- **刷新聊天历史恢复**:
    - 当前页面级窗口集合会通过浏览器 `sessionStorage` 恢复，范围仅限“同一页面刷新”，不扩展到项目级持久化聊天记录。
    - 聊天消息本体通过 Agent `GET /api/history?windowId=...` 的 session transcript 回放，能恢复用户消息、AI 消息、thinking/tool/subtask/AskUserQuestion 卡片，以及用户消息里的图片附件缩略图。
- **截图附件资源化**:
    - 上传/粘贴/框选截图统一先上传到 `BIMCanvas.Server`
    - 前端输入区持有 `pendingAttachments: ChatAttachmentRef[]`，不再持有 `base64[]`
    - 聊天发送只传 `clientMessageId + attachmentIds`，不再把整张图片塞进 `/api/chat/stream`
    - 失败或中止时恢复附件草稿，避免重新截图
- **后台截图渲染**:
    - `/screenshot-render` 支持 `viewport.id` 局部取景，按房间、计算房间分区、设计分区依次查找目标。
    - `viewport.bounds` 优先于 ID，并会作为局部截图范围裁剪输出。
    - 后台截图合成 PNG 时默认使用 `labelScale=1.8` 放大标签，普通 Web 画布标签字号不受影响。
- **模型/思考强度**: 模型列表、默认模型与思考强度统一来自 `/api/config`；`/api/web_config` 只负责图层预设等 Web 展示配置。
- **运行时能力降级**: `/api/config.capabilityMatrix` 会驱动 Thinking 开关、Tasks 视图等前端降级；当 `thinking` 或 `subtask_causality` 不受支持时，相关入口会自动隐藏或禁用。
- **接口基址兜底**: 未显式配置 `VITE_SERVER_URL` / `VITE_AGENT_URL` 时，开发态默认使用当前主机的 `5000`，并统一通过 `/agent` 代理访问 Agent；生产静态托管时，Server 与 Agent 默认统一收口到同源入口，其中 Agent 走 `/agent`。

**代码拆分**（核心文件）:
- `src/components/UI/AICommandCenter.vue`: 组装层，负责 UI 绑定与模块协作。
- `src/components/UI/HomeSettingsPanel.vue`: 首页实例设置台，统一管理四份实例级配置。
- `src/composables/aiCommandCenter/useWindowManager.ts`: 窗口/分支/Worktree 管理。
- `src/composables/aiCommandCenter/useChatStream.ts`: SSE 流处理与消息发送。
- `src/composables/aiCommandCenter/useAgentConfig.ts`: 模型与思考强度配置。
- `src/composables/aiCommandCenter/useChatScroll.ts`: 滚动与自动滚动策略。
- `src/composables/aiCommandCenter/usePanelUI.ts`: 面板尺寸、Tab 横向滚动、轮播滚动。
- `src/composables/aiCommandCenter/useScreenshot.ts`: 截图监听与附件资源化上传。
- `src/composables/aiCommandCenter/useContextMenu.ts`: Context/Attachment 菜单逻辑。
- `src/services/ChatAttachmentService.ts`: 对话附件上传 / 删除 / 提交接口。
- `src/services/SettingsService.ts`: 实例设置聚合 API 访问层。
- `src/constants/aiCommandCenter.ts`: WAITING_VERBS / thinkingLevels / contextOptions / proposalMocks。
- `src/types/aiCommandCenter.ts`: ChatWindow/ChatMessage/Proposal 等类型。
- `src/types/chatAttachment.ts`: `ChatAttachmentRef` / `sourceKind` / `status`。

#### 6.4 上下文绑定 (Context Binding)

> **核心目标**：让 AI 能"看到"用户在画布上选中了什么，对标 Claude Code 的 `<ide_selection>` / `<ide_opened_file>` 上下文注入模式。

##### 模块 ID 标识

`Module.id` 是模块的唯一标识，格式 `"m_" + 8位随机字母数字`（36^8 ≈ 2.8 万亿种组合）：

- **C# Core**：`Module.Id` + `[OnDeserialized]` 反序列化时自动补全空 id
- **TypeScript**：`Module.id: string`（必填）
- **生成方式**：Web 端由 `generateModuleId()` 生成，Agent 写入时由 Server 自动补全

##### 支持的对象类型

| 类型 | payload 键 | 字段 | 区域信息 | UI 显示标签 |
|------|-----------|------|---------|------------|
| 家具模块 | `modules` | `id`, `name`, `zoneId`, `zoneName` | 每模块携带所属区域 | moduleName |
| 区域标签 | `zones` | `id`, `name` | 仅直接选中时出现（作为"分区"类别） | name |
| 墙体 | `walls` | `id`, `elementId` | 无 | 墙体 #xxxx |
| 柱子 | `columns` | `id`, `elementId`, `isStructural` | 无 | 柱 #xxxx |
| 门 | `doors` | `id`, `elementId` | 无 | 门 #xxxx |
| 窗 | `windows` | `id`, `elementId` | 无 | 窗 #xxxx |
| 禁区 | `exclusions` | `id`, `name` | 无 | 禁区: name |

##### 数据流

```
前端选中任意对象（模块/墙体/门窗/区域等）
    ↓
useSelectionContext.buildContextPayload()
→ { modules: [{id, name, zoneId, zoneName}],
    zones: [{id, name}],              ← 仅直接选中的区域标签
    walls: [{id, elementId}], columns: [{id, elementId, isStructural}],
    doors: [{id, elementId}], windows: [{id, elementId}],
    exclusions: [{id, name}] }
    ↓
useChatStream → fetch('/api/chat/stream', { message, context })
    ↓
http_server.py → data.get("context") → agent.chat_stream(message, context=context)
    ↓
main_agent.py → _build_context_block(context) → 独立 content block
    ↓
Claude API messages.content: [
  { type: "text", text: "<canvas_context>...</canvas_context>" },  ← 上下文
  { type: "text", text: "用户消息" }                                ← 用户输入
]
```

##### 注入格式

```xml
<canvas_context>用户在设计画布上选中了以下对象：
模块：三人沙发（m_a7x2k9m1）、茶几（m_b3y5j8n2），所在区域：客厅活动区（rz_1）
门：门(id:d_3)
分区：主卧(id:rz_3)

以上上下文可能与当前请求相关，也可能无关。</canvas_context>
```

> 模块按所属区域分组显示（用"；"分隔不同区域），其他类型按类型平铺，直接选中的区域标签作为"分区"类别独立展示。

##### 边界情况

| 场景 | 行为 |
|------|------|
| 无选中 | 不注入 `<canvas_context>`，只发送用户消息 |
| 仅选区域标签 | 显示"分区：XXX"，无"模块"行 |
| 仅选模块 | 显示"模块：XXX，所在区域：YYY"，无"分区"行 |
| 跨区域多选模块 | 模块按区域分组，用"；"分隔不同区域 |
| 模块 + 区域标签混合选中 | 模块按各自区域分组，区域标签作为独立"分区"行 |
| 仅选中建筑元件（墙体/门窗/柱） | 上下文包含对应元件，按类型独立行显示 |
| 模块无 zoneId（新复制的模块） | 模块名后不带区域信息 |

##### 核心文件

| 文件 | 职责 |
|------|------|
| `src/composables/aiCommandCenter/useSelectionContext.ts` | 画布选择上下文（全部 7 种对象分离、scope 推断、payload 构建） |
| `src/utils/shortId.ts` | 模块 ID 生成器（`m_` + 8 位随机） |
| `src/composables/aiCommandCenter/useChatStream.ts` | 发送请求时注入 context |
| `BIMCanvas.Agent/src/agent/main_agent.py` | `_build_context_block()` 构建独立 content block |
| `BIMCanvas.Agent/src/server/http_server.py` | 提取 context 字段并转发 |

## 🛠️ 技术栈 (Tech Stack)

| 领域 | 技术选型 | 说明 |
|------|----------|------|
| **核心框架** | Vue 3 + TypeScript | 使用 Composition API 和 `<script setup>` 语法 |
| **构建工具** | Vite | 极速冷启动与热更新 (HMR) |
| **3D 引擎** | Three.js | 业界标准的 WebGL 库 |
| **状态管理** | Pinia | 轻量级、类型安全的状态管理 |
| **样式方案** | Vanilla CSS | 使用 CSS Variables 定义设计系统 (Design Tokens) |
| **通信协议** | HTTP Streaming (SSE) + SignalR (已实现) | Agent 流式输出 + Server 实时双向同步 |

## 🚀 快速开始 (Getting Started)

### 环境要求
- Node.js 16+
- npm 或 yarn/pnpm

### 安装与运行

1.  **安装依赖**
    ```bash
    npm install
    ```

2.  **启动开发服务器**
    ```bash
    npm run dev
    ```
    启动后访问：`http://localhost:5173`

3.  **构建生产版本**
    ```bash
    npm run build
    ```

### 环境变量

- `.env.development`
  - `VITE_SERVER_URL=`（留空时回退到当前主机的 `5000`）
  - `VITE_AGENT_URL=`（留空时统一使用 `${VITE_SERVER_URL}/agent` 或同源 `/agent`）
- `.env.production`
  - 两者可留空，表示由生产环境同源入口或外层反向代理决定

## 📂 项目结构 (Project Structure)

```
src/
├── components/         # Vue UI 组件
│   └── (UI 覆盖层、工具栏等)
├── composables/        # 组合式逻辑
│   └── aiCommandCenter/ # AI Command Center 模块化逻辑
├── constants/          # 项目常量
│   └── aiCommandCenter.ts # AI 指挥中心常量
├── services/           # 核心业务逻辑服务
│   ├── builders/       # 3D 场景构建器
│   │   ├── SceneBuilder.ts      # 负责解析 JSON 并生成 Three.js Mesh（含 Ghost 保留逻辑）
│   │   ├── SVGModuleRenderer.ts # SVG → Three.js Group 渲染器
│   │   ├── ExclusionBuilder.ts  # 禁区构建器
│   │   ├── GridBuilder.ts       # 网格构建器
│   │   ├── LabelBuilder.ts      # 标签构建器
│   │   ├── OutlineBuilder.ts    # 轮廓构建器
│   │   └── ZoneBuilder.ts       # 区域构建器
│   ├── interaction/    # 交互工具层
│   │   ├── InteractionService.ts # 交互总线（事件分发、快捷键管理、工具生命周期）
│   │   ├── ShortcutManager.ts    # 全局快捷键管理（支持组合键和序列键）
│   │   ├── GhostManager.ts       # 幽灵预览管理器
│   │   ├── ViewportService.ts    # 视口计算和管理
│   │   ├── snap/                 # 语义吸附子系统
│   │   │   ├── SnappingEngine.ts    # 吸附引擎
│   │   │   ├── SnapConfig.ts        # 吸附配置
│   │   │   └── SnapIndex2D.ts       # 空间索引
│   │   └── tools/                # 工具实现
│   │       ├── PlaceTool.ts      # 模块放置工具（连续放置 + Ghost 预览）
│   │       ├── MoveTool.ts       # 移动工具
│   │       ├── RotateTool.ts     # 旋转工具
│   │       ├── CopyTool.ts       # 复制工具
│   │       ├── MirrorTool.ts     # 镜像工具
│   │       └── MeasurementTool.ts# 测量工具
│   ├── validation/     # 约束验证
│   │   └── ConstraintService.ts  # 布置约束验证
│   ├── state/          # 状态管理
│   │   └── TimelineManager.ts    # Undo/Redo 时间线管理
│   ├── screenshot/     # 截图服务
│   │   └── LabelRenderer.ts     # 截图标签渲染
│   ├── theme/          # 主题系统
│   │   └── ThemeService.ts      # 明/暗主题切换
│   ├── canvas/         # 画布样式
│   │   └── CanvasStyleService.ts# 画布样式管理
│   ├── ModuleLibraryService.ts   # 模块库数据服务（加载 JSON、缓存、标签索引）
│   ├── SignalRService.ts         # SignalR 实时通信（事件监听 + 重连）
│   ├── GitService.ts             # Git 操作
│   ├── GitWorktreeService.ts     # Worktree 管理
│   ├── ProjectService.ts         # 项目加载
│   ├── SchemeService.ts          # 方案管理
│   ├── ScreenshotService.ts      # 截图服务
│   └── three/          # Three.js 集成层
│       ├── ThreeSceneService.ts  # 场景、相机、渲染器、光照的生命周期管理
│       └── LayerManager.ts       # 图层管理器（11 个图层控制）
├── stores/             # Pinia 状态仓库
│   ├── canvasStore.ts  # CanvasDocument 数据流和加载状态
│   ├── gitStore.ts     # Git 分支/Worktree 状态
│   ├── windowStore.ts  # 多窗口管理状态
│   ├── mergeStore.ts   # 合并流程状态
│   └── debugStore.ts   # 调试面板状态
├── types/              # TypeScript 类型定义
│   ├── canvas.ts       # 核心数据模型 (Wall, Column, Opening, etc.)
│   └── aiCommandCenter.ts # AI 指挥中心类型
├── App.vue             # 应用入口组件 (负责挂载 3D 画布和 UI)
└── main.ts             # 应用初始化
```

## 🎨 设计规范 (Design Philosophy)

### 坐标系统 (Coordinate System)
- **Y-Up**: 遵循 Three.js 标准，Y 轴垂直向上。
- **单位**: 毫米 (mm)。
- **数据映射**: JSON 中的 `[x, y]` 坐标直接映射到 3D 场景的 `x, y` 平面，高度由 `z` 轴（挤压深度）控制，或通过旋转使平面躺在 XZ 面上（当前实现为 XY 平面直立模式，相机 Z 轴朝向）。

### 视觉风格与主题 (Visual Style & Themes)
项目内置了强大的主题系统 (`ThemeService`)，支持 **明亮 (Light)** / **暗色 (Dark)** 模式一键切换。

#### AI 视觉配色 (AI Vision Scheme - Elegant Tech)
专为计算机视觉设计的语义化配色方案：

| 构件 | 颜色 | Hex | 说明 |
| :--- | :--- | :--- | :--- |
| **家具模块** | 暖金 (Warm Gold) | `#FFB74D` | **视觉焦点**，柔和高亮 |
| **墙体** | 深蓝灰 (Blue Grey 800) | `#37474F` | 深沉背景结构 |
| **柱子** | 亮蓝灰 (Blue Grey 300) | `#90A4AE` | 高明度，与墙体形成对比 |
| **门** | 春绿 (Spring Green) | `#00E676` | 清新高亮，代表通行/安全 |
| **窗** | 天蓝 (Blue 300) | `#64B5F6` | 标准玻璃语义 |

### 配色系统原则 (Color System Principles)

遵循以下三大核心原则，确保视觉清晰度与一致性：

1.  **不同图层颜色不同**：
    *   **Grid (网格)**：灰色系（辅助层）。
    *   **Components (构件)**：绿色系（核心层）。
    *   **Labels (标签)**：黑/白单色（信息层，极致对比）。

2.  **统一图层颜色统一**：
    *   同一模式下，所有同类元素（如所有标签）必须使用完全一致的颜色，方便 AI 视觉识别。

3.  **明亮主题单独设计**：
    *   不搞简单的颜色反转，而是针对白色背景重新设计高对比度配色。

#### 最终配色方案

| 图层 | 亮色模式 (Light) | 暗色模式 (Dark) | 样式特征 |
| :--- | :--- | :--- | :--- |
| **Grid** | 灰色 (`#6b7280`) | 灰色 (`#6b7280`) | 低调辅助，无干扰 |
| **Components** | 绿色 (`#34c759`) | 绿色 (`#34c759`) | 鲜明轮廓，核心主体 |
| **AI Vision** | **Elegant Tech** | **Elegant Tech** | 高对比度语义填充 (Overlay) |
| **Labels** | **纯黑** (`#000000`) | **纯白** (`#ffffff`) | **极简风格**，无背景，极细反色描边 (1px) |

> **注**：标签层移除了所有发光效果和胶囊背景，采用工程制图标准的“黑白文字 + 描边”方案，以实现最佳的可读性和通透感。

## 📊 数据模型摘要

前端核心依赖 `CanvasDocument` 接口渲染：

```typescript
interface CanvasDocument {
  walls: Wall[];       // 墙体 (Polygon2D)
  columns: Column[];   // 柱子 (Polygon2D)
  openings: Opening[]; // 门窗 (Line2D + Type)
  modules: Module[];   // 家具组合 (Polygon2D + Items)
  // ... 其他字段
}
```

其中 `Module.facing` 已统一为：

```typescript
type FacingData = {
  value: [number, number] | null
  semantic: 'north' | 'south' | 'east' | 'west' | 'northeast' | 'northwest' | 'southeast' | 'southwest' | null
}
```

- `bounds` 仍是模块轮廓真理，`facing.value` 是方向真理
- Web 渲染、箭头、旋转、镜像、保存只读取和写回 `facing.value`
- Web 不会根据 `semantic` 实时推导方向
- 如果读取到 `facing.value = null` 或非法值，前端仅在渲染时临时 fallback 为北向 `[0, 1]` 并记录 warning，不会改写文件

## 🧠 开发经验 (Lessons Learned)

### Ghost 预览系统：为何不用 BoxHelper

**问题现象**：旋转预览时，Ghost 轮廓位置偏移，不与原模块重合。移动预览正常。

**根因分析**：

```
Three.js BoxHelper 特性：
├── 顶点使用世界坐标存储
├── matrixAutoUpdate = false（源码设计）
└── 只有调用 update() 才会重新计算包围盒

setPivot(pivot) 的变换逻辑：
├── ghostGroup.position = pivot
├── clone.position = -pivot  → 世界位置 = pivot + (-pivot) + 顶点 = 原位 ✓
└── BoxHelper.position = 0   → 世界位置 = pivot + 0 + 顶点 = 偏移！ ✗
```

**为什么移动正确、旋转错误**：
- **移动**: `W → W + delta`，BoxHelper 跟随父级平移即可实现
- **旋转**: `W → pivot + R*(W - pivot)`，需要先做 `W - pivot` 抵消，BoxHelper 缺少这一步

**解决方案**：用 `LineLoop` 替代 `BoxHelper`

```typescript
// ✗ 错误：BoxHelper 使用世界坐标
const boxHelper = new THREE.BoxHelper(clone, color);

// ✓ 正确：LineLoop 使用本地坐标，跟随父级变换链
private createOutlineFromBounds(bounds: [number, number][]): THREE.LineLoop {
    const points = bounds.map(([x, y]) => new THREE.Vector3(x, y, 0));
    const geometry = new THREE.BufferGeometry().setFromPoints(points);
    const outline = new THREE.LineLoop(geometry, material);
    outline.rotation.x = -Math.PI / 2;  // XY → XZ 翻转
    return outline;
}
```

**经验总结**：

| 场景 | 推荐方案 | 原因 |
|------|----------|------|
| 静态包围盒显示 | `BoxHelper` | 简单快速，自动计算 AABB |
| 需要跟随变换的轮廓 | `LineLoop` + 本地坐标 | 正确响应父级 position/rotation |
| 旋转时保持形状 | 避免 `BoxHelper.update()` | update() 会重算 AABB 导致变形 |

> 相关文件: `src/services/interaction/GhostManager.ts`

### 角度语义系统 ⚠️ 重要

项目中存在三套角度系统，混用会导致方向相反。规范定义见 [Architecture.md §1.5](../docs/Architecture.md#角度语义规范)。

**速查**：交互角（`atan2(z,x)`）是 CW+，数据模型角是 CCW+，**传入 `rotatePoint2D()` 前必须取反**。

#### 旋转数据流

```
用户顺时针拖动 → atan2(z,x) = +π/2 (CW+)
                     ↓
    ┌────────────────┴────────────────┐
    ↓                                 ↓
Ghost 预览                        数据更新
rotation.y = -π/2              delta = -π/2
Three.js 顺时针 ✓              rotatePoint2D 顺时针 ✓
```

#### 历史教训

| 提交 | 问题 | 后果 |
|------|------|------|
| `88faf08` | 移除取反，注释写"与 Ghost 保持一致" | 预览对、结果反 |
| `d5f80d7` | 恢复取反 | ✓ 修复 |

> 相关文件: `RotateTool.ts`, `GhostManager.ts`, `coordinates.ts`
> 完整分析: `reports/BUG_RotateDirection/`

### 模块库与放置系统 (Module Library & Placement)

**系统概述**：类 Revit 族库功能，从 Server 加载模块定义（JSON + SVG），通过浮动面板浏览，点击后进入连续放置模式。

**数据流**：

```
Server 模块库 API
├── GET /api/modules/library → module_library.json（21 个模块定义）
└── GET /api/modules/svg/{id} → SVG 缩略图文件

用户操作流：
Ribbon [Local] → CustomEvent('bimcanvas:open-module-library')
→ MainLayout 切换 ModuleLibraryPanel 可见性
→ 用户点击模块卡片 → CustomEvent('bimcanvas:activate-place-tool')
→ ThreeSceneService → InteractionService.activatePlaceTool()
→ PlaceTool 创建 LineLoop 预览 → 鼠标跟随
→ 点击放置 → store.addModule() → endBatchUpdate() → 持久化
→ 预览保持（Ghost 保留机制）→ 继续放置...
→ Esc 退出 → cancelTool() → 恢复快捷键
```

**核心代码位置**：

| 文件 | 职责 |
|------|------|
| `src/services/ModuleLibraryService.ts` | 模块库数据加载、缓存、标签索引、SVG URL 生成 |
| `src/services/interaction/tools/PlaceTool.ts` | 放置工具（预览创建、鼠标跟随、旋转、连续放置） |
| `src/components/UI/ModuleLibraryPanel.vue` | 浮动面板 UI（PropertyPanel 风格、标签筛选、拖拽/缩放） |
| `src/components/UI/Ribbon/LibraryGroup.vue` | Ribbon 工具栏 Library 分组（Local/Cloud 按钮） |
| `src/layouts/MainLayout.vue` | 面板挂载点 + 事件桥接（面板 ↔ 放置工具） |

**关键设计决策**：

1. **Ghost 保留机制**：PlaceTool 创建的预览对象标记 `userData.isGhost = true`，SceneBuilder.clearScene() 通过 `isGhostObject()` 方法检查祖先链，跳过所有 Ghost 对象。这解决了 `addModule()` 触发 deep watcher → 场景重建 → 预览被意外清除的问题。

2. **快捷键隔离**：工具激活时调用 `shortcutManager.setEnabled(false)` 禁用全局快捷键（R/M/C/Delete 等），避免与工具内部按键（如 PlaceTool 的 R 旋转）冲突。工具退出时自动恢复。

3. **事件通信**：面板与 Three.js 服务之间通过 `window.dispatchEvent(CustomEvent)` 桥接，遵循现有项目的解耦模式。

### 文件驱动持久化 ⚠️ 核心架构

**问题现象**：移动家具模块后刷新页面，家具回到原位。

**根因分析**：

项目采用"文件驱动架构"（File-Driven Architecture），要求 Web 端的修改**必须立即写入文件系统**。但编辑操作完成后，`endBatchUpdate()` 只保存到内存中的 Timeline（支持 Undo/Redo），**没有调用 `saveToServer()` 持久化到磁盘**。

```
修复前（断裂）:
用户移动 → updateModule() → 内存更新 ✓ → saveState() → [停止]
                                                         ↑
                                              缺少 saveToServer()
刷新页面 → Server 读取磁盘上的旧数据 → 家具回到原位

修复后（完整）:
用户移动 → updateModule() → 内存更新 ✓ → saveState() → saveToServer()
                                                            ↓
                                         Server 写入 modules.json ✓
刷新页面 → Server 读取磁盘上的新数据 → 家具位置正确
```

**修复方案**：

```typescript
// src/stores/canvasStore.ts - endBatchUpdate()
const endBatchUpdate = async () => {
    batchUpdateMode.value = false;

    // 1. 保存到本地Timeline历史（Undo/Redo）
    await nextTick();
    saveState();

    // 2. 持久化到文件系统（File-Driven Architecture）
    if (isDirty.value) {
        await saveToServer();  // ← 关键：必须调用！
    }
};
```

**架构原则**（摘自 `docs/FileDrivenArchitecture.md`）：

> **场景B：可视化设计** - 用户在 Web 端拖拽 → Server 验证通过后**直接覆写**硬盘上的 JSON → 文件系统发生物理变更

**开发检查清单**：

| 新增编辑操作时 | 检查项 |
|---------------|--------|
| ✅ 内存更新 | 调用 `store.updateModule()` |
| ✅ 脏标记 | `isDirty.value = true`（updateModule 自动设置）|
| ✅ 批量更新 | 使用 `beginBatchUpdate()` / `endBatchUpdate()` 包裹 |
| ✅ 持久化 | `endBatchUpdate()` 中自动调用 `saveToServer()` |

> 相关文件: `src/stores/canvasStore.ts`, `src/services/interaction/tools/*.ts`
> 架构文档: `docs/FileDrivenArchitecture.md`

---
*文档最后更新时间: 2026-03-05*
