# Arch_Web — 前端架构

> **本文用途**：BIMCanvas Web 端的架构与设计原则。两类读者——① 想改前端核心的**贡献者**；② 想为自己插件做 Web 的**插件开发者**（见 §4）。
>
> **状态**：2026-06 当前态。坐标/角度契约本体见 [Arch_Spatial.md](./Arch_Spatial.md)；平台/插件机制见 [Arch_Plugin.md](./Arch_Plugin.md)；前端工程导览（启动 / 目录 / 改 X 去哪）见 `BIMCanvas.Web/README.md`。本文讲"架构怎么组织、为什么这么设计"，琐碎实现细节留给代码与模块 README。

---

## 1. 前端在架构中的角色

BIMCanvas 是文件驱动的（`.bcp` 文件是唯一真理源，见 [Architecture.md](./Architecture.md)）。**前端不是真理源的持有者，是真理源的投影**：Server 读取-聚合磁盘文件 → REST 发给前端 → 落进单一状态 → Three.js 场景重建。任何"别处"对文件的改动（Agent / 另一窗口 / Git 切分支）经实时通道回灌前端。

前端有**双重身份**(README 的"皮肤 + 眼睛")：既是给人用的 2.5D 协作画布，也是 **AI 的眼睛**——它为每个构件额外渲染一套高对比纯色的"AI 视觉层"，供 Agent 截图识别。这个双重定位解释了后面很多设计(双 mesh、`preserveDrawingBuffer`、AI/User 两套图层预设)。

技术栈：Vue 3 + Vite + Pinia + Three.js。

## 2. 渲染架构

### 2.1 单正交俯视场景 + 图层位掩码

整个画布是**一个 Three.js 正交俯视场景**(`OrthographicCamera`,正上方垂直向下,非斜视),由 `services/three/ThreeSceneService.ts` 统一编排。"2.5D"指:正交俯视 + 墙/柱/家具被拉伸成有高度的体块,俯视下靠厚度和阴影产生立体感,而非真三维斜视。室内布置是 2D 平面设计,正交保证尺寸/位置无透视畸变(CAD 标准),人工对齐和 AI 识图都需要"所见即真实比例"。

**分层用 Three.js `camera.layers` 位掩码**(不是独立 scene、不是 z-index),权威清单在 `services/three/LayerManager.ts`:模型 / 网格 / 标签 / OBB 描边 / 边界描边 / SVG 预览 / Zone / AI 视觉 / 墙柱 / 家具等约 11 层。两套预设:**User 模式**只开干净的人看视图(模型+网格+建筑+家具),**Agent 模式**开全部含 AI 视觉层(高信息密度叠加,给 AI 截图)。

> 遮挡是三套机制并存(layers 决定显隐 / renderOrder+depthTest 决定线与 SVG 叠放 / y 高度决定半透明面叠放),散在各 builder——这是踩坑固化的现状,新增图层时要留意,没有统一的 z 编排表。

### 2.2 SVG + WebGL 混合渲染(为什么)

三种渲染技术各司其职,这是核心设计决策:

- **WebGL mesh(几何体块)**:墙/柱/门窗/家具/Zone 用 `ExtrudeGeometry` 拉成真实体块——撑起 2.5D 空间感。
- **SVG mesh(语义图标)**:家具的"这是床/沙发"的符号识别,用 `SVGLoader` 把 SVG 拍平成 mesh 贴在体块上。**关键:用 SVGLoader 转 mesh 而非 DOM `<svg>`**,是为了和 WebGL 同场景、同相机、共享 layers/renderOrder 遮挡体系——纯 DOM SVG 无法和 3D 体块正确互相遮挡、无法跟随正交相机缩放。
- **CSS2D DOM(文字)**:房间名/构件 ID/Zone 标签走 `CSS2DRenderer`,DOM 锚在 3D 点上但用屏幕像素渲染。文字需要 CSS 排版(竖排、点击命中)且永远清晰不变形,这是"WebGL 渲几何、DOM 渲文字"的经典分工。

SVG 构件按朝向旋转用**父子 Group 方案**(子 Group 在 XY 平面内做 2D 朝向旋转、父 Group 做压平)——规避 Three.js Euler 本地轴旋转把 SVG 掀成垂直面、文字倒置的坑。

### 2.3 双 mesh:前端是 AI 的眼睛

每个构件**同时建两套 mesh**:普通材质(给人看,有光照)+ AI 视觉材质(纯色无光照)。Agent 模式切到 AI 视觉层 → 高对比度纯色分割图,专为 AI 视觉识别优化。这是"前端要自己渲一套"的核心动机——它不只是查看器,还是 Agent 的视觉输入源(`canvas_vision` 截图链路、`preserveDrawingBuffer` 都为此服务)。

### 2.4 坐标与角度

坐标契约本体见 [Arch_Spatial.md](./Arch_Spatial.md);前端侧的落地真理源是 `utils/coordinates.ts`(红线:禁止业务代码手动 ×-1)。要点:数据模型 Y-up → Three.js 映射 `Data(x,y) → View(x, 0, -y)`(Y 轴变 -Z);三套角度系统(数据模型角 CCW+ / 交互角 CW+ / Three.js 角)的转换**统一收在工具层**,鼠标交互算出的角(CW+)喂给旋转函数前取反成模型角。所有 builder 经 `toWorld` 等转换,无散落的手动翻转。

## 3. 状态与通信架构

### 3.1 单一真理源 + watch 重建

前端状态是 8 个 Pinia store,但**画布数据的唯一真理源是 `canvasStore` 的一个 `projectData` ref**(含 baseline / 当前方案 modules / zones / computed)。Three.js 场景 `watch` 这个 ref,变更即重建。放弃前端持局部状态、整个 `ProjectData` 一个 ref——符合"文件是真理源、前端是投影"。代价是粗粒度(Agent 改动走整 reload 而非细粒度 patch;变体计数是唯一轻量例外)。

其余 store 各管一域:`appStore`(视图/项目列表)、`systemStore`(通知中枢:toast/重启/限流)、`pluginStore`、`gitStore`、`mergeStore`、`windowStore`、`debugStore`。

### 3.2 文件驱动闭环

这是前端最关键的链路,正反向各一条:

- **正向(文件→渲染)**:`GET /api/project` 一次性取聚合 `ProjectData` → 写 `projectData.value` → Three.js watch 重建。
- **反向(别处改文件→前端重渲染)**:Server `ProjectWatcherService`(`FileSystemWatcher` 监听 `schemes/` 子树,500ms 防抖)→ SignalR 广播 `ReceiveUpdate` → 前端 `SignalRService` 转成 DOM CustomEvent → `canvasStore` 监听 → `syncFromServer` 重拉 REST(保留历史+保持视图)。

SignalR → DOM CustomEvent 这层解耦是有意的:服务层不依赖 Pinia,换通信实现不动 store。

### 3.3 三条通信通道

| 通道 | 承载 | 说明 |
|------|------|------|
| **REST** | 状态拉取 / 落盘 | `GET /api/project`、modules/plugins/git 等 |
| **SignalR**(`/hubs/canvas`) | 文件变更 / Git 状态 / Agent 通知广播 | 直连 Server |
| **SSE**(`/agent/api/interaction/events`) | Agent 交互带外:问答、后台任务/workflow 心跳 | 经 `/agent` 代理 |

**Agent 请求统一经 Server `/agent` 代理**(`config/api.ts` 的 `AGENT_API`),不直连 Agent 端口——架构红线。SignalR(文件/Git,直连 Server)与 SSE(Agent 交互,经代理)是两套独立通道。AI 对话/workflow 进度/后台任务由 `useChatStream` / `useWorkflowProgress` / `useBackgroundTask` 三个 composable 消费流式事件。

### 3.4 两种运行时模式

前端最重要的架构分叉,在 `runtime/`:

- **Connected**:有 Server。真理源在磁盘,前端投影+经 REST 落盘,实时同步/AI 对话/Git 全可用。
- **Standalone**:无 Server,纯浏览器内存 + 快照文件。落盘退化为"导出快照",无实时同步/AI/Git。

差异用**能力表门控**(`capabilities.ts` 的声明式表 + `supports()` 守卫),不是 `if(mode)` 散布——文件同步监听、本地更新回送等整段只在 Connected 挂载。新增能力只改表 + 加守卫。

### 3.5 Undo/Redo:策略驱动

`services/state/TimelineManager.ts` 按 **ChangeSource** 决定清栈/保栈:换项目类来源(上传/新建/Git init)清空历史,Agent 改动/Server 同步/用户编辑追加快照。即时写盘(符合文件驱动),批量操作由 `endBatchUpdate` 收口一次快照。本地写入触发的 SignalR 回弹用计数器跳过,避免误清 redo 栈。

## 4. 插件开发者在 Web 端能做什么 ★

**一句话现状**:Web 的插件可扩展性是一套**数据 / 编排契约**(REST + SSE + 指针模型),对编排类 UI 真实且通用;**但不存在任何前端代码注入机制**——画布/编辑/类型核心仍硬编码于当前 domain。插件今天能驱动通用外壳,无法在不改平台 Web 代码的前提下定制视觉/编辑表面。

### 4.1 已落地的扩展点

| 扩展点 | 机制 | 插件怎么用 |
|--------|------|-----------|
| **通用编排外壳** | 指针模型 + 通用事件契约 | 插件产出 schemes/variants → `VariantNavigatorBar` 渲染候选、采纳按钮翻 `adopted` 指针(零复制);插件 workflow 发 `Workflow` 工具 + SSE phase/task 心跳 → 通用 Workflow/Task 面板渲染任意 phases/agents。**换 domain 仍成立** |
| **web_action(JSON RPC)** | 插件 `register(builder)` 内 `@builder.web_action` 装饰器注册;Web `fetch /api/plugin-actions/{ns}/{action}`(经 Server 代理) | 无状态 HTTP RPC(JSON in/out),handler 内可调外部 API / 读 `ctx.get_config()`。**不经 LLM、不能调 Agent 工具** |
| **插件设置页** | manifest `configSchema[]`(key/label/secret/required) | Web 自动生成配置表单(`PluginConfigDialog`),值存 instance config 的 `pluginConfigs.{id}`,运行时 `ctx.get_config()` 注入,改配置免重启 |
| **安装/信任 UI** | 平台基座通用 | 用户经对话框 install(github / 本地目录)→ trust 二次确认(RCE 警告)→ activate。所有插件共享 |
| **scenes 场景模板** | 插件根 `scenes/`(index.json + {id}/scene.bcp)→ `GET /api/scenes` 聚合 | Web「从场景新建项目」渲染并拷贝 scene.bcp 建项目 |

### 4.2 能力边界(今天做不到的)

- **不能注入自定义 Vue 组件 / 面板 / 画布渲染器**:Web 与插件只经 REST/SSE 数据交互,无前端代码下发路径(无 module federation / 动态 import 插件代码 / iframe 插件 UI)。
- **不能注册新 Web capability**:`capabilities.ts` 是固定枚举、按运行时模式选择,与 active plugin 无关,新增须改 TS 重编译。Web 端**没有** Agent 侧"五层投影"的对等扩展体系(manifest 旧的 `web.*` 字段族已在 v3.3.2 删除)。
- **domain 画布核心是硬编码**:RoomType/ZoneTag 枚举、家具图层、Move/Rotate 等编辑工具的"只有家具可…"语义、AI 对话的"家具/设计区/通道/禁区"提示——都是当前 interior-layout domain 的假设,换 domain 会按家具/房间假设渲染。(注:Web 代码本身用 `pluginId` 泛化引用,不含 `"interior-layout"` 字面量,但类型与画布语义是 domain 绑定的。)

### 4.3 设计预留 / 当前缺口

以下机制管线已通、但内容未填或未接线,插件开发者需知道别白费力气:

- **web_action 的 UI 入口是逐个硬编码**,无"自动发现 active plugin 暴露了哪些 action 并动态渲染按钮"的机制——加一个 web_action 目前仍需平台侧手写对应 Vue 入口 + capability 门控。
- **zip 安装源**:`SourceKind` 枚举含 Zip 但标注 Phase 2 占位,后端未实现、UI 无入口。
- **configSchema 的 `required`** 目前仅渲染星号,表单与激活流程**无强制必填校验**。
- **scenes / Envision 等**当前无插件实际提供内容(管线可跑,数据为空)。

### 4.4 给插件开发者的结论

今天你能做的,全部经后端中介、零前端代码:经 Web 对话框 install/trust/config;发 `Workflow` + SSE 心跳驱动通用进度可视化;产出 schemes/variants 由通用变体外壳 + 采纳指针管理;经 web_action 做 JSON RPC(但 UI 入口需平台预置)。**想要 domain 专属的画布渲染 / 编辑工具 / 自定义面板,目前必须改平台 Web 代码**——这是 Web 插件化相对 Agent 侧(五层投影成熟)的主要差距,也是未来方向。

## 5. 贡献者上手地图

| 想改 | 入口 |
|------|------|
| 渲染 / 加图层 / 加几何 | `ThreeSceneService`(装配)→ `LayerManager`(图层常量+预设)→ `services/builders/*`(仿现有 builder 双建普通+AI mesh)→ 坐标走 `utils/coordinates.ts` |
| 编辑交互 / 加工具 | `services/interaction/`,实现 `Tool` 接口、经 `bimcanvas:action-*` 事件激活、回写 `canvasStore` |
| 状态 / 同步 / Undo | `stores/canvasStore.ts`、`services/state/TimelineManager.ts` |
| 通信 | `config/api.ts`(base 解析)、`services/SignalRService.ts`、`InteractionChannelService.ts` |
| 运行时模式 / 能力 | `runtime/{capabilities,createWebRuntime,ConnectedRuntime,StandaloneRuntime}.ts` |
| AI 对话 / workflow 面板 | `composables/aiCommandCenter/{useChatStream,useWorkflowProgress,useBackgroundTask}.ts` |
| 启动 / 目录 / 工程纪律 | `BIMCanvas.Web/README.md`(工程导览) |
